"""
In-game check: two Botanists work a full grow room in the finished Sweatshop wing without getting stuck between
the pots (Based#69), and the wing's north-west floor spot next to the old bay takes a pot (Based#70).

The layout is the one from the Nexus report: four rows of plastic pots in the wing's north block (6 x 4 m, the sink
in the north-west corner) and two rows in the south strip. A Botanist that cannot get close enough to a pot's
access point walks to it again every tick; with the wing's seams to the old bay that shows up as a stream of
`nav link: ... crossed a seam` lines while it stands still.

Needs Debug builds of MapExpansions (`mxreach`) and Based and a running test slot whose wing is finished, for
example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Build MapExpansions,Based -Template mrmonkey-dock -NoApp
    python Based/Tests/botanist-pots.py [--slot mptest] [--minutes 6] [--no-setup]
"""
import argparse
import os
import re
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[botanist-pots] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

CODE = "sweatshop"
POT = "plasticpot"
# Tile origins of the 2 x 2 pots, (x, y) on grid 0. North block x 10..21, y 12..19; the sink stands on 10..11, 18..19.
NORTH_BLOCK = [(20, 18), (18, 18), (16, 18), (14, 18), (12, 18),
               (20, 16), (10, 16),
               (20, 14), (18, 14), (14, 14), (10, 14),
               (20, 12), (18, 12), (16, 12), (14, 12)]
# The spot marked 0 in the report (Based#70).
CORNER_SPOT = (10, 12)
# The report's drying racks along the far wall, 3 tiles wide and 3 deep each. A harvest from the north block walks to them
# through the old bay, across the seams on both sides.
RACKS = [(10, 2), (13, 2), (16, 2), (19, 2)]
BED = (10, 4)
IDLE = re.compile(r"idle point ([\d.]+|-) m away")
WORK = re.compile(r"mxreach work: (\w+) at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), active ([^,]+), moving (\w+)")
POTS = re.compile(r"mxreach work: pots (\d+), full of soil (\d+), planted (\d+), ready for harvest (\d+)")
CROSS = re.compile(r"nav link: (.+?) \((\w+)\) crossed a seam")
LOOP = re.compile(r"mxreach trace: LOOP (.+?) \((\w+)\) re-plan")
HARVEST = re.compile(r"mxreach trace: harvest by (.+?) \((\w+)\) at pot")
POT_LINE = re.compile(r"mxreach pots: \(([-\d.]+), [-\d.]+, ([-\d.]+)\) (.+?), botanist (\w+), destination (.+)$")
# Pots taken out and put back while the Botanists work, so the NavMesh carving changes under warm caches.
RESHUFFLE = [(18, 14), (14, 14), (16, 12), (14, 12)]


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.results = []
        self.issues = {}

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

    def log_file(self):
        latest = os.path.join(self.g.root, "MelonLoader", "Latest.log")
        return latest if os.path.isfile(latest) else self.g.log_path()

    def lines(self, command, max_wait_ms=10000):
        """Log lines the command wrote. Read from the log file: the bridge capture stops at 300 lines."""
        log = self.log_file()
        start = os.path.getsize(log) if log else 0
        result = self.g.run(command, max_wait_ms=max_wait_ms)
        if not isinstance(result, dict) or not result.get("ok", True):
            print("  %s: bridge error %s" % (command, (result or {}).get("error") if isinstance(result, dict) else result), flush=True)
        if not log:
            out = (result.get("result") or {}).get("lines") or [] if isinstance(result, dict) else []
            return [str(l.get("text", "")) if isinstance(l, dict) else str(l) for l in out]
        with open(log, encoding="utf-8", errors="replace") as f:
            f.seek(start)
            return f.read().splitlines()

    def place(self, x, y):
        text = "\n".join(self.lines("mxreach at %s %s %d %d 0" % (CODE, POT, x, y)))
        return "mxreach at: built" in text, text.strip().splitlines()[-1] if text.strip() else "no log line"

    def setup(self):
        for rect in ("10 12 21 19", "10 2 21 5"):
            self.lines("mxreach clear %s %s" % (CODE, rect))
        racks = 0
        for x, y in RACKS:
            text = "\n".join(self.lines("mxreach at %s dryingrack %d %d 0 0" % (CODE, x, y)))
            if "mxreach at: built" in text:
                racks += 1
            else:
                print("  rack at %d,%d: %s" % (x, y, text.strip().splitlines()[-1] if text.strip() else "no log line"))
        self.check("layout: the %d drying racks along the far wall stand" % len(RACKS), racks == len(RACKS), "%d of %d built" % (racks, len(RACKS)))
        # Clearing the far wall removes the template's bed, and an employee without a locker only waits outside.
        text = "\n".join(self.lines("mxreach at %s bed %d %d 0" % ((CODE,) + BED)))
        self.check("layout: a bed for the workers' lockers stands", "mxreach at: built" in text,
                   text.strip().splitlines()[-1] if text.strip() else "no log line")
        built = []
        for x, y in NORTH_BLOCK:
            ok, line = self.place(x, y)
            if not ok:
                print("  could not place %d,%d: %s" % (x, y, line))
            built.append(ok)
        self.check("layout: all %d pots of the report's north block stand" % len(built), all(built), "%d of %d built" % (sum(built), len(built)))
        ok, line = self.place(*CORNER_SPOT)
        self.check("Based#70: the north-west spot next to the old bay takes a pot", ok, line)
        # The template has one Botanist; the report has two.
        have = self.count_botanists()
        print("  botanists before setup: %d" % have)
        if have < 2:
            # The template's Sweatshop is full at Based's limit of 3. The report lists WorkerLimits, which raises the
            # limit on its own; raising it past Based's limit here hires the extra Botanist the same way.
            log = self.log_file()
            start = os.path.getsize(log) if log else 0
            print("  " + next((l for l in self.lines("mxreach capacity %s %d" % (CODE, 3 + 2 - have)) if "capacity" in l), "no capacity line"))
            for _ in range(2 - have):
                self.lines("addemployee botanist %s" % CODE)
            deadline = time.time() + 30
            while self.count_botanists() < 2 and time.time() < deadline:
                time.sleep(3)
            hired = []
            if log:
                with open(log, encoding="utf-8", errors="replace") as f:
                    f.seek(start)
                    hired = f.read().splitlines()
            thrown = [l for i, l in enumerate(hired) if "IndexOutOfRange" in l and any("AssignProperty" in x for x in hired[i:i + 6])]
            self.check("Based#69: a Botanist hired above Based's limit is assigned without an exception", not thrown,
                       "%d IndexOutOfRangeException in Employee.AssignProperty" % len(thrown) if thrown else "none")
        for line in self.lines("mxreach setup %s" % CODE):
            if "setup:" in line:
                print("  " + line)
        for item, qty in (("soil", 40), ("ogkushseed", 40)):
            self.lines("mxreach stock %s %s %d" % (CODE, item, qty))
        print("  " + next((l.split("[dev] ", 1)[-1] for l in self.lines("mxreach route %s ogkushseed" % CODE) if "mxreach route" in l), "no route line"))
        # The report's Botanists stopped harvesting: every pot starts fully grown. A plant sown within a few seconds
        # of building its pot is gone soon after, so the pots are sown twice and ripen after the second sowing.
        self.sow_and_ripen()
        self.lines("settime 0800")

    def sow_and_ripen(self):
        for _ in range(2):
            print("  " + next((l.split("[dev] ", 1)[-1] for l in self.lines("mxreach plant %s ogkushseed" % CODE) if "mxreach plant" in l), "no plant line"), flush=True)
            time.sleep(8)
        print("  " + self.ripen(), flush=True)

    def ripen(self):
        return next((l.split("[dev] ", 1)[-1] for l in self.lines("mxreach ripen %s" % CODE) if "mxreach ripen" in l), "no ripen line")

    def botanists(self):
        names = []
        for line in self.lines("mxreach %s" % CODE, max_wait_ms=20000):
            m = re.search(r"mxreach: employee (\w+) \(Botanist\) at", line)
            if m:
                names.append(m.group(1))
        return names

    def count_botanists(self):
        return len(self.botanists())

    def reshuffle(self):
        # Like a player, the clear leaves a pot in use where it is; only the pots taken out are put back.
        out = []
        busy = 0
        for x, y in RESHUFFLE:
            text = "\n".join(self.lines("mxreach clear %s %d %d %d %d" % (CODE, x, y, x + 1, y + 1)))
            if "mxreach clear: removing" in text:
                out.append((x, y))
            elif "mxreach clear: skipping" in text:
                busy += 1
        time.sleep(5)
        back = sum(self.place(x, y)[0] for x, y in out)
        print("  layout change while working: %d of %d pots taken out, %d put back, %d in use stayed"
              % (len(out), len(RESHUFFLE), back, busy), flush=True)
        if back:
            self.sow_and_ripen()

    def watch(self, minutes, reshuffle=False):
        log = self.log_file()
        if not log:
            print("  no MelonLoader log found under %s; seam crossings are not counted" % self.g.root, flush=True)
        start = os.path.getsize(log) if log else 0
        end = time.time() + minutes * 60
        history = {}
        pots = None
        first = None
        half = time.time() + minutes * 30 if reshuffle else None
        # Sown plants need days of game time and the wing has no grow lights; every two minutes all of them ripen, so
        # harvest walks keep coming.
        ripen = time.time() + 60
        while time.time() < end:
            if half and time.time() >= half:
                half = None
                self.reshuffle()
            if time.time() >= ripen:
                ripen = time.time() + 120
                print("  " + self.ripen(), flush=True)
            for line in self.lines("mxreach work %s" % CODE):
                m = WORK.search(line)
                if m:
                    name = m.group(1)
                    pos = tuple(float(v) for v in m.group(2, 3, 4))
                    idle = IDLE.search(line)
                    # "-" is an employee without an idle point; it never counts as standing at one.
                    away = float(idle.group(1)) if idle and idle.group(1) != "-" else float("inf")
                    history.setdefault(name, []).append((time.time(), pos, m.group(5), m.group(6), away))
                    self.issues[name] = line.split("issues: ", 1)[-1] if "issues: " in line else ""
                m = POTS.search(line)
                if m:
                    pots = tuple(int(v) for v in m.groups())
                    first = first or pots
            time.sleep(10)
        crossings = {}
        loops = {}
        self.harvests = {}
        if log:
            with open(log, encoding="utf-8", errors="replace") as f:
                f.seek(start)
                for line in f:
                    m = HARVEST.search(line)
                    if m and m.group(2) == "Botanist":
                        self.harvests[m.group(1)] = self.harvests.get(m.group(1), 0) + 1
                    m = CROSS.search(line)
                    if m and m.group(2) == "Botanist":
                        crossings[m.group(1)] = crossings.get(m.group(1), 0) + 1
                    m = LOOP.search(line)
                    if m and m.group(2) == "Botanist":
                        loops.setdefault(m.group(1), []).append(line.split("LOOP ", 1)[-1].strip()[:400])
        return history, crossings, first, pots, loops

    def pot_states(self, label):
        """Prints how many pots are in which state, per assigned botanist and destination."""
        groups = {}
        for line in self.lines("mxreach pots %s" % CODE):
            m = POT_LINE.search(line)
            if m:
                state = "ready" if m.group(3) == "ready" else ("growing" if "grown" in m.group(3) else m.group(3))
                key = (state, m.group(4), m.group(5).split(" at ")[0])
                groups[key] = groups.get(key, 0) + 1
        print("  pots %s: %s" % (label, "; ".join("%d %s, botanist %s, destination %s" % (n, *k) for k, n in sorted(groups.items())) or "none"), flush=True)
        return groups

    @staticmethod
    def longest_still(samples, radius=0.3):
        """Longest stretch in seconds a worker stood within radius of one point while a behaviour was active."""
        best = 0.0
        i = 0
        for j in range(len(samples)):
            while i < j and max(abs(a - b) for a, b in zip(samples[i][1], samples[j][1])) > radius:
                i += 1
            if all(s[2] not in ("none", "Wait ouside", "Idle") for s in samples[i:j + 1]):
                best = max(best, samples[j][0] - samples[i][0])
        return best


    @staticmethod
    def longest_stuck_waiting(samples, radius=0.3, idle_reach=1.5):
        """Longest stretch in seconds a worker waited outside, stood still, and was not at its idle point."""
        best, where = 0.0, None
        i = 0
        for j in range(len(samples)):
            while i < j and max(abs(a - b) for a, b in zip(samples[i][1], samples[j][1])) > radius:
                i += 1
            if all(s[2] == "Wait ouside" and s[4] > idle_reach for s in samples[i:j + 1]):
                if samples[j][0] - samples[i][0] > best:
                    away = "no idle point" if samples[j][4] == float("inf") else "%.1f m from the idle point" % samples[j][4]
                    best, where = samples[j][0] - samples[i][0], "%s, %s" % (samples[j][1], away)
        return best, where


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--minutes", type=float, default=6)
    ap.add_argument("--no-setup", action="store_true")
    ap.add_argument("--reshuffle", action="store_true", help="take four pots out and put them back at half time")
    args = ap.parse_args()
    r = Run(args.slot)
    if not args.no_setup:
        r.setup()
    for line in r.lines("mxreach navmap %s Employee" % CODE, max_wait_ms=20000):
        if "mxreach navmap" in line:
            print("  " + line.split("[dev] ", 1)[-1])
    print("  " + next((l.split("[dev] ", 1)[-1] for l in r.lines("mxreach trace on") if "mxreach trace" in l), "no trace line"))
    names = r.botanists()
    r.check("two Botanists work the Sweatshop", len(names) >= 2, ", ".join(names))
    staff = [m for m in (WORK.search(l) for l in r.lines("mxreach work %s" % CODE)) if m]
    without = [m.group(1) for m in staff if not IDLE.search(m.string) or IDLE.search(m.string).group(1) == "-"]
    r.check("Based#69: every employee of the Sweatshop has an idle point", staff and not without,
            "%d employees, without: %s" % (len(staff), ", ".join(without) or "none"))
    r.pot_states("at the start")
    history, crossings, first, pots, loops = r.watch(args.minutes, args.reshuffle)
    for name, found in sorted(loops.items()):
        for line in found[:3]:
            print("  loop: " + line)
    r.check("Based#69: no Botanist re-plans one walk 50 times in a row", not loops,
            ", ".join("%s %d loops" % (n, len(v)) for n, v in sorted(loops.items())) or "none")
    history = {n: s for n, s in history.items() if n in names}
    for name, samples in sorted(history.items()):
        still = r.longest_still(samples)
        last = samples[-1]
        print("  %s: last at %s, active %s; longest still while working %.0f s" % (name, last[1], last[2], still))
    for name, samples in sorted(history.items()):
        r.check("Based#69: %s never stands still at one spot while working for 60 s" % name, r.longest_still(samples) < 60,
                "%.0f s" % r.longest_still(samples))
    for name, samples in sorted(history.items()):
        stuck, at = r.longest_stuck_waiting(samples)
        r.check("Based#69: %s, when out of work, reaches the idle point instead of standing still for 60 s" % name, stuck < 60,
                "%.0f s at %s" % (stuck, at) if at else "0 s")
    total = sum(crossings.values())
    r.check("Based#69: Botanists cross the wing seams less than once per 20 s", total < args.minutes * 3,
            "%d crossings in %.1f min: %s" % (total, args.minutes, crossings))
    for name in sorted(history):
        # The trace logs each finished harvest under the full name; history keys are first names.
        harvests = sum(n for full, n in r.harvests.items() if full.split(" ")[0] == name)
        # Full drying racks stop harvests in vanilla too; the game then names the reason in the worker's issues.
        full = "destination is full" in r.issues.get(name, "") and history[name][-1][2] == "Wait ouside"
        r.check("Based#69: %s keeps harvesting, or waits with the game's 'destination is full' issue" % name,
                harvests >= 3 or full,
                "%d harvests in %.1f min%s" % (harvests, args.minutes, ", waits outside: destination full" if full else ""))
    if pots:
        print("  pots at the end: %d, planted %d, ready for harvest %d" % (pots[0], pots[2], pots[3]))
    r.pot_states("at the end")
    for name in names:
        for line in r.lines("mxreach path %s %s" % (CODE, name)):
            if "mxreach path" in line:
                print("  " + line.split("[dev] ", 1)[-1], flush=True)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
