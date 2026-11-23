"""Fixed shot set of one Bungalow build: same viewpoints before and after, day and night.

python Tests/bungalow-shots.py --build carport --slot mptest2 --label vanilla --time 1200
Copies each shot to _drafts/based-bungalow/<build>/<label>-<time>-<view>.png and prints PASS/FAIL per shot.
"""
import argparse
import json
import math
import pathlib
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
OUT = ROOT / "_drafts" / "based-bungalow"

VIEWS = {
    "carport": {
        "east": ["--eye", "-151", "-1.8", "108", "--target", "-166", "-2.5", "107.5"],
        "northeast": ["--eye", "-155", "-1.5", "116", "--target", "-166", "-2.5", "107"],
        "porch": ["--eye", "-168.5", "-2.0", "114.3", "--target", "-165", "-2.8", "104"],
        "west": ["--eye", "-178", "-1.5", "104", "--target", "-165", "-2.5", "108"],
        "far": ["--eye", "-166", "30", "160", "--target", "-166", "-3", "107"],
        "top": ["--topdown", "-175", "100", "-155", "118"],
        # North of the east pine, where the lumberjack works on the day the carport is ordered.
        "lumberjack": ["--eye", "-159.3", "-2.3", "108.3", "--target", "-161.5", "-3.0", "104.7"],
    },
    "deck": {
        # From the water west of the slope, at deck height.
        "sea": ["--eye", "-206", "-4.2", "113", "--target", "-190", "-4.6", "113"],
        "southwest": ["--eye", "-201", "-2.5", "101", "--target", "-190", "-4.5", "113"],
        # From the gravel at the sliding door, where the step leads up.
        "garden": ["--eye", "-178", "-2.1", "112.5", "--target", "-191", "-3.6", "113"],
        # The step from the gravel up onto the deck.
        "step": ["--eye", "-184.6", "-2.7", "116.8", "--target", "-188.3", "-3.9", "113"],
        "ondeck": ["--eye", "-189", "-2.0", "116", "--target", "-198", "-4.8", "109"],
        # Under the deck on the south slope, the stilts against the ground.
        "under": ["--eye", "-192.5", "-5.5", "105.6", "--target", "-191", "-4.6", "112"],
        "top": ["--topdown", "-198", "104", "-182", "122"],
        # East of the outer beam, where the carpenter works on the stilts day.
        "carpenter": ["--eye", "-186.2", "-3.0", "109.6", "--target", "-188.2", "-3.6", "112.2"],
    },
    "sunroom": {
        # From the gravel west of the room, the whole west front with the house behind.
        # No diagonal views: the pine belt around the west corners hides the room from every corner eye.
        "west": ["--eye", "-186.9", "-1.7", "114.2", "--target", "-178", "-2.6", "115.4"],
        # The roof meeting the house under its eave, seen from the south.
        "seam": ["--eye", "-179.6", "-1.3", "107.4", "--target", "-176.4", "-1.0", "110.6"],
        # Inside: from the sliding door across the room, and back toward the house.
        "inside": ["--eye", "-176.8", "-2.1", "110.2", "--target", "-182.8", "-2.7", "119.5"],
        "back": ["--eye", "-182.5", "-2.1", "120.8", "--target", "-176.2", "-2.6", "111"],
        "top": ["--topdown", "-188", "104", "-172", "125"],
        # North of the pines, where the woodcutter works on the day the room is ordered, and the west front on frames day.
        "woodcutter": ["--eye", "-180.5", "-2.6", "120.5", "--target", "-180.5", "-3.2", "117.5"],
        "carpenter": ["--eye", "-186.5", "-2.8", "115.2", "--target", "-183.3", "-3.3", "112.5"],
    },
    "storey": {
        # The whole house from the south-east gravel, stair and balcony in front.
        "southeast": ["--eye", "-152", "1.5", "99", "--target", "-169", "0", "115"],
        # The south front with the upper sliding door and its railing at the west end.
        "south": ["--eye", "-160", "-1.6", "104", "--target", "-170", "-0.3", "114", "--fov", "70"],
        # The stair's top step against the landing's east edge: from the south below it and from above.
        "stair-joint": ["--eye", "-166.6", "-0.3", "110.4", "--target", "-168.35", "-0.8", "112.1"],
        "stair-joint-top": ["--eye", "-167.2", "0.5", "112.1", "--target", "-168.5", "-0.75", "112.1"],
        "north": ["--eye", "-166", "1.5", "136", "--target", "-170", "0", "115"],
        # Along the stair from its foot, and down it from the landing.
        "stair-foot": ["--eye", "-160.5", "-2.4", "107.6", "--target", "-166.5", "-2.2", "112"],
        "landing": ["--eye", "-169.2", "1.0", "111.9", "--target", "-161", "-3.6", "111.9"],
        # The balcony on the porch, toward the copied porch door into the west room.
        "balcony": ["--eye", "-165.2", "1.0", "113.2", "--target", "-170", "0.3", "113.8"],
        # Upstairs: the west rooms from the south, the north-east room, and the upper door from inside.
        "upper-west": ["--eye", "-173", "1.0", "110.2", "--target", "-173", "0.2", "121"],
        "upper-ne": ["--eye", "-164.6", "1.0", "116.1", "--target", "-169.4", "0.2", "121"],
        "upper-door": ["--eye", "-172.5", "1.0", "113.5", "--target", "-176.2", "0.4", "111.4"],
        # Downstairs under the new floor: the ceiling must stay shut and shaded.
        "lower": ["--eye", "-171", "-2.0", "110", "--target", "-174", "-2.0", "120"],
        "top": ["--topdown", "-180", "105", "-158", "125"],
        # The crew's site east of the house from the pavement: table, fire barrel, generator, sign and cones.
        "site": ["--eye", "-157.8", "-2.2", "112.5", "--target", "-162.2", "-3.4", "118.5"],
        # The timber stock and the barrels north of the house.
        "stock": ["--eye", "-159.2", "-2.4", "125.6", "--target", "-162.6", "-3.5", "122.4"],
        # The fitter's weld at the stair's foot post from the pavement, away from a car parked on the loading dock.
        # Tests/crew-shots.py photographs him at whichever post he works.
        "weld": ["--eye", "-159.6", "-2.5", "110.2", "--target", "-162.4", "-3.2", "111.3"],
        # The balcony and its canopy from below, from the porch.
        "under-balcony": ["--eye", "-164.9", "-3.1", "114.6", "--target", "-167.5", "-0.9", "113.4"],
        # The landing from below, west of the loading dock where a parked car would hide it.
        "under-landing": ["--eye", "-168.3", "-3.3", "109.2", "--target", "-169.1", "-0.9", "112.1"],
        # Upward from the upper west room and from the balcony: the ceiling, or the sky before the roof goes on.
        "ceiling": ["--eye", "-173", "0.6", "113", "--target", "-173", "3.6", "116.5"],
        "balcony-up": ["--eye", "-166.5", "0.5", "113.8", "--target", "-166.5", "3.5", "116"],
        # Close views of the east and north fronts and the east corners from eye height on the ground: corner posts,
        # windows and the band between the floors, where the far views above lose the house behind pines.
        "east-front": ["--eye", "-156.5", "-1.2", "117.5", "--target", "-164", "-0.5", "117.5", "--fov", "70"],
        "north-front": ["--eye", "-170", "-1.0", "125.5", "--target", "-170", "-0.3", "121.2", "--fov", "70"],
        "corner-ne": ["--eye", "-158.5", "-1.2", "128", "--target", "-164.5", "-0.2", "121.2"],
        "corner-se": ["--eye", "-158.5", "-1.2", "104.5", "--target", "-164.5", "-0.2", "109.6"],
        # Pines stand around the west corners and close to the north wall, so the west front is shot between two
        # pines from the gravel, and the north and west fronts once more from above the treetops.
        "west-front": ["--eye", "-185", "-1.5", "109", "--target", "-175.5", "0.0", "114", "--fov", "70"],
        "north-high": ["--eye", "-170", "6", "133", "--target", "-170", "-0.5", "118", "--fov", "70"],
        "west-high": ["--eye", "-190", "6", "115", "--target", "-175", "-0.5", "115", "--fov", "70"],
    },
}


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def is_night(time):
    hhmm = int(time)
    return hhmm >= 2000 or hhmm < 600


def brighten(source, target):
    """Night shots are near black; a gamma lift brings the build out of the dark, the original stays beside it."""
    from PIL import Image, ImageStat
    image = Image.open(source).convert("RGB")
    mean = sum(ImageStat.Stat(image).mean) / 3
    # Lift the mean toward 110 of 255; gamma keeps lamps from clipping more than a plain gain would.
    gamma = 1.0 if mean <= 0 else max(.25, min(1.0, math.log(110 / 255) / math.log(max(mean, 1) / 255)))
    lut = [round(255 * (i / 255) ** gamma) for i in range(256)]
    lifted = image.point(lut * 3)
    lifted.save(target)
    return sum(ImageStat.Stat(lifted).mean) / 3


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", choices=sorted(VIEWS), required=True)
    ap.add_argument("--slot", default="mptest2")
    ap.add_argument("--label", required=True)
    ap.add_argument("--time", default="1200")
    ap.add_argument("--views", default="")
    a = ap.parse_args()
    views = VIEWS[a.build]
    out_dir = OUT / a.build
    out_dir.mkdir(parents=True, exist_ok=True)
    code, out = s1(a.slot, "time", a.time)
    print(("PASS" if code == 0 else "FAIL") + f" time {a.time}" + ("" if code == 0 else ": " + out.strip()[-300:]))
    failed = code != 0
    for name in (a.views.split(",") if a.views else list(views)):
        # A hitch while the game streams the new view can outlast the bridge's wait; that shot is taken once more.
        for attempt in range(2):
            code, out = s1(a.slot, "cam", "shot", *views[name], "--hud", "hide", "--settle", "20")
            if "did not finish" not in out:
                break
            print(f"NOTE {name}: the game did not answer in time, attempt {attempt + 1}")
        path = None
        try:
            path = json.loads(out[out.index("{"):]).get("path")
        except (ValueError, json.JSONDecodeError) as e:
            print(f"FAIL {name}: no JSON in the cam answer ({e}): {out.strip()[-300:]}")
            failed = True
            continue
        if code != 0 or not path or not pathlib.Path(path).exists():
            print(f"FAIL {name}: exit {code}, path {path}")
            failed = True
            continue
        target = out_dir / f"{a.label}-{a.time}-{name}.png"
        shutil.copyfile(path, target)
        print(f"PASS {name}: {target}")
        if is_night(a.time):
            lit = target.with_name(target.stem + "-lit.png")
            print(f"PASS {name} lit: {lit} (mean {brighten(target, lit):.0f} of 255)")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
