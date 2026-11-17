"""
Puts a site's project folder into the inventory for a hand-over and checks that it is there, preferably in one hotbar
slot. With --reload the save is stored, left and loaded again, and the folder must still be in the inventory.

    python _wt-Based-storageunit/Tests/folder-slot.py --slot mptest5 [--site storageunit] [--number 8] [--reload] [--save 5]

Prints PASS or FAIL per check and INFO for the slot the folder ended up in.
"""
import argparse
import os
import re
import subprocess
import sys
import time

BRIDGE = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge"
S1 = os.path.join(BRIDGE, "s1.py")


def run(slot, command, until):
    out = subprocess.run([sys.executable, S1, "--slot", slot, "run", "--max-wait", "60000", "--until", until, command],
                         capture_output=True, text=True, timeout=120, encoding="utf-8", errors="replace")
    return (out.stdout + out.stderr).replace('\\"', '"')


def state(slot, site):
    """(carried, hotbar numbers, raw line) from `baseddiag folder state`."""
    line = run(slot, "baseddiag site=%s folder state" % site, r"baseddiag: folder |failed")
    carried = re.search(r"carried=(True|False)", line)
    hotbar = re.search(r"hotbar=\[([^\]]*)\]", line)
    numbers = [int(n) for n in hotbar.group(1).split(",") if n.strip()] if hotbar else []
    return (carried is not None and carried.group(1) == "True"), numbers, line


def report(label, carried, numbers, line, preferred):
    ok = carried
    print("%s %s: %s" % ("PASS" if ok else "FAIL", label,
                         ("carried, hotbar=%s" % numbers) if carried else line[-300:]), flush=True)
    if carried and preferred not in numbers:
        print("INFO the folder is not in hotbar slot %d but in %s" % (preferred, numbers or "the backpack"), flush=True)
    return ok


def reload(slot, save):
    sys.path.insert(0, BRIDGE)
    from s1 import Game
    game = Game(slot)
    game.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    game.call("return_to_menu", {}, timeout_ms=60000)
    time.sleep(8)
    for attempt in range(6):
        reply = game.call("load_save", {"slot": save}, timeout_ms=60000)
        result = (reply or {}).get("result") or {}
        if reply and reply.get("ok") and result.get("loading"):
            break
        print("INFO load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
        time.sleep(5)
    ready, _ = game.wait_ready(timeout=1200, interval=10, say=lambda s: None)
    return ready


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--site", default="storageunit")
    ap.add_argument("--number", type=int, default=8, help="1-8; key 9 is the management clipboard or cash, no item slot")
    ap.add_argument("--reload", action="store_true")
    ap.add_argument("--save", type=int, default=5)
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    placed = run(a.slot, "baseddiag site=%s folder slot %d" % (a.site, a.number), r"folder in hotbar slot|failed")
    m = re.search(r"folder in hotbar slot \d+: [^\"\n]*", placed)
    print("INFO " + (m.group(0) if m else placed[-300:]), flush=True)
    ok = report("project folder in the inventory", *state(a.slot, a.site), a.number)
    if a.reload:
        ready = reload(a.slot, a.save)
        print("%s the save loads again" % ("PASS" if ready else "FAIL"), flush=True)
        ok = ready and ok
        if ready:
            time.sleep(5)
            ok = report("project folder still in the inventory after the reload", *state(a.slot, a.site), a.number) and ok
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
