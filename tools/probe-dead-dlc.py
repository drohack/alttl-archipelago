"""Which controllers register but never pay a check, across a whole run.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-dead-dlc.py 2>/dev/null

WHY THIS EXISTS, and it is not hypothetical. The 0.4.0 DLC gate run stalled at
5 of 8 puzzles: the Progressive Puzzle Pack had been placed on
`Game Pieces (Cupboards and Drawers) - Drawers`, and that location cannot be
earned. The level registers seven controllers and raises a solved event for
six - `Drawers/DrawerController` is the CONTAINER the other six live in, not an
objective, so it never reports. One unearnable location holding the only pack
item is a run that cannot continue, and nothing before this measured for it.

This is the same class as TupperwareTower's two StackableGrids and
SomethingEggstra Fridge's spare groups, both already pinned in
SurveyCrossCheckTests: a controller that is mechanism rather than puzzle.

HOW IT MEASURES. One seed containing every level of interest, then per level:
boot it, solve EVERYTHING, and diff the checks that arrived against the group
locations the level is supposed to mint. Whatever never arrives is a location
nobody can earn.

Deliberately no per-controller attribution. The first version solved one
controller at a time and blamed whichever one preceded a quiet stretch of log,
which reported 46 dead controllers including `Angled Image Frame / Angled` -
that level's only controller and its entire puzzle. The fault was the
attribution, not the game: a check that arrives a moment late lands in the next
controller's window. Diffing totals cannot make that mistake.

The seed must CONTAIN the level - the mod files a check only against a slot the
run holds - which is why this generates its own rather than reusing the gate's.
"""
import glob
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import Environment, close_game       # noqa: E402

import release_e2e as e2e                             # noqa: E402

OUT = os.path.join(e2e.REPO, "testserver", "out-deaddlc")
YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-deaddlc")
REPORT = os.path.join(e2e.REPO, "testserver", "logs", "dead-controllers.tsv")

#: Every DLC puzzle, in one run. 62 of them and the option caps at 79, so they
#: all fit - locks off and no packs, so every slot is playable immediately.
YAML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 79
  levels_to_beat: 79
  pack_size: 10
  guaranteed_open_slots: 10
  cupboards_and_drawers: true
  seeing_stars: true
  cupboards_weight: 50
  stars_weight: 50
  # Enough generator weight to pull in the four randomizable DLC levels,
  # and no archive or campaign at all, so the 79 slots are spent on DLC
  # content. Verified at this seed: all 62 DLC levels are drawn.
  generator_weight: 25
  archive_weight: 0
  base_weight: 0
  mechanic_coverage: 0
  ability_locks: false
  skip_count: 0
  cat_trap_chance: 0
  hint_coverage: 0
  progression_balancing: 0
  accessibility: full
"""


def generate():
    for folder in (OUT, YAML_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "probe.yaml"), "w",
              encoding="utf-8", newline="\n") as fh:
        fh.write(YAML.format(slot=e2e.SLOT))

    result = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT, "--seed", "9000"],
        cwd=e2e.AP, capture_output=True, text=True)
    zips = glob.glob(os.path.join(OUT, "*.zip"))
    if not zips:
        print(result.stdout[-1500:], flush=True)
        print(result.stderr[-1500:], flush=True)
        raise SystemExit("generation produced no seed")
    return os.path.basename(zips[0])


def probe(log, index, level_id, expected):
    """Group locations the level should mint that never got a check.

    NO PER-CONTROLLER ATTRIBUTION. An earlier version solved one controller at
    a time and blamed whichever one preceded a quiet log chunk, which reported
    a level's only controller as dead because its check arrived a moment late.
    Solving everything and diffing what arrived against what should have
    needs no attribution and cannot make that mistake.
    """
    opened, _out = e2e.boot_level(log, index)
    if not opened:
        print("      %s did not open" % level_id, flush=True)
        return None

    log.new()
    # The gate's own solve path: several passes, proper waits, and it reports
    # when a level cannot be finished this way.
    _done, text = e2e.solve_level(log)
    time.sleep(1.5)
    text += log.new()

    got = set()
    for line in text.splitlines():
        if "check:" not in line:
            continue
        name = line.split("check:", 1)[1].strip()
        if " - " in name:
            got.add(name.rsplit(" - ", 1)[1])

    missing = sorted(set(expected) - got)
    return missing


def main():
    print("[1/4] generating a seed holding every DLC puzzle", flush=True)
    seed = generate()
    slots = e2e.read_plan(OUT, seed)["slots"]
    dlc = [(i, n) for i, n in slots if n.startswith("DLC")]
    print("      %s: %d slots, %d of them DLC"
          % (seed, len(slots), len(dlc)), flush=True)

    # The group DISPLAY names each level is supposed to mint a location for.
    # A single-group level mints none - its group check and its first solution
    # check are the same event - so those are skipped rather than reported as
    # entirely dead.
    names = json.load(open(os.path.join(
        e2e.REPO, "apworld", "alttl", "data", "names.json"), encoding="utf-8"))
    expected = {}
    for level_id, entry in names["levels"].items():
        parts = [part["display"] for part in entry["parts"].values()]
        expected[level_id] = parts if len(parts) > 1 else []

    for stale in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(stale)

    close_game()
    findings = []
    with Environment("dead-dlc-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        server = None
        try:
            print("[2/4] starting the server", flush=True)
            server = subprocess.Popen(
                'py -3.13 -u MultiServer.py --port %d "%s"'
                % (e2e.PORT, os.path.join(OUT, seed)),
                shell=True, cwd=e2e.AP, stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)
            if not e2e.port_open():
                print("FAIL: the server never bound", flush=True)
                return 1

            print("[3/4] launching the game", flush=True)
            log = e2e.Log()
            what, _windowed = e2e.describe_display()
            print("      " + what, flush=True)
            log.before_launch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            if "connected. " not in log.wait(["connected. "], 180, 3,
                                             "the connection"):
                print("FAIL: never connected", flush=True)
                return 1

            print("[4/4] probing %d level(s)" % len(slots), flush=True)
            for n, (index, level_id) in enumerate(slots, 1):
                print("[4/4] %d/%d %s" % (n, len(slots), level_id), flush=True)
                if not expected.get(level_id):
                    continue
                missing = probe(log, index, level_id, expected[level_id])
                if missing:
                    for name in missing:
                        findings.append((level_id, name))
                        print("        NEVER CHECKED: %s" % name, flush=True)
        finally:
            close_game()
            if server:
                subprocess.run(["powershell", "-NoProfile", "-Command",
                                "Get-NetTCPConnection -LocalPort 38281 -State "
                                "Listen -ErrorAction SilentlyContinue | "
                                "ForEach-Object { Stop-Process -Id "
                                "$_.OwningProcess -Force }"],
                               capture_output=True)

    os.makedirs(os.path.dirname(REPORT), exist_ok=True)
    with open(REPORT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("levelId\tgroupLocation\n")
        for row in findings:
            fh.write("\t".join(row) + "\n")

    print("Done: %d group location(s) were never checked after solving "
          "everything; each is a location nobody can earn. Written to %s"
          % (len(findings), REPORT), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
