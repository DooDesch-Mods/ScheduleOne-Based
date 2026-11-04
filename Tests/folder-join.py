"""
Co-op join check for the project folder item (Based Runtime/ProjectFolder.cs). A folder hung on a wall is a network
object whose spawn data carries the item; a joining client reads that item while the host's world arrives. The client
must have the item registered by then, or the read throws and the join hangs on "synching".

Needs two running slots from `duo.ps1 -Build <Based worktree>`: the host in a loaded save, the client in the menu,
both with a Debug build of Based. The host hangs a folder on the wall nearest to its player through
`baseddiag folder wall`, then the client joins: by default it is restarted with LocalLobby's `--join`, which reads the
lobby id duo.ps1 shared; `--join browser` clicks Join in the vanilla lobby list instead. Then the client log is read.
Prints PASS or FAIL per check.

    python _wt-Based-folderjoin/Tests/folder-join.py --host mptest --client mptest2 [--join browser] [--no-hang]
"""
import argparse
import os
import re
import subprocess
import sys
import time


def _bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[folder-join] Workspace/tools/bridge/s1.py not found above " + os.path.dirname(os.path.abspath(__file__)))


sys.path.insert(0, _bridge())
from s1 import Game  # noqa: E402

REGISTERED = r"\[Based\].*Project folder item registered\."
HUNG = r"\[Based\].*Project folder hung at"
JOIN_START = r"Joining as client to:"
# A missing definition shows as the registry's warning, then as the null dereference in CreateInstanceAndRead.
MISSING = r"based_project_folder' not found in registry|CreateInstanceAndRead|ReadItemInstance"


class Log:
    def __init__(self, game):
        self.g = game
        path = game.log_path()
        self.path, self.offset = path, (os.path.getsize(path) if path else 0)

    def lines(self):
        now = self.g.log_path()
        if not now:
            return []
        offset = self.offset if now == self.path else 0
        with open(now, "rb") as handle:
            handle.seek(offset)
            return handle.read().decode("utf-8", errors="replace").splitlines()

    def first(self, pattern, lines=None):
        rx = re.compile(pattern)
        for i, line in enumerate(lines if lines is not None else self.lines()):
            if rx.search(line):
                return i, line
        return None, None

    def wait(self, pattern, timeout):
        end = time.time() + timeout
        while time.time() < end:
            i, line = self.first(pattern)
            if line:
                return line
            time.sleep(0.5)
        return None


class Run:
    def __init__(self, host, client):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.host, self.client = Game(host), Game(client)
        self.results = []

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        failed = [r for r in self.results if r[1] != "PASS"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(failed), len(failed)))
        for name, verdict, detail in failed:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if failed else 0

    def hang_on_host(self):
        log = Log(self.host)
        self.host.call("run_console_command", {"command": "baseddiag folder wall", "capture": False})
        line = log.wait(r"baseddiag: folder hung on the|baseddiag.*(Exception|failed)", 10)
        self.check("host hangs a folder on a wall", bool(line and "folder hung on the" in line), line or "no answer in 10 s")
        dressed = log.wait(HUNG, 5)
        self.check("host dresses the hung folder", bool(dressed), dressed or "no 'Project folder hung at' line")

    def join_button(self):
        found = self.client.call("ui_find", {"query": "Join"}).get("result") or {}
        for index, element in enumerate(found.get("elements") or []):
            text = " ".join(str(v) for v in element.values()) if isinstance(element, dict) else str(element)
            if re.search(r"\bJoin\b", text):
                return index, text
        return None, found

    # LocalLobby's own join: a client started with --join reads the lobby id from its UserData/LocalLobby/lobby.txt.
    # Works where the lobby list is not on screen, for example under Side Hustle's hub page.
    def join_locallobby(self, timeout):
        host_root, client_root = self.host.root, self.client.root
        host_file = os.path.join(host_root, "UserData", "LocalLobby", "lobby.txt")
        if not os.path.isfile(host_file):
            return self.check("host has a lobby id", False, "no " + host_file + " (start the pair with duo.ps1)")
        lobby = open(host_file, encoding="utf-8").read().strip()
        client_dir = os.path.join(client_root, "UserData", "LocalLobby")
        os.makedirs(client_dir, exist_ok=True)
        with open(os.path.join(client_dir, "lobby.txt"), "w", encoding="utf-8") as handle:
            handle.write(lobby)
        exe = os.path.join(client_root, "Schedule I.exe")
        stop = ("Get-CimInstance Win32_Process -Filter \"Name='Schedule I.exe'\" | Where-Object { $_.ExecutablePath -eq '%s' } "
                "| ForEach-Object { Stop-Process -Id $_.ProcessId -Force; 'stopped ' + $_.ProcessId }") % exe.replace("'", "''")
        stopped = subprocess.run(["pwsh", "-NoProfile", "-Command", stop], capture_output=True, text=True)
        print("      client restart: %s" % (stopped.stdout.strip() or "was not running"), flush=True)
        time.sleep(3)
        subprocess.Popen([exe, "--join"], cwd=client_root)
        print("      client launched with --join for lobby %s" % lobby, flush=True)
        ready, last = self.client.wait_ready(timeout=timeout, say=lambda s: None)
        return self.check("client finishes the join", ready, "stuck after %d s; last log line: %s" % (timeout, last) if not ready else "")

    def join(self, timeout):
        for attempt in range(4):
            self.client.call("run_console_command", {"command": "shbrowser", "capture": False})
            time.sleep(4)
            index, info = self.join_button()
            if index is not None:
                answer = self.client.call("ui_click", {"query": "Join", "index": index})
                print("      join clicked (attempt %d): %s" % (attempt + 1, answer.get("result") or answer.get("error")), flush=True)
                break
            print("      no Join button yet (attempt %d): %s" % (attempt + 1, info), flush=True)
            time.sleep(16)
        else:
            return self.check("client finds the host's lobby", False, "no Join button after 4 lobby lists")
        ready, last = self.client.wait_ready(timeout=timeout, say=lambda s: None)
        return self.check("client finishes the join", ready, "stuck after %d s; last log line: %s" % (timeout, last) if not ready else "")

    # A client the host's save does not know yet plays the intro, then opens the character creator. The folder may
    # arrive before that, so the creator only matters for the screenshot of a player standing in the world.
    def finish_creator(self, log, timeout=300):
        start = time.time()
        while time.time() - start < timeout:
            mark = Log(self.client)
            self.client.call("run_console_command", {"command": "baseddiag creator", "capture": False})
            line = mark.wait(r"baseddiag: creator (done|not open)|baseddiag.*(Exception|failed)", 10)
            if line and "creator done" in line:
                print("      intro and creator took %d s" % (time.time() - start), flush=True)
                break
            if line and "creator not open" not in line:
                print("      'baseddiag creator' failed: %s" % line, flush=True)
                break
            time.sleep(3)
        else:
            print("      no character creator opened in %d s" % timeout, flush=True)
        hung = log.wait(HUNG, 30)
        print("      %s" % (hung or "no folder on the client 30 s after the creator"), flush=True)

    def read_client(self, log):
        lines = log.lines()
        start, _ = log.first(JOIN_START, lines)
        if start is None:
            start = 0
        lines = lines[start:]
        reg, reg_line = log.first(REGISTERED, lines)
        miss, miss_line = log.first(MISSING, lines)
        hung, hung_line = log.first(HUNG, lines)
        self.check("client registers the folder item during the join", reg is not None, reg_line or "no registration line")
        self.check("client never reads the folder before it is registered", miss is None, miss_line or "")
        self.check("client dresses the host's hung folder", hung is not None, hung_line or "no 'Project folder hung at' line")
        if reg is not None and hung is not None:
            self.check("registration comes before the first folder read", reg < hung, "%s | %s" % (reg_line[:24], hung_line[:24]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="mptest")
    parser.add_argument("--client", default="mptest2")
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument("--join", choices=("locallobby", "browser"), default="locallobby",
                        help="locallobby restarts the client with --join; browser clicks Join in the vanilla lobby list")
    parser.add_argument("--no-hang", action="store_true", help="the host already has a folder on a wall")
    args = parser.parse_args()
    run = Run(args.host, args.client)
    if not args.no_hang:
        run.hang_on_host()
    log = Log(run.client)
    if args.join == "locallobby":
        run.join_locallobby(args.timeout)
    else:
        run.join(args.timeout)
    run.finish_creator(log)
    run.read_client(log)
    sys.exit(run.summary())


if __name__ == "__main__":
    main()
