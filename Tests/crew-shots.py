"""Photographs the storey crew on the frame day: each worker at his station, then the first worker on a break.

python Tests/crew-shots.py --slot mptest6
Copies the shots to _drafts/based-bungalow/storey/crew-<worker>-<side>.png and crew-break-<worker>-<side>.png, one per
side (e, n, w, s), prints PASS/FAIL.
"""
import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
OUT = ROOT / "_drafts" / "based-bungalow" / "storey"
FAILED = []


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""), flush=True)
    if not ok:
        FAILED.append(name)


def workers(slot):
    _, out = s1(slot, "run", "baseddiag site=bungalow workers", "--until", "worker diagnostics", "--max-wait", "20000")
    return [json.loads(m.group(1)) for m in re.finditer(r"worker diagnostics: (\{.*\})", out)]


# Pines, the skip and a car on the loading dock stand around the stations, so each worker is shot from all four sides,
# 2.4 m out and a little above head height, and the evidence sheet takes the side that shows him.
SIDES = {"e": (2.4, -1.0), "n": (1.0, 2.4), "w": (-2.4, 1.0), "s": (-1.0, -2.4)}


def shoot(slot, w, name):
    x, y, z = w["position"]
    for side, (dx, dz) in SIDES.items():
        code, out = s1(slot, "cam", "shot", "--eye", "%.2f" % (x + dx), "%.2f" % (y + 2.0), "%.2f" % (z + dz),
                       "--target", "%.2f" % x, "%.2f" % (y + 1.0), "%.2f" % z, "--hud", "hide", "--settle", "5")
        try:
            path = json.loads(out[out.index("{"):]).get("path")
        except (ValueError, json.JSONDecodeError) as e:
            check("shot %s-%s" % (name, side), False, "no JSON in the cam answer (%s): %s" % (e, out.strip()[-200:]))
            continue
        target = OUT / ("%s-%s.png" % (name, side))
        shutil.copyfile(path, target)
        check("shot %s-%s" % (name, side), True, "%s at (%.2f, %.2f, %.2f): %s" % (w["id"], x, y, z, target))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest6")
    a = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    s1(a.slot, "run", "baseddiag site=bungalow phase complete", "--until", "baseddiag", "--max-wait", "20000")
    _, out = s1(a.slot, "run", "baseddiag site=bungalow phase storey-frame storey", "--until", "baseddiag", "--max-wait", "20000")
    check("storey frame day", "ray.bungalow-storey" in out, out.strip()[-200:])
    s1(a.slot, "time", "1000")
    # Each worker is photographed the first time he stands at a station; the carpenter once he swings.
    deadline, shot, seen = time.time() + 150, set(), []
    while time.time() < deadline and len(shot) < 3:
        seen = [w for w in workers(a.slot) if w.get("visible")]
        for w in seen:
            ready = w.get("atAnchor") is True and ("carpenter" not in w["id"] or w.get("hammering"))
            if ready and w["id"] not in shot:
                shoot(a.slot, w, "crew-" + w["id"].replace("based_bungalow_", ""))
                shot.add(w["id"])
        time.sleep(3)
    check("three workers photographed at their stations", len(shot) == 3,
          "shot: %s; seen: %s" % (", ".join(sorted(shot)), ", ".join("%s atAnchor=%s" % (w["id"], w.get("atAnchor")) for w in seen)))
    # The first break comes 20 to 80 game minutes into the day; the clock runs.
    deadline, resting = time.time() + 240, None
    while time.time() < deadline and not resting:
        resting = next((w for w in workers(a.slot) if w.get("onBreak") and w.get("visible")), None)
        if not resting:
            time.sleep(4)
    check("a worker takes a break", resting is not None, "none within 4 minutes")
    if resting:
        time.sleep(4)
        resting = next((w for w in workers(a.slot) if w["id"] == resting["id"]), resting)
        shoot(a.slot, resting, "crew-break-" + resting["id"].replace("based_bungalow_", ""))
    print("%s crew-shots: %d failed" % ("FAIL" if FAILED else "PASS", len(FAILED)))
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
