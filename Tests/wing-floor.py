"""
In-game check of Based#112: employees who work in the upgraded Sweatshop's wing stay on its floor.

A player with a mixing setup in the wing reported employees that walked through the floor and ended up below the
Sweatshop. This builds that setup: mixing stations on the wing's tiles, worked by the site's chemist, with their output
sent to a storage rack in the old room, so the chemist walks between the old room and the wing for every batch. It runs
the work for a few in-game hours and tracks every employee four times a second (baseddiag track): a drop of more than
0.6 m in one sample is a fall, a jump of more than 2.5 m a warp, and a position inside the wing's outline more than
0.6 m below its floor counts as under the floor. Employees may leave the Sweatshop (the vanilla wait spot is on the
street, 3.75 m below), so only the wing's outline is judged. With --reload the game then saves while the chemist works
the wing, loads that save and tracks again, because a load puts every employee back where it was saved.

Needs a Debug build of Based (`baseddiag`) and MapExpansions (`mxreach`) in a running test slot whose save has an upgraded
Sweatshop with a chemist, for example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Profile main -Build MapExpansions,_wt-Based-<topic> -Template mrmonkey-dock -NoApp
    python _wt-Based-<topic>/Tests/wing-floor.py [--slot mptest] [--stations 2] [--seconds 240] [--reload 120]
"""
import argparse
import os
import re
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
    raise SystemExit("[wing-floor] Workspace/tools/bridge/s1.py not found")


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

SITE = "site=ming.sweatshop"
CODE = "sweatshop"
V = r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)"
SUMMARY = re.compile(r"baseddiag track: summary (.+?): (\d+) samples.*?warps (\d+), falls (\d+), under the wing floor (\d+),.*?y ([-\d.]+) to ([-\d.]+)")
STATION = re.compile(r"baseddiag chemist:\s+(\S.*?) at " + V + r"( in the wing)?: product (\d+), mixer (\d+), output (\d+)")


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
        out = [str(l.get("text", "")) if isinstance(l, dict) else str(l) for l in lines]
        for line in out:
            if "baseddiag" in line or "mxreach" in line:
                print("  " + line.split("] ", 2)[-1], flush=True)
        return out

    def log_since(self, mark):
        """Log lines of the running game after the line that contains mark."""
        path = os.path.join(_bridge(), "..", "..", "..", "_MPTest", "Schedule I", "MelonLoader", "Latest.log")
        if not os.path.isfile(path):
            print("  no game log at %s" % path, flush=True)
            return []
        with open(path, encoding="utf-8", errors="replace") as f:
            lines = f.read().splitlines()
        for i in range(len(lines) - 1, -1, -1):
            if mark in lines[i]:
                return lines[i + 1:]
        return lines


def stations(lines):
    return [(m.group(1), tuple(float(m.group(i)) for i in (2, 3, 4)), bool(m.group(5)), int(m.group(6)), int(m.group(7)), int(m.group(8)))
            for m in (STATION.search(l) for l in lines) if m]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--stations", type=int, default=2)
    ap.add_argument("--seconds", type=int, default=240)
    ap.add_argument("--reload", type=int, default=0, help="after the work, save, load and track again for this many seconds")
    args = ap.parse_args()
    run = Run(args.slot)

    before = stations(run.cmd("baseddiag %s chemist" % SITE))
    have = [s for s in before if s[2]]
    for _ in range(max(0, args.stations - len(have))):
        run.cmd("mxreach place %s mixingstation wing" % CODE, wait=2.0)
    setup = run.cmd("baseddiag %s chemist setup wing" % SITE, wait=2.0)
    worked = [l for l in setup if "chemist setup:" in l and "in the wing" in l]
    if not run.check("setup: the chemist works mixing stations in the wing", bool(worked),
                     "%d wing stations assigned" % len(worked)):
        return run.summary()
    # Work hours, so the chemist starts at once.
    run.g.world("set_time", {"time": 900})
    time.sleep(2)
    track(run, args.seconds, "work")
    after = run.cmd("baseddiag %s chemist" % SITE)
    done = stations(after)
    used = [s for s in done if s[2] and (s[3] < 20 or s[5] > 0)]
    run.check("the chemist worked a station in the wing", bool(used),
              "; ".join("%s product %d output %d" % (s[0], s[3], s[5]) for s in done if s[2]) or "no wing station")
    if args.reload:
        reload(run, args.slot)
        run.g.world("set_time", {"time": 900})
        time.sleep(2)
        track(run, args.reload, "after load")
    return run.summary()


def track(run, seconds, label):
    """Tracks every employee for seconds and checks the trace: no fall, no warp, nobody under the wing floor."""
    mark = "baseddiag track: started for %d s" % seconds
    run.cmd("baseddiag %s track %d" % (SITE, seconds))
    end = time.time() + seconds + 5
    while time.time() < end:
        time.sleep(min(30, max(1, end - time.time())))
        run.cmd("baseddiag %s chemist" % SITE)
    log = run.log_since(mark)
    falls = [l for l in log if "baseddiag track:" in l and " fell " in l]
    warps = [l for l in log if "baseddiag track:" in l and " jumped " in l]
    under = [l for l in log if "baseddiag track:" in l and " is under the wing floor " in l]
    starts = [l for l in log if "baseddiag track:" in l and " starts " in l]
    for line in starts + falls + warps + under:
        print("  " + line.split("] ", 2)[-1], flush=True)
    sums = [m for m in (SUMMARY.search(l) for l in log) if m]
    first = lambda lines: (": " + lines[0].split("] ", 2)[-1]) if lines else ""
    run.check("%s: every employee was sampled" % label, bool(sums), "%d summaries" % len(sums))
    run.check("%s: nobody fell" % label, not falls, "%d falls%s" % (len(falls), first(falls)))
    run.check("%s: nobody warped" % label, not warps, "%d warps%s" % (len(warps), first(warps)))
    for m in sums:
        run.check("%s: %s stays out from under the wing floor" % (label, m.group(1)), int(m.group(5)) == 0,
                  "%s samples under it, y %s to %s" % (m.group(5), m.group(6), m.group(7)))


def reload(run, slot):
    """Saves, goes to the menu, loads the same save and waits until it is ready."""
    print("  save: %s" % run.g.call("save_now", {}, timeout_ms=30000), flush=True)
    time.sleep(5)
    print("  menu: %s" % run.g.call("return_to_menu", {}, timeout_ms=15000), flush=True)
    time.sleep(12)
    print("  load: %s" % run.g.call("load_save", {"slot": 5}, timeout_ms=15000), flush=True)
    time.sleep(10)
    ready = subprocess.run([sys.executable, os.path.join(_bridge(), "s1.py"), "--slot", slot, "wait-ready", "--timeout", "300"])
    run.check("reload: the save loads again", ready.returncode == 0, "wait-ready exit %d" % ready.returncode)


if __name__ == "__main__":
    sys.exit(main())
