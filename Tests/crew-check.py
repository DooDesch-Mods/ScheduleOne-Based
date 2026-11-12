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
    # A phase change applies its world state for about a second; waiting for the baseddiag line keeps the bridge from
    # returning before the command has finished.
    code, out = s1(slot, "run", command, "--until", "baseddiag|worker diagnostics", "--max-wait", "20000")
    errors = [l for l in out.splitlines() if re.search(r"\[error\]|Exception", l, re.I)]
    # Unity skips the house's unreadable foundation mesh in the sunroom bake; that floor belongs to the world's NavMesh
    # (the state leaves it as existing floor), so the notice is printed and not counted.
    skipped = [l for l in errors if "does not allow read access" in l]
    if skipped:
        print(f"INFO {command}: {len(skipped)} Unity notices of unreadable bake sources, first: {skipped[0].strip()[:200]}")
    errors = [l for l in errors if l not in skipped]
    # s1.py exits with 2 when the command logged errors; those are judged above, line by line.
    ok = code in (0, 2) and "\n-- " in "\n" + out and "until NOT matched" not in out and not errors
    check("run " + command, ok, "" if ok else ("; ".join(errors[:3]) or out.strip()[-400:]))
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
    # The site has a lumberjack and a carpenter; only the one on the job is visible.
    workers = [w for w in wait_for_work(slot, "bungalow", 1, 1) if w.get("visible")]
    check("one worker on the carport", len(workers) == 1, f"{len(workers)} visible")
    for w in workers:
        check("the carport worker is the lumberjack", "lumberjack" in str(w.get("id", "")).lower(), str(w.get("id")))
        check("lumberjack follows the carport", w.get("upgrade") == "ray.bungalow-carport", str(w.get("upgrade")))
        check("lumberjack at a pine", w.get("atAnchor") is True, str(w.get("anchor")))
        check("lumberjack swings", w.get("hammering") is True)
        check("lumberjack without failure", not w.get("failure"), str(w.get("failure")))
    # Only one upgrade builds at a time; the carport has to stand before the next one is ordered.
    run(slot, "baseddiag site=bungalow phase complete carport")


def sunroom(slot):
    """The day the sunroom is ordered the woodcutter fells its pines; on the frames day the carpenter works at the west front."""
    run(slot, "baseddiag site=bungalow phase complete")
    for phase, role, worker_id in ((None, "woodcutter", "lumberjack"), ("sunroom-glass", "carpenter", "carpenter")):
        out = run(slot, "baseddiag site=bungalow phase " + (phase + " sunroom" if phase else "ordered sunroom"))
        check(f"sunroom {phase or 'ordered'}", "ray.bungalow-sunroom" in out, out.strip()[-300:])
        code, out = s1(slot, "time", "1000")
        check("time 1000", code == 0, out.strip()[-200:])
        workers = [w for w in wait_for_work(slot, "bungalow", 1, 1) if w.get("visible")]
        check(f"one worker on the sunroom {phase or 'ordered'}", len(workers) == 1, f"{len(workers)} visible")
        for w in workers:
            check(f"sunroom {role} is the {worker_id}", worker_id in str(w.get("id", "")).lower(), str(w.get("id")))
            check(f"sunroom {role} follows the sunroom", w.get("upgrade") == "ray.bungalow-sunroom", str(w.get("upgrade")))
            check(f"sunroom {role} at its anchor", w.get("atAnchor") is True, str(w.get("anchor")))
            check(f"sunroom {role} swings", w.get("hammering") is True)
            check(f"sunroom {role} without failure", not w.get("failure"), str(w.get("failure")))
    run(slot, "baseddiag site=bungalow phase complete sunroom")


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
        sunroom(a.slot)
    if a.only != "bungalow":
        ming(a.slot)
    print(f"{'FAIL' if FAILED else 'PASS'} crew-check: {len(FAILED)} failed")
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
