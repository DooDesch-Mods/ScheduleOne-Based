"""
In-game check of Based#101: a wall item (Modern Wall Lamp) can be placed on every wall Based builds.

The game's wall ghost (BuildUpdate_Surface) is valid only where its look ray hits a collider with a Surface in its
parents, of a type the item takes, on an owned property, on the surface's allowed face. This completes the Storage
Unit's upper floor, the Bungalow's storey and sunroom and the Sweatshop's wing, gives the player a Modern Wall Lamp, and for each
wall stands 1.5 m in front of it, aims the lamp's ghost at the wall (`baseddiag aim ... modernwalllamp`) and reads it
back (`baseddiag ghost`): the ghost must be valid, and the readout names the collider the ray hit and its Surface.
A vanilla wall of the Bungalow's ground floor is the control.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage
Unit, the Bungalow and the Sweatshop (template `played`). Uses MapExpansions' Tests/employee-reach.py for the console
helpers.

    python _wt-Based-<topic>/Tests/wall-items.py [--slot mptest] [--only storage,bungalow,sunroom,sweatshop,control]
        [--mapexpansions MapExpansions] [--shots <folder>]
"""
import argparse
import importlib.util
import os
import re
import shutil
import sys
import time

ITEM = "modernwalllamp"
# Per site: the command that completes its build, the floor height, and its walls as (name, point on the wall,
# direction from the wall into the room). The ghost is aimed 1.5 m above the floor, the player stands 1.5 m inside.
SITES = {
    "storage": dict(cmds=["baseddiag site=storageunit phase ray.storageunit-bay:complete"], floor=3.25, walls=[
        ("Storage loft west wall (z 108)", (-8.75, 108.0), (1, 0)),
        ("Storage loft west wall (z 111.5)", (-8.75, 111.5), (1, 0)),
        ("Storage loft north wall", (-5.5, 113.8), (0, -1)),
        ("Storage east room east wall", (3.25, 108.8), (-1, 0)),
        ("Storage east room north wall", (1.5, 113.8), (0, -1)),
        ("Storage south room east wall", (3.25, 97.5), (-1, 0)),
        ("Storage south room south end wall", (1.5, 94.3), (0, 1)),
        ("Storage south room north wall", (2.0, 101.3), (0, -1)),
    ]),
    "bungalow": dict(cmds=["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete storey"],
                     floor=-0.65, walls=[
        ("Bungalow storey south-west room west wall", (-176.0, 114.0), (1, 0)),
        ("Bungalow storey south-west room south wall", (-173.0, 109.35), (0, 1)),
        ("Bungalow storey north-west room west wall", (-176.0, 120.5), (1, 0)),
        ("Bungalow storey north-west room north wall", (-173.0, 121.5), (0, -1)),
        ("Bungalow storey north-east room north wall", (-166.9, 121.5), (0, -1)),
        ("Bungalow storey north-east room east wall", (-163.85, 120.5), (-1, 0)),
    ]),
    # The sunroom (Content/Bungalow/BungalowSunroom.cs): glass fronts on three sides, the house's outer wall on the east.
    # The glass takes no wall item, like the game's own windows.
    "sunroom": dict(cmds=["baseddiag site=bungalow phase complete", "baseddiag site=bungalow phase complete sunroom"],
                    floor=-3.70, walls=[
        ("Bungalow sunroom house wall (middle)", (-176.17, 115.5), (-1, 0)),
        ("Bungalow sunroom house wall (south of the door)", (-176.17, 109.8), (-1, 0)),
        ("Bungalow sunroom house wall (north of the window)", (-176.17, 120.3), (-1, 0)),
        ("Bungalow sunroom house wall (under the window)", (-176.17, 118.4, -3.0), (-1, 0)),
        ("Bungalow sunroom west front (glass)", (-183.05, 117.0), (1, 0), False),
        ("Bungalow sunroom south end (glass)", (-179.5, 109.30), (0, 1), False),
        ("Bungalow sunroom north end (glass)", (-179.5, 121.40), (0, -1), False),
    ]),
    "sweatshop": dict(cmds=["baseddiag site=sweatshop phase complete"], floor=-0.25, walls=[
        ("Sweatshop wing east wall", (-53.99, 140.2), (-1, 0)),
        ("Sweatshop wing north wall", (-58.5, 145.0), (0, -1)),
        ("Sweatshop wing south wall", (-58.5, 136.0), (0, 1)),
    ]),
    "control": dict(cmds=[], floor=-3.7, walls=[
        ("control: Bungalow ground floor west wall (vanilla)", (-176.0, 120.5), (1, 0)),
    ]),
}
GHOST = re.compile(r"baseddiag ghost: wall item '([^']*)', valid (True|False), hit (.*), surface (.*)\.$")


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def wall(run, pick, name, floor, at, inward, shots, placeable=True):
    # at: (x, z), aimed 1.5 m above the floor, or (x, z, y) for a point at another height.
    x, z = at[0], at[1]
    y = at[2] if len(at) > 2 else floor + 1.5
    stand = (x + 1.5 * inward[0], floor + 1.2, z + 1.5 * inward[1])
    run.g.call("teleport", {"x": stand[0], "y": stand[1], "z": stand[2]}, timeout_ms=8000)
    time.sleep(1.0)
    aim = run.cmd("baseddiag aim %.2f %.2f %.2f - - %s" % (x, y, z, ITEM), wait=1.5)
    log = run.cmd("baseddiag ghost", wait=0.5)
    ghost = next((m for m in (GHOST.search(l.strip()) for l in log) if m), None)
    if ghost is None:
        run.check("%s: the lamp's wall ghost answers" % name, False,
                  pick(log, "baseddiag ghost") or pick(aim, "baseddiag") or "no ghost line")
        return
    shot = None
    if shots:
        taken = run.g.shot()
        if taken and os.path.isfile(taken):
            shot = os.path.join(shots, re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-") + ".png")
            shutil.copy(taken, shot)
    run.check("%s: Modern Wall Lamp %s" % (name, "placeable" if placeable else "not placeable"),
              ghost.group(1) == ITEM and ghost.group(2) == ("True" if placeable else "False"),
              "item %s, valid %s, hit %s, surface %s%s" % (ghost.group(1), ghost.group(2), ghost.group(3),
                                                          ghost.group(4), (", shot " + shot) if shot else ""))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--only", default=",".join(SITES))
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\MapExpansions",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    ap.add_argument("--shots", default="", help="folder for one screenshot per wall")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick
    if args.shots:
        os.makedirs(args.shots, exist_ok=True)

    run.cmd("settime 1200", wait=1.0)
    run.cmd("give %s 1" % ITEM, wait=1.5)
    for key in [k.strip() for k in args.only.split(",") if k.strip()]:
        site = SITES[key]
        for command in site["cmds"]:
            log = run.cmd(command, wait=10.0)
            run.check("%s: %s" % (key, command), any("construction moved to" in l or "nothing to" in l or
                                                     "are complete" in l for l in log), pick(log, "baseddiag"))
        for name, at, inward, *placeable in site["walls"]:
            wall(run, pick, name, site["floor"], at, inward, args.shots, *placeable)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
