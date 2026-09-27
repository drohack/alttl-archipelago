"""Watch the game while droha hand-tests: each useful log line as it arrives,
and a crash the moment the game dies.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/watch-handtest.py [--minutes 15] [--until REGEX] > watch.out 2>/dev/null

Run it with run_in_background before asking droha to play. It exits, and so
wakes the session, on the first of:
  - the game closing or crashing (the Windows crash record is printed if any),
  - an Error, Fatal or Exception line from any plugin,
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

GAME = r"G:\Games\Steam\steamapps\common\A Little To The Left"
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
EXE = "A Little To The Left.exe"

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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--minutes", type=float, default=15)
    ap.add_argument("--until", default=None, help="stop when a log line matches this regex")
    args = ap.parse_args()
    until = re.compile(args.until) if args.until else None

    since = time.strftime("%Y-%m-%d %H:%M:%S")
    start = time.time()
    pos = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    kept = 0
    checked = 0.0
    matched = None  # (clock, deadline) once --until matched
    say(f"watching the log and the game for {args.minutes:g} min"
        + (f", until /{args.until}/" if until else ""))
    if not game_running():
        print("Done: the game is not running", flush=True)
        return 1

    while True:
        size = os.path.getsize(LOG) if os.path.exists(LOG) else 0
        if size < pos:
            pos = 0  # a new launch rewrote the log
        if size > pos:
            with open(LOG, "rb") as fh:
                fh.seek(pos)
                chunk = fh.read(size - pos)
            cut = chunk.rfind(b"\n")
            if cut >= 0:
                pos += cut + 1
                for raw in chunk[:cut].split(b"\n"):
                    line = raw.decode("utf-8", "replace").rstrip("\r")
                    if until and not matched and until.search(line):
                        say(short(line)[:240])
                        matched = (now(), time.time() + 2)
                        continue
                    if not line or DROP.search(line) or not KEEP.search(line):
                        continue
                    kept += 1
                    say(short(line)[:240])
                    if STOP.search(line):
                        print(f"Done: stopped on an error line at {now()}, {kept} line(s) kept", flush=True)
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
