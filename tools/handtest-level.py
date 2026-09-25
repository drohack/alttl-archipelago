"""Put one level in front of a person, holding exactly the abilities named.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/handtest-level.py <index> [ability ...]

    e.g.  tools/handtest-level.py 29 Sticking
          tools/handtest-level.py 1209 Grids

    --setup-only   serve the seed and stop; the player opens the game
    --locks-off    ability_locks false: the mod's lock code does nothing
    --boot         for a game already open: set 720p, boot the level
    --keep-game    leave the game running; switch seeds from the mod's pane
    --in-run       want the level IN the run's opening, to open from the track

    tools/handtest-level.py --grant Ability [Ability ...]
                   give the running session more abilities, no new seed

THE SERVER READS A COMMAND FILE, testserver/handlevel-commands.txt, emptied at
every setup. It is MultiServer's stdin: its console thread retries readline()
at end of file, so a line appended later is read like `tail -f`. --grant
appends `/send <slot> <Ability>` lines to it.

WHY THIS IS THE ONLY INSTRUMENT THAT HAS NEVER BEEN WRONG. Four automated
detectors were built on 2026-09-22 and three of them reported confident wrong
answers across dozens of levels: a collider census, a `freeze` walking the
wrong object list, and a `blocked` count that fused "gated" with "already
placed". Every real correction that day came from droha playing a level, and
the probes' only honest role was choosing WHICH level to play.

So this is deliberately small. It generates a seed granting exactly the
abilities asked for, serves it, launches, boots the level, and stops. What
happens next is a person trying to do the puzzle.

THE SEED. ability_locks ON, so the mod's real lock is what gates - not a
DevTools freeze, which only disables colliders and answered "unreachable" for
the wrong reason once already. starting_abilities 0 with start_inventory
naming the ones to hold, so what is held is KNOWN rather than drawn.

THE QUESTION IS ALWAYS THE SAME: holding only these, can the group be
FINISHED? Not touched, not dragged - finished. Reaching a piece and completing
an arrangement are different questions and only the first is physics.
"""
import json
import os
import subprocess
import sys
import time
import zipfile
import zlib

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402
from harness_env import close_game                         # noqa: E402

YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-handlevel")
OUT_DIR = os.path.join(e2e.REPO, "testserver", "out-handlevel")
CMDS = os.path.join(e2e.REPO, "testserver", "handlevel-commands.txt")
SERVER_LOG = os.path.join(e2e.REPO, "testserver", "logs", "handlevel-server.log")


def grant(abilities):
    """Append /send lines for the running server to pick up."""
    if not os.path.isfile(CMDS):
        print(f"FAIL: no command file at {CMDS} - set a session up first",
              flush=True)
        return 1
    if not e2e.port_open():
        print(f"FAIL: nothing is listening on {e2e.PORT}", flush=True)
        return 1
    with open(CMDS, "a", encoding="utf-8") as fh:
        for ability in abilities:
            fh.write(f"/send {e2e.SLOT} {ability}\n")
    print(f"Done: sent {', '.join(abilities)} to {e2e.SLOT}", flush=True)
    return 0


def yaml_for(abilities, locks=True):
    held = "\n".join(f"    {a}: 1" for a in abilities) or "    Skip: 0"
    return f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 40
  levels_to_beat: 40
  pack_size: 10
  ability_locks: {'true' if locks else 'false'}
  starting_abilities: 0
  cat_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  cupboards_and_drawers: true
  seeing_stars: true
  start_inventory:
{held}
"""


def slot_data(seed_zip):
    """A generated seed's slot_data."""
    sys.path.insert(0, e2e.AP)
    from Utils import restricted_loads                    # noqa: E402
    with zipfile.ZipFile(seed_zip) as zf:
        raw = zf.read(next(n for n in zf.namelist()
                           if n.endswith(".archipelago")))
    data = restricted_loads(zlib.decompress(raw[1:]))
    return next(iter(data["slot_data"].values()))


def starting_abilities(seed_zip):
    """The abilities a generated seed grants at the start."""
    return tuple(slot_data(seed_zip).get("starting_abilities", ()))


def run_contains(seed_zip, index):
    """Whether the level under test is one of the run's own slots.

    If it is, the mod relaunches it with the run's seed the moment a boot
    starts it, and the two launches together left a loading screen looping
    (Water Glasses, 2026-09-23). So a hand test wants it OUT of the run.
    """
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "levels.json"), encoding="utf-8") as fh:
        level_id = next((l["levelId"] for l in json.load(fh)["levels"]
                         if l["levelIndex"] == index), None)
    slots = slot_data(seed_zip).get("slots", [])
    return any((s.get("levelId") if isinstance(s, dict) else s) == level_id
               for s in slots)


def run_opens(seed_zip, index):
    """Whether the level is a slot in the run's opening (no pack needed)."""
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "levels.json"), encoding="utf-8") as fh:
        level_id = next((l["levelId"] for l in json.load(fh)["levels"]
                         if l["levelIndex"] == index), None)
    sd = slot_data(seed_zip)
    opening = (sd.get("pack_boundaries") or [0])[0]
    slots = sd.get("slots", [])[:opening]
    return any((s.get("levelId") if isinstance(s, dict) else s) == level_id
               for s in slots)


def seed_for(abilities):
    """One generation seed per held set, so each hand test has its own save.

    A single fixed seed gave every test the same save_ap_<slot>_<seed> file,
    which could only be cleared with the game closed.
    """
    return 20260922 + zlib.crc32("+".join(sorted(abilities)).encode()) % 100000


def main():
    # Flags stripped BEFORE the ability list, because Archipelago takes
    # start_inventory names literally: "--setup-only" went straight into the
    # yaml and generation died with "Item '--setup-only' ... is not a valid
    # item name. Did you mean 'Swapping' (25% sure)".
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--grant" in sys.argv:
        if not args:
            raise SystemExit(__doc__)
        return grant(args)
    if not args or not args[0].isdigit():
        raise SystemExit(__doc__)
    index = int(args[0])
    abilities = args[1:]

    # The player opened the game after --setup-only: set 720p, boot, stop.
    if "--boot" in sys.argv:
        # The game is running, so NOT before_launch (it deletes the log):
        # read once to skip everything already written.
        log = e2e.Log()
        log.new()
        return setres_and_boot(log, index, abilities, set_size=False)

    # --keep-game: the player switches seeds from the mod's own pane
    # (Disconnect, then Connect), which rebuilds the run from the new seed.
    # droha, 2026-09-23, after four restarts in a row: "doesn't just
    # connecting to a new server set the new seed/abilities?"
    keep = "--keep-game" in sys.argv
    # --in-run: the opposite of the default. For a level that is nearly
    # always drawn (the Seeing Stars generators), find a seed that OPENS it at
    # the start instead, and let the player open it from the track. droha:
    # "again do you need to generate a seed? can't you just load the level?"
    in_run = "--in-run" in sys.argv

    print(f"[1/5] clearing the way", flush=True)
    if not keep:
        close_game()
    pid = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"Get-NetTCPConnection -LocalPort {e2e.PORT} -State Listen "
         "-ErrorAction SilentlyContinue | Select-Object -First 1 "
         "-ExpandProperty OwningProcess"],
        capture_output=True, text=True).stdout.strip()
    if pid.isdigit():
        subprocess.run(["powershell", "-NoProfile", "-Command",
                        f"Stop-Process -Id {pid} -Force"], capture_output=True)
        time.sleep(2)
    if not keep:
        for name in list(os.listdir(e2e.SAVE_DIR)):
            if name.startswith("save_ap_") or name == "alttl-last-session.json":
                os.remove(os.path.join(e2e.SAVE_DIR, name))

    print(f"[2/5] generating: holding {abilities or ['nothing']}", flush=True)
    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "h.yaml"), "w", encoding="utf-8") as fh:
        fh.write(yaml_for(abilities, locks="--locks-off" not in sys.argv))

    # HOLDING EXACTLY WHAT WAS ASKED, checked in the seed itself. The
    # generator grants starting abilities when a run's opening is thin, and
    # starting_abilities: 0 does not stop that. On 2026-09-23 the Fridge seed
    # quietly granted Stacking - the one ability that run existed to withhold.
    # So read the seed's slot_data, and move to the next seed if it grants
    # anything beyond the request.
    seed = None
    for attempt in range(80):
        for name in os.listdir(OUT_DIR):
            os.remove(os.path.join(OUT_DIR, name))
        r = subprocess.run(
            [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
             "--outputpath", OUT_DIR,
             "--seed", str(seed_for(abilities) + attempt)],
            cwd=e2e.AP, capture_output=True, text=True,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
        if r.returncode:
            print(r.stdout[-1200:], flush=True)
            print(r.stderr[-1200:], flush=True)
            return 1
        candidate = os.path.join(
            OUT_DIR, [f for f in os.listdir(OUT_DIR) if f.endswith(".zip")][0])
        granted = set(starting_abilities(candidate))
        extra = sorted(granted - set(abilities))
        if extra:
            print(f"      seed {seed_for(abilities) + attempt} grants {extra} "
                  f"at the start - trying the next", flush=True)
            continue
        if in_run:
            # The level must be a slot the player can open from the track at
            # once - so nothing boots it and the mod cannot relaunch over it.
            if not run_opens(candidate, index):
                print(f"      seed {seed_for(abilities) + attempt} does not "
                      f"open level {index} at the start - trying the next",
                      flush=True)
                continue
        elif run_contains(candidate, index):
            print(f"      seed {seed_for(abilities) + attempt} has level "
                  f"{index} in its run - trying the next", flush=True)
            continue
        seed = candidate
        break
    if seed is None:
        print("FAIL: 80 seeds all granted extra abilities or had the level in their run",
              flush=True)
        return 1

    # A fresh save for THIS seed, whichever mode. Each held set has its own
    # seed, so this never touches the save the running game is using.
    seed_name = os.path.basename(seed)[len("AP_"):-len(".zip")]
    for name in list(os.listdir(e2e.SAVE_DIR)):
        if name.startswith("save_ap_") and seed_name in name:
            os.remove(os.path.join(e2e.SAVE_DIR, name))

    print("[3/5] serving", flush=True)
    logs = os.path.join(e2e.REPO, "testserver", "logs")
    flags = (subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP) \
        if os.name == "nt" else 0
    # An EMPTY command file as stdin, so nothing old is replayed and --grant
    # can reach this server later.
    open(CMDS, "w", encoding="utf-8").close()
    subprocess.Popen(
        [sys.executable, "-u", "MultiServer.py", "--port", str(e2e.PORT), seed],
        cwd=e2e.AP, stdout=open(SERVER_LOG, "w"),
        stderr=open(os.path.join(logs, "handlevel-server.err"), "w"),
        stdin=open(CMDS, "r", encoding="utf-8"), creationflags=flags,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    for _ in range(60):
        if e2e.port_open():
            break
        time.sleep(1)
    e2e.write_config()
    e2e.write_devtools_config()

    if "--setup-only" in sys.argv:
        print("", flush=True)
        print(f"  server up on localhost:{e2e.PORT}, slot {e2e.SLOT!r}, "
              f"auto-connect ON", flush=True)
        print(f"  holding: {', '.join(abilities) or 'nothing'}", flush=True)
        if keep:
            print("  in the game: Archipelago pane -> Disconnect -> Connect",
                  flush=True)
        else:
            print(f"  open the game yourself; it connects on its own.",
                  flush=True)
        print(f"  then: tools/handtest-level.py {index} "
              f"{' '.join(abilities)} --boot  (720p, then the level)",
              flush=True)
        print("", flush=True)
        print("Done: session ready, game NOT launched.", flush=True)
        return 0

    print("[4/5] launching", flush=True)
    log = e2e.Log()
    log.before_launch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    got = ""
    end = time.time() + 240
    while time.time() < end:
        got += log.new()
        if "connected." in got:
            break
        time.sleep(1)
    print("      " + next((l.split("] ")[-1] for l in got.splitlines()
                           if "connected." in l), "NOT CONNECTED"), flush=True)
    time.sleep(8)

    # 720p FOR EVERY TEST SESSION, by SIZE and not by index.
    #
    # playerPrefs.resolution is a position in a list the game rebuilds per
    # monitor, so the same number is 1280x720 on one display and 1920x1080 on
    # another - which is exactly why droha kept setting 720p and getting
    # something else. The setting was never failing to persist; a
    # non-portable number was persisting perfectly. `setres` takes a literal
    # size and sidesteps the whole problem.
    #
    # Done AFTER connect so the mod's settings sync has already run, and left
    # to the player's own next change to undo - droha: "i don't care if your
    # tests set it back. but i just don't want it at 4k".
    return setres_and_boot(log, index, abilities)


def setres_and_boot(log, index, abilities, set_size=True):
    """720p, then the level. Shared by a launch and by --boot.

    --boot exists because --setup-only leaves the launch to the player, and
    that path used to skip this step entirely: droha opened the game for a
    hand test on 2026-09-23 and got 4K again.
    """
    # A game the PLAYER opened keeps the player's size (droha set 1920x1080);
    # only a launch this tool made is forced to 720p.
    if set_size:
        print("[5/5] setting the window to 720p", flush=True)
        e2e.dev("setres:1280x720", settle=3.0)
        for line in log.new().splitlines():
            if "setres" in line:
                print("      " + line.split("] ", 1)[-1], flush=True)

    # TITLE FIRST. Booting over a FINISHED level loads the new one twice, and
    # one copy can survive every cleanup until the game restarts (Spoons and
    # Record Player, 2026-09-23). From the title a boot has always been clean.
    print(f"      booting index {index} (via the title)", flush=True)
    log.new()
    e2e.dev("menu:title", settle=3.0)
    e2e.dev(f"boot:{index}", settle=9.0)
    e2e.dev("locks", settle=3.0)
    for line in log.new().splitlines():
        if "waiting on" in line or "locks: " in line:
            print("      " + line.split("] ", 1)[-1], flush=True)

    # EXACTLY ONE LEVEL, or nobody plays. 2026-09-23: a --boot from the title
    # screen left two MedicineCabinet(Clone)s alive, and droha found it before
    # any tool did: "i've got multiple levels loaded again.... please stop
    # doing that". Two live copies means two sets of drop targets, so any
    # verdict from that session would be worthless.
    e2e.dev("livelevels", settle=2.0)
    count = None
    for line in log.new().splitlines():
        if "livelevels: " in line:
            print("      " + line.split("] ", 1)[-1], flush=True)
            try:
                count = int(line.split("livelevels: ", 1)[1].split()[0])
            except (IndexError, ValueError):
                count = None
    if count != 1:
        print("", flush=True)
        print(f"FAIL: {count} levels are loaded, not 1 - do NOT play this "
              f"session", flush=True)
        return 1

    print("", flush=True)
    print(f"  Holding: {', '.join(abilities) or 'nothing'}", flush=True)
    print("  Can each group be FINISHED with only that? Not touched - "
          "finished.", flush=True)
    print("  More abilities mid-test: tools/handtest-level.py --grant "
          "<Ability>", flush=True)
    print("", flush=True)
    print("Done: game is running and waiting.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
