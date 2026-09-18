"""Prove a DLC puzzle actually plays in a run: launch, register, check, gate.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-dlc.py 2>/dev/null

WHAT NOTHING ELSE COVERS. The sweep proved DLC levels LOAD, and the unit suites
prove the draw and the tables. Neither touches the path a player takes: the mod
connected, the run's own track built from slot_data, and a DLC card launched
from it. That is different code - Track.BeforeStartLevel resolves a slot and
passes a seed, where the sweep called SetActiveLevel directly.

Three things it settles, each of which would otherwise surface first in a
fifteen-minute gate run or in somebody's game:

  1. A DLC level launches from the run's track and registers its controllers,
     WITHOUT the game being put into DLC mode. The game treats DLC as a mode
     elsewhere - SetCurrentDLC, IsDLCGamePlay, a separate DLCLevelSelect - and
     whether a level resolves its assets outside that was an open question.
  2. Its checks reach the mod. A location that exists in the table but whose
     controller never routes to a check is a location nobody can earn.
  3. A star-gated Seeing Stars level opens. The game locks five of them behind
     50 to 90 solution stars, and a locked level does NOT fail loudly: it
     redirects to the DLC level select and returns, so a mod that failed to
     clear the gate would look like a puzzle that simply never opens.

WHAT IT CANNOT SEE, and this cost two wrong "fixed" calls. It boots levels
straight through DevTools, so the POST-LEVEL menu flow never runs: measured
2026-09-17, a probe run entered DLCLevels_GameState zero times while a gate run
entered it fourteen. Anything about navigation - which menu opens after a
puzzle, whether a run lands back on its own track - is invisible here and must
be measured with `tools/release_e2e.py --dlc`.

Two minutes rather than the gate's fifteen. Needs both DLCs installed.
"""
import glob
import os
import subprocess
import sys
import tempfile
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game       # noqa: E402

import release_e2e as e2e                             # noqa: E402

OUT = os.path.join(e2e.REPO, "testserver", "out-dlcprobe")
YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-dlcprobe")
CMDS = os.path.join(tempfile.gettempdir(), "alttl-dlc-probe-cmds.txt")

#: The five Seeing Stars puzzles the game locks behind a star total.
GATED = {"DLC2 Bread Crusts", "DLC2 Cupcakes", "DLC2 Markers",
         "DLC2 Whistles", "DLC2 Ghost Cat"}

#: DLC levels the game marks randomizable, so they are generators: they repeat
#: with a fresh procedural layout per slot instead of appearing once.
#:
#: Worth probing separately because their SOURCE is "generator", not "dlc1" or
#: "dlc2" - the only thing keeping them out of a no-DLC run is the level's
#: `dlc` field - and because a forced seed has to actually reach the level.
#: Nothing else plays one: the release gate's DLC scenario sets
#: generator_weight to 0.
DLC_GENERATORS = {"DLC1 Trophy Cabinet", "DLC2 Water Glasses",
                  "DLC2 Figurines", "DLC2 Bread Crusts"}

#: Seeing Stars ONLY, at twenty puzzles, so a star-gated level is certain.
#:
#: Sized deliberately rather than hopefully. Four of the 34 drawable Seeing
#: Stars puzzles are star-gated, so an eight-slot run drawing from both DLCs
#: contains one about 40% of the time - measured over 40 draws - and an
#: earlier version of this probe rolled twelve seeds looking for one and found
#: none. Twenty slots out of 34 hits every time, measured 20 of 20. A probe
#: that depends on luck to reach its own assertion is a probe that silently
#: tests less than it claims.
#:
#: Ability locks off and a wide opening, because this is about whether a level
#: plays at all, not about gating.
YAML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 20
  levels_to_beat: 20
  pack_size: 10
  guaranteed_open_slots: 10
  cupboards_and_drawers: false
  seeing_stars: true
  cupboards_weight: 0
  stars_weight: 100
  # Non-zero so the four randomizable DLC levels can be drawn. They are
  # the only generators the DLCs have, and with every other source at 0 the
  # generator share can only come from them.
  generator_weight: 40
  archive_weight: 0
  base_weight: 0
  archive_packs: []
  mechanic_coverage: 0
  ability_locks: false
  skip_count: 0
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""


def generate(seed_number):
    for folder in (OUT, YAML_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "probe.yaml"), "w",
              encoding="utf-8", newline="\n") as fh:
        fh.write(YAML.format(slot=e2e.SLOT))

    result = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT, "--seed", str(seed_number)],
        cwd=e2e.AP, capture_output=True, text=True)
    zips = glob.glob(os.path.join(OUT, "*.zip"))
    if not zips:
        print(result.stdout[-1500:], flush=True)
        print(result.stderr[-1500:], flush=True)
        raise SystemExit("generation produced no seed")
    return os.path.basename(zips[0])


def controller_count(text):
    for line in text.splitlines():
        if "controllers: " in line:
            try:
                return int(line.split("controllers: ")[1].split()[0])
            except (IndexError, ValueError):
                pass
    return -1


def main():
    print("[1/7] generating a Seeing Stars seed", flush=True)
    seed = generate(20260917)
    slots = e2e.read_plan(OUT, seed)["slots"]
    drawn_gated = [n for _i, n in slots if n in GATED]
    if not drawn_gated:
        # The run is sized so this cannot happen; if it does, the draw changed
        # and the probe would otherwise quietly stop testing the gate.
        print("FAIL: no star-gated level was drawn. The run is sized so that "
              "is not supposed to be possible - re-measure before changing "
              "the size.", flush=True)
        return 1
    print("      " + seed, flush=True)
    for index, name in slots:
        mark = " <- star-gated" if name in GATED else ""
        print("        %-34s index %s%s" % (name, index, mark), flush=True)

    drawn_gen = [(i, n) for i, n in slots if n in DLC_GENERATORS]
    print("      DLC generators drawn: "
          + (", ".join(n for _i, n in drawn_gen) or "none"), flush=True)

    # NOT "every slot must be DLC" any more, and the reason is worth stating.
    # The four randomizable DLC levels share the "generator" source with the
    # base game's sixteen, so the only way to draw a DLC generator is to give
    # that source weight - which brings base generators along with it. There
    # is no weight that selects one and not the other.
    #
    # So the checks that matter are named directly instead: a star-gated level
    # and a DLC generator both have to be in the run, and both are booted
    # below. A base generator sitting in an unplayed slot proves nothing and
    # harms nothing.
    if not drawn_gen:
        print("FAIL: no DLC generator was drawn, so the generated half of the "
              "DLC content cannot be probed", flush=True)
        return 1

    for stale in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(stale)
    open(CMDS, "w", encoding="utf-8").close()

    close_game()
    rc = 1
    server = None
    with Environment("dlc-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        try:
            print("[2/7] starting the server", flush=True)
            server = subprocess.Popen(
                'tail -f "%s" | py -3.13 -u MultiServer.py --port %d "%s"'
                % (CMDS, e2e.PORT, os.path.join(OUT, seed)),
                shell=True, cwd=e2e.AP,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)
            if not e2e.port_open():
                print("FAIL: the server never bound 38281", flush=True)
                return 1

            print("[3/7] launching the game with the mod", flush=True)
            log = e2e.Log()
            what, _windowed = e2e.describe_display()
            print("      " + what, flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            out = log.wait(["connected. ", "refusing the seed"], 180, 3,
                           "the connection")
            if "refusing the seed" in out:
                for line in out.splitlines():
                    if "refusing the seed" in line:
                        print("FAIL: " + line.strip(), flush=True)
                return 1
            if "connected. " not in out:
                print("FAIL: never connected", flush=True)
                return 1
            print("      connected, and the seed was not refused", flush=True)

            star_lines = [l for l in out.splitlines()
                          if "cleared the star gate" in l]
            if drawn_gated and not star_lines:
                print("FAIL: the run drew a star-gated level and the mod "
                      "never cleared the gate", flush=True)
                return 1
            if star_lines:
                print("      " + star_lines[-1].split("] ")[-1].strip(),
                      flush=True)

            print("[4/7] booting the star-gated puzzle from the run",
                  flush=True)
            index, name = next((i, n) for i, n in slots if n in GATED)
            opened, out = e2e.boot_level(log, index)
            if not opened:
                print("FAIL: %s did not open from the run's track" % name,
                      flush=True)
                return 1
            print("      %s opened" % name, flush=True)

            print("[5/7] checking it registered controllers", flush=True)
            log.new()
            e2e.dev("controllers", 1.0)
            listing = log.wait(["controllers: "], 10, 5, "the controller list")
            count = controller_count(listing)
            print("      %d controller(s) registered" % count, flush=True)
            if count <= 0:
                print("FAIL: the level registered nothing, so it has no "
                      "checks to send", flush=True)
                return 1

            print("[6/7] solving it and watching for a check", flush=True)
            log.new()
            done, text = e2e.solve_level(log)
            checks = [l for l in text.splitlines() if "check:" in l]
            print("      completed=%s  checks sent=%d" % (done, len(checks)),
                  flush=True)
            if not checks:
                print("FAIL: no check was sent from a DLC level", flush=True)
                return 1
            for line in checks[:3]:
                print("      " + line.split("] ")[-1].strip(), flush=True)

            # A DLC GENERATOR, which is a different thing from the levels
            # above: it is built from a seed rather than authored, so the
            # question is whether the run's seed reaches it and the level
            # still registers controllers and pays checks.
            if drawn_gen:
                index, name = drawn_gen[0]
                print(f"[7/7] booting the DLC generator {name}", flush=True)
                opened, _out = e2e.boot_level(log, index)
                if not opened:
                    print(f"FAIL: {name} did not open", flush=True)
                    return 1
                log.new()
                e2e.dev("controllers", 1.0)
                listing = log.wait(["controllers: "], 10, 7,
                                   "the controller list")
                count = controller_count(listing)
                print(f"      {count} controller(s) registered", flush=True)
                if count <= 0:
                    print("FAIL: the generated level registered nothing",
                          flush=True)
                    return 1

                log.new()
                done, text = e2e.solve_level(log)
                gen_checks = [l for l in text.splitlines() if "check:" in l]
                print(f"      completed={done}  checks sent={len(gen_checks)}",
                      flush=True)
                if not gen_checks:
                    print("FAIL: no check came from a generated DLC level",
                          flush=True)
                    return 1
                for line in gen_checks[:2]:
                    print("      " + line.split("] ")[-1].strip(), flush=True)
            else:
                print("FAIL: no DLC generator was drawn, so the generated "
                      "half of the DLC content went untested", flush=True)
                return 1

            print("[7/7] every question answered", flush=True)
            print("PASS: DLC puzzles launch from the run's track - authored, "
                  "star-gated and procedurally generated alike - register "
                  "their controllers and send checks", flush=True)
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
