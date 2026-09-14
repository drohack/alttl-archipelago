"""Set up a run that is one puzzle away from the credits, and HAND IT OVER.

Every other harness here closes the game and puts the config back. This one
deliberately does not: droha asked to see the ending behaviour first hand -
"can you set it up so that i have a just 1 level to complete to unlock the
credits, and i have all level requirements beaten met. i want to see this for
myself." So it leaves the server up, the game running, and the save in a state
where exactly one puzzle stands between the player and the finale.

WHAT IT LEAVES BEHIND

  - a fresh seed: 8 puzzles, all open at once, no ability locks,
    levels_to_beat 1
  - one puzzle already beaten, so the beaten requirement is MET
  - the Credits item still out there, on a known puzzle
  - the game open on the level select, waiting

It prints which puzzle to play. Beating that one grants the Credits item,
which opens the credits card behind its own chapter break - and the run is NOT
reported won until that card is actually played.

WHERE THE CREDITS ITEM IS, without guessing: read out of the seed's own
spoiler, which generation writes inside the zip. Asking the server with
`/hint` would also work, but its reply is rendered from JSON message parts and
matching that text is guesswork.

CLEANING UP AFTERWARDS - this is the part that matters, because nothing here
does it for you:

    py -3.13 tools/harness_env.py --restore-latest

That puts the Archipelago host, port, slot name and the display settings back
the way they were. Until it is run, the mod is pointed at localhost.
"""
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harness_env
from harness_env import close_game, ensure_no_steam_relaunch, take_snapshot, set_config

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "Archipelago")
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")

WORK = os.path.join(ROOT, "testserver", "out-demo")
YAML_DIR = os.path.join(ROOT, "testserver", "yaml-demo")
SERVER_LOG = os.path.join(ROOT, "testserver", "demo-server.log")
SLOT = "droha"
PORT = 38281

YAML = f"""name: {SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 8
  levels_to_beat: 1
  ability_locks: false
  pack_size: 8
  skip_count: 0
description: one puzzle from the credits
"""


def say(what):
    print(f"-- {what}", flush=True)


class Log:
    def __init__(self):
        self.pos = os.path.getsize(LOG) if os.path.exists(LOG) else 0
        self.all = ""

    def new(self):
        try:
            size = os.path.getsize(LOG)
        except OSError:
            return ""
        if size < self.pos:
            self.pos = 0
        if size == self.pos:
            return ""
        with open(LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(self.pos)
            text = f.read()
        self.pos = os.path.getsize(LOG)
        self.all += text
        return text

    def wait(self, needles, seconds):
        end = time.time() + seconds
        text = ""
        while time.time() < end:
            text += self.new()
            if any(n in text for n in needles):
                return text
            time.sleep(0.3)
        return text


def dev(cmd, settle=0.0, seconds=60.0):
    with open(CMD, "w", encoding="utf-8") as f:
        f.write(cmd)
    end = time.time() + seconds
    ok = False
    while time.time() < end:
        try:
            if os.path.getsize(CMD) == 0:
                ok = True
                break
        except OSError:
            pass
        time.sleep(0.25)
    if settle and ok:
        time.sleep(settle)
    return ok


def require_free_port():
    import socket
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        probe.bind(("127.0.0.1", PORT))
    except OSError:
        raise SystemExit(
            f"port {PORT} is busy - an older server is still running. Close it "
            f"first, or the game will connect to the wrong seed.")
    finally:
        probe.close()


def generate():
    for d in (WORK, YAML_DIR):
        if os.path.isdir(d):
            shutil.rmtree(d)
        os.makedirs(d)
    with open(os.path.join(YAML_DIR, "demo.yaml"), "w", encoding="utf-8") as f:
        f.write(YAML)

    run = subprocess.run(
        [sys.executable, "Generate.py",
         "--player_files_path", YAML_DIR, "--outputpath", WORK],
        cwd=AP, capture_output=True, text=True,
    )
    zips = [f for f in os.listdir(WORK) if f.endswith(".zip")] \
        if os.path.isdir(WORK) else []
    if not zips:
        print(run.stdout[-2000:], flush=True)
        print(run.stderr[-2000:], flush=True)
        raise SystemExit("generation produced no seed")
    return os.path.join(WORK, zips[0])


def in_a_puzzle(log, seconds=60):
    end = time.time() + seconds
    while time.time() < end:
        log.new()
        if not dev("controllers", 1.0):
            return None
        text = log.wait(["controllers: ", "no level running"], 10)
        for line in text.splitlines():
            if "registered on " in line:
                return line.split("registered on ", 1)[1].split(" levelInstance")[0]
        time.sleep(1.0)
    return None


def credits_level(zip_path):
    """Which puzzle holds the Credits item, read from the seed's own spoiler.

    The spoiler ships INSIDE the zip - generation writes AP_<seed>_Spoiler.txt
    into the archive rather than beside it - and its location lines read
    "<location>: <item>". So "Desktop Computer - Keys: Credits" means the
    Credits item sits on the Desktop Computer puzzle.

    Read from the file rather than asked of the server. `/hint` would also
    answer, but the server renders its reply from JSON message parts and
    matching that text is guesswork; the spoiler is a fact on disk.
    """
    import zipfile
    try:
        with zipfile.ZipFile(zip_path) as z:
            name = next((n for n in z.namelist() if n.endswith("_Spoiler.txt")),
                        None)
            if name is None:
                return None, None
            text = z.read(name).decode("utf-8", "replace")
    except Exception as e:
        print(f"-- could not read the spoiler: {e}", flush=True)
        return None, None

    for line in text.splitlines():
        # Indented lines belong to the playthrough section, which repeats
        # placements and would match a second time.
        if line.startswith(" ") or not line.rstrip().endswith(": Credits"):
            continue
        location = line.rstrip()[: -len(": Credits")].strip()
        return location.split(" - ")[0].strip(), location

    return None, None


def main():
    require_free_port()
    close_game()

    zip_path = generate()
    say(f"seed: {os.path.basename(zip_path)}")

    # A snapshot, but NO restore - this script hands the machine over on
    # purpose. The tail of this output says how to undo it.
    snap = take_snapshot("credits-demo")
    set_config("droha.alttl.devtools.cfg", {"MuteAudio": "true"})
    set_config("droha.alttl.archipelago.cfg", {
        "Host": "localhost", "Port": str(PORT),
        "SlotName": SLOT, "AutoConnect": "true",
    })

    os.makedirs(os.path.dirname(SERVER_LOG), exist_ok=True)
    server_out = open(SERVER_LOG, "w", encoding="utf-8")
    flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    server = subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(PORT), zip_path],
        cwd=AP, stdin=subprocess.PIPE, stdout=server_out,
        stderr=subprocess.STDOUT, text=True, creationflags=flags,
    )
    say(f"server up on {PORT} (pid {server.pid}), launching the game")

    ensure_no_steam_relaunch()
    log = Log()
    subprocess.Popen([EXE], cwd=GAME)
    log.wait(["connected. ", "Archipelago refused"], 180)
    time.sleep(6.0)
    say("connected")

    target, location = credits_level(zip_path)
    if target is None:
        say("WARNING: the spoiler did not say where the Credits item is")
    else:
        say(f"the Credits item is on {target} (location: {location})")

    # Satisfy the beaten requirement with one puzzle, and not the one holding
    # the Credits item - beating that would unlock the credits immediately and
    # there would be nothing left for droha to do.
    dev("menu:title", 3.0)
    dev("play", 9.0)

    beaten = None
    for _ in range(6):
        here = in_a_puzzle(log)
        if here is None:
            break
        if target and here.split(" #")[0].strip() == target:
            say(f"{here} is the one holding the Credits item - leaving it alone")
            dev("menu:title", 3.0)
            dev("play", 9.0)
            continue
        log.new()
        dev("complete", 3.0)
        if "beaten:" in log.wait(["beaten:"], 20):
            beaten = here
            break

    if beaten is None:
        say("WARNING: could not pre-beat a puzzle; the requirement is NOT met")
    else:
        say(f"beat {beaten} - the beaten requirement is now met")

    # Park on the level select, which is where the player picks up.
    dev("menu:levels", 3.0)

    held = "credits: unlocked" in log.all
    print("", flush=True)
    print("=" * 66, flush=True)
    print("  READY - the game is yours, leave it open", flush=True)
    print("=" * 66, flush=True)
    print(f"  seed            {os.path.basename(zip_path)}", flush=True)
    print(f"  beaten needed   1, and {beaten or 'NOTHING'} is done", flush=True)
    print(f"  PLAY THIS       {target or 'unknown'}", flush=True)
    print(f"  already open    {'YES - something went wrong' if held else 'no'}",
          flush=True)
    print("", flush=True)
    print("  1. play the puzzle named above - it holds the Credits item", flush=True)
    print("  2. the toast should say the credits are unlocked and to play them,", flush=True)
    print("     and the run should NOT be reported won yet", flush=True)
    print("  3. the credits card appears after its own chapter break, lit, and", flush=True)
    print("     clickable - it used to be greyed out", flush=True)
    print("  4. play it. THAT is what reports the run to the server.", flush=True)
    print("", flush=True)
    print("  when you are done, put your settings back with:", flush=True)
    print("    py -3.13 tools/harness_env.py --restore-latest", flush=True)
    print(f"  and stop the server (pid {server.pid}).", flush=True)
    print(f"  snapshot: {os.path.basename(snap)}", flush=True)
    print("=" * 66, flush=True)


if __name__ == "__main__":
    main()
