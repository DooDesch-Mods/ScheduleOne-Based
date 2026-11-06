"""
In-game check: the four cells under the moved sink in the finished Sweatshop wing are part of the grid, closed for
building while the sink stands there, and free as soon as it is gone, hidden or released.

Based used to leave those cells out of the grid. A mod that removes the sink (Property Renovations) then left an empty
corner nobody could build in. With MapExpansions 1.2.0 the cells are tiles plus a fixture; this reads the grid size,
the fixture's verdict and the game's own tile check (`Grid.IsTileValidAtCoordinate`, raw and with the fixture patch)
through `mxfixtures`, then hides and releases the sink and checks that the cells open and close again.

Needs Debug builds of MapExpansions (1.2.0 or a worktree with fixtures) and Based, and a test slot with a save whose wing
is finished, for example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Build MapExpansions,Based -Template mrmonkey-dock -NoApp
    python Based/Tests/sink-corner.py [--slot mptest] [--tiles 330]
"""
import argparse
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
    raise SystemExit("[sink-corner] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

FIXTURE = "room-sink"
CELLS = {(10, 18), (10, 19), (11, 18), (11, 19)}


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

    def log_text(self):
        path = self.g.log_path()
        if not path:
            return ""
        with open(path, "rb") as handle:
            return handle.read().decode("utf-8", errors="replace")

    def cmd(self, command, marker, wait=1.0):
        path = self.g.log_path()
        offset = os.path.getsize(path) if path else 0
        self.g.call("run_console_command", {"command": command, "capture": False})
        time.sleep(wait + 0.5)
        now = self.g.log_path()
        if not now:
            return []
        with open(now, "rb") as handle:
            handle.seek(offset if now == path else 0)
            text = handle.read().decode("utf-8", errors="replace")
        return [l for l in text.splitlines() if marker in l]


def fixture_view(r):
    """The sink fixture's verdict line and its cells as {(x, y): (in_grid, raw, patched)}, plus the counter line."""
    lines = r.cmd("mxfixtures", "mxfixtures")
    head = next((l for l in lines if "fixture-tile checks" in l), "")
    own = next((l for l in lines if "/" + FIXTURE + " path " in l), "")
    cells = {}
    if own:
        start = lines.index(own)
        for line in lines[start + 1:]:
            m = re.search(r"tile (\d+),(\d+): in grid (yes|no), occupants \d+, IsTileValidAtCoordinate grid (\w+), with fixtures (\w+)", line)
            if not m:
                break
            cells[(int(m.group(1)), int(m.group(2)))] = (m.group(3) == "yes", m.group(4).lower() == "true", m.group(5).lower() == "true")
    return head, own, cells


def expect_cells(r, label, cells, patched_valid):
    r.check("%s: all four sink cells reported" % label, set(cells) == CELLS, sorted(cells))
    for cell in sorted(CELLS):
        if cell not in cells:
            continue
        in_grid, raw, patched = cells[cell]
        r.check("%s: cell %d,%d is a grid tile the game accepts" % (label, cell[0], cell[1]), in_grid and raw,
                "in grid %s, raw %s" % (in_grid, raw))
        r.check("%s: cell %d,%d is %s with fixtures" % (label, cell[0], cell[1], "free" if patched_valid else "closed"),
                patched == patched_valid, "with fixtures %s" % patched)


def counters(r):
    """(reached, blocked, ghost line) of the patch counters, or None."""
    lines = r.cmd("mxfixtures count", "mxfixtures count")
    line = next((l for l in lines if "patch reached" in l), "")
    where = next((l.split("[dev] ")[-1] for l in lines if "ghost" in l), "no ghost line")
    m = re.search(r"patch reached (\d+) fixture-tile checks, turned (\d+) invalid", line)
    return (int(m.group(1)), int(m.group(2)), where) if m else None


def ghost(r, args):
    """The game's own placement loop: stand in the wing, look at the sink cells with a pot equipped, count the patch."""
    r.cmd("give %s 1" % args.item, "give", wait=1.0)
    # South-east of the sink in a free spot of the furnished test room; closer spots push the player through the floor.
    r.g.call("teleport", {"x": -58.3, "y": -0.2, "z": 142.6})
    time.sleep(1.5)
    # The ghost stands closer the steeper the look, so search the pitch until it snaps onto a sink cell.
    aim, snapped, tried = "", None, []
    for pitch in (30, 25, 20, 15, 35, 10, 40, 5):
        aim = next(iter(r.cmd("mxfixtures aim %s %d" % (FIXTURE, pitch), "mxfixtures aim", wait=1.0)), "")
        c = counters(r)
        m = re.search(r"snapped to tile (\d+),(\d+)", c[2]) if c else None
        tile = (int(m.group(1)), int(m.group(2))) if m else None
        tried.append("%d -> %s" % (pitch, "%d,%d" % tile if tile else "none"))
        if tile in CELLS:
            snapped = pitch
            break
    r.check("the ghost of a buildable aims at the sink cells", "equipped" in aim and snapped is not None,
            "pitch %s; tried %s; %s" % (snapped, ", ".join(tried), aim.split("[dev] ")[-1] or "no answer"))
    for label, off, on, closed in (("standing sink", None, None, True), ("hidden sink", "hide", "show", False)):
        if off:
            r.cmd("mxfixtures %s %s" % (off, FIXTURE), "mxfixtures %s" % off)
        before = counters(r)
        time.sleep(2.0)
        after = counters(r)
        shot = r.g.shot() if args.shots else None
        ok = bool(before and after)
        reached = after[0] - before[0] if ok else 0
        blocked = after[1] - before[1] if ok else 0
        detail = "reached +%d, turned invalid +%d; %s%s" % (reached, blocked, after[2] if ok else "no counters",
                                                           ("  shot " + str(shot)) if shot else "")
        r.check("%s: the game's placement loop reaches the patch" % label, reached > 0, detail)
        if closed:
            r.check("%s: the game's ghost is refused on the sink cells" % label, blocked > 0, detail)
        else:
            r.check("%s: the game's ghost is accepted on the sink cells" % label, reached > 0 and blocked == 0, detail)
        if on:
            r.cmd("mxfixtures %s %s" % (on, FIXTURE), "mxfixtures %s" % on)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", default="mptest")
    parser.add_argument("--tiles", type=int, default=330, help="grid 0 size of the finished wing with the sink cells")
    parser.add_argument("--ghost", action="store_true", help="also drive the game's own placement ghost over the cells")
    parser.add_argument("--item", default="plasticpot", help="buildable item id for --ghost")
    parser.add_argument("--shots", action="store_true", help="screenshot each ghost state")
    args = parser.parse_args()
    r = Run(args.slot)

    log = r.log_text()
    r.check("Based uses the fixture", "holds the tiles under the moved sink while it stands" in log)
    stands = [l for l in log.splitlines() if "fixture '%s'" % FIXTURE in l and "stands, 4 tiles closed" in l]
    r.check("the sink fixture stands at apply", bool(stands), stands[-1].strip() if stands else "no 'stands' line")

    prop = r.cmd("mxproperty sweatshop", "mxproperty")
    grid0 = next((l for l in prop if "grid 0 " in l), "")
    m = re.search(r"(\d+) tiles", grid0)
    r.check("grid 0 has %d tiles" % args.tiles, bool(m) and int(m.group(1)) == args.tiles, grid0.split("[dev] ")[-1] or "no grid 0 line")

    _, own, cells = fixture_view(r)
    r.check("the fixture is registered and standing", " standing " in own, own.split("[dev] ")[-1] or "no fixture line")
    expect_cells(r, "standing", cells, patched_valid=False)

    if args.ghost:
        ghost(r, args)

    for off, on, reason in (("hide", "show", "inactive"), ("release", "unrelease", "released")):
        line = next(iter(r.cmd("mxfixtures %s %s" % (off, FIXTURE), "mxfixtures %s" % off)), "")
        r.check("%s: the fixture reads %s" % (off, reason), ("now " + reason) in line, line.split("[dev] ")[-1] or "no answer")
        _, own, cells = fixture_view(r)
        expect_cells(r, off, cells, patched_valid=True)
        line = next(iter(r.cmd("mxfixtures %s %s" % (on, FIXTURE), "mxfixtures %s" % on)), "")
        r.check("%s: the fixture stands again" % on, "now standing" in line, line.split("[dev] ")[-1] or "no answer")
        _, own, cells = fixture_view(r)
        expect_cells(r, on, cells, patched_valid=False)

    errors = [l for l in r.log_text().splitlines() if ("fixture" in l.lower() and ("[Error]" in l or "ERROR" in l))
              or "the tile check failed" in l]
    r.check("no fixture errors in the log", not errors, errors[-1].strip() if errors else "")
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
