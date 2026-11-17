"""
In-game check of where the Sweatshop's employees wait outside work: with the room and the loading dock finished and
every place taken, each place has its own spot at the foot of the yard stair, a route from the street, keeps clear of
the loading dock, stands at least 0.9 m from the next one, and each employee stands at its place. Then it shoots the
group from several sides.

Needs a Debug build of Based (`baseddiag`) in a running test slot whose save owns no Sweatshop employees yet:

    pwsh -File Workspace/tools/session/session.ps1 -Build MapExpansions,_wt-Based-<topic> -NoApp
    python _wt-Based-<topic>/tests/sweatshop-idle-points.py --slot mptest2 --out _drafts/based-idlepoints/sweatshop
"""
import argparse
import importlib.util
import math
import os
import sys
import time

spec = importlib.util.spec_from_file_location("storage", os.path.join(os.path.dirname(__file__), "storageunit-idle-points.py"))
storage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(storage)
check, s1, run, results = storage.check, storage.s1, storage.run, storage.results

storage.SITE = "site=ming.sweatshop"
CODE = "sweatshop"
# The delivery van's spot of the loading dock, from the outline the dock's world state draws.
KEEP_OUT = {
    "loading dock": (-58.26, 147.0, -53.76, 150.0),
}
storage.VIEWS = [
    ("from-west", (-68.5, -2.2, 144.2), (-63.2, -3.2, 144.0)),
    ("from-east", (-58.5, -2.0, 146.5), (-63.2, -3.2, 144.0)),
    ("from-north-high", (-62.0, 1.2, 150.0), (-63.2, -3.4, 144.0)),
    ("overview", (-70.0, 2.5, 150.0), (-62.0, -4.0, 142.0)),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--time", default="1300", help="time of day for the views, HHMM")
    ap.add_argument("--no-jump", action="store_true", help="the save already has every build finished")
    a = ap.parse_args()

    if not a.no_jump:
        s1(a.slot, "time", "0800")
        text = run(a.slot, "construction moved to|baseddiag: .*(not|no |unknown)", "baseddiag %s phase ming.loading-dock:complete" % storage.SITE, 90000)
        check("every build finished", "construction moved to" in text, text[-200:])

    head, places, employees = storage.staff(a.slot)
    if not check("staff diagnostics answered", head is not None and places, head):
        return 1
    count, limit, total = head
    check("one place per employee", total == limit == 3, "limit %d, places %d" % (limit, total))
    for i in range(count, limit):
        kind = storage.EMPLOYEE_TYPES[i % len(storage.EMPLOYEE_TYPES)]
        run(a.slot, r"Adding employee", "addemployee %s %s" % (kind, CODE))
        time.sleep(3.0)

    deadline = time.time() + 150
    while time.time() < deadline:
        time.sleep(10)
        head, places, employees = storage.staff(a.slot)
        if head and head[0] == head[1] and employees and all(math.dist(e[2], e[3]) < 1.0 for e in employees):
            break
    check("every place is taken", head is not None and head[0] == head[1] == len(employees), head)

    spots = {}
    for i, (at, route, mesh, _) in sorted(places.items()):
        p = (at[0], at[2])
        spots.setdefault((round(p[0], 2), round(p[1], 2)), []).append(i)
        check("place %d reachable from the street" % i, route == "PathComplete" and mesh < .3, "%s route %s mesh %.2f" % (p, route, mesh))
        hits = [n for n, r in KEEP_OUT.items() if storage.inside(p, r, storage.RADIUS)]
        check("place %d keeps clear" % i, not hits, "%s in %s" % (p, hits) if hits else str(p))
    check("every place has its own spot", all(len(v) == 1 for v in spots.values()), spots)
    pts = [(at[0], at[2]) for at, _, _, _ in places.values()]
    closest = min(math.dist(p, q) for i, p in enumerate(pts) for q in pts[i + 1:])
    check("places stand at least 0.9 m apart", closest >= .9, "closest %.2f m" % closest)

    for name, index, at, waits in employees:
        check("%s (index %d) stands at its place" % (name, index), math.dist(at, waits) < 1.0, "at %s, place %s" % (at, waits))

    s1(a.slot, "time", a.time)
    time.sleep(2)
    storage.shoot(a.slot, a.out)

    failed = [r for r in results if not r[1]]
    print("\n===== %d PASS, %d FAIL" % (len(results) - len(failed), len(failed)))
    for name, _, detail in failed:
        print("FAIL  %s  - %s" % (name, detail))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
