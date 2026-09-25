"""Turn record-unlocks recordings into edge proposals, one level at a time.

    py -3.13 tools/analyse-unlocks.py [levelIndex ...]

Reads testserver/logs/unlocks/<index>.json (written by record-unlocks.py while
a person played the level with ability locks OFF) and compares what happened
with levels.json:

  MISSING   the group opened only after another group, and levels.json does not
            say so. The edge to add (tightening, via tools/add-edges.py).
  OVER      levels.json has an edge the play contradicts: the group was open
            before its supposed prerequisite. Overstatement - safe, left alone.
  REVIEW    something only the player can settle: a group that never opened,
            never finished, opened with nothing solved before it well after the
            intro, or shares objects with another group while never finished.

A group open from the first poll needs nothing else, whatever order it was
solved in - which is why the player's own order does not matter here.
"""
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

DIR = os.path.join(e2e.REPO, "testserver", "logs", "unlocks")
TABLE = os.path.join(e2e.REPO, "apworld", "alttl", "data", "levels.json")

#: Opening within this long of the first poll, with nothing solved, is the
#: level still coming up (Sharp Pencils read stuck for ~58s; droha saw
#: everything movable). Longer than this is a question for the player.
INTRO = 60.0

OPENERS = {"DrawerController", "DrawerExpandableController", "Cupboard",
           "AnimScrubbables", "HangingToolsController"}


def closure(deps, name, seen=None):
    seen = set() if seen is None else seen
    for d in deps.get(name, []):
        if d not in seen:
            seen.add(d)
            closure(deps, d, seen)
    return seen


def prerequisite(name, g, groups, solves):
    """Which group opened this one, or None when nothing had to come first."""
    opened = g.get("opened")
    first = min(o["appeared"] for o in groups.values())
    if opened is None or opened <= first:
        return None
    # An opener freeing objects in the same poll wins: a drawer reads solved
    # while it is shut, so "last solved" would name the wrong thing.
    for other, o in groups.items():
        if other == name:
            continue
        kind = o.get("type") or ""
        history = o.get("stuck") or []
        freed_now = any(ts == opened and v < prev for (ts, v), (_, prev)
                        in zip(history[1:], history[:-1]))
        opened_now = o.get("opened") == opened and o["stuckAtFirst"] > 0
        if (kind in OPENERS or "Drawer" in other or "Cupboard" in other) and (
                freed_now or opened_now):
            return other
    before = [n for (ts, n) in solves if ts <= opened and n != name]
    return before[-1] if before else None


def analyse(index, table):
    path = os.path.join(DIR, f"{index}.json")
    with open(path, encoding="utf-8") as fh:
        rec = json.load(fh)
    level = next(l for l in table["levels"] if l["levelIndex"] == index)
    deps = {c["name"]: list(c.get("dependsOn") or []) for c in level["controllers"]}
    known = set(deps)
    groups = rec["groups"]
    solves = [tuple(s) for s in rec["solveOrder"]]
    first = min((g["appeared"] for g in groups.values()), default=0)

    missing, over, review = [], [], []
    for name, g in sorted(groups.items(), key=lambda kv: kv[1]["appeared"]):
        if name not in known:
            review.append(f"{name}: not in levels.json (a live-only controller)")
            continue
        pre = prerequisite(name, g, groups, solves)
        if g.get("opened") is None:
            review.append(f"{name}: never opened while recorded")
        elif pre is None and g["opened"] > first + INTRO:
            review.append(f"{name}: opened at {g['opened']:.0f}s with nothing "
                          f"solved before it")
        if g.get("solved") is None:
            review.append(f"{name}: never finished"
                          + (f" (shares {g['shared']} object(s))"
                             if g.get("shared") else ""))
        if pre and pre in known and pre not in closure(deps, name):
            missing.append((name, pre))
        # Overstatement: a declared prerequisite that had NOT been solved or
        # opened when this group was already open.
        for d in deps.get(name, []):
            dg = groups.get(d)
            if dg and g.get("opened") is not None and g["opened"] <= first:
                over.append((name, d))
    return level["levelId"], missing, over, review


def main():
    wanted = [int(a) for a in sys.argv[1:] if a.isdigit()]
    with open(TABLE, encoding="utf-8") as fh:
        table = json.load(fh)
    files = sorted(int(f[:-5]) for f in os.listdir(DIR)
                   if f.endswith(".json") and f[:-5].isdigit())
    indices = wanted or files
    total_missing = 0
    for n, index in enumerate(indices, start=1):
        if index not in files:
            print(f"[{n}/{len(indices)} {index}] no recording", flush=True)
            continue
        level_id, missing, over, review = analyse(index, table)
        total_missing += len(missing)
        state = "clean" if not (missing or review) else ""
        print(f"[{n}/{len(indices)} {index}] {level_id} {state}", flush=True)
        for name, pre in missing:
            print(f"    MISSING  {name} dependsOn {pre}", flush=True)
        for name, pre in over:
            print(f"    OVER     {name} dependsOn {pre} (was open before it)",
                  flush=True)
        for line in review:
            print(f"    REVIEW   {line}", flush=True)
    print(f"Done: {len(indices)} level(s), {total_missing} missing edge(s)",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
