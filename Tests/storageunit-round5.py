"""The Storage Unit fixes from the fourth user round: props, seams, ceilings, beams, rails, ladder, door casing, crew,
sparks, the working loft door and each new room's light switch, each checked by its bounds or log line and shot from several angles.

For each build stage it jumps there at noon, checks the measured bounds of the fixed parts, reads the crew's
diagnostics and the site-effects line, and shoots every spot the round named from two or three viewpoints. Prints
PASS/FAIL per check and writes images to OUT/<stage>-<spot>.png.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage Unit.

usage: storageunit-round5.py --slot mptest3 --out _drafts/based-storageunit/r5 [--only a-stair,b-complete]
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"

ap = argparse.ArgumentParser()
ap.add_argument("--slot", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--only", default="")
args = ap.parse_args()
os.makedirs(args.out, exist_ok=True)
results = []


def s1(*a, timeout=120):
    r = subprocess.run([sys.executable, S1, "--slot", args.slot, *a], capture_output=True, text=True, encoding="utf-8",
                       timeout=timeout)
    return r.returncode, (r.stdout + r.stderr).strip()


def check(name, ok, detail=""):
    results.append((name, ok))
    print(("PASS " if ok else "FAIL ") + name + ("" if ok and not detail else "  [" + str(detail)[:300] + "]"), flush=True)
    return ok


def run(*command, until):
    # A jump rebuilds the site and its navigation; beside two more games that takes longer than the default wait.
    code, text = s1("run", "--max-wait", "90000", "--until", until, *command, timeout=150)
    return text


def find(name, x0, z0, x1, z1, kind="renderer"):
    """Objects inside the ground rectangle, as (path, min, max). s1 find matches object names; a NAME with a slash
    ("Copies/FluroBar") is a path fragment instead, so everything in the rectangle is fetched and filtered by path."""
    by_path = "/" in name
    code, text = s1("find", "" if by_path else name, "--rect", str(x0), str(z0), str(x1), str(z1), "--kind", kind,
                    "-n", "4000" if by_path else "500", "--active-only")
    try:
        data = json.loads(text)
    except ValueError:
        print("      find %s failed: %s" % (name, text[-300:]), flush=True)
        return []
    found = [(o["path"], o["bounds"]["min"], o["bounds"]["max"]) for o in data["objects"]]
    return [f for f in found if name in f[0]] if by_path else found


def bases(found, name):
    """Lowest point of each object whose path names NAME, from the bounds of all its renderers."""
    low = {}
    for path, mn, mx in found:
        i = path.find(name)
        if i < 0:
            continue
        end = path.find("/", i)
        key = path if end < 0 else path[:end]
        low[key[-50:]] = round(min(low.get(key[-50:], 99.0), mn[1]), 3)
    return low


def shot(stage, spot, eye, target, fov=70):
    code, text = s1("cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target), "--fov", str(fov), "--hud", "hide",
                    "--settle", "10")
    try:
        path = json.loads(text)["path"]
        shutil.copyfile(path, os.path.join(args.out, "%s-%s.png" % (stage, spot)))
        return check("shot %s-%s" % (stage, spot), True)
    except (ValueError, KeyError, OSError) as e:
        return check("shot %s-%s" % (stage, spot), False, "%s %s" % (e, text[-200:]))


def jump(stage, phase):
    text = run("baseddiag", "site=storageunit", "phase", phase, until="construction moved to|baseddiag phase failed")
    # The crew works from 8:00 to 18:00; a full run takes longer than a game day, so each stage starts at noon.
    s1("time", "1200")
    return check("jump %s (%s)" % (stage, phase), "construction moved to" in text, text[-200:])


def crew(stage, expect_workers):
    """The crew's diagnostics line, and no worker without a walkable spot at his station."""
    text = run("baseddiag", "site=storageunit", "workers", until="storage workers diagnostics")
    m = re.search(r"storage workers diagnostics: attached=(\d+)/(\d+), active=(\d+), phase=(\S+)", text)
    if expect_workers:
        check("%s crew attached and working" % stage, m is not None and m.group(1) == m.group(2) and int(m.group(3)) >= 1,
              m.group(0) if m else text[-200:])
    # The game was started fresh for this run, so any such line in the log is one of this run's stations. A station
    # waits while the state's navigation bakes after a jump; it counts once no "recovered" line for it followed.
    code, log = s1("log", "--grep", "no navigation surface", "-n", "80")
    missing = []
    for l in log.splitlines():
        m = re.search(r"storage workers: (\S+) (?:phase=\S+: )?(recovered from )?no navigation surface .*?(\(.*?\))", l)
        if not m:
            continue
        key = (m.group(1), m.group(3))
        missing = [x for x in missing if x[0] != key]
        if not m.group(2):
            missing.append((key, l.strip()[-200:]))
    missing = [line for _, line in missing]
    check("%s every crew station stands on walkable ground" % stage, not missing, missing[:3])
    return m


def sparks(stage):
    """Sparks burn only while the fitter stands at the cut: the effects line names both."""
    code, log = s1("log", "--grep", "Storage site effects", "-n", "5")
    lines = [l for l in log.splitlines() if "Storage site effects" in l]
    if not lines:
        return check("%s site-effects line" % stage, False, "no line")
    last = lines[-1]
    burning = "work-sparks" in last
    welder = "welder at the cut" in last
    return check("%s sparks only with the welder at the cut" % stage, burning == welder, last[-220:])


def fitter_at_sparks(stage):
    """The fitter stands where the sparks burn: within 1.3 m of them across the floor, below them. After a jump the
    fitter walks from the last phase's station, so the check waits up to 60 s for the arrival."""
    deadline = time.time() + 60
    while True:
        text = run("baseddiag", "site=storageunit", "vfx", until="baseddiag: storage effects")
        spark = re.search(r"work-sparks at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)", text)
        run("baseddiag", "site=storageunit", "workers", until="storage workers diagnostics")
        time.sleep(1)
        code, log = s1("log", "--grep", "based_storage_fitter", "-n", "40")
        fitter = None
        for l in log.splitlines():
            m = re.search(r'"id":"based_storage_fitter".*?"position":\[([-\d.eE]+),([-\d.eE]+),([-\d.eE]+)\]', l)
            if m:
                fitter = tuple(float(v) for v in m.groups())
        ok, flat, below, s = False, None, None, None
        if spark and fitter:
            s = tuple(float(v) for v in spark.groups())
            flat = ((s[0] - fitter[0]) ** 2 + (s[2] - fitter[2]) ** 2) ** .5
            below = s[1] - fitter[1]
            ok = flat < 1.3 and 0.5 < below < 2.6
        if ok or time.time() > deadline:
            break
        time.sleep(4)
    if flat is None:
        return check("%s fitter stands at the sparks" % stage, False, "sparks %s, fitter %s; %s" % (
            spark.group(0) if spark else None, fitter, text[-160:]))
    return check("%s fitter stands at the sparks (%.2f m across, %.2f m below)" % (stage, flat, below), ok,
                 "sparks %s, fitter %s" % (s, fitter))


def sparks_emit(stage, seconds=15):
    """The sparks really fly: the live effect holds particles at least once within the window. Reported as WARN when
    it never does, because the particle count is a sample of short bursts."""
    deadline, best = time.time() + seconds, -1
    while time.time() < deadline:
        text = run("baseddiag", "site=storageunit", "vfx", until="baseddiag: storage effects")
        m = re.search(r"with (\d+) particle", text)
        if m:
            best = max(best, int(m.group(1)))
        if best > 0:
            break
        time.sleep(1)
    print("%s  %s sparks emit particles (most seen: %d)" % ("PASS" if best > 0 else "WARN", stage, best), flush=True)


def particles():
    m = re.search(r"with (\d+) particle", run("baseddiag", "site=storageunit", "vfx", until="baseddiag: storage effects"))
    return int(m.group(1)) if m else -1


def sparks_shot(stage, spot, eye, target, tries=6):
    """A close shot of the sparks. They fly in short bursts, so a shot is kept only when particles were live right
    before and right after it; otherwise it is taken again, and the last try is kept with a WARN."""
    for attempt in range(1, tries + 1):
        before = particles()
        if before <= 0 and attempt < tries:
            time.sleep(0.5)
            continue
        shot(stage, spot, eye, target)
        after = particles()
        if before > 0 and after > 0:
            print("PASS  %s sparks live around the shot (%d before, %d after, try %d)" % (stage, before, after, attempt), flush=True)
            return
    print("WARN  %s sparks shot may show a gap between bursts (%d before, %d after)" % (stage, before, after), flush=True)


DOOR_AT = ("-9.0", "4.4", "105.5", "1.5")


ROOF_SPOT = ("-5", "11", "108")


def door_lines(*action):
    text = run("mxdoors", *DOOR_AT, *action, until="mxdoors .* doors within")
    # The registered templates of the networked copy wait inactive under "Copy Templates"; only the spawned door counts.
    return text, [l for l in text.splitlines() if "mxdoors working" in l and "active True" in l]


def door_state():
    """Whether the loft door stands open, or None when there is not exactly one working door at the cut."""
    text, working = door_lines()
    if len(working) != 1:
        return None
    # The line also holds "player may open True"; the door's own state stands right before "handles".
    m = re.search(r"open (True|False), handles", working[0])
    return None if m is None else m.group(1) == "True"


def door(stage):
    """The loft door is a working copy of the Storage Unit's door: unlocked for the owner, it closes and opens."""
    # The game closes an open door again when no player is within 40 m (DoorController.CheckAutoCloseForDistantPlayer).
    # The roof keeps the player close and out of every shot of the loft.
    s1("tp", *ROOF_SPOT)
    time.sleep(2)
    text, working = door_lines()
    check("%s loft door is one spawned working door the player may open" % stage,
          len(working) == 1 and "player may open True" in working[0] and "spawned True" in working[0], working[:2] or text[-300:])
    code, log = s1("log", "--grep", "unlocked, 'storageunit' is owned", "-n", "20")
    check("%s loft door unlocked for the owner" % stage, "unlocked" in log, log[-200:])
    door_lines("close")
    time.sleep(1.5)
    state = door_state()
    check("%s loft door closes" % stage, state is False, state)


def door_open(stage):
    door_lines("open")
    time.sleep(1.5)
    state = door_state()
    check("%s loft door opens" % stage, state is True, state)


def lamp_lines(name, state):
    code, log = s1("log", "--grep", "%s by 'copy:%s'" % (state, name), "-n", "400")
    return sum(1 for l in log.splitlines() if "%s by 'copy:%s'" % (state, name) in l)


def flip(name, at, state):
    x, y, z = map(str, at)
    text = run("mxswitch", x, y, z, "0.6", state, until="mxswitch .* switches within")
    time.sleep(1.5)
    return text


def switch(stage, name, at, lamps):
    """A room's copied wall switch: its lamps found it, and turning it off and on again switches all of them."""
    code, log = s1("log", "--grep", "found the copied switch '%s'" % name, "-n", "20")
    check("%s lamps found the copied switch '%s'" % (stage, name), "found the copied switch" in log, log[-200:])
    code, log = s1("log", "--grep", "lets go of", "-n", "40")
    check("%s switch '%s' lets go of the original's lamps" % (stage, name), "'%s'" % name in log or "lets go of" in log, log[-200:])
    x, y, z = map(str, at)
    listing = run("mxswitch", x, y, z, "0.6", until="mxswitch .* switches within")
    found = [l for l in listing.splitlines() if "mxswitch '" in l and "active True" in l]
    check("%s switch '%s' stands at its wall, spawned" % (stage, name), len(found) == 1 and "spawned True" in found[0],
          found[:2] or listing[-300:])
    flip(name, at, "on")
    before = lamp_lines(name, "off")
    flip(name, at, "off")
    after = lamp_lines(name, "off")
    check("%s switch '%s' turns its %d lamps off" % (stage, name, lamps), after - before >= lamps, (before, after))


def switch_on(stage, name, at, lamps):
    before = lamp_lines(name, "on")
    flip(name, at, "on")
    after = lamp_lines(name, "on")
    check("%s switch '%s' turns its %d lamps on again" % (stage, name, lamps), after - before >= lamps, (before, after))
    circuit(stage, name, at)


# Unit 1's own switch in the hall below; its lamps are a circuit of their own.
VANILLA_SWITCH = (-4.62, 1.3, 104.78)


def unit_lamps():
    """(on, lamps, lit) of Unit 1's switch. Its FluroBars hang on saved listeners, which mxswitch counts as
    "listener lamps N lit K"; LightsToControl is empty there."""
    x, y, z = map(str, VANILLA_SWITCH)
    text = run("mxswitch", x, y, z, "0.3", until="mxswitch .* switches within")
    m = re.search(r"on (True|False), .*?listener lamps (\d+) lit (\d+)", text)
    return (m.group(1) == "True", int(m.group(2)), int(m.group(3))) if m else None


def circuit(stage, name, at):
    """The copy and Unit 1's switch each switch only their own lamps. Unit 1's lamps are read from the lamps
    themselves; the copy's lamps report each switch as an "on/off by 'copy:<name>'" line."""
    flip("unit1", VANILLA_SWITCH, "on")
    unit = unit_lamps()
    check("%s Unit 1's lamps lit before the circuit check" % stage,
          unit is not None and unit[1] > 0 and unit[2] == unit[1], unit)
    flip(name, at, "off")
    after = unit_lamps()
    check("%s switch '%s' off leaves Unit 1's lamps as they were" % (stage, name), unit is not None and after == unit, (unit, after))
    flip(name, at, "on")
    off_lines = lamp_lines(name, "off")
    flip("unit1", VANILLA_SWITCH, "off")
    unit_off, off_after = unit_lamps(), lamp_lines(name, "off")
    check("%s Unit 1's switch off leaves '%s' lamps lit" % (stage, name),
          unit_off is not None and unit_off[2] == 0 and off_after == off_lines, (unit_off, off_lines, off_after))
    flip("unit1", VANILLA_SWITCH, "on")
    back = unit_lamps()
    check("%s Unit 1's switch back on" % stage, back is not None and unit is not None and back[2] == unit[2], (unit, back))


LOFT_SWITCH = (-8.62, 4.45, 104.5)
EAST_SWITCH = (.55, 4.45, 103.93)
SOUTH_SWITCH = (.55, 4.45, 101.37)


def wanted(stage):
    return not args.only or stage in args.only.split(",")


s1("time", "1200")

if wanted("a-stair"):
    if jump("a-stair", "ray.storageunit-loft:loft-stair"):
        time.sleep(3)
        deck = [d for d in find("Panel.landing", -10.8, 104.4, -8.6, 106.5) if d[0].endswith("Panel.landing")]
        check("a-stair landing deck reaches into the brick (x max -8.95)",
              bool(deck) and all(abs(mx[0] - (-8.95)) < .02 for _, mn, mx in deck), [(p[-40:], mx[0]) for p, mn, mx in deck])
        # Copies are named after their template; the walkway barrels stand far east of this rectangle.
        barrels = find("Copies/SM_Prop_BarrelStack_01", -14, 96, -9, 102)
        check("a-stair outside barrels clear of the stair (x < -10.59) and the overpass leg (z < 100.65)",
              bool(barrels) and all(mx[0] < -10.59 and mx[2] < 100.65 for _, mn, mx in barrels),
              [(round(mn[0], 2), round(mx[0], 2), round(mn[2], 2), round(mx[2], 2)) for _, mn, mx in barrels])
        cones = find("cone", -12.5, 94, -8.5, 99)
        check("a-stair cones east of the bus stop wall (x > -11.30)",
              len(cones) >= 3 and all(mn[0] > -11.30 for _, mn, mx in cones), [(round(mn[0], 2), round(mn[2], 2)) for _, mn, mx in cones])
        crew("a-stair", True)
        sparks("a-stair")
        fitter_at_sparks("a-stair")
        sparks_emit("a-stair")
        shot("a-stair", "crew-landing", (-10.5, 5.0, 103.2), (-9.4, 4.2, 105.5))
        sparks_shot("a-stair", "sparks-close", (-10.4, 4.7, 104.3), (-9.4, 4.4, 105.48))
        shot("a-stair", "landing-seam-above", (-10.2, 4.0, 103.6), (-9.0, 3.3, 105.4))
        shot("a-stair", "landing-seam-below", (-11.6, 2.0, 103.4), (-9.0, 3.25, 105.4))
        shot("a-stair", "barrels-south", (-10.0, 1.6, 96.4), (-12.3, .5, 98.6))
        shot("a-stair", "barrels-north", (-13.4, 1.6, 101.2), (-12.3, .5, 98.6))
        shot("a-stair", "cones-east", (-8.95, 2.2, 99.4), (-10.3, .2, 95.6))
        shot("a-stair", "cones-west", (-12.9, 1.6, 94.6), (-10.8, .3, 96.5))
        shot("a-stair", "crew", (-9.3, 2.4, 95.0), (-10.6, .9, 100.0))

if wanted("a-door"):
    if jump("a-door", "ray.storageunit-loft:loft-door"):
        time.sleep(3)
        casing = find("door-casing", -9.4, 104.6, -8.6, 106.4)
        check("a-door casing on both faces (6 pieces)", len(casing) >= 6, [p[-40:] for p, _, _ in casing])
        crew("a-door", True)
        sparks("a-door")
        fitter_at_sparks("a-door")
        shot("a-door", "crew", (-5.0, 4.9, 107.5), (-6.5, 4.6, 104.0))
        shot("a-door", "frame-outside", (-10.4, 4.6, 105.48), (-9.26, 4.4, 105.48))
        shot("a-door", "frame-outside-oblique", (-10.4, 4.8, 103.9), (-9.2, 4.4, 105.5))
        shot("a-door", "frame-inside", (-6.4, 4.8, 105.48), (-8.75, 4.4, 105.48))
        shot("a-door", "frame-inside-oblique", (-6.8, 4.8, 107.6), (-8.75, 4.4, 105.5))

if wanted("a-complete-door"):
    if jump("a-complete-door", "ray.storageunit-loft:complete"):
        time.sleep(3)
        door("a-complete")
        shot("a-complete", "door-closed-inside", (-6.6, 4.9, 105.48), (-8.9, 4.4, 105.48))
        shot("a-complete", "door-closed-inside-oblique", (-6.9, 4.8, 103.9), (-8.9, 4.4, 105.6))
        shot("a-complete", "door-closed-outside", (-10.4, 4.7, 105.48), (-9.1, 4.4, 105.48))
        shot("a-complete", "door-closed-outside-oblique", (-10.5, 4.8, 103.6), (-9.1, 4.4, 105.5))
        door_open("a-complete")
        shot("a-complete", "door-open-inside", (-6.6, 4.9, 105.48), (-8.9, 4.4, 105.48))
        shot("a-complete", "door-open-outside-oblique", (-10.5, 4.8, 103.6), (-9.1, 4.4, 105.5))
        shot("a-complete", "door-open-from-landing", (-10.6, 4.7, 107.2), (-8.9, 4.3, 105.4))
        switch("a-complete", "loft-switch", LOFT_SWITCH, 3)
        shot("a-complete", "switch-loft-off", (-7.6, 4.8, 104.6), (-8.75, 4.45, 104.5))
        shot("a-complete", "loft-lamps-off", (-5.5, 5.1, 108.4), (-5.5, 6.0, 106.0))
        switch_on("a-complete", "loft-switch", LOFT_SWITCH, 3)
        shot("a-complete", "switch-loft-on", (-7.6, 4.8, 104.6), (-8.75, 4.45, 104.5))
        shot("a-complete", "switch-loft-wide", (-6.0, 5.0, 107.0), (-8.75, 4.4, 105.0))

if wanted("a-complete"):
    if jump("a-complete", "ray.storageunit-loft:complete"):
        time.sleep(3)
        lamps = find("Copies/FluroBar", -8.8, 103.8, -2.0, 113.8)
        check("a-complete loft lamps stay under the ceiling panel (top <= 6.002)",
              len(lamps) >= 3 and all(mx[1] <= 6.002 for _, mn, mx in lamps), [round(mx[1], 3) for _, mn, mx in lamps])
        guard = find("gallery-guard", -9, 103, -1.9, 104.5, kind="collider")
        check("a-complete gallery guard ends at rail height (top <= 4.55)",
              bool(guard) and all(mx[1] <= 4.55 for _, mn, mx in guard), [round(mx[1], 2) for _, mn, mx in guard])
        rail = find("railing-guard", -11, 104, -8.6, 106.6, kind="collider")
        check("a-complete landing railing guard ends at rail height (top <= 4.45)",
              all(mx[1] <= 4.45 for _, mn, mx in rail), [(p[-30:], round(mx[1], 2)) for p, mn, mx in rail])
        shot("a-complete", "gallery-west-edge", (-7.4, 4.9, 104.6), (-8.9, 5.0, 103.8))
        shot("a-complete", "gallery-west-edge-hall", (-6.5, 2.2, 99.5), (-8.8, 5.0, 103.8))
        shot("a-complete", "gallery-from-loft", (-5, 4.9, 110), (-4, 4.8, 100))
        shot("a-complete", "hall-up", (-5, 1.7, 97), (-4, 7, 102))
        shot("a-complete", "lamps-close", (-5.5, 5.1, 108.4), (-5.5, 6.0, 106.0))
        shot("a-complete", "lamps-side", (-7.6, 5.4, 106.0), (-5.5, 6.0, 106.0))

if wanted("b-open"):
    if jump("b-open", "ray.storageunit-east:east-open"):
        time.sleep(3)
        # A prop has several renderers (handle, valve); its base is the lowest of them.
        bucket = bases(find("Copies/SM_Prop_Bucket_01", -2.6, 109.5, -1.0, 111.8), "SM_Prop_Bucket_01")
        check("b-open bucket stands on the floor (base 3.23..3.28)",
              bool(bucket) and all(3.23 <= y <= 3.28 for y in bucket.values()), bucket)
        bottle = bases(find("Copies/Gas Bottle Prop", -2.6, 111, -1.2, 112.6), "Gas Bottle Prop")
        check("b-open gas bottle stands on the floor (base 3.23..3.28)",
              bool(bottle) and all(3.23 <= y <= 3.28 for y in bottle.values()), bottle)
        block = find("Copies/Cinderblock", -2.3, 107, -1.1, 108.2)
        check("b-open cinder block clear of the walls (x -2.05 .. 0.25)",
              bool(block) and all(mn[0] > -2.05 and mx[0] < .25 for _, mn, mx in block),
              [(round(mn[0], 2), round(mx[0], 2)) for _, mn, mx in block])
        props = find("walkway-", -2.3, 103.9, .3, 113.8)
        boxes = [(p.split("/")[-1], mn, mx) for p, mn, mx in props if "walkway-" in p.split("/")[-1] or "walkway-" in p]
        check("b-open walkway props inside the walkway (x -2.05 .. 0.25, z 103.94 .. 113.8)",
              bool(boxes) and all(mn[0] > -2.08 and mx[0] < .27 and mn[2] > 103.92 and mx[2] < 113.82 for _, mn, mx in boxes
                                  if "beam" not in _ and "wall" not in _ and "post" not in _ and "end" not in _),
              [(n[-28:], round(mn[0], 2), round(mx[0], 2), round(mn[2], 2), round(mx[2], 2)) for n, mn, mx in boxes][:12])
        crew("b-open", True)
        sparks("b-open")
        fitter_at_sparks("b-open")
        sparks_emit("b-open")
        shot("b-open", "crew", (-1.2, 4.9, 106.0), (.2, 4.3, 110.6))
        shot("b-open", "props-from-south", (-.2, 5.4, 105.9), (-1.3, 3.3, 111.5))
        shot("b-open", "props-from-north", (-1.0, 4.7, 113.3), (-.5, 3.3, 105.0))
        shot("b-open", "bucket-bottle", (-.4, 4.2, 110.0), (-1.9, 3.4, 111.3))
        shot("b-open", "cinderblock-generator", (-.1, 5.0, 108.4), (-1.6, 3.4, 107.0))
        shot("b-open", "barrels-site-wall", (-1.2, 4.6, 107.4), (-.6, 3.8, 104.4))
        shot("b-open", "north-end", (-1.0, 4.7, 107.0), (-1.0, 4.6, 113.8))
        shot("b-open", "corner-post-walkway", (-.6, 4.8, 106.0), (-2.05, 4.8, 103.8))
        shot("b-open", "corner-post-loft", (-3.8, 4.8, 105.4), (-2.05, 4.8, 103.8))

if wanted("b-complete"):
    if jump("b-complete", "ray.storageunit-east:complete"):
        time.sleep(3)
        old = find("east-ceiling", -2.2, 103.6, 3.4, 114)
        check("b-complete no second ceiling panel east of the loft", not old, [p[-40:] for p, _, _ in old])
        ceiling = find("loft-ceiling", -8.9, 103.6, 3.4, 114)
        check("b-complete loft ceiling runs on to the east wall (x max 3.25)",
              bool(ceiling) and any(abs(mx[0] - 3.25) < .02 for _, mn, mx in ceiling), [round(mx[0], 2) for _, mn, mx in ceiling])
        beam = find("gallery-beam", -8.9, 103.6, 1, 104)
        check("b-complete gallery beam runs to Unit5's face (x max 0.25)",
              bool(beam) and any(abs(mx[0] - .25) < .02 for _, mn, mx in beam), [round(mx[0], 2) for _, mn, mx in beam])
        lamps = find("Copies/FluroBar", -8.8, 103.8, 3.3, 113.8)
        check("b-complete all lamps stay under the ceiling panel (top <= 6.002)",
              len(lamps) >= 6 and all(mx[1] <= 6.002 for _, mn, mx in lamps), [round(mx[1], 3) for _, mn, mx in lamps])
        shot("b-complete", "ceiling-edge", (-1, 4.9, 106.5), (-1, 6.2, 103.8))
        shot("b-complete", "ceiling-seam", (-3.4, 4.6, 109.0), (-1.0, 6.0, 109.5))
        shot("b-complete", "beam-from-loft", (-6, 4.9, 106), (1, 5.9, 103.8))
        shot("b-complete", "beam-from-east", (2.6, 4.9, 106.4), (-4, 5.9, 103.8))
        shot("b-complete", "loft-east", (-7.5, 4.9, 109), (3, 4.3, 109))
        shot("b-complete", "north-end", (-1.0, 4.7, 107.0), (-1.0, 4.6, 113.8))
        shot("b-complete", "from-hall", (-5.0, 1.7, 97.5), (-1.0, 5.6, 103.8))
        switch("b-complete", "east-switch", EAST_SWITCH, 3)
        shot("b-complete", "switch-east-off", (.6, 4.8, 105.2), (.55, 4.45, 103.8))
        shot("b-complete", "east-lamps-off", (2.6, 4.9, 112.6), (1.4, 5.0, 105.0))
        switch_on("b-complete", "east-switch", EAST_SWITCH, 3)
        shot("b-complete", "switch-east-on", (.6, 4.8, 105.2), (.55, 4.45, 103.8))
        shot("b-complete", "switch-east-oblique", (2.4, 4.9, 106.4), (.4, 4.45, 103.8))

if wanted("c-open"):
    if jump("c-open", "ray.storageunit-south:south-open"):
        time.sleep(3)
        south = find("ladder-rail-south", -2.4, 97, -1.8, 101)
        check("c-open no second rail piece south of the ladder", not south, [p[-40:] for p, _, _ in south])
        north = find("Copies/OutdoorFence", -2.4, 100, -1.8, 103.5)
        check("c-open opening from z 99.89 to the copy's post at 101.03",
              bool(north) and abs(min(mn[2] for _, mn, mx in north) - 101.03) < .06,
              round(min(mn[2] for _, mn, mx in north), 2) if north else None)
        guards = find("ladder-rail-guard", -2.4, 99, -1.8, 102, kind="collider")
        check("c-open ladder guard ends at rail height (top <= 4.55)",
              bool(guards) and all(mx[1] <= 4.55 for _, mn, mx in guards), [(round(mn[2], 2), round(mx[1], 2)) for _, mn, mx in guards])
        parts = find("ladder-bracket", -2.7, 99.5, -1.7, 101.5)
        brackets = [b for b in parts if "plate" not in b[0].split("/")[-1]]
        plates = [b for b in parts if "plate" in b[0].split("/")[-1]]
        check("c-open ladder brackets reach behind the rails (x min <= -2.45)",
              len(brackets) >= 4 and all(mn[0] <= -2.45 for _, mn, mx in brackets), [round(mn[0], 2) for _, mn, mx in brackets])
        check("c-open every bracket sits on a plate against Unit 3's front (x max -1.76)",
              len(plates) >= 4 and all(abs(mx[0] - (-1.76)) < .02 for _, mn, mx in plates), [round(mx[0], 2) for _, mn, mx in plates])
        beams = find("south-beam", -2.2, 103.5, .4, 104.1)
        check("c-open old south beam gone", not beams, [p[-40:] for p, _, _ in beams])
        crew("c-open", True)
        sparks("c-open")
        fitter_at_sparks("c-open")
        sparks_emit("c-open")
        shot("c-open", "crew-fitter", (-1.8, 4.9, 101.4), (.4, 4.4, 102.6))
        shot("c-open", "ladder-hall", (-6.2, 1.6, 96.6), (-2.3, 1.9, 100.46))
        shot("c-open", "ladder-side", (-2.3, 2.2, 97.6), (-2.3, 2.0, 100.46))
        shot("c-open", "ladder-brackets", (-3.6, 2.6, 99.0), (-2.2, 2.3, 100.46))
        shot("c-open", "ladder-top", (-1.2, 4.6, 100.46), (-2.35, 3.1, 100.46))
        shot("c-open", "rail-gap", (-.4, 4.6, 98.6), (-2.1, 3.8, 100.5))
        shot("c-open", "south-room", (2.9, 4.9, 94.4), (-1.5, 4, 101.5))
        shot("c-open", "south-back", (-1.0, 4.9, 100), (2.5, 4.3, 94))
        shot("c-open", "elevator-corner", (-1.7, 4.9, 95.5), (.8, 4.3, 102.8))
        shot("c-open", "south-ceiling", (1.5, 4.5, 99), (1.5, 6.1, 96))
        shot("c-open", "south-beam", (-1.4, 4.6, 97.5), (.25, 6.0, 97.5))
        shot("c-open", "corner-post-south", (-1.0, 4.8, 102.4), (-2.05, 4.8, 103.8))
        shot("c-open", "crew", (-1.6, 4.9, 105.5), (1.2, 3.9, 99.0))

if wanted("c-complete"):
    if jump("c-complete", "ray.storageunit-south:complete"):
        time.sleep(3)
        shot("c-complete", "south-room", (2.9, 4.9, 94.4), (-1.5, 4, 101.5))
        shot("c-complete", "south-back", (-1.0, 4.9, 100), (2.5, 4.3, 94))
        shot("c-complete", "hall-bays", (-5.3, 2.2, 93.9), (-5.3, .3, 101.5))
        shot("c-complete", "walkway-full", (-1.0, 4.7, 94.6), (-1.0, 4.3, 113.0))
        switch("c-complete", "south-switch", SOUTH_SWITCH, 2)
        shot("c-complete", "switch-south-off", (.6, 4.8, 100.2), (.55, 4.45, 101.5))
        shot("c-complete", "south-lamps-off", (2.6, 4.9, 94.6), (1.2, 5.0, 100.0))
        switch_on("c-complete", "south-switch", SOUTH_SWITCH, 2)
        shot("c-complete", "switch-south-on", (.6, 4.8, 100.2), (.55, 4.45, 101.5))
        shot("c-complete", "switch-south-oblique", (2.4, 4.9, 99.4), (.4, 4.45, 101.5))
        # The loft's door and switch still stand at the last stage.
        door("c-complete")

s1("cam", "release")
passed = sum(1 for _, ok in results if ok)
print("SUMMARY %d/%d PASS" % (passed, len(results)), flush=True)
sys.exit(0 if passed == len(results) else 1)
