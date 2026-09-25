"""Splice swept rows into levels.json without touching the rows already there.

    py -3.13 tools/merge-levels.py <swept.json> [<swept.json> ...]

WHY MERGE RATHER THAN REPLACE. A fresh sweep is not a better copy of this
file - it is a WORSE one for the levels that were audited by hand. Measured
2026-09-17, a full sweep taken with both DLCs installed disagreed with the
shipped table on five levels, and the shipped side was right every time:

    Radial Dance Party   12 controllers -> 0      phases the sweep cannot see
    TupperwareNesting     8 controllers -> 2      same
    TupperwareTower       1 controller  -> 3      two prefab ghosts restored
    SomethingEggstra Fridge 1           -> 3      same
    PawPrints            hintAvailable true -> false

A ghost mints a location nobody can ever check; a missing phase loses a
location and can hide an ability the level really needs. SurveyCrossCheckTests
pins each of these deliberately. So copying a fresh sweep over levels.json
silently undoes the 2026-09-08 phase audit, and this tool exists so that
cannot happen by accident.

It appends only levels the table does not already have. An existing levelId is
reported and SKIPPED - if a row genuinely needs to change, change it
deliberately rather than through a bulk copy.

The existing text is preserved byte for byte, which is checked before writing.
"""
import io
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TABLE = os.path.join(REPO, "apworld", "alttl", "data", "levels.json")

#: The shipped table is a PROJECTION of the sweep, not the whole of it: the
#: sweep also emits objectIds, objectsGatedFirst and matchDependencySolutions,
#: which nothing downstream reads. New rows are projected the same way so the
#: file stays one shape.
#:
#: `drawers` USED TO BE ON THAT LIST AND IT COST A RUN. The comment here said
#: it was among the fields "nothing downstream reads", which was true and was
#: the bug: a drawer's contents are not an ObjectController.dependencies edge,
#: so dropping this block left every group inside every drawer recorded as
#: needing nothing at all. The generator believed it, put a Progressive Puzzle
#: Pack behind a shut drawer, and the run ended. It is kept now, and
#: tools/merge-drawers.py backfills it onto the rows that predate the change.
LEVEL_KEYS = ["levelIndex", "levelId", "source", "dlc", "solutionCount",
              "isRandomizable", "isArchived", "isDailyTidy", "controllers",
              "hintAvailable", "hintImages", "cats", "randomizerHintPool",
              "randomizerHints", "isHolidayDaily", "levelClass"]

#: Present only where they say something, matching the shipped rows.
OPTIONAL = ["extraAbilities", "phases", "drawers"]

#: Of the drawer block, the parts that say something durable. `contains` is a
#: count of objects and `containsControllers` resolves those objects to the
#: controller names the logic actually speaks in - see DataTable
#: .ContainedControllersOf, which does the join while the instance ids are
#: still live.
DRAWER_KEYS = ["name", "contains", "containsControllers", "unlockOn", "openOn",
               "subDrawers"]

CONTROLLER_KEYS = ["name", "type", "objects", "dependsOn"]


def project(row):
    out = {}
    for key in LEVEL_KEYS:
        if key == "controllers":
            out["controllers"] = [
                {k: c[k] for k in CONTROLLER_KEYS if k in c}
                for c in row["controllers"]
            ]
        elif key == "dlc":
            # Omitted rather than written empty on base-game rows, so the field
            # means "this level needs a DLC" wherever it appears.
            if row.get("dlc"):
                out["dlc"] = row["dlc"]
        elif key in row:
            out[key] = row[key]
    for key in OPTIONAL:
        value = row.get(key)
        if not value:
            continue
        if key == "drawers":
            out[key] = [{k: d[k] for k in DRAWER_KEYS if k in d}
                        for d in value]
        else:
            out[key] = value
    return out


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)

    raw = io.open(TABLE, encoding="utf-8", newline="").read()
    ending = "\r\n" if "\r\n" in raw else "\n"
    table = json.loads(raw)
    have = {l["levelId"] for l in table["levels"]}
    before = len(table["levels"])
    print(f"levels.json holds {before} level(s)", flush=True)

    adding, skipped = [], []
    for path in sys.argv[1:]:
        with io.open(path, encoding="utf-8") as fh:
            rows = json.load(fh)
        rows = rows["levels"] if isinstance(rows, dict) else rows
        for row in rows:
            name = row["levelId"]
            if name in have or name in {r["levelId"] for r in adding}:
                skipped.append(name)
                continue
            adding.append(row)
        print(f"  {os.path.basename(path)}: {len(rows)} row(s) read", flush=True)

    if skipped:
        print(f"skipped {len(skipped)} level(s) already in the table "
              "(change those deliberately, not in bulk)", flush=True)
    if not adding:
        print("Done: nothing to add", flush=True)
        return 0

    adding.sort(key=lambda r: r["levelIndex"])
    body = ",".join(
        ending + _indent(json.dumps(project(r), indent=2), ending)
        for r in adding)

    # Splice before the closing bracket of "levels", so every byte already in
    # the file stays exactly where it was.
    marker = ending + "  ]" + ending
    if raw.count(marker) != 1:
        raise SystemExit("could not find the single closing bracket of levels")
    head, tail = raw.split(marker)
    merged = head + "," + body + marker + tail

    check = json.loads(merged)
    if len(check["levels"]) != before + len(adding):
        raise SystemExit("the merged file does not hold the expected rows")
    if check["levels"][:before] != table["levels"]:
        raise SystemExit("an existing row changed; refusing to write")
    if not merged.startswith(head):
        raise SystemExit("the existing text moved; refusing to write")

    io.open(TABLE, "w", encoding="utf-8", newline="").write(merged)
    print(f"Done: {before} + {len(adding)} = {len(check['levels'])} levels; "
          "the rows already present are byte-identical", flush=True)
    return 0


def _indent(text, ending):
    return ending.join("    " + line for line in text.split("\n"))


if __name__ == "__main__":
    sys.exit(main())
