"""Co-op check for the sunroom's sliding door (Content/Bungalow/BungalowSunroomDoor.cs).

The door is a visual copy plus Based's own logic, so its open state travels over Based's NativeBus, not FishNet. The
host opens the door before the client joins; the client must find it open (late join). Then the client closes and
opens it, and the host and the client must both follow. Prints PASS/FAIL per check.

    pwsh -File Workspace/tools/session/duo.ps1 -Build _wt-Based-bungalow,_wt-MapExpansions-tree-rules
    python _wt-Based-bungalow/Tests/sunroom-door-coop.py --host mptest2 --client mptest4
"""
import argparse
import importlib.util
import os
import re
import subprocess
import sys
import time

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("folder_join", os.path.join(_here, "folder-join.py"))
folder_join = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(folder_join)
Log, Run = folder_join.Log, folder_join.Run

STATE = r"baseddiag: sunroom door (open|closed); (no leaf bound|leaf at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), progress ([\d.]+))"
OUT = os.path.normpath(os.path.join(_here, "..", "..", "_drafts", "based-bungalow", "sunroom"))


def command(game, text, pattern, timeout=10):
    log = Log(game)
    game.call("run_console_command", {"command": text, "capture": False})
    return log.wait(pattern, timeout)


def state(game, timeout=8):
    """Waits until the leaf has finished sliding, then returns (open, progress, z) or None."""
    end = time.time() + timeout
    last = None
    while time.time() < end:
        line = command(game, "baseddiag door state", STATE, 5)
        m = re.search(STATE, line or "")
        if m and m.group(3):
            last = (m.group(1) == "open", float(m.group(6)), float(m.group(5)))
            if last[1] in (0.0, 1.0) and last[0] == (last[1] == 1.0):
                return last
        time.sleep(1)
    return last


def shot(slot, name, eye, target):
    bridge = os.path.join(folder_join._bridge(), "s1.py")
    args = [sys.executable, bridge, "--slot", slot, "cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target),
            "--hud", "hide", "--settle", "20"]
    out = subprocess.run(args, capture_output=True, text=True).stdout
    m = re.search(r'"path":\s*"([^"]+)"', out)
    if not m:
        print("      shot %s failed: %s" % (name, out.strip()[-200:]), flush=True)
        return
    os.makedirs(OUT, exist_ok=True)
    target_path = os.path.join(OUT, name + ".png")
    with open(m.group(1).replace("\\\\", "\\"), "rb") as src, open(target_path, "wb") as dst:
        dst.write(src.read())
    print("      %s" % target_path, flush=True)


def finish_creator(client, timeout=300):
    start = time.time()
    while time.time() - start < timeout:
        line = command(client, "baseddiag creator", r"baseddiag: creator (done|not open)|baseddiag.*(Exception|failed)")
        if line and "creator done" in line:
            print("      intro and creator took %d s" % (time.time() - start), flush=True)
            return
        if line and "creator not open" not in line:
            print("      'baseddiag creator' failed: %s" % line, flush=True)
            return
        time.sleep(3)
    print("      no character creator opened in %d s" % timeout, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="mptest2")
    parser.add_argument("--client", default="mptest4")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--join", choices=("locallobby", "browser"), default="browser")
    parser.add_argument("--joined", action="store_true", help="the client is already in the host's world")
    args = parser.parse_args()
    run = Run(args.host, args.client)
    host, client = run.host, run.client

    command(host, "baseddiag site=bungalow phase complete", r"baseddiag", 10)
    command(host, "baseddiag site=bungalow phase complete sunroom", r"baseddiag", 10)
    time.sleep(6)
    command(host, "baseddiag door open", r"Sunroom door (opened|is now)", 10)
    h = state(host)
    run.check("host opens the door before the join", bool(h and h[0] and h[1] == 1.0), h)

    if not args.joined:
        client_log = Log(client)
        joined = run.join_locallobby(args.timeout) if args.join == "locallobby" else run.join(args.timeout)
        if not joined:
            sys.exit(run.summary())
        finish_creator(client)
        bound = client_log.wait(r"Sunroom door bound", 60)
        run.check("client binds its copy of the door", bool(bound), bound or "no 'Sunroom door bound' in 60 s")
        asked = client_log.wait(r"Sunroom door asked the host", 15)
        run.check("client asks the host for the door state", bool(asked), asked or "no query line")

    c = state(client, 15)
    run.check("late joiner finds the door open", bool(c and c[0] and c[1] == 1.0), c)
    if h and c:
        run.check("leaf stands at the same place on both", abs(h[2] - c[2]) < 0.02, "host z %.2f, client z %.2f" % (h[2], c[2]))

    host_log = Log(host)
    command(client, "baseddiag door close", r"asked the host to close", 10)
    by_peer = host_log.wait(r"Sunroom door closed by peer", 10)
    run.check("host takes the client's close", bool(by_peer), by_peer or "no 'closed by peer' on the host")
    h, c = state(host), state(client)
    run.check("door is closed on the host", bool(h and not h[0] and h[1] == 0.0), h)
    run.check("door is closed on the client", bool(c and not c[0] and c[1] == 0.0), c)
    shot(args.client, "coop-client-closed", (-179.5, -2.2, 113.5), (-183, -2.6, 111.6))

    host_log = Log(host)
    command(client, "baseddiag door open", r"asked the host to open", 10)
    by_peer = host_log.wait(r"Sunroom door opened by peer", 10)
    run.check("host takes the client's open", bool(by_peer), by_peer or "no 'opened by peer' on the host")
    h, c = state(host), state(client)
    run.check("door is open on the host", bool(h and h[0] and h[1] == 1.0), h)
    run.check("door is open on the client", bool(c and c[0] and c[1] == 1.0), c)
    shot(args.client, "coop-client-open", (-179.5, -2.2, 113.5), (-183, -2.6, 111.6))

    client_log = Log(client)
    command(host, "baseddiag door close", r"Sunroom door closed by the host", 10)
    seen = client_log.wait(r"Sunroom door is now closed", 10)
    run.check("client follows the host's close", bool(seen), seen or "no 'is now closed' on the client")
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
