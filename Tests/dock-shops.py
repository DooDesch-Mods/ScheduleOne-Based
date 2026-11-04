"""
In-game check: every shop's delivery vehicle parks at the Sweatshop loading dock so that a handler can unload it.

The game moves the dock's access point behind the parked vehicle, and vehicles differ in length per shop, so one
shop passing says nothing about the others. For each shop with a delivery vehicle this sends a delivery of soil
to the dock, waits until the vehicle parks, checks that a handler can reach the spot behind it, and waits until the
handler has carried the soil onto a rack.

Needs Debug builds of Based (`baseddiag`) and MapExpansions (`mxreach`) in a running test slot.

    python _wt-Based-dockshops/Tests/dock-shops.py [--slot mptest] [--shops 0,3]
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
    raise SystemExit("[dock-shops] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


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

    def mark(self):
        path = self.g.log_path()
        return (path, os.path.getsize(path) if path else 0)

    def lines(self, mark):
        path, offset = mark
        now = self.g.log_path()
        if now != path:
            offset = 0
        if not now:
            return []
        with open(now, "rb") as handle:
            handle.seek(offset)
            return handle.read().decode("utf-8", errors="replace").splitlines()

    def cmd(self, command, wait=1.0):
        mark = self.mark()
        self.g.call("run_console_command", {"command": command, "capture": False})
        time.sleep(wait + 0.5)
        return [l for l in self.lines(mark) if "[Based]" in l or "[MapExpansions]" in l or "Console" in l]

    def reload(self, save):
        mark = self.mark()
        self.g.call("save_now", {}, timeout_ms=60000)
        time.sleep(3)
        self.g.call("return_to_menu", {}, timeout_ms=60000)
        time.sleep(8)
        # The menu can drop a load request while its own pages are still mounting, so retry until it reports loading.
        for attempt in range(6):
            reply = self.g.call("load_save", {"slot": save}, timeout_ms=60000)
            result = (reply or {}).get("result") or {}
            if reply and reply.get("ok") and result.get("loading"):
                break
            print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
            time.sleep(5)
        ready, last = self.g.wait_ready(timeout=1200, interval=10, say=lambda s: None)
        print("      reload: %s" % ("ready" if ready else "not ready: " + str(last)), flush=True)
        time.sleep(5)
        return ready, self.lines(mark)


def has(lines, text):
    return any(text in l for l in lines)


def pick(lines, text):
    return next((l.strip()[-200:] for l in lines if text in l), "")


def soil(lines):
    for l in lines:
        m = re.search(r"dockroute work: \d+ racks hold .* free slots, soil (\d+)", l)
        if m:
            return int(m.group(1))
    return None


def parked_van(lines):
    for l in lines:
        m = re.search(r"dockroute dock 0 guid \S+ at .*, van (.+?), output slots (\d+)", l)
        if m:
            return None if m.group(1) == "none" else m.group(1), int(m.group(2))
    return None, 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--shops", default="", help="comma separated shop indices; default every shop with a vehicle")
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--after-reload", action="store_true",
                    help="keep the dock, handler and route from an earlier run, save, reload, then deliver")
    args = ap.parse_args()
    run = Run(args.slot)

    if args.after_reload:
        ready, _ = run.reload(args.save)
        if not run.check("reload ready", ready):
            return run.summary()
        run.cmd("settime 0800", wait=1.0)
        log = run.cmd("mxreach setup sweatshop", wait=1.0)
        print("      " + pick(log, "setup: handler"), flush=True)
        log = run.cmd("baseddiag dockroute routes", wait=1.0)
        run.check("after loading the route still starts at the dock", has(log, "from the loading dock to"), pick(log, "routes:   0"))
    else:
        run.cmd("settime 0800", wait=1.0)
        log = run.cmd("baseddiag dockroute build", wait=6.0)
        run.check("wing and dock complete", has(log, "wing and loading dock complete"), pick(log, "dockroute"))
        run.cmd("addemployee handler sweatshop", wait=4.0)
        for item in ("locker", "smallstoragerack", "smallstoragerack", "smallstoragerack"):
            log = run.cmd("mxreach place sweatshop %s old" % item, wait=2.0)
            print("      " + (pick(log, "mxreach place") or "no reply to mxreach place " + item), flush=True)
        log = run.cmd("mxreach setup sweatshop", wait=1.0)
        run.check("the handler has a locker and is paid", has(log, "mxreach setup: handler") and "locker none" not in pick(log, "setup: handler"), pick(log, "setup: handler"))
        log = run.cmd("baseddiag dockroute route", wait=1.0)
        run.check("the handler gets a route from the dock to a rack", has(log, "to shelf"), pick(log, "dockroute route"))

    log = run.cmd("baseddiag dock inspect sweatshop", wait=1.0)
    shops = []
    for l in log:
        m = re.search(r"dock shop index=(\d+) name='(.*?)' vehicle=True", l)
        if m:
            shops.append((int(m.group(1)), m.group(2)))
    if args.shops:
        wanted = {int(s) for s in args.shops.split(",")}
        shops = [s for s in shops if s[0] in wanted]
    run.check("shops with a delivery vehicle found", len(shops) > 0, ", ".join("%d %s" % s for s in shops))

    for index, name in shops:
        # A route ends at one rack, and a full rack makes the handler idle ("nothing for me to do"), so every
        # delivery gets one more route, to the rack with the most room.
        run.cmd("baseddiag dockroute route", wait=1.0)
        before = soil(run.cmd("baseddiag dockroute work", wait=1.0)) or 0
        log = run.cmd("baseddiag dockroute deliver %d soil" % index, wait=1.0)
        if not run.check("%s: delivery sent" % name, has(log, "dockroute deliver"), pick(log, "deliver")):
            continue
        van, reach, slots, work = None, [], 0, []
        deadline = time.time() + 150
        while time.time() < deadline:
            log = run.cmd("baseddiag dockroute", wait=0.5)
            van, slots = parked_van(log)
            if van:
                reach = [l for l in log if "behind the parked van" in l]
                work = run.cmd("baseddiag dockroute work", wait=0.5)
                break
            time.sleep(1.5)
        if not run.check("%s: the vehicle parks at the dock" % name, van is not None, "" if van else "never seen parked within 150 s"):
            continue
        print("      %s parked: %s, %d output slots" % (name, van, slots), flush=True)
        for l in reach + [l for l in work if "route 0:" in l]:
            print("      " + l.strip()[-240:], flush=True)
        run.check("%s: a handler can reach the spot behind the vehicle" % name, has(reach, "employee can get there: True"), pick(reach, "behind the parked van"))

        moved, now = False, before
        deadline = time.time() + 180
        while time.time() < deadline:
            work = run.cmd("baseddiag dockroute work", wait=0.5)
            now = soil(work)
            if now is not None and now > before:
                moved = True
                break
            time.sleep(4)
        if not run.check("%s: the handler carried the soil onto a rack" % name, moved, "soil %s -> %s; %s" % (before, now, pick(work, "route 0:"))):
            print("      stopping: the vehicle still blocks the dock, later shops cannot park", flush=True)
            break
        # Let the vehicle leave before the next delivery parks.
        deadline = time.time() + 120
        while time.time() < deadline:
            van, _ = parked_van(run.cmd("baseddiag dockroute", wait=0.5))
            if not van:
                break
            time.sleep(3)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
