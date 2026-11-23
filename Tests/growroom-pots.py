"""
In-game check of issue #70: the game's own placement ghost of a pot is accepted on every free floor tile of the
finished Sweatshop wing. The player stands near each tile with a pot in hand and looks at it; `baseddiag ghost read`
reports the game's verdict (BuildUpdate_Grid._validPosition) and, per footprint tile, why a tile is refused.

A tile is a defect when the ghost lands on it and the game refuses it although no item stands there: "no tile",
"corner obstacle" or "tile refuses". A tile under another item is listed as occupied, not as a defect.

Needs a Debug build of Based (`baseddiag aim|ghost`) in a running test slot and a save whose wing is finished, for
example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Profile main -Build MapExpansions,_wt-Based-<topic> -Template mrmonkey-dock -NoApp
    python _wt-Based-<topic>/Tests/growroom-pots.py [--slot mptest] [--item plasticpot] [--only 21,19]

Prints PASS or FAIL per check, a map of the wing (. accepted, o occupied, s under the sink, X refused, ? no verdict) and
a summary line.
"""
import argparse
import importlib.util
import os
import re
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("ray_plan_copies", os.path.join(_here, "ray-plan-copies.py"))
_rpc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_rpc)

FLOOR_Y = -0.25
# The wing's rooms on the floor (x1, z1, x2, z2), as Content/Ming/MingWorld.cs AddPlacementTiles lays its tiles.
ROOMS = [(-60.0, 136.0, -54.0, 138.0), (-56.0, 138.0, -54.0, 141.0), (-60.0, 141.0, -54.0, 145.0), (-60.0, 140.0, -59.0, 141.0)]
# Unity's Vector3.ToString("F2") writes "(x, y, z)", the ghost line writes "(x,y,z)"; both are read.
GHOST = re.compile(r"baseddiag ghost: valid (True|False), ghost at \(([-\d.]+), ?([-\d.]+), ?([-\d.]+)\).*nearest (?:grid '([^']*)' tile (\d+),(\d+)|none)")
CAMERA = re.compile(r"camera \(([-\d.]+), ?([-\d.]+), ?([-\d.]+)\)")
# The sink stands on these cells; MapExpansions closes a fixture's tiles for building while the object stands.
SINK = {(10, 18), (10, 19), (11, 18), (11, 19)}
# The camera sits about 1.7 m above the feet. Higher means the player stands on an item and looks at the tile from above it.
CAMERA_MAX_Y = FLOOR_Y + 2.2
FOOT = re.compile(r"baseddiag ghost: footprint (\d+) on (\d+),(\d+): (.+)\.$")


def inside(x, z, margin=0.01):
    return any(r[0] - margin <= x <= r[2] + margin and r[1] - margin <= z <= r[3] + margin for r in ROOMS)


def tiles():
    found = []
    for x in range(10, 22):
        for y in range(1, 21):
            wx, wz = -64.75 + x * .5, 135.25 + y * .5
            corners = [(wx - .25, wz - .25), (wx + .25, wz - .25), (wx - .25, wz + .25), (wx + .25, wz + .25), (wx, wz)]
            if all(inside(cx, cz) for cx, cz in corners):
                found.append((x, y, wx, wz))
    return found


def stand_for(wx, wz):
    # 1.6 m from the tile, towards the middle of the wing's big room, so the look ray meets the floor at a slant.
    cx, cz = -57.0, 142.0
    dx, dz = cx - wx, cz - wz
    length = (dx * dx + dz * dz) ** .5
    if length < 0.8:
        return wx + 1.6, wz
    sx, sz = wx + dx / length * 1.6, wz + dz / length * 1.6
    return (sx, sz) if inside(sx, sz) else (cx, cz)


def probe(run, x, y, wx, wz):
    sx, sz = stand_for(wx, wz)
    # teleport places the capsule centre; the feet stand about a metre lower.
    run.g.call("teleport", {"x": sx, "y": FLOOR_Y + 1.1, "z": sz}, timeout_ms=8000)
    time.sleep(.5)
    run.cmd("baseddiag aim %.2f %.2f %.2f" % (wx, FLOOR_Y, wz), wait=.6)
    lines = run.cmd("baseddiag ghost read", wait=.6)
    head = next((GHOST.search(l) for l in lines if GHOST.search(l)), None)
    feet = [FOOT.search(l).groups() for l in lines if FOOT.search(l)]
    return head, feet


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--item", default="plasticpot")
    parser.add_argument("--only", action="append", default=[], help="x,y of one tile; repeatable")
    args = parser.parse_args()
    run = _rpc.Run(args.slot)

    held = run.cmd("baseddiag ghost %s" % args.item, wait=1.0)
    run.check("a %s is in hand" % args.item, any("holding " + args.item in l for l in held), next((l.strip()[-200:] for l in held if "baseddiag" in l), "no answer"))
    wanted = {tuple(int(v) for v in o.split(",")) for o in args.only}
    cells = [t for t in tiles() if not wanted or (t[0], t[1]) in wanted]
    run.note("%d tiles in the wing" % len(cells))

    verdict = {}
    for x, y, wx, wz in cells:
        head, feet = probe(run, x, y, wx, wz)
        if head is None:
            verdict[(x, y)] = ("s", "the sink stands here") if (x, y) in SINK else ("?", "no ghost verdict")
            continue
        valid = head.group(1) == "True"
        landed = (int(head.group(6)), int(head.group(7))) if head.group(6) else None
        reasons = [f[3] for f in feet]
        camera = CAMERA.search(head.string)
        if valid:
            verdict[(x, y)] = (".", "accepted at %s" % (landed,))
        elif (x, y) in SINK:
            verdict[(x, y)] = ("s", "the sink stands here")
        elif camera and float(camera.group(2)) > CAMERA_MAX_Y:
            verdict[(x, y)] = ("o", "the player stood on an item (camera at y %s), so an item stands near the tile" % camera.group(2))
        elif any(r.startswith("occupied") for r in reasons) and not any(r in ("no tile", "corner obstacle") or r.startswith("tile refuses") for r in reasons):
            verdict[(x, y)] = ("o", "; ".join(sorted(set(reasons))))
        else:
            verdict[(x, y)] = ("X", "landed %s on grid '%s': %s" % (landed, head.group(5), "; ".join("%s,%s %s" % (f[1], f[2], f[3]) for f in feet) or "no footprint lines"))

    print("\n      wing map, x 10..21 left to right, y from 20 down to 1:")
    for y in range(20, 0, -1):
        row = "".join(verdict.get((x, y), (" ",))[0] for x in range(10, 22))
        if row.strip():
            print("      %2d %s" % (y, row))
    for (x, y), (mark, detail) in sorted(verdict.items()):
        if mark in ("X", "?"):
            run.check("tile %d,%d takes a %s" % (x, y, args.item), False, detail)
    accepted = sum(1 for m, _ in verdict.values() if m == ".")
    run.check("every free wing tile takes a %s" % args.item, all(m in (".", "o", "s") for m, _ in verdict.values()),
              "%d accepted, %d occupied, %d under the sink, %d refused, %d without verdict" % (
                  accepted, sum(1 for m, _ in verdict.values() if m == "o"), sum(1 for m, _ in verdict.values() if m == "s"),
                  sum(1 for m, _ in verdict.values() if m == "X"), sum(1 for m, _ in verdict.values() if m == "?")))
    run.cmd("baseddiag ghost drop", wait=.5)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
