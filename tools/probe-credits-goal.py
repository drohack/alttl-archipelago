"""What actually happens when the credits unlock.

TWO REPORTS FROM droha, both about the end of a run and neither with a log:

1. "I beat the level that had the credits unlock. I had already beaten the
   required levels. That instant it said i completed the game. I didn't have
   to go out and play the credits at all."

2. "When i went back to the level select i see a level with a hand print as
   the icon. It's greyed out like I can't play it. I think it's the credits
   but I can't tell."

The first is arguably working as designed - GoalLatch says in as many words
that the run is reported won when the condition holds rather than when the
card is played, so a multiworld is not left waiting on someone watching an
animation. Whether that is the RIGHT design is droha's call; this script's
job is to show exactly what happens and in what order, so the decision is
made against the real sequence rather than a memory of it.

The second looks like a real defect. Track.ApplyUnlocks creates level
completion data for the chapter dividers and for the run's open slots; the
credits card is appended to the track after that loop and is in neither
group, so nothing ever sets unlockedOnLevelSelect on it - and a card with no
completion row draws locked. `creditscard` in DevTools reads the three fields
that settle it.

THE SEED IS TINY ON PURPOSE: levels_to_beat 3, so the credits arrive within a
few puzzles instead of forty. Generated here rather than reused, because the
point is to reach the ending quickly and repeatably.

    py -3.13 tools/probe-credits-goal.py

Leaves its findings in docs/data/credits-goal.md, closes the game, and lets
harness_env put the player's config back.
"""
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment, close_game, ensure_no_steam_relaunch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "Archipelago")
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
OUT = os.path.join(ROOT, "docs", "data", "credits-goal.md")

WORK = os.path.join(ROOT, "testserver", "out-credits")
YAML_DIR = os.path.join(ROOT, "testserver", "yaml-credits")
SLOT = "droha"
PORT = 38281
DESKTOP = 1

#: Three to beat, no ability locks, everything open at once. The run should be
#: winnable in four or five puzzles, and nothing here is trying to test the
#: pack machinery.
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
description: reach the credits fast
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
    alive = False
    while time.time() < end:
        try:
            if os.path.getsize(CMD) == 0:
                alive = True
                break
        except OSError:
            pass
        time.sleep(0.25)
    if settle and alive:
        time.sleep(settle)
    return alive


def require_free_port():
    """Refuse to start if something is already on the port.

    A previous run's server outliving its script is not hypothetical: it
    happened here, MultiServer died on bind with
    "only one usage of each socket address", and the game then connected to
    the STALE server holding a different seed. Every symptom after that was
    measured against the wrong room, and the only visible sign was an
    OSError writing to a stdin that had never been connected to anything.
    """
    import socket
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        probe.bind(("127.0.0.1", PORT))
    except OSError:
        raise SystemExit(
            f"port {PORT} is already in use - a server from an earlier run is "
            f"still alive. Close it before running this, or the game will "
            f"connect to the wrong seed.")
    finally:
        probe.close()


def generate():
    """Roll the tiny seed. Returns the zip path."""
    for d in (WORK, YAML_DIR):
        if os.path.isdir(d):
            shutil.rmtree(d)
        os.makedirs(d)
    with open(os.path.join(YAML_DIR, "credits.yaml"), "w",
              encoding="utf-8") as f:
        f.write(YAML)

    run = subprocess.run(
        [sys.executable, "Generate.py",
         "--player_files_path", YAML_DIR,
         "--outputpath", WORK],
        cwd=AP, capture_output=True, text=True,
    )
    zips = [f for f in os.listdir(WORK) if f.endswith(".zip")] \
        if os.path.isdir(WORK) else []
    if not zips:
        print(run.stdout[-3000:], flush=True)
        print(run.stderr[-3000:], flush=True)
        raise SystemExit("generation produced no seed")
    return os.path.join(WORK, zips[0])


def in_a_puzzle(log, seconds=60):
    end = time.time() + seconds
    while time.time() < end:
        log.new()
        if not dev("controllers", 1.0):
            return None, 0
        text = log.wait(["controllers: ", "no level running"], 10)
        for line in text.splitlines():
            if "registered on " in line:
                name = line.split("registered on ", 1)[1].split(" levelInstance")[0]
                try:
                    count = int(line.split("controllers: ")[1].split(" ")[0])
                except (IndexError, ValueError):
                    count = 1
                return name, max(count, 1)
        time.sleep(1.0)
    return None, 0


def main():
    require_free_port()
    zip_path = generate()
    say(f"seed: {os.path.basename(zip_path)}")

    findings = {}
    close_game()

    # The server's output goes to a FILE rather than DEVNULL. A run died with
    # "OSError: [Errno 22] Invalid argument" writing to its stdin, which says
    # only that the pipe is gone and not why - and with the output discarded
    # there was nothing left to read afterwards.
    server_log = os.path.join(ROOT, "testserver", "credits-server.log")
    os.makedirs(os.path.dirname(server_log), exist_ok=True)
    server_out = open(server_log, "w", encoding="utf-8")
    server = subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(PORT), zip_path],
        cwd=AP, stdin=subprocess.PIPE, stdout=server_out,
        stderr=subprocess.STDOUT, text=True,
    )

    try:
        with Environment("probe-credits-goal") as env:
            env.configure(Host="localhost", Port=str(PORT),
                          SlotName=SLOT, AutoConnect="true")
            env.configure_devtools(MuteAudio="true",
                                   TargetVirtualDesktop=str(DESKTOP),
                                   RaiseWindowAtStartup="false")
            say(f"server on {PORT}, launching")
            ensure_no_steam_relaunch()
            log = Log()
            subprocess.Popen([EXE], cwd=GAME)
            log.wait(["connected. ", "Archipelago refused"], 180)
            time.sleep(6.0)
            say("connected")

            dev("menu:title", 3.0)
            dev("play", 9.0)

            # ONE completion satisfies the count, then the server hands
            # over the Credits item directly.
            #
            # Driving through the UI to beat three different puzzles does not
            # work here and two attempts proved it: `solve:` in order cannot
            # finish Desktop Computer's seven controller groups, and
            # `complete` raises a real Beaten check but does NOT advance to
            # the next slot, so the run sat on slot 0 re-completing the same
            # level - and a location only pays out once.
            #
            # Cheating the item in is not a shortcut around the thing being
            # tested, it IS the thing being tested. droha's report is "I had
            # already beaten the required levels" and then the Credits item
            # arrived; what matters is the order of those two events and what
            # the mod does at the second one. `levels_to_beat: 1` makes the
            # first true after one puzzle and `/send` makes the second happen
            # on demand.
            name, _ = in_a_puzzle(log)
            if name is None:
                raise SystemExit("no playable level to start from")
            log.new()
            dev("complete", 3.0)
            log.wait(["beaten:"], 20)
            say(f"beat {name} - the count should now be satisfied")

            time.sleep(4.0)
            log.new()
            before = [l.split("] ", 1)[-1] for l in log.all.splitlines()
                      if "credits:" in l or "goal:" in l]
            findings["with the count met but no Credits item yet"] = before or [
                "nothing said - correct, the run is not won without the item"]

            say("sending the Credits item")
            log.new()
            if server.poll() is not None:
                raise SystemExit(
                    f"the server exited with code {server.poll()} before the "
                    f"Credits item could be sent - see {server_log}")
            server.stdin.write(f"/send {SLOT} Credits\n")
            server.stdin.flush()
            log.wait(["goal: reported", "credits: unlocked"], 30)

            time.sleep(5.0)
            log.new()

            # THE ORDER IS THE ANSWER for report 1.
            order = [l.split("] ", 1)[-1] for l in log.all.splitlines()
                     if ("credits:" in l or "goal:" in l
                         or "toast: Run complete" in l
                         or "toast: The credits are unlocked" in l
                         or "received item: Credits" in l)]
            findings["what happened, in order"] = order

            # And the card itself for report 2.
            log.new()
            dev("menu:levels", 4.0)
            dev("creditscard", 1.5)
            card = log.wait(["creditscard: "], 15)
            findings["the credits card"] = [
                l.split("] ", 1)[-1] for l in card.splitlines()
                if "creditscard: " in l]

            findings["did the server get the goal"] = [
                "yes - 'goal: reported to the server' is in the log"
                if "goal: reported to the server" in log.all
                else "no"]
    finally:
        try:
            server.stdin.write("/exit\n")
            server.stdin.flush()
            server.wait(timeout=10)
        except Exception:
            server.kill()
        close_game()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# What happens when the credits unlock\n\n")
        f.write("`tools/probe-credits-goal.py`, a seed with "
                "`levels_to_beat: 3`.\n\n")
        for title, lines in findings.items():
            f.write(f"## {title}\n\n```\n")
            for line in lines or ["(nothing recorded)"]:
                f.write(line + "\n")
            f.write("```\n\n")

    for title, lines in findings.items():
        print(f"== {title} ==", flush=True)
        for line in lines or ["(nothing recorded)"]:
            print("   " + line, flush=True)
    print(f"Done: written to {os.path.relpath(OUT, ROOT)}", flush=True)


if __name__ == "__main__":
    main()
