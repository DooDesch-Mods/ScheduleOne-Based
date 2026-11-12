# Sunroom navigation and grid: after the room is complete the NavMesh bake joins its probes, an NPC path runs from the
# house's west room through the sliding door into the far corner and from the garden through the west door, and an item
# placed on the room's tiles stands inside it. Prints PASS/FAIL per check. Usage: bash Tests/sunroom-nav.sh [slot]
# The placed rack stays and blocks taking the room back (tiles occupied), so this runs last on a fresh save.
SLOT="${1:-mptest2}"
cd "$(dirname "$0")/../.." || exit 1
S="python Workspace/tools/bridge/s1.py --slot $SLOT"
fail=0
pass() { echo "PASS $1"; }
bad() { echo "FAIL $1 - $(echo "$2" | cut -c1-240)"; fail=1; }
check() { if echo "$2" | grep -qE "$3"; then pass "$1"; else bad "$1" "$2"; fi; }

$S run "baseddiag site=bungalow phase complete" >/dev/null 2>&1
$S run "baseddiag site=bungalow phase rights sunroom" >/dev/null 2>&1
sleep 3
out=$($S run "baseddiag site=bungalow phase complete sunroom" 2>&1)
sleep 8
log=$($S log -n 600 2>&1 | grep -iE "based_bungalow_sunroom" | grep -iE "probe|stitch|bake|navmesh" | tail -6)
echo "$log"
check "bake joins the sunroom probes" "$log" "probe"
echo "$log" | grep -qiE "not connected|unreachable|0/[1-9]" && bad "no unconnected probe" "$log" || pass "no unconnected probe"

line=$($S run "mxnav sample -180 -3.7 115" 2>&1 | grep "mxnav sample" | grep -E ") Humanoid:" | tail -1); echo "$line"
check "NavMesh under the sunroom floor" "$line" "\(0\.[0-4] m\)"

line=$($S run "mxnav path -173.5 -3.7 111 -182.3 -3.7 120.8" 2>&1 | grep "mxnav path" | tail -1); echo "$line"
check "path from the west room into the far corner" "$line" "ok=True status=PathComplete"

line=$($S run "mxnav path -188 -4 112 -179 -3.7 118" 2>&1 | grep "mxnav path" | tail -1); echo "$line"
check "path from the garden through the west door" "$line" "ok=True status=PathComplete"

line=$($S run "mxreach place bungalow dryingrack wing" 2>&1 | grep -i "mxreach place" | tail -1); echo "$line"
pos=$(echo "$line" | grep -oE "\(-?[0-9.]+, -?[0-9.]+, -?[0-9.]+\)" | tail -1)
python -c "
import sys
x,y,z=[float(v) for v in '$pos'.strip('()').split(',')]
sys.exit(0 if -183.05<x<-176.17 and 109.23<z<121.63 and abs(y+3.7)<.15 else 1)" 2>/dev/null \
  && pass "item on a sunroom tile stands inside the room at floor height $pos" || bad "item on a sunroom tile" "$line"
exit $fail
