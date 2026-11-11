"""Records which NPCs enter an x/z rectangle over a span of game time.

Speeds up the clock with the vanilla `settimescale` command, samples every NPC's hips position inside the rectangle
about once per real second, and prints per NPC the first and last game time seen, the sample count and the bounding
box of its positions. Used before building at a site to learn who walks through the area at night.

usage: npc-trace.py --slot mptest --rect X0 Z0 X1 Z1 --from 1800 --to 0900 [--scale 10] [--out trace.csv]
"""
import argparse
import csv
import json
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"


def s1(slot, *args):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError("s1.py " + " ".join(args) + " failed: " + (out.stderr or out.stdout)[-800:])
    return out.stdout


def minutes_of(hhmm):
    return hhmm // 100 * 60 + hhmm % 100


def clock(start_hhmm, game_minutes):
    # The bridge does not report the game clock. One game minute lasts one real second at timescale 1, so the clock
    # follows from the start time and the real time since then.
    total = minutes_of(start_hhmm) + int(game_minutes)
    return (total // 60 % 24) * 100 + total % 60


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--rect", nargs=4, type=float, required=True)
    ap.add_argument("--from", dest="start", default="1800")
    ap.add_argument("--to", dest="end", default="0900")
    ap.add_argument("--scale", type=float, default=10)
    ap.add_argument("--out", default="")
    a = ap.parse_args()
    x0, z0, x1, z1 = a.rect
    start, end = int(a.start), int(a.end)
    span_minutes = (minutes_of(end) - minutes_of(start)) % 1440

    s1(a.slot, "time", a.start)
    s1(a.slot, "run", "settimescale %g" % a.scale)
    began = time.monotonic()
    seen = {}
    rows = []
    print("tracing %s..%s at timescale %g in x %g..%g z %g..%g" % (a.start, a.end, a.scale, x0, x1, z0, z1), flush=True)
    try:
        while True:
            minutes = (time.monotonic() - began) * a.scale
            if minutes >= span_minutes:
                break
            now = clock(start, minutes)
            data = json.loads(s1(a.slot, "find", "Pickpocket Interaction", "--kind", "collider", "--rect",
                                 str(x0), str(z0), str(x1), str(z1), "-n", "400", "--sort", "none"))
            # The pickpocket collider is inactive on almost every NPC; only its position counts here.
            for o in data["objects"]:
                parts = o["path"].split("/")
                name = parts[parts.index("Avatar") - 1] if "Avatar" in parts else parts[0]
                x, y, z = o["bounds"]["center"]
                if not (x0 <= x <= x1 and z0 <= z <= z1):
                    continue
                rows.append((now, name, round(x, 2), round(y, 2), round(z, 2)))
                e = seen.setdefault(name, {"first": now, "last": now, "n": 0, "x": [x, x], "z": [z, z], "y": [y, y]})
                e["last"] = now
                e["n"] += 1
                for k, v in (("x", x), ("z", z), ("y", y)):
                    e[k][0] = min(e[k][0], v)
                    e[k][1] = max(e[k][1], v)
            time.sleep(0.5)
    finally:
        s1(a.slot, "run", "settimescale 1")
    if a.out:
        with open(a.out, "w", newline="") as f:
            w = csv.writer(f)
            w.writerow(["time", "npc", "x", "y", "z"])
            w.writerows(rows)
    if not seen:
        print("RESULT no NPC entered the rectangle")
    for name, e in sorted(seen.items(), key=lambda kv: -kv[1]["n"]):
        print("RESULT %-14s %04d..%04d  samples %3d  x %6.1f..%6.1f  y %5.1f..%5.1f  z %6.1f..%6.1f" % (
            name, e["first"], e["last"], e["n"], *e["x"], *e["y"], *e["z"]))


if __name__ == "__main__":
    main()
