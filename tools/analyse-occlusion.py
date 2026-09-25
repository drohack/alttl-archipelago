"""Turn the occlusion dump into leads, by throwing most of it away.

    py -3.13 tools/analyse-occlusion.py

WHY THIS IS A SEPARATE STEP. probe-occlusion reports every pair of groups whose
bounding boxes overlap, and raw that is almost all noise. Bounding boxes are
axis-aligned, and a group whose objects are scattered across a level has a box
that swallows the whole scene - so DLC1 Tea Cabinet's first run said "Teapot
Stack is 99% inside Items Placements Draggables" when Items Placements is
simply strewn everywhere. Mutual pairs give it away: Teapot was 86% inside Jam
Jars while Jam Jars was 94% inside Teapot. Neither contains the other.

So two filters, and both are about what the number can honestly mean:

  1. THE CONTAINER MUST BE AN OPENER. A drawer or a cupboard is a thing you
     open, and a group inside one is plausibly unreachable until it opens. A
     group overlapping another pile on the same desk is not.
  2. CONTAINMENT IS NOT MUTUAL. If A is inside B and B is inside A, the boxes
     are simply the same size and the relationship is nothing. Drop both.

What survives is a candidate: a group whose objects sit inside something that
has to be opened. Still not proof - a piece can rest beside an open drawer and
overlap its footprint - but it is the only instrument this project has that
could have caught the chalk, which read blocked=0 dimmed=0 the whole time it
was unreachable.
"""
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

LOGS = os.path.join(e2e.REPO, "testserver", "logs")
REPORT = os.path.join(LOGS, "occlusion-report.json")
LEVELS = os.path.join(e2e.REPO, "apworld", "alttl", "data", "levels.json")

#: Controller types you OPEN. HangingToolsController is deliberately absent:
#: Workbench proved a pegboard gates nothing and already bypasses Drawer.
OPENER_TYPES = {"DrawerController", "DrawerExpandableController", "Cupboard"}

#: Cupboard doors are AnimScrubbables and map to Gadgets rather than Drawer,
#: so the type list above misses them. Matched by name because that is what
#: the game calls them and there is no better handle.
#:
#: DELIBERATELY NARROW. This began as ("drawer", "cupboard", "door", "lid",
#: "box", "tray", "case") and those last four are puzzle OBJECTS, not things
#: you open: MedicineCabinet's "Jar Lid" would have been promoted to a
#: container and every shelf item overlapping it reported as trapped inside.
#: A false opener does not just add noise, it manufactures exactly the finding
#: this tool exists to look for - which is the failure mode that has already
#: produced three confident wrong answers today.
OPENER_NAMES = ("drawer", "cupboard", "door")


def openers_for(raw):
    out = set()
    for c in raw["controllers"]:
        if c["type"] in OPENER_TYPES:
            out.add(c["name"])
            continue
        low = c["name"].lower()
        if any(word in low for word in OPENER_NAMES):
            out.add(c["name"])
    return out


def main():
    if not os.path.isfile(REPORT):
        raise SystemExit(f"{REPORT} is missing - run tools/probe-occlusion.py")

    with open(REPORT, encoding="utf-8") as fh:
        report = json.load(fh)
    with open(LEVELS, encoding="utf-8") as fh:
        by_index = {l["levelIndex"]: l for l in json.load(fh)["levels"]}

    total_pairs = sum(len(v["pairs"]) for v in report.values())
    kept_total = 0
    print(f"{len(report)} level(s), {total_pairs} raw overlapping pair(s)",
          flush=True)
    print("", flush=True)

    for index, data in sorted(report.items(), key=lambda kv: int(kv[0])):
        raw = by_index.get(int(index))
        if raw is None:
            continue
        openers = openers_for(raw)
        if not openers:
            continue

        # Mutual containment means the boxes are the same size, not that one
        # holds the other.
        both = {(p["inside"], p["container"]) for p in data["pairs"]}
        kept = [p for p in data["pairs"]
                if p["container"] in openers
                and p["inside"] not in openers
                and (p["container"], p["inside"]) not in both]
        if not kept:
            continue

        kept_total += len(kept)
        print(f"  {raw['levelId']}  (index {index})", flush=True)
        for p in sorted(kept, key=lambda q: -q["fraction"]):
            print(f"      {p['inside']:30} {int(p['fraction'] * 100):3}% "
                  f"inside {p['container']}", flush=True)

    print("", flush=True)
    print(f"Done: {kept_total} candidate(s) survived, from {total_pairs} raw "
          f"pairs. Each is a group sitting inside something that must be "
          f"opened - a lead to play, not a verdict.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
