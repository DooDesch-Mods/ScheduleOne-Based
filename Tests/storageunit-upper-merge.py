"""
In-game check of Based#88: the Storage Unit's upper floor is one placement grid, so an item can stand across the lines
where the loft meets the walkway (x -2.25) and where the walkway meets the south room (z 103.8).

With the build complete (stage bay) it
  1. aims the game's own placement ghost of a large storage rack across each line (rotation 0 across x -2.25,
     rotation 90 across z 103.8) and takes a screenshot each: every ghost whose footprint spans the line must sit on
     one grid with all its tiles there and be valid (white, not red);
  2. builds a rack across each line on the upper grid (`mxreach at`), and a small rack each on the loft's and the
     south room's part of it;
  3. saves, rewrites the two small racks in the save file onto the grids Based 1.2.2 kept for the loft and the south
     room (with their old tiles), loads, and checks that Based moved both onto the upper grid and that all four racks
     stand where they stood.

Needs Debug builds of MapExpansions (`mxreach`) and Based (`baseddiag`) in a running test slot and a save where the
player owns the Storage Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers. --ghost-only runs
step 1 alone (for a build without the merged grid).

    python _wt-Based-bay-area/Tests/storageunit-upper-merge.py [--slot mptest] [--ghost-only]
        [--mapexpansions _wt-MapExpansions-combo]
"""
import argparse
import glob
import hashlib
import importlib.util
import json
import os
import re
import sys
import time
import uuid

CODE = "storageunit"
PHASE = "ray.storageunit-bay:complete"
UPPER_Y = 3.25
UPPER_EXT = "storage-east-floor"
SAVES = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "TVGS", "Schedule I", "Saves")
# Per line: where the player stands (on the upper floor), the floor points the ghost is aimed at, the ghost's
# rotation, the world axis the line cuts (0 = x, 2 = z) and where.
LINES = {
    "x -2.25 (loft to walkway)": dict(stand=(-4.5, 4.45, 108.8), axis=0, at=-2.25, yaw=0,
                                       aims=[(x, 108.8) for x in (-2.75, -2.5, -2.25, -2.0, -1.75)]),
    "z 103.8 (walkway to south room)": dict(stand=(-0.75, 4.45, 106.8), axis=2, at=103.8, yaw=90,
                                             aims=[(-0.75, z) for z in (104.3, 104.05, 103.8, 103.55, 103.3)]),
}
# Racks built straight on the upper grid: (item, tile x, tile y, rotation, what it shows). Tile x 10/11 is the line
# x -2.25, tile y 19/20 the line z 103.8; tiles 11.. of the loft and y 20.. of the south room were their own grids up
# to Based 1.2.2.
RACKS = [
    ("largestoragerack", 9, 10, 0, "across x -2.25"),
    ("largestoragerack", 7, 21, 90, "across z 103.8"),
    ("smallstoragerack", 17, 8, 0, "loft"),
    ("smallstoragerack", 4, 32, 0, "south room"),
]
# The grids Based 1.2.2 built for the loft and the south room, and how far their tiles lie from the upper grid's.
OLD = {"loft": ("storage-loft-floor", (11, 0), "loft's"), "south room": ("storage-south-floor", (0, 20), "south room's")}


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def stable_guid(extension):
    """MapExpansions' Places.StableGuid for an extension's own grid: the MD5 bytes read as a .NET Guid."""
    digest = hashlib.md5(("mapexpansions/based_storage_upper/grid/" + extension).encode("utf-8")).digest()
    return str(uuid.UUID(bytes_le=digest))


def parse_ghost(line):
    m = re.search(r"baseddiag ghost: at \(([^)]*)\), rotation (\S+), grid '([^']*)', (\d+) footprint tiles \[(.*)\], valid (True|False)", line)
    if not m:
        return None
    feet = [(float(a), float(b), state) for a, b, state in re.findall(r"\((-?[\d.]+),(-?[\d.]+)\) (tile|missing[^;\]]*)", m.group(5))]
    pos = tuple(float(v) for v in m.group(1).split(","))
    return dict(pos=pos, rotation=m.group(2), grid=m.group(3), feet=feet, valid=m.group(6) == "True", line=line.strip()[-400:])


def spans(ghost, axis, at):
    values = [f[0] if axis == 0 else f[1] for f in ghost["feet"]]
    return bool(values) and min(values) < at < max(values)


def ghost_line(run, name, spec, reach):
    run.g.call("teleport", {"x": spec["stand"][0], "y": spec["stand"][1], "z": spec["stand"][2]}, timeout_ms=8000)
    time.sleep(1.5)
    seen, shot = [], None
    for x, z in spec["aims"]:
        run.cmd("baseddiag aim %.2f %.2f %.2f - %d" % (x, UPPER_Y, z, spec["yaw"]), wait=1.5)
        log = run.cmd("baseddiag ghost", wait=0.5)
        ghost = next((g for g in (parse_ghost(l) for l in log) if g), None)
        if ghost is None:
            print("      aim (%.2f, %.2f): %s" % (x, z, reach.pick(log, "baseddiag ghost") or "no ghost line"), flush=True)
            continue
        across = spans(ghost, spec["axis"], spec["at"]) and abs(ghost["pos"][1] - UPPER_Y) < 1.0
        print("      aim (%.2f, %.2f): %s%s" % (x, z, "ACROSS " if across else "", ghost["line"]), flush=True)
        if across:
            seen.append(ghost)
            if shot is None:
                shot = run.g.shot()
    run.check("%s: a placement ghost spans the line" % name, bool(seen), "%d of %d aims" % (len(seen), len(spec["aims"])))
    for g in seen:
        whole = all(state == "tile" for _, _, state in g["feet"])
        run.check("%s: the ghost across the line has all its tiles on grid '%s' and is valid" % (name, g["grid"]),
                  whole and g["valid"], g["line"])
    run.check("%s: screenshot of the ghost across the line" % name, bool(shot), shot or "no ghost to show")


def contents(run, item):
    log = run.cmd("baseddiag site=%s contents %s" % (CODE, item), wait=1.5)
    found = []
    for l in log:
        m = re.search(r"baseddiag contents: (\S+) at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)", l)
        if m and m.group(1) == item:
            found.append(tuple(float(m.group(i)) for i in (2, 3, 4)))
    return found


def stands(found, pos):
    return any(all(abs(a - b) < .05 for a, b in zip(f, pos)) for f in found)


def latest_save(slot_save, guid):
    """The save file the game just wrote that holds the item: the newest Storage Unit file of save slot N."""
    files = sorted(glob.glob(os.path.join(SAVES, "*", "SaveGame_%d" % slot_save, "Properties", "Storage Unit.json")),
                   key=os.path.getmtime, reverse=True)
    for path in files:
        with open(path, encoding="utf-8") as handle:
            if guid in handle.read():
                return path
    return None


def rewrite_to_old(path, moves):
    """moves: GUID -> (old grid GUID, (dx, dy)). Puts each item back on its 1.2.2 grid with that grid's tile."""
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    done = {}
    for obj in data.get("Objects") or []:
        base = json.loads(obj["BaseData"])
        if base.get("GUID") not in moves:
            continue
        grid, (dx, dy) = moves[base["GUID"]]
        before = (base["GridGUID"], base["OriginCoordinate"]["x"], base["OriginCoordinate"]["y"])
        base["GridGUID"] = grid
        base["OriginCoordinate"]["x"] -= dx
        base["OriginCoordinate"]["y"] -= dy
        obj["BaseData"] = json.dumps(base, separators=(",", ":"))
        done[base["GUID"]] = (before, (grid, base["OriginCoordinate"]["x"], base["OriginCoordinate"]["y"]))
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=4)
    return done


def load(run, save):
    """From the menu, load save slot N and wait until the game is playable. Returns (ready, last log line)."""
    for attempt in range(6):
        reply = run.g.call("load_save", {"slot": save}, timeout_ms=60000)
        result = (reply or {}).get("result") or {}
        if reply and reply.get("ok") and result.get("loading"):
            break
        print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
        time.sleep(5)
    return run.g.wait_ready(timeout=1200, interval=5, say=lambda s: None)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--ghost-only", action="store_true")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-combo",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 1000", wait=1.0)
    mark = run.mark()
    log = run.cmd("baseddiag site=%s phase %s" % (CODE, PHASE), wait=10.0)
    run.check("build complete (stage bay)", any("construction moved to" in l or "nothing to" in l for l in log),
              pick(log, "baseddiag"))
    failed = next((l.strip()[-260:] for l in run.lines(mark) if "' failed" in l and "storage" in l.lower()), "")
    run.check("world state applied without rollback", not failed, failed or "no failure line")

    run.cmd("give largestoragerack 1", wait=1.5)
    for name, spec in LINES.items():
        ghost_line(run, name, spec, reach)
    # Off the lines, so the player does not stand on the tiles the racks are built on next.
    run.g.call("teleport", {"x": -6.5, "y": 4.45, "z": 111.0}, timeout_ms=8000)
    if args.ghost_only:
        return run.summary()

    built = []
    for item, x, y, rot, what in RACKS:
        log = run.cmd("mxreach at %s %s %d %d %d 1" % (CODE, item, x, y, rot), wait=2.0)
        line = next((l.strip()[-300:] for l in log if "mxreach at: built " + item in l and "of grid 1 " in l), "")
        m = re.search(r"at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\), guid ([0-9a-fA-F-]{36})", line)
        if run.check("built %s %s at tile %d,%d rot %d of the upper grid" % (item, what, x, y, rot), bool(m),
                     line or pick(log, "mxreach at")):
            built.append(dict(item=item, what=what, pos=tuple(float(m.group(i)) for i in (1, 2, 3)), guid=m.group(4)))
    if len(built) != len(RACKS):
        return run.summary()

    run.g.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    path = latest_save(args.save, built[2]["guid"])
    if not run.check("the save file holds the racks", bool(path), path or "no Storage Unit.json of save %d with the loft rack" % args.save):
        return run.summary()
    reload_mark = run.mark()
    run.g.call("return_to_menu", {}, timeout_ms=60000)
    time.sleep(8)
    moves = {r["guid"]: (stable_guid(OLD[r["what"]][0]), OLD[r["what"]][1]) for r in built if r["what"] in OLD}
    done = rewrite_to_old(path, moves)
    run.check("the loft and south racks were put back on their 1.2.2 grids in the save", len(done) == len(moves),
              "; ".join("%s: %s -> %s" % (g, a, b) for g, (a, b) in done.items()))

    ready, last = load(run, args.save)
    if not run.check("reload ready", ready, "" if ready else str(last)):
        return run.summary()
    time.sleep(5)
    lines = run.lines(reload_mark)
    for r in built:
        if r["what"] not in OLD:
            continue
        moved = next((l.strip()[-300:] for l in lines if "grid item %s moved from the %s old grid" % (r["guid"], OLD[r["what"]][2]) in l), "")
        run.check("after reload: Based moved the %s rack from the old grid onto the upper grid" % r["what"], bool(moved),
                  moved or "no 'moved from' line for %s" % r["guid"])
    missing = [l.strip()[-260:] for l in lines if re.search(r"Failed to find grid|could not be moved onto the upper", l)]
    run.check("after reload: no item lost its grid", not missing, " | ".join(missing[:3]) or "none")
    upper = next((l.strip()[-260:] for l in lines if "the upper floor's grid is grid 1 of the property" in l), "")
    run.check("after reload: grid 1 of the property is the upper grid", bool(upper), upper or "no upper grid line")
    for item in sorted({r["item"] for r in built}):
        found = contents(run, item)
        for r in (r for r in built if r["item"] == item):
            run.check("after reload: the %s %s stands where it stood %s" % (item, r["what"], r["pos"]),
                      stands(found, r["pos"]), "found %s" % found)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
