"""
In-game check of issue #80: Ray's estate agent menu (his realty-office conversation) lists Based's two entries,
"I lost my building plans." (Runtime/PlanCopies.cs) and "I want to expand one of my properties." (Runtime/RayMenu.cs),
also with other mods loaded that touch his menu. Drives a running test slot over the ScheduleMCP bridge with the
helpers of ray-plan-copies.py and prints PASS or FAIL per check.

Needs a Debug build of Based in the slot and test save 5 (the player owns the Sweatshop and the Storage Unit).
The run moves the Sweatshop expansion past "rights missing" so Ray keeps plan copies, and empties the inventory.

    python _wt-Based-x/tests/ray-menu-choices.py [--slot mptest] [--with ChemicalPlant] [--where working|offwork|copy|office|street|all]

--where all (the default) talks to Ray in both real schedule states: at work (10:00, the estate agent menu) and off
work (19:00, the controller's own choices), then at work with a copy of the estate agent conversation, as a mod that
rebuilds his office menu would set it. office and street force the controller into one state instead.
--with names a mod whose load line must be in the log, so a run without it cannot pass as the run with it.
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
Run = _rpc.Run

COPIES = r"building plans|Baupläne"
EXPAND = r"expand one of my properties|meiner Immobilien ausbauen"
BYE = r"\bBye\b|Tschüss"


def order(shown):
    """Choices sorted by their on-screen number "[n]"; unnumbered ones last."""
    def key(c):
        m = re.match(r"\[(\d+)\]", c)
        return int(m.group(1)) if m else 99
    return sorted(shown, key=key)


# Ray's work schedule (NPCEvent_LocationDialogue in Main.unity, 07:30 to 18:00) overrides his controller with the
# estate agent conversation while he stands at the realty office; outside it his controller's own choices show.
STATES = {
    "working": (1000, "Realtor_SellProperty"),
    "offwork": (1900, "none"),
}


def override_of(r, mark):
    line = r.wait_line(mark, r"baseddiag ray: override '", timeout=4)
    m = re.search(r"override '([^']*)'", line or "")
    return m.group(1) if m else None


def suite(r, where):
    r.prefix = "[%s] " % where
    print("\n########## Ray: %s" % where)
    if where in STATES:
        # The real schedule: set the clock, let the schedule run, talk without touching the controller.
        hhmm, expected = STATES[where]
        r.g.world("set_time", {"time": hhmm})
        time.sleep(8.0)
        mark = r.mark()
        shown = r.talk("")
        found = override_of(r, mark)
        r.check("at %04d Ray's conversation override is %s" % (hhmm, expected), found == expected, found)
    elif where == "copy":
        # At work, with the work event and the controller pointing to a copy of the estate agent conversation.
        r.g.world("set_time", {"time": 1000})
        time.sleep(8.0)
        mark = r.mark()
        shown = r.talk("copy")
        line = r.wait_line(mark, r"office menu is the conversation 'Realtor_SellProperty\(Clone\)'", timeout=4)
        r.check("Based takes the copy for Ray's office menu", bool(line), line or r.lines(mark, r"\[Based\]")[-3:])
    else:
        shown = r.talk(where)
    r.shot("ray-menu-%s" % where)
    r.note("choices: %r" % shown)
    r.check("Ray's menu lists the plan copies", any(re.search(COPIES, c) for c in shown), shown)
    r.check("Ray's menu lists the expansion", any(re.search(EXPAND, c) for c in shown), shown)
    if where in ("office", "working", "copy"):
        r.check("the vanilla estate agent menu is still there", any("purchase a property" in c for c in shown), shown)
        ordered = order(shown)
        r.check("the farewell stays last", bool(ordered) and re.search(BYE, ordered[-1]) is not None, ordered)
    r.close_dialog()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", default=None)
    p.add_argument("--with", dest="with_mods", action="append", default=[])
    p.add_argument("--where", default="all", choices=["working", "offwork", "copy", "office", "street", "all"])
    a = p.parse_args()
    r = Run(a.slot)

    for mod in a.with_mods:
        loaded = r.g.log(grep=mod + " v", lines=5)
        r.check("%s is loaded" % mod, bool(loaded), loaded[-1:] if loaded else "no load line for " + mod)

    # Ray keeps plan copies once the rights are bought and no folder is carried or hung up.
    moved = r.cmd("baseddiag phase frame", wait=1.5)
    r.check("the Sweatshop expansion is past the rights", any("construction moved to frame" in l or "already" in l for l in moved),
            [l for l in moved if "baseddiag" in l][-1:])
    r.close_dialog()
    r.cmd("clearinventory", wait=0.8)
    time.sleep(2.0)
    copies = r.g.log(grep="Ray offers copies of the building plans.", lines=1)
    r.check("Based arms the plan copies", bool(copies), copies[-1] if copies else "no log line")
    expand = r.g.log(grep="Ray offers to expand a property.", lines=1)
    r.check("Based arms the expansion", bool(expand), expand[-1] if expand else "no log line")

    for where in (["working", "offwork", "copy"] if a.where == "all" else [a.where]):
        suite(r, where)
    r.prefix = ""
    bad = [l for l in r.g.log(grep="[Based]", lines=2000)
           if ("WARNING" in l or "ERROR" in l) and "baseddiag ray:" not in l]
    r.check("no Based warning or error during the run", not bad, bad[-2:])
    sys.exit(r.summary())


if __name__ == "__main__":
    main()
