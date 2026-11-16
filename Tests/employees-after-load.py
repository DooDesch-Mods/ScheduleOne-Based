"""
In-game check: every employee hired at the expanded Sweatshop is there after each load - visible, active, on the
NavMesh and with a behaviour - and the wing's world state was cut and baked before the game placed them.

A report had one botanist or handler missing on some loads: the employee still held a locker but stood invisible and
did nothing until the next reload. The fault is intermittent, so this loads the save several times in one run and
checks every employee after each load.

Needs a Debug build of Based (`baseddiag staff`) and a running test slot with a save whose wing is finished and which
has employees, for example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Build MapExpansions,_wt-Based-<topic> -Template mrmonkey-dock -NoApp
    python Based/Tests/employees-after-load.py [--slot mptest] [--loads 5] [--no-save]
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
    raise SystemExit("[employees-after-load] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

EMPLOYEE = re.compile(r"baseddiag: employee (.+?) index (\d+) at (\(.+?\)).*?on mesh (\w+).*?reaches the street (\w+), visible (\w+), "
                      r"active (\w+), agent enabled (\w+), building (.+?), behaviour (.+?)"
                      r"(?:, home (\w+), paid (\w+), end of day (\w+))?$")
STAFF = re.compile(r"baseddiag: staff (\d+)/(\d+)")


class Run:
    def __init__(self, slot, save):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.save = save
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

    def offset(self):
        path = self.g.log_path()
        return path, (os.path.getsize(path) if path else 0)

    def since(self, mark):
        path, offset = mark
        now = self.g.log_path()
        if not now:
            return []
        with open(now, "rb") as handle:
            handle.seek(offset if now == path else 0)
            return handle.read().decode("utf-8", errors="replace").splitlines()

    def staff(self):
        mark = self.offset()
        self.g.call("run_console_command", {"command": "baseddiag staff", "capture": False})
        time.sleep(2)
        return [l for l in self.since(mark) if "baseddiag" in l]

    def reload(self, save_first):
        if save_first:
            self.g.call("save_now", {}, timeout_ms=60000)
            time.sleep(3)
        self.g.call("return_to_menu", {}, timeout_ms=60000)
        time.sleep(8)
        for attempt in range(6):
            reply = self.g.call("load_save", {"slot": self.save}, timeout_ms=60000)
            result = (reply or {}).get("result") or {}
            if reply and reply.get("ok") and result.get("loading"):
                return True
            print("      load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
            time.sleep(5)
        return False

    # Order of the wing's world state against the employees' load in one load's log lines.
    def order(self, label, lines):
        cut = next((i for i, l in enumerate(lines) if re.search(r"state cut: 'based_ming_upper' .* applied", l)), None)
        bake = next((i for i, l in enumerate(lines) if "state nav: 'based_ming_upper/complete' baked" in l), None)
        placed = next((i for i, l in enumerate(lines) if "[Based]" in l and "employee" in l.lower() and "placed" in l.lower()), None)
        self.check("%s: the wing's state cut is applied" % label, cut is not None, "" if cut is not None else "no 'state cut ... applied' line")
        self.check("%s: the wing's navigation is baked" % label, bake is not None, "" if bake is not None else "no bake line")
        if cut is not None and bake is not None:
            self.check("%s: the cut comes before the bake" % label, cut < bake, "cut line %d, bake line %d" % (cut, bake))
        joins = [l for l in lines if "state nav: 'based_ming_upper/complete' existing floor" in l]
        for line in joins:
            m = re.search(r"at (\d+) of (\d+) edge points for agent '([^']+)'", line)
            if m:
                print("      %s: %s joins %s of %s edge points" % (label, m.group(3), m.group(1), m.group(2)), flush=True)
        if placed is not None and bake is not None:
            self.check("%s: employees are placed after the bake" % label, placed > bake, lines[placed].strip()[:160])

    def employees(self, label, expected):
        lines = self.staff()
        head = next((STAFF.search(l) for l in lines if STAFF.search(l)), None)
        found = [m for m in (EMPLOYEE.search(l) for l in lines) if m]
        count = int(head.group(1)) if head else -1
        if expected is None:
            expected = count
        self.check("%s: the property lists every employee" % label, count == expected and count > 0, "%d listed, %d expected" % (count, expected))
        self.check("%s: every listed employee is reported" % label, len(found) == count, "%d of %d" % (len(found), count))
        for m in found:
            who = "%s: %s" % (label, m.group(1))
            state = "at %s, on mesh %s, visible %s, active %s, agent %s, building %s, behaviour %s, home %s, paid %s, end of day %s" % (
                m.group(3), m.group(4), m.group(6), m.group(7), m.group(8), m.group(9), m.group(10).strip(),
                m.group(11), m.group(12), m.group(13))
            self.check(who + " is visible and active", m.group(6) == "True" and m.group(7) == "True", state)
            self.check(who + " stands on the NavMesh", m.group(4) == "True" and m.group(8) == "True", state)
            self.check(who + " is not hidden in a building", m.group(9).strip() == "none", state)
        return expected


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--save", type=int, default=5)
    parser.add_argument("--loads", type=int, default=5, help="loads to check, the running one included")
    parser.add_argument("--settle", type=int, default=25, help="seconds after each load before the employees are read")
    parser.add_argument("--no-save", action="store_true", help="reload without saving first, so every load starts from the same file")
    args = parser.parse_args()
    run = Run(args.slot, args.save)

    expected = None
    mark = (run.g.log_path(), 0)
    for n in range(1, args.loads + 1):
        label = "load %d" % n
        if n > 1:
            mark = run.offset()
            if not run.check("%s: the save loads again" % label, run.reload(not args.no_save)):
                break
        ready, last = run.g.wait_ready(timeout=600, interval=5, say=lambda s: None)
        if not run.check("%s: the game is ready" % label, ready, "" if ready else str(last)):
            break
        time.sleep(args.settle)
        lines = run.since(mark)
        if n == 1:
            # The first load may have started before this script; only its last session in the log counts.
            starts = [i for i, l in enumerate(lines) if "Based" in l and "Main" in l and "scene" in l.lower()]
            lines = lines[starts[-1]:] if starts else lines
        run.order(label, lines)
        expected = run.employees(label, expected)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
