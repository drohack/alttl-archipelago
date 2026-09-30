"""Watch the game while droha hand-tests: each useful log line as it arrives,
and a crash the moment the game dies.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/watch-handtest.py [--minutes 15] [--until REGEX]
        [--errors-only] [--wait-for-game SECONDS] 2>/dev/null

Run it with run_in_background before asking droha to play. Beside a probe
that prints its own progress, pass --errors-only (nothing is printed until a
crash, an error or the end, so the probe's lines stay the ones on screen) and
--wait-for-game 300 (start it before the probe launches the game). It exits,
and so wakes the session, on the first of:
  - the game closing or crashing (the Windows crash record is printed if any),
  - an Error, Fatal or Exception line from any plugin,
  - an exception from the GAME in Unity's Player.log, with the top of its
    stack (those never reach LogOutput.log),
  - a log line matching --until (e.g. 'LevelComplete +id=Fruit Stickers'),
    2 s after it, so the other solves of that moment print too (a level's last
    part and its completion land in the same second),
  - --minutes passing.
Every kept line is `[watch HH:MM:SS] mod|dev|game: text`; the last line starts
with `Done:` and says why it stopped.
"""
import argparse
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import GAME  # noqa: E402  (src/GameDir.props, not a literal)

LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
EXE = "A Little To The Left.exe"

#: Unity's own log. The GAME's exceptions land here and in the BepInEx
#: console, never in LogOutput.log - a NullReferenceException in
#: LevelInterface.CheckWinCondition went unseen until droha pasted it from the
#: console (2026-09-28: "please watch out for errors").
PLAYER_LOG = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                          "maxinferno", "A Little To The Left", "Player.log")
UNITY_ERROR = re.compile(r"Exception[:\s]")

KEEP = re.compile(r"^\[(Warning|Error|Fatal)|Exception|PartSolved|LevelComplete|checks:|"
                  r"retry panel:|abilities:|received item|trap|credits:|navigation:|beaten:|"
                  r"refus|daily guard|track:")
DROP = re.compile(r"repainted the camera|timeScale changed|OnApplicationFocus|game: Pause\(|"
                  r"keeps objects outside ManagedObjects|base flags only")
STOP = re.compile(r"^\[(Error|Fatal)|Exception")
SOURCES = (("[Info   :A Little To The Left Archipelago] ", "mod: "),
           ("[Info   :ALTTL Dev Tools] ", "dev: "))


def now():
    return time.strftime("%H:%M:%S")


def say(text):
    print(f"[watch {now()}] {text}", flush=True)


def game_running():
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {EXE}", "/NH"],
                         capture_output=True, text=True).stdout
    return EXE.lower() in out.lower()


def crash_record(since):
    """The Windows Application Error entry for the game since `since`, or ''."""
    ps = ("Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; "
          f"StartTime=[datetime]'{since}'}} -ErrorAction SilentlyContinue | "
          "Where-Object { $_.Message -like '*A Little To The Left*' } | Select-Object -First 1 | "
          "ForEach-Object { ($_.Message -split \"`n\" | "
          "Where-Object { $_ -match 'Exception code|Fault offset' }) -join ' ' }")
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                         capture_output=True, text=True).stdout
    return " ".join(out.split())


def short(line):
    for prefix, tag in SOURCES:
        if line.startswith(prefix):
            return tag + line[len(prefix):]
    return "game: " + line


def new_lines(path, pos):
    """(new position, complete new lines) of a growing log; a log rewritten
    by a new launch starts again from the top."""
    size = os.path.getsize(path) if os.path.exists(path) else 0
    if size < pos:
        pos = 0
    if size <= pos:
        return pos, []
    with open(path, "rb") as fh:
        fh.seek(pos)
        chunk = fh.read(size - pos)
    cut = chunk.rfind(b"\n")
    if cut < 0:
        return pos, []
    lines = [raw.decode("utf-8", "replace").rstrip("\r") for raw in chunk[:cut].split(b"\n")]
    return pos + cut + 1, lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--minutes", type=float, default=15)
    ap.add_argument("--until", default=None, help="stop when a log line matches this regex")
    ap.add_argument("--errors-only", action="store_true",
                    help="print only error lines and the end, beside a probe that reports progress")
    ap.add_argument("--wait-for-game", type=float, default=0,
                    help="seconds to wait for the game to start before giving up")
    args = ap.parse_args()
    until = re.compile(args.until) if args.until else None
    keep = STOP if args.errors_only else KEEP

    waited = time.time()
    while not game_running() and time.time() - waited < args.wait_for_game:
        time.sleep(2)

    since = time.strftime("%Y-%m-%d %H:%M:%S")
    start = time.time()
    pos = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    ppos = os.path.getsize(PLAYER_LOG) if os.path.exists(PLAYER_LOG) else 0
    unity = None  # (clock, deadline, lines) once a Unity exception is seen
    kept = 0
    checked = 0.0
    matched = None  # (clock, deadline) once --until matched
    if not args.errors_only:
        say(f"watching the log, Unity's Player.log and the game for {args.minutes:g} min"
            + (f", until /{args.until}/" if until else ""))
    if not game_running():
        print("Done: the game is not running", flush=True)
        return 1

    while True:
        pos, lines = new_lines(LOG, pos)
        for line in lines:
            if until and not matched and until.search(line):
                say(short(line)[:240])
                matched = (now(), time.time() + 2)
                continue
            if not line or DROP.search(line) or not keep.search(line):
                continue
            kept += 1
            say(short(line)[:240])
            if STOP.search(line):
                print(f"Done: stopped on an error line at {now()}, {kept} line(s) kept", flush=True)
                return 0

        # The game's own exceptions, with the top of their stack: the first
        # "at" lines follow the message, so give them a second to arrive.
        ppos, plines = new_lines(PLAYER_LOG, ppos)
        for line in plines:
            if unity is None and UNITY_ERROR.search(line):
                unity = (now(), time.time() + 1, [line])
            elif unity is not None and len(unity[2]) < 4 and line.strip().startswith("at "):
                unity[2].append(line.strip())
        if unity is not None and time.time() > unity[1]:
            for line in unity[2]:
                say("unity: " + line[:240])
            print(f"Done: stopped on a Unity exception at {unity[0]}, {kept} line(s) kept", flush=True)
            return 0

        if matched and time.time() > matched[1]:
            print(f"Done: matched /{args.until}/ at {matched[0]}, {kept} line(s) kept", flush=True)
            return 0

        if time.time() - checked > 2:
            checked = time.time()
            if not game_running():
                time.sleep(3)  # give Windows time to write the crash record
                record = crash_record(since)
                if record:
                    print(f"Done: CRASHED at {now()}: {record}", flush=True)
                else:
                    print(f"Done: the game closed at {now()}, no crash record", flush=True)
                return 0

        if time.time() - start > args.minutes * 60:
            print(f"Done: {args.minutes:g} min up, {kept} line(s) kept, the game is still running",
                  flush=True)
            return 0
        time.sleep(0.5)


if __name__ == "__main__":
    sys.exit(main())
