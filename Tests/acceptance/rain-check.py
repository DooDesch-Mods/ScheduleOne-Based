"""
Rain on the storey's open floor costs one night, once per build (wish 17). Forces rain with baseddiag, sleeps two
cycles and reads the log lines of each step.

    python _wt-Based-bungalow/Tests/acceptance/rain-check.py --slot mptest6
"""
import argparse
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Workspace", "tools", "bridge"))
from s1 import Game  # noqa: E402

UPGRADE = "ray.bungalow/ray.bungalow-storey"


def lines(game, command, until):
    # The bridge returns the lines it caught while the command ran; a line logged in the same frame as the sleep
    # can be missed there, so the log written meanwhile is read as well.
    path = game.log_path()
    start = os.path.getsize(path) if path else 0
    answer = game.run(command, until=until, max_wait_ms=60000)
    text = "\n".join(l["text"] for l in (answer.get("result") or {}).get("lines", []))
    if path:
        with open(path, "rb") as f:
            f.seek(start)
            text += "\n" + f.read().decode("utf-8", "replace")
    else:
        print("warn: no MelonLoader log found; only the bridge lines are checked")
    return text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slot", required=True)
    args = parser.parse_args()
    game = Game(args.slot)
    failed = 0

    def check(name, ok, detail):
        nonlocal failed
        print("%s %s%s" % ("PASS" if ok else "FAIL", name, "" if ok else " | " + detail[-400:].replace("\n", " ")))
        if not ok:
            failed += 1

    text = lines(game, "baseddiag site=bungalow phase storey:storey-floor", "construction moved to|Exception")
    check("enter storey-floor", "construction moved to" in text, text)
    try:
        text = lines(game, "baseddiag rain on", "baseddiag: rain " + re.escape(UPGRADE) + "|Exception")
        check("storey floor open to rain", "openToRain=True" in text and "delayed=False" in text, text)

        text = lines(game, "baseddiag advance", "delayed one night by rain|No rain delay|refused|Sleep skipped|Exception")
        check("first rainy night delays the build", "delayed one night by rain" in text, text)
        text = lines(game, "baseddiag rain state", "baseddiag: rain " + re.escape(UPGRADE) + "|Exception")
        check("build stays on the floor night", "delayed=True" in text and "cycles=1/" in text, text)

        text = lines(game, "baseddiag advance", "delayed one night by rain|No rain delay|refused|Sleep skipped|Exception")
        check("second rainy night does not delay again", "rain already cost this build a night" in text, text)
        text = lines(game, "baseddiag rain state", "baseddiag: rain " + re.escape(UPGRADE) + "|Exception")
        check("build moves on after the lost night", "cycles=2/" in text, text)
    finally:
        lines(game, "baseddiag rain off", "baseddiag: rain|Exception")
    print("rain-check: %d failed" % failed)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
