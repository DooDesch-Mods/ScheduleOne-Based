"""The Storage Unit's upper level as a player sees it: site props on the floor and out of the walls, NPCs on the outside
stair instead of under it, closed wall seams, and ceiling bars that light the floor and follow their switch.

Groups (--only, comma separated, default all):
  props   levels 2, 4 and 6: every prop copy lies inside its room's wall linings, stands on the floor or on another
          prop, does not overlap another prop, and the buckets stand upright.
  nav     level 3: the ground under the stair is no navmesh any more, the paths to the loft door and into the loft are
          complete, and a hired employee sent to the loft door climbs the stair without a warp, and no NPC walks below its
          steps.
  seams   level 7: no surface of ours flickers in one plane against another (mxcoplanar), every mesh of ours has
          tangents, so the sheets' normal map draws clean ribs (mxtangents), and no dark slit (brick showing between
          panels) in fixed wall views.
  hairlines  levels 3 and 7 at 23:00: no thin dark line at the loft's south-east corner, seen from the hall and the
          loft.
  lights  level 7 at 23:00: every ceiling bar's spot light is on with at least the house bars' reach (6) and Unit 1's
          intensity (2) and goes out with its switch; each room's floor gains at least 70 % of the light Unit 1's own bar gives its floor (same camera,
          linear on/off ratio); the loft bars do not light Unit 1's floor through the loft floor.
  leak    level 7 at 23:00: with all copy bars on against all off, the outside faces of the east, south, west and north
          walls at the upper rooms' floor, Unit 2 and Unit 3 below the rooms and the hall floor in front of Unit 3 stay
          as dark in the middle of each view (linear ratio, or under one 8-bit step).
  staff   rooms finished one by one: every place for an employee has a spot of its own.

Prints PASS/FAIL per check and writes the views to OUT.

usage: storageunit-handtest.py --slot mptest4 --out _drafts/based-storageunit/handtest [--only props,nav]
"""
import argparse
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import sys
import time

from PIL import Image

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
UPPER = 3.25

# Inner faces of the wall linings per room: x0, x1, z0, z1.
ROOMS = {
    "loft": (-8.74, -2.06, 103.81, 113.79),
    "walkway": (-2.05, 0.245, 93.8, 113.79),
    "east": (0.26, 3.24, 103.81, 113.79),
    "south": (0.26, 3.24, 94.31, 101.29),
}
# Copies under "Copies/" that are part of the building, not props ("InteriorSpawnPoint" carries the wall and floor panels).
STRUCTURE = ("FluroBar", "OutdoorFence", "ModularSwitch", "MapExpansions", "InteriorSpawnPoint")
# From level 4 on the loft's east face is gone, so loft and walkway are one room.
OPEN_LOFT = dict(ROOMS, loft=(-8.74, 0.245, 103.81, 113.79))
PROP_STAGES = [
    ("l2", "ray.storageunit-loft:loft-door", (-8.8, 103.7, -2.06, 113.9), ROOMS),
    ("l4", "ray.storageunit-east:east-open", (-2.4, 93.8, 3.3, 113.9), OPEN_LOFT),
    ("l6", "ray.storageunit-south:south-open", (0.2, 93.9, 3.3, 101.6), OPEN_LOFT),
]
# An upright bucket is 0.38 m high, one on its side 0.35 m.
BUCKET_UPRIGHT = .37
# The stair and the loft are property interior on the walking map, as vanilla marks its buildings: town NPCs' masks
# leave that area out and stop at the stair's foot, employees' masks hold it. The walk sends a hired handler.
EMPLOYEE = "handler"

# The outside stair: footprint and walking surface (measured on the steps, rising 0.5625 m per metre north).
STAIR_X = (-10.59, -9.09)
STAIR_Z = (100.1, 104.9)
DOOR = (-8.1, 3.25, 105.45)


def stair_y(z):
    return max(0.0, min(UPPER, 0.79 + (z - 100.5) * 0.5625))


# Wall views by day in level 7 (eye, target, share of the image width that shows wall), checked for dark slits in the
# rows of the wall band. Outside the share the view holds rails, the hall or a door frame, which are dark by design.
SEAM_VIEWS = [
    ("seam-east-room-along", (0.6, 4.7, 104.4), (3.25, 4.5, 113.4), (0, 1)),
    ("seam-east-room-wall", (0.7, 4.6, 108.8), (3.25, 4.6, 108.8), (0, 1)),
    ("seam-east-room-north", (1.75, 4.6, 105.0), (1.75, 4.6, 113.79), (0, 1)),
    ("seam-walkway-n", (-0.9, 4.9, 95.0), (-0.9, 4.6, 113.0), (.58, .99)),
    ("seam-walkway-s", (-0.9, 4.9, 113.0), (-0.9, 4.6, 95.0), (0, .45)),
    ("seam-loft-north", (-5.5, 4.6, 105.5), (-5.5, 4.6, 113.8), (0, 1)),
    # The right edge holds the steel post of the opening to the south walkway.
    ("seam-loft-east", (-5.5, 4.6, 108.8), (-2.25, 4.6, 108.8), (0, .95)),
    ("seam-south-room-wall", (0.4, 4.6, 97.75), (3.25, 4.6, 97.75), (0, 1)),
    ("seam-south-room-north", (1.75, 4.6, 95.0), (1.75, 4.6, 101.3), (.41, .99)),
    # The far wall ends at 0.71 of the width; beyond it the open face shows the hall's dark brick.
    ("seam-south-room-south", (1.75, 4.6, 100.5), (1.75, 4.6, 94.3), (0, .70)),
]
# Hairlines at the loft's south-east corner at night (name, phase, eye, target, pixel window x0, x1, y0, y1, ratio).
# The windows frame the spot where the line was: at level 3 the bare south end of the walkway's roller face beside the
# corner post (x -2.2, z 103.8, y 4.7 to 5.5), at level 7 the cut ends of the south linings beside the openings.
HAIRLINE_VIEWS = [
    ("hairline-l3-hall", "ray.storageunit-loft:complete", (-4.0, 1.7, 100.0), (-2.1, 5.0, 103.8), (1217, 1299, 626, 770), .55),
    ("hairline-l7-hall", "ray.storageunit-south:complete", (-4.0, 1.7, 100.0), (-2.1, 5.0, 103.8), (1217, 1299, 626, 770), .55),
    ("hairline-l7-loft", "ray.storageunit-south:complete", (-4.5, 4.9, 105.5), (-2.1, 5.4, 103.8), (900, 1040, 700, 1250), .62),
]
# Straight onto the two truss seams of the south room's east wall; for the eye, not measured.
SEAM_CLOSEUPS = [
    ("seam-south-truss-95", (2.2, 4.0, 95.95), (3.25, 4.0, 95.95)),
    ("seam-south-truss-100", (2.2, 4.0, 99.98), (3.25, 4.0, 99.98)),
    # The east room's south and north walls face along z, where missing tangents fray the ribs.
    ("ribs-east-south-wall", (-0.44, 5.0, 106.12), (1.48, 4.93, 103.8)),
    ("ribs-east-north-wall", (1.75, 4.8, 106.5), (1.75, 4.6, 113.8)),
]
COPLANAR = {
    "loft": "-8.8 3.2 103.7 -2.0 6.3 113.9",
    "walkway": "-2.2 3.2 93.9 0.3 6.3 113.9",
    "east": "0.2 3.2 103.7 3.6 6.3 113.9",
    "south": "0.2 3.2 93.9 3.6 6.3 101.6",
}
# room, light probe centre, switch position, bar the floor view looks under (x, z), floor y, player spot behind the camera
LIGHTS = [
    ("loft", (-5.5, 4.5, 108.8), "-8.71 4.45 104.5", (-5.5, 108.8), UPPER, (-7.0, 3.3, 105.0)),
    ("east", (1.75, 4.5, 108.8), "0.55 4.45 103.84", (0.6, 108.8), UPPER, (1.5, 3.3, 104.6)),
    ("south", (1.75, 4.5, 97.75), "0.55 4.45 101.27", (1.1, 99.4), UPPER, (2.6, 3.3, 95.0)),
]
# Unit 1's own bar below the loft, the reference for how much a bar lights its floor.
VANILLA_LIGHT = ("unit1", "-4.62 1.3 104.78", (-6.52, 107.23), 0.1, (-8.0, 0.1, 104.4))
# A copy's floor gain (linear on/off ratio) must reach this share of the vanilla bar's.
VANILLA_SHARE = .7
# Unit 1's floor with its own bar off may get at most this much brighter from the loft bars above it.
LEAK_RATIO = 1.15
# Views of the rooms' outside faces and of the rooms below them (name, eye, target). The bars hang at y 6 and point
# down, so their cone meets an outer wall lowest, at the upper rooms' floor (y 3.25 to 4); the views look there, at the
# point of the wall nearest a bar. Outside: the east wall (x 3.51) of the east and south rooms, the south wall (z 93.5),
# the loft's west wall (x -9.01), the north wall (z 114) of the loft and the east room. Below: Unit 2 under the east
# room, Unit 3 under the south room, the hall floor in front of Unit 3.
LEAK_VIEWS = [
    ("east-wall-east-room", (8.5, 3.6, 108.8), (3.51, 3.6, 108.8)),
    ("east-wall-south-room", (8.5, 3.6, 96.0), (3.51, 3.6, 96.0)),
    ("south-wall", (1.1, 3.6, 88.5), (1.1, 3.6, 93.5)),
    ("loft-west-wall", (-14.0, 3.6, 108.8), (-9.01, 3.6, 108.8)),
    ("north-wall-loft", (-5.5, 3.6, 119.0), (-5.5, 3.6, 114.0)),
    ("north-wall-east-room", (0.6, 3.6, 119.0), (0.6, 3.6, 114.0)),
    ("unit2-floor", (-1.2, 1.75, 106.0), (1.75, 0.1, 108.8)),
    ("unit3-floor", (-1.2, 1.75, 96.0), (1.75, 0.1, 98.5)),
    ("hall-floor-at-unit3", (-5.5, 1.75, 97.0), (-1.8, 0.1, 98.5)),
]
# The middle of a view (the target point) may get at most this much brighter with the bars on, linear; below one 8-bit
# step on average the change is not visible on these near-black faces, so that passes too.
OUTSIDE_RATIO = 1.05

failed = 0


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""), flush=True)


def s1(slot, *args, timeout=240):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def run(slot, until, command, max_wait=90000):
    return s1(slot, "run", "--until", until, "--max-wait", str(max_wait), command)[1]


def phase(slot, name):
    text = run(slot, "construction moved to|baseddiag: .*(not|no |unknown)", "baseddiag site=storageunit phase " + name)
    ok = "construction moved to" in text
    report(ok, "jump to " + name, "" if ok else text[-300:])
    return ok


def find(slot, rect):
    code, text = s1(slot, "find", "--kind", "renderer", "--active-only", "--rect", *[str(v) for v in rect],
                    "-n", "600", "--sort", "none")
    if code != 0:
        print("      find failed: " + text[-300:], flush=True)
        return None
    return json.loads(text)["objects"]


def shot(slot, out, name, eye, target):
    code, text = s1(slot, "cam", "shot", "--eye", *[str(v) for v in eye], "--target", *[str(v) for v in target],
                    "--fov", "60", "--hud", "hide", "--settle", "10")
    try:
        path = json.loads(text)["path"]
    except ValueError:
        report(False, "shot " + name, text[-300:])
        return None
    dst = os.path.join(out, name + ".png")
    shutil.copyfile(path, dst)
    print("      shot %s" % dst, flush=True)
    return dst


def lower_phone(slot):
    sys.path.insert(0, os.path.dirname(S1))
    from s1 import Game
    try:
        Game(slot).call("phone", {"open": False})
    except Exception as e:  # a raised phone covers the middle of every view; the views still run
        print("      phone not lowered: %s" % e, flush=True)


# ---------------------------------------------------------------- props

def clusters(objects):
    """Prop renderers grouped per copy name into clusters of overlapping x/z boxes."""
    by_name = {}
    for o in objects:
        path = o.get("path", "")
        if "based_storage_upper/Copies/" not in path:
            continue
        name = path.split("based_storage_upper/Copies/")[1].split("/")[0]
        if name.startswith(STRUCTURE):
            continue
        b = o["bounds"]
        by_name.setdefault(name, []).append([list(b["min"]), list(b["max"])])
    result = []
    for name, boxes in by_name.items():
        groups = []
        for mn, mx in boxes:
            hit = [g for g in groups if mn[0] <= g[1][0] + .01 and mx[0] >= g[0][0] - .01
                   and mn[2] <= g[1][2] + .01 and mx[2] >= g[0][2] - .01]
            merged = [mn[:], mx[:]]
            for g in hit:
                groups.remove(g)
                merged = [[min(a, c) for a, c in zip(merged[0], g[0])], [max(a, c) for a, c in zip(merged[1], g[1])]]
            groups.append(merged)
        result += [(name, g[0], g[1]) for g in groups]
    return result


def fmt(mn, mx):
    return "x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f" % (mn[0], mx[0], mn[1], mx[1], mn[2], mx[2])


def overlap(a, b, margin=.02):
    """True when the x/z boxes of a and b ((min, max) pairs) overlap by more than margin on both axes."""
    return a[1][0] - margin > b[0][0] and b[1][0] - margin > a[0][0] \
        and a[1][2] - margin > b[0][2] and b[1][2] - margin > a[0][2]


def check_props(slot):
    for stage, name, rect, rooms in PROP_STAGES:
        if not phase(slot, name):
            continue
        objects = find(slot, rect)
        if objects is None:
            report(False, stage + " props found")
            continue
        props = clusters(objects)
        report(len(props) > 0, stage + " props found", "%d props" % len(props))
        boxes = [(n, mn, mx) for n, mn, mx in props]
        for n, mn, mx in boxes:
            label = "%s %s at %.2f,%.2f" % (stage, n, (mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2)
            room = next((r for r, (x0, x1, z0, z1) in rooms.items()
                         if mn[0] >= x0 - .005 and mx[0] <= x1 + .005 and mn[2] >= z0 - .005 and mx[2] <= z1 + .005), None)
            report(room is not None, label + " inside the walls", fmt(mn, mx) + ("" if room is None else ", " + room))
            on_floor = abs(mn[1] - UPPER) <= .03
            under = [m for m, a, b in boxes if (m, a, b) != (n, mn, mx) and overlap((mn, mx), (a, b), 0)
                     and abs(b[1] - mn[1]) <= .04]
            report(on_floor or bool(under), label + " stands on the floor or a prop",
                   "bottom %.2f%s" % (mn[1], (" on " + under[0]) if under and not on_floor else ""))
            if "Bucket" in n:
                report(mx[1] - mn[1] >= BUCKET_UPRIGHT, label + " bucket upright", "height %.2f" % (mx[1] - mn[1]))
        for i, (n, mn, mx) in enumerate(boxes):
            for m, a, b in boxes[i + 1:]:
                if not overlap((mn, mx), (a, b)):
                    continue
                stacked = abs(mn[1] - b[1]) <= .04 or abs(a[1] - mx[1]) <= .04
                if not stacked:
                    report(False, "%s %s clear of %s" % (stage, n, m), fmt(mn, mx) + " | " + fmt(a, b))


# ---------------------------------------------------------------- nav

V = r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)"


def check_nav(slot, out):
    if not phase(slot, "ray.storageunit-loft:complete"):
        return
    for z in (100.5, 101.5, 102.5, 103.5, 104.3):
        text = run(slot, r"mxnav sample .* Employee", "mxnav sample -9.84 0 %g" % z)
        hits = re.findall(r"mxnav sample " + V + r" (Humanoid|Ignore Costs|Employee): " + V, text)
        if not hits:
            report(True, "nothing walkable under the stair at z %g" % z, "no navmesh near")
            continue
        bad = [(agent, float(x), float(y)) for _, _, _, agent, x, y, hz in hits
               if STAIR_X[0] <= float(x) <= STAIR_X[1] and float(y) < stair_y(float(hz)) - .3]
        report(not bad, "nothing walkable under the stair at z %g" % z,
               ", ".join("%s (%s, %s, %s)" % (a, x, y, hz) for _, _, _, a, x, y, hz in hits))
    # Under the stair a path either finds no navmesh at its end (PathInvalid) or goes round; a straight one is wrong.
    text = run(slot, r"mxnav path", "mxnav path -9.84 0 99 -9.84 0 104.8")
    m = re.search(r"status=(\w+) corners=(\d+) length=([\d.]+) m end=", text)
    if not m:
        report(False, "no straight path under the stair", text[-300:])
    else:
        straight = m.group(1) == "PathComplete" and int(m.group(2)) == 2 and float(m.group(3)) <= 6.5
        report(not straight, "no straight path under the stair", m.group(0))
    for label, a, b in (("path to the loft door", "-9.84 0 98.4", DOOR), ("path into the loft", "-9.84 0 98.4", (-5.5, 3.25, 109))):
        text = run(slot, r"mxnav path", "mxnav path %s %g %g %g" % (a, *b))
        m = re.search(r"status=(\w+) corners=(\d+) length=([\d.]+) m end=" + V, text)
        ok = bool(m) and m.group(1) == "PathComplete" and abs(float(m.group(4)) - b[0]) + abs(float(m.group(6)) - b[2]) < .8 \
            and abs(float(m.group(5)) - b[1]) < .5
        report(ok, label, m.group(0) if m else text[-300:])
    walk(slot, out)


def npc_centres(slot, rect):
    """Collider centre of every NPC in rect, by the NPC's object name."""
    code, text = s1(slot, "find", "Pickpocket Interaction", "--kind", "collider", "--rect", *[str(v) for v in rect],
                    "-n", "300", "--sort", "none")
    if code != 0:
        print("      npc find failed: " + text[-300:], flush=True)
        return {}
    found = {}
    for o in json.loads(text)["objects"]:
        parts = o["path"].split("/")
        who = parts[parts.index("Avatar") - 1] if "Avatar" in parts else parts[0]
        found[who] = [round(v, 2) for v in o["bounds"]["center"]]
    return found


def walk(slot, out):
    """Hires a handler at the Storage Unit when it has no employee, stands next to the first employee and sends it to
    the loft door, and samples every NPC on and around the stair while it walks."""
    s1(slot, "time", "1200")
    text = run(slot, r"baseddiag: staff", "baseddiag site=storageunit staff")
    if not re.search(r"baseddiag: employee ", text):
        run(slot, r"Adding employee", "addemployee %s storageunit" % EMPLOYEE)
        time.sleep(3)
        text = run(slot, r"baseddiag: staff", "baseddiag site=storageunit staff")
    # A new employee walks to its waiting place first and sets that destination again until it is there, which would
    # end any walk sent before. The walk starts once it waits.
    began = time.monotonic()
    while True:
        m = re.search(r"baseddiag: employee (.+?) \(\w+\) index \d+ at " + V + r", waits at " + V, text)
        if not m:
            report(False, "employee walks to the loft door", "no employee at the Storage Unit: " + text[-300:])
            return
        at, waits = [float(v) for v in m.group(2, 3, 4)], [float(v) for v in m.group(5, 6, 7)]
        if math.dist(at[::2], waits[::2]) < 1 or time.monotonic() - began > 120:
            break
        time.sleep(3)
        text = run(slot, r"baseddiag: staff", "baseddiag site=storageunit staff")
    name = m.group(1)
    print("      %s is %.1f m from its waiting place after %.0f s" % (name, math.dist(at[::2], waits[::2]),
                                                                       time.monotonic() - began), flush=True)
    print("      walking %s from %s" % (name, at), flush=True)
    s1(slot, "tp", "%g" % at[0], "%g" % (at[1] + .1), "%g" % (at[2] + .6))
    code, text = s1(slot, "log", "--grep", "mxnav walk", "-n", "400")
    seen = set(text.splitlines()) if code == 0 else set()
    text = run(slot, r"mxnav walk", "mxnav walk nearest %g %g %g" % DOOR, max_wait=15000)
    who = re.search(r"mxnav walk: (\S+) from", text)
    if not who:
        report(False, "employee walks to the loft door", "walk did not start: " + text[-300:])
        return
    npc = who.group(1)
    if npc.replace("_", " ") != name.lower():
        report(False, "employee walks to the loft door", "the nearest NPC was %s, not %s" % (npc, name))
        return
    rows, result, began = [], None, time.monotonic()
    while time.monotonic() - began < 180 and result is None:
        for other, c in npc_centres(slot, (-12, 96, -8, 106.5)).items():
            rows.append((round(time.monotonic() - began, 1), other, *c))
        code, text = s1(slot, "log", "--grep", "mxnav walk", "-n", "400")
        for line in [l for l in text.splitlines() if l not in seen] if code == 0 else []:
            # A new walk ends the previous NPC's watch; that line comes within the first seconds.
            if (npc in line and "arrived after" in line) or ("watch ended without arrival" in line
                                                            and time.monotonic() - began > 5):
                result = line.strip()
            elif npc in line and "warped" in line:
                result = line.strip()
        time.sleep(.3)
    arrived = bool(result) and "arrived after" in result and "warps 0" in result
    steps = re.search(r"largest step ([\d.]+) m", result or "")
    report(arrived and steps is not None and float(steps.group(1)) < 1,
           "employee walks up the stair to the loft door without a warp", result or "no result in 180 s")
    with open(os.path.join(out, "stair-walk.csv"), "w", encoding="utf-8") as f:
        f.write("t,npc,x,y,z\n" + "".join("%s,%s,%s,%s,%s\n" % r for r in rows))
    # Collider centre above the feet: from samples on the ground south of the stair or on the landing.
    offsets = [y for _, _, x, y, z in rows if z < 99.3] + [y - UPPER for _, _, x, y, z in rows if z > 105.2 and x > -10.6]
    if not offsets:
        report(False, "NPC height on the stair", "no sample off the stair to measure the collider height")
        return
    offset = statistics.median(offsets)
    on_stair = [(t, who, x, y - offset, z) for t, who, x, y, z in rows
                if STAIR_X[0] <= x <= STAIR_X[1] and 100.5 <= z <= 104.5]
    sunk = [r for r in on_stair if r[3] < stair_y(r[4]) - .3]
    report(bool(on_stair) and not sunk, "NPC feet on the steps, never below them",
           "%d samples on the stair, %d below, collider %.2f m above the feet%s" % (
               len(on_stair), len(sunk), offset,
               "" if not sunk else "; first " + "%s at (%.2f, %.2f, %.2f)" % (sunk[0][1], sunk[0][2], sunk[0][3], sunk[0][4])))


# ---------------------------------------------------------------- seams

def slits(path, y0, y1, span=(0, 1), ratio=.6, reach=40, min_width=4):
    """Dark vertical slits in a wall band: runs of columns darker than ratio x the median of the columns around them,
    starting inside span (share of the image width). The corrugation's grooves stay above 0.65; brick between two
    panels was 0.46 to 0.56."""
    im = Image.open(path).convert("L")
    w, h = im.size
    px = im.load()
    cols = [sum(px[x, y] for y in range(y0, y1)) / (y1 - y0) for x in range(w)]
    runs, start, low = [], None, 1.0
    for x in range(w):
        near = cols[max(0, x - reach):max(0, x - 4)] + cols[x + 5:min(w, x + reach)]
        r = cols[x] / max(1.0, statistics.median(near)) if near else 1.0
        if r < ratio:
            if start is None:
                start, low = x, r
            low = min(low, r)
        elif start is not None:
            if x - start >= min_width and span[0] * w <= start <= span[1] * w:
                runs.append((start, x - 1, round(low, 2)))
            start = None
    return runs


def hairlines(path, x0, x1, y0, y1, ratio=.55, side=8, share=.3):
    """Dark lines one to three pixels wide inside a window: per row, a pixel darker than ratio x both its neighbours
    (median of 7 pixels each side). A column counts when it or a column beside it is dark in share of the rows; the
    corrugation's grooves hit in under 0.2 of the rows, a bare panel end in 0.7. Edges of a dark post do not count,
    because one side of them is dark too."""
    px = Image.open(path).convert("L").load()
    rows = range(y0, y1)
    hit = {x: set() for x in range(x0, x1)}
    for y in rows:
        for x in range(x0, x1):
            left = statistics.median(px[i, y] for i in range(x - side, x - 1))
            right = statistics.median(px[i, y] for i in range(x + 2, x + side + 1))
            if min(left, right) >= 12 and px[x, y] < ratio * min(left, right):
                hit[x].add(y)
    found = []
    for x in range(x0 + 1, x1 - 1):
        rows_hit = len(hit[x - 1] | hit[x] | hit[x + 1]) / len(rows)
        if rows_hit >= share:
            found.append((x, round(rows_hit, 2)))
    return found


def check_seams(slot, out):
    if not phase(slot, "ray.storageunit-south:complete"):
        return
    for room, box in COPLANAR.items():
        text = run(slot, r"mxcoplanar x .*overlapping pairs", "mxcoplanar " + box)
        m = re.search(r"(\d+) overlapping pairs, (\d+) with a MapExpansions part", text)
        pairs = [l.split("mxcoplanar ")[1][:220] for l in text.splitlines() if "mxcoplanar ours" in l]
        report(bool(m) and m.group(2) == "0", room + " has no flickering surface of ours",
               (m.group(0) if m else text[-200:]) + ("; " + " | ".join(pairs[:3]) if pairs else ""))
        # Without tangents the sheets' normal map frays the ribs of walls that face along z.
        text = run(slot, r"mxtangents x .*not readable", "mxtangents " + box)
        m = re.search(r"(\d+) renderers of ours, (\d+) without tangents", text)
        bare = [l.split("mxtangents none ")[1][:160] for l in text.splitlines() if "mxtangents none" in l]
        report(bool(m) and int(m.group(1)) > 0 and m.group(2) == "0", room + " has tangents on every mesh of ours",
               (m.group(0) if m else text[-200:]) + ("; " + " | ".join(bare[:3]) if bare else ""))
    lower_phone(slot)
    s1(slot, "time", "1300")
    for name, eye, target, span in SEAM_VIEWS:
        path = shot(slot, out, name, eye, target)
        if path:
            found = slits(path, 560, 840, span)
            report(not found, name + " without dark slits", str(found) if found else "")
    for name, eye, target in SEAM_CLOSEUPS:
        shot(slot, out, name, eye, target)
    s1(slot, "cam", "release")


def check_hairlines(slot, out):
    current = None
    for name, level, eye, target, window, ratio in HAIRLINE_VIEWS:
        if level != current:
            if not phase(slot, level):
                return
            current = level
            lower_phone(slot)
            s1(slot, "time", "2300")
        path = shot(slot, out, name, eye, target)
        if path:
            found = hairlines(path, *window, ratio=ratio)
            report(not found, name + " without a hairline at the corner", str(found[:6]) if found else "")
    s1(slot, "cam", "release")
    s1(slot, "time", "1300")


# ---------------------------------------------------------------- lights

LIGHT = re.compile(r"mxlight near '([^']*)' (\w+) at " + V + r": active (\w+), enabled (\w+), intensity ([\d.]+), range ([\d.]+)")


def bars(slot, room, probe):
    x0, x1, z0, z1 = ROOMS[room]
    text = run(slot, r"mxlight", "mxlight %g %g %g 4" % probe)
    found = []
    for m in LIGHT.finditer(text):
        path, kind, x, y, z = m.group(1), m.group(2), float(m.group(3)), float(m.group(4)), float(m.group(5))
        if "based_storage_upper" in path and "FluroBar" in path and kind == "Spot" and x0 <= x <= x1 and z0 <= z <= z1:
            found.append((x, z, m.group(6) == "True" and m.group(7) == "True", float(m.group(8)), float(m.group(9))))
    return found


def floor_light(path):
    """Linear light of the floor field: rows 30 to 50 % of the image, middle third. The rows below can hold a customer's
    dialogue box, which covers the floor straight under the bar."""
    im = Image.open(path).convert("L")
    w, h = im.size
    px = im.crop((w // 3, int(h * .3), 2 * w // 3, int(h * .5))).getdata()
    return sum((p / 255) ** 2.2 for p in px) / len(px)


def floor_view(bar, floor):
    """The same camera for every bar: 1.65 m over the floor, 1.6 m south of the point under the bar, looking at it."""
    (x, z) = bar
    return (x, floor + 1.65, z - 1.6), (x, floor, z)


def switch(slot, position, state):
    text = run(slot, r"mxswitch clicked|mxswitch .*no switch|mxswitch \d+ switches", "mxswitch %s 1 %s" % (position, state))
    if "clicked" not in text:
        print("      mxswitch %s %s: %s" % (position, state, text[-200:]), flush=True)


def floor_pair(slot, out, name, position, bar, floor, spot):
    """Floor shots with the switch on and off, ending on; returns the linear on/off ratio or None."""
    s1(slot, "tp", *[str(v) for v in spot])
    lower_phone(slot)
    eye, target = floor_view(bar, floor)
    on_shot = shot(slot, out, "light-%s-on" % name, eye, target)
    switch(slot, position, "off")
    off_shot = shot(slot, out, "light-%s-off" % name, eye, target)
    switch(slot, position, "on")
    if not (on_shot and off_shot):
        return None
    return floor_light(on_shot) / max(1e-6, floor_light(off_shot))


def check_lights(slot, out):
    if not phase(slot, "ray.storageunit-south:complete"):
        return
    lower_phone(slot)
    s1(slot, "time", "2300")
    name, position, bar, floor, spot = VANILLA_LIGHT
    vanilla = floor_pair(slot, out, name, position, bar, floor, spot)
    report(vanilla is not None and vanilla > 2, "vanilla Unit 1 bar lights its floor",
           "linear on/off %.2f" % vanilla if vanilla else "no shots")
    for room, probe, position, bar, floor, spot in LIGHTS:
        lit = bars(slot, room, probe)
        report(bool(lit) and all(on and i >= 1.9 and r >= 5.9 for _, _, on, i, r in lit), room + " bars on with reach and intensity",
               "; ".join("(%.2f, %.2f) on %s intensity %.2f range %.1f" % b for b in lit) or "no bar found")
        s1(slot, "tp", *[str(v) for v in spot])
        switch(slot, position, "off")
        dark = bars(slot, room, probe)
        report(bool(dark) and not any(on for _, _, on, _, _ in dark), room + " bars go out with the switch",
               "; ".join("(%.2f, %.2f) on %s" % b[:3] for b in dark) or "no bar found")
        switch(slot, position, "on")
        back = bars(slot, room, probe)
        report(bool(back) and all(on for _, _, on, _, _ in back), room + " bars on again")
        ratio = floor_pair(slot, out, room, position, bar, floor, spot)
        need = VANILLA_SHARE * vanilla if vanilla else None
        report(ratio is not None and need is not None and ratio >= need, room + " floor lit near Unit 1's",
               "linear on/off %.2f, vanilla %.2f, need %.2f" % (ratio or 0, vanilla or 0, need or 0))
    # The loft bars hang over Unit 1; with Unit 1's own bar off, its floor must stay dark whatever the loft does.
    _, unit_switch, unit_bar, unit_floor, unit_spot = VANILLA_LIGHT
    loft_switch = LIGHTS[0][2]
    s1(slot, "tp", *[str(v) for v in unit_spot])
    lower_phone(slot)
    eye, target = floor_view(unit_bar, unit_floor)
    switch(slot, unit_switch, "off")
    with_loft = shot(slot, out, "leak-unit1-loft-on", eye, target)
    switch(slot, loft_switch, "off")
    without_loft = shot(slot, out, "leak-unit1-loft-off", eye, target)
    switch(slot, loft_switch, "on")
    switch(slot, unit_switch, "on")
    if with_loft and without_loft:
        leak = floor_light(with_loft) / max(1e-6, floor_light(without_loft))
        report(leak <= LEAK_RATIO, "loft bars do not light Unit 1 through the floor", "linear loft on/off %.2f" % leak)
    s1(slot, "cam", "release")
    s1(slot, "time", "1300")


def view_middle(path):
    """Linear light and mean 8-bit value of the middle fifth of the image (40 to 60 % on both axes)."""
    im = Image.open(path).convert("L")
    w, h = im.size
    px = list(im.crop((int(w * .4), int(h * .4), int(w * .6), int(h * .6))).getdata())
    return sum((p / 255) ** 2.2 for p in px) / len(px), sum(px) / len(px)


def check_leak(slot, out):
    """The copy bars cast no shadows, so walls and floors do not stop their light. With all of them on against all off,
    at night, the outside faces of the rooms and the rooms below must not change. Each view is shot twice and the
    second shot counts, so the camera has come to rest; a second pass with the bars on shows the noise."""
    if not phase(slot, "ray.storageunit-south:complete"):
        return
    lower_phone(slot)
    s1(slot, "time", "2300")
    s1(slot, "tp", *[str(v) for v in LIGHTS[0][5]])
    lower_phone(slot)
    shots = {}
    for state in ("on", "off", "on-again"):
        for _, _, position, _, _, _ in LIGHTS:
            switch(slot, position, "off" if state == "off" else "on")
        for name, eye, target in LEAK_VIEWS:
            shot(slot, out, "leak-%s-settle" % name, eye, target)
            shots.setdefault(name, {})[state] = shot(slot, out, "leak-%s-%s" % (name, state), eye, target)
    for name, eye, target in LEAK_VIEWS:
        got = shots[name]
        if not all(got.get(k) for k in ("on", "off", "on-again")):
            report(False, "no light through the walls: " + name, "no shots")
            continue
        (on, on8), (off, off8), (again, again8) = (view_middle(got[k]) for k in ("on", "off", "on-again"))
        ratio = on / max(1e-9, off)
        steps = on8 - off8
        report(ratio <= OUTSIDE_RATIO or steps < 1, "no light through the walls: " + name,
               "middle linear on/off %.3f (on %.5f, off %.5f, on again %.5f), 8-bit on-off %+.2f, on again-off %+.2f"
               % (ratio, on, off, again, steps, again8 - off8))
    s1(slot, "cam", "release")
    s1(slot, "time", "1300")


def check_staff(slot):
    """The employee limit rises with each finished room. After the rooms finish one by one, every place for an employee
    has a spot of its own."""
    for name in ("ray.storageunit-loft:complete", "ray.storageunit-east:complete", "ray.storageunit-south:complete"):
        if not phase(slot, name):
            return
    text = run(slot, r"baseddiag: staff", "baseddiag site=storageunit staff")
    head = re.search(r"baseddiag: staff \d+/(\d+), places (\d+)", text)
    places = re.findall(r"baseddiag: place (\d+) at " + V, text)
    if not head or not places:
        report(False, "every employee place has its own spot", "no staff output: " + text[-300:])
        return
    spots = {}
    for i, x, y, z in places:
        spots.setdefault((round(float(x), 2), round(float(y), 2), round(float(z), 2)), []).append(int(i))
    shared = {k: v for k, v in spots.items() if len(v) > 1}
    report(not shared and int(head.group(2)) == int(head.group(1)) == len(places),
           "every employee place has its own spot",
           "limit %s, places %s, %d spots%s" % (head.group(1), head.group(2), len(spots),
                                                 "" if not shared else ", shared: %s" % shared))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--only", default="props,nav,seams,hairlines,lights,leak,staff")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)
    only = set(a.only.split(","))
    if "props" in only:
        check_props(a.slot)
    if "nav" in only:
        check_nav(a.slot, a.out)
    if "seams" in only:
        check_seams(a.slot, a.out)
    if "hairlines" in only:
        check_hairlines(a.slot, a.out)
    if "lights" in only:
        check_lights(a.slot, a.out)
    if "leak" in only:
        check_leak(a.slot, a.out)
    if "staff" in only:
        check_staff(a.slot)
    print("RESULT %s: %d failed" % ("PASS" if failed == 0 else "FAIL", failed), flush=True)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
