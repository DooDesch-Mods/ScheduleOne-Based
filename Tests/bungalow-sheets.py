"""Acceptance sheets of every Bungalow build stage, day and night, from the fixed views of Tests/bungalow-shots.py.

Walks the builds one after another (only one builds at a time): vanilla, then each phase of each build as the player
sees it the morning after that night, then the finished build. For every stage it shoots all views at 12:00 and 22:00
and puts each set into one sheet: _drafts/based-bungalow/final/<build letter>-<step>-<stage>-<day|night>.png. Night
sheets use the gamma-lifted copies so the build shows. Prints PASS/FAIL per stage and sheet.

    python _wt-Based-bungalow/Tests/bungalow-sheets.py --slot mptest [--only F]
"""
import argparse
import pathlib
import re
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
OUT = ROOT / "_drafts" / "based-bungalow"
FINAL = OUT / "final"
# letter: (shot set, console upgrade suffix, phases in build order). "vanilla" is the house before the build is ordered.
BUILDS = {
    "B": ("sunroom", "sunroom", ["vanilla", "sunroom-base", "sunroom-glass", "complete"]),
    "D": ("deck", "deck", ["vanilla", "deck-stilts", "complete"]),
    "E": ("carport", "carport", ["vanilla", "complete"]),
    "F": ("storey", "storey", ["vanilla", "storey-floor", "storey-walls", "storey-roof", "complete"]),
}
TIMES = {"day": "1200", "night": "2200"}
# A customer who walks up to the player opens a deal dialogue that covers every shot. Dismissing it is the third answer;
# ui_find matches a label only as a whole, with the answer's number.
DISMISS = "[3] Get out of here"
sys.path.insert(0, str(S1.parent))
from s1 import Game  # noqa: E402


def close_dialogue(slot):
    """Dismisses an open customer dialogue; returns whether one was open."""
    g = Game(slot)
    found = g.call("ui_find", {"query": DISMISS}, timeout_ms=20000) or {}
    if not ((found.get("result") or {}).get("count") or 0):
        return False
    reply = g.call("ui_click", {"query": DISMISS}, timeout_ms=20000) or {}
    print("NOTE closed a customer dialogue before the shots: %s" % ("ok" if reply.get("ok") else reply.get("error")))
    time.sleep(2)
    return True


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def console(slot, command):
    code, out = s1(slot, "run", command)
    return code == 0, out.strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--only", default="", help="comma-separated build letters, e.g. F or B,F")
    a = ap.parse_args()
    only = [x for x in a.only.split(",") if x]
    FINAL.mkdir(parents=True, exist_ok=True)
    failed = 0
    ok, out = console(a.slot, "baseddiag site=bungalow phase complete")
    print(("PASS" if ok else "FAIL") + " Bungalow permit complete" + ("" if ok else ": " + out[-300:]))
    failed += not ok
    for letter, (build, suffix, phases) in BUILDS.items():
        if only and letter not in only:
            continue
        for step, phase in enumerate(phases):
            if phase == "vanilla":
                ok, out = console(a.slot, "baseddiag site=bungalow phase rights " + suffix)
            else:
                ok, out = console(a.slot, "baseddiag site=bungalow phase %s %s" % (phase, suffix))
            # The last phase finishes the build: the reply names the completed quest instead of the next phase. s1.py
            # exits non-zero for any error line in the command's window, also one of another system, so the reply decides
            # and the error lines are shown.
            if not ok:
                for line in out.splitlines():
                    if line.startswith("[error]"):
                        print("NOTE %s %s: error line in the window: %s" % (letter, phase, line[:300]))
            ok = ("construction moved to" in out or "rights" in out or
                  re.search(r"Construction quest of \S+ completed", out) is not None) and \
                re.search(r"world state '[^']+' failed", out) is None
            print(("PASS" if ok else "FAIL") + " %s %s" % (letter, phase) + ("" if ok else ": " + out[-300:]))
            if not ok:
                failed += 1
                continue
            time.sleep(8)
            for when, hhmm in TIMES.items():
                label = "%s%d-%s" % (letter.lower(), step, phase)
                # A dialogue open after the set may have opened during it, so the set is shot again once.
                for attempt in range(2):
                    close_dialogue(a.slot)
                    proc = subprocess.run([sys.executable, str(HERE / "bungalow-shots.py"), "--build", build, "--slot",
                                           a.slot, "--label", label, "--time", hhmm], capture_output=True, text=True)
                    if not close_dialogue(a.slot):
                        break
                    print("NOTE %s %s: a customer dialogue covered the shots, attempt %d" % (letter, phase, attempt + 1))
                sys.stdout.write(proc.stdout)
                failed += proc.returncode != 0
                suffix_png = "-lit.png" if when == "night" else ".png"
                files = sorted(str(p) for p in (OUT / build).glob("%s-%s-*%s" % (label, hhmm, suffix_png))
                               if when == "night" or not p.name.endswith("-lit.png"))
                sheet = FINAL / ("%s-%d-%s-%s.png" % (letter.lower(), step, phase, when))
                proc = subprocess.run([sys.executable, str(HERE / "contact-sheet.py"), str(sheet), *files],
                                      capture_output=True, text=True)
                print(proc.stdout.strip() or "FAIL sheet %s: %s" % (sheet, proc.stderr.strip()[-300:]))
                failed += proc.returncode != 0
    print("%s bungalow-sheets: %d failed" % ("FAIL" if failed else "PASS", failed))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
