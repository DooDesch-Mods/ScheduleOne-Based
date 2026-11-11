"""Checks both construction crews in a running test slot and prints PASS/FAIL per check.

python Tests/crew-check.py --slot mptest2 [--only bungalow|ming]
Bungalow: the carport ordered at 10:00 brings the lumberjack to the pines with the machete.
Ming: the frame phase at 10:00 brings the three Sweatshop workers, as before the crew refactor.
"""
import argparse
import json
import pathlib
import re
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
S1 = ROOT / "Workspace" / "tools" / "bridge" / "s1.py"
FAILED = []


def s1(slot, *args):
    proc = subprocess.run([sys.executable, str(S1), "--slot", slot, *args], capture_output=True, text=True)
    return proc.returncode, proc.stdout + proc.stderr


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))
    if not ok:
        FAILED.append(name)


def run(slot, command):
    code, out = s1(slot, "run", command)
    check("run " + command, code == 0 and "0 errors" in out, out.strip()[-400:] if code != 0 or "0 errors" not in out else "")
    return out


def diagnostics(slot, site):
    out = run(slot, f"baseddiag site={site} workers")
    workers = []
    for line in out.splitlines():
        m = re.search(r"worker diagnostics: (\{.*\})", line)
        if m:
            workers.append(json.loads(m.group(1)))
    return out, workers


def wait_for_work(slot, site, expected, want_tool, seconds=60):
    """Polls until every expected worker is visible at its anchor and enough of them swing."""
    deadline = time.time() + seconds
    workers = []
    while time.time() < deadline:
        _, workers = diagnostics(slot, site)
        visible = [w for w in workers if w.get("visible")]
        swinging = [w for w in workers if w.get("hammering")]
        if len(visible) == expected and len(swinging) >= want_tool:
            return workers
        time.sleep(5)
    return workers


def bungalow(slot):
    run(slot, "baseddiag site=bungalow phase complete")
    out = run(slot, "baseddiag site=bungalow phase ordered carport")
    check("carport ordered", "ray.bungalow-carport" in out, out.strip()[-300:])
    code, out = s1(slot, "time", "1000")
    check("time 1000", code == 0, out.strip()[-200:])
    workers = wait_for_work(slot, "bungalow", 1, 1)
    check("lumberjack attached", len(workers) == 1, f"{len(workers)} workers")
    for w in workers:
        check("lumberjack visible", w.get("visible") is True)
        check("lumberjack follows the carport", w.get("upgrade") == "ray.bungalow-carport", str(w.get("upgrade")))
        check("lumberjack at a pine", w.get("atAnchor") is True, str(w.get("anchor")))
        check("lumberjack swings", w.get("hammering") is True)
        check("lumberjack without failure", not w.get("failure"), str(w.get("failure")))


def ming(slot):
    run(slot, "baseddiag site=sweatshop phase frame")
    code, out = s1(slot, "time", "1000")
    check("time 1000", code == 0, out.strip()[-200:])
    workers = wait_for_work(slot, "sweatshop", 3, 2)
    check("three Sweatshop workers attached", len(workers) == 3, f"{len(workers)} workers")
    check("three Sweatshop workers visible", sum(1 for w in workers if w.get("visible")) == 3)
    check("carpenter and fitter swing", sum(1 for w in workers if w.get("hammering")) >= 2,
          ", ".join(f'{w["id"]}={w.get("hammering")}' for w in workers))
    for w in workers:
        check(w["id"] + " without failure", not w.get("failure"), str(w.get("failure")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default="mptest2")
    ap.add_argument("--only", choices=["bungalow", "ming"])
    a = ap.parse_args()
    if a.only != "ming":
        bungalow(a.slot)
    if a.only != "bungalow":
        ming(a.slot)
    print(f"{'FAIL' if FAILED else 'PASS'} crew-check: {len(FAILED)} failed")
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
