"""
In-game check (Based#85): every employee of the upgraded Sweatshop stays there for several in-game days of play
without a reload.

A player reported 2 of 3 employees gone after playing a while; a reload brought them back. This runs the clock for
several days in one session, with time skips, sleeps, and the player walking away and coming back, and reads every
employee once per in-game hour: still listed, visible, active, not inside a building, on the NavMesh, near the
Sweatshop. It also watches the log for the game's own ways to lose an employee: firing (vanilla fires an employee whose
health reaches zero), warps after pathing failures, NPCs that fell through the map.

Needs a Debug build of Based (`baseddiag`) in a running test slot whose save has an upgraded Sweatshop with employees,
for example template `mrmonkey-dock` (botanist, handler, chemist):

    pwsh -File Workspace/tools/session/session.ps1 -Build MapExpansions,_wt-Based-<topic> -Template mrmonkey-dock -NoApp
    python _wt-Based-<topic>/Tests/staff-during-play.py --slot mptest [--days 3] [--scale 4] [--out <folder>]

Each hour's reading goes to `<out>/staff-during-play.jsonl`; the script prints PASS or FAIL per check.
"""
import argparse
import json
import math
import os
import re
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
SITE = "site=ming.sweatshop"
V = r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)"
HEAD = re.compile(r"baseddiag: staff (\d+)/(\d+), places (\d+), spawn " + V)
EMPLOYEE = re.compile(r"baseddiag: employee (.+?) index (\d+) at " + V + r", waits at (none|\([^)]*\)), agent type -?\d+, on mesh (\w+), "
                      r"route to the street (\w+), reaches the street (\w+), visible (\w+), active (\w+), agent enabled (\w+), "
                      r"building (.+?), behaviour (.+?), home (\w+), paid (\w+), end of day (\w+)")
# Log lines that mean the game moved, hurt or dropped an employee on its own.
EVENTS = re.compile(r"Firing employee|too many pathing failures|fell (through|out of)|below the map|has died|knocked out|"
                    r"IndexOutOfRange|NullReferenceException.*(Employee|Botanist|Packager|Chemist|Cleaner)|Despawn", re.I)
# The Sweatshop with its yard and wing; an employee farther than this from the middle has left the site.
MIDDLE, RADIUS = (-63.0, 144.0), 40.0
# Where the player waits while away: far enough that the Sweatshop is out of every NPC range.
AWAY = (-80.7, -1.25, -55.76)
HOME = (-70.0, -3.0, 150.0)

results = []


def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print("%s  %s%s" % ("PASS" if ok else "FAIL", name, ("  - " + str(detail)) if detail else ""), flush=True)
    return ok


def s1(slot, *args, timeout=240):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def run(slot, until, command, max_wait=30000):
    return s1(slot, "run", "--until", until, "--max-wait", str(max_wait), command)[1]


def log_path(slot):
    sys.path.insert(0, os.path.dirname(S1))
    from s1 import Game  # noqa: E402
    return Game(slot).log_path()


class LogTail:
    def __init__(self, slot):
        self.slot = slot
        self.path = log_path(slot)
        self.offset = os.path.getsize(self.path) if self.path and os.path.isfile(self.path) else 0

    def new(self):
        if not self.path or not os.path.isfile(self.path):
            return []
        with open(self.path, "rb") as handle:
            handle.seek(self.offset)
            data = handle.read()
        self.offset += len(data)
        return data.decode("utf-8", errors="replace").splitlines()


def staff(slot):
    text = run(slot, r"baseddiag: staff", "baseddiag %s staff" % SITE)
    tail = text[text.rfind("baseddiag: staff "):] if "baseddiag: staff " in text else ""
    head = HEAD.search(tail)
    employees = {}
    for m in EMPLOYEE.finditer(tail):
        employees[m.group(1)] = {
            "index": int(m.group(2)), "at": [float(m.group(3)), float(m.group(4)), float(m.group(5))],
            "on_mesh": m.group(7) == "True", "reaches_street": m.group(9) == "True", "visible": m.group(10) == "True",
            "active": m.group(11) == "True", "agent": m.group(12) == "True", "building": m.group(13).strip(),
            "behaviour": m.group(14).strip(), "home": m.group(15) == "True", "paid": m.group(16) == "True",
            "end_of_day": m.group(17) == "True",
        }
    return (int(head.group(1)) if head else -1), employees


def missing_reasons(e):
    reasons = []
    if not e["visible"]:
        reasons.append("hidden")
    if not e["active"]:
        reasons.append("inactive")
    if e["building"] != "none":
        reasons.append("in building " + e["building"])
    if not e["on_mesh"]:
        reasons.append("off the NavMesh")
    if not e["reaches_street"]:
        reasons.append("no route to the street")
    d = math.dist((e["at"][0], e["at"][2]), MIDDLE)
    if d > RADIUS:
        reasons.append("%.0f m from the Sweatshop" % d)
    if e["at"][1] < -15:
        reasons.append("below the floor at y %.1f" % e["at"][1])
    return reasons


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--days", type=int, default=3)
    ap.add_argument("--scale", type=float, default=4.0, help="game time scale while the clock runs")
    ap.add_argument("--day-minutes", type=float, default=24.0, help="real minutes of one in-game day at scale 1")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "staff-during-play"))
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    os.makedirs(a.out, exist_ok=True)
    record = open(os.path.join(a.out, "staff-during-play.jsonl"), "w", encoding="utf-8")

    tail = LogTail(a.slot)
    check("the slot log is found", tail.path is not None, tail.path)
    count, start = staff(a.slot)
    if not check("staff diagnostics answered", count > 0 and len(start) == count, "%d listed, %d read" % (count, len(start))):
        return 1
    print("      employees: %s" % ", ".join("%s (index %d)" % (n, e["index"]) for n, e in start.items()), flush=True)

    hour_seconds = a.day_minutes * 60.0 / 24.0 / a.scale
    s1(a.slot, "time", "0700")
    run(a.slot, r"[Tt]ime scale", "settimescale %s" % a.scale)
    lost, events, readings = {}, [], 0
    for day in range(1, a.days + 1):
        hour = 7
        while True:
            # Day 1 runs the clock through; on later days the morning is skipped with settime, the way players skip.
            if day >= 2 and hour == 9:
                s1(a.slot, "time", "1400")
                hour = 14
            # The player leaves for a few hours each afternoon and comes back for the evening.
            if hour == 12:
                s1(a.slot, "tp", *map(str, AWAY))
            elif hour == 17:
                s1(a.slot, "tp", *map(str, HOME))
            time.sleep(hour_seconds)
            hour += 1
            count, now = staff(a.slot)
            readings += 1
            for line in tail.new():
                if EVENTS.search(line):
                    events.append("day %d %02d:00 %s" % (day, hour % 24, line.strip()[:220]))
                    print("      event day %d %02d:00: %s" % (day, hour % 24, line.strip()[:220]), flush=True)
            row = {"day": day, "hour": hour % 24, "listed": count, "employees": now}
            record.write(json.dumps(row) + "\n")
            record.flush()
            for name in start:
                e = now.get(name)
                reasons = ["not listed at the property"] if e is None else missing_reasons(e)
                if reasons and name not in lost:
                    lost[name] = "day %d %02d:00: %s%s" % (day, hour % 24, ", ".join(reasons),
                                                           "" if e is None else " (at %s, behaviour %s)" % (e["at"], e["behaviour"]))
                    print("      %s missing at %s" % (name, lost[name]), flush=True)
            summary = ", ".join("%s %s%s" % (n.split(" (")[0], e["behaviour"], "" if not missing_reasons(e) else " [" + ", ".join(missing_reasons(e)) + "]")
                                for n, e in now.items())
            print("      day %d %02d:00  %d listed  %s" % (day, hour % 24, count, summary), flush=True)
            if any(e["end_of_day"] for e in now.values()) or hour >= 28:
                break
        # The night: the player sleeps, which pays the employees from their lockers and starts the next day.
        run(a.slot, r"[Ss]leep|forcesleep", "forcesleep", 20000)
        time.sleep(30)
        count, now = staff(a.slot)
        for name in start:
            e = now.get(name)
            reasons = ["not listed at the property"] if e is None else missing_reasons(e)
            if reasons and name not in lost:
                lost[name] = "after sleep %d: %s" % (day, ", ".join(reasons))
                print("      %s missing at %s" % (name, lost[name]), flush=True)
        print("      after sleep %d: %d listed, paid %s" % (day, count, [e["paid"] for e in now.values()]), flush=True)

    run(a.slot, r"[Tt]ime scale", "settimescale 1")
    record.close()
    check("hourly readings taken", readings >= a.days * 10, "%d readings" % readings)
    for name in start:
        check("%s stays at the Sweatshop for %d days" % (name, a.days), name not in lost, lost.get(name, ""))
    check("the game neither fires, warps nor drops an employee", not events, "; ".join(events[:5]))

    failed = [r for r in results if not r[1]]
    print("\n===== %d PASS, %d FAIL" % (len(results) - len(failed), len(failed)))
    for name, _, detail in failed:
        print("FAIL  %s  - %s" % (name, detail))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
