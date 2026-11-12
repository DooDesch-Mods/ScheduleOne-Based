"""Fixed viewpoints of the Storage Unit building, shot at one time of day.

Used for the vanilla image set before building and for the same views after each construction night, so every stage
can be compared to vanilla from the same place. Prints PASS/FAIL per view and copies each image to OUT/<prefix>-<view>.png.

usage: storageunit-shots.py --slot mptest --time 1200 --prefix day --out _drafts/based-storageunit/vanilla [--only a,b]
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"

# name: (eye x y z, target x y z). World metres; the hall floor is y 0.1, the upper level y 3.25.
VIEWS = {
    "roof-sw": ((-1, 26, 80), (-3, 6, 104)),
    "roof-ne": ((14, 18, 126), (-3, 6, 104)),
    "west-wall": ((-13, 4.5, 91), (-9.5, 3, 104)),
    "west-wall-north": ((-13, 4.5, 117), (-9.5, 3, 102)),
    "hall-north": ((-4, 1.7, 95), (-5, 3, 103.8)),
    "hall-west": ((-2.5, 1.7, 99), (-8.8, 3, 99)),
    "hall-east": ((-7.5, 1.7, 99), (0, 2.5, 99)),
    "hall-south": ((-5, 1.7, 102.8), (-5, 2, 93)),
    "hall-up": ((-5, 1.7, 97), (-4, 7, 102)),
    "corridor": ((-2.8, 1.7, 104.3), (-2.8, 1.5, 113.5)),
    "upper-walkway-north": ((-1.1, 4.9, 94.6), (-1.1, 4.5, 113)),
    "upper-walkway-south": ((-1.1, 4.9, 113.3), (-1.1, 4.5, 95)),
    "upper-over-hall": ((-1.4, 5.2, 102), (-8, 2, 97)),
    # The outside stair on the west wall: from the side, from its foot, and down from the landing.
    "stair-side": ((-17, 2.2, 100.5), (-9.8, 2, 100.5)),
    "stair-foot": ((-11.5, 1.7, 93.5), (-9.8, 3, 104)),
    "stair-landing": ((-10.2, 5, 105), (-9.8, 1, 97)),
    # The upper door from the landing, and the loft above Unit 1 from inside.
    "door-outside": ((-10.4, 4.9, 104.5), (-7.5, 4.3, 104.5)),
    "loft-door": ((-4, 4.9, 107), (-8.75, 4.2, 104.5)),
    "loft-north": ((-7.2, 4.9, 104.4), (-3, 4, 113.5)),
    "loft-south": ((-3, 4.9, 113.3), (-8.2, 4, 104.2)),
    # B: from the loft through its east wall, the walkway's south end with the site wall, the east room from its corner.
    "loft-east": ((-7.5, 4.9, 109), (3, 4.3, 109)),
    "walkway-site-wall": ((-0.9, 4.9, 113.3), (-0.9, 4.2, 103.8)),
    "east-room": ((2.9, 4.9, 113.4), (-2, 4, 104.5)),
    "south-room": ((2.9, 4.9, 94.4), (-1.5, 4, 101.5)),
    "elevator-corner": ((-1.7, 4.9, 95.5), (0.8, 4.3, 102.8)),
    # D: both loading bays from the hall's south entrance.
    "hall-bays": ((-5.3, 2.2, 93.9), (-5.3, 0.3, 101.5)),
}


def s1(slot, *args):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True)
    return out.returncode, (out.stdout + out.stderr).strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--time", required=True)
    ap.add_argument("--prefix", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    only = [v for v in a.only.split(",") if v]
    code, text = s1(a.slot, "time", a.time)
    if code != 0:
        print("FAIL time %s: %s" % (a.time, text[-400:]))
        sys.exit(1)
    failed = 0
    try:
        for name, (eye, target) in VIEWS.items():
            if only and name not in only:
                continue
            code, text = s1(a.slot, "cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target),
                            "--fov", "70", "--hud", "hide", "--settle", "10")
            try:
                path = json.loads(text)["path"]
            except (ValueError, KeyError) as e:
                path = ""
                text = "no path in cam output (%s): %s" % (e, text)
            if code != 0 or not os.path.isfile(path):
                failed += 1
                print("FAIL %s: %s" % (name, text[-400:]))
                continue
            dest = os.path.join(a.out, "%s-%s.png" % (a.prefix, name))
            shutil.copyfile(path, dest)
            print("PASS %s -> %s" % (name, dest))
    finally:
        code, text = s1(a.slot, "cam", "release")
        if code != 0:
            print("FAIL cam release: %s" % text[-400:])
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
