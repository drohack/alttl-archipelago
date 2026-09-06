"""Hammer level loads looking for the blank-level bug.

The bug: a level opens with nothing in it and every input ignored. It has never
been reproduced on demand, so there is nothing to fix from. Checks.cs already
arms a watchdog that logs "LEVEL LOADED EMPTY: <id>" when a level comes up with
no objects; this just gives it many chances to fire.

Deliberately hostile ordering. The one report we have came from opening a later
card after moving around the menus, and levels that load slowly are the obvious
suspects, so this interleaves heavy and light levels and re-visits rather than
walking the table in order. A soak that only ever loads levels the same way
reproduces only the paths that already work.

Reports every empty hit AND the total loads attempted, because "no hits" is only
meaningful next to how many chances it had.

Boots levels directly, which writes save state, so it runs inside harness_env
and puts the save folder and BepInEx config back on exit.
"""
import json
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from harness_env import Environment

GAME = r"G:/Games/Steam/steamapps/common/A Little To The Left"
LOG = os.path.join(GAME, "BepInEx", "LogOutput.log")
CMD = os.path.join(GAME, "BepInEx", "alttl-devtools-commands.txt")
TABLE = "apworld/alttl/data/levels.json"

ROUNDS = int(os.environ.get("SOAK_ROUNDS", "3"))
PER_ROUND = int(os.environ.get("SOAK_PER_ROUND", "40"))

#: Seconds to sit on each level. Must exceed the watchdog's 6 second window.
DWELL = float(os.environ.get("SOAK_DWELL", "7.5"))


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


class Log:
    def __init__(self):
        self.pos = os.path.getsize(LOG)

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
        self.pos = size
        return text


def main():
    levels = json.load(open(TABLE, encoding="utf-8"))["levels"]
    # Heaviest first by controller count, so the interleave below alternates
    # slow, asset-heavy levels with trivial ones.
    heavy = sorted(levels, key=lambda l: -len(l["controllers"]))
    light = list(reversed(heavy))

    log = Log()
    empties = []
    loads = 0
    rng = random.Random(20260904)

    for rnd in range(1, ROUNDS + 1):
        order = []
        for i in range(PER_ROUND // 2):
            order.append(heavy[i % len(heavy)])
            order.append(light[i % len(light)])
        rng.shuffle(order)

        for n, lv in enumerate(order, 1):
            idx = lv["levelIndex"]
            name = lv["levelId"]
            log.new()
            dev(f"boot:{idx}")
            loads += 1
            # Dwell longer than the watchdog's own threshold, or this proves
            # nothing.
            #
            # Checks.cs only reports a level empty after it has stayed empty for
            # a CONTINUOUS 6 seconds, ticked once a second. The first version of
            # this soak moved on after 0.4 to 1.2 seconds, so the counter never
            # reached 6 and the watchdog could not have fired however broken the
            # level was. It ran 40 loads and reported "0 empty", which was not a
            # result - it was the harness measuring nothing and saying it was
            # fine. Hostile ordering is still worth having, but not at the cost
            # of making the detector unreachable.
            time.sleep(DWELL)
            out = log.new()
            if "LEVEL LOADED EMPTY" in out:
                empties.append(name)
                print(f"[round {rnd}/{ROUNDS} {n}/{len(order)}] EMPTY: {name} (idx {idx})",
                      flush=True)
            elif n % 5 == 0:
                print(f"[round {rnd}/{ROUNDS} {n}/{len(order)}] {loads} loads, "
                      f"{len(empties)} empty so far (last {name})", flush=True)

    print(f"Done: {loads} level loads, {len(empties)} empty "
          f"({', '.join(sorted(set(empties))) if empties else 'none reproduced'})",
          flush=True)


if __name__ == "__main__":
    with Environment("emptysoak"):
        main()
