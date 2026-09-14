"""Reproduce the freeze droha hit on 0.3.1, then show the guard stops it.

THE REPORT. "When finishing a level I got a background change trap. The
background started kind of strobing/shifting between multiple colors. It reset
(maybe a cat trap as well?) and when I clicked anywhere the game fully froze.
Had to alt+F4." The log ends mid-navigation with no exception in 1,549 lines.

WHY THE EARLIER PROBE WAS NOT ENOUGH. tools/probe-trap-window.py fired a trap
into a `loadlevel:` reload and the trap survived it - but that window reports
`level=null`, which even 0.3.1 treated as a miss. It never entered the state
the game died in. The difference is that droha finished a PUZZLE: a real
completion, a real navigation to the next slot, and a trap arriving inside it.
DevTools `complete` does not produce that (docs/release-testing.md:35), so
this drives the puzzle the way playthrough.py does - `solve:` per controller
group, which raises a genuine LevelComplete.

THE TIMING, which is the whole experiment. The trap is sent the instant the
game reports the level beaten, NOT before the final solve. droha's trap was
the completion's own reward: the log reads "beaten:", then "checks: sent 1",
then "received item: Cat Trap", and only then the navigation and the reset.
Pre-loading it instead - the first version of this script - left it already
owed when the puzzle finished, so it resolved before the navigation started
and eight attempts in a row sailed through with the guard AND the completion
grace both disabled. The window only takes an item that is delivered into it.

HOW A FREEZE IS DETECTED. Not by watching the log, which simply stops and
looks the same as a quiet game. DevTools consumes the command file inside
Update, so a command that is never taken back means Update is not running:
the game is hung. That is a direct read of the thing that actually broke.

    py -3.13 tools/repro-trap-freeze.py [--attempts N] [seed.zip]

Run it against a build WITH the guard and again against one without, and the
verdicts should differ. It closes the game either way, including when the game
is hung and has to be killed.
"""
import os
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment, close_game, ensure_no_steam_relaunch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
OUT = os.path.join(ROOT, "docs", "data", "trap-freeze-repro.md")

SLOT = "droha"
PORT = 38281
TRAP = "Cat Trap"
BACKGROUND = "Background Change Trap"

#: How many times to re-settle the pieces around the trap's arrival. Each
#: `jiggle` starts a fresh set of tweens; the trap springs on the mod's own
#: timer, so the dangerous window is held open rather than aimed at.
JIGGLES = 6

#: Where to click after the reset, in screen pixels on a 1920x1080 window.
#: Spread out because the report is "anywhere" - the middle of the puzzle, a
#: corner of it, and two points well off it.
CLICK_POINTS = [(960, 540), (700, 400), (1300, 700), (200, 900)]

#: Send no traps at all, to find out what the HARNESS breaks on its own.
#:
#: Run B showed the guarded build reaching two live levels at attempt 6 -
#: directly after attempt 5, where this script gave up on MedicineCabinet's
#: 13 controller groups and navigated away from a half-solved puzzle. Leaving
#: a level mid-flight through menu:title and play is something no player does
#: and something this script does constantly, so it is a candidate cause in
#: its own right. With no trap sent, any doubling that remains is ours, and
#: any conclusion drawn from a run without this control is worth very little.
NO_TRAP = "--no-trap" in sys.argv

#: Which virtual desktop to run the game on. This script deliberately breaks
#: the game, and droha watching it thrash with no idea whether the harness or
#: the mod is at fault is not a thing to put in front of someone.
#: harness_env restores the setting on the way out.
DESKTOP = 1


def say(what):
    print(f"-- {what}", flush=True)


class Log:
    def __init__(self):
        self.pos = os.path.getsize(LOG) if os.path.exists(LOG) else 0

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
        return text

    def wait(self, needles, seconds, what=""):
        end = time.time() + seconds
        text = ""
        while time.time() < end:
            text += self.new()
            if any(n in text for n in needles):
                return text
            time.sleep(0.3)
        return text


def send(cmd):
    """Drop a command in the file without waiting for it to be taken."""
    with open(CMD, "w", encoding="utf-8") as f:
        f.write(cmd)


def taken(seconds):
    """True if DevTools consumed the command file - ie Update is alive."""
    end = time.time() + seconds
    while time.time() < end:
        try:
            if os.path.getsize(CMD) == 0:
                return True
        except OSError:
            pass
        time.sleep(0.25)
    return False


def dev(cmd, settle=0.0, seconds=60.0):
    send(cmd)
    alive = taken(seconds)
    if settle and alive:
        time.sleep(settle)
    return alive


def alive(seconds=20.0):
    """Is the game's Update loop still running?"""
    return dev("state", 0.0, seconds)


def newest_zip():
    out = os.path.join(ROOT, "Archipelago", "output")
    zips = [os.path.join(out, f) for f in os.listdir(out) if f.endswith(".zip")]
    if not zips:
        raise SystemExit(f"no seed zip in {out}")
    return max(zips, key=os.path.getmtime)


def in_a_puzzle(log, seconds=60):
    """Block until a playable level is up; returns (name, group count)."""
    end = time.time() + seconds
    while time.time() < end:
        log.new()
        if not dev("controllers", 1.0):
            return None, 0
        text = log.wait(["controllers: "], 10)
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


def trap_note(lines):
    return "; ".join(lines) if lines else "no trap line logged"


def count_levels(log):
    """How many levels are loaded right now. Exactly one is correct.

    THE SIGNAL, and it is a count rather than a hope. Waiting for a hard
    freeze made this untestable: the hang is a race, it needs a click, and it
    turned up in maybe a third of runs. Two levels alive at once is the STATE
    that leads there - a direct consequence of a reset landing inside a
    navigation, and what droha actually saw: "oh god 2 levels loaded at once".
    A state can be counted on every attempt and compared between builds.

    Returns -1 rather than raising, so a caller on an error path can still
    report a number. The first version only counted on the happy path, which
    meant the attempts that had gone WRONG were the ones with no measurement.
    """
    log.new()
    if not dev("livelevels", 1.0, 25.0):
        return -1
    counted = log.wait(["livelevels: "], 15)
    for line in counted.splitlines():
        if "livelevels: " in line:
            try:
                return int(line.split("livelevels: ")[1].split(" ")[0])
            except (IndexError, ValueError):
                pass
    return -1


def attempt(log, server, n):
    """One go: finish a puzzle with a trap in flight. Returns a verdict line."""
    name, groups = in_a_puzzle(log)
    if name is None:
        return f"attempt {n}: no playable level, skipped"

    # COUNTED HERE, with a puzzle actually up, because this is the only moment
    # where exactly one loaded level is unambiguously correct.
    #
    # The first version counted straight after the trap instead and read 0
    # every single time - which is simply what "between levels" looks like,
    # not a defect. A metric that fires on every run measures nothing. With a
    # playable puzzle on screen, anything but 1 is the stacked-levels state
    # droha photographed.
    standing = count_levels(log)
    say(f"attempt {n}: {name}, {groups} group(s), {standing} level(s) loaded")
    if standing != 1:
        return f"attempt {n}: {name} - opened with {standing} LEVELS ALIVE"

    # Everything but the last group, so the completion is one command away.
    for i in range(groups - 1):
        if not dev(f"solve:{i}", 0.4):
            return f"attempt {n}: {name} - HUNG while solving group {i}"
        if "LevelComplete " in log.new():
            say(f"attempt {n}: finished early at group {i}, no window to aim at")
            return f"attempt {n}: {name} - completed early, no trap window"

    # THE MOMENT, and the timing is the whole experiment.
    #
    # The trap is sent AFTER the completion, not before it. droha's trap was
    # the completion's own reward - the log reads "beaten:", then
    # "checks: sent 1", then "received item: Cat Trap", and only then the
    # navigation and the reset. Pre-loading the trap (the first version of
    # this script) meant it was already owed when the puzzle finished, so it
    # resolved before the navigation ever started, and eight attempts sailed
    # through with the guard AND the grace both disabled. The window is the
    # one between the completion landing and the next level finishing its
    # load, and an item only enters it by being delivered into it.
    log.new()
    send(f"solve:{groups - 1}")
    if not taken(60.0):
        return f"attempt {n}: {name} - HUNG on the final solve"

    # The instant the game says the level is beaten, put the trap in flight.
    beaten = log.wait(["beaten:", "LevelComplete "], 20)
    if "beaten:" not in beaten and "LevelComplete " not in beaten:
        # Count anyway. A puzzle that will not report its own completion is
        # exactly the shape of two levels stacked on one another, and the
        # first version of this script skipped the measurement on precisely
        # the attempts that had gone wrong.
        stuck = count_levels(log)
        if stuck != 1:
            return f"attempt {n}: {name} - no completion AND {stuck} LEVELS ALIVE"
        return f"attempt {n}: {name} - no completion, but only 1 level"
    if not NO_TRAP:
        server.stdin.write(f"/send {SLOT} {TRAP}\n")
        server.stdin.flush()

    # LIVE SETTLE TWEENS, the ingredient every earlier run was missing and the
    # reason 29 attempts came back clean.
    #
    # droha was PLAYING - dropping pieces. Dropping one starts a LeanTween
    # settle whose callback closes over the piece, and a closure over a
    # DESTROYED piece is the whole cat-trap failure mode. `solve:` force-solves
    # a controller and starts no such tween, so every reset here landed on a
    # puzzle with nothing dangerous in flight. droha put it straight: "do you
    # need me to drop something off center again or something you can't do
    # easily?"
    #
    # `jiggle` calls DragObject.Snap() by reflection, which IS the settle.
    # DevTools already records that a synthetic pointer drag starts no tween at
    # all - 24 drags, zero detached - because DragObject has no OnDrag handler,
    # so dispatching pointer events would not have helped either.
    #
    # Repeated, because the trap springs on the mod's own timer: the window has
    # to be held open rather than hit on the first try.
    for _ in range(JIGGLES):
        if not dev("jiggle", 0.0, 20.0):
            return f"attempt {n}: {name} - FROZE while the pieces were settling"
    say(f"attempt {n}: completion seen, trap sent, pieces settling")

    if not alive(25.0):
        return f"attempt {n}: {name} - FROZE after the completion"

    time.sleep(4.0)
    tail = log.new()
    trap_lines = [l.split("] ", 1)[-1] for l in tail.splitlines() if "trap:" in l]
    if not alive(25.0):
        return f"attempt {n}: {name} - FROZE shortly after the completion"

    # AND THEN CLICK, which is where droha's game actually died: "it reset ...
    # and when I clicked anywhere the game fully froze."
    #
    # By now the trap has reset a level inside a navigation and the scene can
    # be holding TWO live levels - droha, watching a run of this script: "oh
    # god 2 levels loaded at once". A click into that is the one action the
    # harness never took, and the levels that should have been torn down still
    # have their listeners on the global event bus.
    #
    # Several clicks across the window rather than one in the middle, because
    # "anywhere" is the claim and one point can easily find nothing.
    for x, y in CLICK_POINTS:
        if not dev(f"clickat:{x},{y}", 0.3, 25.0):
            return (f"attempt {n}: {name} - FROZE on a click at ({x},{y}) "
                    f"after the reset")
    if not alive(25.0):
        return f"attempt {n}: {name} - FROZE shortly after the clicks"

    what = trap_note(trap_lines)
    return f"attempt {n}: {name} - survived ({what})"


def main():
    args = [a for a in sys.argv[1:]]
    attempts = 4
    if "--no-trap" in args:
        args.remove("--no-trap")
    if "--attempts" in args:
        i = args.index("--attempts")
        attempts = int(args[i + 1])
        del args[i:i + 2]
    zip_path = args[0] if args else newest_zip()

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

    verdicts = []
    try:
        with Environment("repro-trap-freeze") as env:
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

            # A background trap up front, because droha had one: it is the
            # other half of the report and it recolours the very transition
            # the cat trap lands in.
            server.stdin.write(f"/send {SLOT} {BACKGROUND}\n")
            server.stdin.flush()
            log.wait([f"received item: {BACKGROUND}"], 30)

            dev("menu:title", 3.0)
            dev("play", 9.0)

            for n in range(1, attempts + 1):
                verdict = attempt(log, server, n)
                say(verdict)
                verdicts.append(verdict)
                if "FROZE" in verdict or "HUNG" in verdict:
                    break
                # On to the next puzzle for a fresh window.
                if not dev("menu:title", 3.0) or not dev("play", 9.0):
                    verdicts.append(f"attempt {n}: could not open the next level")
                    break
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

    froze = any("FROZE" in v or "HUNG" in v or "LEVELS ALIVE" in v
                for v in verdicts)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# Reproducing the trap-on-completion freeze\n\n")
        f.write(f"`tools/repro-trap-freeze.py`, {len(verdicts)} attempt(s).\n\n")
        f.write("BROKE\n" if froze else "CLEAN on every attempt\n")
        f.write("\n```\n")
        for v in verdicts:
            f.write(v + "\n")
        f.write("```\n")

    for v in verdicts:
        print("   " + v, flush=True)
    print(f"Done: {'BROKE' if froze else 'clean'} "
          f"over {len(verdicts)} attempt(s), written to "
          f"{os.path.relpath(OUT, ROOT)}", flush=True)


if __name__ == "__main__":
    main()
