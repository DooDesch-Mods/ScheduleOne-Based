"""Puts shots into one labelled grid for review: python Tests/contact-sheet.py out.png a.png b.png ..."""
import sys

from PIL import Image, ImageDraw

TILE = 640


def main():
    out, files = sys.argv[1], sys.argv[2:]
    if not files:
        print("FAIL no input files")
        sys.exit(1)
    cols = 3 if len(files) > 4 else 2
    rows = (len(files) + cols - 1) // cols
    tiles = []
    for f in files:
        im = Image.open(f).convert("RGB")
        im = im.resize((TILE, int(im.height * TILE / im.width)))
        tiles.append((f, im))
    h = max(im.height for _, im in tiles)
    sheet = Image.new("RGB", (cols * TILE, rows * h), "black")
    draw = ImageDraw.Draw(sheet)
    for i, (f, im) in enumerate(tiles):
        x, y = (i % cols) * TILE, (i // cols) * h
        sheet.paste(im, (x, y))
        label = f.replace("\\", "/").rsplit("/", 1)[-1]
        draw.rectangle([x, y, x + 8 * len(label) + 8, y + 18], fill="black")
        draw.text((x + 4, y + 3), label, fill="white")
    sheet.save(out)
    print(f"PASS sheet {out}: {len(files)} shots")


if __name__ == "__main__":
    main()
