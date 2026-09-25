"""Measure what the game records in the save when a level is finished.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-star.py
    py -3.13 tools/probe-star.py --setup-only   serve a seed, droha plays
    py -3.13 tools/probe-star.py --dump         read the save back out
    py -3.13 tools/probe-star.py --save         same, offline, no game needed

Answers "why is the level-beaten star empty on some cards". Generates a seed
with nothing gated, launches, then for one plain level per source (generator,
base, archive) boots it, forces every controller solved through the game's own
dispatcher, and dumps the save before and after. Prints both rows per level.

The base and archive levels are the CONTROL: droha confirmed those star
correctly, so if they do not come out solved the method did not work and the
generator row means nothing. Two earlier readings were discarded that way.

`solved` is the column that matters - the star draws only on a solved level.
`found`, `completed`, `store` and `solutionsInSave` say why it moved or not.
"""
import json
import os
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402
from harness_env import close_game                         # noqa: E402

YAML_DIR = os.path.join(e2e.REPO, "testserver", "yaml-star")
OUT_DIR = os.path.join(e2e.REPO, "testserver", "out-star")
TSV = os.path.join(e2e.GAME, "BepInEx", "alttl-unlocks.tsv")
LEVELS = os.path.join(e2e.REPO, "apworld", "alttl", "data", "levels.json")

#: How many controller slots to force solved per level. Comfortably more than
#: any level in the table has; solving past the end matches nothing and logs
#: that it matched nothing.
SOLVE_SLOTS = 12

#: Everything open. The star is a base-game display question and has nothing
#: to do with ability gating, so gating is removed rather than reasoned around.
YAML = f"""name: {e2e.SLOT}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 40
  levels_to_beat: 40
  pack_size: 10
  ability_locks: false
  cat_trap_chance: 0
  hint_coverage: 0
  skip_count: 0
  progression_balancing: 0
  accessibility: full
  cupboards_and_drawers: true
  seeing_stars: true
  start_inventory:
    Progressive Puzzle Pack: 3
"""


def sources():
    with open(LEVELS, encoding="utf-8") as fh:
        table = json.load(fh)
    return {str(l.get("levelIndex")):
            (l["levelId"], l.get("source"), l.get("levelClass", "Level"))
            for l in table["levels"]}


def read_tsv():
    """The dump, keyed by level index. Empty dict if it was never written."""
    if not os.path.exists(TSV):
        return {}
    with open(TSV, encoding="utf-8") as fh:
        lines = [l.rstrip("\n") for l in fh if l.strip()]
    if not lines:
        return {}
    head = lines[0].split("\t")
    rows = {}
    for line in lines[1:]:
        cells = line.split("\t")
        if len(cells) != len(head):
            continue
        row = dict(zip(head, cells))
        rows[row["levelIndex"]] = row
    return rows


def show(label, row, keys):
    if row is None:
        print(f"      {label:34} <no row>", flush=True)
        return
    print(f"      {label:34} "
          + "  ".join(f"{k}={row.get(k, '?')}" for k in keys), flush=True)


def save_report():
    """What the GAME recorded, read straight out of the save file.

    Needs no game running and no DevTools, so it works after a session has
    ended. `solutions` is the arrangements actually found; `numSolutions` is
    how many the level has. An empty `solutions` on a level that was beaten is
    the symptom droha reported as an empty completion star.
    """
    levels = {l["levelId"]: l.get("source") for l in
              json.load(open(LEVELS, encoding="utf-8"))["levels"]}

    saves = [f for f in os.listdir(e2e.SAVE_DIR)
             if f.startswith("save_ap_") and f.endswith(".json")
             and not f.endswith(".run.json")]
    if not saves:
        print("Done: no save_ap_*.json in the save folder - "
              "has a run been played?", flush=True)
        return 1
    path = max((os.path.join(e2e.SAVE_DIR, f) for f in saves),
               key=os.path.getmtime)
    print(f"  {os.path.basename(path)}", flush=True)

    data = e2e.read_save(path)
    empty = found = 0
    for key in ("levelCompletionData", "archiveCompletionData"):
        rows = data.get(key) or []
        print("", flush=True)
        print(f"=== {key}: {len(rows)} row(s) ===", flush=True)
        for row in rows:
            level_id = row.get("levelId", "?")
            source = levels.get(level_id)
            if source is None:
                continue                      # chapter dividers, not levels
            banked = len(row.get("solutions") or [])
            total = row.get("numSolutions", "?")
            mark = "  " if banked else "  <- NOTHING BANKED"
            if banked:
                found += 1
            else:
                empty += 1
            print(f"  {source:10} {level_id:36} "
                  f"solutions={banked}/{total} skipped={row.get('skipped')}"
                  f"{mark}", flush=True)

    print("", flush=True)
    print(f"Done: {found} level(s) with a solution banked, {empty} with none.",
          flush=True)
    return 0


def dump_only():
    """Read the save back out of a game droha is already playing.

    Prints every level that has banked at least one solution, which is the
    set they just played, with the source beside it.
    """
    src = sources()
    keys = ["solutionCount", "found", "solved", "completed",
            "store", "solutionsInSave"]
    e2e.dev("unlocks", settle=4.0)
    rows = read_tsv()
    if not rows:
        print("Done: no dump written - is the game running?", flush=True)
        return 1

    played = [(i, r) for i, r in rows.items()
              if r.get("found", "0") not in ("0", "?")
              or r.get("solutionsInSave", "0") not in ("0", "-", "?")]
    if not played:
        print(f"Done: {len(rows)} level(s) dumped, none with a solution "
              f"banked yet.", flush=True)
        return 0

    for idx, row in sorted(played, key=lambda kv: int(kv[0])):
        entry = src.get(idx)
        name = entry[0] if entry else "?"
        source = entry[1] if entry else "?"
        show(f"{source} {idx} {name}", row, keys)
    print("", flush=True)
    print(f"Done: {len(played)} level(s) with a solution banked, of "
          f"{len(rows)} dumped.", flush=True)
    return 0


def main():
    if "--save" in sys.argv:
        return save_report()
    if "--dump" in sys.argv:
        return dump_only()
    src = sources()
    keys = ["solutionCount", "found", "solved", "completed",
            "hasSaveEntry", "store", "solutionsInSave", "isDailyTidy"]

    print("[1/7] clearing the way", flush=True)
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
    for name in list(os.listdir(e2e.SAVE_DIR)):
        if name.startswith("save_ap_") or name == "alttl-last-session.json":
            os.remove(os.path.join(e2e.SAVE_DIR, name))
    if os.path.exists(TSV):
        os.remove(TSV)

    print("[2/7] generating a 40-puzzle seed, nothing gated", flush=True)
    for folder in (YAML_DIR, OUT_DIR):
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            os.remove(os.path.join(folder, name))
    with open(os.path.join(YAML_DIR, "s.yaml"), "w", encoding="utf-8") as fh:
        fh.write(YAML)
    r = subprocess.run(
        [sys.executable, "Generate.py", "--player_files_path", YAML_DIR,
         "--outputpath", OUT_DIR, "--seed", "20260922"], cwd=e2e.AP,
        capture_output=True, text=True,
        env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode:
        print(r.stdout[-1500:], flush=True)
        print(r.stderr[-1500:], flush=True)
        return 1
    seed = os.path.join(OUT_DIR,
                        [f for f in os.listdir(OUT_DIR)
                         if f.endswith(".zip")][0])

    print("[3/7] serving", flush=True)
    logs = os.path.join(e2e.REPO, "testserver", "logs")
    flags = (subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP) \
        if os.name == "nt" else 0
    subprocess.Popen(
        [sys.executable, "MultiServer.py", "--port", str(e2e.PORT), seed],
        cwd=e2e.AP, stdout=open(os.path.join(logs, "star-server.log"), "w"),
        stderr=open(os.path.join(logs, "star-server.err"), "w"),
        stdin=subprocess.DEVNULL, creationflags=flags,
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
              f"auto-connect ON, nothing gated", flush=True)
        print("  open the game yourself; it connects on its own.", flush=True)
        print("  beat ONE generator level and ONE plain base level, then say "
              "so and I will run --dump.", flush=True)
        print("", flush=True)
        print("Done: session ready, game NOT launched.", flush=True)
        return 0

    print("[4/7] launching", flush=True)
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
    if "connected." not in got:
        print("Done: never connected, nothing measured.", flush=True)
        return 1
    print("      connected", flush=True)
    time.sleep(8)
    e2e.dev("setres:1280x720", settle=3.0)

    print("[5/7] baseline dump, before anything is completed", flush=True)
    e2e.dev("unlocks", settle=4.0)
    before = read_tsv()
    print(f"      {len(before)} level(s) in the dump", flush=True)
    if not before:
        print("Done: the dump is empty, so nothing can be concluded.",
              flush=True)
        return 1

    # One per source, from levels the mod revealed, so all three carry the
    # same mod-created completion row.
    picks, want = [], {"generator": None, "base": None, "archive": None}
    for idx, row in before.items():
        entry = src.get(idx)
        if not entry:
            continue
        level_id, source, level_class = entry

        # Plain levels only: a bespoke class reveals its stages by playing,
        # so it will not complete here and makes a useless control.
        if level_class != "Level":
            continue
        # solutionCount 1 on all three, or the comparison is not one.
        if source in want and want[source] is None \
                and row.get("hasSaveEntry") == "True" \
                and row.get("solutionCount") == "1" \
                and row.get("solved") == "False":
            want[source] = (idx, level_id)
    for source in ("generator", "base", "archive"):
        if want[source]:
            picks.append((source, ) + want[source])

    if not any(s == "generator" for s, _, _ in picks):
        print("Done: no unsolved generator level in this seed, so the case "
              "droha reported cannot be reproduced here.", flush=True)
        return 1
    if len(picks) < 2:
        print("Done: no control level to compare against, so nothing is "
              "reported.", flush=True)
        return 1

    print("      picked:", flush=True)
    for source, idx, level_id in picks:
        show(f"{source} {idx} {level_id}", before.get(idx), keys)

    print(f"[6/7] booting and solving {len(picks)} level(s)", flush=True)
    for n, (source, idx, level_id) in enumerate(picks, 1):
        print(f"      [{n}/{len(picks)}] {source} {idx} {level_id}: boot",
              flush=True)
        e2e.dev(f"boot:{idx}", settle=9.0)

        # solve: dispatches ObjectControllerSolved through the game's own
        # dispatcher, so CheckWinCondition runs and the game finishes the
        # level. `complete` (CompleteLevel) writes nothing to the save - it
        # runs the presentation only. Indices past the end match nothing.
        for c in range(SOLVE_SLOTS):
            e2e.dev(f"solve:{c}", settle=0.8)
        print(f"      [{n}/{len(picks)}] {source} {idx} {level_id}: "
              f"solved {SOLVE_SLOTS} slot(s)", flush=True)
        time.sleep(6)

    print("[7/7] dump after", flush=True)
    e2e.dev("unlocks", settle=4.0)
    after = read_tsv()

    print("", flush=True)
    moved = 0
    for source, idx, level_id in picks:
        print(f"  {source.upper()} {idx} {level_id}", flush=True)
        show("before", before.get(idx), keys)
        show("after ", after.get(idx), keys)
        b, a = before.get(idx, {}), after.get(idx, {})
        if b.get("solved") != a.get("solved"):
            moved += 1
        print("", flush=True)

    close_game()
    print(f"Done: {moved} of {len(picks)} level(s) changed `solved`. "
          f"If every source moved the same way this instrument sees no "
          f"difference and the cause is elsewhere.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
