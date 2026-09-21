"""Boot every slot back-to-back in ONE session, the way the gate does.

WHY THIS EXISTS, and it is the hole that let a gate failure through.

tools/probe-slots.py gives each level its own fresh game session, on
purpose: a level booted over another one inherits its leftovers. That
isolation is what makes its verdicts about the LEVEL. It also means it
cannot see anything that only goes wrong after a session has been used.

On 2026-09-21 the DLC gate could not force-solve DLC2 Pizza, DLC2 Books
Stacked, DLC1 Clock Cupboard, DLC1 Daggers or DLC1 Filing Cabinet, and
burned five Skips doing it. probe-slots had reported every one of those
slots fine. The difference between the two is not the level and not the
abilities - it is that the gate had already played three other puzzles
in that process.

So this changes exactly one variable against probe-slots: same seed,
same solve path, same verdicts, ONE launch. If a level solves here and
not in the gate, the difference is something else. If it solves in
probe-slots and fails here, the session is the variable and this is the
cheapest possible reproducer - three minutes against fifteen.

    ALTTL_SEED_DIR_NAME=out-dlc-open py -3.13 tools/probe-session.py

Use a locks-off seed (make-seed.py --dlc --quick --tag dlc-open) so
ability gating cannot confound the answer: every level must be
solvable, so anything that is not is the session.
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import Environment, close_game, ensure_no_steam_relaunch

OUT = os.path.join(e2e.REPO, "testserver",
                   os.environ.get("ALTTL_SEED_DIR_NAME", "out-dlc-open"))
CMDS = os.path.join(e2e.REPO, "testserver", "e2e-cmds.txt")
SERVER_LOG = os.path.join(e2e.REPO, "testserver", "probe-session-server.log")


def wipe():
    """Delete the randomizer's own save, never the player's campaign."""
    gone = 0
    for folder, suffix in ((OUT, ".apsave"), (e2e.SAVE_DIR, None)):
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            if (suffix and name.endswith(suffix)) or \
                    (suffix is None and name.startswith("save_ap_")):
                os.remove(os.path.join(folder, name))
                gone += 1
    return gone


def serve(seed):
    if e2e.port_open():
        return
    open(CMDS, "w", encoding="utf-8").close()
    log_file = open(SERVER_LOG, "w", encoding="utf-8")
    subprocess.Popen(
        f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
        f'--port {e2e.PORT} "{os.path.join(OUT, seed)}"',
        shell=True, cwd=e2e.AP, stdout=log_file, stderr=subprocess.STDOUT)
    for _ in range(40):
        if e2e.port_open():
            break
        time.sleep(1)
    if not e2e.port_open():
        sys.exit("FAIL: the server never bound the port")


def stop_server():
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Get-CimInstance Win32_Process -Filter \"Name like "
                    "'%python%'\" | Where-Object { $_.CommandLine -like "
                    "'*MultiServer*' } | ForEach-Object { Stop-Process "
                    "-Id $_.ProcessId -Force }"],
                   capture_output=True, text=True)
    for _ in range(10):
        if not e2e.port_open():
            return True
        time.sleep(1)
    return not e2e.port_open()


def attempt(log, index, name, n, total):
    """One level, in a session that has already been used."""
    opened, out = e2e.boot_level(log, index)
    if not opened:
        return "DID NOT OPEN", out

    done, chunk = e2e.solve_level(log)
    chunk += log.wait(["beaten:", "check:", "credits:"], 10, 6, "the check")

    if done and "beaten:" in chunk:
        return "beaten", chunk
    if e2e.EXHAUSTED_MARK in chunk:
        # THE GATE'S FAILURE, REPRODUCED. Everything solvable was solved
        # and the level still would not complete.
        return "CANNOT FORCE", chunk
    if e2e.GATED_MARK in chunk:
        return "gated, SOLVING UNTESTED", chunk
    if "check:" in chunk:
        return "partial", chunk
    return "NOTHING", chunk


def main():
    seeds = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not seeds:
        sys.exit(f"no seed in {os.path.relpath(OUT, e2e.REPO)} - make one "
                 f"with tools/make-seed.py --dlc --quick --tag dlc-open")
    seed = seeds[0]
    plan = e2e.read_plan(OUT, seed)
    slots = plan["slots"]

    close_game()
    stop_server()
    print(f"cleared {wipe()} saved-progress file(s)", flush=True)
    serve(seed)

    print(f"-- {len(slots)} slot(s) from {seed} "
          f"({os.path.relpath(OUT, e2e.REPO)}), ONE session --", flush=True)

    log = e2e.Log()
    log.before_launch()
    ensure_no_steam_relaunch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    if "connected. " not in log.wait(["connected. "], 150, 1, "the connection"):
        close_game()
        stop_server()
        sys.exit("FAILED TO CONNECT")
    time.sleep(3.0)

    verdicts = []
    last_done = False
    for n, (index, name) in enumerate(slots, 1):
        # UNWIND ONLY AFTER A SUCCESS, which is what play() does and why.
        # Taking a FAILED level back to the title deactivates it and
        # leaks a subscribed CheckWinCondition; play() skips the unwind
        # in that case on purpose. Unwinding unconditionally here would
        # make this probe tidier than the thing it is reproducing, and
        # the leak is a prime suspect for a session that degrades.
        if n > 1 and last_done:
            e2e.to_title(log)
        try:
            verdict, _out = attempt(log, index, name, n, len(slots))
        except Exception as exc:
            verdict = f"THREW: {exc}"
        print(f"[{n}/{len(slots)} slot {n - 1} {name}] {verdict}", flush=True)
        verdicts.append((n - 1, name, verdict))
        last_done = verdict == "beaten"

    close_game()
    print(f"server stopped: {stop_server()}", flush=True)
    print("", flush=True)
    for slot, name, verdict in verdicts:
        print(f"   slot {slot:>2} {name:34s} {verdict}", flush=True)

    bad = [v for v in verdicts
           if v[2] in ("CANNOT FORCE", "NOTHING", "DID NOT OPEN")
           or v[2].startswith("THREW")]
    print(f"Done: {len(verdicts)} slot(s) in one session, "
          f"{len(bad)} needing attention", flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    with Environment("probe-session") as _env:
        _env.configure(Host="localhost", Port=str(e2e.PORT),
                       SlotName=e2e.SLOT, AutoConnect="true")
        try:
            sys.exit(main())
        finally:
            stop_server()
