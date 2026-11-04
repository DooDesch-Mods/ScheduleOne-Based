"""
In-game check: a handler carries a delivery from the Sweatshop loading dock onto a shelf, and the route from the
dock survives saving and loading.

Needs Debug builds of Based (`baseddiag dockroute`) and MapExpansions (`mxreach place|setup`) in a running test
slot. It finishes the wing and the dock in the save, hires a handler, builds a locker and a shelf on the old floor,
orders soil to the dock, then saves and reloads save 5 once.

    python _wt-Based-dockroute/tests/dock-routes.py [--slot mptest] [--save 5]
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
    raise SystemExit("[dock-routes] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402


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

    def reload(self):
        mark = self.mark()
        self.g.call("save_now", {}, timeout_ms=60000)
        time.sleep(3)
        self.g.call("return_to_menu", {}, timeout_ms=60000)
        time.sleep(8)
        # The menu can drop a load request while its own pages are still mounting, so retry until it reports loading.
        for attempt in range(6):
            reply = self.g.call("load_save", {"slot": self.save}, timeout_ms=60000)
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


def soil_on_shelf(lines):
    for l in lines:
        m = re.search(r"dockroute work: \d+ racks hold .* free slots, soil (\d+)", l)
        if m:
            return int(m.group(1))
    return None


def pick(lines, text):
    return next((l.strip()[-180:] for l in lines if text in l), "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--save", type=int, default=5)
    args = ap.parse_args()
    run = Run(args.slot, args.save)

    run.cmd("settime 0800", wait=1.0)
    log = run.cmd("baseddiag dockroute build", wait=6.0)
    run.check("wing and dock complete", has(log, "wing and loading dock complete"), pick(log, "dockroute"))
    run.check("the dock stands", has(log, "dockroute dock 0 guid"), pick(log, "dockroute dock 0"))
    run.cmd("addemployee handler sweatshop", wait=4.0)
    # A save that already holds a locker and a rack (an earlier test) keeps them; a full floor is not a failure then.
    for item in ("locker", "smallstoragerack"):
        log = run.cmd("mxreach place sweatshop %s old" % item, wait=2.0)
        print("      " + (pick(log, "mxreach place") or "no reply to mxreach place " + item), flush=True)
    log = run.cmd("mxreach setup sweatshop", wait=1.0)
    run.check("the handler has a locker and is paid", has(log, "mxreach setup: handler") and "locker none" not in pick(log, "setup: handler"), pick(log, "setup: handler"))
    log = run.cmd("baseddiag dockroute route", wait=1.0)
    run.check("a handler gets a route from the dock to a storage rack", has(log, "to shelf"), pick(log, "dockroute route"))
    before = soil_on_shelf(run.cmd("baseddiag dockroute work", wait=1.0))
    print("      soil on the rack before the delivery: %s" % before, flush=True)

    log = run.cmd("baseddiag dockroute deliver 3 soil", wait=1.0)
    run.check("delivery sent to the dock", has(log, "dockroute deliver"), pick(log, "deliver"))
    # The van parks only until a handler has emptied it, which a poll easily misses, so its state is printed, not
    # checked; the soil on the racks below shows whether the delivery arrived.
    time.sleep(60)
    log = run.cmd("baseddiag dockroute", wait=1.0)
    print("      dock after the delivery: " + pick(log, "dockroute dock 0 guid"), flush=True)

    # The report: the handler never takes the cargo. Give it two in-game hours of real time to carry one load.
    moved, work = False, []
    for _ in range(10):
        work = run.cmd("baseddiag dockroute work", wait=1.0)
        now = soil_on_shelf(work)
        if before is not None and now is not None and now > before:
            moved = True
            break
        time.sleep(12)
    for l in work:
        if "dockroute work" in l:
            print("      " + l.strip()[-240:], flush=True)
    run.check("the handler carried the delivery onto the shelf", moved, pick(work, "holds"))

    ready, log = run.reload()
    run.check("reload ready", ready)
    log += run.cmd("baseddiag dockroute routes", wait=1.0)
    run.check("after loading the route still starts at the dock", has(log, "from the loading dock to"), pick(log, "routes:   0"))
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
