"""
In-game check of the Storage Unit crew's breaks.

The loft build runs from 09:00 for about two game hours with the clock running. Each worker's break must last 5 to
10 game minutes and end on its own, never two workers may be on a break at the same time, and the next sleep must
still move the build one phase on.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage
Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-storageunit/Tests/storageunit-breaks.py --slot mptest2 [--minutes 130] [--mapexpansions _wt-MapExpansions-v130]
"""
import argparse
import importlib.util
import os
import re
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
SITE = "site=storageunit"
LOFT = "ray.storageunit-loft"
START = re.compile(r"storage workers: (\S+) break starts at (\d{4}) for (\d+) game minutes")
END = re.compile(r"storage workers: (\S+) break ends at (\d{4}): (.+)\.$")



def send(run, text):
    """Queues a console command. A bridge timeout means the game hitched (a sleep cycle bakes navigation); the
    command stays queued and runs, so the caller goes on waiting for its log line."""
    try:
        run.g.call("run_console_command", {"command": text, "capture": False})
    except TimeoutError as error:
        print("      info: %r hit the bridge timeout, waiting for its log line: %s" % (text, error), flush=True)

def load_reach(root):
    spec = importlib.util.spec_from_file_location("employee_reach", os.path.join(root, "Tests", "employee-reach.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def minutes(clock):
    return int(clock[:2]) * 60 + int(clock[2:])


def until(run, mark, pattern, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        for line in run.lines(mark):
            if re.search(pattern, line):
                return line.strip()
        time.sleep(0.5)
    return ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--minutes", type=int, default=130, help="game minutes to watch (about one real second each)")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-v130")
    args = ap.parse_args()
    run = load_reach(args.mapexpansions).Run(args.slot)

    mark = run.mark()
    send(run, "baseddiag %s phase %s:loft-stair" % (SITE, LOFT))
    moved = until(run, mark, r"construction moved|baseddiag [a-z]+ failed", 90)
    if not run.check("the loft build runs", "construction moved" in moved, moved[-200:] or "no phase line"):
        sys.exit(run.summary())
    subprocess.run([sys.executable, S1, "--slot", args.slot, "time", "0900"], capture_output=True, text=True, timeout=60)

    # The clock runs; breaks are logged by the host as they start and end.
    mark = run.mark()
    deadline = time.time() + args.minutes
    while time.time() < deadline:
        time.sleep(5)
    lines = [l.strip() for l in run.lines(mark)]
    events = []
    for line in lines:
        m = START.search(line)
        if m:
            events.append(("start", m.group(1), minutes(m.group(2)), int(m.group(3)), line))
        m = END.search(line)
        if m:
            events.append(("end", m.group(1), minutes(m.group(2)), m.group(3), line))
    starts = [e for e in events if e[0] == "start"]
    run.check("at least two breaks start", len(starts) >= 2, "%d start(s)" % len(starts))
    for e in starts[:4]:
        print("      " + e[4][-200:], flush=True)

    on_break, overlap, bad = {}, [], []
    for e in events:
        if e[0] == "start":
            if on_break:
                overlap.append("%s starts while %s is on a break" % (e[1], ", ".join(on_break)))
            on_break[e[1]] = e
            if not 5 <= e[3] <= 10:
                bad.append("%s: %d minutes" % (e[1], e[3]))
        else:
            begun = on_break.pop(e[1], None)
            if begun is None:
                continue
            took = (e[2] - begun[2]) % (24 * 60)
            if e[3] == "the break is over" and abs(took - begun[3]) > 1:
                bad.append("%s ended after %d of %d minutes" % (e[1], took, begun[3]))
    run.check("never two workers on a break at once", not overlap, "; ".join(overlap[:3]))
    run.check("each break lasts 5 to 10 game minutes", not bad and bool(starts), "; ".join(bad[:3]))
    ended = [e for e in events if e[0] == "end" and e[3] == "the break is over"]
    run.check("breaks end on their own", len(ended) >= max(1, len(starts) - 1), "%d of %d ended" % (len(ended), len(starts)))
    smoke = sum(1 for e in starts if "with a cigarette" in e[4])
    print("      %d of %d break(s) with a cigarette" % (smoke, len(starts)), flush=True)
    warns = [l for l in lines if "break without a cigarette" in l]
    run.check("no break lost its cigarette", not warns, warns[0][-200:] if warns else "")

    # The next sleep still moves the build one phase on.
    mark = run.mark()
    send(run, "baseddiag %s advance" % SITE)
    stage = until(run, mark, r"storage workers: state=\S+, phase=(?!loft-stair)", 60)
    run.check("the next sleep moves the build on", bool(stage), stage[-200:] or "no state line after the sleep")
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
