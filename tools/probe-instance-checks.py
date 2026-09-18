"""A generator drawn twice must pay out BOTH instances - and it does.

WHAT THIS WAS WRITTEN TO FIX, AND WHY NOTHING WAS FIXED. The project carried
"repeated-instance checks (#2 and up) have never been earned in any run" as a
known bug. The evidence was that across 712 "check:" lines in testserver/logs,
not one contained a "#". That evidence was worthless: every one of those runs
came from testserver/yaml-e2e/e2e.yaml, which sets generator_weight to 0, so
NO LEVEL EVER REPEATED IN ANY OF THEM. Absence of "#2" was absence of a second
instance, not absence of a payout.

Measured 2026-09-18 against the unmodified 0.4.0 build: a single-solution
generator drawn twice pays out "<Level> - Solution 1" for one instance and
"<Level> #2 - Solution 1" for the other. The mechanism works. Two candidate
fixes - per-slot solution records in the run state, and matching the loaded
RandomSeed against the slot's seed - were written, measured to change nothing,
and reverted rather than shipped on a theory.

TWO THINGS THE SAME MEASUREMENT ESTABLISHED, both of which shape this probe:

  - Checks.SeedSolutionsFromSave NEVER FIRES for generator levels. It logs
    "already had N solution(s)" when it does, and across a full run of this
    probe it logged none. So the cross-instance collision it was supposed to
    cause - one instance adopting another's solutionIds, because the game's
    save is keyed by level id - does not happen for the levels that can
    repeat. It does fire for ordinary levels (11 times across the saved logs),
    which is why it was left alone.

  - ONLY SINGLE-SOLUTION GENERATORS WORK HERE. The second visit has to land on
    the other instance, and ResolveSlotFor picks "the earliest open slot for
    this level with work left". A three-solution generator still has work left
    after a force-solve finds one arrangement, so the first instance wins again
    and both visits play the same slot - measured twice on Pencils
    (Randomized), which is what made an earlier version of this probe report a
    failure that was its own fault.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-instance-checks.py 2>/dev/null

Generates its own seed. Exits 0 only if both instances file their own
locations, and if neither was handed one merely for opening its card.
"""
import glob
import json
import os
import subprocess
import sys
import tempfile
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game

import release_e2e as e2e

CMDS = os.path.join(tempfile.gettempdir(), "alttl-instance-cmds.txt")
OUT = os.path.join(e2e.REPO, "testserver", "out-instance")
YAML = os.path.join(e2e.REPO, "testserver", "yaml-instance")

#: Generators only, and more slots than there are generator levels, so a
#: repeat is arithmetic rather than luck. pack_size caps at 10, so the back
#: half of the run opens on a Progressive Puzzle Pack sent below.
YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 20
  levels_to_beat: 20
  pack_size: 10
  guaranteed_open_slots: 10
  cupboards_and_drawers: false
  seeing_stars: false
  generator_weight: 100
  archive_weight: 0
  base_weight: 0
  archive_packs: []
  mechanic_coverage: 0
  ability_locks: true
  starting_abilities: 6
  skip_count: 0
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""

#: Reads what release_e2e.read_plan does not: the instance number and the
#: procedural seed, which are the two fields this probe is entirely about.
PLAN_CODE = """
import zipfile, zlib, json, sys
from Utils import restricted_loads
f = zipfile.ZipFile(sys.argv[1])
n = [x for x in f.namelist() if x.endswith('.archipelago')][0]
d = restricted_loads(zlib.decompress(f.read(n)[1:]))['slot_data'][1]
print(json.dumps([{'levelIndex': s['levelIndex'], 'levelId': s['levelId'],
                   'instance': s.get('instance', 1), 'seed': s.get('seed', -1)}
                  for s in d['slots']]))
"""


def send(item):
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write("/send droha %s\n" % item)


def read_slots(folder, seed_zip):
    r = subprocess.run([sys.executable, "-c", PLAN_CODE,
                        os.path.join(folder, seed_zip)],
                       cwd=e2e.AP, capture_output=True, text=True,
                       env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stderr[-1200:], flush=True)
        return None
    return json.loads(r.stdout.strip().splitlines()[-1])


def single_solution_generators():
    """Generator levels with exactly ONE solution.

    THE PROBE ONLY WORKS ON THESE, and finding that out cost two runs. The
    second visit has to land on the OTHER instance, and which slot a boot
    resolves to is decided by ResolveSlotFor: "the earliest open slot for this
    level with work left". A generator with three solutions still has work
    left after a force-solve finds one arrangement, so the first instance wins
    again and both visits play the same slot - measured on Pencils
    (Randomized), twice. A single-solution generator is finished outright by
    one visit, so the next boot has to move on.
    """
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "levels.json"), encoding="utf-8") as f:
        rows = json.load(f)["levels"]
    return {r["levelIndex"] for r in rows
            if r.get("isRandomizable") and r.get("solutionCount") == 1}


def generate():
    """A seed where some single-solution generator is drawn at least twice."""
    usable = single_solution_generators()
    for d in (OUT, YAML):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(YAML, "s.yaml"), "w", newline="\n") as f:
        f.write(YML.format(slot=e2e.SLOT))

    for n in range(25):
        for f in glob.glob(os.path.join(OUT, "*")):
            os.remove(f)
        subprocess.run([sys.executable, "Generate.py",
                        "--player_files_path", YAML, "--outputpath", OUT,
                        "--seed", str(8600 + n)],
                       cwd=e2e.AP, capture_output=True, text=True)
        zips = glob.glob(os.path.join(OUT, "*.zip"))
        if not zips:
            continue
        seed = os.path.basename(zips[0])
        slots = read_slots(OUT, seed)
        if slots is None:
            continue

        for i, first in enumerate(slots):
            if first["instance"] != 1 or first["seed"] < 0:
                continue
            if first["levelIndex"] not in usable:
                continue
            for j, second in enumerate(slots):
                if j == i or second["levelId"] != first["levelId"]:
                    continue
                if second["instance"] != 2 or second["seed"] < 0:
                    continue
                # Distinct seeds are what makes the two tellable apart at
                # runtime; identical ones would make the probe meaningless.
                if second["seed"] == first["seed"]:
                    continue
                print(f"      seed {8600 + n}: {first['levelId']} is slot {i} "
                      f"(instance 1, seed {first['seed']}) and slot {j} "
                      f"(instance 2, seed {second['seed']})", flush=True)
                filler = next((e["levelIndex"] for e in slots
                               if e["levelIndex"] != first["levelIndex"]),
                              None)
                return seed, first, second, filler
    return None, None, None, None


def play(log, index, what):
    """Boot a level and force-solve it, letting the MOD pick the instance.

    NOT boot:<index>:<seed>. Asking for a particular seed does not select a
    particular instance - measured: Track.BeforeStartLevel resolves the slot
    first and then overwrites randomSeed with THAT slot's seed, so a boot
    asking for instance 1's seed was rewritten to instance 2's and played
    instance 2. Which instance you get is the mod's decision, and reading it
    back out of the log is the honest way to find out.
    """
    print(f"      {what}: booting level {index}", flush=True)
    log.new()
    e2e.dev(f"boot:{index}", 7.0)
    for _ in range(8):
        e2e.dev("state", 0.4)
        out = log.wait(["state: gameState"], 8, 5, "the level")
        if "Gameplay_GameState" in out:
            break
        time.sleep(2.0)
    else:
        return False, ""
    done, text = e2e.solve_level(log)
    slots = [l.split("now playing slot", 1)[1].strip()
             for l in text.splitlines() if "now playing slot" in l]
    print(f"      {what}: completed={done}  slot={slots[-1] if slots else '?'}",
          flush=True)
    return done, text


def main():
    print("[1/6] generating a seed with a generator drawn twice", flush=True)
    seed, first, second, filler = generate()
    if seed is None:
        print("FAIL: no seed drew the same generator twice in 25 tries",
              flush=True)
        return 1

    open(CMDS, "w", encoding="utf-8").close()
    for p in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(p)

    rc = 1
    server = None
    close_game()
    with Environment("instance-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        try:
            print("[2/6] starting the server", flush=True)
            server = subprocess.Popen(
                f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
                f'--port {e2e.PORT} "{os.path.join(OUT, seed)}"',
                shell=True, cwd=e2e.AP,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)
            if not e2e.port_open():
                print("FAIL: the server never bound 38281", flush=True)
                return 1

            print("[3/6] launching the game", flush=True)
            log = e2e.Log()
            what_display, _windowed = e2e.describe_display()
            print(f"      {what_display}", flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            if "connected. " not in log.wait(["connected. "], 150, 3,
                                             "the connection"):
                print("FAIL: never connected", flush=True)
                return 1

            problem = e2e.patch_problem(e2e.whole_log())
            if problem:
                print(f"FAIL: the mod did not fully install: {problem}",
                      flush=True)
                return 1

            print("[4/6] opening the whole run so both slots are playable",
                  flush=True)
            log.new()
            send("Progressive Puzzle Pack")
            # EVERY ABILITY TOO, and that is not tidiness. The check this
            # probe cares most about - a location granted merely for opening
            # a card - is gated on reachability, and an ability the run does
            # not hold makes it unreachable, so the grant silently does not
            # happen and the probe reports green for the wrong reason.
            import json as _json
            with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                                   "abilities.json"), encoding="utf-8") as f:
                _ab = _json.load(f)
            for _name in list(_ab["abilities"]):
                send(_name)
            log.wait(["received item: Progressive Puzzle Pack"], 30, 4,
                     "the pack")
            time.sleep(4.0)

            print("[5/6] playing the level, something else, then the level "
                  "again", flush=True)
            # THE FILLER BOOT IS LOAD-BEARING. ResolveSlotFor tier 2 returns
            # "the slot already running" for a relaunch of the same level, so
            # booting one level twice in a row lands on the SAME instance both
            # times and the second play proves nothing. Leaving the level for
            # a different one breaks that continuity, and the next boot
            # resolves to the earliest open slot with work left - which, the
            # first instance now being finished, is the other one.
            seen = ""
            ok, text = play(log, first["levelIndex"], "first visit")
            seen += text
            if not ok:
                print("FAIL: the first visit never completed", flush=True)
                return 1

            if filler is None:
                print("FAIL: this seed holds no second level to step through",
                      flush=True)
                return 1
            print(f"      stepping through level {filler} to break the "
                  "relaunch continuity", flush=True)
            log.new()
            e2e.dev(f"boot:{filler}", 7.0)
            time.sleep(2.0)
            seen += log.new()

            ok, text = play(log, first["levelIndex"], "second visit")
            seen += text
            if not ok:
                print("FAIL: the second visit never completed", flush=True)
                return 1
            time.sleep(2.0)
            seen += log.new()

            print("[6/6] checking BOTH instances filed their own locations",
                  flush=True)
            level = first["levelId"]
            checks = [l.split("check: ", 1)[1].strip()
                      for l in seen.splitlines() if "check: " in l]
            for c in checks:
                if level in c:
                    print(f"      {c}", flush=True)

            plain = any(c.startswith(f"{level} - ") for c in checks)
            second_up = any(f"{level} #" in c for c in checks)
            print(f"      instance 1 location sent={plain}  "
                  f"instance 2 location sent={second_up}", flush=True)

            # NOTHING MAY BE GRANTED ON ENTRY. This is the sharp end of the
            # per-slot fix: seeding a slot's ordinals from the level-keyed
            # save made a repeated instance inherit the OTHER instance's
            # arrangements, and FileSolutionsAlreadyEarned then handed out
            # its early Solution locations the moment the card was opened -
            # a check the player never earned. Post-fix a fresh instance
            # starts empty, so no location may appear between entering the
            # slot and the first forced solve.
            unearned = []
            entered = False
            for line in seen.splitlines():
                if "now playing slot" in line:
                    entered = True
                elif "solve: forcing" in line:
                    entered = False
                elif entered and "check: " in line and level in line:
                    unearned.append(line.split("check: ", 1)[1].strip())
            if unearned:
                print("      granted on ENTRY, before any solve:", flush=True)
                for u in unearned:
                    print(f"        {u}", flush=True)

            if unearned:
                print("FAIL: a location was granted just for opening the card. "
                      "The instance inherited another instance's arrangements "
                      "from the level-keyed save.", flush=True)
            elif plain and second_up:
                print("PASS: two instances of one generator file separate "
                      "locations, and neither was granted on entry.",
                      flush=True)
                rc = 0
            else:
                print("FAIL: the two instances did not both pay out. Either "
                      "the ordinal counter was seeded from the level-keyed "
                      "save again, or both visits resolved to one slot.",
                      flush=True)
                for line in seen.splitlines():
                    if "now playing slot" in line:
                        print("   " + line.split("] ")[-1].strip(), flush=True)
        finally:
            close_game()
            if server:
                subprocess.run(["powershell", "-NoProfile", "-Command",
                                "Get-NetTCPConnection -LocalPort 38281 -State "
                                "Listen -ErrorAction SilentlyContinue | "
                                "ForEach-Object { Stop-Process -Id "
                                "$_.OwningProcess -Force }"],
                               capture_output=True)
            print("Done: game closed, server stopped", flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())
