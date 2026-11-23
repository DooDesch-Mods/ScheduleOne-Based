"""Checks both construction crews in a running test slot and prints PASS/FAIL per check.

python Tests/crew-check.py --slot mptest2 [--only bungalow|ming]
Bungalow: the whole crew of three is on every build day. Per day the check names who swings: the foreman never does,
the carpenter wherever his work is in reach, the woodcutter at the pines and on the storey's hammer days.
Ming: the frame phase at 10:00 brings the three Sweatshop workers, as before the crew refactor.
"""
import argparse
import json
import pathlib
import re
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
FAILED = []
ROLES = ("foreman", "carpenter", "lumberjack")


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))
    if not ok:
        FAILED.append(name)


def run(slot, command):
    # A phase change applies its world state for about a second; waiting for the baseddiag line keeps the bridge from
    # returning before the command has finished.
    code, out = s1(slot, "run", command, "--until", "baseddiag|worker diagnostics", "--max-wait", "20000")
    errors = [l for l in out.splitlines() if re.search(r"\[error\]|Exception", l, re.I)]
    # Unity skips the house's unreadable foundation mesh in the sunroom bake; that floor belongs to the world's NavMesh
    # (the state leaves it as existing floor), so the notice is printed and not counted.
    skipped = [l for l in errors if "does not allow read access" in l]
    if skipped:
        print(f"INFO {command}: {len(skipped)} Unity notices of unreadable bake sources, first: {skipped[0].strip()[:200]}")
    errors = [l for l in errors if l not in skipped]
    # s1.py exits with 2 when the command logged errors; those are judged above, line by line.
    ok = code in (0, 2) and "\n-- " in "\n" + out and "until NOT matched" not in out and not errors
    check("run " + command, ok, "" if ok else ("; ".join(errors[:3]) or out.strip()[-400:]))
    return out


def diagnostics(slot, site):
    out = run(slot, f"baseddiag site={site} workers")
    workers = []
    for line in out.splitlines():
        m = re.search(r"worker diagnostics: (\{.*\})", line)
        if m:
            workers.append(json.loads(m.group(1)))
    return out, workers


def wait_for_work(slot, site, expected, want_tool, seconds=60, standing=False):
    """Polls until every expected worker is visible and enough of them swing. With standing, each visible worker is
    judged by his latest reading at his station: workers who walk between two stations rarely all stand at once, so the
    readings are gathered per worker, and a hammering one is kept over one that has just arrived."""
    deadline = time.time() + seconds
    workers, best = [], {}
    while time.time() < deadline:
        _, workers = diagnostics(slot, site)
        visible = [w for w in workers if w.get("visible")]
        if not standing:
            if len(visible) == expected and sum(1 for w in workers if w.get("hammering")) >= want_tool:
                return workers
        else:
            for w in visible:
                if w.get("atAnchor") is True and (w.get("hammering") or not best.get(w["id"], {}).get("hammering")):
                    best[w["id"]] = w
            ready = [best[w["id"]] for w in visible if w["id"] in best]
            if len(visible) == expected and len(ready) == expected and sum(1 for w in ready if w.get("hammering")) >= want_tool:
                return ready
        time.sleep(5)
    if standing:
        return [best.get(w["id"], w) for w in workers]
    return workers


def role(worker):
    wid = str(worker.get("id", "")).lower()
    return next((r for r in ROLES if r in wid), wid)


def crew_day(slot, upgrade, name, swingers):
    """One build day at 10:00: all three visible, each following the upgrade at its station, the ones in swingers
    swinging and the others not, none with a failure."""
    code, out = s1(slot, "time", "1000")
    check("time 1000", code == 0, out.strip()[-200:])
    workers = [w for w in wait_for_work(slot, "bungalow", 3, len(swingers), seconds=90, standing=True) if w.get("visible")]
    roles = sorted(role(w) for w in workers)
    check(f"{name}: crew of three", roles == sorted(ROLES), ", ".join(roles) or "none visible")
    for w in workers:
        r = role(w)
        check(f"{name} {r} follows the build", w.get("upgrade") == upgrade, str(w.get("upgrade")))
        check(f"{name} {r} at its anchor", w.get("atAnchor") is True, str(w.get("anchor")))
        swings = r in swingers
        check(f"{name} {r} {'swings' if swings else 'keeps the tool down'}", (w.get("hammering") is True) == swings,
              str(w.get("hammering")))
        check(f"{name} {r} without failure", not w.get("failure"), str(w.get("failure")))


def bungalow(slot):
    """The carport's order day: the woodcutter fells the pines while the carpenter measures the bays."""
    run(slot, "baseddiag site=bungalow phase complete")
    out = run(slot, "baseddiag site=bungalow phase ordered carport")
    check("carport ordered", "ray.bungalow-carport" in out, out.strip()[-300:])
    # The carpenter measures from 2 m off the bay line, out of reach for a swing.
    crew_day(slot, "ray.bungalow-carport", "carport ordered", {"lumberjack"})
    # Only one upgrade builds at a time; the carport has to stand before the next one is ordered.
    run(slot, "baseddiag site=bungalow phase complete carport")


def sunroom(slot):
    """The day the sunroom is ordered the woodcutter fells its pines while the carpenter works at the west front; once
    the frames stand the crew works inside on the slab, where only the carpenter swings."""
    run(slot, "baseddiag site=bungalow phase complete")
    for phase, swingers in ((None, {"lumberjack", "carpenter"}), ("sunroom-glass", {"carpenter"})):
        name = "sunroom " + (phase or "ordered")
        out = run(slot, "baseddiag site=bungalow phase " + (phase + " sunroom" if phase else "ordered sunroom"))
        check(name, "ray.bungalow-sunroom" in out, out.strip()[-300:])
        crew_day(slot, "ray.bungalow-sunroom", name, swingers)
    run(slot, "baseddiag site=bungalow phase complete sunroom")


def storey(slot):
    """The second storey's crew: the foreman at the site table and the carpenter at the timber stock every day, the
    fitter fells the stair's pine on the order day, welds at the stair on the floor and frame days and hammers on the
    others."""
    run(slot, "baseddiag site=bungalow phase complete")
    # phase, the fitter welding
    days = ((None, False), ("storey-floor", True), ("storey-frame", True), ("storey-walls", False), ("storey-roof", False))
    for phase, welding in days:
        name = "storey " + (phase or "ordered")
        out = run(slot, "baseddiag site=bungalow phase " + (phase + " storey" if phase else "ordered storey"))
        check(name, "ray.bungalow-storey" in out, out.strip()[-300:])
        crew_day(slot, "ray.bungalow-storey", name, {"carpenter"} if welding else {"carpenter", "lumberjack"})
        if welding:
            # The welder's sparks fly while he stands at the weld.
            out = run(slot, "baseddiag vfx")
            line = next((l for l in out.splitlines() if "bungalow effects" in l), "")
            check(f"{name} weld sparks", "weld-sparks" in line, line.strip()[-200:])
    run(slot, "baseddiag site=bungalow phase complete storey")


def ming(slot):
    run(slot, "baseddiag site=sweatshop phase frame")
    code, out = s1(slot, "time", "1000")
    check("time 1000", code == 0, out.strip()[-200:])
    workers = wait_for_work(slot, "sweatshop", 3, 2)
    check("three Sweatshop workers attached", len(workers) == 3, f"{len(workers)} workers")
    check("three Sweatshop workers visible", sum(1 for w in workers if w.get("visible")) == 3)
    check("carpenter and fitter swing", sum(1 for w in workers if w.get("hammering")) >= 2,
          ", ".join(f'{w["id"]}={w.get("hammering")}' for w in workers))
    for w in workers:
        check(w["id"] + " without failure", not w.get("failure"), str(w.get("failure")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest2")
    ap.add_argument("--only", choices=["bungalow", "ming"])
    a = ap.parse_args()
    if a.only != "ming":
        bungalow(a.slot)
        sunroom(a.slot)
        storey(a.slot)
    if a.only != "bungalow":
        ming(a.slot)
    print(f"{'FAIL' if FAILED else 'PASS'} crew-check: {len(FAILED)} failed")
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
