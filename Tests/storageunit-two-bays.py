"""
In-game check of the Storage Unit's second loading bay (stage D): bay 1 stands moved, bay 2 stands beside it, two shops
deliver at the same time, both vans park without touching each other, the hall walls or Geraldine's door, and a handler
reaches the spot behind each van. With --after-reload the docks are checked again after save and reload.

Needs a Debug build of Based (`baseddiag`) in a running test slot and a save where the player owns the Storage Unit.

    python _wt-Based-storageunit/Tests/storageunit-two-bays.py [--slot mptest] [--after-reload]
"""
import argparse
import importlib.util
import os
import re
import sys
import time

CODE = "storageunit"
SITE = "site=" + CODE
PHASE = "ray.storageunit-bay:complete"
# Dock roots after stage D, and the hall a parked van must stay inside: west wall x -8.75, Unit 3 front x -2.2, the
# hall's north end z 103.9, and Geraldine's door, which ends at z 97.03 on the Unit 3 side.
DOCKS = {0: (-7.20, 102.35), 1: (-3.95, 102.35)}
HALL_X = (-8.75, -2.2)
HALL_NORTH = 103.9
DOOR_Z = 97.03
VEC = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def docks(log):
    found = {}
    for l in log:
        m = re.search(r"dockroute dock (\d+) guid (\S+) at " + VEC + r", van (.+?), output", l)
        if m:
            found[int(m.group(1))] = dict(guid=m.group(2), x=float(m.group(3)), z=float(m.group(5)),
                                         van=None if m.group(6) == "none" else m.group(6))
    for l in log:
        m = re.search(r"dockroute dock (\d+) van at " + VEC + " forward " + VEC + " size " + VEC, l)
        if m and int(m.group(1)) in found:
            g = [float(v) for v in m.groups()[1:]]
            found[int(m.group(1))]["box"] = footprint(g[0], g[2], g[3], g[5], g[6], g[8])
    return found


def footprint(x, z, fx, fz, width, length):
    """The van's ground rectangle (x0, x1, z0, z1); vans park along x or z, so the forward axis decides the length."""
    if abs(fz) >= abs(fx):
        return (x - width / 2, x + width / 2, z - length / 2, z + length / 2)
    return (x - length / 2, x + length / 2, z - width / 2, z + width / 2)


def overlap(a, b):
    return a[0] < b[1] and b[0] < a[1] and a[2] < b[3] and b[2] < a[3]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--after-reload", action="store_true")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-combo",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 0800", wait=1.0)
    mark = run.mark()
    log = run.cmd("baseddiag %s phase %s" % (SITE, PHASE), wait=10.0)
    run.check("bay complete", any("construction moved to" in l for l in log), pick(log, "baseddiag"))
    failed = next((l.strip()[-260:] for l in run.lines(mark) if "based_storage_bay' failed" in l or "'complete' failed" in l), "")
    run.check("state applied without rollback", not failed, failed or "no failure line")

    log = run.cmd("baseddiag %s dockroute" % SITE, wait=1.0)
    found = docks(log)
    for index, (x, z) in DOCKS.items():
        d = found.get(index)
        run.check("dock %d stands at x %.2f z %.2f" % (index, x, z), d is not None and abs(d["x"] - x) < .05 and abs(d["z"] - z) < .05,
                  pick(log, "dockroute dock %d guid" % index) or "dock %d missing" % index)
    guids = {i: d["guid"] for i, d in found.items()}

    log = run.cmd("baseddiag dock inspect %s" % CODE, wait=1.0)
    shops = [int(m.group(1)) for l in log for m in [re.search(r"dock shop index=(\d+) name='.*?' vehicle=True", l)] if m]
    if not run.check("two shops with a delivery vehicle", len(shops) >= 2, shops):
        return run.summary()
    run.cmd("addemployee handler %s" % CODE, wait=4.0)
    for dock, shop in zip((0, 1), shops):
        log = run.cmd("baseddiag %s dockroute deliver %d soil %d" % (SITE, shop, dock), wait=1.0)
        run.check("delivery from shop %d to dock %d sent" % (shop, dock), any("dockroute deliver" in l and "dock %d" % dock in l for l in log),
                  pick(log, "deliver"))

    deadline, found = time.time() + 180, {}
    while time.time() < deadline:
        log = run.cmd("baseddiag %s dockroute" % SITE, wait=0.5)
        found = docks(log)
        if all(found.get(i, {}).get("van") for i in DOCKS):
            break
        time.sleep(2)
    for i in DOCKS:
        run.check("a van parks at dock %d" % i, bool(found.get(i, {}).get("van")), found.get(i, {}).get("van") or "none within 180 s")
    boxes = {i: found[i]["box"] for i in DOCKS if "box" in found.get(i, {})}
    for i, b in boxes.items():
        detail = "x %.2f..%.2f z %.2f..%.2f" % b
        print("      dock %d van %s" % (i, detail), flush=True)
        run.check("dock %d van stays inside the hall walls" % i, HALL_X[0] <= b[0] and b[1] <= HALL_X[1] and b[3] <= HALL_NORTH, detail)
        run.check("dock %d van leaves Geraldine's door free" % i, b[2] > DOOR_Z, detail)
    if len(boxes) == 2:
        run.check("the two vans do not touch", not overlap(boxes[0], boxes[1]), "%s / %s" % (boxes[0], boxes[1]))
    behind = [l.strip()[-220:] for l in log if "behind the parked van" in l]
    for l in behind:
        print("      " + l, flush=True)
    run.check("a handler reaches the spot behind both vans", len(behind) == 2 and all("employee can get there: True" in l for l in behind), behind)

    if args.after_reload:
        ready, _ = run.reload(args.save) if hasattr(run, "reload") else (None, None)
        if ready is None:
            run.g.call("save_now", {}, timeout_ms=60000)
            time.sleep(3)
            run.g.call("return_to_menu", {}, timeout_ms=60000)
            time.sleep(8)
            for attempt in range(6):
                reply = run.g.call("load_save", {"slot": args.save}, timeout_ms=60000)
                result = (reply or {}).get("result") or {}
                if reply and reply.get("ok") and result.get("loading"):
                    break
                print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
                time.sleep(5)
            ready, _ = run.g.wait_ready(timeout=1200, interval=10, say=lambda s: None)
        if run.check("reload ready", ready):
            time.sleep(5)
            log = run.cmd("baseddiag %s dockroute" % SITE, wait=1.0)
            again = docks(log)
            for index, (x, z) in DOCKS.items():
                d = again.get(index)
                run.check("after reload: dock %d at x %.2f with the same GUID" % (index, x),
                          d is not None and abs(d["x"] - x) < .05 and d["guid"] == guids.get(index),
                          pick(log, "dockroute dock %d guid" % index) or "dock %d missing" % index)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
