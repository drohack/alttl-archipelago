"""What a Skip does on a puzzle that is already beaten.

TWO CLAIMS WENT IN AND ONE CAME OUT. This project believed, in a comment in
release_e2e.py and in docs/manual-container-test.md, that a Skip spent on an
already-beaten puzzle was consumed and granted nothing - because "a beaten
level never fires LevelComplete again". Measured 2026-09-18 against a build
with the pre-fix Skips.cs: IT DOES FIRE. Beating DLC1 Filing Cabinet,
re-entering it and skipping logged LevelCompleteEarly, LevelComplete,
Solutions 2 and 3, then LevelSkipped. That premise was wrong, and had to be -
the only way to press Skip is to be standing in a loaded level.

THE DEFECT THAT IS REAL, and that the same measurement found: a Skip was
consumed on a slot with NOTHING LEFT TO GRANT. The old build logged
"skip: spent one, 0 left" and then "sent 0 remaining location(s)" - an item
spent, written to disk with no refund path, for nothing at all. The spend site
now refuses instead.

WHY THIS EXACT SCENARIO. "Beaten but not fully checked" is a real state, not a
contrived one: a puzzle whose Beaten token is banked while a container group -
a drawer, a cupboard door, a lid - stays unearned because nothing forced it
open. The release gate hit it for real, stalling a DLC run at 5 of 8 puzzles
with the only Progressive Puzzle Pack sitting on Game Pieces' Drawers, and
docs/manual-container-test.md recorded that a Skip "would have released the
pack" - which was not true at the time. This probe is the thing that makes
that sentence true and keeps it true.

Two assertions:

  1. a Skip spent on a beaten-but-incomplete slot sends its remaining
     locations - this held BEFORE the fix too, and is pinned so the
     behaviour cannot quietly regress, and
  2. a Skip is REFUSED, not consumed, on a slot with nothing left to find -
     this is the one that fails on the pre-fix build.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-skip-beaten.py 2>/dev/null
    ... --target pencils   a GENERATOR level, and the harness's own Skip path
    ... --target pencils --unbeaten   one Skip on it without beating it first

--target pencils is the 2026-09-24 gate failure on one level: forcing a
beaten generator level re-completes it, the mod moves on, and a Skip sent
then lands on the next level. It checks that release_e2e's pre-Skip check
catches that order, and that the harness's order (open, check, Skip - no
forcing) releases Pencils Solution 2.

Generates its own seed, so it does not depend on the gate having run. Exits 0
only if both hold.
"""
import glob
import os
import subprocess
import sys
import tempfile
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game

import release_e2e as e2e

CMDS = os.path.join(tempfile.gettempdir(), "alttl-skipbeaten-cmds.txt")
OUT = os.path.join(e2e.REPO, "testserver", "out-skipbeaten")
YAML = os.path.join(e2e.REPO, "testserver", "yaml-skipbeaten")

#: The puzzle to probe with, and a location that survives beating it.
#:
#: A MULTI-SOLUTION LEVEL, deliberately. The first candidate was Game Pieces,
#: on the theory that its Drawers group cannot be forced open - and measured,
#: it can: the group fired immediately and the level came out fully collected,
#: so there was nothing left for a Skip to grant and the probe proved nothing.
#: That matches droha's hand test, which already disproved the "dead drawer"
#: reading.
#:
#: Filing Cabinet has three solutions and one controller. Force-solving finds
#: ONE arrangement, which beats the level and banks its token while Solutions
#: 2 and 3 stay owed - exactly "beaten but not fully checked", reached without
#: depending on anything being unforceable.
TARGET = "DLC1 Filing Cabinet"
OWED = "Filing Cabinet (Cupboards and Drawers) - Solution 2"

#: --target pencils: a generator level with two solutions. It re-randomizes
#: on every open, so forcing it after it is beaten completes it again.
PENCILS = "Pencils (Randomized)"
PENCILS_OWED = "Pencils (Randomized) - Solution 2"

YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 15
  levels_to_beat: 15
  pack_size: 10
  guaranteed_open_slots: 10
  cupboards_and_drawers: true
  cupboards_weight: 100
  seeing_stars: false
  generator_weight: 0
  archive_weight: 0
  base_weight: 0
  archive_packs: []
  mechanic_coverage: 0
  ability_locks: false
  skip_count: 5
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""


PENCILS_YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 15
  levels_to_beat: 15
  pack_size: 10
  guaranteed_open_slots: 10
  generator_weight: 100
  archive_weight: 0
  base_weight: 0
  archive_packs: []
  mechanic_coverage: 0
  ability_locks: false
  skip_count: 5
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""


def send(item):
    with open(CMDS, "a", encoding="utf-8") as f:
        f.write("/send droha %s\n" % item)


def generate(yml=YML, target=None):
    """A seed holding the target level, rolled until one does."""
    target = target or TARGET
    for d in (OUT, YAML):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(YAML, "s.yaml"), "w", newline="\n") as f:
        f.write(yml.format(slot=e2e.SLOT))

    for n in range(60):
        for f in glob.glob(os.path.join(OUT, "*")):
            os.remove(f)
        subprocess.run([sys.executable, "Generate.py",
                        "--player_files_path", YAML, "--outputpath", OUT,
                        "--seed", str(8300 + n)],
                       cwd=e2e.AP, capture_output=True, text=True)
        zips = glob.glob(os.path.join(OUT, "*.zip"))
        if not zips:
            continue
        seed = os.path.basename(zips[0])
        plan = e2e.read_plan(OUT, seed)
        for i, (index, name) in enumerate(plan["slots"]):
            if name == target:
                print(f"      seed {8300 + n} holds {target} as slot {i}",
                      flush=True)
                return seed, i, index
    return None, -1, -1


def main():
    global TARGET, OWED
    pencils = "--target" in sys.argv and "pencils" in sys.argv
    if pencils:
        TARGET, OWED = PENCILS, PENCILS_OWED
    print(f"[1/8] generating a seed that contains {TARGET}", flush=True)
    seed, slot, index = generate(PENCILS_YML if pencils else YML, TARGET)
    if seed is None:
        print(f"FAIL: no seed drew {TARGET} in 60 tries", flush=True)
        return 1

    open(CMDS, "w", encoding="utf-8").close()
    # MultiServer's record of what it already sent. Left in place, a rerun
    # replays every item and the probe measures a different starting state.
    for p in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(p)

    rc = 1
    server = None
    close_game()
    with Environment("skip-beaten-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        try:
            print("[2/8] starting the server", flush=True)
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

            print("[3/8] launching the game", flush=True)
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

            print("[4/8] sending two Skips so supply is never the variable",
                  flush=True)
            log.new()
            send("Skip")
            send("Skip")
            if "received item: Skip" not in log.wait(["received item: Skip"],
                                                     30, 4, "the skip items"):
                print("FAIL: no Skip arrived", flush=True)
                return 1
            time.sleep(2.0)

            if "--unbeaten" in sys.argv:
                # Does the game's own Skip complete this level at all, beaten
                # or not? One Skip on the fresh level, and what follows it.
                print(f"[5/8] --unbeaten: one Skip on {TARGET}, never beaten",
                      flush=True)
                if not e2e.boot_level(log, index)[0]:
                    print(f"FAIL: {TARGET} did not open", flush=True)
                    return 1
                here = e2e.await_skip_target(log, index)
                if not e2e.skip_target_ok(here, index):
                    print(f"FAIL: {TARGET} is not the loaded level: "
                          f"{e2e.active_level(here)}", flush=True)
                    return 1
                log.new()
                e2e.dev("skip", 2.5)
                more = log.wait(["LevelSkipped", "LevelComplete "], 20, 5,
                                "the skip to complete the level")
                time.sleep(2.0)
                more += log.new()
                spent = "skip: spent one" in more
                completed = "LevelComplete " in more or "LevelSkipped" in more
                not_used = "skip: not used" in more
                print(f"      skip spent={spent}  level completed={completed}  "
                      f"not used={not_used}", flush=True)
                for line in more.splitlines():
                    if any(k in line for k in ("skip", "LevelComplete", "check:")):
                        print("      " + line.split("] ", 1)[-1][:120], flush=True)
                # A Skip works on every level (2026-09-25): the game completes
                # it, or - where the game will not skip - the mod releases the
                # slot and goes back to the track. Either way spent once.
                released = ("checks: skipped slot" in more
                            and "navigation: skip -> the run's track" in more)
                if not spent or not (completed or released) or not_used:
                    print(f"FAIL: on {TARGET} spent={spent} completed={completed} "
                          f"released={released}", flush=True)
                    return 1
                print(f"PASS: a Skip on {TARGET} "
                      + ("completes it and is spent" if completed
                         else "releases the slot, is spent, and goes back to the track"),
                      flush=True)
                rc = 0
                return 0

            print(f"[5/8] booting {TARGET} and beating it", flush=True)
            opened, _out = e2e.boot_level(log, index)
            if not opened:
                print(f"FAIL: {TARGET} did not open", flush=True)
                return 1
            # TWO ATTEMPTS. solve_level force-solves controllers and waits a
            # bounded time for the completion, and one run in four or so lost
            # that race on a cold first boot - "completed=False" for a level
            # that solves perfectly well on the next try. A flaky probe gets
            # ignored, which is worse than a slow one.
            banked = False
            text = ""
            for attempt in (1, 2):
                log.new()
                done, text = e2e.solve_level(log)
                banked = "beaten:" in text
                print(f"      attempt {attempt}: completed={done}  "
                      f"Beaten token banked={banked}", flush=True)
                if banked:
                    break
                if attempt == 1:
                    print("      not beaten; re-booting and trying once more",
                          flush=True)
                    if not e2e.boot_level(log, index)[0]:
                        break
            if not banked:
                print("FAIL: the level was not beaten in two attempts, so this "
                      "probes nothing about a BEATEN level", flush=True)
                return 1
            if OWED in text:
                print(f"NOTE: {OWED} was earned by the force-solve, so there "
                      "is nothing left for the Skip to grant and this seed "
                      "cannot probe the bug.", flush=True)
                return 1
            print(f"      {OWED} is still owed, as expected", flush=True)

            if pencils:
                # THE OLD ORDER, which must now be caught: force the beaten
                # level first. It completes again and the mod moves on.
                print("[5b/8] old order: re-open, force, then look before "
                      "the Skip", flush=True)
                e2e.to_title(log)
                if not e2e.boot_level(log, index)[0]:
                    print(f"FAIL: {TARGET} did not re-open", flush=True)
                    return 1
                log.new()
                redone, visit = e2e.solve_level(log)
                time.sleep(3.0)
                here = e2e.await_skip_target(log, index)
                caught = not e2e.skip_target_ok(visit + here, index)
                print(f"      forced again: completed={redone}; running now: "
                      f"{e2e.active_level(here)}; pre-Skip check stops it="
                      f"{caught}", flush=True)
                if not caught:
                    print("FAIL: the pre-Skip check let a Skip through after "
                          "the level had moved on", flush=True)
                    return 1
                e2e.to_title(log)

            print("[6/8] re-entering the beaten level", flush=True)
            # THE WHOLE POINT. The level is now beaten. Re-entering reloads it,
            # and a skip there still raises LevelComplete (measured
            # 2026-09-18); what matters is that the Skip releases the rest of
            # the slot rather than vanishing.
            opened, _out = e2e.boot_level(log, index)
            if not opened:
                print(f"FAIL: {TARGET} did not re-open", flush=True)
                return 1

            print("[7/8] spending a Skip on the beaten level", flush=True)
            # The harness's order: look, then Skip - nothing forced first.
            here = e2e.await_skip_target(log, index)
            if not e2e.skip_target_ok(here, index):
                print(f"FAIL: about to Skip {TARGET} but the game is running "
                      f"{e2e.active_level(here)}", flush=True)
                return 1
            print(f"      running: {e2e.active_level(here)}", flush=True)
            log.new()
            e2e.dev("skip", 2.5)
            more = log.wait(["check: ", "skip: "], 20, 7, "the skip")
            time.sleep(2.0)
            more += log.new()
            # Pencils too: the game will not skip a generator level in a run,
            # so the mod releases the slot itself (2026-09-25) - spent, and
            # the owed Solution 2 sent, like any other level.
            spent = "skip: spent one" in more
            paid = OWED in more
            print(f"      skip spent={spent}  {OWED} sent={paid}", flush=True)
            if not spent:
                print("FAIL: the skip was refused. Relevant lines:", flush=True)
                for line in more.splitlines():
                    if "skip" in line.lower():
                        print("   " + line.strip(), flush=True)
                return 1
            if not paid:
                print("FAIL: the Skip was consumed and the owed location never "
                      "arrived. Note this assertion passes on the pre-fix "
                      "build too, so a failure here is a NEW regression in "
                      "the payout path, not the old refusal bug.", flush=True)
                return 1

            print("[8/8] spending a Skip on the same slot, now complete",
                  flush=True)
            # The other half: an item with nothing to buy must not be taken.
            # Re-opened and checked first: after a Skip the mod may move on,
            # and a Skip sent then would test some other slot.
            e2e.to_title(log)
            if not e2e.boot_level(log, index)[0]:
                print(f"FAIL: {TARGET} did not re-open for the second Skip",
                      flush=True)
                return 1
            here = e2e.await_skip_target(log, index)
            if not e2e.skip_target_ok(here, index):
                print(f"FAIL: about to Skip {TARGET} again but the game is "
                      f"running {e2e.active_level(here)}", flush=True)
                return 1
            log.new()
            e2e.dev("skip", 2.5)
            again = log.wait(["skip: "], 15, 8, "the second skip")
            refused = "nothing left to find" in again
            burnt = "skip: spent one" in again
            print(f"      refused={refused}  spent anyway={burnt}", flush=True)
            if burnt or not refused:
                print("FAIL: a Skip was consumed on a slot with nothing left "
                      "to grant.", flush=True)
                return 1

            print("PASS: a Skip pays out on a beaten puzzle, and is refused "
                  "rather than burnt when there is nothing left to buy.",
                  flush=True)
            rc = 0
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
