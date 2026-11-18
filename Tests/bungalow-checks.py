"""
Scripted acceptance checks of every Bungalow build stage, in the order the builds go up (only one builds at a time):

  grid      every outer edge of every placement grid lies on a wall's face or at a floor's end (baseddiag gridedges),
            per stage; when all builds stand, the storey's three grids and the west room's grid (house, sunroom and
            deck) cover exactly the planned extents
  zfight    no two faces of different parts lie in one plane (baseddiag zfight). A pair of two read meshes fails; a pair
            with a side that stands in as its bounds ([box]) is listed as INFO, since it may be no real overlap
  lines     the walkable surfaces outside the property (stair landing, balcony) end on the 0.5 m grid lines
  switches  the storey's two copied wall switches stand, each switches only its own lamps, and neither the house's
            switches nor the other copy are touched
  litter    every finished build gets 8 to 15 game trash items from the shared construction litter, all inside its rooms
  place     the game's own placement ghost (baseddiag ghost) is valid on the storey's floor in front of the door, on the
            balcony and in the rooms, aimed from the player's eye
  walls     after the step storey-roof to complete, the shell copy shows the outer walls the state cut had switched off
  stair     a player sent under the stair's run does not stay there with the camera inside the treads
  batch     each copied wall switch draws its own mesh: its bounds sit at the copy, not at the vanilla switch it copied

What these four add, from hand test 3 (the checks of round 102 passed on that build):
  7, 8  grid checked tile positions only; a tile cloned from a distance-culled template kept its collider off and the
        balcony had no grid at all, so only a real ghost shows it (place)
  5, 1  the checks loaded complete directly; the outer walls vanished only on the step from storey-roof (walls)
  6     nothing checked where the player can walk; the space under the run was open (stair)
  9     switches pressed the switch, never looked at what is drawn; the copy shared the vanilla switch's batch (batch)
  2, 3, 4  ground paint and grass are judged in the player-view shot set, not here

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Bungalow.
Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-bungalow/Tests/bungalow-checks.py [--slot mptest] [--only grid,zfight,lines,switches,litter,batch,place,stair,walls]
        [--mapexpansions _wt-MapExpansions-bungalow-int]
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
S1 = os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge", "s1.py")
SITE = "site=bungalow"
# build suffix: phases in build order
BUILDS = [
    ("sunroom", ["sunroom-base", "sunroom-glass", "complete"]),
    ("deck", ["deck-stilts", "complete"]),
    ("carport", ["carport-posts", "complete"]),
    ("storey", ["storey-floor", "storey-frame", "storey-walls", "storey-rafters", "storey-roof", "complete"]),
]
TOL = .011
# Grids by their height and west edge, when every build stands: (name, y, x min, x max, z min, z max).
GRIDS = [
    ("upper south-west", -.65, -176.0, -170.0, 109.35, 115.35),
    ("upper north-west", -.65, -176.0, -170.0, 115.5, 121.5),
    ("upper north-east", -.65, -169.85, -163.85, 115.5, 121.5),
    ("upper terrace", -.65, -177.5, -176.5, 109.85, 112.85),
    # The house's south-west room with the sunroom and the deck west of it.
    ("west room, sunroom and deck", -3.7, -194.0, -170.0, 107.85, 121.35),
]
# The 0.5 m lines: x on -169.85 + k/2 (the north-east room's grid), z on 115.35 + k/2 (the west room's grid).
X_LINE, Z_LINE = -169.85, 115.35
SURFACES = {"landing": "landing", "balcony": "balcony"}
# Vanilla switches of the house (centre of their plate) and the copies: the storey's 3 m above them, the sunroom's on
# the house's outer west wall beside the sliding door. Each copy: (where its plate is, what it is, how many lamps).
HOUSE_SWITCHES = {"west": (-170.02, -2.5, 114.6), "east": (-169.83, -2.5, 117.75)}
COPIES = {
    "storey-switch-west": ((-170.02, .5, 114.6), "3 m above the house's west switch", 2),
    "storey-switch-east": ((-169.83, .5, 117.75), "3 m above the house's east switch", 1),
    "sunroom-switch": ((-176.17, -2.5, 112.8), "on the house's wall beside the sunroom door", 2),
}
VEC = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def on_line(value, line):
    off = (value - line) % .5
    return min(off, .5 - off) < TOL


def wall_strip(line):
    """The sunroom's slab runs on from the west room's last grid line at x -176.5 to the house wall at -176.17, where the
    house's own grid has no tile; this strip under the wall is the one floor edge allowed off the lines."""
    return ("edge at (-176.500," in line and "out (1.000," in line and "based_bungalow_sunroom" in line
            and "slab-collision" in line and "goes on past" in line) or door_balcony(line) or terrace_strip(line)


def terrace_strip(line):
    """The terrace's boards run on from its grid's east line at x -176.5 to the house wall at -176.2, where the south
    room's grid has no tile either; the same strip under the wall as below."""
    return ("edge at (-176.500," in line and "out (1.000," in line and "terrace" in line
            and ("goes on past" in line or "reaches more than" in line))


def door_balcony(line):
    """The upper sliding door stands in the west wall between z 110.25 and 112.55; the west edge in that doorway sees
    the closed door's glass 8 cm out, as the house's own door sits below, or with the door open the terrace outside,
    whose own grid starts on the next line at -176.5."""
    m = re.search(r"edge at \(-176\.000,[-\d.]+,([\d.]+)\) out \(-1\.000", line)
    return bool(m) and 110.25 < float(m.group(1)) < 112.55 and (
        "terrace" in line or "Sliding Glass Door/Sliding glass door" in line)


def grids(run, pick, label):
    log = run.cmd("baseddiag %s gridedges bungalow" % SITE, wait=4.0)
    verdicts = [l.strip()[-240:] for l in log if re.search(r"baseddiag gridedges grid \d+ '.*' (PASS|FAIL):", l)]
    extents = [l for l in log if re.search(r"baseddiag gridedges grid \d+ '.*': \d+ tiles, origin", l)]
    done = pick(log, "baseddiag gridedges bungalow done")
    run.check("%s: grid check ran" % label, bool(done) and bool(verdicts), done or "no gridedges summary")
    for v in verdicts:
        grid = re.search(r"grid (\d+) '([^']*)'", v)
        name = "%s: grid %s '%s' edges on the lines" % (label, grid.group(1), grid.group(2)) if grid else label + ": grid"
        offs = [l.strip() for l in log if grid and ("gridedges grid %s " % grid.group(1)) in l and
                re.search(r"off [-\d.]+ cm|reaches more than|goes on past", l)]
        real = [o for o in offs if not wall_strip(o)]
        for o in offs:
            if wall_strip(o):
                print("INFO  %s: allowed strip %s" % (label, o[-160:]), flush=True)
        run.check(name, " PASS:" in v or (offs and not real), [o[-200:] for o in real[:4]] or v)
    return extents


def grid_extents(run, extents):
    found = []
    for l in extents:
        m = re.search(r"tiles x (-?[\d.]+)\.\.(-?[\d.]+) z (-?[\d.]+)\.\.(-?[\d.]+) y (-?[\d.]+)", l)
        if m:
            found.append(tuple(float(g) for g in m.groups()))
    for name, y, x0, x1, z0, z1 in GRIDS:
        hit = [f for f in found if abs(f[4] - y) < .1 and abs(f[0] - x0) < TOL and abs(f[2] - z0) < TOL]
        ok = len(hit) == 1 and all(abs(a - b) < TOL for a, b in zip(hit[0][:4], (x0, x1, z0, z1)))
        run.check("all builds: %s grid covers x %.2f..%.2f z %.2f..%.2f" % (name, x0, x1, z0, z1), ok, hit or found)


def floor_void(pair):
    at = re.findall(r"at \((-?[\d.]+), ?(-?[\d.]+), ?(-?[\d.]+)\)", pair)
    return ("storey/Copies/bungalow/Wall.001/" in pair and "Bungalow/bungalow/Wall.001/" in pair and bool(at)
            and -1.0 <= float(at[-1][1]) <= -.645)


def zfight(run, pick, label):
    log = run.cmd("baseddiag %s zfight based_ -176 -2 114 26" % SITE, wait=6.0)
    done = pick(log, "baseddiag zfight done")
    run.check("%s: z-fight check ran" % label, bool(done), done or "no zfight summary")
    pairs = [l.strip() for l in log if "baseddiag zfight: FAIL" in l]
    # Two parts of the storey's copy of the house shell keep the house's own layout; the same two faces meet on the
    # ground floor (a window frame on the cladding), so such a pair is the vanilla house, not a seam of the build.
    # The house's inner wall reaches 0.33 m into the void between the ground floor's ceiling and the storey's boards,
    # where its copy begins; that band lies inside the floor and is seen from neither storey.
    shell = [p for p in pairs if p.count("storey/Copies/bungalow/") == 2 or floor_void(p)]
    meshes = [p[p.index("FAIL") + 5:][-260:] for p in pairs if p.count("[mesh]") == 2 and p not in shell]
    boxes = [p[p.index("FAIL") + 5:][-260:] for p in pairs if p.count("[mesh]") < 2]
    for b in boxes:
        print("INFO  %s: bounds pair %s" % (label, b), flush=True)
    for s in shell:
        print("INFO  %s: house shell pair %s" % (label, s[s.index("FAIL") + 5:][-260:]), flush=True)
    run.check("%s: no two meshes in one plane" % label, not meshes, meshes[:6])


def find(slot, name, under):
    proc = subprocess.run([sys.executable, S1, "--slot", slot, "find", name, "--under", under, "--kind", "renderer", "-n", "20"],
                          capture_output=True, text=True)
    try:
        return json.loads(proc.stdout).get("objects", [])
    except ValueError:
        return []


def lines(run, slot):
    for name, part in SURFACES.items():
        objects = [o for o in find(slot, part, "based_bungalow_storey") if o.get("path", "").endswith(".Panel." + part)]
        if not objects:
            run.check("%s edges on the 0.5 m lines" % name, False, "no renderer named '%s' in the storey" % part)
            continue
        b = objects[0].get("bounds") or {}
        mn, mx = b.get("min") or [None] * 3, b.get("max") or [None] * 3
        edges = {"west": (mn[0], X_LINE), "east": (mx[0], X_LINE), "south": (mn[2], Z_LINE), "north": (mx[2], Z_LINE)}
        bad = {k: round(v, 3) for k, (v, line) in edges.items() if v is None or not on_line(v, line)}
        # The balcony's west and north sides meet the landing and the house; only its open sides must lie on the lines.
        if name == "balcony":
            bad.pop("west", None)
            bad.pop("north", None)
        run.check("%s edges on the 0.5 m lines" % name, not bad, bad or {k: round(v, 3) for k, (v, _) in edges.items()})


def switch_line(run, at, radius=.45, action=""):
    log = run.cmd("mxswitch %.2f %.2f %.2f %.2f %s" % (*at, radius, action), wait=1.5)
    return [l.strip()[-300:] for l in log if "[dev] mxswitch '" in l and "active True" in l], log


def house_state(run):
    out = {}
    for side, at in HOUSE_SWITCHES.items():
        found, _ = switch_line(run, at)
        m = re.search(r"on (True|False), lamps (-?\d+), lit (\d+)", found[0]) if len(found) == 1 else None
        out[side] = m.groups() if m else None
    return out


def lamp_lines(run, mark, copy_id, state):
    return sum(1 for l in run.lines(mark) if "%s by 'copy:%s'" % (state, copy_id) in l)


def switches(run, pick):
    run.cmd("settime 2200", wait=1.0)
    start = run.mark()
    for copy_id, (at, where, lamps) in COPIES.items():
        found, _ = switch_line(run, at)
        run.check("switch '%s' stands %s, spawned" % (copy_id, where),
                  len(found) == 1 and "spawned True" in found[0], found or "no active switch within 0.45 m of %s" % (at,))
        arrived = [l for l in run.lines((start[0], 0)) if "found the copied switch '%s'" % copy_id in l]
        run.check("lamps of '%s' found their switch" % copy_id, len(arrived) >= lamps, len(arrived))
    for copy_id, (at, where, lamps) in COPIES.items():
        others = [c for c in COPIES if c != copy_id]
        switch_line(run, at, action="on")
        house = house_state(run)
        mark = run.mark()
        switch_line(run, at, action="off")
        time.sleep(1.0)
        run.check("'%s' off turns its %d lamps off" % (copy_id, lamps), lamp_lines(run, mark, copy_id, "off") >= lamps,
                  lamp_lines(run, mark, copy_id, "off"))
        moved = {c: lamp_lines(run, mark, c, "off") for c in others}
        run.check("'%s' off leaves the other copies' lamps alone" % copy_id, not any(moved.values()), moved)
        after = house_state(run)
        run.check("'%s' off leaves the house's switches as they were" % copy_id, house == after and None not in house.values(), (house, after))
        mark = run.mark()
        switch_line(run, at, action="on")
        time.sleep(1.0)
        run.check("'%s' on turns its %d lamps on again" % (copy_id, lamps), lamp_lines(run, mark, copy_id, "on") >= lamps,
                  lamp_lines(run, mark, copy_id, "on"))
    # The house's own switch: the copies' lamps do not follow it.
    at = HOUSE_SWITCHES["west"]
    switch_line(run, at, action="on")
    mark = run.mark()
    switch_line(run, at, action="off")
    time.sleep(1.0)
    moved = sum(lamp_lines(run, mark, c, "off") for c in COPIES)
    run.check("the house's switch off leaves the storey's and the sunroom's lamps alone", moved == 0, moved)
    switch_line(run, at, action="on")
    run.cmd("settime 1200", wait=1.0)


UPGRADES = {"sunroom": "ray.bungalow-sunroom", "deck": "ray.bungalow-deck", "carport": "ray.bungalow-carport",
            "storey": "ray.bungalow-storey"}


def litter(run, pick):
    """The crew's trash of every finished build: 8 to 15 game trash items, all dropped, all inside the build's rooms."""
    for suffix, upgrade in UPGRADES.items():
        log = run.cmd("baseddiag %s litter drop %s" % (SITE, upgrade), wait=4.0)
        line = next((l.strip() for l in log if "Construction litter for" in l and upgrade in l), "")
        m = re.search(r": (\d+) of (\d+) piece", line)
        run.check("%s: trash dropped" % suffix, bool(m), line[-220:] or "no litter line")
        if not m:
            continue
        placed, count = int(m.group(1)), int(m.group(2))
        run.check("%s: 8 to 15 pieces, none missed" % suffix, 8 <= count <= 15 and placed == count, line[-220:])
        log = run.cmd("baseddiag %s litter %s" % (SITE, upgrade), wait=2.0)
        now = next((l.strip() for l in log if "in the rooms now" in l), "")
        n = re.search(r"in the rooms now (-?\d+)", now)
        run.check("%s: the pieces lie in the build's rooms" % suffix, bool(n) and int(n.group(1)) >= placed, now[-160:] or "no count")


def s1(slot, *args):
    proc = subprocess.run([sys.executable, S1, "--slot", slot, *[str(a) for a in args]], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


# Placement spots, each one the game should allow: (name, player x, z, floor y, target x, y, z).
PLACE_SPOTS = [
    ("in front of the upstairs door", -172.0, 113.1, -.65, -170.55, -.65, 113.1),
    ("on the balcony", -169.0, 113.4, -.65, -166.5, -.65, 114.0),
    ("on the balcony's east end", -166.5, 114.8, -.65, -165.0, -.65, 113.4),
    ("in the storey's south-west room", -174.0, 114.5, -.65, -173.0, -.65, 112.5),
    ("in the storey's north-east room", -167.0, 119.5, -.65, -166.0, -.65, 117.5),
]
PLACE_ITEM = "smallstoragerack"


def place(run, slot):
    run.cmd("baseddiag ghost hush", wait=1.0)
    log = run.cmd("baseddiag ghost %s" % PLACE_ITEM, wait=2.0)
    held = any("baseddiag ghost: holding" in l or "baseddiag ghost: gave" in l for l in log)
    run.check("ghost: %s in hand" % PLACE_ITEM, held, [l.strip()[-160:] for l in log if "baseddiag ghost" in l][-2:])
    if not held:
        return
    for name, px, pz, fy, tx, ty, tz in PLACE_SPOTS:
        s1(slot, "tp", px, round(fy + 1.0, 3), pz)
        code, out = s1(slot, "cam", "shot", "--eye", px, round(fy + 1.785, 3), pz, "--target", tx, ty, tz,
                       "--hud", "show", "--settle", "30", "--sight", "off", "--keep")
        read = run.cmd("baseddiag ghost read", wait=1.5)
        verdict = next((l.strip() for l in read if "baseddiag ghost: valid" in l), "")
        run.check("ghost valid %s" % name, "valid True" in verdict, verdict[-260:] or ("no ghost line; cam %d %s" % (code, out[-160:])))
    s1(slot, "cam", "release")
    run.cmd("baseddiag ghost drop", wait=1.0)


def walls(run):
    """The outer walls after the step storey-roof to complete, as the night's sleep takes it."""
    stage(run, "storey", "storey-roof")
    mark = run.mark()
    stage(run, "storey", "complete")
    time.sleep(2.0)
    woke = [l.strip() for l in run.lines(mark) if "switched on in the copy" in l]
    cuts = [int(m.group(1)) for m in (re.search(r", cut (\d+)\)", l) for l in woke) if m]
    run.check("storey-roof to complete: the shell copy shows the outer walls the cut switched off",
              bool(cuts) and max(cuts) >= 1, woke[-3:] or "no 'switched on in the copy' line")


# The stair's run: from the landing's east edge at the upper floor down to its foot at the ground, over the landing's
# width. A player whose position lies below the treads at its own x stands under the run.
STAIR_TOP_X, STAIR_TOP_Y, STAIR_FOOT_X, STAIR_FOOT_Y = -168.35, -.65, -162.46, -3.96
STAIR_Z = (111.35, 112.85)


def tread(x):
    return STAIR_TOP_Y + (STAIR_FOOT_Y - STAIR_TOP_Y) * (x - STAIR_TOP_X) / (STAIR_FOOT_X - STAIR_TOP_X)


def stair(run, slot):
    z0, z1 = STAIR_Z
    for px, pz in ((-164.8, 112.1), (-163.4, 111.8)):
        s1(slot, "tp", px, -3.0, pz)
        time.sleep(1.5)
        code, out = s1(slot, "where")
        m = re.search(r'"position": \[\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)', out)
        if not m:
            run.check("stair: player sent to %.1f, %.1f" % (px, pz), False, out[-200:])
            continue
        x, y, z = (float(v) for v in m.groups())
        inside = STAIR_TOP_X < x < STAIR_FOOT_X and z0 < z < z1 and y < tread(x)
        run.check("stair: player sent under the run at %.1f, %.1f does not stay under it" % (px, pz), not inside,
                  "ended at %.2f, %.2f, %.2f, treads there at %.2f" % (x, y, z, tread(x)))


def batch(run, slot):
    """The spawned copies are roots named ModularSwitch; a copy that shares the vanilla switch's static batch draws its
    mesh with the vanilla switch's bounds, 3 m under the storey's plate."""
    proc = subprocess.run([sys.executable, S1, "--slot", slot, "find", "", "--under", "ModularSwitch", "--rect", "-177", "112",
                           "-169", "118.5", "--kind", "renderer", "--active-only", "-n", "200"], capture_output=True, text=True)
    try:
        objects = json.loads(proc.stdout).get("objects", [])
    except ValueError:
        objects = []
    drawn = [o for o in objects if o.get("enabled") and o.get("path", "").startswith("ModularSwitch")]
    for copy_id, (at, where, _) in COPIES.items():
        mine = [((o.get("bounds") or {}).get("center") or [0, 0, 0]) for o in drawn]
        mine = [c for c in mine if abs(c[0] - at[0]) < .3 and abs(c[2] - at[2]) < .3]
        near = [c for c in mine if abs(c[1] - at[1]) <= .6]
        far = [[round(v, 2) for v in c] for c in mine if abs(c[1] - at[1]) > .6]
        run.check("switch '%s' draws its own mesh at its plate" % copy_id, bool(near) and not far,
                  far[:3] or "%d renderers drawn at the plate" % len(near))


def stage(run, suffix, phase):
    log = run.cmd("baseddiag %s phase %s %s" % (SITE, phase, suffix), wait=10.0)
    ok = any("construction moved to" in l or "complete" in l for l in log)
    failed = next((l.strip()[-240:] for l in log if "' failed" in l and "based_bungalow" in l), "")
    run.check("%s %s: state applied" % (suffix, phase), ok and not failed, failed or next((l.strip()[-200:] for l in log if "baseddiag" in l), "no reply"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--only", default="grid,zfight,lines,switches,litter")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-bungalow-int",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    only = set(args.only.split(","))
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    run.cmd("settime 1200", wait=1.0)
    run.cmd("baseddiag %s phase complete" % SITE, wait=6.0)
    extents = []
    if only & {"grid", "zfight"}:
        for suffix, phases in BUILDS:
            run.cmd("baseddiag %s phase rights %s" % (SITE, suffix), wait=8.0)
            for phase in phases:
                stage(run, suffix, phase)
                label = "%s %s" % (suffix, phase)
                if "grid" in only:
                    extents = grids(run, pick, label)
                if "zfight" in only:
                    zfight(run, pick, label)
        if "grid" in only:
            grid_extents(run, extents)
    else:
        for suffix, _ in BUILDS:
            run.cmd("baseddiag %s phase complete %s" % (SITE, suffix), wait=10.0)
    if "lines" in only:
        lines(run, args.slot)
    if "switches" in only:
        switches(run, pick)
    if "litter" in only:
        litter(run, pick)
    if "batch" in only:
        batch(run, args.slot)
    if "place" in only:
        place(run, args.slot)
    if "stair" in only:
        stair(run, args.slot)
    if "walls" in only:
        walls(run)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
