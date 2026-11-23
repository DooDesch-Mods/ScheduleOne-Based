"""
In-game check of Based#111: the vanilla graffiti spot on the Bungalow roof (a WorldSpraySurface) after each of the
four Bungalow expansions.

For the plain Bungalow and then for each expansion, built alone on a fresh load of the test save, this finds the
spray surfaces around the Bungalow, checks that the roof spot is still there and active, checks a clear line of sight
to it from above and from the street side, and takes one picture of it. An expansion that removes, hides or covers
the spot shows up as a FAIL with the reason.

Needs a Debug build of Based (and MapExpansions) in a running test slot and save 5 with the Bungalow owned
(template `played`).

    python _wt-Based-<topic>/Tests/bungalow-graffiti.py [--slot mptest] [--only plain,storey,sunroom,carport,deck]
        [--shots <folder>]
"""
import argparse
import os
import re
import shutil
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[bungalow-graffiti] Workspace/tools/bridge/s1.py not found")


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

# The Bungalow and its yard, with room for the carport and the deck.
RECT = [-200.0, 95.0, -150.0, 135.0]
EXPANSIONS = {
    "plain": [],
    "storey": ["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete storey"],
    "sunroom": ["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete sunroom"],
    "carport": ["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete carport"],
    "deck": ["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete deck"],
}


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
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

    def surfaces(self):
        """Every spray surface in the rect: (name, centre, size, active)."""
        # A spot is a 'SpraySurface (n)' object under @Managers/@Graffiti; its collider sits on a child 'IntObj', and
        # find_world_objects matches names on the renderer or collider itself, so the search goes by the parent.
        res = self.g.world("find_world_objects", {"under": "SpraySurface", "rect": RECT, "kind": "both", "limit": 200}) or {}
        found = {}
        for o in (res.get("result") or res).get("objects") or []:
            b = o.get("bounds") or {}
            c, s = b.get("center"), b.get("size") or [0, 0, 0]
            path = re.sub(r"/IntObj$", "", o.get("path") or o.get("name") or "?")
            # One entry per surface: its renderer and its collider both show up.
            if c is None or (path in found and found[path][3]):
                continue
            found[path] = (path, tuple(float(v) for v in c), tuple(float(v) for v in s), bool(o.get("active", True)))
        return list(found.values())

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

    def sight(self, eye, target):
        r = self.g.world("line_of_sight", {"eye": list(eye), "target": list(target), "targetRadius": 0.6}) or {}
        res = r.get("result") or r
        blockers = res.get("blockers") or []
        return res.get("status") or "no answer: %s" % str(r)[:200], (blockers[0].get("path") if blockers else None)

    def shot(self, eye, target, name, shots):
        if not shots:
            return None
        r = self.g.world("camera_shot", {"eye": list(eye), "target": list(target), "settleFrames": 20}) or {}
        path = (r.get("result") or r).get("path")
        if path and os.path.isfile(path):
            out = os.path.join(shots, name + ".png")
            shutil.copy(path, out)
            return out
        return None


def roof_spot(found):
    """The spray surface highest above the Bungalow's ground floor: the one on the roof."""
    return max(found, key=lambda f: f[1][1]) if found else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--only", default=",".join(EXPANSIONS))
    ap.add_argument("--shots", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "graffiti"))
    args = ap.parse_args()
    r = Run(args.slot)
    if args.shots:
        os.makedirs(args.shots, exist_ok=True)
    r.g.world("set_time", {"time": 1200})
    found = r.surfaces()
    for f in found:
        print("  spray surface %s at (%.2f, %.2f, %.2f), size (%.2f, %.2f, %.2f), active %s" % ((f[0],) + f[1] + f[2] + (f[3],)))
    spot = roof_spot([f for f in found if f[3]])
    if not r.check("the Bungalow has a graffiti spot on its roof", spot is not None and spot[1][1] > -1.5,
                   "%d spray surfaces around the Bungalow%s" % (len(found), (", highest at y %.2f" % spot[1][1]) if spot else "")):
        return r.summary()
    name, centre, size = spot[0], spot[1], spot[2]
    above = (centre[0], centre[1] + 6.0, centre[2] - 0.01)
    for key in [k.strip() for k in args.only.split(",") if k.strip()]:
        # Each case starts from the save as it was written, so no build of an earlier case or run stands.
        r.reload()
        r.g.world("set_time", {"time": 1200})
        for command in EXPANSIONS[key]:
            log = r.cmd(command, wait=10.0)
            r.check("%s: %s" % (key, command), any("construction moved to" in l or "nothing to" in l or "are complete" in l for l in log),
                    next((l for l in reversed(log) if "baseddiag" in l), "no baseddiag line"))
        time.sleep(4)
        # The storey lifts the roof and moves the spot up with it, so the spot is matched by name and the checks
        # below aim at where it stands now.
        now = [f for f in r.surfaces() if f[0] == name]
        active = [f for f in now if f[3]]
        r.check("%s: the roof's graffiti spot is still there and active" % key, bool(active),
                ", ".join("%s active %s at (%.2f, %.2f, %.2f)" % ((f[0], f[3]) + f[1]) for f in now) or "gone")
        pos = active[0][1] if active else centre
        if active and max(abs(a - b) for a, b in zip(pos, centre)) > 0.05:
            print("  %s: the spot moved to (%.2f, %.2f, %.2f)" % ((key,) + pos), flush=True)
        above = (pos[0], pos[1] + 6.0, pos[2] - 0.01)
        state, hit = r.sight(above, pos)
        r.check("%s: nothing covers the spot from above" % key, state in ("clear", "moved"), "%s%s" % (state, (", hit " + str(hit)) if hit else ""))
        # From the street side, 8 m out in each horizontal direction at the spot's height plus 2 m; one clear view is enough.
        views = []
        for dx, dz in ((8, 0), (-8, 0), (0, 8), (0, -8)):
            eye = (pos[0] + dx, pos[1] + 2.0, pos[2] + dz)
            views.append((eye,) + r.sight(eye, pos))
        clear = [v for v in views if v[1] in ("clear", "moved")]
        r.check("%s: the spot is in view from at least one side" % key, bool(clear),
                "; ".join("%s %s" % (tuple(round(c, 1) for c in v[0]), v[1]) for v in views))
        eye = clear[0][0] if clear else above
        shot = r.shot(eye, pos, "graffiti-%s" % key, args.shots)
        print("  shot %s: %s" % (key, shot or "none"), flush=True)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
