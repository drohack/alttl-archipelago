"""Set up a seed for solving TupperwareTower and Desktop Computer BY HAND.

WHY THIS EXISTS. The release gate beats both of those levels with a Skip
rather than by solving them, because `solve:` sets a controller's flag and
that is not enough for either one. So the gate proves the Skip path and says
nothing about whether a person can finish the puzzle - and a Skip banks the
Beaten token, so a skipped level is indistinguishable from a solved one in
every count the gate prints. tools/release_e2e.py lists both in
KNOWN_UNFORCEABLE and docs/release-testing.md carries this as a manual item.

No harness can close that gap. A real solve needs the drag path: DragObject
has no OnDrag, so synthetic pointer input never starts the settle tween that
Snap() does. A person has to play it.

WHAT IS ACTUALLY BEING TESTED, and it is narrower than "does the level work".
droha solved TupperwareTower by hand once already, back when its abilities
were OVERSTATED - the level asked for more than it needed. Overstating is the
safe direction; it only gates the level harder. The table has since been
narrowed, and the dangerous direction is the other one: if a level's declared
abilities are now fewer than what it really needs, objects stay dimmed for a
player holding exactly the declared set and the puzzle cannot be finished at
all. A solve performed while holding EXTRA abilities cannot rule that out, so
the old playthrough does not settle it.

Hence the shape of this script: grant exactly the declared abilities and
nothing else, then hand it over. If the level completes on those, the
declaration is sufficient. Measured from the shipped tables:

    TupperwareTower   Stacking, Grids
    Desktop Computer  Containers, Gadgets, Rotating, Swapping

Grids is the level's extraAbilities entry rather than a part requirement - the
falling blocks are dimmed without it - which is exactly the kind of thing a
narrowed table can lose.

TWO STAGES, because the two sets must not be mixed. Holding Desktop
Computer's four while testing the tower would prove nothing about the tower's
two. So this grants the tower's abilities only, and stage two is one line:

    py -3.13 tools/setup-handtest.py --stage2

which appends the other four to the server's command file. The server reads
that file through `tail -f`, so stage two needs nothing still running from
stage one.

WHAT IT LEAVES BEHIND - deliberately, unlike every other harness here:

  - a seed containing both levels, all of it open at once, no packs to earn
  - the server up on 38281, reading testserver/handtest-commands.txt
  - the game running and connected, holding Stacking and Grids and nothing
    else
  - skip_count 0, so a Skip cannot quietly finish the level being tested and
    make it look solved

CLEANING UP AFTERWARDS. Nothing here does it for you:

    py -3.13 tools/harness_env.py --restore-latest

Until that runs, the mod points at localhost instead of your real server.
"""
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import (close_game, ensure_no_steam_relaunch,
                         set_config, take_snapshot)

import release_e2e as e2e

REPO = e2e.REPO
CMDS = os.path.join(REPO, "testserver", "handtest-commands.txt")
OUT = os.path.join(REPO, "testserver", "out-handtest")
SERVER_LOG = os.path.join(REPO, "testserver", "handtest-server.log")
DUMP = os.path.join(e2e.GAME, "BepInEx", "alttl-dump.json")

#: The levels under test, and the abilities the shipped tables say each needs.
#: Read back from the data at runtime rather than trusted here - see
#: declared_abilities() - so this cannot drift from what actually ships.
TARGETS = ("TupperwareTower", "Desktop Computer")

PUZZLES = 10
DESKTOP = 1


def say(what):
    print(f"-- {what}", flush=True)


def declared_abilities(level_id):
    """Every ability the tables demand for one level, parts and extras.

    The union a player must hold to touch every object on the level. Read from
    the same two files the generator reads, so a table change moves this.
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
    for part in names["levels"][level_id]["parts"].values():
        need |= set(part.get("abilities") or [])
    return sorted(need)


def send(line):
    """Append a console command for the server, which is tailing this file."""
    os.makedirs(os.path.dirname(CMDS), exist_ok=True)
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def grant(abilities, note):
    for ability in abilities:
        send(f"/send {e2e.SLOT} {ability}")
    say(f"granted {note}: {', '.join(abilities)}")


def write_yaml(yaml_dir):
    """A run that is nothing but open campaign levels.

    base_weight alone, because both targets are hand-made campaign puzzles and
    a generator-heavy draw would rarely include either. pack_size at the
    puzzle count so the whole run opens at once - the point is to reach two
    specific levels, not to earn the right to. skip_count 0 so a Skip cannot
    finish the level under test and make an unsolvable puzzle look solved.
    """
    os.makedirs(yaml_dir, exist_ok=True)
    with open(os.path.join(yaml_dir, "handtest.yaml"), "w",
              encoding="utf-8", newline="\n") as f:
        f.write(
            f"name: {e2e.SLOT}\n"
            "game: A Little to the Left\n"
            "description: hand-test seed\n"
            "requires:\n  version: 0.6.7\n"
            "A Little to the Left:\n"
            "  goal: beat_levels\n"
            "  levels_to_beat: 1\n"
            f"  puzzle_count: {PUZZLES}\n"
            f"  pack_size: {PUZZLES}\n"
            "  generator_weight: 0\n"
            "  archive_weight: 0\n"
            "  base_weight: 100\n"
            "  mechanic_coverage: 0\n"
            "  ability_locks: true\n"
            "  starting_abilities: 0\n"
            f"  guaranteed_open_slots: {min(PUZZLES, 10)}\n"
            "  skip_count: 0\n"
            "  hint_coverage: 0\n"
            "  cat_trap_chance: 0\n"
            "  progression_balancing: 0\n"
            "  accessibility: full\n")


def generate_until_both(yaml_dir, attempts=600):
    """Roll seeds until one draw contains both levels.

    There is no option that pins a specific level into a run, and adding one
    to the world for a test harness would be worse than rolling: the draw is
    the thing under test everywhere else. Generation takes about a tenth of a
    second, so a few hundred tries is cheaper than a feature.
    """
    os.makedirs(OUT, exist_ok=True)
    for old in os.listdir(OUT):
        if old.endswith((".zip", ".apsave")):
            os.remove(os.path.join(OUT, old))

    for attempt in range(1, attempts + 1):
        for stale in os.listdir(OUT):
            if stale.endswith(".zip"):
                os.remove(os.path.join(OUT, stale))
        r = subprocess.run(
            [sys.executable, "Generate.py", "--player_files_path", yaml_dir,
             "--outputpath", OUT, "--seed", str(70000 + attempt)],
            cwd=e2e.AP, capture_output=True, text=True,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
        if r.returncode != 0:
            print(r.stdout[-1500:], flush=True)
            sys.exit("generation failed")

        zips = [f for f in os.listdir(OUT) if f.endswith(".zip")]
        if not zips:
            sys.exit("generation produced no seed")
        plan = e2e.read_plan(OUT, zips[0])
        drawn = {name for _, name in plan["slots"]}
        if all(t in drawn for t in TARGETS):
            say(f"seed {zips[0]} has both, after {attempt} draw(s)")
            return zips[0], plan
        if attempt % 50 == 0:
            say(f"draw {attempt}/{attempts}: not both yet, still rolling")

    sys.exit(f"no seed in {attempts} draws held both of {TARGETS}")


def report_dlc():
    """What the GAME says about the DLCs, which is the only real authority.

    Steam's appmanifest lists what is INSTALLED. DLCManager.DLCInfo carries
    what the running game can actually load, which is the question that
    matters for whether a DLC level could ever be placed in a run.
    """
    if not e2e.dev("dump", 3.0):
        say("WARNING: the dump command never completed")
        return
    for _ in range(20):
        if os.path.exists(DUMP):
            break
        time.sleep(0.5)
    try:
        with open(DUMP, encoding="utf-8") as f:
            data = json.load(f)
    except Exception as exc:
        say(f"WARNING: could not read the dump ({exc})")
        return

    print("   DLC, as the running game reports it:", flush=True)
    for entry in data.get("dlc", []):
        print(f"      {entry.get('name', '?')} "
              f"(appId {entry.get('appId', '?')}): "
              f"installed={entry.get('installed', '?')}, "
              f"{entry.get('levelCount', '?')} level(s)", flush=True)


def stage2():
    """Swap the granted set over to Desktop Computer's, on a running server."""
    need = declared_abilities("Desktop Computer")
    if not os.path.exists(CMDS):
        sys.exit(f"no command file at {CMDS} - run stage one first")
    grant(need, "Desktop Computer's declared abilities")
    print("Done: play Desktop Computer now. It needs "
          f"{', '.join(need)} and nothing else.", flush=True)
    return 0


def main():
    if "--stage2" in sys.argv:
        return stage2()

    close_game()
    tower = declared_abilities("TupperwareTower")
    computer = declared_abilities("Desktop Computer")
    say(f"TupperwareTower declares {', '.join(tower)}")
    say(f"Desktop Computer declares {', '.join(computer)}")

    # take_snapshot, NOT the Environment context manager. Environment puts
    # the config back when it exits, and this script is meant to exit with the
    # game still connected to localhost - see the cleanup note in the module
    # docstring.
    snap = take_snapshot("setup-handtest")
    say(f"snapshot taken: {os.path.basename(snap)}")
    set_config("droha.alttl.archipelago.cfg", {
        "Host": "localhost", "Port": str(e2e.PORT),
        "SlotName": e2e.SLOT, "AutoConnect": "true"})
    set_config("droha.alttl.devtools.cfg", {
        "MuteAudio": "true", "TargetVirtualDesktop": str(DESKTOP),
        "RaiseWindowAtStartup": "false"})

    say("rolling seeds until one holds both levels")
    yaml_dir = os.path.join(REPO, "testserver", "yaml-handtest")
    write_yaml(yaml_dir)
    seed, plan = generate_until_both(yaml_dir)

    open(CMDS, "w", encoding="utf-8").close()
    os.makedirs(os.path.dirname(SERVER_LOG), exist_ok=True)
    log_file = open(SERVER_LOG, "w", encoding="utf-8")
    say(f"starting the server on {e2e.PORT}")
    subprocess.Popen(
        f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
        f'--port {e2e.PORT} "{os.path.join(OUT, seed)}"',
        shell=True, cwd=e2e.AP, stdout=log_file,
        stderr=subprocess.STDOUT)
    for _ in range(40):
        if e2e.port_open():
            break
        time.sleep(1)
    if not e2e.port_open():
        sys.exit("the server never bound the port")

    say("launching the game")
    ensure_no_steam_relaunch()
    log = e2e.Log()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    log.wait(["connected. ", "Archipelago refused"], 180, 5,
             "the connection")
    time.sleep(8.0)
    say("connected")

    report_dlc()
    grant(tower, "TupperwareTower's declared abilities")
    time.sleep(3.0)

    slots = {name: i for i, (_, name) in enumerate(plan["slots"])}
    print("", flush=True)
    print("THE RUN IS OPEN AND WAITING. Every puzzle is playable; the two "
          "under test are:", flush=True)
    for target in TARGETS:
        print(f"   slot {slots.get(target, '?')}  {target}", flush=True)
    print("", flush=True)
    print(f"STAGE 1, now: play TupperwareTower holding only "
          f"{', '.join(tower)}.", flush=True)
    print("   It completes           -> the declared abilities are enough.",
          flush=True)
    print("   Objects stay dimmed    -> the table is understated, and the "
          "level cannot be finished by a player who holds exactly what it "
          "asks for.", flush=True)
    print("", flush=True)
    print("STAGE 2, when the tower is done:", flush=True)
    print("   py -3.13 tools/setup-handtest.py --stage2", flush=True)
    print(f"   then play Desktop Computer, which declares "
          f"{', '.join(computer)}.", flush=True)
    print("", flush=True)
    print("AFTERWARDS, to get your own server settings back:", flush=True)
    print("   py -3.13 tools/harness_env.py --restore-latest", flush=True)
    print("", flush=True)
    print(f"Done: seed {seed} served on {e2e.PORT}, game connected as "
          f"{e2e.SLOT}, holding {', '.join(tower)}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
