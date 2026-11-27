"""
In-game checks of the staff pack: Based#128 (Sweatshop graffiti spot), Based#129 (Bungalow employees under the new
stair) and Based#131 (number of the Bungalow's second loading bay).

  sweatshop  Save with the Sweatshop wing finished (template `mrmonkey-dock`). The game's graffiti spot of the old
             bay's south wall sits on the outer face of the new south facade, is active and in view from the yard.
  bungalow   Save with the Bungalow owned (template `played`). Finishes the storey and the carport, then:
             no waiting place lies under the landing or the stair, every place has a route from the street, hired
             employees reach their places and stand clear of the gap under the stair; the second bay's marking reads 2
             and the first keeps 1.

Needs Debug builds of Based and MapExpansions in a running test slot:

    python _wt-Based-<topic>/Tests/staff-pack.py --slot mptest --part sweatshop
    python _wt-Based-<topic>/Tests/staff-pack.py --slot mptest --part bungalow [--shots <folder>]
"""
import argparse
import math
import os
import re
import shutil
import subprocess
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[staff-pack] Workspace/tools/bridge/s1.py not found")


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

V = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"
RADIUS = .25

# Sweatshop: the spot and the face it belongs on.
SWEAT_SPOT = "SpraySurface (45)"
SWEAT_RECT = [-66.0, 128.0, -50.0, 146.0]
SWEAT_FACADE_Z = 135.88

# Bungalow: landing and stair footprint (x0, z0, x1, z1) and the gap under them no place may stand in.
UNDER_STAIR = (-169.85, 111.35, -162.46, 112.85)
EMPLOYEE_TYPES = ["botanist", "handler", "chemist", "cleaner"]


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.slot = slot
        self.results = []

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        failed = [r for r in self.results if r[1] != "PASS"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(failed), len(failed)))
        for name, verdict, detail in failed:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if failed else 0

    def cmd(self, command, wait=1.0):
        result = self.g.run(command, max_wait_ms=int(max(wait, 1.0) * 1000) + 9000)
        lines = (result.get("result") or {}).get("lines") or [] if isinstance(result, dict) else []
        return [str(l.get("text", "")) if isinstance(l, dict) else str(l) for l in lines]

    def surfaces(self, rect):
        res = self.g.world("find_world_objects", {"under": "SpraySurface", "rect": rect, "kind": "both", "limit": 200}) or {}
        found = {}
        for o in (res.get("result") or res).get("objects") or []:
            b = o.get("bounds") or {}
            c, s = b.get("center"), b.get("size") or [0, 0, 0]
            path = re.sub(r"/IntObj$", "", o.get("path") or o.get("name") or "?")
            if c is None or (path in found and found[path][3]):
                continue
            found[path] = (path, tuple(float(v) for v in c), tuple(float(v) for v in s), bool(o.get("active", True)))
        return list(found.values())

    def sight(self, eye, target):
        r = self.g.world("line_of_sight", {"eye": list(eye), "target": list(target), "targetRadius": 0.6}) or {}
        res = r.get("result") or r
        blockers = res.get("blockers") or []
        return res.get("status") or "no answer: %s" % str(r)[:200], (blockers[0].get("path") if blockers else None)

    def shot(self, eye, target, name, shots):
        """Free-camera picture without the HUD; returns the copied file or None."""
        if not shots:
            return None
        out = subprocess.run([sys.executable, os.path.join(_bridge(), "s1.py"), "--slot", self.slot, "cam", "shot",
                              "--eye", *map(str, eye), "--target", *map(str, target), "--hud", "hide", "--sight", "off"],
                             capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
        m = re.search(r'"path": "(.+?)"', out.stdout)
        path = m.group(1).replace("\\\\", "\\") if m else None
        if path and os.path.isfile(path):
            dest = os.path.join(shots, name + ".png")
            shutil.copy(path, dest)
            return dest
        print("  shot %s failed: %s" % (name, (out.stdout + out.stderr)[-200:]), flush=True)
        return None

    def reload(self):
        self.g.call("return_to_menu", {}, timeout_ms=20000)
        time.sleep(8)
        for _ in range(6):
            r = self.g.call("load_save", {"slot": 5}, timeout_ms=20000)
            if isinstance(r, dict) and (r.get("result") or {}).get("loading"):
                break
            time.sleep(5)
        self.g.wait_ready(timeout=300, say=lambda s: None)
        time.sleep(3)

    def staff(self, site):
        log = "\n".join(self.cmd("baseddiag site=%s staff" % site, wait=2.0))
        head = None
        for m in re.finditer(r"baseddiag: staff (\d+)/(\d+), places (\d+)", log):
            head = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
        tail = log[log.rfind("baseddiag: staff "):] if head else ""
        places = {}
        # Builds before the staff pack name no object; the place then shows as "?".
        for m in re.finditer(r"baseddiag: place (\d+) at " + V + r" route (\w+), mesh ([\d.]+) m away[^\n]*?(?:object ([^\n]+))?$", tail, re.M):
            places[int(m.group(1))] = ((float(m.group(2)), float(m.group(4))), m.group(5), float(m.group(6)), (m.group(7) or "?").strip())
        for m in re.finditer(r"baseddiag: place (\d+) at " + V + r" route none", tail):
            places[int(m.group(1))] = ((float(m.group(2)), float(m.group(4))), "none", 99.0, "?")
        employees = []
        for m in re.finditer(r"baseddiag: employee (.+?) index (\d+) at " + V + r", waits at " + V + r"[^\n]*?behaviour ([^,\n]+)", tail):
            employees.append((m.group(1), int(m.group(2)), (float(m.group(3)), float(m.group(5))), (float(m.group(6)), float(m.group(8))),
                              m.group(9).strip()))
        return head, places, employees

    def markings(self):
        """Material names of the Bungalow's dock decals, per bay index."""
        out = {}
        for line in self.cmd("baseddiag dock inspect bungalow", wait=2.0):
            m = re.search(r"dock marking bungalow\[(\d+)\] decal '(.*?)' material '(.*?)'", line)
            if m:
                out.setdefault(int(m.group(1)), []).append(m.group(3))
        return out

    def phase(self, command):
        log = self.cmd(command, wait=10.0)
        self.check(command, any("construction moved to" in l or "nothing to" in l or "are complete" in l for l in log),
                   next((l for l in reversed(log) if "baseddiag" in l), "no baseddiag line"))


def waiting(employee):
    """The game's idle behaviour is named "Wait ouside" (sic)."""
    return employee[4].lower().startswith("wait")


def inside(p, rect, margin):
    x0, z0, x1, z1 = rect
    return x0 - margin < p[0] < x1 + margin and z0 - margin < p[1] < z1 + margin


def sweatshop(r, shots):
    found = [f for f in r.surfaces(SWEAT_RECT) if f[0].endswith(SWEAT_SPOT)]
    for f in found:
        print("  %s at (%.2f, %.2f, %.2f), size (%.2f, %.2f, %.2f), active %s" % ((f[0],) + f[1] + f[2] + (f[3],)), flush=True)
    active = [f for f in found if f[3]]
    if not r.check("Based#128: the Sweatshop's south graffiti spot is there and active", bool(active), "%d found" % len(found)):
        return
    c = active[0][1]
    # The spot's box is 0.4 m deep and stands proud of its wall; its back lies on the facade.
    back = c[2] + active[0][2][2] / 2
    r.check("Based#128: the spot sits on the new south facade", abs(back - SWEAT_FACADE_Z) < .25 and -60.1 < c[0] < -53.9,
            "centre (%.2f, %.2f, %.2f), back face at z %.2f, facade at z %.2f" % (c + (back, SWEAT_FACADE_Z)))
    eye = (c[0], c[1] + .4, c[2] - 6.0)
    state, hit = r.sight(eye, c)
    r.check("Based#128: the spot is in view from the yard to the south", state in ("clear", "moved"),
            "%s%s" % (state, (", hit " + str(hit)) if hit else ""))
    print("  shot: %s" % (r.shot(eye, c, "sweatshop-graffiti", shots) or "none"), flush=True)


def bungalow(r, shots):
    r.g.world("set_time", {"time": 800})
    r.phase("baseddiag site=bungalow phase complete")
    r.phase("baseddiag site=bungalow phase complete storey")
    r.phase("baseddiag site=bungalow phase complete carport")
    time.sleep(4)

    head, places, _ = r.staff("bungalow")
    if not r.check("Bungalow staff diagnostics answered", head is not None and bool(places), head):
        return
    for i, (p, route, mesh, obj) in sorted(places.items()):
        print("  place %d at (%.2f, %.2f), %s, %s" % (i, p[0], p[1], route, obj), flush=True)
        r.check("Based#129: place %d (%s) is not under the landing or the stair" % (i, obj), not inside(p, UNDER_STAIR, RADIUS),
                "(%.2f, %.2f)" % p)
        r.check("Based#129: place %d has a route from the street" % i, route == "PathComplete" and mesh < .3,
                "route %s, mesh %.2f m away" % (route, mesh))

    count, limit, _ = head
    for i in range(count, limit):
        r.cmd("addemployee %s bungalow" % EMPLOYEE_TYPES[i % len(EMPLOYEE_TYPES)], wait=3.0)
        time.sleep(3)
    deadline, employees = time.time() + 150, []
    while time.time() < deadline:
        time.sleep(10)
        head, places, employees = r.staff("bungalow")
        if head and head[0] == head[1] and employees and all(math.dist(e[2], e[3]) < 1.0 for e in employees if waiting(e)):
            break
    r.check("Based#129: every Bungalow employee was hired", head is not None and head[0] == head[1] == len(employees), head)
    for name, index, at, waits, behaviour in employees:
        # Only an idle employee walks to its waiting place; one at work stands at its station.
        if waiting((name, index, at, waits, behaviour)):
            r.check("Based#129: %s (index %d) stands at its place" % (name, index), math.dist(at, waits) < 1.0, "at %s, place %s" % (at, waits))
        else:
            print("SKIP  Based#129: %s (index %d) is at work (%s), place not checked - at %s, place %s"
                  % (name, index, behaviour, at, waits), flush=True)
        r.check("Based#129: %s stands clear of the gap under the stair" % name, not inside(at, UNDER_STAIR, 0), "at (%.2f, %.2f)" % at)

    marks = r.markings()
    print("  markings: %s" % marks, flush=True)
    first, second = marks.get(0, []), marks.get(1, [])
    r.check("Based#131: the first bay keeps its 1", any(m.startswith("Loading dock decal mat 1") for m in first), first)
    r.check("Based#131: the second bay reads 2", any(m.startswith("Loading dock decal mat 2") for m in second)
            and not any(m.startswith("Loading dock decal mat 1") for m in second), second)
    r.g.world("set_time", {"time": 1200})
    # Under the carport roof from the east: both bays' markings side by side.
    print("  shot bays: %s" % (r.shot((-159.5, -2.4, 107.7), (-165.8, -4.0, 107.7), "bungalow-bays", shots) or "none"), flush=True)
    print("  shot stair: %s" % (r.shot((-163.0, -3.0, 111.0), (-168.5, -3.6, 111.6), "bungalow-under-stair", shots) or "none"), flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--part", choices=["sweatshop", "bungalow"], required=True)
    ap.add_argument("--shots", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "staff-pack"))
    args = ap.parse_args()
    r = Run(args.slot)
    if args.shots:
        os.makedirs(args.shots, exist_ok=True)
    r.g.world("set_time", {"time": 1200})
    if args.part == "sweatshop":
        sweatshop(r, args.shots)
    else:
        bungalow(r, args.shots)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
