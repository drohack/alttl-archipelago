"""Which controllers does each level ACTUALLY need to complete?

THE QUESTION droha ASKED: "we already had TupperwareTower that said it needed 4
abilities, but that was a lie / had to be hand tested. And only really needed I
think 2, but it might only need 1, we're not sure. That's what we want to find
out." Nothing has ever measured it, so every level's ability requirement is
whatever the sweep inferred from its controller list.

WHAT THIS MEASURES, AND WHAT IT CANNOT. Controllers are forced one at a time in
index order and the pass stops the moment LevelComplete fires. Everything not
yet forced at that point is PROVABLY not needed - the level finished without
it. That is a sound one-sided result.

It does NOT prove the rest are needed. Forcing in index order finds one
sufficient set, not the smallest one: a controller early in the order might
have been unnecessary too, and it was already forced by the time the level
finished. Establishing the true minimum needs leave-one-out - N boots per
level - which is why this reports candidates for a human to confirm rather
than rewriting levels.json.

WHY ONLY SOME LEVELS. A single-controller level cannot overstate anything: its
one controller is the level. Of 111 non-DLC levels only 36 have more than one,
so those are the whole search space.

LOCKS OFF, DELIBERATELY. This is about what the game's win condition needs, not
about what the randomizer gates - with locks on, a dimmed controller would be
skipped and confused with one that was never required.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-level-requirements.py 2>/dev/null

Base game only, so it runs with the Steam client closed. Writes a report to
testserver/logs/level-requirements.json.
"""
import glob
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import (Environment, close_game, ensure_no_steam_relaunch)

import release_e2e as e2e

OUT = os.path.join(e2e.REPO, "testserver", "out-req")
YAML = os.path.join(e2e.REPO, "testserver", "yaml-req")
REPORT = os.path.join(e2e.REPO, "testserver", "logs",
                      "level-requirements.json")
DATA = os.path.join(e2e.REPO, "apworld", "alttl", "data")

#: Everything playable, nothing gated: the question is what the LEVEL needs.
YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 8
  levels_to_beat: 8
  pack_size: 8
  guaranteed_open_slots: 8
  seeing_stars: false
  cupboards_and_drawers: false
  ability_locks: false
  skip_count: 0
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""


def catalogue():
    with open(os.path.join(DATA, "abilities.json"), encoding="utf-8") as f:
        ab = json.load(f)
    cls = {}
    for name, classes in ab["abilities"].items():
        for c in classes:
            cls[c] = name
    for _dlc, block in ab.get("dlcAbilities", {}).items():
        for name, classes in block.items():
            for c in classes:
                cls[c] = name

    with open(os.path.join(DATA, "levels.json"), encoding="utf-8") as f:
        rows = json.load(f)["levels"]
    return cls, rows


def targets(rows):
    """Non-DLC levels with more than one controller, in index order."""
    out = []
    for r in rows:
        if r.get("dlc"):
            continue
        if len(r["controllers"]) < 2:
            continue
        out.append(r)
    return sorted(out, key=lambda r: r["levelIndex"])


def main():
    cls, rows = catalogue()
    todo = targets(rows)
    print(f"[1/4] {len(todo)} non-DLC level(s) with more than one controller",
          flush=True)

    for d in (OUT, YAML):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(YAML, "s.yaml"), "w", newline="\n") as f:
        f.write(YML.format(slot=e2e.SLOT))
    subprocess.run([sys.executable, "Generate.py", "--player_files_path", YAML,
                    "--outputpath", OUT, "--seed", "31337"],
                   cwd=e2e.AP, capture_output=True, text=True)
    zips = glob.glob(os.path.join(OUT, "*.zip"))
    if not zips:
        print("FAIL: generation produced no seed", flush=True)
        return 1
    seed = os.path.basename(zips[0])
    for p in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(p)

    findings = []
    close_game()
    with Environment("level-requirements") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        server = None
        try:
            print("[2/4] starting the server", flush=True)
            server = subprocess.Popen(
                f'py -3.13 -u MultiServer.py --port {e2e.PORT} '
                f'"{os.path.join(OUT, seed)}"',
                shell=True, cwd=e2e.AP, stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)

            print("[3/4] launching the game", flush=True)
            log = e2e.Log()
            log.before_launch()
            ensure_no_steam_relaunch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            text = log.wait(["connected. "], 150, 3, "the connection")
            if "connected. " not in text:
                why = e2e.why_no_connection(e2e.whole_log())
                print("FAIL: never connected" + (f" - {why}" if why else ""),
                      flush=True)
                return 1

            problem = e2e.patch_problem(e2e.whole_log())
            if problem:
                print(f"FAIL: the mod did not fully install: {problem}",
                      flush=True)
                return 1

            print(f"[4/4] visiting {len(todo)} level(s)", flush=True)
            for n, row in enumerate(todo, 1):
                index = row["levelIndex"]
                level_id = row["levelId"]
                classes = [c["type"] for c in row["controllers"]]
                declared = ({cls[c] for c in classes if c in cls}
                            | set(row.get("extraAbilities", [])))

                opened, _out = e2e.boot_level(log, index)
                if not opened:
                    print(f"      [{n}/{len(todo)}] {level_id}: did not open",
                          flush=True)
                    continue

                done, chunk = e2e.solve_level(log)

                # UNWIND AFTER A FINISHED LEVEL, exactly as play() does, and
                # ONLY after a finished one. Without this the sweep completed
                # its first five levels and then nothing at all: booting
                # straight off a completion screen leaves the finished level
                # loaded, and the state degrades until no level completes
                # again. play()'s own comment records the other half - that
                # unwinding after an UNFINISHED level deactivates it without
                # destroying it and leaks a subscribed CheckWinCondition,
                # which is why this is conditional rather than unconditional.
                if done:
                    e2e.to_title(log)

                if not done or e2e.SOLVED_MARK not in chunk:
                    print(f"      [{n}/{len(todo)}] {level_id}: no completion "
                          "(phased, or needs a real solve)", flush=True)
                    findings.append({"levelIndex": index, "levelId": level_id,
                                     "declared": sorted(declared),
                                     "completed": False})
                    continue

                mark = chunk.split(e2e.SOLVED_MARK, 1)[1].split("\n")[0]
                forced = [int(x) for x in mark.split("|")[0].strip().split(",")
                          if x.strip().isdigit()]
                # Everything the level finished WITHOUT.
                untouched = [i for i in range(len(classes))
                             if i not in forced]
                needed = {cls[classes[i]] for i in forced
                          if i < len(classes) and classes[i] in cls}
                spare = sorted(declared - needed)

                flag = "  OVERSTATES" if spare else ""
                print(f"      [{n}/{len(todo)}] {level_id}: forced "
                      f"{len(forced)}/{len(classes)}, "
                      f"declares {len(declared)}{flag}", flush=True)
                if spare:
                    print(f"         finished without {len(untouched)} "
                          f"controller(s); not required: {', '.join(spare)}",
                          flush=True)

                findings.append({
                    "levelIndex": index, "levelId": level_id,
                    "declared": sorted(declared), "completed": True,
                    "forced": forced, "untouched": untouched,
                    "neededByMeasurement": sorted(needed),
                    "notRequired": spare,
                })
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
    with open(REPORT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(findings, f, indent=1)

    over = [f for f in findings if f.get("notRequired")]
    print(f"\n{len(over)} level(s) declare an ability they did not need:",
          flush=True)
    for f in over:
        print(f"  {f['levelId']} ({f['levelIndex']}): "
              f"{', '.join(f['notRequired'])}", flush=True)
    print("\nONE-SIDED: this proves those abilities were not needed to "
          "COMPLETE the level. It does not prove the remaining ones are "
          "required, and a part check can still need an ability the "
          "completion did not. Confirm by hand before levels.json moves - "
          "the requirement feeds the logic and test_regression.py pins the "
          "draw.", flush=True)
    print(f"\nreport: {os.path.relpath(REPORT, e2e.REPO)}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
