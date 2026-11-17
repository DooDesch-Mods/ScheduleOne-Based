"""The pencil on the plan clipboard casts a shadow, so it reads as lying on the board.

Opens the Storage Unit plan from the project folder, checks that the shadow layer is drawn under the pencil and offset
down-right, shoots the plan with the shadow and with the shadow hidden, and writes both crops around the pencil plus a
side-by-side sheet (top: without shadow, bottom: with shadow). Prints PASS/FAIL per check.

usage: plan-pencil.py --slot mptestN --out _drafts/based-storageunit/pencil
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

from PIL import Image

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
APP = "based-plan"
FAILS = []


def s1(slot, *args, timeout=180):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def report(ok, label, detail=""):
    print("%s  %s%s" % ("PASS" if ok else "FAIL", label, ("  - " + str(detail)) if detail else ""), flush=True)
    if not ok:
        FAILS.append(label)
    return ok


def run(slot, until, *command):
    return s1(slot, "run", "--until", until, *command)[1]


def js(slot, code):
    code_, text = s1(slot, "eval", APP, code)
    return text.strip()


def open_plan(slot):
    for word, expect in (("close", "baseddiag: folder plan closed"), ("give", "baseddiag: folder given"),
                         ("hold", "baseddiag: folder in hand"), ("open", "Project folder opened")):
        text = run(slot, expect + "|rror|not on the hotbar|not given", "baseddiag", "site=storageunit", "folder", word)
        if word == "open":
            return report(expect in text, "plan opened", text[-200:])
    return False


RECTS = """(() => { const out = {}; for (const c of ['layer-pencil', 'layer-pencil-shadow', 'layer-board']) {
  const e = document.querySelector('.' + c); if (!e) { out[c] = null; continue; }
  const r = e.getBoundingClientRect(); out[c] = [r.left, r.top, r.width, r.height]; }
  out.viewport = [window.innerWidth, window.innerHeight]; return JSON.stringify(out); })()"""


def shot(slot):
    code, text = s1(slot, "shot")
    m = re.search(r"([A-Z]:\\[^\"\r\n]+\.png)", text)
    path = m.group(1).replace("\\\\", "\\") if m else ""
    return path if code == 0 and os.path.isfile(path) else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    if not open_plan(a.slot):
        return 1
    time.sleep(1.5)

    raw = js(a.slot, RECTS)
    try:
        rects = json.loads(json.loads(raw) if raw.startswith('"') else raw)
    except ValueError:
        report(False, "pencil layers measured", raw[-300:])
        return 1
    pencil, shadow, view = rects.get("layer-pencil"), rects.get("layer-pencil-shadow"), rects.get("viewport")
    report(pencil is not None, "pencil layer drawn", pencil)
    if not report(shadow is not None, "shadow layer drawn", shadow) or pencil is None:
        return 1
    # The shadow's box is the pencil's outline with a blur margin, shifted so the pencil sits up-left inside it.
    pcx, pcy = pencil[0] + pencil[2] / 2, pencil[1] + pencil[3] / 2
    scx, scy = shadow[0] + shadow[2] / 2, shadow[1] + shadow[3] / 2
    report(scx > pcx and scy > pcy, "shadow sits down-right of the pencil", "pencil centre (%.1f, %.1f), shadow centre (%.1f, %.1f)" % (pcx, pcy, scx, scy))
    order = js(a.slot, "(() => { const k = [...document.querySelectorAll('img')].map(e => e.className); "
                       "return k.indexOf('layer-pencil-shadow') + ',' + k.indexOf('layer-pencil'); })()").strip('"')
    try:
        s_i, p_i = (int(v) for v in order.split(","))
        report(0 <= s_i < p_i, "shadow is drawn before (under) the pencil", order)
    except ValueError:
        report(False, "shadow is drawn before (under) the pencil", order)

    after = shot(a.slot)
    report(after is not None, "shot with shadow")
    hidden = js(a.slot, "(() => { const e = document.querySelector('.layer-pencil-shadow'); e.style.display = 'none'; return e.style.display; })()")
    time.sleep(1.0)
    before = shot(a.slot)
    report(before is not None, "shot without shadow", hidden)
    js(a.slot, "(() => { document.querySelector('.layer-pencil-shadow').style.display = ''; return 'restored'; })()")
    if not (after and before):
        return 1

    crops = []
    for name, path in (("pencil-before", before), ("pencil-after", after)):
        im = Image.open(path).convert("RGB")
        sx, sy = im.width / view[0], im.height / view[1]
        x0, y0 = min(pencil[0], shadow[0]) - 30, min(pencil[1], shadow[1]) - 30
        x1, y1 = max(pencil[0] + pencil[2], shadow[0] + shadow[2]) + 30, max(pencil[1] + pencil[3], shadow[1] + shadow[3]) + 30
        crop = im.crop((int(x0 * sx), int(y0 * sy), int(x1 * sx), int(y1 * sy)))
        crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
        crop.save(os.path.join(a.out, name + ".png"))
        crops.append(crop)
        im.save(os.path.join(a.out, name + "-full.png"))
    sheet = Image.new("RGB", (crops[0].width, crops[0].height * 2 + 8), "white")
    sheet.paste(crops[0], (0, 0))
    sheet.paste(crops[1], (0, crops[0].height + 8))
    sheet.save(os.path.join(a.out, "pencil-before-after.png"))
    report(True, "crops written to " + a.out)
    run(a.slot, "baseddiag: folder plan closed|rror", "baseddiag", "site=storageunit", "folder", "close")
    print("\n===== %s" % ("PASS" if not FAILS else "%d FAIL: %s" % (len(FAILS), ", ".join(FAILS))), flush=True)
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
