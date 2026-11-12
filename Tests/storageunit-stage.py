"""Jump the Storage Unit to one build stage and shoot its fixed views by day and by night.

Prints PASS/FAIL for the jump (the log line of `baseddiag phase`) and for each view, and writes the images to
OUT/<stage>/<day|night>-<view>.png next to the vanilla set.

usage: storageunit-stage.py --slot mptest --phase ray.storageunit-loft:loft-stair --out _drafts/based-storageunit/stages [--only a,b]
"""
import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--phase", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    out = subprocess.run([sys.executable, S1, "--slot", a.slot, "run", "--until", "construction moved to|baseddiag: .*(fail|rror)",
                          "baseddiag", "site=storageunit", "phase", a.phase],
                         capture_output=True, text=True)
    text = out.stdout + out.stderr
    # `s1.py run` exits non-zero on any error line of the command's frames, also from other mods; the jump itself
    # is judged by its own log line.
    if "construction moved to" not in text:
        print("FAIL jump %s: %s" % (a.phase, text[-600:]))
        sys.exit(1)
    print("PASS jump %s" % a.phase)
    stage = a.phase.replace(":", "-")
    failed = 0
    for prefix, time in (("day", "1200"), ("night", "0100")):
        cmd = [sys.executable, os.path.join(HERE, "storageunit-shots.py"), "--slot", a.slot, "--time", time,
               "--prefix", prefix, "--out", os.path.join(a.out, stage)]
        if a.only:
            cmd += ["--only", a.only]
        r = subprocess.run(cmd, capture_output=True, text=True)
        print(r.stdout.strip())
        if r.returncode != 0:
            failed += 1
            print("FAIL shots %s: %s" % (prefix, r.stderr[-400:]))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
