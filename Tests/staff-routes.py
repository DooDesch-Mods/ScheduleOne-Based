"""
In-game check: every Sweatshop employee can walk between the upper floor, the street and the loading dock once the
wing is finished, also right after a save loads with employees standing upstairs.

The wing bakes its own NavMesh. When that bake covered the old room as well, it laid a second floor over the vanilla
one with no way to the stairs, and employees who loaded upstairs stood on it for good (a handler never emptied the
dock). This reads the bake log, the waiting places, every employee's route to the street and, with --deliver, has a
van bring an order and waits for the handler to carry it off.

Needs a Debug build of Based (`baseddiag staff|dockroute`) and a running test slot with a save whose wing is finished
and which has employees upstairs, for example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Build MapExpansions,Based -Template mrmonkey-dock -NoApp
    python Based/Tests/staff-routes.py [--slot mptest] [--deliver]
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
    raise SystemExit("[staff-routes] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402


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

    def log_text(self):
        path = self.g.log_path()
        if not path:
            return ""
        with open(path, "rb") as handle:
            return handle.read().decode("utf-8", errors="replace")

    def cmd(self, command, wait=1.0):
        path = self.g.log_path()
        offset = os.path.getsize(path) if path else 0
        self.g.call("run_console_command", {"command": command, "capture": False})
        time.sleep(wait + 0.5)
        now = self.g.log_path()
        if not now:
            return []
        with open(now, "rb") as handle:
            handle.seek(offset if now == path else 0)
            text = handle.read().decode("utf-8", errors="replace")
        return [l for l in text.splitlines() if "baseddiag" in l]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--deliver", action="store_true", help="order iodine to the dock and wait for the handler")
    args = parser.parse_args()
    r = Run(args.slot)

    log = r.log_text()
    bakes = [l for l in log.splitlines() if "state nav: 'based_ming_upper/complete'" in l and "probe" in l]
    r.check("the finished wing's bake connects every probe", any("probes connected" in l for l in bakes),
            bakes[-1].strip() if bakes else "no bake line for based_ming_upper/complete")
    joins = [l for l in log.splitlines() if "state nav: 'based_ming_upper/complete' existing floor 0" in l]
    for agent in ("Humanoid", "Ignore Costs", "Employee"):
        mine = [l for l in joins if f"agent '{agent}'" in l]
        m = re.search(r"at (\d+) of (\d+) edge points", mine[-1]) if mine else None
        r.check(f"the old bay is joined to the new floor for {agent}", bool(m) and int(m.group(1)) > 0,
                mine[-1].strip() if mine else "no existing floor line")

    staff = r.cmd("baseddiag staff")
    places = [l for l in staff if "baseddiag: place " in l]
    r.check("staff diagnostics answered", bool(places), staff[-1] if staff else "no baseddiag line")
    for line in places:
        m = re.search(r"place (\d+) at (\S+ \S+ \S+) route (\w+)", line)
        if m:
            r.check("waiting place %s is reachable from the street" % m.group(1), m.group(3) == "PathComplete", line.split("baseddiag: ")[-1])
    employees = [l for l in staff if "baseddiag: employee " in l]
    r.check("the property has employees", bool(employees))
    for line in employees:
        m = re.search(r"employee (.+?) index (\d+) at (\(.+?\))", line)
        who = "%s (index %s) at %s" % (m.group(1), m.group(2), m.group(3)) if m else line
        r.check("%s reaches the street" % who, "reaches the street True" in line, line.split("baseddiag: ")[-1])

    route = r.cmd("baseddiag dockroute")
    access = [l for l in route if "access point 0" in l]
    r.check("the dock's access point is reachable from the street", bool(access) and "employee can get there: True" in access[0],
            access[0].split("baseddiag: ")[-1] if access else "no access point line")

    if args.deliver:
        r.cmd("baseddiag dockroute deliver 1 iodine", wait=2.0)
        start, parked, reached, emptied = time.time(), False, False, False
        while time.time() - start < 240 and not emptied:
            time.sleep(2)
            dock = [l for l in r.cmd("baseddiag dockroute") if "dockroute dock 0" in l]
            van_there = bool(dock) and "van none" not in dock[0]
            if not parked:
                parked = van_there
                continue
            work = r.cmd("baseddiag dockroute work")
            ready = [l for l in work if re.search(r"route \d+: item ready (?!none)", l) and "source output slots 16" in l]
            # A handler waiting in the yard can take the whole stack between two polls, so "Move items" while the
            # van is there counts as reaching it, like a dock route that lists the item and reaches its source.
            if any("reach source True" in l for l in ready) or any("active Move items" in l for l in work):
                reached = True
            # The van leaves once its slots are empty; until then a dock route still lists the item.
            van_empty = any("source output slots 16" in l for l in work) and not ready
            if reached and (not van_there or van_empty):
                emptied = True
        r.check("a van parked at the dock", parked)
        r.check("the handler can reach the dock while it holds items", reached)
        r.check("the handler emptied the dock", emptied)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
