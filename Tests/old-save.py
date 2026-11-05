"""
In-game check that a save written by an older Based loads unchanged under the current build: the construction state,
the project folder in the inventory and the folder on a wall must all come back. Prints PASS or FAIL per check.

Two runs in the same slot, with the save kept between them:

    1. Start the slot with the older build (for example the main checkout) and a fresh save 5:
           pwsh -File Workspace/tools/session/session.ps1 -Build Based,MapExpansions -NoApp
       then write the state:
           python _wt-Based-multisite/Tests/old-save.py --slot mptest write
    2. Start the slot with the current build and keep save 5 as it is:
           pwsh -File Workspace/tools/session/session.ps1 -Build _wt-Based-multisite,MapExpansions -NoApp -Template none
       then read it back:
           python _wt-Based-multisite/Tests/old-save.py --slot mptest read

`write` moves the Sweatshop expansion to the frame phase, hangs a folder on the nearest wall, puts a second one in the
inventory, saves, and records what it set in Tests/out/old-save.json. `read` compares the loaded game with that file.
Needs Debug builds of Based in both runs (`baseddiag`).
"""
import argparse
import json
import os
import re
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[old-save] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

RECORD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "old-save.json")
HUNG_LIST = re.compile(r"hung=\[([^\]]*)\]")
POSITION = re.compile(r"\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)")


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.results = []

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        failed = [r for r in self.results if r[1] != "PASS"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(failed), len(failed)))
        for name, verdict, detail in failed:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if failed else 0

    # The Based lines a console command wrote, or the bridge's error as one line.
    def based(self, command, pattern):
        reply = self.g.run(command, until=pattern, max_wait_ms=10000)
        result = (reply or {}).get("result") or {}
        lines = [str(l.get("text", "")) for l in result.get("lines") or [] if isinstance(l, dict) and l.get("source") == "Based"]
        if not lines:
            print("      %s: no Based line; bridge ok=%s, %s lines, error %s" % (
                command, (reply or {}).get("ok"), result.get("lineCount"), (reply or {}).get("error")), flush=True)
        return lines

    def first(self, command, pattern):
        rx = re.compile(pattern)
        return next((l for l in self.based(command, pattern) if rx.search(l)), None)


def positions(line):
    found = HUNG_LIST.search(line or "")
    return [tuple(float(v) for v in m.groups()) for m in POSITION.finditer(found.group(1))] if found else []


def write(run):
    moved = run.first("baseddiag phase frame", r"construction moved to frame")
    run.check("the expansion is in the frame phase", bool(moved), moved or "no 'construction moved to frame' line")
    run.g.run("clearinventory", max_wait_ms=3000)
    wall = run.first("baseddiag folder wall", r"folder hung on the|failed")
    run.check("a folder hangs on a wall", bool(wall and "folder hung on the" in wall), wall or "no answer")
    given = run.first("baseddiag folder give", r"folder given")
    run.check("a folder is in the inventory", bool(given and "answer ''" in given), given or "no answer")
    time.sleep(1)
    state = run.first("baseddiag folder state", r"baseddiag: folder")
    hung = positions(state)
    run.check("the folder state shows it carried and hung", bool(state and "carried=True" in state and hung), state or "no answer")
    saved = run.g.call("save_now", {}, timeout_ms=60000)
    run.check("the game saved", bool(saved and saved.get("ok")), saved)
    time.sleep(3)
    os.makedirs(os.path.dirname(RECORD), exist_ok=True)
    with open(RECORD, "w", encoding="utf-8") as handle:
        json.dump({"hung": hung, "state": state, "written": time.strftime("%Y-%m-%d %H:%M:%S")}, handle, indent=2)
    print("      recorded in %s" % RECORD, flush=True)


def read(run):
    if not os.path.isfile(RECORD):
        run.check("a record from the write run exists", False, RECORD)
        return
    with open(RECORD, encoding="utf-8") as handle:
        record = json.load(handle)
    log = run.g.log(grep="[Based]", lines=100000)
    registered = [l for l in log if "Project folder item registered: based_project_folder." in l]
    run.check("the folder item registers under its old id", bool(registered), registered[-1] if registered else "no line")
    hung_lines = [l for l in log if "Project folder hung at" in l and "ming.sweatshop" in l]
    run.check("the saved wall folder is dressed again", bool(hung_lines), hung_lines[-1] if hung_lines else "no line")
    mounted = [l for l in log if "Project folder page mounted: ming.sweatshop." in l]
    run.check("the plan page is mounted for the Sweatshop", bool(mounted), mounted[-1] if mounted else "no line")

    state = run.first("baseddiag folder state", r"baseddiag: folder")
    run.check("the folder is still in the inventory", bool(state and "carried=True" in state), state or "no answer")
    now = positions(state)
    near = all(any(max(abs(a - b) for a, b in zip(old, new)) < 0.05 for new in now) for old in record["hung"])
    run.check("the wall folder is where it was saved", bool(now) and near, "saved %s, loaded %s" % (record["hung"], now))

    sites = run.first("baseddiag sites", r"baseddiag sites:")
    run.check("the expansion is still in the frame phase", bool(sites and "Constructing (2/4 nights)" in sites), sites or "no answer")

    bad = [l for l in log if re.search(r"\[(ERROR|WARNING)\]", l) and "baseddiag" not in l]
    run.check("no Based warning or error while loading", not bad, " | ".join(bad[-3:]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("mode", choices=("write", "read"))
    args = parser.parse_args()
    run = Run(args.slot)
    (write if args.mode == "write" else read)(run)
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
