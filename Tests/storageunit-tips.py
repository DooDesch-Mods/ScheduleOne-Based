"""
In-game check of the tip the Storage Unit's foreman takes during a build.

The loft's last night runs three times with a known tip total: no tip leaves all of its trash, half the limit
(5 % of the build price) leaves half, the full limit (10 %) leaves none and hides the tip choice. A product tip is
worth its market price times 1.5 per unit and counts as that much. A tip given before a save is still there after the
reload. Each case reads `baseddiag tip state` (tips, limit and the trash forecast) and the real drop of the sleep that
completes the build.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage
Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-storageunit/Tests/storageunit-tips.py --slot mptest2 [--save 5] [--mapexpansions _wt-MapExpansions-v130]
        [--skip-reload]
"""
import argparse
import importlib.util
import os
import re
import sys
import time

SITE = "site=storageunit"
LOFT = "ray.storageunit-loft"
CAP = 400
PRODUCT = "ogkush"



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


def until(run, mark, pattern, timeout=40.0):
    """The first new log line that matches, or "" after the timeout."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        for line in run.lines(mark):
            if re.search(pattern, line):
                return line.strip()
        time.sleep(0.5)
    return ""


def command(run, text, pattern, timeout=40.0):
    mark = run.mark()
    send(run, text)
    return until(run, mark, pattern, timeout), mark


def state(run):
    """(tips, cap, litter, full) of the loft from `baseddiag tip state`, plus the line."""
    line, _ = command(run, "baseddiag %s tip state" % SITE, r"baseddiag: tips ", 15)
    m = re.search(re.escape(LOFT) + r" stage=(\w+) tips=(\d+) cap=(\d+) litter=(\d+)/(\d+)", line)
    if not m:
        return None, line
    return {"stage": m.group(1), "tips": int(m.group(2)), "cap": int(m.group(3)), "litter": int(m.group(4)),
            "full": int(m.group(5))}, line


def last_night(run, label):
    moved, _ = command(run, "baseddiag %s phase %s:loft-door" % (SITE, LOFT), r"construction moved|baseddiag [a-z]+ failed", 90)
    ok = run.check("%s: the loft's last night" % label, "construction moved" in moved, moved[-200:] or "no phase line")
    set_line, _ = command(run, "baseddiag %s tip set 0" % SITE, r"baseddiag: tips |baseddiag [a-z]+ failed", 15)
    run.check("%s: tips start at $0" % label, "baseddiag: tips " in set_line, set_line[-200:])
    return ok


def sleep_drop(run, label, expected):
    """Sleeps through the loft's last night and checks the trash the crew leaves."""
    mark = run.mark()
    send(run, "baseddiag %s advance" % SITE)
    # The cycle bakes the new floor's navigation first; the litter line can follow the command by well over 8 s.
    line = until(run, mark, r"Construction litter for .*" + re.escape(LOFT) + r"(: \d+ of \d+ piece| skipped)", 60)
    if expected == 0:
        run.check("%s: the sleep leaves no trash" % label, "skipped: the crew got" in line, line[-240:] or "no litter line")
        return
    counted = until(run, mark, r"Construction litter for .*" + re.escape(LOFT) + r": \d+ of \d+ piece\(s\) dropped", 30)
    m = re.search(r": (\d+) of (\d+) piece\(s\) dropped", counted)
    run.check("%s: the sleep drops %d trash items" % (label, expected), m is not None and int(m.group(2)) == expected,
              (line[-200:] + " | " + counted[-200:]) if counted else (line[-240:] or "no litter line"))


def reload(run, save):
    run.g.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    run.g.call("return_to_menu", {}, timeout_ms=60000)
    time.sleep(8)
    for attempt in range(6):
        reply = run.g.call("load_save", {"slot": save}, timeout_ms=60000)
        result = (reply or {}).get("result") or {}
        if reply and reply.get("ok") and result.get("loading"):
            break
        print("      reload: load_save attempt %d not accepted: %s" % (attempt + 1, reply), flush=True)
        time.sleep(5)
    ready, _ = run.g.wait_ready(timeout=1200, interval=10, say=lambda s: None)
    return ready


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-v130")
    ap.add_argument("--skip-reload", action="store_true")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run = reach.Run(args.slot)

    command(run, "changecash 3000", r"cash|Cash", 10)

    # No tip: all of the trash.
    if last_night(run, "tip $0"):
        now, line = state(run)
        ok = now is not None and now["tips"] == 0 and now["cap"] == CAP and now["full"] > 0 and now["litter"] == now["full"]
        run.check("tip $0: limit $%d, full trash forecast" % CAP, ok, line[-260:])
        if now:
            sleep_drop(run, "tip $0", now["full"])

    # Half the limit: half of the trash, paid from the player's cash.
    if last_night(run, "tip $200"):
        paid, mark = command(run, "baseddiag %s tip cash 200" % SITE, r"Cash tip of \$200 .* paid|baseddiag [a-z]+ failed", 15)
        m = re.search(r"cash (-?\d[\d,]*(?:\.\d+)?) -> (-?\d[\d,]*(?:\.\d+)?)", paid)
        spent = float(m.group(1).replace(",", "")) - float(m.group(2).replace(",", "")) if m else None
        run.check("tip $200: $200 leaves the player's cash", spent is not None and abs(spent - 200) < .01, paid[-220:] or "no cash line")
        counted = until(run, mark, r"counted for [\w.]+/" + re.escape(LOFT), 10)
        run.check("tip $200: the host counts it", "counted for" in counted, counted[-220:] or "no counted line")
        now, line = state(run)
        ok = now is not None and now["tips"] == 200 and now["litter"] == round(now["full"] * .5)
        run.check("tip $200: half the trash forecast", ok, line[-260:])
        if now:
            sleep_drop(run, "tip $200", now["litter"])

    # The full limit: no trash, and the tip choice is gone.
    if last_night(run, "tip $400"):
        command(run, "baseddiag %s tip cash 400" % SITE, r"Cash tip of \$400 .* paid|baseddiag [a-z]+ failed", 15)
        now, line = state(run)
        run.check("tip $400: no trash forecast", now is not None and now["tips"] == CAP and now["litter"] == 0, line[-260:])
        hidden, _ = command(run, "baseddiag %s tip cash 10" % SITE, r"already got|Cash tip of \$10 ", 15)
        run.check("tip $400: no tip choice once the limit is reached", "already got" in hidden, hidden[-220:] or "no reply")
        sleep_drop(run, "tip $400", 0)

    # Product: market price times 1.5 per unit, counted at that value.
    if last_night(run, "product tip"):
        command(run, "give %s 10" % PRODUCT, r"[Gg]ave|[Aa]dded|baseddiag [a-z]+ failed", 10)
        took, mark = command(run, "baseddiag %s tip product 0" % SITE, r"Product tip of|baseddiag [a-z]+ failed", 15)
        m = re.search(r"Product tip of (\d+)x (\S+) worth \$(\d+) \(market price \$([\d.]+) x (\d+) per unit x ([\d.]+)\)", took)
        if m:
            units, value, price, pack, factor = int(m.group(1)), int(m.group(3)), float(m.group(4)), int(m.group(5)), float(m.group(6))
            run.check("product tip: worth market price x 1.5", factor == 1.5 and value == round(units * price * pack * 1.5), took[-240:])
            now, line = state(run)
            run.check("product tip: counted at its value", now is not None and now["tips"] == value, line[-260:])
        else:
            run.check("product tip: taken from the inventory", False, took[-240:] or "no product line")

    # Save and reload keep the total.
    if not args.skip_reload and last_night(run, "reload"):
        command(run, "baseddiag %s tip cash 100" % SITE, r"Cash tip of \$100 .* paid|baseddiag [a-z]+ failed", 15)
        before, _ = state(run)
        ready = reload(run, args.save)
        run.check("reload ready", ready, "")
        if ready:
            time.sleep(5)
            now, line = state(run)
            run.check("reload: the tip total is saved", before is not None and now is not None and now["tips"] == 100 == before["tips"], line[-260:])
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
