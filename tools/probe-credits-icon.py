"""What art the credits card carries, and what else the game could offer.

droha, on the hand print the credits card shows: "sure the hand print is the
games authored art, but is that for the credits, or you just picked it? is
there a C for credits or a Star one we can use if we get to choose?"

TWO QUESTIONS, TWO ANSWERS.

Whose art is it. LevelInterface carries its own LockedIcon and UnlockedIcon,
and the mod sets neither - it puts the game's credits level on the track and
the LevelIcon draws whatever that level already has. `creditscard` now prints
both sprite names, so the answer is a name rather than an inference.

What else there is. The level select draws from the game's own sprite set, so
anything already loaded is a candidate that will look like it belongs. This
sweeps for the obvious families - a letter C, a star, a trophy, an ending -
and exports the plausible ones to PNG so they can be looked at rather than
guessed at from a name.

    py -3.13 tools/probe-credits-icon.py

Findings land in docs/data/credits-icon.md and any exports in
docs/images/raw/credits-icons/. The game is closed afterwards and harness_env
restores the config.
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
GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
EXE = os.path.join(GAME, "A Little To The Left.exe")
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
OUT = os.path.join(ROOT, "docs", "data", "credits-icon.md")
EXPORTS = os.path.join(ROOT, "docs", "images", "raw", "credits-icons")

DESKTOP = 1

#: Families worth looking at for an ending card. Kept broad - a name is cheap
#: to reject and an icon that exists but was never listed cannot be chosen.
FILTERS = [
    "credit", "star", "trophy", "medal", "end", "finish", "complete",
    "heart", "letter", "ribbon", "award", "flag",
]


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


def main():
    close_game()
    if os.path.isdir(EXPORTS):
        shutil.rmtree(EXPORTS)
    os.makedirs(EXPORTS, exist_ok=True)

    findings = {}
    with Environment("probe-credits-icon") as env:
        env.configure_devtools(MuteAudio="true",
                                   TargetVirtualDesktop=str(DESKTOP),
                               RaiseWindowAtStartup="false")
        # NO server and NO run. The question is about the game's own art, and
        # the credits LevelInterface exists from the title screen - the level
        # table is in memory long before anything is played. Connecting would
        # only add a seed's worth of noise.
        say("launching (no server needed - this is about the game's art)")
        ensure_no_steam_relaunch()
        log = Log()
        subprocess.Popen([EXE], cwd=GAME)
        log.wait(["Loading [A Little To The Left Archipelago",
                  "AllLevelInterfaces"], 180)
        time.sleep(12.0)

        log.new()
        dev("creditscard", 2.0)
        card = log.wait(["creditscard: "], 20)
        findings["the credits card's own art"] = [
            l.split("] ", 1)[-1] for l in card.splitlines()
            if "creditscard: " in l]

        found = {}
        for word in FILTERS:
            log.new()
            if not dev(f"sprites {word}", 1.5):
                say(f"'{word}': the game stopped responding")
                break
            text = log.wait(["sprites:"], 20)
            # Entries are logged as "sprites:  <name>" with TWO spaces; the
            # summary is "sprites: N distinct name(s)..." with one. Matching
            # loosely would file the summary as a sprite called
            # "12 distinct name(s) of 40000 loaded".
            names = [m.group(1).strip()
                     for m in re.finditer(r"sprites:  (.+)", text)]
            if names:
                found[word] = sorted(set(names))
                say(f"'{word}': {len(found[word])} sprite(s)")
            else:
                say(f"'{word}': none")

        for word, names in found.items():
            findings[f"sprites matching '{word}'"] = names

    close_game()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# The credits card's icon, and what else the game has\n\n")
        f.write("`tools/probe-credits-icon.py`. The mod sets no icon on this "
                "card - the level carries its own, and the first section names "
                "it.\n\n")
        for title, lines in findings.items():
            f.write(f"## {title}\n\n```\n")
            for line in lines or ["(nothing)"]:
                f.write(line + "\n")
            f.write("```\n\n")

    for title, lines in findings.items():
        print(f"== {title} ==", flush=True)
        for line in (lines or ["(nothing)"])[:40]:
            print("   " + line, flush=True)
    print(f"Done: written to {os.path.relpath(OUT, ROOT)}", flush=True)


if __name__ == "__main__":
    main()
