"""Which ability gates can be bypassed, and what does it take to bypass them?

THE QUESTION THE ZERO-ABILITY SWEEP CANNOT ANSWER. probe-toothless-gates.py
finds gates whose objects are all shared with a BASELINE group - Books 3,
Workbench, TrickOrTidy_ChocolateBars - because those leak even holding
nothing. It is blind to the other shape: Spoons' Ordering group shares all
seven spoons with a Stacking group, so it leaks the moment you hold Stacking
and looks perfectly healthy when you hold neither.

Brute force is not available. Archipelago items cannot be un-sent, so every
partial holding needs its own server session - about 35 for the non-DLC
levels alone, each with a generate and a launch.

It is a static question anyway. Given which controllers hold which objects,
"is group G still gated when the player holds S" is arithmetic: G is bypassed
when every object it manages is ALSO held by some controller that is either
baseline or unlocked by something in S. So dump membership once and answer it
for every subset at once.

The dump comes from DevTools' `sharing:`, which walks a controller's full
object set - ManagedObjects plus the collections its concrete class declares
itself, since ManagedObjects alone missed Dirtyables' coins entirely.

    py -3.13 tools/probe-object-sharing.py [--dlc]
"""
import collections
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
from harness_env import close_game, ensure_no_steam_relaunch

REPO = e2e.REPO
OUT = os.path.join(REPO, "testserver", "out-abilitytest")
CMDS = os.path.join(REPO, "testserver", "abilitytest-cmds.txt")
SERVER_LOG = os.path.join(REPO, "testserver", "abilitytest-server.log")
DUMP = os.path.join(e2e.GAME, "BepInEx", "alttl-sharing.tsv")
REPORT = os.path.join(REPO, "docs", "gate-sharing.md")

#: Classes that hold objects without acting on them. Mirrors
#: ObjectLock.Enclosures in Core: the dimmer never lets them free an object
#: another group holds, so they are no route around a gate.
ENCLOSURES = {"DrawerController", "DrawerExpandableController", "Cupboard"}

WANT_DLC = "--dlc" in sys.argv


def tables():
    with open(os.path.join(REPO, "apworld", "alttl", "data", "abilities.json"),
              encoding="utf-8") as f:
        ab = json.load(f)
    owner = {}
    for group in ("abilities", "dlcAbilities"):
        for ability, classes in (ab.get(group) or {}).items():
            for cls in classes:
                owner[cls] = ability

    with open(os.path.join(REPO, "apworld", "alttl", "data", "levels.json"),
              encoding="utf-8") as f:
        lv = json.load(f)
    levels = [(l["levelIndex"], l["levelId"]) for l in lv["levels"]
              if len(l["controllers"]) > 1
              and (WANT_DLC or not l["source"].startswith("dlc"))]
    return owner, levels


def sweep(levels):
    """Open each level and append its membership to the dump."""
    close_game()
    seeds = sorted((f for f in os.listdir(OUT) if f.endswith(".zip")),
                   key=lambda f: os.path.getmtime(os.path.join(OUT, f)))
    if not seeds:
        sys.exit("no seed in testserver/out-abilitytest")

    if not e2e.port_open():
        open(CMDS, "w", encoding="utf-8").close()
        log_file = open(SERVER_LOG, "w", encoding="utf-8")
        print(f"step 0: starting the server on {e2e.PORT}", flush=True)
        subprocess.Popen(
            f'tail -f "{CMDS}" | py -3.13 -u MultiServer.py '
            f'--port {e2e.PORT} "{os.path.join(OUT, seeds[-1])}"',
            shell=True, cwd=e2e.AP, stdout=log_file, stderr=subprocess.STDOUT)
        for _ in range(40):
            if e2e.port_open():
                break
            time.sleep(1)
    if not e2e.port_open():
        sys.exit("FAIL: the server never bound the port")

    log = e2e.Log()
    log.before_launch()
    ensure_no_steam_relaunch()
    subprocess.Popen([e2e.EXE], cwd=e2e.GAME)
    if "connected. " not in log.wait(["connected. "], 150, 1, "the connection"):
        sys.exit("FAIL: " + (e2e.why_no_connection(e2e.whole_log()) or "no connect"))
    time.sleep(4.0)

    if os.path.exists(DUMP):
        os.remove(DUMP)

    seen = 0
    for n, (index, name) in enumerate(levels, 1):
        e2e.dev(f"boot:{index}", 8.0)
        time.sleep(1.2)
        e2e.dev(f"sharing:{'new' if seen == 0 else 'append'}", 1.5)
        time.sleep(0.8)
        seen += 1
        print(f"[{n}/{len(levels)} {name}] step 1/2: membership dumped",
              flush=True)

    close_game()
    return seen


def analyse(owner):
    """Work out, per gated controller, what frees it."""
    if not os.path.exists(DUMP):
        sys.exit(f"no dump at {DUMP}")

    # level -> controller -> (type, {objectId})
    holds = collections.defaultdict(lambda: collections.defaultdict(
        lambda: ["", set()]))
    with open(DUMP, encoding="utf-8") as f:
        next(f, None)
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 5:
                continue
            level, ctrl, ctype, oid, _oname = parts[:5]
            entry = holds[level][ctrl]
            entry[0] = ctype
            entry[1].add(oid)

    free, conditional = [], []
    for level, controllers in sorted(holds.items()):
        for ctrl, (ctype, objects) in sorted(controllers.items()):
            ability = owner.get(ctype)
            if ability is None or not objects:
                continue          # not a gate

            # For each object, which OTHER holders could free it?
            # None at all -> that object is genuinely held only by this gate.
            per_object = []
            for oid in objects:
                openers = set()
                baseline = False
                for other, (otype, oobjects) in controllers.items():
                    if other == ctrl or oid not in oobjects:
                        continue
                    if otype in ENCLOSURES:
                        continue      # holds it, does not free it (ObjectLock)
                    other_ability = owner.get(otype)
                    if other_ability is None:
                        baseline = True      # ungated: always frees it
                    elif other_ability != ability:
                        openers.add(other_ability)
                per_object.append((baseline, openers))

            if all(b for b, _ in per_object):
                free.append((level, ctrl, ctype, ability, len(objects)))
                continue
            if any(not b and not o for b, o in per_object):
                continue          # at least one object only this gate holds

            # Every object is freed by baseline or by some other ability.
            needed = set()
            for baseline, openers in per_object:
                if not baseline:
                    needed |= openers
            conditional.append(
                (level, ctrl, ctype, ability, len(objects), sorted(needed)))

    return free, conditional


def write(free, conditional, swept):
    lines = [
        "# Ability gates and what bypasses them",
        "",
        "Generated by `tools/probe-object-sharing.py` from DevTools'",
        "`sharing:` dump of which controllers hold which objects. The dimmer",
        "merges per object and lets UNLOCKED WIN among the groups that act on it,",
        "so a gated group is bypassed whenever every object it holds is also held",
        "by a group the player can already touch. A drawer or cupboard does not",
        "count: it holds what sits inside it and frees nothing another group holds",
        "(`ObjectLock`).",
        "",
        f"Swept {swept} multi-controller level(s).",
        "",
        "## Bypassed by nothing at all",
        "",
        "Every object also held by a BASELINE group, so these gate nothing",
        "even for a player holding no abilities whatsoever. Confirmed by hand",
        "on Books 3: droha moved all 17 books and completed the arrangement",
        "holding zero abilities.",
        "",
    ]
    if free:
        lines += ["| Level | Controller | Class | Ability | Objects |",
                  "|---|---|---|---|---|"]
        lines += [f"| {l} | {c} | {t} | {a} | {n} |" for l, c, t, a, n in free]
    else:
        lines.append("None.")
    lines += [
        "",
        "## Bypassed once the player holds something else",
        "",
        "These hold up for a player with nothing, and stop gating the moment",
        "the listed ability arrives - which is why a zero-ability sweep cannot",
        "see them. Spoons is the known example: its Ordering group shares all",
        "seven spoons with a Stacking group, and droha completed it, earning",
        "the Ordering-gated location, while holding only Stacking.",
        "",
    ]
    if conditional:
        lines += ["| Level | Controller | Class | Ability | Objects | Bypassed by |",
                  "|---|---|---|---|---|---|"]
        lines += [f"| {l} | {c} | {t} | {a} | {n} | {', '.join(b)} |"
                  for l, c, t, a, n, b in conditional]
    else:
        lines.append("None.")
    lines.append("")
    with open(REPORT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def main():
    owner, levels = tables()
    print(f"-- {len(levels)} multi-controller level(s) "
          f"({'with' if WANT_DLC else 'without'} DLC) --", flush=True)
    swept = sweep(levels)
    free, conditional = analyse(owner)
    write(free, conditional, swept)
    print(f"Done: {len(free)} gate(s) bypassed by nothing, "
          f"{len(conditional)} bypassed by another ability "
          f"-> {os.path.relpath(REPORT, REPO)}", flush=True)


if __name__ == "__main__":
    main()
