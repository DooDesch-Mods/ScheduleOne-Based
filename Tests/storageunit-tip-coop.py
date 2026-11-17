"""
Co-op check of a tip a client gives: it leaves the client's cash, goes to the host, counts once there and the
client hears back that it counted.

Needs a host and a client in the same game with Debug builds of MapExpansions and Based.

    python _wt-Based-storageunit/Tests/storageunit-tip-coop.py --host mptest2 --client mptest3
"""
import argparse
import re
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
SITE = "site=storageunit"
LOFT = "ray.storageunit-loft"
failed = 0


def run(slot, command, until, wait=60000):
    out = subprocess.run([sys.executable, S1, "--slot", slot, "run", "--max-wait", str(wait), "--until", until, command],
                         capture_output=True, text=True, timeout=wait / 1000 + 60, encoding="utf-8", errors="replace")
    return (out.stdout + out.stderr).replace('\\"', '"')


def log(slot, grep):
    out = subprocess.run([sys.executable, S1, "--slot", slot, "log", "--grep", grep, "-n", "400"],
                         capture_output=True, text=True, timeout=60, encoding="utf-8", errors="replace")
    return out.stdout


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""), flush=True)


def tips(slot):
    text = run(slot, "baseddiag %s tip state" % SITE, r"baseddiag: tips |failed")
    m = re.search(re.escape(LOFT) + r" stage=\w+ tips=(\d+)", text)
    return (int(m.group(1)) if m else None), text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")

    moved = run(a.host, "baseddiag %s phase %s:loft-door" % (SITE, LOFT), r"construction moved|failed", 90000)
    report("construction moved" in moved, "host: the loft build runs", "" if "construction moved" in moved else moved[-300:])
    run(a.host, "baseddiag %s tip set 0" % SITE, r"baseddiag: tips |failed")
    time.sleep(3)
    # The game's changecash runs only on the host.
    run(a.client, "baseddiag %s tip wallet 500" % SITE, r"baseddiag: wallet|failed")
    paid = run(a.client, "baseddiag %s tip cash 100" % SITE, r"goes to the host|failed")
    m = re.search(r"Tip (\S+) of \$100 \(\$100 cash\) for [\w.]+/" + re.escape(LOFT) + " goes to the host", paid)
    report(m is not None, "client: the $100 tip goes to the host", "" if m else paid[-300:])
    if not m:
        print("RESULT FAIL: %d failed" % failed)
        return 1
    tip_id = m.group(1)
    deadline, counted, answered = time.time() + 30, "", ""
    while time.time() < deadline and not (counted and answered):
        time.sleep(2)
        counted = counted or next((l.strip() for l in log(a.host, tip_id).splitlines() if "counted for" in l), "")
        answered = answered or next((l.strip() for l in log(a.client, tip_id).splitlines() if "counted by the host" in l), "")
    report(bool(counted), "host: the client's tip counts", counted[-240:] or "no counted line on the host")
    report(bool(answered), "client: the host's answer arrives", answered[-240:] or "no answer line on the client")
    again = [l for l in log(a.host, tip_id).splitlines() if "counted for" in l]
    report(len(again) == 1, "host: the tip counts once", "%d counted line(s)" % len(again))
    total, text = tips(a.host)
    report(total == 100, "host: the loft's tip total is $100", "tips=%s" % total if total is not None else text[-300:])
    client_total, text = tips(a.client)
    report(client_total == 100, "client: the snapshot shows $100", "tips=%s" % client_total if client_total is not None else text[-300:])
    print("RESULT %s: %d failed" % ("PASS" if failed == 0 else "FAIL", failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
