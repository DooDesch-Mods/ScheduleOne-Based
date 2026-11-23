"""
In-game check of Based#67 and Based#68 on the finished Storage Unit: the loft door opens and shuts on the interact key,
and each new room (loft, east room, south room) has a wall switch that turns that room's lamps on and off and leaves
the other rooms alone. Unit 1's own switch downstairs no longer drives the new rooms.

The player stands next to the door or switch and presses interact through the bridge (the game's own
InteractableObject path, no Based code in between). A door counts as moved when its leaf's bounds turn by about 90°.
A switch must change the mean brightness of a camera shot of its own room's floor at night by at least LIT_CHANGE. Which
lamps it drove comes from MapExpansions' log line per lamp ("lamps: ... 'east-light-north' off by 'copy:east-switch'"):
only its own room's lamps may appear after the press. Brightness cannot judge the other rooms, because the loft and the
east room open onto each other through the walkway and one room's lamps light the other's floor.

Needs a Debug build of Based (`baseddiag`) in a running test slot and save 5 where the player owns the Storage Unit
(template `played`):

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Profile main -Build MapExpansions,_wt-Based-<topic> -Template played -NoApp
    python _wt-Based-<topic>/Tests/storageunit-door-switches.py [--slot mptest] [--shots <folder>]
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
    raise SystemExit("[storageunit-door-switches] Workspace/tools/bridge/s1.py not found")


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402
from PIL import Image, ImageStat  # noqa: E402

COMPLETE = "baseddiag site=storageunit phase ray.storageunit-bay:complete"
# The loft door (Content/StorageUnit/StorageWorld.cs Doorway): hinged at the north jamb, the closed leaf runs south
# along z at x -9.0. The player stands on the landing west of it.
DOOR_RECT = [-10.3, 104.3, -7.7, 107.3]
DOOR_STAND = (-9.9, 4.5, 105.5)
# Per room: where the player stands next to its switch, and the camera that looks at the room's floor under its lamps.
ROOMS = {
    "loft": dict(stand=(-7.9, 4.5, 104.6), eye=(-3.0, 5.6, 104.4), target=(-5.5, 3.25, 109.5)),
    "east": dict(stand=(0.55, 4.5, 104.6), eye=(2.8, 5.6, 104.4), target=(0.6, 3.25, 109.5)),
    "south": dict(stand=(0.55, 4.5, 100.4), eye=(2.8, 5.6, 100.8), target=(1.1, 3.25, 96.0)),
}
UNIT1_STAND = (-4.6, 2.5, 103.8)
LIT_CHANGE = 0.15
LAMP_LINE = re.compile(r"lamps: 'based_storage_upper' state '[^']+' '(\w+)-light-\w+' (on|off) by 'copy:(\w+)-switch'")


class Run:
    def __init__(self, slot, shots):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.slot = slot
        self.shots = shots
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

    def cmd(self, command, wait=1.0):
        result = self.g.run(command, max_wait_ms=int(max(wait, 1.0) * 1000) + 9000)
        lines = (result.get("result") or {}).get("lines") or [] if isinstance(result, dict) else []
        return [str(l.get("text", "")) if isinstance(l, dict) else str(l) for l in lines]

    def stand(self, at):
        self.g.call("teleport", {"x": at[0], "y": at[1], "z": at[2]}, timeout_ms=8000)
        time.sleep(1.0)

    def interact(self, query):
        r = self.g.call("interact", {"query": query}, timeout_ms=8000) or {}
        res = r.get("result") or r
        time.sleep(1.5)
        return res

    def door_bounds(self):
        """Centre and size of every renderer of the copied door in the doorway."""
        res = self.g.world("find_world_objects", {"rect": DOOR_RECT, "kind": "renderer", "limit": 200}) or {}
        out = {}
        for o in (res.get("result") or res).get("objects") or []:
            path = o.get("path") or ""
            if "loft-door" not in path and "Door" not in path:
                continue
            b = o.get("bounds") or {}
            # The unit's ground-floor door lies under the same rect; the loft door stands on the upper floor.
            if b.get("center") and b.get("size") and b["center"][1] > 3.0:
                out[path] = (tuple(b["center"]), tuple(b["size"]))
        return out

    def brightness(self, room, label):
        r = self.g.world("camera_shot", {"eye": list(ROOMS[room]["eye"]), "target": list(ROOMS[room]["target"]),
                                         "settleFrames": 20}) or {}
        path = (r.get("result") or r).get("path")
        if not path or not os.path.isfile(path):
            return None
        if self.shots:
            shutil.copy(path, os.path.join(self.shots, "storageunit-%s-%s.png" % (room, label)))
        with Image.open(path) as img:
            return ImageStat.Stat(img.convert("L")).mean[0] / 255.0

    def all_rooms(self, label):
        return {room: self.brightness(room, label) for room in ROOMS}

    def log_lines(self):
        """Lines of the running game's log so far (MelonLoader/Latest.log of the slot)."""
        root = os.path.normpath(os.path.join(_bridge(), "..", "..", ".."))
        folder = "_MPTest" if self.slot == "mptest" else "_" + self.slot.upper()
        path = os.path.join(root, folder, "Schedule I", "MelonLoader", "Latest.log")
        if not os.path.isfile(path):
            print("  no game log at %s" % path, flush=True)
            return []
        with open(path, encoding="utf-8", errors="replace") as f:
            return f.read().splitlines()

    def lamps_since(self, count):
        """(room, on/off) of every Storage Unit lamp MapExpansions switched after the first count log lines."""
        return [(m.group(1), m.group(2)) for m in (LAMP_LINE.search(l) for l in self.log_lines()[count:]) if m]


def changed(before, after):
    if before is None or after is None:
        return None
    return abs(after - before) / max(before, after, 1e-3)


def door(run):
    run.stand(DOOR_STAND)
    first = run.door_bounds()
    if not run.check("door: the copied loft door stands in the doorway", bool(first), "%d renderers" % len(first)):
        return
    swung = False
    for step in ("first press", "second press"):
        res = run.interact("door")
        now = run.door_bounds()
        moved = [p for p in now if p in first and max(abs(a - b) for a, b in zip(now[p][1], first[p][1])) > 0.3]
        target = res.get("target") or res
        if step == "first press":
            swung = run.check("door: the %s swings the leaf" % step, bool(moved), "interact %s; %d of %d renderers turned" % (
                target, len(moved), len(first)))
        else:
            # Back in place only counts after the first press really swung the leaf.
            back = [p for p in now if p in first and max(abs(a - b) for a, b in zip(now[p][1], first[p][1])) < 0.05]
            run.check("door: the %s swings it back" % step, swung and len(back) == len(first),
                      "interact %s; %d of %d renderers back%s" % (target, len(back), len(first), "" if swung else ", first press did not swing"))
        time.sleep(1.0)


def switches(run):
    run.g.world("set_time", {"time": 200})
    time.sleep(3)
    for room in ROOMS:
        before = run.brightness(room, "before")
        run.stand(ROOMS[room]["stand"])
        mark = len(run.log_lines())
        res = run.interact("modularswitch")
        after = run.brightness(room, "after")
        lamps = run.lamps_since(mark)
        own = changed(before, after)
        run.check("%s: its switch changes its own lamps" % room, own is not None and own >= LIT_CHANGE,
                  "interact %s; brightness %s -> %s" % (res.get("target") or res, before, after))
        mine = [l for l in lamps if l[0] == room]
        others = [l for l in lamps if l[0] != room]
        run.check("%s: its switch leaves the other rooms alone" % room, "target" in res and bool(mine) and not others,
                  "%d own lamps switched (%s), other rooms' lamps switched: %s" % (
                      len(mine), ", ".join(sorted({s for _, s in mine})) or "none", ", ".join("%s %s" % l for l in others) or "none"))
    run.stand(UNIT1_STAND)
    mark = len(run.log_lines())
    res = run.interact("Storage Unit/ModularSwitch")
    lamps = run.lamps_since(mark)
    run.check("unit 1: its switch downstairs leaves the new rooms alone", "target" in res and not lamps,
              "interact %s; new rooms' lamps switched: %s" % (res.get("target") or res, ", ".join("%s %s" % l for l in lamps) or "none"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--shots", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "storageunit-door-switches"))
    args = ap.parse_args()
    if args.shots:
        os.makedirs(args.shots, exist_ok=True)
    run = Run(args.slot, args.shots)
    log = run.cmd(COMPLETE, wait=10.0)
    run.check("the Storage Unit is finished", any("construction moved to" in l or "are complete" in l for l in log),
              next((l for l in reversed(log) if "baseddiag" in l), "no baseddiag line"))
    time.sleep(5)
    door(run)
    switches(run)
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
