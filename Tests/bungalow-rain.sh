# Rain under the bungalow's new roofs: inside the sunroom and under the carport the rain stops around the camera, above
# them it falls. Completes both upgrades and leaves heavy rain on. Prints PASS/FAIL per check.
# Usage: bash Tests/bungalow-rain.sh [slot]
SLOT="${1:-mptest2}"
cd "$(dirname "$0")/../.." || exit 1
S="python Workspace/tools/bridge/s1.py --slot $SLOT"
OUT="_drafts/based-bungalow/rain"
mkdir -p "$OUT"
fail=0
check() { if echo "$2" | grep -q "$3"; then echo "PASS $1"; else echo "FAIL $1 - $(echo "$2" | cut -c1-220)"; fail=1; fi; }
cover() { $S run "mxcover" 2>&1 | grep "weather cover:" | grep " at (" | tail -1; }

$S run "baseddiag site=bungalow phase complete" >/dev/null 2>&1
$S run "baseddiag site=bungalow phase complete sunroom" >/dev/null 2>&1
$S run "baseddiag site=bungalow phase complete carport" >/dev/null 2>&1
$S run "setweather heavyrain" >/dev/null 2>&1
$S time 1200 >/dev/null 2>&1
sleep 3

spot() {
  # name x y z: teleport, let the cover update, read it back.
  $S tp "$2" "$3" "$4" >/dev/null 2>&1
  sleep 3
  cover
}

line=$(spot sunroom -180 -3.6 114); echo "sunroom: $line"
check "sunroom: world state area" "$line" "in a world state area"
check "sunroom: under a roof" "$line" ", under a roof;"
check "sunroom: rain off around the camera" "$line" "camera effects off"
p=$($S shot 2>/dev/null | grep -o "[A-Z]:[^\"]*png" | tail -1); [ -n "$p" ] && cp "$p" "$OUT/rain-sunroom.png" && echo "$OUT/rain-sunroom.png"

line=$(spot sunroom-above -180 3 114); echo "above sunroom: $line"
check "above the sunroom: open sky" "$line" ", open sky;"
check "above the sunroom: rain on" "$line" "camera effects on"

line=$(spot carport -163.8 -3.6 107.5); echo "carport: $line"
check "carport: under a roof" "$line" ", under a roof;"
check "carport: rain off around the camera" "$line" "camera effects off"
p=$($S shot 2>/dev/null | grep -o "[A-Z]:[^\"]*png" | tail -1); [ -n "$p" ] && cp "$p" "$OUT/rain-carport.png" && echo "$OUT/rain-carport.png"

$S run "setweather clear" >/dev/null 2>&1
exit $fail
