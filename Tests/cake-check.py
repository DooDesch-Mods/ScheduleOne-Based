"""
In-game check of izy's birthday cake on the Bungalow's kitchen counter (Content/Bungalow/BungalowCake.cs).

On 23 November by the local clock (or with `baseddiag cake date 11-23`) the Kenney cake stands on the counter. This
reads the log lines of the build, asks `baseddiag cake`, takes a closeup with the label in view and a shot of the room,
and then checks that the label was hovered and centred over the cake (the hover line names the shift in pixels).

Needs a Debug build of Based in a running test slot with a save where the player owns the Bungalow.

    python _wt-Based-<topic>/Tests/cake-check.py [--slot mptest] [--out <folder>] [--force-date]
"""
import argparse
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
FAILED = []
# The cake stands at (-175.45, counter top -2.71, 119.95); the counter runs along the west wall.
CAKE = (-175.45, -2.71, 119.95)
CLOSEUP = dict(stand=(-173.6, -2.7, 119.95), eye=(-174.35, -2.2, 119.95), target=(-175.45, -2.55, 119.95))
ROOM = dict(stand=(-171.5, -2.7, 118.0), eye=(-171.6, -1.95, 117.4), target=(-175.45, -2.6, 119.95))


def s1(slot, *args):
    p = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def check(name, ok, detail=""):
    print(("PASS  " if ok else "FAIL  ") + name + ("  - " + detail if detail else ""))
    if not ok:
        FAILED.append(name)


def log(slot, text):
    return [l.strip() for l in s1(slot, "log", "--grep", text, "-n", "200")[1].splitlines() if text in l]


def shot(slot, view, out, name):
    s1(slot, "tp", *("%.2f" % v for v in view["stand"]))
    time.sleep(1.5)
    code, o = s1(slot, "cam", "shot", "--eye", *("%.2f" % v for v in view["eye"]),
                 "--target", *("%.2f" % v for v in view["target"]), "--hud", "show", "--settle", "30", "--sight", "off")
    m = re.search(r'"path"\s*:\s*"([^"]+)"', o)
    if not m:
        check("shot %s taken" % name, False, o.strip()[-300:])
        return None
    dest = os.path.join(out, name + ".png")
    shutil.copy(m.group(1).replace("\\\\", "\\"), dest)
    check("shot %s taken" % name, True, dest)
    return dest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--out", default=".")
    ap.add_argument("--force-date", action="store_true", help="set the date to 11-23 first (not needed on 23 November)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    if args.force_date:
        s1(args.slot, "run", "baseddiag cake date 11-23", "--max-wait", "5000")
        time.sleep(2)

    shown = log(args.slot, "Birthday cake shown")
    check("log: cake shown on 11-23", any("11-23" in l for l in shown), shown[-1][-200:] if shown else "missing")
    built = log(args.slot, "Birthday cake built: Kenney model")
    check("log: Kenney model built", bool(built), built[-1][-220:] if built else "missing")
    placed = log(args.slot, "Birthday cake placed at")
    check("log: cake placed on the counter", bool(placed), placed[-1][-160:] if placed else "missing")
    warns = log(args.slot, "Birthday cake skipped")
    check("log: no skipped build", not warns, warns[-1][-200:] if warns else "")
    code, o = s1(args.slot, "run", "baseddiag cake", "--until", "cake", "--max-wait", "15000")
    line = next((l.strip() for l in o.splitlines() if "date" in l and "cake" in l), "")
    check("baseddiag cake: shown", "cake shown at" in line, line[-220:] or o.strip()[-300:])

    shot(args.slot, CLOSEUP, args.out, "cake-closeup")
    # The label line is written while the player looks at the cake, once the prompt's text was measured.
    centred, hovered = [], []
    for _ in range(6):
        time.sleep(1.0)
        hovered = log(args.slot, "Birthday cake label shown")
        centred = [l for l in hovered if "centred over the cake" in l]
        if centred:
            break
    check("label shown and centred over the cake", bool(centred), centred[-1][-200:] if centred else
          (hovered[-1][-200:] if hovered else "no hover line"))
    shot(args.slot, ROOM, args.out, "cake-room")
    if args.force_date:
        s1(args.slot, "run", "baseddiag cake date off", "--max-wait", "5000")
    print("===== %d FAIL" % len(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
