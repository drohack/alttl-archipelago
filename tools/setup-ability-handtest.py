"""Set up hand tests for levels whose declared abilities look wrong.

WHAT THE HARNESS FOUND AND CANNOT SETTLE. tools/probe-level-requirements.py
swept all 36 non-DLC levels with more than one controller, forcing controllers
one at a time and stopping the moment LevelComplete fired. Anything not yet
forced at that point was provably not needed to COMPLETE the level. Seven
levels came out declaring an ability the completion never used.

That result is one-sided on purpose - it proves an ability was unnecessary,
never that the others are necessary - and it is blind to one thing that
matters enormously: PHYSICAL ACCESS. Forcing a controller's flag reaches
objects a player cannot. So the seven split in two, and only a person can tell
them apart.

STAGE 1, THE DRAWERS - expected to FAIL, which is the useful outcome.
MedicineCabinet, Paper Plane Supplies, Tool Drawer and Bathroom Drawer all
completed without their DrawerController. Almost certainly an artifact: the
harness rearranged the contents without ever opening the drawer. The project
already knows this shape - test_a_drawer_cannot_be_emptied_before_it_opens
exists because 0.3.0 logic said Tool Drawer's 47 draggables needed nothing
while the game kept the drawer shut. If you CANNOT finish these without
Drawer, the table is right and the harness is limited. If you CAN, that is a
real finding.

STAGE 2, THE REAL CANDIDATES - expected to SUCCEED if the tables overstate.
None is access-gated; all three are arrangement mechanics:

    Spoons          declares Ordering + Stacking, finished without Ordering
    Fruit Stickers  declares Sticking + Tidying, finished without Sticking
    Coins 1 (Shape) declares Ordering + Stacking, finished without Stacking

Each is granted its declared set MINUS the suspect one. Finishing the level
anyway means the requirement is overstated - safe, never unwinnable, but it
gates progress harder than the game does.

STAGE 3, THE EIGHT THE HARNESS CANNOT FINISH AT ALL. These never complete
under a forced solve, so nothing is known about their requirements:

    Radial Dance Party  registers ZERO controllers at boot - nothing to force
    PawPrints           phased; PawPrintsPhaseLevel never raises LevelComplete
    Sharp Pencils       Removables - a sequence, not an arrangement
    Record Player       Rotateables + GenericLevelObjects
    Wilting Flowers     AnimScrubbables - scrub an animation
    Lamp                Toggleables - switches
    Desktop Computer    already on KNOWN_UNFORCEABLE
    TupperwareNesting   phased

Granted their FULL declared set. The question here is only "can a person
finish this at all, holding exactly what the table demands" - which also
catches the dangerous direction, a table that demands too FEW abilities and
leaves objects dimmed forever.

    py -3.13 tools/setup-ability-handtest.py             # stage 1
    py -3.13 tools/setup-ability-handtest.py --stage2
    py -3.13 tools/setup-ability-handtest.py --stage3

Each stage grants a different ability set, so they must not be mixed - the
whole point is what you are NOT holding. Stages 2 and 3 append to the running
server's command file, so nothing from stage 1 needs restarting.

Record results in docs/manual-ability-test.md.

CLEANING UP - nothing here does it for you:

    py -3.13 tools/harness_env.py --restore-latest
"""
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import close_game, set_config, take_snapshot

import release_e2e as e2e

REPO = e2e.REPO
CMDS = os.path.join(REPO, "testserver", "abilitytest-commands.txt")
OUT = os.path.join(REPO, "testserver", "out-abilitytest")
YAML = os.path.join(REPO, "testserver", "yaml-abilitytest")
SERVER_LOG = os.path.join(REPO, "testserver",
                          "abilitytest-server.log")

#: level -> the ability to WITHHOLD, or None to grant the declared set whole.
STAGE1 = {
    "MedicineCabinet": "Drawer",
    "NeatStreak_Paper Plane Supplies": "Drawer",
    "NeatStreak_Tool Drawer": "Drawer",
    "NeatStreak_Bathroom Drawer": "Drawer",
}
STAGE2 = {
    "Spoons": "Ordering",
    "Fruit Stickers": "Sticking",
    "Coins 1 (Shape)": "Stacking",
}
STAGE3 = {
    "Radial Dance Party": None,
    "PawPrints": None,
    "Sharp Pencils": None,
    "Record Player": None,
    "Wilting Flowers": None,
    "Lamp": None,
    "Desktop Computer": None,
    "TupperwareNesting": None,
}

#: One level per LOCKED CONTROLLER TYPE, tested ONE AT A TIME.
#:
#: THE QUESTION IS NOT WHETHER THINGS GO GREY - it is whether a greyed object
#: can still be moved. droha found Fruit Stickers' objects greyed and
#: draggable anyway while the mod reported every flag set (blocked=12,
#: dimmed=12). If that generalises the ability locks are cosmetic and every
#: gated check is earnable out of logic; if it is one controller type, it is a
#: narrow fix. One sticker level cannot tell those apart.
#:
#: HOW THIS IS RUN, after a first attempt that was badly designed. That
#: version generated a throwaway seed, granted nothing, and asked droha to
#: boot each level through DevTools while not completing anything - so the
#: levels were not in the level select at all, and the seed still contained
#: ability items the test depended on him not collecting. A test that needs
#: restraint to stay valid is not a test. Each level now goes in its own seed,
#: on the track, with every OTHER ability granted up front; the withheld one
#: is confirmed absent from the log afterwards rather than trusted.
#:
#: NO AUTOMATED VERSION EXISTS. DragObject has no OnDrag, so synthetic pointer
#: input never starts a drag - the same reason TupperwareTower has always
#: needed a hand test.
#: ONE SESSION, NOT TWELVE. Archipelago cannot un-send an item, so a test
#: that needs ability X absent cannot follow one that granted it - which is
#: why stages 2 and 3 run a level at a time. Granting NOTHING sidesteps that
#: entirely: every gated mechanic is locked at once, so all twelve levels can
#: be walked in one sitting.
#:
#: Ability items still exist in the seed's pool - they have to, because a
#: controller is only lockable if its ability is in the seed's catalogue, and
#: that only happens when some drawn level needs it. Completing levels can
#: therefore hand over an ability mid-session. That is checked in the log
#: afterwards rather than prevented by asking for restraint.
STAGE_DRAG = dict.fromkeys([
    "Calendar",                  # Stickables
    "Trim Plant",                # Pluckables
    "Books",                     # Shuffleables
    "Eggs",                      # DraggablesOrdered
    "Stacked Papers",            # StackablesZ
    "Cat Food Cans",             # StackablesY
    "Angled Image Frame",        # Rotateables
    "Rock Collection",           # GridPuzzle
    "Clover",                    # SymmetricalPlaceables
    "Breadtags",                 # Removables
    "Coins 2 (Dirtyness)",       # Dirtyables
    "SomethingEggstra Fridge",   # Containables
], "EVERYTHING")

PUZZLES = 10


def say(what):
    print(f"  {what}", flush=True)


def declared_abilities(level_id):
    """Every ability the tables demand for one level, parts and extras.

    The same union tools/setup-handtest.py computes, read from the same two
    files the generator reads, so a table change moves this with it.
    """
    with open(os.path.join(REPO, "apworld", "alttl", "data", "names.json"),
              encoding="utf-8") as f:
        names = json.load(f)
    with open(os.path.join(REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        levels = json.load(f)

    need = set()
    for level in levels["levels"]:
        if level["levelId"] == level_id:
            need |= set(level.get("extraAbilities") or [])
    entry = names["levels"].get(level_id)
    if entry:
        for part in entry["parts"].values():
            need |= set(part.get("abilities") or [])
    return sorted(need)


def send(line):
    os.makedirs(os.path.dirname(CMDS), exist_ok=True)
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def plan_for(stage):
    """What to grant per level, and what is deliberately withheld."""
    rows = []
    for level_id, withhold in stage.items():
        declared = declared_abilities(level_id)
        grant = ([] if withhold == "EVERYTHING"
                 else [a for a in declared if a != withhold])
        rows.append((level_id, declared, grant, withhold))
    return rows


def puzzles_for(stage_levels):
    """Enough slots to hold the named levels, and no more.

    ASKING FOR TWELVE NAMED LEVELS IN A FOURTEEN-PUZZLE SEED NEVER TERMINATES
    - it is twelve specific draws out of 111. The search ran 400 generations
    and found nothing, which is arithmetic rather than bad luck. A run has to
    be several times the size of the list for the draw to have room, and the
    packs are then sent up front so nothing sits locked behind them.
    """
    return max(PUZZLES, min(79, len(stage_levels) * 7))


def write_yaml(stage_levels):
    os.makedirs(YAML, exist_ok=True)
    for f in os.listdir(YAML):
        os.remove(os.path.join(YAML, f))
    # Everything open at once and no packs to earn: this is about one level at
    # a time, not about pacing. skip_count 0 so a Skip cannot quietly finish
    # the level under test and make it look solved.
    #
    # SIZED BY puzzles_for, NOT BY THE PUZZLES CONSTANT. This line used to read
    # PUZZLES, which is 10, while --drag asks for 12 NAMED levels - a seed of
    # ten can never contain twelve, so the search ran its full 400 generations
    # and exited. puzzles_for was written to prevent exactly that and its
    # docstring says so in as many words; it simply was never called from here.
    # The cost of the bug was minutes of silence ending in a failure that looks
    # like bad luck, which is why it survived.
    count = puzzles_for(stage_levels)
    with open(os.path.join(YAML, "handtest.yaml"), "w", newline="\n") as f:
        f.write(
            f"name: {e2e.SLOT}\n"
            "game: A Little to the Left\n"
            "requires:\n"
            "  version: 0.6.7\n"
            "A Little to the Left:\n"
            f"  puzzle_count: {count}\n"
            f"  levels_to_beat: {count}\n"
            "  pack_size: 10\n"
            f"  guaranteed_open_slots: 10\n"
            "  seeing_stars: false\n"
            "  cupboards_and_drawers: false\n"
            "  base_weight: 60\n"
            "  archive_weight: 40\n"
            "  generator_weight: 5\n"
            "  mechanic_coverage: 6\n"
            "  ability_locks: true\n"
            "  starting_abilities: 0\n"
            "  skip_count: 0\n"
            "  cat_trap_chance: 0\n"
            "  hint_coverage: 100\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n")


def generate_any():
    """Any seed at all - the levels under test are opened with boot:."""
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        os.remove(os.path.join(OUT, f))
    subprocess.run([sys.executable, "Generate.py",
                    "--player_files_path", YAML, "--outputpath", OUT,
                    "--seed", "777"],
                   cwd=e2e.AP, capture_output=True, text=True)
    zips = [f for f in os.listdir(OUT) if f.endswith(".zip")]
    if not zips:
        sys.exit("generation produced no seed")
    say(f"seed {zips[0]} (its contents do not matter; levels open with boot:)")
    return zips[0]


def generate_until_present(levels, attempts=400):
    """A seed holding every level of this stage.

    PRINTS A LINE PER DRAW, and that is not decoration. Each attempt is a
    whole Generate.py run, so a stage that takes forty draws sat silent for
    minutes with no way to tell generation from a hang - droha, watching this
    very run: "what is the test session and what is going on right now".
    A silent loop is indistinguishable from a broken one.
    """
    os.makedirs(OUT, exist_ok=True)
    for n in range(attempts):
        for f in os.listdir(OUT):
            os.remove(os.path.join(OUT, f))
        subprocess.run([sys.executable, "Generate.py",
                        "--player_files_path", YAML, "--outputpath", OUT,
                        "--seed", str(60000 + n)],
                       cwd=e2e.AP, capture_output=True, text=True)
        zips = [f for f in os.listdir(OUT) if f.endswith(".zip")]
        if not zips:
            say(f"seed {60000 + n} (draw {n + 1}/{attempts}): "
                f"generation produced nothing, trying the next")
            continue
        drawn = {name for _i, name in e2e.read_plan(OUT, zips[0])["slots"]}
        missing = [l for l in levels if l not in drawn]
        if not missing:
            say(f"seed {60000 + n} holds all {len(levels)} level(s)")
            return zips[0]
        # Name what is missing, not just the count: a level that is absent
        # from every draw is a stage that will never generate, and the only
        # way to see that is to watch the same name go past each time.
        say(f"seed {60000 + n} (draw {n + 1}/{attempts}): "
            f"{len(levels) - len(missing)}/{len(levels)} level(s), "
            f"still missing {', '.join(missing[:3])}"
            + (f" +{len(missing) - 3} more" if len(missing) > 3 else ""))
    sys.exit(f"no seed in {attempts} draws held every level of this stage. "
             f"Raise PUZZLES, or split the stage.")


def run_stage(stage, number):
    rows = plan_for(stage)
    print(f"[stage {number}] {len(rows)} level(s)", flush=True)
    for level_id, declared, grant, withhold in rows:
        note = (f"WITHHOLDING {withhold}" if withhold
                else "full declared set")
        say(f"{level_id}: declares {', '.join(declared) or 'nothing'} "
            f"-> granting {', '.join(grant) or 'nothing'}  [{note}]")

    write_yaml(list(stage))
    seed = generate_until_present(list(stage))

    # take_snapshot, NOT the Environment context manager - this script is
    # meant to EXIT with the server still up and the mod still pointed at
    # localhost, so droha only has to start the game. See the cleanup note in
    # the module docstring.
    close_game()
    snap = take_snapshot("ability-handtest")
    say(f"snapshot taken: {os.path.basename(snap)}")
    set_config("droha.alttl.archipelago.cfg", {
        "Host": "localhost", "Port": str(e2e.PORT),
        "SlotName": e2e.SLOT, "AutoConnect": "true"})

    open(CMDS, "w", encoding="utf-8").close()
    os.makedirs(os.path.dirname(SERVER_LOG), exist_ok=True)
    log_file = open(SERVER_LOG, "w", encoding="utf-8")
    say(f"starting the server on {e2e.PORT}")
    subprocess.Popen(
        f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
        f'--port {e2e.PORT} "{os.path.join(OUT, seed)}"',
        shell=True, cwd=e2e.AP, stdout=log_file, stderr=subprocess.STDOUT)
    for _ in range(40):
        if e2e.port_open():
            break
        time.sleep(1)
    if not e2e.port_open():
        sys.exit("the server never bound the port")

    # Queued now; the server hands them over the moment the game connects.
    for _level_id, _declared, grant, _withhold in rows:
        for ability in grant:
            send(f"/send {e2e.SLOT} {ability}")
    # EVERY PACK, UP FRONT. guaranteed_open_slots caps at 10, so on any run
    # longer than that the levels under test start locked behind packs -
    # droha hit exactly that in stage 1 and had to be sent four by hand.
    # Packs are not abilities, so this changes nothing the test measures.
    for _ in range(12):
        send(f"/send {e2e.SLOT} Progressive Puzzle Pack")

    granted = sorted({a for _l, _d, g, _w in rows for a in g})
    say("queued: " + (", ".join(granted) or "nothing"))
    withheld = sorted({w for _l, _d, _g, w in rows if w})
    if withheld:
        say("deliberately NOT granted: " + ", ".join(withheld))

    print("", flush=True)
    print("SERVER IS UP. Just start the game:", flush=True)
    print(f'  "{e2e.EXE}"', flush=True)
    print("", flush=True)
    print(f"It auto-connects as {e2e.SLOT}. Record results in "
          "docs/manual-ability-test.md.", flush=True)
    print("", flush=True)
    print("When finished:  py -3.13 tools/harness_env.py --restore-latest",
          flush=True)
    return 0


def main():
    # ONE LEVEL AT A TIME for everything except the drawers, and the reason is
    # a mistake this script made first time out. A stage grants the UNION of
    # its levels' sets, so putting Spoons (withhold Ordering) beside Coins 1
    # (withhold Stacking) grants Ordering for Coins 1 and Stacking for Spoons
    # - each test hands the other exactly the ability it was meant to be
    # missing, and both prove nothing. setup-handtest.py warns about this in
    # so many words: "holding Desktop Computer's four while testing the tower
    # would prove nothing about the tower's two".
    #
    # The drawers are the one coherent group: every one of them withholds
    # Drawer, and nothing in the union re-grants it.
    #
    # Stage 3 is per level for the other half of the same rule - granting the
    # union would hand each level MORE than its declared set, and a solve
    # performed while holding extra cannot rule out a table that demands too
    # few.
    # --drag runs the WHOLE list in one session: nothing is granted, so there
    # is no ability set to keep apart and no reason to split it up.
    if "--drag" in sys.argv:
        return run_stage(STAGE_DRAG, "drag")
    for flag, stage in (("--stage2", STAGE2), ("--stage3", STAGE3)):
        if flag in sys.argv:
            name = next((a for a in sys.argv if a in stage), None)
            if name is None:
                print(f"{flag} needs a level name. Choose one:", flush=True)
                for level_id in stage:
                    print(f'  py -3.13 tools/setup-ability-handtest.py '
                          f'{flag} "{level_id}"', flush=True)
                return 1
            return run_stage({name: stage[name]},
                             2 if flag == "--stage2" else 3)
    return run_stage(STAGE1, 1)


if __name__ == "__main__":
    sys.exit(main())
