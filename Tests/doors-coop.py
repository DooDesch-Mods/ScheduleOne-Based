"""Co-op check for the Bungalow's copied sliding doors (Content/Bungalow/BungalowDoors.cs): the sunroom's and the upper storey's.

The door is a visual copy plus Based's own logic, so its open state travels over Based's NativeBus, not FishNet. The
host opens each door before the client joins; the client must find it open (late join). Then the client closes and
opens it, and the host and the client must both follow. Prints PASS/FAIL per check.

    pwsh -File Workspace/tools/session/duo.ps1 -Build _wt-Based-bungalow,_wt-MapExpansions-tree-rules
    python _wt-Based-bungalow/Tests/doors-coop.py --host mptest2 --client mptest4
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

STATE = r"baseddiag: %s (open|closed); (no leaf bound|leaf at \(([-\d.]+), ([-\d.]+), ([-\d.]+)\), progress ([\d.]+))"
# key: (console argument, log name, shot folder, shot eye, shot target, upgrade). The leaf's slide axis is z for both.
DOORS = {
    "sunroom": ("sunroom", "Sunroom door", "sunroom", (-179.5, -2.2, 113.5), (-183, -2.6, 111.6), "sunroom"),
    "storey": ("storey", "Upper storey door", "storey", (-172.5, 1.0, 113.5), (-176.2, 0.4, 111.4), "storey"),
}


def command(game, text, pattern, timeout=10):
    log = Log(game)
    game.call("run_console_command", {"command": text, "capture": False})
    return log.wait(pattern, timeout)


def state(game, door, timeout=8):
    """Waits until the leaf has finished sliding, then returns (open, progress, z) or None."""
    end = time.time() + timeout
    last = None
    while time.time() < end:
        pattern = STATE % door[1].lower()
        line = command(game, "baseddiag door %s state" % door[0], pattern, 5)
        m = re.search(pattern, line or "")
        if m and m.group(3):
            last = (m.group(1) == "open", float(m.group(6)), float(m.group(5)))
            if last[1] in (0.0, 1.0) and last[0] == (last[1] == 1.0):
                return last
        time.sleep(1)
    return last


def shot(slot, folder, name, eye, target):
    bridge = os.path.join(folder_join._bridge(), "s1.py")
    args = [sys.executable, bridge, "--slot", slot, "cam", "shot", "--eye", *map(str, eye), "--target", *map(str, target),
            "--hud", "hide", "--settle", "20"]
    out = subprocess.run(args, capture_output=True, text=True).stdout
    m = re.search(r'"path":\s*"([^"]+)"', out)
    if not m:
        print("      shot %s failed: %s" % (name, out.strip()[-200:]), flush=True)
        return
    out_dir = os.path.normpath(os.path.join(_here, "..", "..", "_drafts", "based-bungalow", folder))
    os.makedirs(out_dir, exist_ok=True)
    target_path = os.path.join(out_dir, name + ".png")
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
    parser.add_argument("--doors", default="sunroom,storey")
    args = parser.parse_args()
    run = Run(args.host, args.client)
    host, client = run.host, run.client

    doors = [DOORS[d] for d in args.doors.split(",") if d]
    # Right after the host reports ready, Based may still be setting up and the jump fails; it is repeated until it
    # takes.
    for text in ["baseddiag site=bungalow phase complete"] + ["baseddiag site=bungalow phase complete " + d[5] for d in doors]:
        line = None
        for attempt in range(12):
            line = command(host, text, r"baseddiag", 10)
            if line and "failed" not in line:
                break
            print("      '%s' not taken yet (attempt %d): %s" % (text, attempt + 1, (line or "no reply")[-160:]), flush=True)
            time.sleep(5)
        run.check("host: %s" % text, bool(line and "failed" not in line), line)
    time.sleep(6)
    opened = {}
    for door in doors:
        command(host, "baseddiag door %s open" % door[0], door[1] + r" (opened|is now)", 10)
        h = state(host, door)
        opened[door[0]] = h
        run.check("%s: host opens it before the join" % door[1], bool(h and h[0] and h[1] == 1.0), h)

    if not args.joined:
        client_log = Log(client)
        joined = run.join_locallobby(args.timeout) if args.join == "locallobby" else run.join(args.timeout)
        if not joined:
            sys.exit(run.summary())
        finish_creator(client)
        for door in doors:
            bound = client_log.wait(door[1] + " bound", 60)
            run.check("%s: client binds its copy" % door[1], bool(bound), bound or "no '%s bound' in 60 s" % door[1])
            asked = client_log.wait(door[1] + " asked the host", 15)
            run.check("%s: client asks the host for the state" % door[1], bool(asked), asked or "no query line")

    for door in doors:
        name = door[1]
        h = opened[door[0]]
        c = state(client, door, 15)
        run.check("%s: late joiner finds it open" % name, bool(c and c[0] and c[1] == 1.0), c)
        if h and c:
            run.check("%s: leaf stands at the same place on both" % name, abs(h[2] - c[2]) < 0.02,
                      "host z %.2f, client z %.2f" % (h[2], c[2]))

        host_log = Log(host)
        command(client, "baseddiag door %s close" % door[0], r"asked the host to close", 10)
        by_peer = host_log.wait(name + " closed by peer", 10)
        run.check("%s: host takes the client's close" % name, bool(by_peer), by_peer or "no 'closed by peer' on the host")
        h, c = state(host, door), state(client, door)
        run.check("%s: closed on the host" % name, bool(h and not h[0] and h[1] == 0.0), h)
        run.check("%s: closed on the client" % name, bool(c and not c[0] and c[1] == 0.0), c)
        shot(args.client, door[2], "coop-client-closed", door[3], door[4])

        host_log = Log(host)
        command(client, "baseddiag door %s open" % door[0], r"asked the host to open", 10)
        by_peer = host_log.wait(name + " opened by peer", 10)
        run.check("%s: host takes the client's open" % name, bool(by_peer), by_peer or "no 'opened by peer' on the host")
        h, c = state(host, door), state(client, door)
        run.check("%s: open on the host" % name, bool(h and h[0] and h[1] == 1.0), h)
        run.check("%s: open on the client" % name, bool(c and c[0] and c[1] == 1.0), c)
        shot(args.client, door[2], "coop-client-open", door[3], door[4])

        client_log = Log(client)
        command(host, "baseddiag door %s close" % door[0], name + " closed by the host", 10)
        seen = client_log.wait(name + " is now closed", 10)
        run.check("%s: client follows the host's close" % name, bool(seen), seen or "no 'is now closed' on the client")
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
