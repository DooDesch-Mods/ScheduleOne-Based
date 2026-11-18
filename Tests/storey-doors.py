"""Single-player check for the upper storey's two wooden doors (landing and hall, Content/Bungalow/BungalowDoors.cs).

From the night the walls go up each door stands in its copied wall with a leaf, an interaction and a closed start; it
swings open and shuts again on the console. Prints PASS/FAIL per check and leaves shots of both doors shut and open.

    python _wt-Based-bungalow/Tests/storey-doors.py --slot mptest6
"""
import argparse
import os
import re
import subprocess
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
S1 = os.path.normpath(os.path.join(_here, "..", "..", "Workspace", "tools", "bridge", "s1.py"))
OUT = os.path.normpath(os.path.join(_here, "..", "..", "_drafts", "based-bungalow", "storey"))
# The wooden doors stand in the upper floor's wall at x -169.93 (landing door z 113.3, hall door z 119.0); both are
# seen from the room east of that wall.
LANDING_EYE, LANDING_TARGET = (-166.6, 0.95, 115.2), (-169.9, 0.25, 113.6)
HALL_EYE, HALL_TARGET = (-166.6, 0.95, 117.2), (-169.9, 0.25, 119.3)
FAILED = []
STATE = (r"baseddiag: %s (open|closed); (no leaf bound|leaf at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), angle (-?[\d.]+))"
         r"[^;]*; (no interaction|interaction '[^']*')")
# console name, log name, shot eye, shot target
DOORS = (
    ("landing", "Upper landing door", LANDING_EYE, LANDING_TARGET),
    ("hall", "Upper hall door", HALL_EYE, HALL_TARGET),
)


def s1(*args):
    proc = subprocess.run([sys.executable, S1, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (": " + str(detail) if detail != "" else ""), flush=True)
    if not ok:
        FAILED.append(name)


def run(slot, command):
    _, out = s1("--slot", slot, "run", command, "--until", "baseddiag", "--max-wait", "20000")
    return out


def state(slot, door, timeout=8):
    """(open, angle, interaction text) once two readings a second apart show the same angle, or the last reading."""
    end, last = time.time() + timeout, None
    while time.time() < end:
        m = re.search(STATE % door[1].lower(), run(slot, "baseddiag door %s state" % door[0]))
        if m:
            reading = (m.group(1) == "open", float(m.group(6)) if m.group(6) else None, m.group(7))
            if last and reading[1] is not None and last[1] is not None and abs(reading[1] - last[1]) < .5:
                return reading
            last = reading
        time.sleep(1)
    return last


def shot(slot, name, eye, target):
    _, out = s1("--slot", slot, "cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target), "--hud", "hide", "--settle", "20")
    m = re.search(r'"path":\s*"([^"]+)"', out)
    if not m:
        print("INFO shot %s failed: %s" % (name, out.strip()[-200:]), flush=True)
        return
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".png")
    with open(m.group(1).replace("\\\\", "\\"), "rb") as src, open(path, "wb") as dst:
        dst.write(src.read())
    print("INFO shot " + path, flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest6")
    a = ap.parse_args()
    run(a.slot, "baseddiag site=bungalow phase complete")
    for stage in ("storey-walls", "storey-roof", "complete"):
        out = run(a.slot, "baseddiag site=bungalow phase %s storey" % stage)
        check("phase %s" % stage, "ray.bungalow-storey" in out and "failed" not in out, out.strip()[-200:])
        time.sleep(3)
        for door in DOORS:
            label = "%s %s" % (stage, door[1].lower())
            s = state(a.slot, door)
            check("%s: leaf bound and shut" % label, bool(s and s[1] is not None and not s[0] and abs(s[1]) < 1), s)
            check("%s: has an interaction" % label, bool(s and s[2].startswith("interaction")), s and s[2])
            if stage == "complete":
                shot(a.slot, "door-%s-shut" % door[0], door[2], door[3])
            run(a.slot, "baseddiag door %s open" % door[0])
            s = state(a.slot, door)
            check("%s: swings open" % label, bool(s and s[0] and s[1] is not None and abs(s[1]) > 30), s)
            if stage == "complete":
                shot(a.slot, "door-%s-open" % door[0], door[2], door[3])
            run(a.slot, "baseddiag door %s close" % door[0])
            s = state(a.slot, door)
            check("%s: shuts again" % label, bool(s and not s[0] and s[1] is not None and abs(s[1]) < 1), s)
    print(("FAIL" if FAILED else "PASS") + " storey-doors: %d failed" % len(FAILED))
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
