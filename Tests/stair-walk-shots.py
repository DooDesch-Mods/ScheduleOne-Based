"""Photographs an employee on the storey's stair: runs storey-function.py (botanist with supplies and drying rack
upstairs) and, while it waits for the botanist, polls his position and takes a shot each time he is on the stair.

python Tests/stair-walk-shots.py --slot mptest6
Copies the shots to _drafts/based-bungalow/storey/stair-walk-<n>.png and prints PASS/FAIL.
"""
import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
OUT = ROOT / "_drafts" / "based-bungalow" / "storey"
WORK = re.compile(r"mxreach work: (\S+) at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)")


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def on_stair(x, y, z):
    # The stair climbs west from its foot at the gravel to the landing's east edge, about 1 m either side of z 111.9.
    return -168.4 < x < -161.5 and 110.6 < z < 113.2 and -3.6 < y < -1.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest6")
    ap.add_argument("--shots", type=int, default=3)
    ap.add_argument("--minutes", type=float, default=8)
    a = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    log = open(OUT / "stair-walk-function.log", "w", encoding="utf-8")
    function = subprocess.Popen([sys.executable, str(HERE / "storey-function.py"), "--slot", a.slot, "--reloads", "0"],
                                stdout=log, stderr=subprocess.STDOUT)
    taken, seen, deadline = [], None, time.time() + a.minutes * 60
    while time.time() < deadline and len(taken) < a.shots and function.poll() is None:
        code, out = s1(a.slot, "run", "mxreach work bungalow", "--until", "mxreach work:", "--max-wait", "4000")
        # Every employee of the property is listed; the one who carries the weed upstairs need not be the first.
        found = [(m.group(1), float(m.group(2)), float(m.group(3)), float(m.group(4))) for m in WORK.finditer(out)]
        if not found:
            time.sleep(1)
            continue
        seen = found
        climbing = next((f for f in found if on_stair(*f[1:])), None)
        if not climbing:
            time.sleep(.5)
            continue
        name, x, y, z = climbing
        code, out = s1(a.slot, "cam", "shot", "--eye", "%.2f" % (x + 1.2), "%.2f" % (y + 1.3), "107.6",
                       "--target", "%.2f" % x, "%.2f" % (y + .9), "%.2f" % z, "--hud", "hide", "--settle", "2")
        try:
            path = json.loads(out[out.index("{"):]).get("path")
        except (ValueError, json.JSONDecodeError) as e:
            print("FAIL shot: no JSON in the cam answer (%s): %s" % (e, out.strip()[-200:]), flush=True)
            continue
        target = OUT / ("stair-walk-%d.png" % (len(taken) + 1))
        shutil.copyfile(path, target)
        taken.append(target)
        print("PASS %s on the stair at (%.2f, %.2f, %.2f): %s" % (name, x, y, z, target), flush=True)
    if function.poll() is None:
        function.wait(timeout=600)
    log.close()
    print("INFO storey-function exit %s, log %s" % (function.returncode, OUT / "stair-walk-function.log"))
    if not taken:
        print("FAIL no shot of an employee on the stair; last positions %s" % (seen,))
    print("%s stair-walk-shots: %d shots" % ("PASS" if taken else "FAIL", len(taken)))
    sys.exit(0 if taken else 1)


if __name__ == "__main__":
    main()
