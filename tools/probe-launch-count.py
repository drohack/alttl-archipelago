"""Does one harness launch produce exactly one game?

THE ASSERTION NO UNIT TEST CAN COVER. "two game launches, no more"
counts "A Little To The Left Archipelago loaded" across the whole
transcript and expects two - one for the arrow check, one for the run.
The HARNESS half is checkable by reading the source: there is exactly
one subprocess.Popen([EXE]) and it is called twice
(tools/test_scheduler.py, TestTheGateLaunchesTheGameTwice). That is not
the half that fails.

The other half is the GAME starting itself: Steam relaunching it, or a
restart after a crash. On the base gate of 2026-09-21 the count came
back THREE from two harness launches, with everything else green and
8 of 8 beaten. The DLC gate fifteen minutes earlier, on identical code,
counted two - which points at the environment rather than the code, and
this is the thing that says which.

It does exactly what the gate does - two launches, a close between -
and counts the loaded lines PER SESSION, which the gate cannot do
because Log.before_launch deletes the log each time and only the last
session survives.

    py -3.13 tools/probe-launch-count.py [--sessions 2]
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import Environment, close_game, ensure_no_steam_relaunch

LOADED = "A Little To The Left Archipelago loaded"

SESSIONS = 2
for i, a in enumerate(sys.argv):
    if a == "--sessions" and i + 1 < len(sys.argv):
        SESSIONS = int(sys.argv[i + 1])


def steam_guard_state():
    """Whether the file that stops Steam relaunching the game is there."""
    path = os.path.join(e2e.GAME, "steam_appid.txt")
    return os.path.isfile(path)


def main():
    print(f"steam_appid.txt present before anything: "
          f"{steam_guard_state()}", flush=True)

    # A LEFTOVER GAME IS THE OBVIOUS SUSPECT and has to be ruled out
    # first: a process still up from an earlier probe would log its own
    # loaded line into the very log this is about to read.
    close_game()
    time.sleep(2.0)
    running = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "(Get-Process -Name 'A Little To The Left' "
         "-ErrorAction SilentlyContinue | Measure-Object).Count"],
        capture_output=True, text=True).stdout.strip()
    print(f"game processes alive after close_game: {running}", flush=True)

    counts = []
    for session in range(1, SESSIONS + 1):
        log = e2e.Log()
        log.before_launch()
        wrote = ensure_no_steam_relaunch()
        ensure_no_steam_relaunch_note = (
            " (wrote steam_appid.txt)" if wrote else "")
        subprocess.Popen([e2e.EXE], cwd=e2e.GAME)

        text = log.wait([LOADED], 150, 3, f"session {session}")
        # Keep reading for a while AFTER the first loaded line: a
        # relaunch shows up seconds later, and a wait that returns on
        # the first match would never see the second.
        settle = time.time() + 25
        while time.time() < settle:
            text += log.new()
            time.sleep(2.0)

        seen = text.count(LOADED)
        counts.append(seen)
        print(f"session {session}: {seen} loaded line(s)"
              f"{ensure_no_steam_relaunch_note}", flush=True)
        close_game()
        time.sleep(3.0)

    total = sum(counts)
    print("", flush=True)
    print(f"per session: {counts}   total: {total}", flush=True)
    ok = all(c == 1 for c in counts)
    print(f"  {'PASS' if ok else 'FAIL'}  one harness launch, one game",
          flush=True)
    if not ok:
        print("  a session logged more than one load - the game restarted "
              "itself, which is what the gate's launch count catches",
              flush=True)
    print(f"Done: {total} load(s) from {SESSIONS} launch(es)", flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    with Environment("probe-launch-count") as _env:
        sys.exit(main())
