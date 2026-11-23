"""
In-game check of Based#108: a Botanist harvests pots on the property's old tiles and carries the yield to a drying rack
on the expansion tiles. The report: the yield disappears and the Botanist never walks to the rack.

Clears the Sweatshop's far wall and the wing's north block, builds pots on the old tiles and drying racks on the
expansion tiles, assigns only the expansion's racks (`mxreach setup <code> wing`), routes every pot to its nearest
rack, sows and ripens. Then it watches the Botanists: harvests from the trace, the racks' contents from `mxreach work`,
and the game's "Invalid transit route" warning, which the game logs when a harvest is stranded in the pot.

Needs Debug builds of MapExpansions (`mxreach`) and Based and a running test slot whose wing is finished, for
example template `mrmonkey-dock`:

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Profile main -Build MapExpansions,_wt-Based-<topic> -Template mrmonkey-dock -NoApp
    python _wt-Based-<topic>/Tests/rack-in-wing.py [--slot mptest] [--minutes 6] [--code sweatshop]
"""
import argparse
import importlib.util
import os
import re
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("botanist_pots", os.path.join(_here, "botanist-pots.py"))
_bp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_bp)

RACK = re.compile(r"mxreach work:   drying rack at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)( on expansion tiles)?: "
                  r"input (\d+), drying (\d+), output (\d+)")
PLACED = re.compile(r"mxreach place: built (\w+) on (expansion|old) tile")
INVALID = re.compile(r"Invalid transit route for move item behaviour.*")
REACH = re.compile(r"mxreach: .*(drying ?rack|DryingRack).*", re.I)


class Run(_bp.Run):
    def __init__(self, slot, code):
        super().__init__(slot)
        self.code = code
        _bp.CODE = code

    def racks(self):
        """Drying racks of the property, once: (x, z, on expansion, input + drying + output)."""
        found = {}
        for line in self.lines("mxreach work %s" % self.code):
            m = RACK.search(line)
            if m:
                key = (round(float(m.group(1)), 1), round(float(m.group(3)), 1))
                found[key] = (key[0], key[1], bool(m.group(4)), sum(int(v) for v in m.group(5, 6, 7)))
        return list(found.values())

    def place(self, item, where):
        text = "\n".join(self.lines("mxreach place %s %s %s" % (self.code, item, where)))
        m = PLACED.search(text)
        own = [l for l in text.splitlines() if "mxreach place" in l]
        return bool(m and m.group(2) == ("expansion" if where == "wing" else "old")), (own[-1] if own else "no place line")

    def setup(self, pots, racks):
        # The wing's north block, its far wall and the old room west of the wing, so no rack of the save is left on old tiles.
        for rect in ("10 12 21 19", "10 2 21 5", "0 0 9 20"):
            self.lines("mxreach clear %s %s" % (self.code, rect))
        built = [self.place("dryingrack", "wing") for _ in range(racks)]
        for ok, line in built:
            print("  " + line.split("[dev] ", 1)[-1], flush=True)
        self.check("layout: %d drying racks stand on the expansion tiles" % racks, all(ok for ok, _ in built),
                   "%d of %d" % (sum(ok for ok, _ in built), racks))
        built = [self.place("plasticpot", "old") for _ in range(pots)]
        for ok, line in built:
            print("  " + line.split("[dev] ", 1)[-1], flush=True)
        self.check("layout: %d pots stand on the old tiles" % pots, all(ok for ok, _ in built),
                   "%d of %d" % (sum(ok for ok, _ in built), pots))
        old = [r for r in self.racks() if not r[2]]
        self.check("layout: no drying rack is left on the old tiles", not old, old or "none")
        # The clears remove the save's lockers, and a Botanist without a locker never reaches its task checks.
        ok, line = self.place("locker", "old")
        print("  " + line.split("[dev] ", 1)[-1], flush=True)
        self.check("layout: a locker stands on the old tiles", ok, line)
        # Unassigning an object the clear destroyed throws in the game's management UI after the removal is done, so
        # each setup run drops one dead assignment; it repeats until the Botanist's setup line comes through.
        assigned = []
        for attempt in range(1, 16):
            lines = self.lines("mxreach setup %s wing" % self.code)
            assigned = [l for l in lines if re.search(r"mxreach setup: \w+ got \d+ more objects", l)]
            if assigned:
                break
        for line in assigned:
            print("  " + line.split("[dev] ", 1)[-1])
        self.check("setup: the Botanist got the pots and the expansion's racks", bool(assigned),
                   "after %d setup runs" % attempt)
        for item, qty in (("soil", 40), ("ogkushseed", 40)):
            self.lines("mxreach stock %s %s %d" % (self.code, item, qty))
        print("  " + next((l.split("[dev] ", 1)[-1] for l in self.lines("mxreach route %s ogkushseed" % self.code)
                           if "mxreach route" in l), "no route line"))
        self.sow_and_ripen()
        self.lines("settime 0800")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--code", default="sweatshop")
    ap.add_argument("--minutes", type=float, default=6)
    ap.add_argument("--pots", type=int, default=4)
    ap.add_argument("--racks", type=int, default=2)
    args = ap.parse_args()
    r = Run(args.slot, args.code)
    r.setup(args.pots, args.racks)
    # The vanilla reach of every object per employee: the racks' access points in the property bounds and on a path.
    for line in r.lines("mxreach %s" % args.code, max_wait_ms=20000):
        if REACH.search(line):
            print("  " + line.split("[dev] ", 1)[-1], flush=True)
    print("  " + next((l.split("[dev] ", 1)[-1] for l in r.lines("mxreach trace on") if "mxreach trace" in l), "no trace line"))
    names = r.botanists()
    r.check("a Botanist works the property", bool(names), ", ".join(names) or "none")
    before = sum(x[3] for x in r.racks())
    log = r.log_file()
    start = os.path.getsize(log) if log else 0
    history, crossings, first, pots, loops = r.watch(args.minutes)
    after = r.racks()
    # What each employee did, one line per change of behaviour, so a harvest that never reaches a rack shows where the
    # Botanist went instead.
    for name, samples in history.items():
        last = None
        for t, pos, active, moving, away in samples:
            if active != last:
                print("  %s: %s at (%.1f, %.1f, %.1f), moving %s" % (name, active, pos[0], pos[1], pos[2], moving), flush=True)
                last = active
    for x, z, wing, n in after:
        print("  drying rack at (%.1f, %.1f)%s holds %d" % (x, z, " on expansion tiles" if wing else "", n))
    invalid = []
    if log:
        with open(log, encoding="utf-8", errors="replace") as f:
            f.seek(start)
            invalid = [m.group(0)[:300] for m in (INVALID.search(l) for l in f) if m]
    harvests = sum(r.harvests.values())
    stored = sum(x[3] for x in after) - before
    r.check("the Botanists harvest the pots on the old tiles", harvests >= 1, "%d harvests in %.1f min" % (harvests, args.minutes))
    r.check("the harvest reaches the drying racks on the expansion tiles", stored > 0,
            "racks hold %d more items than at the start" % stored)
    r.check("the game strands no harvest (no 'Invalid transit route')", not invalid, invalid[:2] or "none")
    for name in names:
        print("  %s issues: %s" % (name, r.issues.get(name, "-")))
    r.pot_states("at the end")
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
