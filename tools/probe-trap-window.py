"""Measure what the cat trap's load guard and the background poll actually see.

Two fixes went in on reasoning alone and droha asked the right question about
both: "does the cat trap hold actually work at the end of the level when it
fires again? did you test these on an actual game?" This is the test.

WHAT IS BEING MEASURED

1. `LevelIsLoaded` and `IsTransitioning` outside a puzzle. The trap now HOLDS
   itself rather than spending while a level is mid-load. If those flags also
   read "not loaded" on the level select or the post-level screen, the hold
   never releases and a trap is stranded forever - which is worse than the
   freeze it replaced, because nothing logs it.

2. Whether the game repaints `Camera.main.backgroundColor` after a level has
   settled, or only during setup. `Backgrounds.Tick` writes it on every
   differing frame on the grounds that two one-shot writes lost to a later
   paint. If the paint only happens at setup, the poll spends the rest of the
   puzzle doing nothing and should stop.

3. The freeze itself: a trap landing squarely in the load window. The run
   completes a level with a trap already owed, so the trap ticks while the
   next level loads - the exact sequence in droha's 0.3.1 log.

HOW THE TRAP IS TIMED. Not by luck. The server holds the trap back until the
completion has been sent, so it arrives while the game is between levels. The
old code would call ResetLevel on a half-built level there; the new code
should log a hold and then resolve on the far side.

    py -3.13 tools/probe-trap-window.py [path/to/seed.zip]

It prints a verdict and leaves the full watch output in
docs/data/trap-window.log for reading.
"""
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import (EXE, Environment, GAME, close_game,
                         ensure_no_steam_relaunch)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
# .md, not .log - .gitignore drops every *.log in the tree, and this file is
# the evidence for two fixes rather than a run artifact.
OUT = os.path.join(ROOT, "docs", "data", "trap-window.md")

SLOT = "droha"
PORT = 38281
TRAP = "Cat Trap"
BACKGROUND = "Background Change Trap"


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

    def wait(self, needles, seconds, what):
        end = time.time() + seconds
        text = ""
        last = 0.0
        while time.time() < end:
            text += self.new()
            if any(n in text for n in needles):
                return text
            if time.time() - last > 10:
                last = time.time()
                say(f"waiting for {what}, {int(end - time.time())}s left")
            time.sleep(0.4)
        return text                      # a timeout is a result, not a crash


def dev(cmd, settle=0.0):
    with open(CMD, "w", encoding="utf-8") as f:
        f.write(cmd)
    for _ in range(240):
        try:
            if os.path.getsize(CMD) == 0:
                break
        except OSError:
            pass
        time.sleep(0.25)
    if settle:
        time.sleep(settle)


def newest_zip():
    out = os.path.join(ROOT, "Archipelago", "output")
    zips = [os.path.join(out, f) for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        raise SystemExit(f"no seed zip in {out}")
    return max(zips, key=os.path.getmtime)


def flags(text):
    """Every distinct 'watch: gameState=... loaded=... ' line, in order."""
    return [m.group(0) for m in re.finditer(r"watch: gameState=[^\r\n]*", text)]


def cameras(text):
    return [m.group(1) for m in re.finditer(r"watch: camera=([^\r\n]*)", text)]


def repaints(text):
    """How often Backgrounds.Tick actually wrote, per second it wrote in."""
    return [m.group(0).split("] ", 1)[-1]
            for m in re.finditer(r"backgrounds: repainted[^\r\n]*", text)]


def traps(text):
    return [m.group(0) for m in re.finditer(r"trap: [^\r\n]*", text)]


def level_index(text):
    """The index `state` last reported, so a reload targets what is loaded."""
    found = re.findall(r"state: .*? index=(-?\d+)", text)
    if not found:
        raise SystemExit("state never reported a level index")
    return found[-1]


def in_a_puzzle(log, seconds=45):
    """Block until a real, playable level is up, and say which.

    Asked with `controllers`, because "N registered on <level>" is only
    printed when a level is genuinely interactive - and nothing prints it on
    its own, which is why an earlier version of this probe waited for a line
    no one was going to write. gameState is not a substitute: it has reported
    Gameplay and RetryUI for the same action on different runs.
    """
    end = time.time() + seconds
    while time.time() < end:
        log.new()
        dev("controllers", 1.2)
        text = log.wait(["controllers: "], 10, "the controller list")
        for line in text.splitlines():
            if "registered on " in line:
                which = line.split("registered on ", 1)[1].split(" levelInstance")[0]
                say(f"in a puzzle: {which}")
                time.sleep(2.0)
                return which
        time.sleep(1.5)
    raise SystemExit("never landed in a playable level")


def to_level_select(log):
    for _ in range(8):
        dev("menu:levels", 2.5)
        log.new()
        dev("state", 1.0)
        text = log.wait(["state: gameState="], 15, "the game state")
        if "gameState=Levels_GameState" in text:
            time.sleep(2.0)
            return text
        time.sleep(2.0)
    raise SystemExit("never reached the level select")


def main():
    zip_path = sys.argv[1] if len(sys.argv) > 1 else newest_zip()
    save = zip_path[:-4] + ".apsave"
    parked = save + ".parked"

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    close_game()
    if os.path.exists(save):
        shutil.move(save, parked)

    server = subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(PORT), zip_path],
        cwd=os.path.join(ROOT, "Archipelago"),
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL, text=True,
    )

    findings = {}
    try:
        with Environment("probe-trap-window") as env:
            env.configure(Host="localhost", Port=str(PORT),
                          SlotName=SLOT, AutoConnect="true")
            say(f"server on {PORT}, launching")
            ensure_no_steam_relaunch()
            log = Log()
            subprocess.Popen([EXE], cwd=GAME)
            log.wait(["connected. ", "Archipelago refused"], 180, "the connection")
            log.wait(["toasts: overlay ready"], 60, "the title")
            time.sleep(3.0)
            say("connected")

            # --- QUESTION 1: the flags on the level select ----------------
            to_level_select(log)
            log.new()
            dev("watch:4", 6.0)
            text = log.new()
            findings["level select"] = flags(text)
            say("measured the level select")

            # --- into a puzzle --------------------------------------------
            # menu:title FIRST. `play` finds a live TitleMenu and clicks its
            # Play button, and the level select has no TitleMenu in the scene -
            # so from there it logs "play: no live TitleMenu" and the probe
            # cheerfully measured a menu for three runs while believing it was
            # in a puzzle. tools/playthrough.py has always done the pair.
            #
            # clickcard:0 was tried before this and did nothing at all.
            dev("menu:title", 3.0)
            dev("play", 9.0)
            in_a_puzzle(log)

            # A background trap first, so the camera has something of ours to
            # hold and question 2 has something to measure.
            server.stdin.write(f"/send {SLOT} {BACKGROUND}\n")
            server.stdin.flush()
            log.wait([f"received item: {BACKGROUND}"], 30, "the background trap")
            time.sleep(2.0)

            log.new()
            dev("watch:10", 12.0)
            text = log.new()
            findings["settled in a puzzle"] = flags(text)
            findings["camera while settled"] = cameras(text)
            findings["repaints while settled"] = repaints(text)
            say("measured a settled puzzle holding a background trap")

            # --- TEST A: a trap in a settled puzzle should just spring -----
            log.new()
            server.stdin.write(f"/send {SLOT} {TRAP}\n")
            server.stdin.flush()
            text = log.wait(["cat(s) reset the puzzle", "cat(s) found nothing",
                             "cat(s) arrived"], 40, "the trap to resolve")
            findings["A: trap in a settled puzzle"] = traps(text)
            say("measured a trap in a settled puzzle")

            # --- TEST B: a trap landing while a level is LOADING -----------
            # This is the one the guard exists for, and the one that has to
            # both avoid the freeze AND still go off afterwards. The trap is
            # sent first and the level is relaunched a beat later, so it ticks
            # while the load is in flight.
            log.new()
            dev("state", 1.0)
            index = level_index(log.wait(["state: gameState="], 15, "the index"))
            say(f"the puzzle in front of us is level index {index}")

            log.new()
            dev("watch:20")
            server.stdin.write(f"/send {SLOT} {TRAP}\n")
            server.stdin.flush()
            time.sleep(0.15)
            dev(f"loadlevel:{index}", 0.0)
            say("sent a trap into a level load")

            text = log.wait(["watch: finished"], 45, "the watch window")
            time.sleep(3.0)
            text += log.new()
            findings["B: trap into a load"] = traps(text)
            findings["B: flags through the load"] = flags(text)
            findings["B: repaints through the load"] = repaints(text)

            # --- TEST C: a trap landing on a COMPLETION --------------------
            log.new()
            dev("watch:20")
            server.stdin.write(f"/send {SLOT} {TRAP}\n")
            server.stdin.flush()
            time.sleep(0.15)
            dev("complete", 0.0)
            say("sent a trap into a completion")

            text = log.wait(["watch: finished"], 45, "the watch window")
            time.sleep(3.0)
            text += log.new()
            findings["C: trap onto a completion"] = traps(text)
            findings["C: flags through the completion"] = flags(text)
            findings["C: repaints through the completion"] = repaints(text)

            # --- is the game alive on the far side? -----------------------
            log.new()
            dev("state", 2.0)
            alive = log.wait(["state: gameState="], 20, "a reply after the trap")
            findings["alive after"] = ["ALIVE" if "state: gameState=" in alive
                                       else "NO REPLY - the game is hung"]
            findings["every trap line in the run"] = [
                l.split("] ", 1)[-1] for l in log.all.splitlines()
                if "trap:" in l
            ]
    finally:
        try:
            server.stdin.write("/exit\n")
            server.stdin.flush()
            server.wait(timeout=10)
        except Exception:
            server.kill()
        if os.path.exists(parked):
            if os.path.exists(save):
                os.remove(save)
            shutil.move(parked, save)
        close_game()

    with open(OUT, "w", encoding="utf-8") as f:
        for title, lines in findings.items():
            f.write(f"== {title} ==\n")
            for line in lines or ["(nothing recorded)"]:
                f.write(line + "\n")
            f.write("\n")

    for title, lines in findings.items():
        print(f"== {title} ==", flush=True)
        for line in lines or ["(nothing recorded)"]:
            print("   " + line, flush=True)

    print(f"Done: {len(findings)} measurements, written to "
          f"{os.path.relpath(OUT, ROOT)}", flush=True)


if __name__ == "__main__":
    main()
