r"""Hold the shipped level table up against the game's own dump.

WHY THIS EXISTS
---------------

This project keeps getting a number wrong, confidently, in prose. How many
levels are randomizable. How many have hints. Which abilities gate a partial
completion. And the one that cost three playtest sessions: how many levels are
in the Daily Tidy pool - a code comment said six, the real answer is 36, and
docs/content-report.md had ALREADY said 36 in bold. The project knew and the
code did not, because nothing connected the two.

A comment cannot fail. A doc cannot fail. This can.

Run it against a fresh DevTools dump and it reports every field where
apworld/alttl/data/levels.json and the running game disagree.

    py -3.13 tools/check-game-facts.py [path-to-alttl-dump.json]

TAKE THE DUMP WITH THE MOD OFF
------------------------------

Not a nicety. The mod's daily guard answers LevelInterface.IsDailyTidy and
IsHolidayDaily with false while a run is active, precisely so the game stops
routing the player to the Daily page - so a dump taken with the mod loaded
reports zero daily levels and looks like proof that there are none. That
happened, and the all-false result was very nearly written into levels.json.

RENAMING THE PLUGIN FOLDER DOES NOT DISABLE IT. BepInEx scans every
subdirectory of plugins/ for DLLs and does not care what the folder is called,
so "BepInEx\plugins\_ALTTLArchipelago.off" loads exactly as before. An earlier
version of this file recommended precisely that, and a level sweep run under it
came back with all 36 daily flags false - the contamination this warning
exists to prevent, produced by following the warning.

Move the folder OUT of plugins entirely:

    move BepInEx\plugins\ALTTLArchipelago  ->  BepInEx\_parked\ALTTLArchipelago
    launch, let the sweep or dump write, close, move it back

Then check before trusting the output: the BepInEx log must contain ZERO lines
tagged "A Little To The Left Archipelago". DevTools alone is enough to produce
the dump.

FIELDS THAT MOVE ON THEIR OWN
-----------------------------

Two of the dump's numbers are answers to "what is true right now", not "what is
true of this level", and comparing them is how you get an unstable constant:

- DailyTidyManager.GetDailyTidyLevels(true) lists what is in rotation TODAY. It
  returned 36 entries in one dump and 16 an hour later on the same day.
- isUnlocked, numSolutionsFound and isDailyTidy read live state and depend on
  which save is loaded. The sweep populates isDailyTidy correctly because it
  BOOTS each level; the dump reads it cold and gets false for everything.

So the daily pool is derived from dailyDateCount, which is a property of the
level and does not move. Anything else here should be equally stable, and if a
field starts flapping between runs it belongs on UNSTABLE rather than in a
constant somewhere.
"""

import io
import json
import os
import sys

#: Read live rather than authored, so a mismatch means nothing.
UNSTABLE = {
    "isUnlocked",
    "numSolutionsFound",
    "isDailyTidy",       # false cold; the levelsweep boots each level and is right
}

DEFAULT_DUMP = os.path.join(
    r"G:\Games\Steam\steamapps\common\A Little To The Left",
    "BepInEx", "alttl-dump.json")

TABLE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                     "apworld", "alttl", "data", "levels.json")


def coerce(value):
    """The dump writes every value as a JSON string. Compare like with like.

    Skipping this step is not harmless: it makes every field disagree, which
    reads as catastrophe and is really just False against "False". That false
    alarm cost twenty minutes the first time.
    """
    if not isinstance(value, str):
        return value
    if value == "True":
        return True
    if value == "False":
        return False
    try:
        return int(value)
    except ValueError:
        return value


def main(argv):
    dump_path = argv[1] if len(argv) > 1 else DEFAULT_DUMP
    if not os.path.isfile(dump_path):
        print(f"FAIL: no dump at {dump_path}", flush=True)
        print("      run the game with ALTTLDevTools and the mod moved aside",
              flush=True)
        return 2

    dump_raw = json.load(io.open(dump_path, encoding="utf-8"))
    dump = {l["levelId"]: {k: coerce(v) for k, v in l.items()}
            for l in dump_raw["levels"]}
    table = json.load(io.open(TABLE, encoding="utf-8"))["levels"]

    print(f"dump taken {dump_raw.get('dumpedAt', '?')}, "
          f"{len(dump)} levels; table has {len(table)}", flush=True)

    problems = []

    absent = [l["levelId"] for l in table if l["levelId"] not in dump]
    if absent:
        problems.append(f"{len(absent)} table level(s) are not in the dump: "
                        f"{absent[:5]}")

    compared = {}
    for level in table:
        rec = dump.get(level["levelId"])
        if rec is None:
            continue
        for field, ours in level.items():
            if field in UNSTABLE or field not in rec:
                continue
            compared[field] = compared.get(field, 0) + 1
            if ours != rec[field]:
                problems.append(
                    f"{level['levelId']}.{field}: table={ours!r} game={rec[field]!r}")

    for field in sorted(compared):
        print(f"  checked {field}: {compared[field]} levels", flush=True)

    # The derived fact that started all this, asserted rather than assumed.
    everyday = sum(1 for l in table if l.get("isDailyTidy"))
    holiday = sum(1 for l in table if l.get("isHolidayDaily"))
    from_dump = sum(1 for l in table
                    if int(dump[l["levelId"]]["dailyDateCount"]) > 0)
    print(f"  daily pool: {everyday} everyday + {holiday} holiday "
          f"= {everyday + holiday}", flush=True)
    if holiday != from_dump:
        problems.append(f"isHolidayDaily: table says {holiday}, the game's "
                        f"dailyDateCount says {from_dump}")

    if problems:
        print(f"\nFAIL: {len(problems)} disagreement(s) between the table and "
              f"the game", flush=True)
        for line in problems[:40]:
            print(f"   {line}", flush=True)
        if len(problems) > 40:
            print(f"   ... and {len(problems) - 40} more", flush=True)
        print("\nRegenerate levels.json with the DevTools levelsweep, or work "
              "out which side is wrong. Do not edit the expectation.", flush=True)
        return 1

    print("\nPASS: the table agrees with the game on every stable field",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
