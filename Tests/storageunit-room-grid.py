"""
In-game check of a finished room on the Storage Unit's upper floor (stage B 'east' or C 'south'): its extension builds
a grid of its own, the grids before it survive the change, items placed on the new grid stand in the room, a botanist
reaches them, and every tile has a walking path. With --after-reload the same after save and reload.

Needs Debug builds of MapExpansions (`mxreach`) and Based (`baseddiag phase`) in a running test slot and a save where
the player owns the Storage Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-storageunit/Tests/storageunit-room-grid.py --room east|south [--slot mptest] [--after-reload]
        [--mapexpansions _wt-MapExpansions-combo]
"""
import argparse
import importlib.util
import os
import re
import sys
import time

CODE = "storageunit"
UPPER_Y = 3.25
ITEMS = ("smallstoragerack", "dryingrack")
# Per room: the build to jump to, its world state, its extension, the grids built before it, the room's extent on the
# upper floor (x, z) and the probes the state's navigation checks.
ROOMS = {
    "east": dict(phase="ray.storageunit-east:complete", state="b-complete", ext="storage-east-floor",
                 before=("storage-loft-floor",), x=(-2.3, 3.3), z=(103.7, 113.9), probes=8),
    "south": dict(phase="ray.storageunit-south:complete", state="c-complete", ext="storage-south-floor",
                  before=("storage-loft-floor", "storage-east-floor"), x=(-2.3, 3.3), z=(93.9, 103.8), probes=11),
}


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def grid_lines(lines, ext):
    return [l.strip()[-260:] for l in lines if re.search(r"property extension: '.*%s' built grid \d+ of '%s'" % (ext, CODE), l)]


def grid_index(line):
    m = re.search(r"built grid (\d+) of", line or "")
    return int(m.group(1)) if m else None


def latest(run, mark, lines, test, why):
    """The last line passing test after the mark; else the last one since the game started. A state already active
    keeps its unchanged extension and navigation and logs neither again."""
    found = [l.strip()[-260:] for l in lines if test(l)]
    if found:
        return found[-1]
    found = [l.strip()[-260:] for l in run.lines((mark[0], 0)) if test(l)]
    if found:
        print("      %s kept from an earlier build in this run" % why, flush=True)
    return (found or [""])[-1]


def room_items(run, room):
    log = run.cmd("mxreach %s" % CODE, wait=3.0)
    items = []
    for l in log:
        m = re.search(r"mxreach:   (\S+) at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\) on expansion tiles: vanilla reach (yes|NO)", l)
        if not m:
            continue
        x, y, z = float(m.group(2)), float(m.group(3)), float(m.group(4))
        # An item's pivot can sit above its feet (a storage rack's by 0.87 m), never below the floor.
        if UPPER_Y - 0.1 <= y <= UPPER_Y + 1.5 and room["x"][0] <= x <= room["x"][1] and room["z"][0] <= z <= room["z"][1]:
            items.append({"id": m.group(1), "reach": m.group(5) == "yes", "line": l.strip()[-220:]})
    tiles = next((l.strip()[-300:] for l in log if re.search(r"\d+ expansion tiles: \d+ outside", l)), "")
    return items, tiles


def check_room(run, room, label):
    items, tiles = room_items(run, room)
    print("      " + (tiles or "no tile summary"), flush=True)
    m = re.search(r"(\d+) expansion tiles: (\d+) outside the bounds, (\d+) without a path, (\d+) cached on another floor", tiles or "")
    run.check("%s: every expansion tile lies inside the property bounds" % label, bool(m) and int(m.group(2)) == 0, tiles)
    run.check("%s: every expansion tile has a walking path" % label, bool(m) and int(m.group(3)) == 0, tiles)
    run.check("%s: no floor sample the game cached lies on another floor" % label, bool(m) and int(m.group(4)) == 0, tiles)
    ids = sorted(i["id"] for i in items)
    run.check("%s: the placed items stand in the room at y %.2f" % (label, UPPER_Y), ids == sorted(ITEMS), ids)
    for i in items:
        run.check("%s: %s in the room is usable (vanilla reach)" % (label, i["id"]), i["reach"], i["line"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--room", choices=sorted(ROOMS), required=True)
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--after-reload", action="store_true")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-combo",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    room = ROOMS[args.room]
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 1000", wait=1.0)
    mark = run.mark()
    log = run.cmd("baseddiag site=%s phase %s" % (CODE, room["phase"]), wait=10.0)
    run.check("%s room complete" % args.room, any("construction moved to" in l for l in log), pick(log, "baseddiag"))
    lines = run.lines(mark)
    failed = next((l.strip()[-260:] for l in lines if "%s' failed" % room["state"] in l), "")
    run.check("state %s applied without rollback" % room["state"], not failed, failed or "no failure line")
    built = latest(run, mark, lines, lambda l: bool(grid_lines([l], room["ext"])), "the extension")
    index = grid_index(built)
    if not run.check("the %s extension built a grid of its own" % args.room, index is not None, built or "no 'built grid' line"):
        return run.summary()
    print("      " + built, flush=True)
    # Only the bake summary; the NavMesh sample cache line after it names the state too.
    nav = latest(run, mark, lines, lambda l: re.search(r"%s' baked \d+ agent types" % re.escape(room["state"]), l), "the navigation")
    want = "%d probes connected" % room["probes"]
    run.check("navigation baked with every probe connected", want in nav, nav or "no bake line")

    run.cmd("addemployee botanist %s" % CODE, wait=5.0)
    for item in ITEMS:
        log = run.cmd("mxreach place %s %s wing 0 %d" % (CODE, item, index), wait=2.0)
        placed = next((l.strip()[-260:] for l in log if "mxreach place: built " + item in l and "of grid %d " % index in l), "")
        run.check("built %s on grid %d" % (item, index), bool(placed), placed or pick(log, "mxreach place"))
    check_room(run, room, "fresh")

    if args.after_reload:
        run.g.call("save_now", {}, timeout_ms=60000)
        time.sleep(3)
        run.g.call("return_to_menu", {}, timeout_ms=60000)
        time.sleep(8)
        mark = run.mark()
        for attempt in range(6):
            reply = run.g.call("load_save", {"slot": args.save}, timeout_ms=60000)
            result = (reply or {}).get("result") or {}
            if reply and reply.get("ok") and result.get("loading"):
                break
            print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
            time.sleep(5)
        ready, last = run.g.wait_ready(timeout=1200, interval=10, say=lambda s: None)
        if run.check("reload ready", ready, "" if ready else str(last)):
            time.sleep(5)
            lines = run.lines(mark)
            again = (grid_lines(lines, room["ext"]) or [""])[-1]
            run.check("after reload: the %s grid is built again with the same index" % args.room, grid_index(again) == index, again or "no 'built grid' line")
            for ext in room["before"]:
                other = (grid_lines(lines, ext) or [""])[-1]
                run.check("after reload: %s is built too" % ext, grid_index(other) is not None, other or "no 'built grid' line")
            broken = next((l.strip()[-260:] for l in lines if "heatmap of '%s' keeps its old" % CODE in l), "")
            run.check("after reload: the heatmap took every grid", not broken, broken or "no heatmap failure")
            check_room(run, room, "after reload")
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
