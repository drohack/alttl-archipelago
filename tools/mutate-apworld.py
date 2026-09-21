"""Put each DLC bug back and check test_dlc.py notices.

A test that has never failed has not been tested. test_dlc.py passed the
moment it was written, which proves nothing on its own - so every bug it
is supposed to catch gets reintroduced here, one at a time, and the suite
must go red for each.

MUTATES THE SYNCED COPY, not the repo. Archipelago/worlds/alttl is a
disposable copy that ap-sync.ps1 rebuilds, so a crash mid-run cannot
damage the source. The copy is restored in a finally regardless, and the
run starts by syncing so it is never testing stale code.

    py -3.13 tools/mutate-apworld.py
"""
import io
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
AP = os.path.join(REPO, "Archipelago")
WORLD = os.path.join(AP, "worlds", "alttl")
MODULE = "worlds.alttl.test.test_dlc"

#: (name, file, find, replace). Each `find` must appear exactly once.
MUTATIONS = [
    ("the content gate keyed on source instead of dlc",
     "slots.py",
     "        if level.dlc and level.dlc not in dlc_on:",
     "        if level.source.startswith('dlc') and level.dlc not in dlc_on:"),

    ("the content gate removed entirely",
     "slots.py",
     "        if level.dlc and level.dlc not in dlc_on:\n            continue",
     "        if False:\n            continue"),

    ("the bypass never subtracted, so logic overstates what a level needs",
     "data.py",
     "        self.enforced_abilities: FrozenSet[str] = self.abilities - bypassed",
     "        self.enforced_abilities: FrozenSet[str] = self.abilities"),

    ("the bypass subtracted from the DRAW view, moving every frozen plan",
     "data.py",
     "        self.enforced_abilities: FrozenSet[str] = self.abilities - bypassed",
     "        self.abilities = self.abilities - bypassed\n"
     "        self.enforced_abilities: FrozenSet[str] = self.abilities"),

    ("requirements reading the draw view, so the subtraction is dead code",
     "rules.py",
     "        level_abilities = (sorted(level.enforced_abilities)",
     "        level_abilities = (sorted(level.abilities)"),

    ("part requirements reading the draw view",
     "rules.py",
     "                part_abilities = (sorted(level.enforced_part_abilities.get(part, ()))",
     "                part_abilities = (sorted(level.part_abilities.get(part, ()))"),

    ("DLC locations interleaved with the base ids",
     "locations.py",
     "        target = dlc if level.dlc else base",
     "        target = base"),

    ("credits no longer the last base id",
     "locations.py",
     "    return base + [data.CREDITS] + dlc",
     "    return base + dlc + [data.CREDITS]"),

    ("the DLC ability item spliced in among the base twelve",
     "items.py",
     "    + DLC_ABILITY_ITEMS",
     "    + []"),

    ("live abilities filtered through the base twelve, dropping Distributing",
     "pool.py",
     "    world.live_abilities = [a for a in data.ALL_ABILITIES if a in live]",
     "    world.live_abilities = [a for a in data.ABILITIES if a in live]"),

    ("the seeing_stars flag dropped from the handoff",
     "pool.py",
     '        "seeing_stars": bool(world.options.seeing_stars.value),',
     '        "seeing_stars": False,'),

    ("the cupboards flag hard-coded, so the mod cannot refuse a bad connect",
     "pool.py",
     '        "cupboards_and_drawers": bool(world.options.cupboards_and_drawers.value),',
     '        "cupboards_and_drawers": False,'),

    ("abilities exported without classes_for, which KeyErrors on a DLC one",
     "pool.py",
     '        "abilities": {a: data.classes_for(a) for a in world.live_abilities},',
     '        "abilities": {a: data.ABILITY_CLASSES.get(a, []) '
     'for a in world.live_abilities},'),
]


def run_suite():
    """The suite's exit code. Non-zero means it noticed something."""
    return subprocess.run(
        [sys.executable, "-m", "unittest", MODULE],
        cwd=AP, capture_output=True, text=True,
        env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1")).returncode


def main():
    sync = subprocess.run(
        ["powershell", "-NoProfile", "-File",
         os.path.join(HERE, "ap-sync.ps1")],
        capture_output=True, text=True)
    if sync.returncode != 0:
        sys.exit(f"could not sync the apworld:\n{sync.stdout}\n{sync.stderr}")

    # BASELINE FIRST, or every result is a lie. A suite that cannot import
    # exits non-zero for every mutation and reports a perfect score while
    # testing nothing - which is exactly how mutate-scheduler.py once
    # printed 9 of 9 against a file with a syntax error.
    if run_suite() != 0:
        sys.exit("baseline: the suite does not pass unmutated. Fix that "
                 "first - every mutation below would score CAUGHT for the "
                 "wrong reason.")
    print("baseline: the suite passes unmutated", flush=True)

    originals = {}
    for _n, filename, _f, _r in MUTATIONS:
        path = os.path.join(WORLD, filename)
        if filename not in originals:
            originals[filename] = io.open(path, encoding="utf-8").read()

    caught = 0
    try:
        for i, (name, filename, find, replace) in enumerate(MUTATIONS, 1):
            path = os.path.join(WORLD, filename)
            src = originals[filename]
            seen = src.count(find)
            if seen != 1:
                # A MISSING ANCHOR IS A FAILURE, NOT A SKIP. It means the
                # mutation tested nothing and nobody would know.
                print(f"[{i}/{len(MUTATIONS)}] SKIP  {name}: anchor appears "
                      f"{seen} times in {filename} - the test cannot be "
                      f"trusted until this is fixed", flush=True)
                continue
            io.open(path, "w", encoding="utf-8").write(src.replace(find, replace))
            rc = run_suite()
            io.open(path, "w", encoding="utf-8").write(src)
            verdict = "CAUGHT" if rc != 0 else "MISSED"
            caught += rc != 0
            print(f"[{i}/{len(MUTATIONS)}] {verdict}  {name}", flush=True)
    finally:
        for filename, src in originals.items():
            io.open(os.path.join(WORLD, filename), "w",
                    encoding="utf-8").write(src)

    print(f"Done: {caught}/{len(MUTATIONS)} mutations caught", flush=True)
    sys.exit(0 if caught == len(MUTATIONS) else 1)


if __name__ == "__main__":
    main()
