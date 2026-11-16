"""Client log after joining a host with every Storage Unit build: the client applies the same world states and bay 2."""
import re
import sys

lines = open(sys.argv[1], encoding="utf-8", errors="replace").read().splitlines()
fails = 0


def check(name, ok, detail):
    global fails
    fails += 0 if ok else 1
    print("%s  client: %s  - %s" % ("PASS" if ok else "FAIL", name, detail[-220:] if detail else "no matching line"), flush=True)


def last(pattern):
    return next((l for l in reversed(lines) if re.search(pattern, l)), "")


check("upper floor applied 'c-complete'", bool(last(r"world state: 'based_storage_upper' applied 'c-complete'")), last(r"world state: 'based_storage_upper' applied"))
check("bay applied 'complete'", bool(last(r"world state: 'based_storage_bay' applied 'complete'")), last(r"world state: 'based_storage_bay' applied"))
check("bay 2 appended as slot 1 with its GUID", bool(last(r"hall-bay-2' appended slot 1 on 'storageunit', GUID 84da8f54")), last(r"hall-bay-2'"))
check("bay 1 moved", bool(last(r"moves: 'based_storage_bay/complete/bay-1' to \(-7.20, 0.10, 102.35\)")), last(r"moves: 'based_storage_bay"))
check("loft grid built on the client", bool(last(r"storage-loft-floor' built grid \d+ of 'storageunit'")), last(r"storage-loft-floor"))
print("info  client: grid culled at build - %s" % (last(r"woke at the scene root")[-200:] or "no line, the parent was active"))
bad = [l for l in lines if re.search(r"based_storage.*failed|\[ERROR\].*(Based|MapExpansions)", l)]
check("no Based or MapExpansions error", not bad, bad[-1] if bad else "none")
print("===== client %s" % ("PASS" if fails == 0 else "%d FAIL" % fails))
sys.exit(1 if fails else 0)
