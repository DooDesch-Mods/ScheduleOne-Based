"""
In-game check of Ray's plan copies (Based Runtime/PlanCopies.cs). Drives a running test slot over the ScheduleMCP
bridge (Workspace/tools/bridge/s1.py): opens Ray's conversation through `baseddiag ray`, picks choices with the
game's own uGUI raycast and reads the Based log lines. Prints PASS or FAIL per check.

Ray talks in two ways: with his realty-office override ("office", the estate agent menu) and without it ("street",
the controller's own choice list). The suite runs in both.

Needs a Debug build of Based in the slot and a save where the player owns the Sweatshop with the expansion past
"rights missing" (test save 5). Language-independent: choices are matched in English and German. It changes cash
and the inventory of the save and ends with an empty inventory.

    python _wt-Based-dock/tests/ray-plan-copies.py [--slot mptest] [--where office|street|both]
"""
import argparse
import os
import re
import shutil
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[ray-plan-copies] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
PRICE = 200
CHOICE = r"building plans|Baupläne"
NEVER_MIND = r"Never mind|Schon gut"
BYE = r"\bBye\b|Tschüss"
SOLD = r"Ray sold a copy of the ming\.sweatshop plans for \$200; cash ([\d.]+) -> ([\d.]+)\."


class Run:
    def __init__(self, slot):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.results = []
        self.prefix = ""

    # ---- results -------------------------------------------------------------------------------

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        name = self.prefix + name
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def note(self, text):
        print("      " + text, flush=True)

    def summary(self):
        failed = [r for r in self.results if r[1] != "PASS"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(failed), len(failed)))
        for name, verdict, detail in failed:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if failed else 0

    # ---- the log -------------------------------------------------------------------------------

    def mark(self):
        path = self.g.log_path()
        return (path, os.path.getsize(path) if path else 0)

    def lines(self, mark, grep=None):
        path, offset = mark
        now = self.g.log_path()
        if now != path:
            offset = 0
        if not now:
            return []
        with open(now, "rb") as handle:
            handle.seek(offset)
            found = handle.read().decode("utf-8", errors="replace").splitlines()
        if grep:
            rx = re.compile(grep)
            found = [line for line in found if rx.search(line)]
        return found

    def wait_line(self, mark, pattern, timeout=12.0):
        rx = re.compile(pattern)
        end = time.time() + timeout
        while True:
            for line in self.lines(mark):
                if rx.search(line):
                    return line
            if time.time() >= end:
                return None
            time.sleep(0.4)

    # ---- driving the game ----------------------------------------------------------------------

    def cmd(self, command, wait=0.6):
        mark = self.mark()
        self.g.call("run_console_command", {"command": command, "capture": False})
        time.sleep(wait + 0.4)
        return self.lines(mark)

    def state(self):
        return self.g.call("get_state", {}, timeout_ms=6000).get("result") or {}

    def cash(self):
        return float(self.state().get("cash") or 0)

    def set_cash(self, amount):
        delta = amount - self.cash()
        if abs(delta) >= 0.5:
            self.cmd("changecash %d" % round(delta), wait=0.8)
        return self.cash()

    def shot(self, name):
        """A screenshot copied to tests/out/<name>.png; a person looks at it, no check reads pixels."""
        path = self.g.shot()
        if not path or not os.path.isfile(path):
            self.note("screenshot failed: %r" % path)
            return None
        os.makedirs(OUT, exist_ok=True)
        target = os.path.join(OUT, name + ".png")
        shutil.copyfile(path, target)
        self.note("screenshot: " + target)
        return target

    # ---- dialogue ------------------------------------------------------------------------------

    def dialog(self):
        res = self.g.call("ui_find", {"query": "Dialogue"}, timeout_ms=8000).get("result") or {}
        return [e for e in (res.get("elements") or []) if "Dialogue" in (e.get("path") or "")]

    def choices(self):
        return [e.get("label") or "" for e in self.dialog() if "DialogueChoice" in (e.get("path") or "")]

    def wait_choices(self, timeout=10.0):
        """The choices of the current line, continuing through lines that have none. Empty when the conversation ended."""
        end = time.time() + timeout
        while time.time() < end:
            found = self.choices()
            if found:
                return found
            self.g.call("dialogue_continue", {}, timeout_ms=5000)
            time.sleep(0.6)
        return []

    def click(self, pattern, timeout=10.0):
        """Click the choice whose label matches the regex; by label, since a path is a substring of its siblings'."""
        rx = re.compile(pattern)
        end = time.time() + timeout
        while True:
            for e in self.dialog():
                if "DialogueChoice" in (e.get("path") or "") and rx.search(e.get("label") or ""):
                    self.g.call("ui_click", {"query": e["label"]}, timeout_ms=8000)
                    time.sleep(0.8)
                    return True
            if time.time() >= end:
                self.note("choice not visible: %r in %r" % (pattern, self.choices()))
                return False
            time.sleep(0.4)

    def close_dialog(self):
        """Leave whatever is open: Bye or Never mind when offered, Continue otherwise, then end the handler's dialogue."""
        for _ in range(5):
            if not self.dialog():
                break
            if self.choices():
                self.click(BYE + "|" + NEVER_MIND, timeout=1.5)
            else:
                self.g.call("dialogue_continue", {}, timeout_ms=5000)
            time.sleep(0.5)
        self.cmd("baseddiag ray end", wait=0.4)
        time.sleep(0.6)

    def talk(self, where):
        """Ray's conversation through the controller's own interact entry; returns the first choices shown."""
        self.close_dialog()
        mark = self.mark()
        self.cmd("baseddiag ray " + where, wait=0.8)
        called = self.wait_line(mark, r"baseddiag ray: Interacted\(\) called", timeout=6)
        if not called:
            self.note("baseddiag ray did not reach Interacted(): " + " | ".join(self.lines(mark, r"baseddiag ray")[-3:]))
            return []
        return self.wait_choices()


def fill_inventory(run):
    """Give items until the game refuses: `give` loops until nothing fits any more."""
    run.cmd("give ogkush 200", wait=0.8)


def suite(r, where):
    r.prefix = "[%s] " % where
    print("\n########## Ray at the %s" % where)
    r.close_dialog()
    r.cmd("clearinventory", wait=0.8)
    r.set_cash(5000)
    time.sleep(1.5)
    armed = r.g.log(grep="Ray offers copies of the building plans.", lines=1)
    r.check("Ray's offer is armed with no folder in the inventory", bool(armed), armed[-1] if armed else "no log line")

    print("\n##### 1 the choice appears")
    mark = r.mark()
    shown = r.talk(where)
    r.shot("ray-%s-1-choices" % where)
    r.note("choices: %r" % shown)
    r.check("choice 'I lost my building plans.' is in Ray's list", any(re.search(CHOICE, c) for c in shown), shown)
    if where == "office":
        r.check("the vanilla estate agent menu is still there", any("purchase a property" in c for c in shown), shown)
        ordered = sorted(shown, key=lambda c: int(re.match(r"\[(\d+)\]", c).group(1)) if re.match(r"\[(\d+)\]", c) else 99)
        r.check("the entry sits right before the farewell",
                len(ordered) >= 2 and re.search(CHOICE, ordered[-2]) is not None and re.search(BYE, ordered[-1]) is not None, ordered)

    print("\n##### 2 Never mind")
    mark = r.mark()
    cash0 = r.cash()
    r.click(CHOICE)
    offer = r.wait_choices()
    r.shot("ray-%s-2-offer" % where)
    r.note("offer: %r" % offer)
    r.check("offer lists 'Sweatshop - $200'", any("Sweatshop" in c and "$%d" % PRICE in c for c in offer), offer)
    r.check("offer lists 'Never mind.'", any(re.search(NEVER_MIND, c) for c in offer), offer)
    r.click(NEVER_MIND)
    left = r.wait_line(mark, r"The player left Ray without a copy of the plans\.", timeout=6)
    r.check("Never mind logs the walk-away", bool(left), left)
    r.check("Never mind takes no cash", abs(r.cash() - cash0) < 0.5, "cash %s -> %s" % (cash0, r.cash()))
    r.check("Never mind hands out no folder", not r.lines(mark, r"Project folder added to the inventory"))

    print("\n##### 3 buying the Sweatshop copy")
    r.talk(where)
    mark = r.mark()
    cash0 = r.set_cash(5000)
    r.click(CHOICE)
    r.wait_choices()
    r.click(r"Sweatshop.*\$%d" % PRICE)
    sold = r.wait_line(mark, SOLD, timeout=8)
    r.check("log line 'Ray sold a copy of the ming.sweatshop plans for $200'", bool(sold), sold)
    r.check("folder handed over", bool(r.lines(mark, r"Project folder added to the inventory")))
    r.check("cash dropped by exactly $%d" % PRICE, abs(r.cash() - (cash0 - PRICE)) < 0.5, "cash %s -> %s" % (cash0, r.cash()))
    r.shot("ray-%s-3-bought" % where)
    r.close_dialog()

    print("\n##### 4 after buying, the choice is gone")
    time.sleep(1.5)
    shown = r.talk(where)
    r.shot("ray-%s-4-after-buying" % where)
    r.note("choices: %r" % shown)
    if where == "office":
        r.check("the estate agent menu opened", bool(shown), shown)
    else:
        r.check("Ray has nothing left to say beyond a greeting", not shown, shown)
    r.check("choice is no longer offered once the folder is in the inventory", not any(re.search(CHOICE, c) for c in shown), shown)
    r.close_dialog()

    print("\n##### 5 less than $%d in cash" % PRICE)
    r.cmd("clearinventory", wait=0.8)
    time.sleep(1.5)
    r.set_cash(PRICE - 1)
    time.sleep(1.5)
    mark = r.mark()
    shown = r.talk(where)
    r.check("choice is listed", any(re.search(CHOICE, c) for c in shown), shown)
    r.click(CHOICE)
    offer = r.wait_choices(timeout=4)
    r.shot("ray-%s-5-too-poor" % where)
    r.note("after the choice, buttons: %r" % offer)
    r.check("Ray logs that cash is short", bool(r.wait_line(mark, r"Ray's offer: cash [\d.]+ is below \$%d" % PRICE, timeout=4)))
    r.check("no copy to pick while cash < $%d" % PRICE, not any("$%d" % PRICE in c for c in offer), offer)
    r.close_dialog()
    r.check("no folder, no sale logged", not r.lines(mark, SOLD) and not r.lines(mark, r"Project folder added"))
    r.check("cash untouched", abs(r.cash() - (PRICE - 1)) < 0.5, "cash %s" % r.cash())

    print("\n##### 6 full inventory")
    r.set_cash(5000)
    r.cmd("clearinventory", wait=0.8)
    fill_inventory(r)
    time.sleep(1.5)
    cash0 = r.cash()
    mark = r.mark()
    shown = r.talk(where)
    r.check("choice is listed", any(re.search(CHOICE, c) for c in shown), shown)
    r.click(CHOICE)
    offer = r.wait_choices(timeout=4)
    r.shot("ray-%s-6-full" % where)
    r.note("after the choice, buttons: %r" % offer)
    r.check("Ray logs that there is no room", bool(r.wait_line(mark, r"Ray's offer: the inventory has no room", timeout=4)))
    r.check("no copy to pick while the pockets are full", not any("$%d" % PRICE in c for c in offer), offer)
    r.close_dialog()
    r.check("no folder, no sale logged", not r.lines(mark, SOLD) and not r.lines(mark, r"Project folder added"))
    r.check("cash untouched with full pockets", abs(r.cash() - cash0) < 0.5, "cash %s -> %s" % (cash0, r.cash()))
    r.cmd("clearinventory", wait=0.8)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", default=None)
    p.add_argument("--where", default="both", choices=["office", "street", "both"])
    a = p.parse_args()
    r = Run(a.slot)
    for where in (["street", "office"] if a.where == "both" else [a.where]):
        suite(r, where)
    r.prefix = ""
    bad = [l for l in r.g.log(grep="[Based]", lines=2000)
           if ("WARNING" in l or "ERROR" in l) and "baseddiag ray:" not in l]
    r.check("no Based warning or error during the run", not bad, bad[-2:])
    sys.exit(r.summary())


if __name__ == "__main__":
    main()
