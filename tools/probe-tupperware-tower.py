"""Are TupperwareTower's Foundation and Falling Blocks real checks?

ANSWERED, AND NOT BY THIS SCRIPT: no. They are the tower's mechanism. They
never raise a solved event, so as locations they could never be collected,
and they were deliberately deleted - see test_fill_stress.py's split comment
("Now (24, 94). TupperwareTower lost three groups and gained one back") and
tools/probe-dead-controllers.py, which measured it.

THIS SCRIPT CONCLUDED THE OPPOSITE, and the reasoning below is where it went
wrong: it solves the Tower, sees the level fail to complete, and calls the
other two "work the player has to do". Forcing SetSolved on the Tower does not
actually finish the puzzle, so the level not completing says nothing about the
other controllers. A verdict of WORK from this script means "inconclusive,
go and measure whether a solved event ever fires" - which is what
probe-dead-controllers.py does.

Kept because the listings it prints are useful and the trap is worth leaving
written down.


THE QUESTION. The level registers three controllers and the shipped table
lists one:

    [0] Foundation      type=StackableGrid
    [1] Tower           type=TupperwareTower   <- the only one in the table
    [2] Falling Blocks  type=StackableGrid

So the mod reports CONTROLLER MISMATCH on it, and the release gate fails on
that - the last failing check of twenty-one. The fix depends entirely on which
of two things those controllers are, and the two answers need opposite work:

  - REAL CHECKS the table is missing, in which case levels.json and names.json
    owe this level two more part locations, which moves location ids; or
  - INTERNALS of the tower, solved as a side effect of solving it, in which
    case they must never be locations and the right fix is to record that.

StackableGrid is not the giveaway it looks like: it carries locations on other
levels, so it cannot be dismissed as scenery the way Pannables was.

HOW THIS ANSWERS IT, without a run or a server. Boot the level directly, solve
ONLY the Tower, and look at the other two. If they flip to solved on their own
they are internals. If they stay unsolved while the level completes, they are
decoration. If they stay unsolved and the level does NOT complete, they are
work the player has to do - and therefore checks.

    py -3.13 tools/probe-tupperware-tower.py

Takes about a minute. Closes the game, restores the config.
"""
import os
import re
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
OUT = os.path.join(ROOT, "docs", "data", "tupperware-tower.md")

LEVEL_INDEX = 83
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


def controllers(log):
    """[(index, name, type, solved)] for the running level."""
    log.new()
    dev("controllers", 1.5)
    text = log.wait(["controllers: "], 15)
    time.sleep(1.0)
    text += log.new()

    found = []
    for line in text.splitlines():
        m = re.search(r"\[(\d+)\] (.+?) type=(\S+) solved=(True|False)", line)
        if m:
            found.append((int(m.group(1)), m.group(2).strip(),
                          m.group(3), m.group(4) == "True"))
    # Deduped by index, last listing wins.
    latest = {}
    for idx, name, kind, solved in found:
        latest[idx] = (idx, name, kind, solved)
    return [latest[k] for k in sorted(latest)]


def main():
    close_game()
    findings = {}

    with Environment("probe-tupperware-tower") as env:
        env.configure_devtools(MuteAudio="true",
                               TargetVirtualDesktop=str(DESKTOP),
                               RaiseWindowAtStartup="false")
        say("launching (no server, no run - this is about the level itself)")
        ensure_no_steam_relaunch()
        log = Log()
        subprocess.Popen([EXE], cwd=GAME)
        log.wait(["AllLevelInterfaces", "Loading [A Little To The Left"], 180)
        time.sleep(12.0)

        say(f"booting level {LEVEL_INDEX}")
        dev(f"boot:{LEVEL_INDEX}", 6.0)

        before = controllers(log)
        findings["as it loads"] = [f"[{i}] {n} type={t} solved={s}"
                                   for i, n, t, s in before]
        if not before:
            findings["as it loads"] = ["the level reported no controllers"]
            raise SystemExit("could not read the level's controllers")

        tower = [i for i, n, t, s in before if t == "TupperwareTower"]
        if not tower:
            raise SystemExit("no TupperwareTower controller on this level")

        say(f"solving ONLY the Tower (index {tower[0]})")
        log.new()
        dev(f"solve:{tower[0]}", 3.0)
        done = log.wait(["LevelComplete "], 10)
        completed = "LevelComplete " in done

        after = controllers(log)
        findings["after solving only the Tower"] = [
            f"[{i}] {n} type={t} solved={s}" for i, n, t, s in after]
        findings["did the level complete"] = [
            "yes" if completed else "no"]

        others = [(n, s) for i, n, t, s in after if t != "TupperwareTower"]
        flipped = [n for n, s in others if s]
        stayed = [n for n, s in others if not s]

        if flipped and not stayed:
            verdict = ("INTERNALS. Solving the Tower solved them too, so they "
                       "are part of the same act and must never be locations.")
        elif completed and stayed:
            verdict = ("DECORATION. The level finished with them still "
                       "unsolved, so nothing waits on them and they must not "
                       "be locations.")
        elif stayed and not completed:
            verdict = ("INCONCLUSIVE. They stayed unsolved and the level did "
                       "not finish - but forcing SetSolved on the Tower does "
                       "not really solve it, so the level was never going to "
                       "finish and this says nothing about the other two. "
                       "Measure whether a solved event ever fires instead: "
                       "tools/probe-dead-controllers.py did, and the answer "
                       "is that it does not. They are the tower's mechanism "
                       "and must not be locations.")
        else:
            verdict = ("UNCLEAR - read the listings above rather than trusting "
                       "a one-line answer.")
        findings["verdict"] = [verdict]
        say(verdict)

    close_game()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# TupperwareTower: are Foundation and Falling Blocks "
                "checks?\n\n")
        f.write("`tools/probe-tupperware-tower.py`. Boots the level with no "
                "run and no server, solves only the Tower, and looks at what "
                "the other two do.\n\n")
        for title, lines in findings.items():
            f.write(f"## {title}\n\n```\n")
            for line in lines or ["(nothing)"]:
                f.write(line + "\n")
            f.write("```\n\n")

    for title, lines in findings.items():
        print(f"== {title} ==", flush=True)
        for line in lines or ["(nothing)"]:
            print("   " + line, flush=True)
    print(f"Done: written to {os.path.relpath(OUT, ROOT)}", flush=True)


if __name__ == "__main__":
    main()
