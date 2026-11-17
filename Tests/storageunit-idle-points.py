"""
In-game check of where the Storage Unit's employees wait outside work: with every build finished and every place
taken, each place lies in its group (fire barrel, stair foot, the game's own), keeps clear of the vans, the bays,
Geraldine's evening path, the bus stop, the stair and the props around the barrel, has a route from the street and
its own spot, faces what its group faces, and each employee stands at its place. Then it shoots each group from
several sides.

Employees without a locker wait outside all day, so hiring them is enough; no time jump is needed.

Needs a Debug build of Based (`baseddiag`) in a running test slot whose save owns no Storage Unit employees yet:

    pwsh -File Workspace/tools/session/session.ps1 -Build MapExpansions,_wt-Based-<topic> -NoApp
    python _wt-Based-<topic>/tests/storageunit-idle-points.py --slot mptest2 --out _drafts/based-idlepoints/after
"""
import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
SITE = "site=storageunit"
CODE = "storageunit"
V = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"
FIRE = (-12.35, 102.35)
STAIR_PAIR = [(-9.25, 97.6), (-10.6, 97.9)]
# Rectangles (x0, z0, x1, z1) no place may stand in; a place is a point, the margin is the employee's radius.
KEEP_OUT = {
    "bay 1 van": (-8.7, 97.5, -5.7, 103.0),
    "bay 2 van": (-5.45, 97.1, -2.45, 102.6),
    # Geraldine walks from the hall's mouth to her door on the Unit 3 side; the game's own place (2) stands 9 cm north
    # of her path, so this one is checked without the radius.
    "Geraldine's evening path": (-8.75, 93.6, -2.2, 96.5),
    "bus stop": (-14.0, 90.0, -11.3, 97.39),
    "stair": (-10.6, 99.2, -9.0, 106.3),
    "walk to the first step": (-10.1, 98.1, -9.6, 99.2),
    "lamp post base": (-12.5, 100.5, -11.5, 101.5),
    "bush": (-13.11, 103.08, -10.63, 105.48),
}
# Props near the places: centre and radius.
PROPS = {
    "fire barrel": (FIRE, .35),
    "trash can": ((-10.92, 102.6), .3),
    "trash drum": ((-11.05, 101.85), .35),
    "crate": ((-12.6, 99.6), .4),
}
RADIUS = .25
EMPLOYEE_TYPES = ["botanist", "handler", "chemist", "cleaner"]
# Views per group: (name, eye, target).
VIEWS = [
    ("fire-from-street", (-16.5, 1.8, 102.0), (-12.3, .8, 102.3)),
    ("fire-from-north", (-12.0, 2.6, 106.8), (-12.3, .6, 102.0)),
    ("fire-from-south", (-12.8, 1.9, 98.4), (-12.3, .8, 102.4)),
    ("stair-from-south", (-10.0, 1.8, 93.8), (-9.9, .9, 98.2)),
    ("stair-from-street", (-14.5, 1.9, 97.4), (-9.9, .8, 97.8)),
    ("stair-from-landing", (-9.9, 4.9, 104.0), (-9.9, .4, 97.8)),
    ("yard-overview", (-16.5, 6.5, 92.5), (-11.0, 0, 100.5)),
    ("hall-north", (-4.5, 2.4, 96.0), (-7.4, .7, 103.6)),
]

results = []


def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print("%s  %s%s" % ("PASS" if ok else "FAIL", name, ("  - " + str(detail)) if detail else ""), flush=True)
    return ok


def s1(slot, *args, timeout=240):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def run(slot, until, command, max_wait=60000):
    return s1(slot, "run", "--until", until, "--max-wait", str(max_wait), command)[1]


def staff(slot):
    # The command writes the head, place and employee lines in one frame; `run` prints all lines of that frame.
    log = run(slot, r"baseddiag: staff", "baseddiag %s staff" % SITE)
    head = None
    for m in re.finditer(r"baseddiag: staff (\d+)/(\d+), places (\d+)", log):
        head = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
    # Only the lines of the newest answer: everything after the last head line.
    tail = log[log.rfind("baseddiag: staff "):] if head else ""
    places = {}
    for m in re.finditer(r"baseddiag: place (\d+) at " + V + r" route (\w+), mesh ([\d.]+) m away, faces " + V, tail):
        places[int(m.group(1))] = ((float(m.group(2)), float(m.group(3)), float(m.group(4))), m.group(5), float(m.group(6)),
                                   (float(m.group(7)), float(m.group(9))))
    for m in re.finditer(r"baseddiag: place (\d+) at " + V + r" route none", tail):
        places[int(m.group(1))] = ((float(m.group(2)), float(m.group(3)), float(m.group(4))), "none", 99.0, (0.0, 0.0))
    employees = []
    for m in re.finditer(r"baseddiag: employee (.+?) index (\d+) at " + V + r", waits at " + V, tail):
        employees.append((m.group(1), int(m.group(2)), (float(m.group(3)), float(m.group(5))), (float(m.group(6)), float(m.group(8)))))
    return head, places, employees


def inside(p, rect, margin):
    x0, z0, x1, z1 = rect
    return x0 - margin < p[0] < x1 + margin and z0 - margin < p[1] < z1 + margin


def shoot(slot, out):
    os.makedirs(out, exist_ok=True)
    for name, eye, target in VIEWS:
        code, text = s1(slot, "cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target), "--hud", "hide")
        try:
            path = json.loads(text[text.index("{"):text.rindex("}") + 1])["path"]
            dest = os.path.join(out, name + ".png")
            shutil.copy(path, dest)
            check("view " + name, True, dest)
        except Exception as error:
            check("view " + name, False, "%s %s" % (error, text[-200:]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--time", default="1300", help="time of day for the views, HHMM")
    ap.add_argument("--no-jump", action="store_true", help="the save already has every build finished")
    a = ap.parse_args()

    if not a.no_jump:
        s1(a.slot, "time", "0800")
        text = run(a.slot, "construction moved to|baseddiag: .*(not|no |unknown)", "baseddiag %s phase ray.storageunit-bay:complete" % SITE, 90000)
        check("every build finished", "construction moved to" in text, text[-200:])

    head, places, employees = staff(a.slot)
    if not check("staff diagnostics answered", head is not None and places, head):
        return 1
    count, limit, total = head
    check("one place per employee", total == limit == 8, "limit %d, places %d" % (limit, total))
    for i in range(count, limit):
        kind = EMPLOYEE_TYPES[i % len(EMPLOYEE_TYPES)]
        run(a.slot, r"addemployee|Added|employee", "addemployee %s %s" % (kind, CODE))
        time.sleep(3.0)

    # Employees walk out from the street door; give them time to reach their places.
    deadline, employees = time.time() + 150, []
    while time.time() < deadline:
        time.sleep(10)
        head, places, employees = staff(a.slot)
        if head and head[0] == head[1] and employees and all(math.dist(e[2], e[3]) < 1.0 for e in employees):
            break
    check("every place is taken", head is not None and head[0] == head[1] == len(employees), head)

    spots = {}
    for i, (at, route, mesh, _) in sorted(places.items()):
        p = (at[0], at[2])
        spots.setdefault((round(p[0], 2), round(p[1], 2)), []).append(i)
        check("place %d reachable from the street" % i, route == "PathComplete" and mesh < .3, "%s route %s mesh %.2f" % (p, route, mesh))
        hits = [n for n, r in KEEP_OUT.items() if inside(p, r, 0 if n.startswith("Geraldine") else RADIUS)]
        check("place %d keeps clear" % i, not hits, "%s in %s" % (p, hits) if hits else str(p))
        props = [n for n, (c, r) in PROPS.items() if math.dist(p, c) < r + RADIUS]
        check("place %d stands off the props" % i, not props, "%s touches %s" % (p, props) if props else "")
    check("every place has its own spot", all(len(v) == 1 for v in spots.values()), spots)
    pts = [(at[0], at[2]) for at, _, _, _ in places.values()]
    closest = min(math.dist(p, q) for i, p in enumerate(pts) for q in pts[i + 1:])
    check("places stand at least 0.9 m apart", closest >= .9, "closest %.2f m" % closest)

    by_fire = [i for i, (at, _, _, _) in places.items() if math.dist((at[0], at[2]), FIRE) < 1.2]
    by_stair = [i for i, (at, _, _, _) in places.items() if any(math.dist((at[0], at[2]), q) < .1 for q in STAIR_PAIR)]
    check("three places ring the fire barrel", len(by_fire) == 3, by_fire)
    check("two places stand in front of the stair", len(by_stair) == 2, by_stair)

    def facing_error(i, towards):
        at, _, _, fwd = places[i]
        want = (towards[0] - at[0], towards[1] - at[2])
        angle = math.degrees(math.atan2(fwd[0], fwd[1]) - math.atan2(want[0], want[1]))
        return abs((angle + 180) % 360 - 180)

    for i in by_fire:
        err = facing_error(i, FIRE)
        check("place %d faces the fire" % i, err < 10, "off by %.0f deg" % err)
    if len(by_stair) == 2:
        a_, b_ = by_stair
        for me, other in ((a_, b_), (b_, a_)):
            err = facing_error(me, (places[other][0][0], places[other][0][2]))
            check("place %d faces its colleague at the stair" % me, err < 10, "off by %.0f deg" % err)

    for name, index, at, waits in employees:
        check("%s (index %d) stands at its place" % (name, index), math.dist(at, waits) < 1.0, "at %s, place %s" % (at, waits))

    _, log = s1(a.slot, "log", "--grep", "faces its own spot", "-n", "10")
    check("no place without a facing", "faces its own spot" not in log.replace("--grep faces its own spot", ""), log[-200:])

    s1(a.slot, "time", a.time)
    time.sleep(2)
    shoot(a.slot, a.out)

    failed = [r for r in results if not r[1]]
    print("\n===== %d PASS, %d FAIL" % (len(results) - len(failed), len(failed)))
    for name, _, detail in failed:
        print("FAIL  %s  - %s" % (name, detail))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
