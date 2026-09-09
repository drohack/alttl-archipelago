"""Generate a seed for a human playtest, and say what is actually in it.

Not the e2e's seed. That one is deliberately small and awkward - eight puzzles,
two packs - to exercise the machinery quickly. This one is for playing.

The point of the settings below is BREADTH: as many of the twelve abilities as
the generator can be made to use. The lever for that is mechanic_coverage,
because stacking, containers, drawers and jigsaws exist only as hand-made
puzzles - at the default of 3 they appear by luck, and at 0 they cannot appear
at all. Archive weight is raised for the same reason: generator puzzles are the
randomized dailies and lean on a narrower set of mechanics.

It prints the ability spread afterwards rather than assuming it worked.

AND IT ROLLS SEVERAL TIMES AND KEEPS THE BEST. The settings ask for breadth;
whether a given roll delivers it is luck, and the part that matters most to a
playtester is the part they see FIRST. droha asked for "new puzzle types
earlier that we haven't talked about", which is a property of the opening ten
cards, not of the 40. So each candidate is scored on how many distinct
mechanics and how many not-yet-played levels appear early, and the winner is
kept.
"""
import io
import json
import os
import subprocess
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(REPO, "Archipelago")
OUT = os.path.join(REPO, "testserver", "out-playtest")
YAML_DIR = os.path.join(REPO, "testserver", "yaml-playtest")

SLOT = "droha"
PUZZLES = 40
BEAT = 20

YAML = f"""name: {SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: {PUZZLES}
  levels_to_beat: {BEAT}

  # BREADTH. 6 is the maximum: every hand-made mechanic is guaranteed a
  # presence rather than left to chance, which is the only way stacking,
  # containers, drawers and jigsaws reliably appear at all.
  mechanic_coverage: 6

  # Generators are the randomized dailies and repeat a narrow set of
  # mechanics. Tilting away from them brings in hand-made puzzles, which is
  # where the variety lives - and a playtest wants variety more than a normal
  # run does.
  #
  # The campaign gets a real share here rather than the default 10: these 69
  # levels went from unreachable to reachable and are the least-tested content
  # in the game.
  generator_weight: 20
  archive_weight: 40
  base_weight: 40

  # Locks ON with a single starting ability - the abilities are then things to
  # find, which is the point of a playtest.
  ability_locks: true
  starting_abilities: 1

  # Four puzzles open immediately so there is something to do before the first
  # pack, then packs of four.
  pack_size: 4
  guaranteed_open_slots: 4

  cat_trap_chance: 25
  hint_coverage: 50
  skip_count: 5
  progression_balancing: 0
  accessibility: full
"""


#: How many seeds to roll before picking. Generation is a few seconds each.
CANDIDATES = 12

#: How far into the run counts as "early" for scoring.
EARLY = 10

#: Levels a human has actually played, read from tools/tested-levels.txt.
#:
#: A file rather than a set in here, because the list only grows and editing a
#: literal in a scoring function is a poor place to record test coverage. The
#: file carries the reasoning too - notably which levels a playthrough has
#: PROVED safe, which is worth more than the fact that they were seen.
def _tested():
    path = os.path.join(REPO, "tools", "tested-levels.txt")
    out = set()
    with io.open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line and not line.startswith("#"):
                out.add(line)
    return out


ALREADY_SEEN = _tested()


def types_of(level_ids):
    """The distinct ObjectController classes those levels contain.

    A "puzzle type" is the controller class, not the ability. There are 38
    classes against 12 abilities, so scoring on abilities - which is what this
    tool did first - counts Stackables and StackableGrid and TupperwareTower as
    one thing when to a player they are three different puzzles.
    """
    levels = json.load(open(os.path.join(REPO, "apworld", "alttl", "data",
                                         "levels.json"), encoding="utf-8"))
    out = set()
    for level in levels["levels"]:
        if level["levelId"] not in level_ids:
            continue
        for c in level["controllers"]:
            out.add(c["type"])
    return out


def types_by_level():
    levels = json.load(open(os.path.join(REPO, "apworld", "alttl", "data",
                                         "levels.json"), encoding="utf-8"))
    return {l["levelId"]: {c["type"] for c in l["controllers"]}
            for l in levels["levels"]}


def plan_of(seed_path):
    """The run's slots, in track order, straight out of the seed."""
    code = """
import zipfile, zlib, json, sys
from Utils import restricted_loads
f = zipfile.ZipFile(sys.argv[1])
n = [x for x in f.namelist() if x.endswith('.archipelago')][0]
d = restricted_loads(zlib.decompress(f.read(n)[1:]))['slot_data'][1]
print(json.dumps({'slots': [s['levelId'] for s in d['slots']],
                  'boundaries': list(d['pack_boundaries'])}))
"""
    r = subprocess.run([sys.executable, "-c", code, seed_path],
                       cwd=AP, capture_output=True, text=True,
                       env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        return None
    return json.loads(r.stdout.strip().splitlines()[-1])


#: Levels whose content changed recently and most needs eyes on it. A seed
#: that contains none of these is a worse test than one that does, whatever
#: else it has going for it.
WANTED = {
    # 1 location -> 11. Ten declared dances restored 2026-09-09.
    "Radial Dance Party",
    # Six phases, the chain corrected and `Food` added.
    "TupperwareNesting",
    # The shared-object dimming fix.
    "Coins 1 (Shape)", "Spoons", "Books 3", "TrickOrTidy_ChocolateBars",
    "Workbench",
}


#: Radial Dance Party used to be unreachable, and this is where that was
#: discovered: eighteen rolls could not place it, because pool.py offered only
#: "generator" and "archive" as sources and a campaign level could enter only
#: through the mechanic-coverage reserve. base_weight fixed that at the source,
#: so the scorer no longer needs to fight for it - it just needs to say that a
#: recently-changed level is worth more than a novel one.


def score(plan, abilities_of):
    """Reward variety, and reward it EARLY.

    Three terms, in the order they matter to someone sitting down to test:
    distinct mechanics in the opening cards, levels they have not seen before
    in the opening cards, and distinct mechanics across the whole run as a
    tie-break.
    """
    early = plan["slots"][:EARLY]
    early_mechanics = set()
    for level_id in early:
        early_mechanics |= abilities_of.get(level_id, set())
    fresh = sum(1 for l in early if l not in ALREADY_SEEN)

    # THE THING droha ACTUALLY ASKS FOR: puzzle types not yet tested, early.
    # Controller classes rather than abilities, because that is the grain a
    # player experiences - Stackables, StackableGrid and TupperwareTower all
    # count as "Stacking" and are three quite different puzzles.
    by_level = types_by_level()
    tested = types_of(ALREADY_SEEN)
    early_new_types = set()
    for level_id in early:
        early_new_types |= (by_level.get(level_id, set()) - tested)

    whole = set()
    for level_id in plan["slots"]:
        whole |= abilities_of.get(level_id, set())

    # Recently-changed levels are worth more than novelty: the point of this
    # seed is to exercise what moved.
    # Deliberately small. At 8 apiece these seven could contribute +56 and
    # swamp the untested-types term they are supposed to sit beside - the
    # winning roll had ONE new puzzle type in its opening and beat a candidate
    # with four. They are worth a nudge, not a veto.
    changed = sum(2 for l in plan["slots"] if l in WANTED)

    # Untested types in the opening dominate, which is what was asked for.
    return ((len(early_new_types) * 10) + (len(early_mechanics) * 3)
            + (fresh * 2) + len(whole) + changed)



def abilities_by_level():
    """levelId -> the abilities its puzzle groups need, from the shipped table."""
    names = json.load(open(os.path.join(REPO, "apworld", "alttl", "data",
                                        "names.json"), encoding="utf-8"))
    out = {}
    for level_id, entry in names["levels"].items():
        needed = set()
        for part in entry.get("parts", {}).values():
            needed |= set(part.get("abilities", ()))
        out[level_id] = needed
    return out


def roll(index):
    """Generate one candidate into its own folder. Returns (path, plan)."""
    folder = os.path.join(OUT, "cand%d" % index)
    os.makedirs(folder, exist_ok=True)
    for f in os.listdir(folder):
        os.remove(os.path.join(folder, f))

    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", folder],
        cwd=AP, capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print("      roll %d failed" % index, flush=True)
        print(r.stdout[-1500:], flush=True)
        return None, None

    seeds = [f for f in os.listdir(folder) if f.endswith(".zip")]
    if not seeds:
        return None, None
    path = os.path.join(folder, seeds[0])
    return path, plan_of(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(YAML_DIR, exist_ok=True)
    for f in os.listdir(YAML_DIR):
        os.remove(os.path.join(YAML_DIR, f))
    # Old candidates and any previous seed, so nothing stale is left to host
    # by accident - a stale zip beside a new one has cost a session before.
    for entry in os.listdir(OUT):
        full = os.path.join(OUT, entry)
        if os.path.isdir(full):
            for f in os.listdir(full):
                os.remove(os.path.join(full, f))
            os.rmdir(full)
        else:
            os.remove(full)

    with open(os.path.join(YAML_DIR, "playtest.yaml"), "w", encoding="utf-8") as fh:
        fh.write(YAML)

    abilities_of = abilities_by_level()

    print("generating %d puzzles, beat %d to finish" % (PUZZLES, BEAT), flush=True)
    print("rolling %d candidates and keeping the one with the most variety "
          "in the first %d cards" % (CANDIDATES, EARLY), flush=True)

    best = None
    for i in range(CANDIDATES):
        path, plan = roll(i)
        if plan is None:
            continue
        value = score(plan, abilities_of)
        early = plan["slots"][:EARLY]
        mechanics = set()
        for level_id in early:
            mechanics |= abilities_of.get(level_id, set())
        fresh = sum(1 for l in early if l not in ALREADY_SEEN)
        by_level = types_by_level()
        tested = types_of(ALREADY_SEEN)
        new_types = set()
        for level_id in early:
            new_types |= (by_level.get(level_id, set()) - tested)
        print("  [%d/%d] score %3d - %d untested puzzle types, %d mechanics, "
              "%d unseen levels in the first %d"
              % (i + 1, CANDIDATES, value, len(new_types), len(mechanics),
                 fresh, EARLY), flush=True)
        if best is None or value > best[0]:
            best = (value, path, plan)

    if best is None:
        print("no seed produced", flush=True)
        return 1

    _, path, plan = best

    # Move the winner up beside the candidates and drop the rest, so there is
    # exactly one zip to host.
    final = os.path.join(OUT, os.path.basename(path))
    os.replace(path, final)
    for entry in os.listdir(OUT):
        full = os.path.join(OUT, entry)
        if os.path.isdir(full):
            for f in os.listdir(full):
                os.remove(os.path.join(full, f))
            os.rmdir(full)

    print(flush=True)
    print("  the seed is at: %s" % final, flush=True)
    print(flush=True)

    boundaries = plan["boundaries"]
    print("  the run, in order (| marks where a pack opens more):", flush=True)
    seen_mechanics = set()
    for i, level_id in enumerate(plan["slots"]):
        if i in boundaries:
            print("     %s" % ("-" * 58), flush=True)
        need = sorted(abilities_of.get(level_id, ()))
        new = [a for a in need if a not in seen_mechanics]
        seen_mechanics.update(need)
        tag = "" if level_id in ALREADY_SEEN else "  NEW TO YOU"
        first = ("   first %s" % ", ".join(new)) if new else ""
        print("     %2d  %-34s %-26s%s%s"
              % (i, level_id, ",".join(need) or "-", tag, first), flush=True)

    print(flush=True)
    print("  %d of the 12 mechanics appear across the run: %s"
          % (len(seen_mechanics), ", ".join(sorted(seen_mechanics))), flush=True)
    early_new = sum(1 for l in plan["slots"][:EARLY] if l not in ALREADY_SEEN)
    print("  %d of the first %d cards are levels you have not played yet."
          % (early_new, EARLY), flush=True)
    print(flush=True)
    print("  To play it:", flush=True)
    print("    1. host the .zip (or upload to archipelago.gg)", flush=True)
    print("    2. in game, Archipelago -> slot name '%s'" % SLOT, flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
