r"""Classify every controller in every level, and say why.

WHY THIS EXISTS
---------------

"Which puzzles have dependencies, and which have mini solutions" was answered
three times by inference and got a different answer each time. Tool Drawer,
TupperwareNesting and Candy Canes were each diagnosed from a playtest report,
patched by hand, and the patch was wrong at least once - the TupperwareNesting
phase order was guessed as a fan out of Lids and Stack 1 when the game declares
a chain that does not involve Lids at all.

The game knows. PhasedLevel.phases, TupperwareNesting.GetPhaseControllers() and
RadialDanceParty.dances are authored, ordered declarations; the levelsweep now
records them. This script turns that into one row per controller so the answer
is a table anyone can read rather than a conclusion someone reached.

    py -3.13 tools/classify-controllers.py [--write]

--write updates docs/data/controller-classes.tsv.

THE CLASSES
-----------

always-on       registers at boot, nothing gates it
phase-revealed  named in the level's declared phase list
phase-driver    the component that ADVANCES phases; not a puzzle group
mutual          engine-declared bidirectional pair; merges into one group
gated           carries a dependsOn edge
seed-varying    present in only some generator seeds
ghost           in the prefab, in no phase list, never registers
non-puzzle      Pannables and friends, filtered before grouping
"""

import collections
import csv
import io
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TABLE = os.path.join(REPO, "apworld", "alttl", "data", "levels.json")
ABILITIES = os.path.join(REPO, "apworld", "alttl", "data", "abilities.json")
SURVEY = os.path.join(REPO, "fixtures", "controller-survey.tsv")
GENSWEEP = os.path.join(REPO, "docs", "data", "generator-sweep.tsv")
OUT = os.path.join(REPO, "docs", "data", "controller-classes.tsv")


def main(argv):
    write = "--write" in argv

    levels = json.load(io.open(TABLE, encoding="utf-8"))["levels"]
    ab = json.load(io.open(ABILITIES, encoding="utf-8"))
    cls = {}
    for ability, classes in ab["abilities"].items():
        for c in classes:
            cls[c] = ability
    notp = set(ab["notPuzzles"])
    baseline = set(ab["baseline"])

    survey = collections.defaultdict(dict)
    with io.open(SURVEY, encoding="utf-8") as fh:
        for r in csv.DictReader(fh, delimiter="\t"):
            if r.get("controller"):
                survey[r["levelId"]][r["controller"]] = r["controllerType"]

    # Which generator controllers are not present in every sampled seed.
    seeds = collections.defaultdict(lambda: collections.defaultdict(set))
    with io.open(GENSWEEP, encoding="utf-8") as fh:
        for r in csv.DictReader(fh, delimiter="\t"):
            for name in (r.get("controllers") or "").split("|"):
                if name:
                    seeds[r["levelId"]][name].add(r["seed"])
    seed_totals = {lid: len({s for c in d.values() for s in c})
                   for lid, d in seeds.items()}
    varying = {(lid, name)
               for lid, d in seeds.items()
               for name, ss in d.items()
               if len(ss) < seed_totals[lid]}

    rows = []
    unusable_phases = []
    for level in levels:
        lid = level["levelId"]
        phases = level.get("phases") or []
        recorded = {c["name"] for c in level["controllers"]}

        # A PHASE LIST THAT NAMES NO CONTROLLER IS NOT A PHASE LIST.
        #
        # Every use of `phases` below is `name in phases`, so an entry that
        # matches no controller simply never fires and says nothing. That is
        # indistinguishable from a level with no phases at all, and it is not
        # hypothetical: DataTable.PhasesOf writes the literal string "(none)"
        # whenever a phase's PhaseController is null, and DLC2 Ghost Cat
        # carries nine of them. Nine unusable entries classified nothing and
        # produced no warning - the file read as complete.
        #
        # Reported rather than raised: the tsv is still worth writing, and the
        # point is that a human sees the gap instead of inferring it later.
        # A phase-revealed controller on such a level falls through to
        # "ghost", which this tool treats as minting no location and implying
        # no ability - the understating direction.
        dead = [p for p in phases if p not in recorded]
        if dead:
            unusable_phases.append((lid, dead))

        for c in level["controllers"]:
            name, ctype = c["name"], c["type"]
            depends = c.get("dependsOn") or []
            mutual = any(
                name in (o.get("dependsOn") or [])
                for o in level["controllers"] if o["name"] in depends)

            if ctype in notp:
                klass = "non-puzzle"
            elif (lid, name) in varying:
                klass = "seed-varying"
            elif mutual:
                klass = "mutual"
            elif name in phases:
                klass = "phase-revealed"
            elif depends:
                klass = "gated"
            else:
                klass = "always-on"

            rows.append((lid, level.get("levelClass", "Level"), name, ctype,
                         "" if ctype in baseline else cls.get(ctype, "?"),
                         klass, ";".join(depends),
                         str(phases.index(name) + 1) if name in phases else ""))

        # Everything the prefab has and the table does not.
        for name, ctype in sorted(survey.get(lid, {}).items()):
            if name in recorded:
                continue
            if name in phases:
                klass = "phase-revealed"          # real, and missing
            elif ctype in notp:
                klass = "non-puzzle"
            elif (lid, name) in varying:
                klass = "seed-varying"
            elif ctype in ("TupperwareNesting", "RadialDanceParty"):
                klass = "phase-driver"
            else:
                klass = "ghost"
            rows.append((lid, level.get("levelClass", "Level"), name, ctype,
                         "" if ctype in baseline else cls.get(ctype, "?"),
                         klass, "",
                         str(phases.index(name) + 1) if name in phases else ""))

    counts = collections.Counter(r[5] for r in rows)
    print("controllers classified: %d" % len(rows), flush=True)
    for k, n in counts.most_common():
        print("   %-16s %d" % (k, n), flush=True)

    if unusable_phases:
        print("\nWARNING: phase entries naming no controller on this level.",
              flush=True)
        print("Nothing below can ever match, so these levels classify as if "
              "they declared no phases at all:", flush=True)
        for lid, dead in unusable_phases:
            shown = ", ".join(dead[:4]) + (" ..." if len(dead) > 4 else "")
            print("   %-24s %d of %d unusable: %s"
                  % (lid, len(dead),
                     len([lv for lv in levels
                          if lv["levelId"] == lid][0].get("phases") or []),
                     shown), flush=True)
        print("   '(none)' means the sweep read a null PhaseController and "
              "wrote a placeholder instead of failing.", flush=True)

    print("\nlevels declaring phases (the mini-solution levels):", flush=True)
    for level in levels:
        if level.get("phases"):
            print("   %-24s %-24s %d phases: %s"
                  % (level["levelId"], level.get("levelClass"),
                     len(level["phases"]), " -> ".join(level["phases"])),
                  flush=True)

    print("\nREAL but not in the table (missing locations):", flush=True)
    missing = [r for r in rows if r[5] == "phase-revealed" and not r[6]
               and r[2] not in {c["name"] for lv in levels
                                if lv["levelId"] == r[0]
                                for c in lv["controllers"]}]
    for r in missing:
        print("   %-24s %-26s phase %s" % (r[0], r[2], r[7]), flush=True)
    print("   total: %d" % len(missing), flush=True)

    if write:
        with io.open(OUT, "w", encoding="utf-8", newline="") as fh:
            fh.write("levelId\tlevelType\tcontroller\tcontrollerType\t"
                     "ability\tclass\tdependsOn\tphaseIndex\n")
            for r in rows:
                fh.write("\t".join(r) + "\n")
        print("\nwritten: %s" % OUT, flush=True)
    else:
        print("\n(dry run - pass --write to update %s)" % OUT, flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
