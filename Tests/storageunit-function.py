"""
In-game check of the finished Storage Unit (every build up to the second loading bay) as a furnished base: racks and
drying racks on the three upper rooms, a botanist who carries weed from a rack upstairs onto a drying rack upstairs, a
handler who carries deliveries from both loading bays onto a rack upstairs, and all of it still in place after two
save and reload rounds.

Needs Debug builds of MapExpansions (`mxreach`) and Based (`baseddiag`) in a running test slot and a save where the
player owns the Storage Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-storageunit/Tests/storageunit-function.py [--slot mptest] [--save 5] [--reloads 2]
        [--mapexpansions _wt-MapExpansions-combo]
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
# The upper rooms' grids in build order: loft, east room, south room. The hall floor is grid 0.
# An item's position is its pivot, which can sit up to a metre above the floor it stands on (y 3.25 upstairs).
ROOMS = {1: "loft", 2: "east room", 3: "south room"}
FURNITURE = [("smallstoragerack", 1), ("smallstoragerack", 2), ("dryingrack", 2), ("smallstoragerack", 3), ("dryingrack", 3)]
DOCKS = {0: (-7.20, 102.35), 1: (-3.95, 102.35)}
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
        m = re.search(r"dockroute dock (\d+) guid (\S+) at " + VEC, l)
        if m:
            found[int(m.group(1))] = dict(guid=m.group(2), x=float(m.group(3)), z=float(m.group(5)))
    return found


def soil_on_racks(lines):
    for l in lines:
        m = re.search(r"dockroute work: \d+ racks hold .* free slots, soil (\d+)", l)
        if m:
            return int(m.group(1))
    return None


def wing_drying(lines):
    """Sum of input + drying + output over the drying racks on expansion tiles."""
    total = 0
    for l in lines:
        m = re.search(r"drying rack at .* on expansion tiles: input (\d+), drying (\d+), output (\d+)", l)
        if m:
            total += sum(int(g) for g in m.groups())
    return total


def upstairs(run):
    """Item ids that stand on expansion tiles, with whether vanilla counts each as reachable."""
    log = run.cmd("mxreach %s" % CODE, wait=3.0)
    items = []
    for l in log:
        m = re.search(r"mxreach:   (\S+) at " + VEC, l)
        if m and "on expansion tiles" in l and "vanilla reach" in l:
            items.append((m.group(1), float(m.group(3)), "vanilla reach yes" in l, l.strip()[-220:]))
    return items


def reload(run, save):
    run.g.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    run.g.call("return_to_menu", {}, timeout_ms=60000)
    time.sleep(8)
    for attempt in range(6):
        reply = run.g.call("load_save", {"slot": save}, timeout_ms=60000)
        result = (reply or {}).get("result") or {}
        if reply and reply.get("ok") and result.get("loading"):
            break
        print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
        time.sleep(5)
    ready, _ = run.g.wait_ready(timeout=1200, interval=10, say=lambda s: None)
    return ready


def check_base(run, pick, label, guids):
    log = run.cmd("baseddiag %s dockroute" % SITE, wait=1.0)
    found = docks(log)
    for index, (x, z) in DOCKS.items():
        d = found.get(index)
        ok = d is not None and abs(d["x"] - x) < .05 and abs(d["z"] - z) < .05 and (guids is None or d["guid"] == guids.get(index))
        run.check("%s: dock %d at x %.2f z %.2f%s" % (label, index, x, z, "" if guids is None else " with the same GUID"), ok,
                  pick(log, "dockroute dock %d guid" % index) or "dock %d missing" % index)
    items = upstairs(run)
    want = sorted(i for i, _ in FURNITURE)
    have = sorted(i[0] for i in items)
    run.check("%s: the furniture stands upstairs" % label, all(have.count(w) >= want.count(w) for w in set(want)), have)
    run.check("%s: everything upstairs sits on the upper floor" % label, all(3.2 < i[1] < 5.5 for i in items),
              [i[3] for i in items if not 3.2 < i[1] < 5.5])
    run.check("%s: everything upstairs is usable (vanilla reach)" % label, bool(items) and all(i[2] for i in items),
              [i[3] for i in items if not i[2]])
    log = run.cmd("baseddiag %s dockroute routes" % SITE, wait=1.0)
    run.check("%s: the handler's routes start at both bays" % label,
              any("from the loading dock to" in l for l in log) and any("from the loading dock 1 to" in l for l in log),
              [l.strip()[-160:] for l in log if "routes:" in l])
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--reloads", type=int, default=2)
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-combo",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 0800", wait=1.0)
    mark = run.mark()
    log = run.cmd("baseddiag %s phase %s" % (SITE, PHASE), wait=10.0)
    run.check("every build complete", any("construction moved to" in l for l in log), pick(log, "baseddiag"))
    failed = next((l.strip()[-260:] for l in run.lines(mark) if "' failed" in l and "based_storage" in l), "")
    run.check("states applied without rollback", not failed, failed or "no failure line")

    run.cmd("addemployee botanist %s" % CODE, wait=5.0)
    run.cmd("addemployee handler %s" % CODE, wait=4.0)
    log = run.cmd("mxreach place %s locker old" % CODE, wait=2.0)
    print("      " + (pick(log, "mxreach place") or "no reply to mxreach place locker"), flush=True)
    for item, grid in FURNITURE:
        log = run.cmd("mxreach place %s %s wing 0 %d" % (CODE, item, grid), wait=2.0)
        placed = next((l.strip()[-240:] for l in log if "mxreach place: built " + item in l and "of grid %d " % grid in l), "")
        run.check("built %s in the %s" % (item, ROOMS[grid]), bool(placed), placed or pick(log, "mxreach place"))

    log = run.cmd("mxreach setup %s wing" % CODE, wait=2.0)
    setup = pick(log, "mxreach setup:")
    run.check("the botanist's supplies are a rack upstairs", "on expansion tiles" in setup and "drying racks" in setup, setup)
    run.check("the handler has a locker and is paid", "locker none" not in pick(log, "setup: handler") and bool(pick(log, "setup: handler")),
              pick(log, "setup: handler"))
    log = run.cmd("baseddiag %s dockroute route" % SITE, wait=1.0)
    run.check("the handler gets a route from each bay to a rack",
              all(any("route from dock %d " % d in l and "to shelf" in l for l in log) for d in DOCKS), pick(log, "dockroute route"))
    guids = {i: d["guid"] for i, d in check_base(run, pick, "furnished", None).items()}

    # Botanist: weed from the rack upstairs onto a drying rack upstairs.
    before = wing_drying(run.cmd("mxreach work %s" % CODE, wait=1.0))
    log = run.cmd("mxreach stock %s ogkush 10" % CODE, wait=1.0)
    run.check("weed put into the supplies upstairs", "on expansion tiles" in pick(log, "mxreach stock"), pick(log, "mxreach stock"))
    # Handler: soil from both bays onto a rack upstairs.
    soil_before = soil_on_racks(run.cmd("baseddiag %s dockroute work" % SITE, wait=1.0))
    log = run.cmd("baseddiag dock inspect %s" % CODE, wait=1.0)
    shops = [int(m.group(1)) for l in log for m in [re.search(r"dock shop index=(\d+) name='.*?' vehicle=True", l)] if m]
    if run.check("two shops with a delivery vehicle", len(shops) >= 2, shops):
        for dock, shop in zip(DOCKS, shops):
            log = run.cmd("baseddiag %s dockroute deliver %d soil %d" % (SITE, shop, dock), wait=1.0)
            run.check("soil ordered to bay %d" % (dock + 1), any("dockroute deliver" in l and "dock %d" % dock in l for l in log), pick(log, "deliver"))

    dried, soil, work, carried = before, soil_before, [], []
    deadline = time.time() + 420
    while time.time() < deadline:
        work = run.cmd("mxreach work %s" % CODE, wait=1.0)
        dried = wing_drying(work)
        carried = run.cmd("baseddiag %s dockroute work" % SITE, wait=1.0)
        soil = soil_on_racks(carried)
        if dried > before and soil is not None and soil_before is not None and soil >= soil_before + 10:
            break
        time.sleep(10)
    run.check("the botanist moved weed onto a drying rack upstairs", dried > before,
              "drying racks %d -> %d; %s" % (before, dried, pick(work, "active")))
    run.check("the handler carried both deliveries onto the racks", soil is not None and soil_before is not None and soil >= soil_before + 10,
              "soil %s -> %s; %s" % (soil_before, soil, pick(carried, "holds")))

    for n in range(1, args.reloads + 1):
        label = "reload %d" % n
        if not run.check("%s ready" % label, reload(run, args.save)):
            break
        time.sleep(5)
        check_base(run, pick, label, guids)
        log = run.cmd("mxreach work %s" % CODE, wait=1.0)
        run.check("%s: the drying racks upstairs still hold the weed" % label, wing_drying(log) >= dried and dried > 0,
                  "drying racks %d, before the reload %d" % (wing_drying(log), dried))
        log = run.cmd("baseddiag %s dockroute work" % SITE, wait=1.0)
        run.check("%s: the soil is still on the racks" % label, soil_on_racks(log) == soil, "soil %s, before %s" % (soil_on_racks(log), soil))
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
