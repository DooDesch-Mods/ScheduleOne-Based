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
    "stair-side": ((-17, 2.2, 101.5), (-9.8, 2, 101.5)),
    "stair-foot": ((-11.5, 1.7, 94.5), (-9.8, 3, 105)),
    "stair-landing": ((-10.2, 5, 106), (-9.8, 1, 98)),
    # Close-ups of the stair's joints: the top tread at the landing, the treads along the wall from above, the foot
    # on the grass from the side and from the front, and the landing against the wall.
    "stair-top-join": ((-12.5, 3.0, 103.6), (-9.8, 3.1, 104.6)),
    "stair-wall-gap": ((-11, 4.6, 99.6), (-9.1, 1.4, 102.5)),
    "stair-foot-side": ((-12.8, 0.7, 99.2), (-9.8, 0.2, 99.2)),
    "stair-foot-front": ((-9.8, 1.3, 96.2), (-9.8, 0.2, 99.3)),
    "landing-wall": ((-12, 3.0, 104.2), (-9.0, 3.25, 105.4)),
    # The upper door: straight on from outside, from the landing, and the loft above Unit 1 from inside.
    "door-front": ((-14, 4.5, 105.5), (-9, 4.4, 105.5)),
    # Close-ups of the joint where the stair meets the landing: from above on the door-front side, and level from the west.
    "joint-front": ((-11.9, 4.3, 104.1), (-9.5, 3.2, 104.6)),
    "joint-side": ((-12.4, 3.35, 104.6), (-9.84, 3.15, 104.6)),
    "door-outside": ((-10.4, 4.9, 105.5), (-7.5, 4.3, 105.5)),
    "loft-door": ((-4, 4.9, 108), (-8.75, 4.2, 105.5)),
    "loft-north": ((-7.2, 4.9, 105.4), (-3, 4, 113.5)),
    "loft-south": ((-3, 4.9, 113.3), (-8.2, 4, 105.2)),
    # B: from the loft through its east wall, the walkway's south end with the site wall, the east room from its corner.
    "loft-east": ((-7.5, 4.9, 109), (3, 4.3, 109)),
    "walkway-site-wall": ((-0.9, 4.9, 113.3), (-0.9, 4.2, 103.8)),
    "east-room": ((2.9, 4.9, 113.4), (-2, 4, 104.5)),
    "south-room": ((2.9, 4.9, 94.4), (-1.5, 4, 101.5)),
    "elevator-corner": ((-1.7, 4.9, 95.5), (0.8, 4.3, 102.8)),
    # The yard west of the stair with the fire barrel and the trash, from the street and close up.
    "yard-strip": ((-13.8, 1.9, 106.8), (-11.3, 0.4, 101.0)),
    "yard-fire": ((-10.8, 1.8, 99.7), (-12.35, 0.7, 102.35)),
    # The interior ladder from the hall floor and down from the loft, and the trash of a finished stage.
    "ladder-hall": ((-2.6, 1.6, 99.5), (-2.65, 2.0, 103.3)),
    "ladder-loft": ((-3.5, 4.9, 107.5), (-2.65, 3.0, 103.3)),
    "litter-loft": ((-3.0, 4.9, 113.0), (-6.5, 3.2, 107.5)),
    "litter-east": ((2.9, 4.9, 113.4), (0.5, 3.2, 107.0)),
    # Where the crew works on the night of each stage: the wall the door is cut through, the loft's south edge, the
    # walkway's east face and the south room.
    "door-sparks": ((-12.6, 4.6, 103.0), (-9.4, 4.4, 105.5)),
    "loft-sparks": ((-4.5, 4.6, 109.0), (-6.5, 5.4, 104.0)),
    "loft-clutter": ((-4.2, 4.9, 106.2), (-7.2, 3.6, 112.6)),
    "walkway-sparks": ((-1.7, 4.8, 105.8), (0.35, 4.75, 108.8)),
    "south-clutter": ((-0.9, 4.9, 101.2), (1.9, 3.5, 95.4)),
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
