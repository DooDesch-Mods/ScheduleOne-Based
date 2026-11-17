"""
Co-op check of a construction quest that ends without its build: host and client each cancel their own copy once, and
the client's quest HUD is destroyed once (no NullReferenceException in QuestHUDUI.Destroy or QuestEntryHUDUI).

The south room is ordered, then the loft jumps back to its door night, which resets the south room to "no rights".

Needs a host and a client in the same game with Debug builds of MapExpansions and Based.

    python _wt-Based-storageunit/Tests/storageunit-quest-cancel-coop.py --host mptest --client mptest3
"""
import argparse
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
SITE = "site=storageunit"
SOUTH = "ray.storageunit-south"
LOFT = "ray.storageunit-loft"
STARTED = "Construction quest of ray.storageunit/%s started" % SOUTH
CANCELLED = "Construction quest of ray.storageunit/%s cancelled" % SOUTH
HUD_ERRORS = ("QuestHUDUI.Destroy", "QuestEntryHUDUI")
failed = 0


def run(slot, command, until, wait=90000):
    out = subprocess.run([sys.executable, S1, "--slot", slot, "run", "--max-wait", str(wait), "--until", until, command],
                         capture_output=True, text=True, timeout=wait / 1000 + 60, encoding="utf-8", errors="replace")
    return (out.stdout + out.stderr).replace('\\"', '"')


def count(slot, text):
    # --grep is plain text, not a regex.
    out = subprocess.run([sys.executable, S1, "--slot", slot, "log", "--grep", text, "-n", "1000"],
                         capture_output=True, text=True, timeout=60, encoding="utf-8", errors="replace")
    return sum(1 for line in out.stdout.splitlines() if text in line)


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""), flush=True)


def wait_for(slot, text, before, seconds):
    deadline = time.time() + seconds
    n = count(slot, text)
    while n <= before and time.time() < deadline:
        time.sleep(2)
        n = count(slot, text)
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    sides = (("host", a.host), ("client", a.client))

    started = {name: count(slot, STARTED) for name, slot in sides}
    moved = run(a.host, "baseddiag %s phase %s:ordered" % (SITE, SOUTH), r"construction moved|failed")
    report("construction moved" in moved, "host: the south room is ordered", "" if "construction moved" in moved else moved[-300:])
    for name, slot in sides:
        n = wait_for(slot, STARTED, started[name], 40)
        report(n > started[name], "%s: the south room's quest starts" % name, "%d start line(s) before, %d after" % (started[name], n))

    cancelled = {name: count(slot, CANCELLED) for name, slot in sides}
    hud = {(name, e): count(slot, e) for name, slot in sides for e in HUD_ERRORS}
    moved = run(a.host, "baseddiag %s phase %s:loft-door" % (SITE, LOFT), r"construction moved|failed")
    report("construction moved" in moved, "host: the loft jumps back, the south room loses its order",
           "" if "construction moved" in moved else moved[-300:])
    for name, slot in sides:
        wait_for(slot, CANCELLED, cancelled[name], 40)
    # The quest HUD fades out over 0.5 s and a completed entry over 3 s before either is destroyed.
    time.sleep(6)
    for name, slot in sides:
        n = count(slot, CANCELLED) - cancelled[name]
        report(n == 1, "%s: the south room's quest is cancelled once" % name, "%d cancel line(s)" % n)
        for e in HUD_ERRORS:
            new = count(slot, e) - hud[(name, e)]
            report(new == 0, "%s: no new %s error" % (name, e), "%d new line(s)" % new)
    print("RESULT %s: %d failed" % ("PASS" if failed == 0 else "FAIL", failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
