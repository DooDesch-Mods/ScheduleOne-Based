"""
In-game check of the Bungalow's finished second storey as a furnished base: the bake joins its probes, NPC paths lead
from the gravel up the stair into each upper room and through the porch to the front door, rain stops upstairs and
falls on the balcony, the upper sliding door opens and closes, a rack and a drying rack stand on the upper floor, a
botanist carries weed from that rack onto the drying rack, and all of it is still in place after two save and reload
rounds.

Needs Debug builds of MapExpansions (`mxreach`, `mxnav`, `mxcover`) and Based (`baseddiag`) in a running test slot and a
save where the player owns the Bungalow. Uses MapExpansions' Tests/employee-reach.py for the console helpers. The
furniture stays upstairs and blocks taking the storey back, so this runs last on a save.

    python _wt-Based-bungalow/Tests/storey-function.py [--slot mptest] [--save 5] [--reloads 2]
        [--mapexpansions _wt-MapExpansions-tree-rules]
"""
import argparse
import importlib.util
import os
import re
import subprocess
import sys
import time

CODE = "bungalow"
SITE = "site=" + CODE
# The upper floor is y -0.65; an item's pivot can sit up to a metre above the floor it stands on.
UPPER = (-.8, .6)
FURNITURE = ["smallstoragerack", "dryingrack"]
VEC = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"
# name: (from, to). The gravel east of the stair foot, each upper room, and the front door under the balcony.
PATHS = {
    "gravel to the upper west room": ((-160.5, -4.0, 111.9), (-173.0, -.65, 112.5)),
    "gravel to the upper north-west room": ((-160.5, -4.0, 111.9), (-173.0, -.65, 118.5)),
    "gravel to the upper north-east room": ((-160.5, -4.0, 111.9), (-166.9, -.65, 118.5)),
    "gravel through the porch to the front door": ((-160.5, -4.0, 114.0), (-171.5, -3.7, 113.8)),
    "front room back out to the gravel south of the stair": ((-172.0, -3.7, 111.0), (-160.0, -4.0, 109.6)),
}


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def wing_drying(lines):
    """Sum of input + drying + output over the drying racks on expansion tiles."""
    total = 0
    for l in lines:
        m = re.search(r"drying rack at .* on expansion tiles: input (\d+), drying (\d+), output (\d+)", l)
        if m:
            total += sum(int(g) for g in m.groups())
    return total


def upstairs(run):
    """Item ids on expansion tiles of the upper floor, with whether vanilla counts each as reachable."""
    log = run.cmd("mxreach %s" % CODE, wait=3.0)
    items = []
    for l in log:
        m = re.search(r"mxreach:   (\S+) at " + VEC, l)
        if m and "on expansion tiles" in l and "vanilla reach" in l and UPPER[0] < float(m.group(3)) < UPPER[1]:
            items.append((m.group(1), "vanilla reach yes" in l, l.strip()[-220:]))
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


def check_paths(run, pick, label):
    for name, (a, b) in PATHS.items():
        log = run.cmd("mxnav path %.2f %.2f %.2f %.2f %.2f %.2f" % (*a, *b), wait=1.0)
        line = pick(log, "mxnav path")
        run.check("%s: path %s" % (label, name), "ok=True status=PathComplete" in (line or ""), line or "no mxnav path reply")


def check_furniture(run, label):
    items = upstairs(run)
    have = sorted(i[0] for i in items)
    run.check("%s: the furniture stands on the upper floor" % label, all(f in have for f in FURNITURE), have)
    run.check("%s: everything upstairs is usable (vanilla reach)" % label, bool(items) and all(i[1] for i in items),
              [i[2] for i in items if not i[1]])


def cover_at(run, pick, slot, x, y, z):
    s1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
    proc = subprocess.run([sys.executable, s1, "--slot", slot, "tp", "%.2f" % x, "%.2f" % y, "%.2f" % z],
                          capture_output=True, text=True)
    if proc.returncode != 0:
        return "tp failed: " + (proc.stdout + proc.stderr).strip()[-200:]
    time.sleep(3)
    lines = run.cmd("mxcover", wait=1.0)
    return next((l.strip() for l in lines if "weather cover:" in l and " at (" in l), "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--reloads", type=int, default=2)
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-tree-rules",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 1200", wait=1.0)
    mark = run.mark()
    run.cmd("baseddiag %s phase complete" % SITE, wait=6.0)
    # From the roof stage, so the finished storey bakes its navigation in this run even if an earlier check left it done.
    run.cmd("baseddiag %s phase storey-roof storey" % SITE, wait=8.0)
    mark = run.mark()
    log = run.cmd("baseddiag %s phase complete storey" % SITE, wait=12.0)
    run.check("the storey is complete", any("construction moved to" in l for l in log), pick(log, "baseddiag"))
    lines = run.lines(mark)
    failed = next((l.strip()[-260:] for l in lines if "' failed" in l and "based_bungalow_storey" in l), "")
    run.check("states applied without rollback", not failed, failed or "no failure line")
    bake = [l.strip()[-200:] for l in lines if "based_bungalow_storey" in l and re.search("probe|stitch|bake", l, re.I)]
    run.check("the bake reports the storey's probes", bool(bake), bake[-1] if bake else "no bake line for based_bungalow_storey")
    bad = [l for l in bake if re.search("not connected|unreachable", l, re.I)]
    run.check("every storey probe is connected", not bad, bad or bake[-3:])
    check_paths(run, pick, "built")

    # Rain: a dry room upstairs, an open sky on the balcony.
    run.cmd("setweather heavyrain", wait=3.0)
    line = cover_at(run, pick, args.slot, -173.0, 0.0, 118.5)
    run.check("upper room: under a roof, rain off around the camera", ", under a roof;" in line and "camera effects off" in line, line)
    line = cover_at(run, pick, args.slot, -166.8, 0.0, 113.9)
    run.check("balcony: open sky, rain on", ", open sky;" in line and "camera effects on" in line, line)
    line = cover_at(run, pick, args.slot, -173.0, -3.6, 118.5)
    run.check("lower room: still under a roof", ", under a roof;" in line, line)
    run.cmd("setweather clear", wait=1.0)

    # The upper sliding door onto the French balcony.
    log = run.cmd("baseddiag door storey state", wait=1.0)
    run.check("upper door leaf bound", "leaf at" in (pick(log, "storey door") or ""), pick(log, "door"))
    run.cmd("baseddiag door storey open", wait=5.0)
    line = pick(run.cmd("baseddiag door storey state", wait=1.0), "storey door") or ""
    run.check("upper door opens", " open; leaf at" in line and "progress 1.00" in line, line)
    run.cmd("baseddiag door storey close", wait=5.0)
    line = pick(run.cmd("baseddiag door storey state", wait=1.0), "storey door") or ""
    run.check("upper door closes", " closed; leaf at" in line, line)

    # Furniture upstairs. The storey's grid is the newest one the property has; the house and its other builds use 0-2.
    run.cmd("addemployee botanist %s" % CODE, wait=5.0)
    grid = None
    for index in range(3, 8):
        log = run.cmd("mxreach place %s %s wing 0 %d" % (CODE, FURNITURE[0], index), wait=2.0)
        placed = pick(log, "mxreach place: built")
        m = re.search(r" at " + VEC, placed or "")
        if m and UPPER[0] < float(m.group(2)) < UPPER[1]:
            grid = index
            break
    run.check("built %s upstairs" % FURNITURE[0], grid is not None, "no grid 3-7 put it on the upper floor")
    for item in FURNITURE[1:]:
        if grid is None:
            break
        log = run.cmd("mxreach place %s %s wing 0 %d" % (CODE, item, grid), wait=2.0)
        placed = pick(log, "mxreach place: built " + item)
        run.check("built %s upstairs" % item, bool(placed), placed or pick(log, "mxreach place"))
    log = run.cmd("mxreach setup %s wing" % CODE, wait=2.0)
    setup = pick(log, "drying racks), supplies")
    run.check("the botanist's supplies are the rack upstairs", "on expansion tiles" in (setup or ""), setup)
    check_furniture(run, "furnished")

    before = wing_drying(run.cmd("mxreach work %s" % CODE, wait=1.0))
    log = run.cmd("mxreach stock %s ogkush 10" % CODE, wait=1.0)
    run.check("weed put into the supplies upstairs", "on expansion tiles" in (pick(log, "mxreach stock") or ""), pick(log, "mxreach stock"))
    dried, work = before, []
    deadline = time.time() + 420
    while time.time() < deadline:
        work = run.cmd("mxreach work %s" % CODE, wait=1.0)
        dried = wing_drying(work)
        if dried > before:
            break
        time.sleep(10)
    run.check("the botanist moved weed onto the drying rack upstairs", dried > before,
              "drying racks %d -> %d; %s" % (before, dried, pick(work, "active")))

    for n in range(1, args.reloads + 1):
        label = "reload %d" % n
        if not run.check("%s ready" % label, reload(run, args.save)):
            break
        time.sleep(5)
        check_furniture(run, label)
        check_paths(run, pick, label)
        log = run.cmd("mxreach work %s" % CODE, wait=1.0)
        run.check("%s: the drying rack upstairs still holds the weed" % label, wing_drying(log) >= dried and dried > 0,
                  "drying racks %d, before the reload %d" % (wing_drying(log), dried))
        line = pick(run.cmd("baseddiag door storey state", wait=1.0), "storey door") or ""
        run.check("%s: upper door leaf bound" % label, "leaf at" in line, line)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
