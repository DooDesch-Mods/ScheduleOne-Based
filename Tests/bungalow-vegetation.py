"""
The crew's kits on the Bungalow stand clear of the plants and of the garden furniture, and on level ground.

For each build stage that sets out a kit (deck stilts, sunroom base, sunroom glass), every part of the kit (table,
pallet, bucket, offcuts) is found by the vanilla prop it copies and read from the running game as renderer bounds and compared on the ground plane with:

  shrub      the bushes (Grass layer, SGS_Shrub)          at least 0.6 m between the bounds
  furniture  the Round Outdoor Set (table and chairs)     at least 0.6 m
  trunk      the tree trunks (their nav mesh cut)         at least 0.3 m

The ground height under the four corners of each table and pallet is printed as INFO, with the spread between them.
A spread above 0.15 m fails: the part would stand tilted on the slope or float at one corner.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Bungalow.

    python _wt-Based-bungalow/Tests/bungalow-vegetation.py [--slot mptest] [--mapexpansions <ME checkout>]
"""
import argparse
import importlib.util
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
S1 = os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge", "s1.py")
SITE = "site=bungalow"
# (build, phase, root): the stages that set out a kit, and the path part of the build's own objects.
STAGES = [
    ("sunroom", "sunroom-base", "based_bungalow_sunroom/"),
    ("sunroom", "sunroom-glass", "based_bungalow_sunroom/"),
    ("deck", "deck-stilts", "based_bungalow_deck/"),
]
RECT = (-192.0, 104.0, -176.0, 124.0)
# Kit parts by the vanilla prop they copy, under the build's own copies.
PARTS = [("table", "Table_Foldout"), ("pallet", "Pallet Prop"), ("pallet", "SM_Prop_Plank"), ("bucket", "SM_Prop_Bucket"), ("offcuts", ".Panel.piece-")]
MIN_GAP = {"shrub": .6, "furniture": .6, "trunk": .3}
MAX_SPREAD = .15


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def s1(slot, *args):
    proc = subprocess.run([sys.executable, S1, "--slot", slot] + list(args), capture_output=True, text=True)
    text = proc.stdout
    try:
        return json.loads(text[text.index("{"):])
    except ValueError:
        print("WARN  s1.py %s gave no JSON: %s" % (" ".join(args[:2]), (proc.stderr or text).strip()[-200:]), flush=True)
        return {}


def renderers(slot):
    x0, z0, x1, z1 = RECT
    data = s1(slot, "find", "--rect", str(x0), str(z0), str(x1), str(z1), "--kind", "renderer", "-n", "3000")
    objects = data.get("objects", [])
    if data and data.get("matched", 0) > len(objects):
        print("WARN  find matched %s renderers, read %d" % (data.get("matched"), len(objects)), flush=True)
    return objects


def flat(o):
    b = o["bounds"]
    return (b["min"][0], b["min"][2], b["max"][0], b["max"][2])


def union(boxes):
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


def gap(a, b):
    dx = max(b[0] - a[2], a[0] - b[2], 0.0)
    dz = max(b[1] - a[3], a[1] - b[3], 0.0)
    return (dx * dx + dz * dz) ** .5


def obstacles(objects):
    found = []
    for o in objects:
        if not o.get("active", True):
            continue
        path, name = o.get("path", ""), o.get("name", "")
        if "SGS_Shrub" in name:
            found.append(("shrub", path.split("/")[-2], flat(o)))
        elif "Round Outdoor" in path:
            found.append(("furniture", "/".join(path.split("/")[-3:-1]), flat(o)))
        elif "Tree rustle" in path and "Nav mesh cut" in name:
            found.append(("trunk", path.split("/")[-2], flat(o)))
    return found


def kit_parts(objects, root):
    """The kit's parts (table, pallet with its planks, bucket, offcuts) among the build's own renderers."""
    parts = {}
    for o in objects:
        path = o.get("path", "")
        if not o.get("active", True) or root not in path:
            continue
        kind = next((k for k, mark in PARTS if mark in path), None)
        if kind:
            parts.setdefault(kind, []).append(flat(o))
    return {k: union(v) for k, v in parts.items()}


def ground(slot, x, z):
    data = s1(slot, "ground", "--", "%.3f" % x, "%.3f" % z)
    value = data.get("ground")
    return value if isinstance(value, (int, float)) else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\_wt-MapExpansions-copy-colliders",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run = reach.Run(args.slot)

    run.cmd("settime 1200", wait=1.0)
    for build, phase, prefix in STAGES:
        run.cmd("baseddiag %s phase rights %s" % (SITE, build), wait=8.0)
        log = run.cmd("baseddiag %s phase %s %s" % (SITE, phase, build), wait=10.0)
        applied = any("construction moved to" in l for l in log)
        label = "%s %s" % (build, phase)
        if not run.check("%s: state applied" % label, applied, reach.pick(log, "baseddiag") or "no reply"):
            continue
        objects = renderers(args.slot)
        parts = kit_parts(objects, prefix)
        if not run.check("%s: kit parts found" % label, len(parts) >= 2, sorted(parts)):
            continue
        near = obstacles(objects)
        print("INFO  %s: %d kit parts, %d plants and furniture in the area" % (label, len(parts), len(near)), flush=True)
        for part, box in sorted(parts.items()):
            worst = {}
            for kind, name, other in near:
                d = gap(box, other)
                if kind not in worst or d < worst[kind][0]:
                    worst[kind] = (d, name)
            bad = {k: "%s %.2f m" % (n, d) for k, (d, n) in worst.items() if d < MIN_GAP[k]}
            closest = ", ".join("%s %s %.2f m" % (k, n, d) for k, (d, n) in sorted(worst.items()))
            run.check("%s: %s clear of plants and furniture" % (label, part), not bad, bad or closest)
            if part in ("table", "pallet"):
                x0, z0, x1, z1 = box
                heights = [ground(args.slot, x, z) for x, z in ((x0, z0), (x1, z0), (x0, z1), (x1, z1))]
                known = [h for h in heights if h is not None]
                spread = max(known) - min(known) if len(known) == 4 else None
                print("INFO  %s: %s ground at corners %s" % (label, part, [None if h is None else round(h, 2) for h in heights]),
                      flush=True)
                run.check("%s: %s on level ground" % (label, part), spread is not None and spread <= MAX_SPREAD,
                          "spread %s m" % (None if spread is None else round(spread, 3)))
    run.cmd("baseddiag %s phase complete" % SITE, wait=6.0)
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
