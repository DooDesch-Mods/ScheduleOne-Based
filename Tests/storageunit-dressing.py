"""The site dressing of the Storage Unit: trash props, the yard fire and the crew's sparks, their frame cost, and the
project folder in hand, on the hotbar, on a wall and open.

For each stage it jumps there, forces the effects on, checks the log lines that place them and shoots the dressing
views by day and by night. Then it measures frame time with the effects off and forced on from one fixed view, and
shoots the folder. Prints PASS/FAIL per check and writes images to OUT.

usage: storageunit-dressing.py --slot mptest2 --out _drafts/based-storageunit/dressing
"""
import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"

# stage name, phase to jump to, views, effects expected with `vfx force`
STAGES = [
    ("a-night1", "ray.storageunit-loft:loft-stair", "yard-strip,yard-fire,door-sparks,stair-side", ["yard-fire", "work-sparks"]),
    ("a-night2", "ray.storageunit-loft:loft-door", "yard-strip,yard-fire,loft-sparks,loft-clutter,loft-north,loft-south", ["yard-fire", "work-sparks"]),
    ("a-night3", "ray.storageunit-loft:complete", "yard-strip,yard-fire,stair-side,stair-foot", ["yard-fire", "work-sparks"]),
    ("b-night1", "ray.storageunit-east:east-open", "walkway-sparks,walkway-site-wall,upper-walkway-north", ["yard-fire", "work-sparks"]),
    ("c-night1", "ray.storageunit-south:south-open", "south-clutter,south-room,elevator-corner", ["yard-fire", "work-sparks"]),
    ("c-night2", "ray.storageunit-south:complete", "yard-strip,yard-fire,south-room", ["yard-fire"]),
]

failed = 0


def s1(slot, *args, timeout=180):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def slot_root(slot):
    out = subprocess.run([sys.executable, os.path.join(os.path.dirname(S1), "..", "slots", "slots.py"), "--json"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    try:
        return next((s["root"] for s in json.loads(out.stdout)["slots"] if s["slot"] == slot), "")
    except (ValueError, KeyError) as e:
        print("      slot root of %s unknown (%s); screenshots are not checked against it" % (slot, e), flush=True)
        return ""


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""))


def run(slot, until, *command):
    return s1(slot, "run", "--until", until, *command)[1]


def set_mode(slot, mode, expected):
    """Sets the effects mode and polls the state line until the expected effects are live (none for off); the mod
    checks once a second. Returns the live list of the last state line."""
    names = ""
    for attempt in range(12):
        words = ["baseddiag", "site=storageunit", "vfx"] + ([mode] if attempt == 0 else [])
        text = run(slot, "baseddiag: storage effects", *words)
        live = re.search(r"baseddiag: storage effects mode \w+, live (.*)", text)
        names = live.group(1).strip() if live else ""
        if (not expected and names.startswith("none")) or (expected and all(e in names for e in expected)):
            return names
        subprocess.run([sys.executable, "-c", "import time; time.sleep(0.5)"])
    return names


def stages(slot, out, only=None):
    for name, phase, views, effects in STAGES:
        if only and name not in only:
            continue
        text = run(slot, "construction moved to|baseddiag: .*(fail|rror)", "baseddiag", "site=storageunit", "phase", phase)
        if "construction moved to" not in text:
            report(False, "jump %s" % phase, text[-400:])
            continue
        report(True, "jump %s" % phase)
        names = set_mode(slot, "force", effects)
        for effect in effects:
            report(effect in names, "%s effect %s live" % (name, effect), names[:200] or "no state line")
        # A placed effect can still emit nothing; a few looks at the particle count tell a running one.
        for effect in effects:
            counts = []
            for _ in range(6):
                text = run(slot, "baseddiag: storage effects", "baseddiag", "site=storageunit", "vfx")
                m = re.search(re.escape(effect) + r" at \([^)]*\) with (\d+) particle", text)
                counts.append(int(m.group(1)) if m else -1)
            report(max(counts) > 0, "%s effect %s emits" % (name, effect), "particles per look: %s" % counts)
        for prefix, time in (("day", "1200"), ("night", "0100")):
            r = subprocess.run([sys.executable, os.path.join(HERE, "storageunit-shots.py"), "--slot", slot, "--time", time,
                                "--prefix", prefix, "--out", os.path.join(out, name), "--only", views],
                               capture_output=True, text=True)
            print(r.stdout.strip())
            if r.returncode != 0:
                report(False, "%s shots %s" % (name, prefix), r.stderr[-300:])
    run(slot, "baseddiag: storage effects", "baseddiag", "site=storageunit", "vfx", "on")


def frame_cost(slot):
    # The door stage at night: the fire barrel and the sparks on the wall are both in front of the camera.
    text = run(slot, "construction moved to|baseddiag: .*(fail|rror)", "baseddiag", "site=storageunit", "phase", "ray.storageunit-loft:loft-stair")
    if "construction moved to" not in text:
        report(False, "frame cost jump", text[-300:])
        return
    s1(slot, "time", "0100")
    s1(slot, "cam", "set", "--eye", "-13.4", "3.2", "100.5", "--target", "-10.4", "2.2", "104.6", "--hud", "hide")
    results = {}
    try:
        for mode in ("off", "force", "off", "force"):
            names = set_mode(slot, mode, [] if mode == "off" else ["yard-fire", "work-sparks"])
            print("effects %s: live %s" % (mode, names[:200]))
            code, text = s1(slot, "frames", "10", timeout=60)
            try:
                stats = json.loads(text[text.find("{"): text.rfind("}") + 1])
            except ValueError:
                report(False, "frames with effects %s" % mode, text[-300:])
                continue
            results.setdefault(mode, []).append(stats)
            print("frames effects %-5s %s" % (mode, json.dumps(stats)[:300]))
    finally:
        s1(slot, "cam", "release")
        run(slot, "baseddiag: storage effects", "baseddiag", "site=storageunit", "vfx", "on")
    if results.get("off") and results.get("force"):
        def mean(mode, key):
            return sum(r[key] for r in results[mode]) / len(results[mode])
        report(True, "frame cost of the effects", "avg %.2f ms without, %.2f ms with (%+.2f ms); p95 %.2f / %.2f ms" % (
            mean("off", "avgMs"), mean("force", "avgMs"), mean("force", "avgMs") - mean("off", "avgMs"),
            mean("off", "p95Ms"), mean("force", "p95Ms")))
    else:
        report(False, "frame cost of the effects", "no measurement for both modes: %s" % json.dumps(results)[:300])


def folder(slot, out):
    os.makedirs(out, exist_ok=True)

    def shot(name):
        code, text = s1(slot, "shot")
        path = text.strip().splitlines()[-1].strip() if text else ""
        m = re.search(r"([A-Z]:\\[^\"]+\.png)", text)
        if m:
            path = m.group(1)
        if code == 0 and os.path.isfile(path):
            shutil.copy(path, os.path.join(out, name + ".png"))
            report(True, "folder shot %s" % name)
        else:
            report(False, "folder shot %s" % name, text[-300:])

    s1(slot, "time", "1200")
    # The loft at the finished first stage; the player faces the loft's north wall from its middle.
    run(slot, "construction moved to|baseddiag: .*(fail|rror)", "baseddiag", "site=storageunit", "phase", "ray.storageunit-loft:complete")
    s1(slot, "tp", "-5.5", "3.3", "110.3", "--yaw", "0")
    text = run(slot, "baseddiag: folder given|rror", "baseddiag", "site=storageunit", "folder", "give")
    report("baseddiag: folder given" in text, "folder given", text[-200:])
    text = run(slot, "baseddiag: folder in hand|rror|not on the hotbar", "baseddiag", "site=storageunit", "folder", "hold")
    report("baseddiag: folder in hand" in text, "folder in hand", text[-200:])
    shot("hand-and-hotbar")
    # A second folder from the item's definition goes on the wall nearest to the player, the north wall.
    text = run(slot, "baseddiag: folder hung|rror", "baseddiag", "site=storageunit", "folder", "wall")
    report("baseddiag: folder hung" in text, "folder hung on a wall", text[-200:])
    # With the folder in hand its placement preview sits on the same wall; an empty slot leaves only the hung one.
    text = run(slot, "baseddiag: hands free|rror", "baseddiag", "site=storageunit", "folder", "drop")
    report("baseddiag: hands free" in text, "hands free", text[-200:])
    shot("on-wall")
    # Last, because the plan stays open over the view.
    text = run(slot, "Project folder opened|rror", "baseddiag", "site=storageunit", "folder", "open")
    report("Project folder opened" in text, "folder opened", text[-200:])
    shot("plan-open")


# The plan in every state of the site: file prefix, label on the sheet, the jump, and whether it is also hung on a wall.
PLAN_STATES = [
    ("01-bought", "1  Gekauft, nichts gebaut", "ray.storageunit-loft:none", True),
    ("02-building", "2  Bau läuft (Obergeschoss, Nacht 2 von 3)", "ray.storageunit-loft:loft-door", False),
    ("03-loft-done", "3  Obergeschoss fertig", "ray.storageunit-loft:complete", False),
    ("04-east-done", "4  Ostabteile fertig", "ray.storageunit-east:complete", False),
    ("05-south-done", "5  Südabteile fertig", "ray.storageunit-south:complete", False),
    ("06-all-done", "6  Laderampe fertig = alles gebaut", "ray.storageunit-bay:complete", True),
]
# On the ground floor, which stands in every state: the middle of the hall facing north. The folder hangs only on the
# property's own walls, which are those of Unit 1: the west wall, at two places so the first folder is not in the
# second shot. A teleport onto the unit floor from 0.1 m can fall through it, hence 0.6 m.
HALL_MIDDLE = ("-5", "0.1", "98.5", "0")
HALL_WALLS = {"01-bought": ("-7.0", "0.6", "108.0", "270"), "06-all-done": ("-7.0", "0.6", "112.5", "270")}


def plan_states(slot, out):
    os.makedirs(out, exist_ok=True)

    root = slot_root(slot)
    size = []

    def shot(name, cam=None):
        # A path outside this slot's game folder, or a size other than this game's first shot, is another game's
        # screenshot; take a new one. cam = (eye, target) shoots from a free camera instead of the player's.
        text = ""
        for attempt in range(3):
            if cam:
                code, text = s1(slot, "cam", "shot", "--eye", *["%.2f" % v for v in cam[0]],
                                "--target", *["%.2f" % v for v in cam[1]], "--hud", "hide")
            else:
                code, text = s1(slot, "shot")
            m = re.search(r"([A-Z]:\\[^\"\r\n]+\.png)", text)
            path = m.group(1).replace("\\\\", "\\") if m else ""
            ok = code == 0 and os.path.isfile(path) and (not root or os.path.normcase(path).startswith(os.path.normcase(root)))
            # The free camera renders its own size; only the player's view is compared with the first shot.
            if ok and not cam:
                with Image.open(path) as im:
                    if not size:
                        size.append(im.size)
                    elif im.size != size[0]:
                        print("      plan shot %s is %sx%s, this game shoots %sx%s" % ((name,) + im.size + size[0]), flush=True)
                        ok = False
            if ok:
                shutil.copy(path, os.path.join(out, name + ".png"))
                report(True, "plan shot %s" % name)
                return
            print("      plan shot %s attempt %d rejected: %s" % (name, attempt + 1, path or text[-200:]), flush=True)
            time.sleep(1)
        report(False, "plan shot %s" % name, text[-300:])

    def jump(phase):
        text = run(slot, "construction moved to|baseddiag: .*(fail|rror)", "baseddiag", "site=storageunit", "phase", phase)
        report("construction moved to" in text, "plan jump %s" % phase, text[-200:])
        return "construction moved to" in text

    def folder(word, expect):
        text = run(slot, expect + "|rror|not on the hotbar", "baseddiag", "site=storageunit", "folder", word)
        report(re.search(expect, text) is not None, "folder %s" % word, text[-200:])

    def tp(spot):
        x, y, z, yaw = spot
        s1(slot, "tp", x, y, z, "--yaw", yaw)

    s1(slot, "time", "1200")
    # A plan left open by an earlier step would turn the first hand shot into a read view.
    folder("close", "baseddiag: folder plan closed")
    folder("give", "baseddiag: folder given")
    for prefix, label, phase, wall in PLAN_STATES:
        if not jump(phase):
            continue
        tp(HALL_MIDDLE)
        folder("hold", "baseddiag: folder in hand")
        # The first hold after the give can come with the plan page up; the hand shot shows the clipboard only.
        folder("close", "baseddiag: folder plan closed")
        time.sleep(1)
        shot(prefix + "-hand")
        folder("open", "Project folder opened")
        shot(prefix + "-open")
        folder("close", "baseddiag: folder plan closed")
    # The last state first, so the folder hung for the first state is not on the wall yet in the other shots.
    for prefix, label, phase, wall in reversed(PLAN_STATES):
        if not wall or not jump(phase):
            continue
        spot = HALL_WALLS[prefix]
        tp(spot)
        text = run(slot, "baseddiag: folder hung|rror|not on the hotbar", "baseddiag", "site=storageunit", "folder", "wall")
        report("baseddiag: folder hung" in text, "folder wall", text[-200:])
        folder("drop", "baseddiag: hands free")
        # A camera 2.2 m in front of the hung folder, on the side the player stood, at the folder's height: the
        # unit's furniture below it stays out of the way.
        m = re.search(r"folder hung on the .* at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)", text)
        cam = None
        if m:
            px, py, pz = float(m.group(1)), float(m.group(2)), float(m.group(3))
            dx, dz = float(spot[0]) - px, float(spot[2]) - pz
            length = math.hypot(dx, dz)
            if length > 0.05:
                cam = ((px + dx / length * 2.2, py - 0.05, pz + dz / length * 2.2), (px, py, pz))
            else:
                print("      folder hangs where the player stands; the wall shot is the player's view", flush=True)
        else:
            print("      no position in the wall line; the wall shot is the player's view", flush=True)
        shot(prefix + "-wall", cam)
    sheet_of(out)


def sheet_of(out):
    """One row per state: hand, open, wall where there is one; the state's label above the row."""
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError as e:
        report(False, "plan sheet", "Pillow missing: %s" % e)
        return
    w, h, band = 800, 450, 44
    try:
        font = ImageFont.truetype("arial.ttf", 26)
    except OSError:
        font = ImageFont.load_default()
    sheet = Image.new("RGB", (3 * w, len(PLAN_STATES) * (h + band)), (24, 26, 30))
    d = ImageDraw.Draw(sheet)
    missing = []
    for row, (prefix, label, phase, wall) in enumerate(PLAN_STATES):
        y = row * (h + band)
        d.text((12, y + 8), label, fill="white", font=font)
        for col, kind in enumerate(("hand", "open", "wall")):
            path = os.path.join(out, "%s-%s.png" % (prefix, kind))
            if kind == "wall" and not wall:
                continue
            if not os.path.isfile(path):
                missing.append(os.path.basename(path))
                continue
            sheet.paste(Image.open(path).convert("RGB").resize((w, h)), (col * w, y + band))
            d.text((col * w + 10, y + band + 8), kind, fill="white", font=font, stroke_width=2, stroke_fill="black")
    path = os.path.join(out, "plan-states-sheet.jpg")
    sheet.save(path, quality=85)
    report(not missing, "plan sheet %s" % path, "missing: " + ", ".join(missing) if missing else "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--skip", default="", help="comma list of parts to leave out: stages,frames,folder,plans")
    ap.add_argument("--plans", default="", help="folder for the plan-state shots; OUT/plan-states by default")
    ap.add_argument("--stages", default="", help="comma list of stage names to shoot, e.g. c-night1,c-night2; all by default")
    a = ap.parse_args()
    skip = set(a.skip.split(","))
    if "stages" not in skip:
        stages(a.slot, a.out, set(filter(None, a.stages.split(","))))
    if "frames" not in skip:
        frame_cost(a.slot)
    if "folder" not in skip:
        folder(a.slot, os.path.join(a.out, "folder"))
    if "plans" not in skip:
        plan_states(a.slot, a.plans or os.path.join(a.out, "plan-states"))
    print("dressing checks failed: %d" % failed)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
