"""
In-game check of issue #99: the Sweatshop expansion's first night (the breakthrough into the upper room) applies after
a sleep, its cut through the restaurant's 'UpperRoom' mesh succeeds and the game saves. Then the save is loaded again
and the breakthrough state is applied once more on load.

Needs a Debug build of Based (`baseddiag phase|advance|sites`) and MapExpansions in a running test slot and a save where
the player owns the Sweatshop, for example save 5 of template `played`:

    pwsh -File Workspace/tools/session/session.ps1 -Profile main -Build MapExpansions,_wt-Based-<topic> -NoApp
    python _wt-Based-<topic>/Tests/breakthrough-sleep.py [--slot mptest] [--no-reload | --reload-only]

Checks, each PASS or FAIL:
  ordered          the construction is ordered and waits for the first night
  slept            a sleep cycle moved the construction to the breakthrough (1 of 4 nights)
  cut              the state cut 'UpperRoom' logged its triangles and no carve or state failure came with it
  saved            the game saved and Based did not block it
  reload-cut       after loading the save the cut is made again without a failure (skipped with --no-reload)
  reload-saved     the reloaded game saves
"""
import argparse
import importlib.util
import os
import re
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("ray_plan_copies", os.path.join(_here, "ray-plan-copies.py"))
_rpc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_rpc)

FAILED = r"world state 'based_ming_upper/[^']*' failed|carve: .*UpperRoom.*(failed|could not)|Save blocked|Sleep ended; construction unchanged"
CUT = r"state cut: 'based_ming_upper[^']*' 'UpperRoom' cut (\d+) triangles"


def failures(run, mark):
    return [l.strip()[-300:] for l in run.lines(mark, FAILED)]


def save(run, name):
    mark = run.mark()
    saved = run.g.call("save_now", {}, timeout_ms=60000)
    time.sleep(3)
    bad = failures(run, mark)
    run.check(name, bool(saved and saved.get("ok")) and not bad, bad[0] if bad else ("" if saved and saved.get("ok") else saved))


def sites(run):
    lines = run.cmd("baseddiag sites", wait=1.0)
    return next((l.strip() for l in lines if "baseddiag sites:" in l and "ming" in l), "")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--no-reload", action="store_true")
    parser.add_argument("--reload-only", action="store_true", help="only load the save again and check the cut")
    parser.add_argument("--save", type=int, default=5)
    args = parser.parse_args()
    run = _rpc.Run(args.slot)
    if not args.reload_only:
        first_night(run)
    if not args.no_reload:
        reload(run, args.save)
    return run.summary()


def first_night(run):
    lines = run.cmd("baseddiag phase ordered", wait=1.5)
    moved = next((l for l in lines if "construction moved to ordered" in l), "")
    run.check("ordered", bool(moved), moved.strip()[-200:] or "no 'construction moved to ordered' line")

    # The player's furniture on the red walls holds the build. The test save is a fresh copy, so the full packing
    # picks up what it can and drops loaded racks and planted pots with their contents.
    lines = run.cmd("baseddiag packdoorway", wait=2.0)
    run.check("walls-clear", any("marked walls are clear" in l for l in lines), next((l.strip()[-200:] for l in reversed(lines) if "baseddiag" in l or "Exception" in l), "no answer"))

    mark = run.mark()
    for night in range(1, 4):
        night_mark = run.mark()
        run.cmd("baseddiag advance", wait=1.0)
        line = run.wait_line(night_mark, CUT + "|" + FAILED + r"|held in sleep cycle|Sleep skipped|sleep cycle \d+ applied", timeout=40)
        state = sites(run)
        run.note("night %d: %s | %s" % (night, (line or "no line").strip()[-160:], state[-120:]))
        if run.lines(mark, CUT + "|" + FAILED):
            break
    status = sites(run)
    run.check("slept", "Constructing (1/4 nights)" in status or "(1/" in status, status[-220:] or "no sites line")
    cut = run.lines(mark, CUT)
    bad = failures(run, mark)
    run.check("cut", bool(cut) and not bad, (bad[0] if bad else cut[-1].strip()[-200:]) if (bad or cut) else "no state cut line for 'UpperRoom'")
    if bad:
        for line in run.lines(mark, r"carve:|state cut|world state|Based\]")[-25:]:
            run.note(line.strip()[-300:])
    save(run, "saved")


def reload(run, slot):
    mark = run.mark()
    # The game loads a save only from the menu.
    run.g.call("return_to_menu", {}, timeout_ms=60000)
    time.sleep(8)
    for attempt in range(6):
        loaded = run.g.call("load_save", {"slot": slot}, timeout_ms=60000)
        if loaded and loaded.get("ok") and ((loaded.get("result") or {}).get("loading")):
            break
        run.note("load_save attempt %d not accepted: %s" % (attempt + 1, loaded))
        time.sleep(5)
    ready, last = run.g.wait_ready(timeout=300, say=lambda s: None)
    run.note("wait-ready: %s, last log line: %s" % (ready, (last or "").strip()[-160:]))
    line = run.wait_line(mark, CUT + "|" + FAILED, timeout=60)
    bad = failures(run, mark)
    run.check("reload-cut", bool(line) and not bad, (bad[0] if bad else line.strip()[-200:]) if line else "no state cut line after the load")
    save(run, "reload-saved")


if __name__ == "__main__":
    sys.exit(main())
