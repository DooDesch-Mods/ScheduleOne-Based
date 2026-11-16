"""The construction hold names the blocking items in quotes, joined the way the reader's language lists things.

On the host it puts the Storage Unit's south build under construction, names two and then three items as the hold and
reads the hold line and the order line in German and English; each is compared with the exact text and the open plan
is shot. With --client the client keeps its own language: it reads the host's names in English while the host reads
German. Prints PASS/FAIL per check and writes the plan shots to OUT.

usage: hold-text.py --host mptestA [--client mptestB] --out _drafts/based-storageunit/r4/hold-text
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import time

S1 = r"F:\Projects\Mods\Schedule1\Workspace\tools\bridge\s1.py"
TWO = ["Small Storage Rack", "Wall Clock"]
THREE = ["Small Storage Rack", "Wall Clock", "Medium Storage Rack"]
HALL_MIDDLE = ("-5", "0.1", "98.5", "0")

failed = 0


def expected(lang, names):
    quoted = ['"%s"' % n for n in names]
    joined = quoted[0] if len(quoted) == 1 else "%s %s %s" % (", ".join(quoted[:-1]), "und" if lang == "de" else "and", quoted[-1])
    if lang == "de":
        return "Baustopp: Räum %s von den roten Wänden weg." % joined, "Räum %s von den roten Wänden weg." % joined
    return "On hold: move %s off the red walls." % joined, "Move %s off the red walls." % joined


def s1(slot, *args, timeout=180):
    out = subprocess.run([sys.executable, S1, "--slot", slot, *args], capture_output=True, text=True, timeout=timeout,
                         encoding="utf-8", errors="replace")
    return out.returncode, (out.stdout + out.stderr).strip()


def run(slot, until, *command):
    return s1(slot, "run", "--until", until, *command)[1].replace('\\"', '"')


def report(ok, label, detail=""):
    global failed
    if not ok:
        failed += 1
    print("%s %s%s" % ("PASS" if ok else "FAIL", label, (": " + detail) if detail else ""), flush=True)


def held(slot, *names):
    """Runs baseddiag heldby (names set the hold, none only reports) and returns (language, hold line, order line)."""
    words = ["baseddiag", "site=storageunit", "heldby"] + (["|".join(names)] if names else [])
    text = run(slot, "baseddiag held:|rror", *words)
    m = re.search(r"baseddiag held: language (\w+); names '[^']*'; hold line '(.*?)'; order line '(.*?)'\.", text)
    if not m:
        report(False, "heldby on %s" % slot, text[-300:])
        return None
    return m.groups()


def language(slot, code):
    text = run(slot, "baseddiag: language set to|rror", "baseddiag", "lang", code)
    report("baseddiag: language set to " + code in text, "%s language %s" % (slot, code), text[-200:])


def shot(slot, out, name):
    code, text = s1(slot, "shot")
    m = re.search(r"([A-Z]:\\[^\"\r\n]+\.png)", text)
    path = m.group(1).replace("\\\\", "\\") if m else ""
    if code == 0 and os.path.isfile(path):
        shutil.copy(path, os.path.join(out, name + ".png"))
        report(True, "plan shot %s" % name)
    else:
        report(False, "plan shot %s" % name, text[-300:])


def open_plan(slot):
    for word, expect in (("close", "baseddiag: folder plan closed"), ("hold", "baseddiag: folder in hand"),
                         ("close", "baseddiag: folder plan closed"), ("open", "Project folder opened")):
        text = run(slot, expect + "|rror|not on the hotbar", "baseddiag", "site=storageunit", "folder", word)
        if word == "open":
            report(expect in text, "%s plan opened" % slot, text[-200:])
    time.sleep(1.5)


def check(slot, lang, names, label):
    got = held(slot, *names) if names else held(slot)
    if not got:
        return
    want_hold, want_order = expected(lang, names or THREE)
    report(got[0] == lang, "%s %s language" % (slot, label), got[0])
    report(got[1] == want_hold, "%s %s hold line" % (slot, label), got[1])
    report(got[2] == want_order, "%s %s order line" % (slot, label), got[2])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", default="")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    first = held(a.host)
    start_lang = first[0] if first else "en"

    s1(a.host, "time", "1200")
    text = run(a.host, "construction moved to|baseddiag: .*(fail|rror)", "baseddiag", "site=storageunit", "phase",
               "ray.storageunit-south:south-open")
    report("construction moved to" in text, "south build under construction", text[-200:])
    for slot in filter(None, (a.host, a.client)):
        s1(slot, "tp", *HALL_MIDDLE[:3], "--yaw", HALL_MIDDLE[3])
        text = run(slot, "baseddiag: folder given|rror", "baseddiag", "site=storageunit", "folder", "give")
        report("baseddiag: folder given" in text, "%s folder given" % slot, text[-200:])

    for lang in ("de", "en"):
        language(a.host, lang)
        for names, count in ((TWO, "2"), (THREE, "3")):
            check(a.host, lang, names, "%s %s items" % (lang, count))
            open_plan(a.host)
            shot(a.host, a.out, "host-%s-%s-items" % (lang, count))

    if a.client:
        # The host reads German, the client English, from the same hold the host sent.
        language(a.host, "de")
        check(a.host, "de", THREE, "de 3 items, client in English")
        language(a.client, "en")
        time.sleep(2)
        check(a.client, "en", None, "en 3 items from the host")
        open_plan(a.client)
        shot(a.client, a.out, "client-en-3-items")
        language(a.client, "de")
        check(a.client, "de", None, "de 3 items from the host")
        open_plan(a.client)
        shot(a.client, a.out, "client-de-3-items")

    for slot in filter(None, (a.host, a.client)):
        run(slot, "baseddiag: folder plan closed|rror", "baseddiag", "site=storageunit", "folder", "close")
    text = run(a.host, "baseddiag held:|rror", "baseddiag", "site=storageunit", "heldby", "off")
    report("names ''" in text, "hold cleared", text[-200:])
    language(a.host, start_lang)
    print("hold-text: %d failed" % failed, flush=True)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
