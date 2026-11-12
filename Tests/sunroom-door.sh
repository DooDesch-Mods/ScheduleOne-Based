# Sunroom sliding door on one slot: builds the room, then checks the door closed and open as the player meets it.
# Prints PASS/FAIL per check. Usage: bash Tests/sunroom-door.sh [slot]
SLOT="${1:-mptest2}"
cd "$(dirname "$0")/../.." || exit 1
S="python Workspace/tools/bridge/s1.py --slot $SLOT"
OUT="_drafts/based-bungalow/sunroom"
mkdir -p "$OUT"
fail=0
check() { if [ "$1" = 0 ]; then echo "PASS $2"; else echo "FAIL $2"; fail=1; fi; }
x_of() { $S where 2>/dev/null | python -c "import json,sys;t=sys.stdin.read();d=json.loads(t[t.index('{'):]);p=d.get('position') or d.get('pos');print(p[0])"; }
walk_west() {
  $S tp -181.6 -3.6 111.9 --yaw 270 >/dev/null 2>&1
  $S run "mxwalk 270 3" >/dev/null 2>&1
  sleep 4
  x_of
}
shot() { $S cam shot --eye "$2" "$3" "$4" --target "$5" "$6" "$7" --hud hide --settle 20 2>/dev/null | python -c "import json,sys,shutil;t=sys.stdin.read();p=json.loads(t[t.index('{'):])['path'];shutil.copyfile(p,'$OUT/'+'$1'+'.png');print('$OUT/$1.png')"; }

$S run "baseddiag site=bungalow phase complete" >/dev/null 2>&1
$S run "baseddiag site=bungalow phase complete sunroom" >/dev/null 2>&1
$S time 1200 >/dev/null 2>&1
sleep 6
state=$($S run "baseddiag door state" 2>&1 | grep "sunroom door" | tail -1)
echo "$state"
echo "$state" | grep -q "leaf at"; check $? "door leaf bound"

$S run "baseddiag door close" >/dev/null 2>&1; sleep 4
x=$(walk_west); echo "closed: player stopped at x=$x"
python -c "import sys;sys.exit(0 if float('$x') > -182.95 else 1)"; check $? "closed door blocks the player (x > -182.95)"
shot door-closed-outside -187 -2.2 111.9 -183 -2.6 111.6
shot door-closed-inside -179.5 -2.2 113.5 -183 -2.6 111.6

$S run "baseddiag door open" >/dev/null 2>&1; sleep 5
state=$($S run "baseddiag door state" 2>&1 | grep "sunroom door" | tail -1); echo "$state"
echo "$state" | grep -q "^.*sunroom door open; leaf at.*progress 1.00"; check $? "door reports open, leaf at the end"
x=$(walk_west); echo "open: player reached x=$x"
python -c "import sys;sys.exit(0 if float('$x') < -183.6 else 1)"; check $? "open door lets the player out (x < -183.6)"
shot door-open-outside -187 -2.2 111.9 -183 -2.6 111.6
shot door-open-inside -179.5 -2.2 113.5 -183 -2.6 111.6

# The prompt the player sees in front of the door, with the HUD on.
$S run "baseddiag door close" >/dev/null 2>&1; sleep 4
$S tp -182.3 -3.6 111.95 --yaw 270 >/dev/null 2>&1; sleep 2
p=$($S shot 2>/dev/null | grep -o "[A-Z]:[^\"]*png" | tail -1); [ -n "$p" ] && cp "$p" "$OUT/door-prompt.png" && echo "$OUT/door-prompt.png"
$S log -n 300 2>&1 | grep -i "sunroom door" | tail -8
exit $fail
