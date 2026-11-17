"""The upper floor's placement grids end where the floor ends, on the lines the walls stand on.

For each stage the Storage Unit's build jumps to that phase and MapExpansions' `mxgrid storageunit check` probes every
outer edge of every grid: a wall or railing must stand on the grid line, and an edge without one must be where the
floor ends or another grid goes on. This script turns that into PASS/FAIL per stage and grid, and also checks each
upper grid's extent against the walls it must reach, so every corner lies on the shared lines (x on .25/.75, z on
.3/.8). Edges that are open on purpose in a stage are allowed and listed:
  - east-open: the loft's east edge (x -2.25) faces the walkway, which is not yet part of the property;
  - south-open: the east grid's walkway edge at z 103.8 faces the walkway south of it;
  - from south-open on: the gap in the kerb at the ladder's top (x -1.75, z 99.89 .. 101.03).
The landing in front of the loft door has no grid; the tile under its middle is reported, not judged, and its open
edges must lie on the grid's lines (west -10.75, south 104.8, north 106.3).

usage: storageunit-grid.py --slot mptestN [--stages loft:complete,east:east-open,...] [--no-jump]
"""
import argparse
import json
import re
import subprocess
import sys

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
STAGES = ["loft:complete", "east:east-open", "east:complete", "south:south-open", "south:complete", "bay:complete"]
# Upper grids by origin, with the extent their tiles must cover (x min, x max, z min, z max) and the first stage they
# exist in. The order is the build's.
GRIDS = {
    "loft": ((-2.5, 3.25, 113.55), (-8.75, -2.25, 103.8, 113.8)),
    "east": ((3.0, 3.25, 113.55), (-2.25, 3.25, 103.8, 113.8)),
    "south": ((3.0, 3.25, 103.55), (-1.75, 3.25, 94.3, 103.8)),
}
EXPECTED = {"loft:complete": ["loft"], "east:east-open": ["loft"], "east:complete": ["loft", "east"],
            "south:south-open": ["loft", "east"], "south:complete": ["loft", "east", "south"],
            "bay:complete": ["loft", "east", "south"]}
TOL = .011

failed = 0


def s1(slot, *args, timeout=180):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def run(slot, until, *command):
    return s1(slot, "run", "--max-wait", "60000", "--until", until, *command)[1].replace('\\"', '"')


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""), flush=True)


def near(a, b):
    return abs(a - b) <= TOL


def allowed(stage, kind, point):
    """Whether an edge that fails the check is open on purpose in this stage."""
    x, _, z = point
    if stage == "east:east-open" and near(x, -2.25) and 103.8 - TOL <= z <= 113.8 + TOL:
        return "loft edge to the walkway (not yet property)"
    if kind != "floor":
        return None
    if -8.75 - TOL <= x <= -7.25 + TOL and 104.8 - TOL <= z <= 106.3 + TOL:
        return "corner the loft door swings through"
    if stage == "south:south-open" and near(z, 103.8) and -2.25 - TOL <= x <= .25 + TOL:
        return "walkway edge south of the east grid"
    if stage in ("south:south-open", "south:complete", "bay:complete") and near(x, -1.75) and 99.89 - .25 <= z <= 101.03 + .25:
        return "kerb gap at the ladder's top"
    return None


def vec(text):
    return tuple(float(v) for v in text.strip("() ").split(","))


def name_of(origin):
    for name, (o, _) in GRIDS.items():
        if all(abs(a - b) < .02 for a, b in zip(origin, o)):
            return name
    return None


def check_stage(slot, stage):
    text = run(slot, r"mxgrid storageunit check:|rror", "mxgrid", "storageunit", "check")
    if "mxgrid storageunit check:" not in text:
        report(False, "%s grid check ran" % stage, text[-300:])
        return
    index_name, seen = {}, set()
    for m in re.finditer(r"mxgrid storageunit grid (\d+) '[^']*': (\d+) tiles, origin (\([^)]*\)), yaw (\S+), tiles x (\S+)\.\.(\S+) z (\S+)\.\.(\S+?),?\s", text):
        gi, count, origin = m.group(1), int(m.group(2)), vec(m.group(3))
        name = name_of(origin)
        if name is None:
            continue
        index_name[gi] = name
        seen.add(name)
        want = GRIDS[name][1]
        got = tuple(float(m.group(i)) for i in (5, 6, 7, 8))
        ok = all(near(a, b) for a, b in zip(got, want))
        report(ok, "%s %s grid extent on the lines" % (stage, name),
               "x %.3f..%.3f z %.3f..%.3f (want x %.2f..%.2f z %.2f..%.2f), %d tiles" % (got + want + (count,)))
    for name in EXPECTED.get(stage, []):
        if name not in seen:
            report(False, "%s %s grid present" % (stage, name), "no grid with origin %s" % (GRIDS[name][0],))
    problems = {}
    for m in re.finditer(r"mxgrid storageunit grid (\d+) tile (-?\d+),(-?\d+) edge (\S+) at (\([^)]*\)): (wall|floor) '([^']*)'(.*)", text):
        gi = m.group(1)
        if gi not in index_name:
            continue
        point, kind = vec(m.group(5)), m.group(6)
        why = allowed(stage, kind, point)
        line = "tile %s,%s %s at %s: %s '%s'%s" % (m.group(2), m.group(3), m.group(4), m.group(5), kind, m.group(7), m.group(8).rstrip())
        if why:
            print("INFO %s %s open on purpose (%s): %s" % (stage, index_name[gi], why, line))
        else:
            problems.setdefault(index_name[gi], []).append(line)
    for gi, name in sorted(index_name.items()):
        lines = problems.get(name, [])
        report(not lines, "%s %s grid edges on walls or open floor ends" % (stage, name),
               "%d off: %s" % (len(lines), " | ".join(lines[:6])) if lines else "")
    landing = run(slot, r"mxgrid at|rror", "mxgrid", "storageunit", "at", "-9.8", "3.3", "105.5")
    for m in re.finditer(r"mxgrid at [^:]*: grid (\d+) '[^']*' tile (\S+) (exists|missing)", landing):
        if m.group(1) in index_name:
            print("INFO %s landing (-9.8, 105.5) on %s grid tile %s: %s" % (stage, index_name[m.group(1)], m.group(2), m.group(3)))
    check_landing(slot, stage)


def check_landing(slot, stage):
    """The landing's open edges lie on the grid's lines: west -10.75, south 104.8, north 106.3. Its east edge reaches
    into the wall and is not judged."""
    code, text = s1(slot, "find", "MapExpansions.Panel.landing", "--exact", "--kind", "renderer", "--active-only", "-n", "20")
    try:
        found = json.loads(text)["objects"]
    except (ValueError, KeyError):
        report(False, "%s landing found" % stage, text[-300:])
        return
    if len(found) != 1:
        report(False, "%s landing found once" % stage, "%d renderers" % len(found))
        return
    mn, mx = found[0]["bounds"]["min"], found[0]["bounds"]["max"]
    edges = {"west": (mn[0], -10.75), "south": (mn[2], 104.8), "north": (mx[2], 106.3)}
    for name, (have, want) in edges.items():
        report(abs(have - want) <= .002, "%s landing %s edge on the line" % (stage, name), "%.3f (want %.2f)" % (have, want))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--stages", default=",".join(STAGES))
    ap.add_argument("--no-jump", action="store_true", help="check the stage the game is in, named by the first --stages")
    a = ap.parse_args()
    for stage in a.stages.split(","):
        if not a.no_jump:
            text = run(a.slot, r"construction moved|complete|rror|nothing to", "baseddiag", "site=storageunit", "phase", "ray.storageunit-" + stage)
            moved = "construction moved" in text or "complete" in text
            report(moved, "%s phase jump" % stage, "" if moved else text[-300:])
            if not moved:
                continue
        check_stage(a.slot, stage)
        if a.no_jump:
            break
    print("RESULT %s: %d failed" % ("PASS" if failed == 0 else "FAIL", failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
