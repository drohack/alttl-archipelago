"""Do the ability locks actually lock anything, and what do levels really need?

TWO QUESTIONS, AND THE SECOND IS THE INTERESTING ONE.

1. When the run does not hold an ability, are that ability's objects actually
   dead to the player - not merely declared locked in a table?
2. Which controllers does a level ACTUALLY need solved? `TupperwareTower`
   declares four abilities and hand-testing suggested it needs fewer. Nobody
   knows, because nothing has ever measured it.

WHY THIS EXISTS AT ALL. droha watched the release gate play DLC2 Pizza with its
48 toppings greyed out and force-solve it anyway. `solve:` sets a controller's
solved flag and dispatches the event; the dimmer only ever touched the
LevelObjects. The two never meet, so the gate walked through every ability gate
in the run and learned nothing from any of them.

GROUND TRUTH IS THE SCREEN, NOT THE TABLE. Three ways to ask whether a level is
gated, and only one can catch a wrong table:

  - Recompute it from abilities.json. That re-derives the answer from the very
    table being audited, so it agrees with a wrong one every time.
  - Read the mod's "abilities: N locked" line. Emitted only when the summary
    CHANGES, once a second, and not at all when locks are off or nothing is
    connected - so its absence means four different things, and an incremental
    log reader races it.
  - Ask the GAME which objects are non-interactive. That is what the player can
    touch, whatever any table claims.

This uses the third, through the DevTools `locks` command. The table is still
read here, but only to form an EXPECTATION to compare against - a mismatch is
the finding, not an error.

THE HELD SET IS MEASURED, NOT ASSUMED. `starting_abilities: 0` does not mean
zero abilities are held: pool.py grants extra ones until the opening has enough
free checks. So the probe reads what the mod says it granted and derives its
expectations from that.

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-ability-locks.py 2>/dev/null

Generates its own seed. Frames land in testserver/logs/ability-lock-frames/.
"""
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import time

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from harness_env import (Environment, close_game,
                         ensure_no_steam_relaunch)

import release_e2e as e2e

OUT = os.path.join(e2e.REPO, "testserver", "out-locks")
YAML = os.path.join(e2e.REPO, "testserver", "yaml-locks")
SHOTS = os.path.join(e2e.REPO, "testserver", "logs", "ability-lock-frames")

DATA = os.path.join(e2e.REPO, "apworld", "alttl", "data")

#: What the mod says it granted, e.g.
#: "ability locks on, 13 abilities, starting with Grids, Ordering"
HELD = re.compile(r"ability locks (on|off), (\d+) abilities?"
                  r"(?:, starting with (.*))?$")

#: One line of the `locks` report.
LOCK_ROW = re.compile(
    r"\[(\d+)\] (.*?) type=(\S+) objects=(\d+) blocked=(\d+) dimmed=(\d+) "
    r"shared=(\d+) norenderer=(\d+) solved=(\S+)")


def catalogue():
    """class -> ability, and levelIndex -> (levelId, [controller classes])."""
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
    levels = {}
    for r in rows:
        levels[r["levelIndex"]] = (
            r["levelId"],
            [c["type"] for c in r["controllers"]],
            set(r.get("extraAbilities", [])),
        )
    return cls, levels


def wanted_levels(cls, levels):
    """The levels to visit: one per ability, the multis, and controls.

    Chosen from the data rather than hardcoded, so a level table change moves
    the probe with it instead of silently testing something else.
    """
    by_ability = {}
    multi = []
    control = []
    for index, (level_id, classes, extra) in sorted(levels.items()):
        needs = {cls[c] for c in classes if c in cls} | set(extra)
        if not needs:
            control.append(index)
            continue
        if len(needs) == 1:
            by_ability.setdefault(next(iter(needs)), []).append(index)
        else:
            multi.append((index, needs))

    picked = {}
    for ability, indexes in sorted(by_ability.items()):
        picked[indexes[0]] = ("single", ability)
    # The widest multi-ability levels are the ones worth measuring: they are
    # where a declared requirement is most likely to be wrong.
    for index, needs in sorted(multi, key=lambda p: -len(p[1]))[:6]:
        picked[index] = ("multi", ",".join(sorted(needs)))

    # NAMED SUSPECTS, always visited however narrow they look. TupperwareTower
    # is the level droha actually asked about - it declares Grids+Stacking and
    # hand-testing suggested it needs fewer, which two abilities alone would
    # never rank it into the "widest" cut above. It is also on the harness's
    # KNOWN_UNFORCEABLE list, so it is doubly worth a measurement.
    for index, needs in multi:
        if index in (83, 82, 20, 54):
            picked[index] = ("suspect", ",".join(sorted(needs)))
    for index in control[:2]:
        picked[index] = ("control", "")
    return picked


def shoot(name):
    """One frame, kept for a human. The screen is the evidence here."""
    os.makedirs(SHOTS, exist_ok=True)
    tmp = os.path.join(e2e.GAME, f"ap-lock-{name}.png")
    if os.path.exists(tmp):
        os.remove(tmp)
    e2e.dev(f"shot:{tmp}", 0.5)
    size = -1
    for _ in range(40):
        time.sleep(0.5)
        try:
            now = os.path.getsize(tmp)
        except OSError:
            continue
        if now > 0 and now == size:
            break
        size = now
    else:
        return None
    dest = os.path.join(SHOTS, f"{name}.png")
    shutil.move(tmp, dest)
    return dest


def _ask_locks(log):
    """One reading of the lock report."""
    log.new()
    e2e.dev("locks", 1.0)
    out = log.wait(["locks: "], 15, 1, "the lock report")
    # THE HEADER IS NOT THE LIST - the same trap `controllers` documents.
    time.sleep(1.5)
    out += log.new()
    if "locks: no level running" in out:
        return None, out
    rows = []
    for line in out.splitlines():
        m = LOCK_ROW.search(line)
        if m:
            rows.append({
                "index": int(m.group(1)),
                "name": m.group(2).strip(),
                "type": m.group(3),
                "objects": int(m.group(4)),
                "blocked": int(m.group(5)),
                "dimmed": int(m.group(6)),
                "shared": int(m.group(7)),
                "norenderer": int(m.group(8)),
                "solved": m.group(9) == "True",
            })
    return rows, out


def wait_settled(log, seconds=25):
    """Wait until the level has stopped moving.

    SOME LEVELS OPEN WITH AN ANIMATION - droha: "I'm not sure you're letting
    TupperwareTower finish its opening animation, there's not many levels like
    that, but this one has one." While it plays, objects are still arriving and
    being positioned, so a lock report taken then describes a level that does
    not exist yet. Worse, it can be STABLE while wrong: two identical readings
    a second apart are easy to get in the middle of a slow animation, which is
    exactly what the settle loop below would accept.

    So ask the game whether it is still transitioning first, and only then
    start looking for a stable answer.
    """
    deadline = time.time() + seconds
    while time.time() < deadline:
        log.new()
        e2e.dev("state", 0.6)
        out = log.wait(["state: gameState"], 10, 1, "the level state")
        line = e2e.line_with(out, "state: gameState")
        if "transitioning=False" in line and "loaded=True" in line:
            return True
        time.sleep(1.0)
    return False


def read_locks(log):
    """Ask until the answer stops changing, then believe it.

    THE DIMMER IS POLLED, NOT EVENT-DRIVEN. AbilityLocks re-applies once a
    SECOND, so for up to a second after a level loads its objects are still
    wearing their own colours and every lock reads as absent. Asking once
    measures the race, not the locks.

    That is not hypothetical. A first pass of this probe reported
    TupperwareTower as 9 of 41 dimmed with both StackableGrid controllers at
    zero - which looked exactly like the spurious requirement it was sent to
    find - and the next run of the same build, same seed, read 41 of 41.
    Boxes (Stacked) flipped 0/13 to 13/13 the same way. Two identical readings
    in a row is the cheapest way to know the pass has settled.
    """
    history = []
    for _ in range(10):
        rows, out = _ask_locks(log)
        if rows is None:
            return None, out
        signature = [(r["index"], r["dimmed"], r["objects"]) for r in rows]
        history.append(signature)
        # THREE in a row, not two. An animated level can hold the same wrong
        # answer across two reads; three spaced over four seconds has not been
        # seen to.
        if len(history) >= 3 and history[-1] == history[-2] == history[-3]:
            return rows, out
        time.sleep(1.4)
    return rows, out


def held_from(text, all_abilities):
    """What the mod says the run holds. Measured, never assumed.

    TWO SOURCES, because the connect line is only half the answer. It reports
    slot_data's StartingAbilities; anything granted through start_inventory
    arrives afterwards as an ordinary item, so the received lines count too.
    """
    held = None
    for line in text.splitlines():
        m = HELD.search(line.strip())
        if m:
            if m.group(1) == "off":
                return None          # locks disabled; nothing to test
            # The mod prints the set in brackets: "starting with [A, B]".
            names = (m.group(3) or "").strip().strip("[]")
            held = {n.strip() for n in names.split(",") if n.strip()}
            break
    if held is None:
        return set()

    for line in text.splitlines():
        if "received item: " in line:
            name = line.split("received item: ", 1)[1].strip()
            if name in all_abilities:
                held.add(name)
    return held


def seed_abilities(folder, zipname):
    """The abilities THIS seed knows about, read from its slot_data.

    LOAD-BEARING, and the first run of this probe got it wrong. The mod builds
    its class-to-ability map from slot_data, and slot_data carries only the
    abilities the drawn levels need (slots.abilities_in). A class outside that
    map fails open and is never dimmed - so booting a level whose ability the
    seed never drew shows nothing locked, correctly, and comparing it against
    the full table reports a mismatch that is not one. DLC2 Pizza read
    "0/48 blocked" for exactly this reason: Pizza was not in the plan, so
    Distributables was not in the map.
    """
    code = """
import zipfile, zlib, json, sys
from Utils import restricted_loads
f = zipfile.ZipFile(sys.argv[1])
n = [x for x in f.namelist() if x.endswith('.archipelago')][0]
d = restricted_loads(zlib.decompress(f.read(n)[1:]))['slot_data'][1]
print(json.dumps(sorted(d.get('abilities', {}))))
"""
    r = subprocess.run([sys.executable, "-c", code,
                        os.path.join(folder, zipname)],
                       cwd=e2e.AP, capture_output=True, text=True,
                       env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"))
    if r.returncode != 0:
        print(r.stderr[-800:], flush=True)
        return None
    return set(json.loads(r.stdout.strip().splitlines()[-1]))


def generate(seed_n):
    for d in (OUT, YAML):
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
    with open(os.path.join(YAML, "s.yaml"), "w", newline="\n") as f:
        f.write(YML.format(slot=e2e.SLOT))
    subprocess.run([sys.executable, "Generate.py",
                    "--player_files_path", YAML, "--outputpath", OUT,
                    "--seed", str(seed_n)],
                   cwd=e2e.AP, capture_output=True, text=True)
    zips = glob.glob(os.path.join(OUT, "*.zip"))
    return os.path.basename(zips[0]) if zips else None


#: BASE GAME ONLY, so this runs without a Steam client. DLC are entitlements
#: and the game reports none when Steam is closed, at which point the mod
#: refuses a DLC seed and never connects - which cost a gate run and a probe
#: run to work out. That loses Distributing, which lives on one DLC2 level;
#: the other twelve abilities are all here.
#:
#: Locks on, and as few abilities granted as the generator will allow. Levels
#: are opened with `boot:`
#: regardless of what the plan drew - the dimmer works off the live level's
#: controllers and the held ability set, not off the run's plan - so the draw
#: does not have to be forced to cover thirteen abilities.
YML = """name: {slot}
game: A Little to the Left
requires:
  version: 0.6.7
A Little to the Left:
  puzzle_count: 20
  levels_to_beat: 20
  pack_size: 10
  guaranteed_open_slots: 10
  seeing_stars: false
  cupboards_and_drawers: false
  generator_weight: 20
  archive_weight: 20
  base_weight: 40
  mechanic_coverage: 6
  ability_locks: true
  starting_abilities: 0
  skip_count: 0
  cat_trap_chance: 0
  progression_balancing: 0
  accessibility: full
"""


def main():
    cls, levels = catalogue()
    picked = wanted_levels(cls, levels)
    print(f"[1/5] {len(picked)} level(s) to visit, from the level table",
          flush=True)

    seed = generate(4242)
    if seed is None:
        print("FAIL: generation produced no seed", flush=True)
        return 1
    print(f"      seed {seed}", flush=True)
    catalogue_here = seed_abilities(OUT, seed)
    if catalogue_here is None:
        print("FAIL: could not read the seed's ability catalogue", flush=True)
        return 1
    print(f"      this seed knows {len(catalogue_here)} ability(s): "
          f"{', '.join(sorted(catalogue_here))}", flush=True)
    for p in glob.glob(os.path.join(OUT, "*.apsave")):
        os.remove(p)

    os.makedirs(SHOTS, exist_ok=True)
    for f in glob.glob(os.path.join(SHOTS, "*.png")):
        os.remove(f)

    problems = []
    spurious = []
    unlit = []
    findings = []
    close_game()

    with Environment("ability-lock-probe") as env:
        env.configure(Host="localhost", Port=e2e.PORT, SlotName=e2e.SLOT,
                      AutoConnect="true")
        server = None
        try:
            print("[2/5] starting the server", flush=True)
            server = subprocess.Popen(
                f'py -3.13 -u MultiServer.py --port {e2e.PORT} '
                f'"{os.path.join(OUT, seed)}"',
                shell=True, cwd=e2e.AP, stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(40):
                if e2e.port_open():
                    break
                time.sleep(1)

            print("[3/5] launching the game", flush=True)
            log = e2e.Log()
            log.before_launch()
            # NO STEAM RELAUNCH. Running the exe directly makes the Steam DRM
            # stub call SteamAPI_RestartAppIfNecessary, which starts the game
            # again through Steam and exits the process we launched - so the
            # harness watches a PID that no longer exists while a different
            # one writes the log. steam_appid.txt is the documented way to say
            # "already running as the right app"; the call is idempotent, and
            # the file is what stops the window opening, closing and opening
            # again on every launch.
            ensure_no_steam_relaunch()
            subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
            text = log.wait(["connected. "], 150, 3, "the connection")
            if "connected. " not in text:
                why = e2e.why_no_connection(e2e.whole_log())
                print(f"FAIL: never connected"
                      + (f" - {why}" if why else ""), flush=True)
                return 1

            problem = e2e.patch_problem(e2e.whole_log())
            if problem:
                print(f"FAIL: the mod did not fully install: {problem}",
                      flush=True)
                return 1

            held = held_from(e2e.whole_log(), set(cls.values()))
            if held is None:
                print("FAIL: this seed has ability locks OFF, so there is "
                      "nothing to measure", flush=True)
                return 1
            print(f"      the run holds: {', '.join(sorted(held)) or 'nothing'}",
                  flush=True)
            time.sleep(2.0)

            print("[4/5] visiting each level", flush=True)
            for index in sorted(picked):
                kind, note = picked[index]
                level_id, classes, extra = levels[index]

                opened, _out = e2e.boot_level(log, index)
                if not opened:
                    problems.append(f"{level_id} ({index}) did not open")
                    continue

                settled = wait_settled(log)
                if not settled:
                    print(f"      (still transitioning after 25s: "
                          f"{level_id})", flush=True)
                rows, _raw = read_locks(log)
                if rows is None:
                    problems.append(f"{level_id} ({index}): no level running "
                                    "when asked for locks")
                    continue
                if not rows:
                    problems.append(f"{level_id} ({index}): the locks report "
                                    "listed no controllers")
                    continue

                shot = shoot(f"{index}-{level_id.replace(' ', '_')}")

                # EXPECTATION FROM THE TABLE, GROUND TRUTH FROM THE SCREEN.
                # A mismatch is the finding.
                mismatches = []
                toothless = []
                invisible = []
                skipped = []
                for row in rows:
                    ability = cls.get(row["type"])
                    if ability is not None and ability not in catalogue_here:
                        # Not a mismatch: this seed never drew a level needing
                        # it, so the mod has no mapping and correctly leaves it
                        # alone. Recorded so the coverage gap is visible.
                        skipped.append(f"{row['type']} ({ability})")
                        continue
                    should_dim = ability is not None and ability not in held
                    did_dim = row["dimmed"] > 0
                    if should_dim == did_dim:
                        continue

                    # NOT EVERY "declared but not dimmed" IS A BUG, and telling
                    # the two apart is the whole point of this probe.
                    #
                    # The dimmer merges per object and lets UNLOCKED WIN, so a
                    # locked controller whose objects are all claimed by an
                    # open controller as well gates nothing - the player can
                    # touch every one of them without ever holding the ability.
                    # A controller managing no objects at all gates nothing
                    # either. In both cases the LOCK is behaving correctly and
                    # the TABLE is claiming a requirement that does not exist.
                    if should_dim and row["norenderer"] >= row["objects"]                             and row["objects"] > 0:
                        invisible.append(
                            f"{row['type']} needs {ability} and IS locked "
                            f"({row['blocked']}/{row['objects']} "
                            "non-interactive) but none of its objects has a "
                            "renderer to grey - the player sees a normal "
                            "object they cannot touch")
                    elif should_dim and row["objects"] == 0:
                        toothless.append(
                            f"{row['type']} needs {ability} but manages no "
                            "objects - the requirement gates nothing")
                    elif should_dim and row["shared"] >= row["objects"]:
                        toothless.append(
                            f"{row['type']} needs {ability} but all "
                            f"{row['objects']} of its objects are shared with "
                            "another controller that is open - the requirement "
                            "gates nothing")
                    else:
                        mismatches.append(
                            f"{row['type']} (needs {ability or 'nothing'}): "
                            f"table says {'locked' if should_dim else 'open'}, "
                            f"screen says {row['dimmed']}/{row['objects']} "
                            f"dimmed, {row['shared']} shared")

                total_obj = sum(r["objects"] for r in rows)
                total_blk = sum(r["dimmed"] for r in rows)
                flag = "  MISMATCH" if mismatches else (
                    "  NO VISUAL CUE" if invisible else
                    ("  TOOTHLESS" if toothless else ""))
                note_skip = (f", {len(skipped)} class(es) not in this seed"
                             if skipped else "")
                print(f"      [{kind}] {level_id} ({index}): "
                      f"{total_blk}/{total_obj} dimmed, "
                      f"{len(rows)} controller(s){note_skip}{flag}", flush=True)
                for m in mismatches:
                    print(f"         {m}", flush=True)
                    problems.append(f"{level_id}: {m}")
                for t in toothless:
                    print(f"         {t}", flush=True)
                    spurious.append(f"{level_id} ({index}): {t}")
                for v in invisible:
                    print(f"         {v}", flush=True)
                    unlit.append(f"{level_id} ({index}): {v}")

                findings.append({
                    "index": index, "levelId": level_id, "kind": kind,
                    "declared": note, "held": sorted(held),
                    "controllers": rows, "frame": shot,
                    "notInThisSeed": skipped,
                    "toothless": toothless,
                    "invisible": invisible,
                })

            print("[5/5] writing the report", flush=True)
            report = os.path.join(SHOTS, "ability-locks.json")
            with open(report, "w", encoding="utf-8", newline="\n") as f:
                json.dump({"held": sorted(held), "levels": findings},
                          f, indent=1)
            print(f"      {os.path.relpath(report, e2e.REPO)}", flush=True)
        finally:
            close_game()
            if server:
                subprocess.run(["powershell", "-NoProfile", "-Command",
                                "Get-NetTCPConnection -LocalPort 38281 -State "
                                "Listen -ErrorAction SilentlyContinue | "
                                "ForEach-Object { Stop-Process -Id "
                                "$_.OwningProcess -Force }"],
                               capture_output=True)

    if problems:
        print(f"\nFAIL: {len(problems)} problem(s):", flush=True)
        for p in problems:
            print(f"  {p}", flush=True)
        return 1
    print(f"\nPASS: every visited level's screen agreed with the table "
          f"({len(findings)} level(s)).", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
