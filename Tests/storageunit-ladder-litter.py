"""
In-game check of the Storage Unit's interior ladder and of the construction trash a finished build leaves.

Ladder: absent before the south build starts, present from its first night, the player climbs it with the game's own
move input from the hall floor to the loft, and it is back after a save and reload. Trash: a real sleep (`baseddiag
advance`) that completes the loft drops 8 to 15 of the game's trash items in the loft, a second sleep drops none, the
trash grabber picks pieces up, and the rest is still there after a reload without a second drop. Shots of the ladder
and the trash by day and by night go to OUT.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage
Unit. Uses MapExpansions' Tests/employee-reach.py for the console helpers.

    python _wt-Based-storageunit/Tests/storageunit-ladder-litter.py --slot mptest2 --out _drafts/based-storageunit/r3/ladder-litter
        [--save 5] [--mapexpansions _wt-MapExpansions-v130]
"""
import argparse
import importlib.util
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
SITE = "site=storageunit"
LOFT = "ray.storageunit-loft"
# The hall floor in front of the ladder, facing it (north), and the loft floor height.
FOOT = ("-2.65", "0.1", "102.75")
LOFT_FLOOR = 3.1


def load_reach(root):
    spec = importlib.util.spec_from_file_location("employee_reach", os.path.join(root, "Tests", "employee-reach.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def s1(slot, *args, timeout=240):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def shots(slot, out, prefix, views):
    for time_of_day, tag in (("1200", "day"), ("0000", "night")):
        done = subprocess.run([sys.executable, os.path.join(HERE, "storageunit-shots.py"), "--slot", slot, "--time", time_of_day,
                               "--prefix", prefix + "-" + tag, "--out", out, "--only", views],
                              capture_output=True, text=True, encoding="utf-8", errors="replace")
        print("%s  shots %s %s%s" % ("PASS" if done.returncode == 0 else "FAIL", prefix, tag,
                                    "" if done.returncode == 0 else "  - " + (done.stdout + done.stderr)[-300:]), flush=True)


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


def ladder_line(run, pick):
    return pick(run.cmd("baseddiag %s ladder" % SITE, wait=1.5), "baseddiag: storage")


def litter_now(run, pick):
    line = pick(run.cmd("baseddiag %s litter %s" % (SITE, LOFT), wait=1.0), "baseddiag litter: " + LOFT)
    m = re.search(r"in the rooms now (-?\d+)", line)
    return (int(m.group(1)) if m else None), line


def climb(run, pick, slot, label):
    s1(slot, "tp", *FOOT, "--yaw", "0")
    time.sleep(1.5)
    log = run.cmd("baseddiag climb 5", wait=7.0)
    start, end = pick(log, "baseddiag climb: start"), pick(log, "baseddiag climb: end")
    m = re.search(r"end at \((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\), highest (-?[\d.]+), ladder (\S+)", end)
    ok = m is not None and float(m.group(2)) > LOFT_FLOOR and m.group(5) != "none"
    run.check("%s: the player climbs from the hall floor to the loft" % label, ok, (start + " | " + end) or "no climb line")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--save", type=int, default=5)
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-v130")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    # A plan pass before this one leaves the project folder in hand, in front of every shot.
    run.cmd("baseddiag %s folder close" % SITE, wait=1.0)
    free = pick(run.cmd("baseddiag %s folder drop" % SITE, wait=1.0), "baseddiag: hands free")
    run.check("hands free for the shots", "hands free" in free, free or "no hands-free line")

    # Ladder: not with the east room, there from the south build's first night.
    run.cmd("baseddiag %s phase ray.storageunit-east:complete" % SITE, wait=10.0)
    line = ladder_line(run, pick)
    run.check("no ladder before the south build", "no ladder" in line, line or "no reply")
    run.cmd("baseddiag %s phase ray.storageunit-south:south-open" % SITE, wait=10.0)
    line = ladder_line(run, pick)
    run.check("ladder with the south build's first night", "ladder at" in line and "climb height" in line, line or "no reply")
    climb(run, pick, args.slot, "south build")
    shots(args.slot, args.out, "ladder", "ladder-hall,ladder-loft")

    # Trash: the loft's last night, then the sleep that completes it.
    run.cmd("baseddiag %s phase %s:loft-door" % (SITE, LOFT), wait=10.0)
    before, _ = litter_now(run, pick)
    mark = run.mark()
    run.cmd("baseddiag %s advance" % SITE, wait=8.0)
    # The cycle bakes the new floor's navigation first; the drop line can follow the command by well over 8 s.
    drop, deadline = "", time.time() + 40
    while not drop and time.time() < deadline:
        drop = next((l.strip() for l in run.lines(mark) if "Construction litter for " in l and "/" + LOFT + ":" in l), "")
        if not drop:
            time.sleep(1)
    m = re.search(r": (\d+) of (\d+) piece", drop)
    placed = int(m.group(1)) if m else 0
    run.check("the loft's sleep drops 8 to 15 trash items", m is not None and 8 <= placed <= 15 and placed == int(m.group(2)), drop[-260:] or "no litter line")
    after, line = litter_now(run, pick)
    run.check("the trash lies in the loft", after is not None and before is not None and after - before == placed, line)
    mark = run.mark()
    run.cmd("baseddiag %s advance" % SITE, wait=8.0)
    again = [l.strip()[-200:] for l in run.lines(mark) if "Construction litter for" in l and "dropped" in l]
    run.check("the next sleep drops nothing more", not again, again or "no drop line")
    shots(args.slot, args.out, "litter", "litter-loft,yard-strip,yard-fire")

    # The trash grabber in hand picks the nearest pieces up.
    run.cmd("give trashgrabber 1", wait=2.0)
    held = ""
    for slot in range(0, 9):
        held = pick(run.cmd("baseddiag hold %d" % slot, wait=1.0), "hotbar slot %d selected" % slot)
        if "Trash Grabber" in held or "Grabber" in held:
            break
    run.check("trash grabber in hand", "Grabber" in held, held or "no hold line")
    s1(args.slot, "tp", "-5.5", "3.4", "110.5", "--yaw", "180")
    time.sleep(1.5)
    log = run.cmd("baseddiag %s litter pickup 3" % SITE, wait=2.5)
    took = pick(log, "litter pickup: took")
    m = re.search(r"took (\d+); trash items in the world (\d+) before, (\d+) after", took)
    run.check("the grabber picks up 3 pieces", m is not None and int(m.group(1)) == 3 and int(m.group(2)) - int(m.group(3)) == 3, took or "no pickup line")
    left, line = litter_now(run, pick)
    print("      loft trash after the pickup: %s" % line, flush=True)

    # Save and reload: the rest is still there, nothing new is dropped, the ladder comes back.
    mark = run.mark()
    ready = reload(run, args.save)
    run.check("reload ready", ready, "")
    if ready:
        time.sleep(5)
        dropped = [l.strip()[-200:] for l in run.lines(mark) if "Construction litter for" in l and "dropped" in l]
        run.check("no trash dropped on load", not dropped, dropped or "no drop line")
        now, line = litter_now(run, pick)
        run.check("the loft's trash is saved", now is not None and left is not None and now == left, "before %s, after: %s" % (left, line))
        run.cmd("baseddiag %s phase ray.storageunit-south:south-open" % SITE, wait=10.0)
        line = ladder_line(run, pick)
        run.check("ladder after the reload", "ladder at" in line, line or "no reply")
        climb(run, pick, args.slot, "after the reload")
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
