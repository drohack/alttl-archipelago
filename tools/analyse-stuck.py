"""Which stuck groups could actually be understating a requirement?

    py -3.13 tools/analyse-stuck.py

TWO FILTERS, AND BOTH WERE LEARNED THE EXPENSIVE WAY on 2026-09-22.

FILTER ONE, in the probe itself: `stuck` rather than `blocked`. The `locks`
command's `blocked` is `!Interactable || PreventSelection`, which fuses two
different situations - a group gated behind something the player has not done,
and a group already sitting in its correct place. Both are untouchable and only
the first is a missing requirement. Fusing them made the count climb as levels
settled, saturated whole content blocks, and flagged Workbench, a level droha
finished holding NOTHING.

FILTER TWO, here: a stuck group only matters if its requirement is a STRICT
SUBSET of its level's. A group that already asks for everything its level asks
for cannot be asking for too little, whatever the game does to its objects at
boot. That is most of what survives filter one:

    Bowls      level needs []          Crack [], Pattern []      nothing to omit
    Cat Frame  level needs [Rotating]  Straighten [Rotating]     equal
    Calendar   level needs [Sticking]  Repeating Sequence [same] equal
    Candles    level needs [Gadgets]   Candles Object [same]     equal

All four are genuinely stuck and all four are already correct.

WHAT SURVIVES BOTH IS A CANDIDATE, NOT A FINDING. The gate might be a phase the
player reaches by playing, which is not a missing ability requirement at all.
Only somebody playing the level settles that - which is how every real fix
today was actually established.
"""
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

REPORT = os.path.join(e2e.REPO, "testserver", "logs", "blocked-report.json")


def main():
    sys.path.insert(0, os.path.join(e2e.REPO, "Archipelago"))
    from worlds.alttl import data                          # noqa: E402

    if not os.path.isfile(REPORT):
        raise SystemExit(f"{REPORT} is missing - run tools/probe-blocked.py")
    with open(REPORT, encoding="utf-8") as fh:
        report = json.load(fh)

    by_index = {l.level_index: l for l in data.LEVELS}
    hits, stuck_total, dropped_equal = [], 0, 0

    for index, entry in report.items():
        level = by_index.get(int(index))
        if level is None:
            continue
        whole = set(level.enforced_abilities)
        for group in entry["groups"]:
            if not group.get("stuck"):
                continue
            stuck_total += 1
            name = level.controller_group.get(group["controller"])
            if name is None:
                continue
            own = set(level.enforced_part_abilities.get(name, ()))
            if not (own < whole):
                dropped_equal += 1
                continue
            hits.append((level.level_id, name, group["controller"],
                         group["stuck"], group["objects"], sorted(own),
                         sorted(whole - own),
                         bool(level.unproven_parts), level.dlc or "base"))

    print(f"{stuck_total} stuck group(s) in the sweep", flush=True)
    print(f"   {dropped_equal} already ask for everything their level does - "
          f"cannot understate, dropped", flush=True)
    print(f"   {len(hits)} are a strict subset AND stuck  <-- the candidates",
          flush=True)
    print("", flush=True)

    unguarded = [h for h in hits if not h[7]]
    print(f"{len(unguarded)} of them are NOT covered by the guard today:",
          flush=True)
    print("", flush=True)
    for lid, grp, _ctrl, stuck, objs, own, missing, guarded, dlc in sorted(
            hits, key=lambda h: (h[7], h[8], h[0])):
        mark = "  " if guarded else "! "
        print(f" {mark}{lid:30} {grp:24} {stuck:3}/{objs:<4} "
              f"{str(own):24} misses {missing}", flush=True)

    print("", flush=True)
    print("Done: lines marked ! have no guard, so progression can land on "
          "them today. Each is a candidate to play, not a proven gate.",
          flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
