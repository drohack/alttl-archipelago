"""Backfill the `drawers` block onto levels.json rows that predate it.

    py -3.13 tools/merge-drawers.py <swept.json> [--write]

WHY THIS IS A SEPARATE TOOL FROM merge-levels.py. That one appends whole rows
and refuses to touch a levelId it already has, because a fresh sweep is a WORSE
copy of the hand-audited rows: it cannot see Radial Dance Party's phases or
TupperwareNesting's, and it would silently undo the 2026-09-08 phase audit.
That caution is right and is not relaxed here.

This tool has a much narrower contract, and the narrowness is the safety:

  * it writes exactly ONE key, `drawers`, and only where the row does not
    already have it
  * it never adds, removes or reorders a level
  * it never touches controllers, phases, extraAbilities or anything else
  * a level absent from the sweep is left alone rather than cleared

So it cannot undo an audit, because it cannot change anything an audit
produced.

WHY IT NEEDS TO EXIST AT ALL. The sweep has always harvested each Drawer's
UnlockOnSolvedControllers and OpenOnSolvedControllers - the game's own authored
answer to "what must be solved before this opens". merge-levels.py projected
that block away as something "nothing downstream reads". It was not read
because it was never kept, and the cost showed up in play on 2026-09-21: a
drawer's contents are not an ObjectController.dependencies edge, so every group
inside every drawer was recorded as needing nothing, the generator put a
Progressive Puzzle Pack behind a shut drawer, and the run died with no way out.

The eight drawer edges that DO exist in levels.json were hand-written from a
playtest report - see test_a_drawer_cannot_be_emptied_before_it_opens, which
pins them as a literal set across four base-game levels. Every DLC drawer level
arrived afterwards with nothing. This is the measured version of that list.

DRY RUN BY DEFAULT. It prints what it would change and writes nothing unless
--write is passed, because the one thing worse than missing data here is data
nobody looked at.
"""
import importlib.util
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
TABLE = os.path.join(REPO, "apworld", "alttl", "data", "levels.json")


def _sibling(name, path):
    """Import a tool whose filename has a hyphen in it.

    `import merge_levels` cannot work - the file is merge-levels.py and a
    hyphen is not a Python identifier. Loaded by path rather than copying
    DRAWER_KEYS, because two lists of field names that must agree and live in
    different files will eventually stop agreeing, and the failure would be a
    silently narrower drawer block.
    """
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


DRAWER_KEYS = _sibling(
    "alttl_merge_levels", os.path.join(HERE, "merge-levels.py")).DRAWER_KEYS


def project(drawers):
    """Keep the durable parts of the sweep's drawer block."""
    return [{k: d[k] for k in DRAWER_KEYS if k in d} for d in drawers]


def interesting(drawers):
    """A drawer block worth recording.

    A level whose drawers gate nothing and contain nothing tells us as little
    as no block at all, and writing it would add noise to a file people read.
    """
    return any(d.get("containsControllers") or d.get("unlockOn")
               or d.get("openOn") for d in drawers)


def main():
    args = [a for a in sys.argv[1:] if a != "--write"]
    write = "--write" in sys.argv
    if not args:
        raise SystemExit(__doc__)

    raw = io.open(TABLE, encoding="utf-8", newline="").read()
    ending = "\r\n" if "\r\n" in raw else "\n"
    table = json.loads(raw)
    rows = {level["levelId"]: level for level in table["levels"]}
    print(f"levels.json holds {len(rows)} level(s)", flush=True)

    swept = {}
    for path in args:
        with io.open(path, encoding="utf-8") as fh:
            data = json.load(fh)
        for row in (data["levels"] if isinstance(data, dict) else data):
            swept[row["levelId"]] = row
    print(f"the sweep holds {len(swept)} level(s)", flush=True)

    added, already, empty, unknown = [], [], [], []
    for level_id, row in sorted(swept.items()):
        if level_id not in rows:
            unknown.append(level_id)
            continue
        drawers = row.get("drawers") or []
        if not interesting(drawers):
            empty.append(level_id)
            continue
        if "drawers" in rows[level_id]:
            already.append(level_id)
            continue
        rows[level_id]["drawers"] = project(drawers)
        added.append(level_id)

    for level_id in added:
        block = rows[level_id]["drawers"]
        gated = sum(len(d.get("containsControllers") or []) for d in block)
        print(f"  + {level_id}: {len(block)} drawer(s), "
              f"{gated} controller group(s) inside", flush=True)

    print("", flush=True)
    print(f"  {len(added)} level(s) would gain a drawers block", flush=True)
    print(f"  {len(already)} already had one (left alone)", flush=True)
    print(f"  {len(empty)} swept with nothing worth recording", flush=True)
    if unknown:
        print(f"  {len(unknown)} in the sweep but not in levels.json - use "
              f"merge-levels.py for those: {', '.join(unknown[:5])}"
              + (" ..." if len(unknown) > 5 else ""), flush=True)

    missing = sorted(set(rows) - set(swept))
    if missing:
        print(f"  {len(missing)} in levels.json but NOT in this sweep, left "
              f"untouched: {', '.join(missing[:5])}"
              + (" ..." if len(missing) > 5 else ""), flush=True)

    if not write:
        print("", flush=True)
        print("Done: dry run, nothing written. Pass --write to apply.",
              flush=True)
        return 0

    with io.open(TABLE, "w", encoding="utf-8", newline=ending) as fh:
        json.dump(table, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    print("", flush=True)
    print(f"Done: wrote {len(added)} drawers block(s) to levels.json",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
