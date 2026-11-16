"""Contact sheet of one build stage next to vanilla, one row per view, for day or night.

Each row shows the vanilla image of the view (or an empty tile when vanilla has none) and the stage's image. Prints
PASS with the sheet path, or FAIL when the stage has no image for the chosen time.

usage: storageunit-compare.py --stage _drafts/based-storageunit/stages/ray.storageunit-loft-loft-stair
       --vanilla _drafts/based-storageunit/vanilla --time day --out sheet-day.png [--only a,b]
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TILE_W, TILE_H, LABEL = 640, 360, 28


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", required=True)
    ap.add_argument("--vanilla", required=True)
    ap.add_argument("--time", choices=("day", "night"), required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="", help="comma-separated views, shown in this order")
    a = ap.parse_args()
    prefix = a.time + "-"
    views = sorted(f[len(prefix):-4] for f in os.listdir(a.stage) if f.startswith(prefix) and f.endswith(".png"))
    only = [v for v in a.only.split(",") if v]
    if only:
        missing = [v for v in only if v not in views]
        if missing:
            print("FAIL no %s images for %s in %s" % (a.time, ", ".join(missing), a.stage))
            sys.exit(1)
        views = only
    if not views:
        print("FAIL no %s images in %s" % (a.time, a.stage))
        sys.exit(1)
    stage_name = os.path.basename(os.path.normpath(a.stage))
    sheet = Image.new("RGB", (2 * TILE_W, len(views) * (TILE_H + LABEL)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except OSError as e:
        print("info: arial.ttf not found (%s), default font" % e)
        font = ImageFont.load_default()
    for row, view in enumerate(views):
        y = row * (TILE_H + LABEL)
        for col, (folder, title) in enumerate(((a.vanilla, "Vanilla"), (a.stage, stage_name))):
            path = os.path.join(folder, prefix + view + ".png")
            x = col * TILE_W
            draw.text((x + 8, y + 4), "%s - %s %s" % (title, a.time, view), fill=(230, 230, 230), font=font)
            if os.path.isfile(path):
                sheet.paste(Image.open(path).convert("RGB").resize((TILE_W, TILE_H)), (x, y + LABEL))
            else:
                draw.text((x + 8, y + LABEL + 8), "no image", fill=(160, 160, 160), font=font)
    sheet.save(a.out)
    print("PASS %s: %d views -> %s" % (a.time, len(views), a.out))


if __name__ == "__main__":
    main()
