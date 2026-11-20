"""
Hiring into the finished Sweatshop next to Actually Working Employees with its capacity override on (Based#85).

    pwsh -File Workspace/tools/session/session.ps1 -Profile main -Build MapExpansions,_wt-Based-<topic>,_wt-ActuallyWorkingEmployees-<topic> -Template mrmonkey-dock -NoApp
    python _wt-Based-<topic>/Tests/sweatshop-hire-with-awe.py --slot mptest

Needs Debug builds of Based (`baseddiag`) and AWE (`awe idle`, `awe set`) and a save whose Sweatshop room is finished.
  capacity-kept     the override's 15 does not lower Based's capacity
  places-cover      the Sweatshop has a place for every employee it can hold
  hire N            one hire adds the employee, keeps every place and adds none
  no-throw          no IndexOutOfRange from Employee.AssignProperty during the hires
  break-recovered   a synthetic assignment with one idle point ('awe idle break') is finished by AWE and logged
  break-no-error    that assignment leaves no IndexOutOfRange in the log
  awe sees places   AWE's view of the Sweatshop holds Based's places after the hires
Prints PASS or FAIL per check and a summary line.
"""
import argparse
import importlib.util
import os
import re
import sys
import time

spec = importlib.util.spec_from_file_location("storage", os.path.join(os.path.dirname(__file__), "storageunit-idle-points.py"))
storage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(storage)
check, s1, run, results = storage.check, storage.s1, storage.run, storage.results

storage.SITE = "site=ming.sweatshop"
CODE = "sweatshop"
OVERRIDE = 15
THROW = "IndexOutOfRangeException"


def log_count(slot, pattern):
    text = s1(slot, "log", "-n", "6000")[1]
    return len(re.findall(pattern, text)), text


def head(slot):
    h, _, _ = storage.staff(slot)
    return h


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--hires", type=int, default=2)
    a = ap.parse_args()

    run(a.slot, r"\[Dev\]|awe", "awe set property.enableemployeecapacityoverride on")
    run(a.slot, r"\[Dev\]|awe", "awe set property.employeecapacity %d" % OVERRIDE)
    time.sleep(3)

    h = head(a.slot)
    if not check("staff diagnostics answered", h is not None, h):
        return 1
    count, limit, places = h
    check("capacity-kept", limit > OVERRIDE, "capacity %d, override %d" % (limit, OVERRIDE))
    check("places-cover", places >= limit, "places %d, capacity %d" % (places, limit))

    throws0, _ = log_count(a.slot, THROW)
    added0, _ = log_count(a.slot, r"sweatshop has \d+ places for employees, \d+ of them added")
    for i in range(a.hires):
        if count >= limit:
            check("hire %d" % (i + 1), False, "the Sweatshop is full (%d/%d); use a save with room" % (count, limit))
            break
        kind = storage.EMPLOYEE_TYPES[i % len(storage.EMPLOYEE_TYPES)]
        run(a.slot, r"Adding employee", "addemployee %s %s" % (kind, CODE))
        time.sleep(4)
        n = head(a.slot)
        ok = n is not None and n[0] == count + 1 and n[2] == places and n[1] == limit
        check("hire %d (%s)" % (i + 1, kind), ok, "before %s, after %s" % ((count, limit, places), n))
        if n:
            count = n[0]
    throws1, text = log_count(a.slot, THROW)
    added1, _ = log_count(a.slot, r"sweatshop has \d+ places for employees, \d+ of them added")
    check("no-throw", throws1 == throws0, "%d new" % (throws1 - throws0))
    check("no new places per hire", added1 == added0, "%d new 'of them added' line(s)" % (added1 - added0))

    if count < limit:
        rec0, _ = log_count(a.slot, r"threw an IndexOutOfRange; another mod changed the property")
        run(a.slot, r"\[Dev\] \[Idle\]", "awe idle break")
        run(a.slot, r"Adding employee", "addemployee chemist %s" % CODE)
        time.sleep(4)
        n = head(a.slot)
        rec1, text = log_count(a.slot, r"threw an IndexOutOfRange; another mod changed the property")
        throws2, _ = log_count(a.slot, THROW)
        synth = "(synthetic)" in text
        check("break-recovered", synth and n is not None and n[0] == count + 1 and n[2] == places and rec1 >= 1,
              "synthetic=%s recovered-lines %d->%d after %s" % (synth, rec0, rec1, n))
        check("break-no-error", throws2 == throws1, "%d new %s line(s)" % (throws2 - throws1, THROW))
    else:
        check("break-recovered", False, "no room for the synthetic hire (%d/%d)" % (count, limit))

    idle = run(a.slot, r"\[Dev\] \[Idle\] \d+ property", "awe idle")
    m = re.search(r"\[Dev\] \[Idle\] Sweatshop: points=(\d+) employees=(\d+)", idle)
    check("awe sees Based's places", m is not None and int(m.group(1)) == places, m.group(0) if m else idle[-300:])

    failed = [r for r in results if not r[1]]
    print("\n===== %d PASS, %d FAIL" % (len(results) - len(failed), len(failed)))
    for name, _, detail in failed:
        print("FAIL  %s  - %s" % (name, detail))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
