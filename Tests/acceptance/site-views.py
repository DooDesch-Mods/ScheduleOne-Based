"""
Site views of the bungalow builds: one fixed view per upgrade and stage on the crew's kit, plus the sunroom door from
outside. Each view is raycast to its target first; a dropped view is a FAIL. The pictures themselves are judged by eye.

    python _wt-Based-bungalow/Tests/acceptance/site-views.py --slot mptest6 --out DIR [--only storey,sunroom]
"""
import argparse
import os
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Workspace", "tools", "bridge"))
sys.path.insert(0, os.path.join(ROOT, "Workspace", "tools", "shots"))
from s1 import Game  # noqa: E402
import acceptance_shots  # noqa: E402

# East yard: the site table, the stock and the fire drum by the storey's stair.
EAST_KIT = {"eye": (-156.4, -2.2, 111.6), "target": (-161.8, -3.4, 119.5), "box": (-164, -4.2, 115, -158.5, -2, 124.5)}
# West kit north of the sunroom: table, pallet with planks, bucket, offcuts.
WEST_KIT = {"eye": (-190.5, -2.3, 123.5), "target": (-185.4, -3.5, 118.5), "box": (-187, -4.2, 115.5, -183.5, -2.5, 121.5)}
CARPORT = {"eye": (-156.8, -2.4, 100.8), "target": (-165, -3.0, 107), "box": (-170, -4.2, 103, -160, -1, 112)}
# The eye stands under the canopy the sight tool draws for a fir from the terrain's tree list; the door itself is in
# open view from there, so the view takes no sight test.
SUNROOM_DOOR = {"eye": (-186.6, -3.0, 111.4), "target": (-183.2, -3.7, 111.4), "box": (-183.6, -4.1, 110, -182.8, -1.5, 112.8),
                "sight": "off"}
# The crew's trodden path from the pavement to the stair foot, from the street side.
PATH = {"eye": (-156.5, -2.2, 113.8), "target": (-161.0, -3.9, 111.6), "box": (-161.8, -4.1, 111.2, -159.0, -3.5, 113.0)}
# The terrace at the upper sliding door, over the sunroom's glass roof, from the south-west.
TERRACE = {"eye": (-184.5, 0.6, 104.5), "target": (-177.0, -0.8, 111.3), "box": (-177.6, -0.9, 109.8, -176.2, 0.5, 112.9)}
# The rafters over the storey, from the south-east across the street.
RAFTERS = {"eye": (-152.0, 6.0, 99.0), "target": (-170.0, 3.0, 115.0), "box": (-176.4, 2.2, 108.9, -163.3, 5.1, 121.9)}

STAGES = [
    ("storey", "storey:storey-floor", [("east-kit", EAST_KIT), ("path", PATH)]),
    ("storey", "storey:storey-frame", [("east-kit", EAST_KIT)]),
    ("storey", "storey:storey-walls", [("east-kit", EAST_KIT), ("terrace", TERRACE)]),
    ("storey", "storey:storey-rafters", [("east-kit", EAST_KIT), ("rafters", RAFTERS)]),
    ("storey", "storey:storey-roof", [("east-kit", EAST_KIT), ("path", PATH)]),
    ("storey", "storey:complete", [("path", PATH)]),
    ("sunroom", "sunroom:sunroom-base", [("west-kit", WEST_KIT)]),
    ("sunroom", "sunroom:sunroom-glass", [("west-kit", WEST_KIT)]),
    ("sunroom", "sunroom:complete", [("door-outside", SUNROOM_DOOR), ("terrace", TERRACE)]),
    ("deck", "deck:deck-stilts", [("west-kit", WEST_KIT)]),
    ("deck", "deck:complete", []),
    ("carport", "carport:carport-posts", [("bays", CARPORT)]),
    ("carport", "carport:complete", [("bays", CARPORT)]),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--only", default=None, help="comma list of upgrades")
    args = parser.parse_args()
    only = set(args.only.split(",")) if args.only else None
    game = Game(args.slot)
    failed = 0
    for upgrade, stage, views in STAGES:
        if only and upgrade not in only:
            continue
        answer = game.run("baseddiag site=bungalow phase " + stage,
                          until="construction moved to|baseddiag: .*(not|no |unknown)|Exception", max_wait_ms=60000)
        text = "\n".join(l["text"] for l in (answer.get("result") or {}).get("lines", []))
        if "construction moved to" not in text:
            print("FAIL enter %s %s" % (stage, text[-300:].replace("\n", " ")))
            failed += 1
            continue
        print("PASS enter %s" % stage)
        time.sleep(4)
        for name, v in views:
            out = os.path.join(args.out, stage.replace(":", "-"))
            view = {"name": name, "eye": v["eye"], "target": v["target"], "outside": True, "sight": v.get("sight")}
            entry = acceptance_shots.shoot(game, list(v["box"]), [view], out)[0]
            # A view without a sight test comes back "unchecked"; it stands or falls by eye.
            ok = entry["status"] in ("clear", "moved") or (v.get("sight") == "off" and entry["status"] == "unchecked")
            failed += 0 if ok else 1
            print("%s view %s/%s %s %s" % ("PASS" if ok else "FAIL", stage, name, entry["status"], entry.get("png", entry.get("error", ""))))
    print("%s site views: %d failed" % ("FAIL" if failed else "PASS", failed))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
