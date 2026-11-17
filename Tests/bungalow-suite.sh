# Every single-player Bungalow check in one run, in the order they need: the stage checks on the fresh save, crews, stage sheets, rain, sunroom navigation,
# then the storey function check, which leaves furniture upstairs and in the sunroom, and last the crews' trash, which would
# show in the earlier stages' sheets.
# Each script prints PASS/FAIL per check; this prints one summary line per script and exits 1 if any failed.
# Usage: bash Tests/bungalow-suite.sh [slot] [log folder]
SLOT="${1:-mptest}"
LOGS="${2:-_drafts/based-bungalow/suite}"
cd "$(dirname "$0")/../.." || exit 1
mkdir -p "$LOGS"
fail=0
step() {
  name="$1"; shift
  "$@" > "$LOGS/$name.log" 2>&1
  code=$?
  passes=$(grep -c "^PASS" "$LOGS/$name.log")
  fails=$(grep -c "^FAIL" "$LOGS/$name.log")
  if [ $code -eq 0 ]; then echo "PASS $name ($passes pass, $fails fail lines)"; else echo "FAIL $name exit $code ($passes pass, $fails fail lines)"; fail=1; fi
}
step checks python _wt-Based-bungalow/Tests/bungalow-checks.py --slot "$SLOT" --only grid,zfight,lines,switches
step crew python _wt-Based-bungalow/Tests/crew-check.py --slot "$SLOT" --only bungalow
step sheets python _wt-Based-bungalow/Tests/bungalow-sheets.py --slot "$SLOT"
step rain bash _wt-Based-bungalow/Tests/bungalow-rain.sh "$SLOT"
step sunroom-nav bash _wt-Based-bungalow/Tests/sunroom-nav.sh "$SLOT"
step storey-doors python _wt-Based-bungalow/Tests/storey-doors.py --slot "$SLOT"
step storey python _wt-Based-bungalow/Tests/storey-function.py --slot "$SLOT"
step litter python _wt-Based-bungalow/Tests/bungalow-checks.py --slot "$SLOT" --only litter
exit $fail
