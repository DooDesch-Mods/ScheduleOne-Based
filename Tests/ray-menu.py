"""
In-game check of Ray's combined expansion menu (Based Runtime/RayMenu.cs, UI/BuildPlan.cs) on the Bungalow. Drives a
running test slot over the ScheduleMCP bridge (Workspace/tools/bridge/s1.py): talks to Ray through `baseddiag ray`,
picks choices with the game's own uGUI raycast, reads and presses the plan page on its Sideload surface and reads
the Based log lines. Prints PASS or FAIL per check.

Needs a Debug build of Based in the slot and test save 5. The script makes the player own the Bungalow
(`setowned bungalow`), so the save afterwards owns it, holds the permit and has the second storey under construction.

    python _wt-Based-bungalow/Tests/ray-menu.py [--slot mptest2] [--where street|office]
"""
import argparse
import importlib.util
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ray_plan_copies", os.path.join(HERE, "ray-plan-copies.py"))
copies = importlib.util.module_from_spec(spec)
spec.loader.exec_module(copies)

SURFACE = "based-plan"
ENTRY = r"expand one of my properties|eine meiner Immobilien ausbauen"
BASE = "ray.bungalow"
PERMIT = 1500
STOREY = 6000


class Run(copies.Run):
    def plan(self):
        raw = self.g.eval(SURFACE, "s1.call('plan.state')")
        try:
            return json.loads(raw) if raw else {}
        except (TypeError, ValueError):
            self.note("plan.state is not JSON: %r" % raw)
            return {}

    def press(self, selector):
        answer = self.g.click(SURFACE, selector)
        time.sleep(1.2)
        return answer

    def select(self, build_id):
        index = [b["id"] for b in self.plan().get("builds", [])].index(build_id)
        return self.press("#builds > button:nth-child(%d)" % (index + 1))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", default=None)
    p.add_argument("--where", default="street", choices=["office", "street"])
    a = p.parse_args()
    r = Run(a.slot)
    r.close_dialog()
    r.cmd("clearinventory", wait=0.8)
    r.cmd("setowned bungalow", wait=1.5)
    r.set_cash(20000)
    time.sleep(2.5)
    armed = r.g.log(grep="Ray offers to expand a property.", lines=1)
    r.check("Ray's expansion entry is armed for the owned Bungalow", bool(armed), armed[-1] if armed else "no log line")

    print("\n##### 1 the entry")
    shown = r.talk(a.where)
    r.shot("menu-1-choices")
    r.note("choices: %r" % shown)
    r.check("Ray lists 'I want to expand one of my properties.'", any(re.search(ENTRY, c) for c in shown), shown)
    mark = r.mark()
    r.click(ENTRY)
    handed = r.wait_line(mark, r"Ray hands over the plan of %s\." % re.escape(BASE), timeout=8)
    r.check("one property to expand: Ray hands over its plan right away", bool(handed), handed)
    time.sleep(2)

    print("\n##### 2 the permit page")
    state = r.plan()
    r.shot("menu-2-permit")
    r.note("state: %s" % json.dumps({k: state.get(k) for k in ("mode", "rights", "buy", "canBuy", "constructionShown", "steps")}))
    r.check("the plan shows the permit first", state.get("mode") != "menu" and state.get("rightsShown"), state.get("mode"))
    r.check("the construction row is hidden on the permit page", state.get("constructionShown") is False, state.get("constructionShown"))
    r.check("the permit costs $%d" % PERMIT, ("%d" % PERMIT) in (state.get("rights") or "").replace(",", ""), state.get("rights"))
    cash0 = r.cash()
    mark = r.mark()
    r.press("#buy")
    r.press("#buy")
    time.sleep(2)
    r.check("cash dropped by exactly $%d after a double click" % PERMIT, abs(r.cash() - (cash0 - PERMIT)) < 0.5, "cash %s -> %s" % (cash0, r.cash()))

    print("\n##### 3 the list of builds")
    state = r.plan()
    r.shot("menu-3-list")
    builds = state.get("builds") or []
    r.note("builds: %s" % json.dumps(builds))
    r.check("the plan lists the builds after the permit", state.get("mode") == "menu", state.get("mode"))
    r.check("four builds, all open", len(builds) == 4 and all(b.get("state") == "open" for b in builds), [b.get("state") for b in builds])

    print("\n##### 4 ordering the second storey")
    r.select(BASE + "-storey")
    state = r.plan()
    r.shot("menu-4-storey")
    r.check("the storey page opens with a way back", state.get("mode") != "menu" and bool(state.get("back")), state.get("back"))
    cash0 = r.cash()
    r.press("#buy")
    r.press("#buy")
    time.sleep(2)
    r.check("cash dropped by exactly $%d after a double click" % STOREY, abs(r.cash() - (cash0 - STOREY)) < 0.5, "cash %s -> %s" % (cash0, r.cash()))
    r.press("#back")
    state = r.plan()
    r.shot("menu-5-locked")
    builds = {b["id"]: b for b in state.get("builds") or []}
    r.note("builds: %s" % json.dumps(list(builds.values())))
    r.check("the storey shows as under construction", builds.get(BASE + "-storey", {}).get("state") == "building", builds.get(BASE + "-storey"))
    others = [b for i, b in builds.items() if i != BASE + "-storey"]
    r.check("the other builds are locked with the reason", others and all(b.get("state") == "locked" and "storey" in (b.get("status") or "").lower() for b in others),
            [(b.get("state"), b.get("status")) for b in others])
    r.select(BASE + "-sunroom")
    state = r.plan()
    r.check("a locked build cannot be ordered", not state.get("canBuy"), state.get("canBuy"))
    r.press("#close")

    print("\n##### 5 the journal")
    quests = r.g.log(grep="Second storey: Bungalow", lines=5)
    r.check("the journal has a page for the second storey", bool(quests), quests[-1:] if quests else "no log line")

    bad = [l for l in r.g.log(grep="[Based]", lines=3000) if ("WARNING" in l or "ERROR" in l) and "baseddiag ray:" not in l]
    r.check("no Based warning or error during the run", not bad, bad[-3:])
    sys.exit(r.summary())


if __name__ == "__main__":
    main()
