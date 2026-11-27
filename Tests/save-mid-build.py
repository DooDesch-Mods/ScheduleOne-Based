"""
In-game check of issue #132: a construction saved in the middle of its nights comes back after the game is quit and
started again, with its stage, its nights and the player's cash. With MapExpansions missing, Based stays off, says
why on loading the save, keeps the build in the save and blocks saving; once MapExpansions is back the build returns.

Runs in one slot, the save kept between the runs (`-Template none` keeps save 5 as the last run left it):

    1. write:  pwsh -File Workspace/tools/session/session.ps1 -Slot mptest2 -Build _wt-Based-<topic> -NoApp
               python _wt-Based-<topic>/Tests/save-mid-build.py --slot mptest2 write
    2. read:   pwsh -File Workspace/tools/session/session.ps1 -Slot mptest2 -Build _wt-Based-<topic> -NoApp -Template none
               python _wt-Based-<topic>/Tests/save-mid-build.py --slot mptest2 read
    3. off:    move <slot>/Mods/MapExpansions.dll out (Tests/save-mid-build-off.ps1 does steps 3 and 4), start with
               -KeepSlot -Template none, then `save-mid-build.py off`
    4. back:   MapExpansions.dll back, start with -KeepSlot -Template none, then `save-mid-build.py read`

`write` orders the Sweatshop expansion through the plan page (rights, then construction, paid in cash), clears the
marked walls, sleeps one night (`baseddiag advance`), saves and records stage, nights and cash in
Tests/out/save-mid-build.json. The caller then quits the game without the menu, the way a player closes it.
`read` checks, each PASS or FAIL:
  stage        the expansion is at the recorded stage and night
  cash         the cash is what it was when the game saved
  no-failure   no world state failure and no blocked save in the log of the load
  saved        the loaded game saves and Based does not block it
`off` checks:
  off          Based stays off and names MapExpansions
  told         the player is told on loading the save that the construction waits for MapExpansions
  file-found   the Based data of the write step is in the save folder
  blocked      a save is refused, so the build stays in the save file
  file-kept    the refused save leaves the Based data in the save folder unchanged
Needs a Debug build of Based (`baseddiag`).
"""
import argparse
import glob
import hashlib
import importlib.util
import json
import os
import re
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("ray_plan_copies", os.path.join(_here, "ray-plan-copies.py"))
_rpc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_rpc)

OUT = os.path.join(_here, "out", "save-mid-build.json")
SURFACE = "based-plan"
SITE = re.compile(r"baseddiag sites: 0 \S+ property \S+, owned (\w+), (\S+) (\w+) \((\d+)/(\d+) nights\)")
FAILED = r"world state '[^']*' failed|Save blocked|construction unchanged because of an earlier failure|\[Based\].*could not be applied"


class Run(_rpc.Run):
    def plan(self):
        raw = self.g.eval(SURFACE, "s1.call('plan.state')")
        try:
            return json.loads(raw) if raw else {}
        except (TypeError, ValueError):
            self.note("plan.state is not JSON: %r" % raw)
            return {}

    def site(self):
        for line in reversed(self.cmd("baseddiag sites", wait=1.0)):
            m = SITE.search(line)
            if m:
                return {"owned": m.group(1), "upgrade": m.group(2), "stage": m.group(3), "nights": int(m.group(4))}
        return {}

    def save(self):
        mark = self.mark()
        answer = self.g.call("save_now", {}, timeout_ms=60000)
        time.sleep(3)
        return answer, self.lines(mark)


SAVES = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow", "TVGS", "Schedule I", "Saves")


def save_files(steamid):
    """Every file of the slot player's save 5 that holds Based's state, with its hash."""
    found = {}
    # S1API writes each field of a saveable to Modded/Saveables/<type>/<field>.json.
    for path in glob.glob(os.path.join(SAVES, steamid, "SaveGame_5", "Modded", "Saveables", "BasedSave", "*")):
        if os.path.isfile(path):
            with open(path, "rb") as handle:
                found[path] = hashlib.sha256(handle.read()).hexdigest()
    return found


def write(r):
    r.close_dialog()
    r.set_cash(40000)
    time.sleep(1)
    site = r.site()
    r.note("before: %s" % site)
    r.check("the Sweatshop is owned in this save", site.get("owned") == "True", site)
    # The player's furniture on the red walls refuses the construction order, so the walls are cleared first.
    lines = r.cmd("baseddiag packdoorway", wait=2.0)
    r.check("marked walls clear", any("marked walls are clear" in l for l in lines), lines[-1:] if lines else "no answer")
    r.cmd("baseddiag planfree on", wait=0.4)
    r.cmd("baseddiag plan", wait=2.5)
    # With the full mod set the plan page mounts a few seconds later; until then plan.state answers nothing.
    deadline = time.time() + 20
    while not r.plan().get("stage") and time.time() < deadline:
        time.sleep(1)
    cash0 = r.cash()
    if r.plan().get("stage"):
        for _ in range(3):
            state = r.plan()
            r.note("plan: stage %s, buy %r, canBuy %s" % (state.get("stage"), state.get("buy"), state.get("canBuy")))
            if not state.get("canBuy"):
                break
            r.g.click(SURFACE, "#buy")
            time.sleep(2.5)
    else:
        # A Release Sideload (the mod set of a player's install) has no page probe, so eval and click answer
        # nothing; the console buys through the same purchase the page calls.
        r.note("plan page has no probe in this mod set; buying through baseddiag buy")
        for kind in ("rights", "construction"):
            lines = r.cmd("baseddiag buy " + kind, wait=2.5)
            r.note(lines[-1].strip()[-200:] if lines else "no answer to baseddiag buy " + kind)
    paid = cash0 - r.cash()
    r.cmd("baseddiag planfree off", wait=0.4)
    r.close_dialog()
    site = r.site()
    r.check("ordered through the plan page and paid in cash", site.get("stage") in ("Constructing", "Ordered") and paid > 0,
            "%s, paid %s" % (site, paid))
    mark = r.mark()
    r.cmd("baseddiag advance", wait=1.0)
    r.wait_line(mark, r"sleep cycle \d+ applied|held in sleep cycle|Sleep skipped|" + FAILED, timeout=40)
    site = r.site()
    r.check("one night of work done", site.get("stage") == "Constructing" and site.get("nights") >= 1, site)
    answer, lines = r.save()
    bad = [l for l in lines if re.search(FAILED, l)]
    r.check("saved", bool(answer and answer.get("ok")) and not bad, bad[:1] or answer)
    record = dict(site, cash=r.cash(), paid=paid, files=save_files(r.steamid))
    r.check("Based data found in save 5", bool(record["files"]), os.path.join(SAVES, r.steamid))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(record, handle, indent=2)
    r.note("recorded: %s" % json.dumps({k: v for k, v in record.items() if k != "files"}))


def read(r):
    with open(OUT, encoding="utf-8") as handle:
        record = json.load(handle)
    log = r.lines((r.g.log_path(), 0))
    site = r.site()
    r.check("stage", site.get("stage") == record["stage"] and site.get("nights") == record["nights"],
            "now %s, saved %s %s/%s" % (site, record["stage"], record["nights"], "?"))
    cash = r.cash()
    r.check("cash", abs(cash - record["cash"]) < 1, "now %s, saved %s" % (cash, record["cash"]))
    bad = [l.strip()[-240:] for l in log if re.search(FAILED, l)]
    r.check("no-failure", not bad, bad[:2])
    answer, lines = r.save()
    bad = [l for l in lines if re.search(FAILED, l)]
    r.check("saved", bool(answer and answer.get("ok")) and not bad, bad[:1] or answer)


def off(r):
    with open(OUT, encoding="utf-8") as handle:
        record = json.load(handle)
    log = r.lines((r.g.log_path(), 0))
    stays = [l for l in log if "Based stays off: " in l]
    r.check("off", any("MapExpansions" in l for l in stays), stays[-1:] or "no 'Based stays off' line")
    told = r.wait_line((r.g.log_path(), 0), r"\[Based\] Construction notice shown: .*MapExpansions", timeout=30)
    r.check("told", bool(told), told.strip()[-240:] if told else "no notice line naming MapExpansions")
    before = save_files(r.steamid)
    r.check("file-found", bool(before) and set(before) == set(record["files"]), sorted(before))
    answer, lines = r.save()
    blocked = [l for l in lines if "Save blocked" in l]
    r.check("blocked", bool(blocked), lines[-3:])
    now = save_files(r.steamid)
    r.check("file-kept", now == before, "changed: %s" % [p for p in now if now.get(p) != before.get(p)])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", default=None)
    p.add_argument("--steamid", required=True, help="the slot player's Steam id, printed by session.ps1 ('save 5 of <id>')")
    p.add_argument("mode", choices=["write", "read", "off"])
    a = p.parse_args()
    r = Run(a.slot)
    r.steamid = a.steamid
    {"write": write, "read": read, "off": off}[a.mode](r)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
