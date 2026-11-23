"""
In-game check of Based#127: stations and employees near the new staircases can be targeted in management mode.

Two looks can fail near a staircase. The object selector (assigning a station) takes the first collider its 0.1 m look
cast hits, and the clipboard (picking an employee or station) the first one its 6 m look ray hits. This completes the
Storage Unit, the Bungalow and its storey and the Sweatshop's wing, then per site:

- Stations: `baseddiag pickscan <code>` casts up from every build tile. No tile may lie under a staircase, its
  landing or the invisible blocks beneath them.
- Employees: `baseddiag pickpoint x y z` looks at a worker standing on each of the site's staff places from eight
  standpoints around it. No look may stop on a Based or MapExpansions collider, and at least one must be clear.

Needs Debug builds of MapExpansions and Based in a running test slot and a save where the player owns the Storage
Unit, the Bungalow and the Sweatshop (template `played`). Uses MapExpansions' Tests/employee-reach.py for the console
helpers.

    python _wt-Based-<topic>/Tests/stair-select.py [--slot mptest] [--only storage,bungalow,sweatshop]
        [--mapexpansions MapExpansions]
"""
import argparse
import importlib.util
import os
import re
import sys

STAIR = re.compile(r"stair|underside|keep-out|landing", re.IGNORECASE)
UNDER = re.compile(r"pickscan: under (.*?): \d+ tiles:")
POINT = re.compile(r"clear from (\d+) of (\d+) standpoints(?:; blocked (.*))?\.$")
# Per site: the commands that complete its build, its property code and its staff places (StaffPlaces in the site).
SITES = {
    "storage": dict(code="storageunit", cmds=["baseddiag site=storageunit phase ray.storageunit-bay:complete"],
                    places=[(-9.25, 0.1, 97.6), (-10.6, 0.1, 97.9)]),
    "bungalow": dict(code="bungalow", cmds=["baseddiag site=bungalow phase complete",
                                            "baseddiag site=bungalow phase complete storey"],
                     places=[(-174.5, -3.7, 110.5), (-173.5, -3.7, 110.5), (-172.5, -3.7, 110.5),
                             (-174.5, -3.7, 111.5), (-173.5, -3.7, 111.5)]),
    "sweatshop": dict(code="sweatshop", cmds=["baseddiag site=sweatshop phase complete"],
                      places=[(-62.9, -4.0, 144.7), (-63.9, -4.0, 144.3)]),
}


def load_reach(root):
    path = os.path.join(root, "Tests", "employee-reach.py")
    spec = importlib.util.spec_from_file_location("employee_reach", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest")
    ap.add_argument("--only", default=",".join(SITES))
    ap.add_argument("--mapexpansions", default=r"F:\Projects\Mods\Schedule1\MapExpansions",
                    help="MapExpansions checkout whose Tests/employee-reach.py is used")
    args = ap.parse_args()
    reach = load_reach(args.mapexpansions)
    run, pick = reach.Run(args.slot), reach.pick

    for key in [k.strip() for k in args.only.split(",") if k.strip()]:
        site = SITES[key]
        for command in site["cmds"]:
            log = run.cmd(command, wait=10.0)
            run.check("%s: %s" % (key, command), any("construction moved to" in l or "nothing to" in l or
                                                     "are complete" in l for l in log), pick(log, "baseddiag"))
        log = [l.strip() for l in run.cmd("baseddiag pickscan " + site["code"], wait=2.0) if "pickscan" in l]
        done = [l for l in log if "pickscan: done" in l]
        stairs = [l[l.find("under "):] for l in log
                  if (m := UNDER.search(l)) and STAIR.search(m.group(1))]
        run.check("%s: no build tile under a staircase" % key, bool(done) and not stairs,
                  "; ".join(stairs) if stairs else (done[0][done[0].find("done"):] if done else pick(log, "pickscan")
                                                    or "no pickscan answer"))
        for x, y, z in site["places"]:
            log = run.cmd("baseddiag pickpoint %g %g %g" % (x, y, z), wait=1.5)
            line = next((l.strip() for l in log if "pickpoint: worker" in l), "")
            m = POINT.search(line)
            blocked = (m.group(3) or "") if m else ""
            ok = m is not None and int(m.group(1)) > 0 and "MapExpansions." not in blocked and "based_" not in blocked
            run.check("%s: worker at (%g, %g, %g) can be targeted" % (key, x, y, z), ok,
                      line[line.find("clear"):] if line else pick(log, "pickpoint") or "no pickpoint answer")
    return run.summary()


if __name__ == "__main__":
    sys.exit(main())
