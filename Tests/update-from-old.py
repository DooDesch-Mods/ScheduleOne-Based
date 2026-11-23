"""
In-game check of issue #48: a save with the Sweatshop expansion finished and items standing in the new wing, written
by an older Based (1.1.0 with MapExpansions 1.0.x), loads under the current build with the expansion still finished,
every wing item in its place and saving allowed.

Two runs in the same slot, with the save kept between them:

    1. The old build, a fresh save 5:
           pwsh -File Workspace/tools/session/session.ps1 -Profile main -Build _wt-MapExpansions-v104,_wt-Based-v110 -NoApp
           python _wt-Based-<topic>/Tests/update-from-old.py --slot mptest write
    2. The current build, save 5 kept:
           pwsh -File Workspace/tools/session/session.ps1 -Profile main -Build MapExpansions,_wt-Based-<topic> -NoApp -Template none
           python _wt-Based-<topic>/Tests/update-from-old.py --slot mptest read

`write` finishes the expansion (`baseddiag phase complete`), builds pots and a rack on the wing's tiles
(`mxreach place sweatshop <item> wing`), saves and records the items in Tests/out/update-from-old.json.
`write --stage rights` (or `frame`) instead leaves the expansion started, gives the project folder and builds nothing.
`read` checks, each PASS or FAIL:
  stage            the expansion is still at the written stage
  folder           a started expansion's project folder is still in the inventory
  no-failure       no world state failure and no blocked save in the log of the load
  item <guid>      each recorded item stands where it was saved (within 5 cm)
  saved            the loaded game saves and Based does not block it
Needs Debug builds of Based (`baseddiag`) and MapExpansions (`mxreach`) in both runs.
"""
import argparse
import importlib.util
import json
import os
import re
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(_here, "out", "update-from-old.json")
ITEMS = ("plasticpot", "plasticpot", "plasticpot", "smallstoragerack")
BUILT = re.compile(r"mxreach place: built (\S+) on expansion tile (\d+),(\d+) at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), guid (\S+)")
CONTENT = re.compile(r"baseddiag contents: (\S+) at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)")
# The stage the write run sets and the stage name the read run expects on the sites line.
STAGES = {"rights": "RightsOwned", "frame": "Constructing", "complete": "Complete"}
FAILED = r"world state '[^']*' failed|Save blocked|construction unchanged because of an earlier failure|\[Based\].*(could not be applied|(?<! 0 )failed)"


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        sys.path.insert(0, self._bridge())
        from s1 import Game  # noqa: E402
        self.g = Game(slot)
        self.results = []

    @staticmethod
    def _bridge():
        folder = _here
        for _ in range(6):
            candidate = os.path.join(folder, "Workspace", "tools", "bridge")
            if os.path.isfile(os.path.join(candidate, "s1.py")):
                return candidate
            folder = os.path.dirname(folder)
        raise SystemExit("[update-from-old] Workspace/tools/bridge/s1.py not found")

    def check(self, name, ok, detail=""):
        self.results.append((name, "PASS" if ok else "FAIL", detail))
        print("%s  %s%s" % ("PASS" if ok else "FAIL", name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        failed = [r for r in self.results if r[1] != "PASS"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(failed), len(failed)))
        for name, verdict, detail in failed:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if failed else 0

    def lines(self, command, wait=1.5):
        path = self.g.log_path()
        start = os.path.getsize(path) if path else 0
        self.g.call("run_console_command", {"command": command, "capture": False})
        time.sleep(wait)
        with open(self.g.log_path(), "rb") as handle:
            handle.seek(start if self.g.log_path() == path else 0)
            return handle.read().decode("utf-8", errors="replace").splitlines()

    def whole_log(self):
        with open(self.g.log_path(), "rb") as handle:
            return handle.read().decode("utf-8", errors="replace").splitlines()


def sites(run):
    return next((l.strip() for l in run.lines("baseddiag sites", 1.0) if "baseddiag sites:" in l and "ming" in l), "")


def status(run):
    # Based 1.1.0 has no "sites"; "status" names the stage there.
    line = sites(run)
    if line:
        return line
    return " | ".join(l.strip()[-200:] for l in run.lines("baseddiag status", 1.0) if "baseddiag" in l)


def write(run, stage):
    moved = [l for l in run.lines("baseddiag phase " + stage, 3.0) if "construction moved to " + stage in l]
    run.check("the expansion is moved to " + stage, bool(moved), moved[0].strip()[-200:] if moved else "no 'construction moved to %s' line" % stage)
    time.sleep(8)
    built = []
    if stage != "complete":
        # A started expansion: the player carries the project folder Ray hands over with the rights.
        given = [l for l in run.lines("baseddiag folder give", 2.0) if "baseddiag" in l]
        run.check("the project folder is given", any("folder given" in l for l in given), given[-1:] or "no line")
        state = [l for l in run.lines("baseddiag folder state", 1.0) if "baseddiag: folder" in l]
        print("      folder: " + (state[-1].strip()[-200:] if state else "no state line"))
    for item in (ITEMS if stage == "complete" else ()):
        found = [BUILT.search(l) for l in run.lines("mxreach place sweatshop %s wing" % item, 2.5)]
        found = [m for m in found if m]
        run.check("built %s on a wing tile" % item, bool(found), found[0].group(0)[-200:] if found else "no 'mxreach place: built' line")
        if found:
            m = found[0]
            built.append({"item": m.group(1), "tile": [int(m.group(2)), int(m.group(3))],
                          "at": [float(m.group(4)), float(m.group(5)), float(m.group(6))], "guid": m.group(7)})
    print("      status: " + status(run))
    saved = run.g.call("save_now", {}, timeout_ms=60000)
    run.check("the game saved", bool(saved and saved.get("ok")), saved)
    time.sleep(3)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump({"stage": stage, "built": built, "written": time.strftime("%Y-%m-%d %H:%M:%S")}, handle, indent=2)
    print("      recorded %d items in %s" % (len(built), OUT))


def read(run):
    if not os.path.isfile(OUT):
        run.check("a record from the write run exists", False, OUT)
        return
    with open(OUT, encoding="utf-8") as handle:
        record = json.load(handle)
    stage = record.get("stage", "complete")
    line = sites(run)
    run.check("stage %s" % stage, STAGES[stage] in line, line[-220:] or "no sites line")
    if stage != "complete":
        state = [l for l in run.lines("baseddiag folder state", 1.0) if "baseddiag: folder" in l]
        # The folder line reads "holding=<bool>, carried=<bool>, ..., hotbar=[slots]".
        carried = re.search(r"carried=True", state[-1]) if state else None
        run.check("folder: the project folder is still in the inventory", bool(carried),
                  state[-1].strip()[-200:] if state else "no folder state line")
    bad =[l.strip()[-300:] for l in run.whole_log() if re.search(FAILED, l)]
    run.check("no-failure", not bad, bad[0] if bad else "")
    placed = [CONTENT.search(l) for l in run.lines("baseddiag contents", 2.0)]
    placed = [(m.group(1), (float(m.group(2)), float(m.group(3)), float(m.group(4)))) for m in placed if m]
    for item in record["built"]:
        near = [p for i, p in placed if i == item["item"] and max(abs(a - b) for a, b in zip(p, item["at"])) < .05]
        run.check("item %s %s" % (item["item"], item["guid"][:8]), bool(near), "saved at %s, found %s" % (
            item["at"], [p for i, p in placed if i == item["item"]]))
    path = run.g.log_path()
    start = os.path.getsize(path)
    saved = run.g.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    with open(path, "rb") as handle:
        handle.seek(start)
        blocked = [l for l in handle.read().decode("utf-8", errors="replace").splitlines() if "Save blocked" in l]
    run.check("saved", bool(saved and saved.get("ok")) and not blocked, blocked[0].strip()[-250:] if blocked else saved)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("write", "read"))
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--stage", default="complete", choices=tuple(STAGES),
                        help="write: how far the expansion is (rights: bought, folder carried; frame: under construction)")
    args = parser.parse_args()
    run = Run(args.slot)
    write(run, args.stage) if args.mode == "write" else read(run)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
