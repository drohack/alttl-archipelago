"""The queue of levels a human still has to check, and a driver for it.

    py -3.13 tools/handtest-queue.py --build     build or refresh the queue
    py -3.13 tools/handtest-queue.py             show it
    py -3.13 tools/handtest-queue.py --next      set the next pending one up
    py -3.13 tools/handtest-queue.py --answer INDEX VERDICT NOTE

WHY A QUEUE AND NOT A CONVERSATION. There are 31 levels carrying 122 guarded
locations and exactly one of them had ever been checked. Doing that one level
at a time through chat is how a day disappears - droha said so, in those words.
So the order is computed once, the setup is scripted, and the only thing a
person supplies is the verdict.

WHAT AUTOMATION SETTLED FIRST, so the queue is as short as it can honestly be.
tools/probe-blocked.py boots every candidate with ability locks OFF and reads
`locks`. With the mod dimming nothing, any object still `blocked` is the GAME
refusing - a shut drawer, an unrevealed phase. That RULES GATING IN, and names
the group.

It cannot rule gating OUT, and that is the whole lesson of 2026-09-22: the
chalk jigsaws read blocked=0 dimmed=0 while sitting physically behind a
drawer. Occlusion is not non-interactivity and no census sees it. So a level
with nothing blocked is NARROWED, never cleared - the queue says so rather
than quietly dropping it.

VERDICTS
  gated      a group cannot be finished without the opener or an earlier
             phase -> its requirement UNDERSTATES, add the edge
  free       it can be finished with only what it declares -> correct as is
  partial    some groups yes, some no -> say which in the note
  unclear    could not tell -> stays queued, note why
"""
import json
import os
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import release_e2e as e2e                                  # noqa: E402

LOGS = os.path.join(e2e.REPO, "testserver", "logs")
CANDIDATES = os.path.join(LOGS, "candidates.json")
BLOCKED = os.path.join(LOGS, "blocked-report.json")
QUEUE = os.path.join(LOGS, "handtest-queue.json")
PROVEN = os.path.join(e2e.REPO, "apworld", "alttl", "data",
                      "proven-requirements.json")

#: Settled by play already, so they never enter the queue. Each carries the
#: date and the words, because "somebody checked it once" is not evidence.
DONE = {
    "NeatStreak_Paper Plane Supplies": (
        "gated",
        "2026-09-22 droha, holding Jigsaw without Drawer: the chalk pieces "
        "are behind the drawer and it cannot be closed to reach them. All 7 "
        "jigsaws given dependsOn Drawer Controller."),
    "NeatStreak_Tool Drawer": (
        "gated",
        "2026-09-18 droha, Drawer withheld: could not complete. Edges "
        "already present in levels.json."),
    "NeatStreak_Bathroom Drawer": (
        "gated",
        "2026-09-18 and 2026-09-20 droha: the drawer will not open without "
        "Drawer. Edges already present."),
}

#: Corrected 2026-09-22 by MEASUREMENT plus an established principle, rather
#: than by playing each one. probe-blocked.py booted them with ability locks
#: OFF and the game still blocked these groups, and the three levels above had
#: already proved by play that a shut drawer gates its contents. 38 dependsOn
#: edges added, all TIGHTENING - the direction that cannot softlock.
#:
#: They are marked settled rather than queued because a hand test would only
#: confirm a principle already established on three levels, and the correction
#: is safe whether or not it is confirmed. Anything that later turns out to be
#: free loses nothing but fill freedom, and moving it back needs evidence.
MEASURED = {
    "DLC1 Boss", "DLC1 Craft Supplies", "DLC1 Fossils", "DLC1 Game Pieces",
    "DLC1 Jewelry Box", "DLC1 Kitchen Utensils Drawers", "DLC1 Lunch Tray",
    "DLC1 Sewing Box", "DLC2 Combs", "DLC2 Junk Drawer Transforming",
    "DLC2 Sticky Drawer",
}
for _name in MEASURED:
    DONE[_name] = (
        "gated",
        "2026-09-22 probe-blocked: the game blocks these groups with ability "
        "locks OFF, and play on three sibling levels established that a shut "
        "drawer gates its contents. dependsOn edges added.")

DONE["TupperwareNesting"] = (
    "gated",
    "2026-09-22 droha could not place the lids without Stacking. Lids now "
    "depends on Stack 1.")
DONE["DLC1 Clock Cupboard"] = (
    "gated",
    "2026-09-22 droha: \"i can move the clocks, but i can't open the cubbord "
    "to put the clocks in\". Items Placements now depends on the doors.")

#: Holes the 2026-09-22 audit found OUTSIDE the guard: parts asking for less
#: than their level, progression-eligible, and stuck at boot or progressive in
#: shape. Each row is one hand test: hold exactly `hold`, answer `question`.
#: The level set is pinned in test_unproven.UNGUARDED_UNDERSTATED. Worst first.
#: Guarded by the level data itself (a bespoke level class), not by a suspect
#: entry, so the suspect list alone does not name them.
STRUCTURALLY_GUARDED = {"TupperwareNesting"}

#: The only rows still worth a play, and why. Medicine Cabinet: proving it takes
#: 15-puzzle base redraws from about 27% to 0% (measured). The Fridge: a
#: single-group level, so no guard can cover its Solution at all.
STILL_WORTH_PLAYING = {"MedicineCabinet", "SomethingEggstra Fridge",
                       "DLC2 Music Box", "Workbench"}

AUDIT = [
    # FIRST: which of its two part checks fire (Organizer depends on
    # StateController, both declare Ordering). Queued 2026-09-23.
    ("DLC2 Music Box", 1227, ["Ordering"],
     "Do the State and the Organizer checks both fire? "
     "Watch the DevTools PartSolved lines."),
    # Workbench's two controllers share the same 21 objects; forcing the
    # tools completed the level and the DraggablesForTargets check never
    # fired (full gate, 2026-09-24). Played 2026-09-24: it never fired for
    # droha either, so it is notALocation now.
    ("Workbench", 50, [],
     "Finish the level normally: does the 'Draggables For Targets' check "
     "fire, as well as Tools? Watch the DevTools PartSolved lines."),
    # PROVE THESE TWO NEXT. They cause most of the remaining redraws and took
    # most of the leaked progression (measured 2026-09-23). A level is proven
    # all-or-nothing and each group must fire holding EXACTLY what it declares,
    # so one run per held set. No Medicine Cabinet run holds Drawer: its
    # extraAbilities Drawer is the suspicion being tested.
    ("MedicineCabinet", 49, [],
     "Holding NOTHING: do Tube, Trinkets, Pick, Jar Lid, Brush, Cup, Contact "
     "Lenses and Pump Bottle all fire?"),
    ("MedicineCabinet", 49, ["Containers"],
     "Do Oral and Swabs fire?"),
    ("MedicineCabinet", 49, ["Ordering"],
     "Do Blue Bottles and Green Bottles fire?"),
    ("MedicineCabinet", 49, ["Stacking"],
     "Does Creams Stacked fire?"),
    ("TupperwareNesting", 82, ["Stacking"],
     "Do Stack 1, Stack 2, Tray and Stack 3 fire?"),
    ("TupperwareNesting", 82, ["Containers", "Stacking"],
     "Do the Lids fire?"),
    ("TupperwareNesting", 82, ["Grids", "Stacking"],
     "Do (Large Square) and Food fire?"),
    ("DLC2 Water Glasses", 1209, ["Sticking"],
     "Can you FINISH the Jigsaw and the Sorting without Ordering?"),
    ("DLC2 Water Glasses", 1209, ["Ordering"],
     "Can you FINISH the water levels without Sticking?"),
    ("DLC2 Figurines", 1223, ["Sticking"],
     "Can you FINISH the plain Draggables and Sorting without Gadgets?"),
    ("DLC2 Figurines", 1223, ["Gadgets"],
     "Can you FINISH the Groupables without Sticking?"),
    ("DLC2 Cat Eyes", 1225, ["Ordering"],
     "Can you FINISH the eyes (Indexables) without Gadgets?"),
    ("SomethingEggstra Fridge", 109, ["Containers"],
     "Can you find all six eggs and finish the level without Stacking?"),
    ("Mirror", 79, ["Gadgets"],
     "Can you FINISH the candle state without Containers or Stacking?"),
    ("Lamp", 80, ["Gadgets"],
     "Can you FINISH all the switches without Rotating?"),
    ("DLC2 Robots", 1230, ["Containers"],
     "Can you FINISH the Containables and both Spring groups without "
     "Ordering?"),
    ("DLC2 Robots", 1230, ["Ordering"],
     "Can you FINISH the Indexables without Containers?"),
    ("Wilting Flowers", 54, ["Tidying"],
     "Can you clean BOTH flowers without Gadgets (before they are upright)?"),
    ("Sharp Pencils", 19, ["Tidying"],
     "Are there shavings to clear, and can you clear them all, without "
     "Ordering?"),
    ("DLC2 First Aid Kit", 1229, ["Containers"],
     "Can you FINISH the Wipe and Bottle groups without Ordering?"),
    ("DLC2 ObsoleteTech", 1207, ["Containers"],
     "Can you FINISH the Floppy group without Ordering?"),
    ("DLC1 Fossils", 1115, ["Drawer"],
     "Does the Drawers check fire with the fossils still unassembled?"),
    ("DLC1 Craft Supplies", 1101, ["Drawer"],
     "Do the Drawers and Misc checks fire without Ordering?"),
    ("DLC1 Game Pieces", 1117, ["Drawer"],
     "Do the drawer checks fire without Containers?"),
    ("DLC2 Sticky Drawer", 1221, ["Drawer"],
     "Do the Drawer and Draggables checks fire without Sticking?"),
]


def build():
    with open(CANDIDATES, encoding="utf-8") as fh:
        candidates = json.load(fh)

    # candidates.json is a 2026-09-22 snapshot; proven-requirements.json is
    # current. A level proven since the snapshot is settled, whatever the
    # snapshot says about it.
    with open(PROVEN, encoding="utf-8") as fh:
        table = json.load(fh)
    suspect = table.get("suspect", {})
    proven = table.get("proven", {})

    blocked = {}
    if os.path.isfile(BLOCKED):
        with open(BLOCKED, encoding="utf-8") as fh:
            blocked = json.load(fh)

    old = {}
    if os.path.isfile(QUEUE):
        with open(QUEUE, encoding="utf-8") as fh:
            old = {e.get("key", e["levelId"]): e for e in json.load(fh)}

    rows = []
    for c in candidates:
        probe = blocked.get(str(c["levelIndex"]), {})
        groups = probe.get("groups", [])
        entry = {
            "levelId": c["levelId"],
            "levelIndex": c["levelIndex"],
            "guarded": c["guarded"],
            "gap": c["gap"],
            # Groups the GAME blocks with no ability lock anywhere near them.
            "gameBlocks": [g["controller"] for g in groups
                           if g.get("stuck", g.get("blocked"))],
            "probed": bool(groups),
            "verdict": "pending",
            "note": "",
        }
        if c["levelId"] in DONE:
            entry["verdict"], entry["note"] = DONE[c["levelId"]]
        elif c["levelId"] in proven:
            entry["verdict"] = "proven"
            entry["note"] = "proven-requirements.json: " + proven[c["levelId"]]
        elif old.get(c["levelId"], {}).get("verdict", "pending") in (
                "pending", "guarded"):
            # Every candidate row was a GUARDED level when the snapshot was
            # taken. One that is neither proven nor still guarded is back to
            # needing a verdict.
            entry["verdict"] = "pending"
        elif c["levelId"] in old and old[c["levelId"]]["verdict"] != "pending":
            entry["verdict"] = old[c["levelId"]]["verdict"]
            entry["note"] = old[c["levelId"]]["note"]
        rows.append(entry)

    # 28 audit levels were guarded instead of played on 2026-09-23, then all
    # of them were played and proven the same day. A proven one is settled.
    # The audit rows go FIRST and keep their order: they carry no guard, so a
    # wrong one can end a run today.
    audit = []
    for level_id, index, hold, question in AUDIT:
        key = f"{index}:{'+'.join(hold)}"
        prior = old.get(key, {})
        if level_id in proven:
            prior = {"verdict": "proven",
                     "note": "proven-requirements.json: " + proven[level_id]}
        elif ((level_id in suspect or level_id in STRUCTURALLY_GUARDED)
                and level_id not in STILL_WORTH_PLAYING
                and prior.get("verdict", "pending") == "pending"):
            prior = {"verdict": "guarded",
                     "note": "guarded instead of played (proven-requirements.json)"}
        audit.append({
            "levelId": level_id, "levelIndex": index, "key": key,
            "hold": hold, "question": question, "guarded": 0,
            "gap": "unguarded: part asks for less than the level",
            "gameBlocks": [], "probed": True,
            "verdict": prior.get("verdict", "pending"),
            "note": prior.get("note", ""),
        })

    # Worst first: a level the game already blocks is the strongest lead, then
    # by how many guarded locations ride on the answer.
    rows.sort(key=lambda r: (-len(r["gameBlocks"]), -r["guarded"], r["levelId"]))
    rows = audit + rows
    with open(QUEUE, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, indent=1)
    return rows


def load():
    if not os.path.isfile(QUEUE):
        return build()
    with open(QUEUE, encoding="utf-8") as fh:
        return json.load(fh)


MARKS = {"pending": "  ", "gated": "G ", "free": "F ", "guarded": "g ",
         "proven": "p ", "partial": "P ", "unclear": "? "}


def show(rows):
    done = [r for r in rows if r["verdict"] != "pending"]
    todo = [r for r in rows if r["verdict"] == "pending"]
    print(f"{len(done)} settled, {len(todo)} to go, "
          f"{sum(r['guarded'] for r in todo)} guarded locations riding on them",
          flush=True)
    unprobed = [r for r in todo if not r["probed"]]
    if unprobed:
        print(f"  ({len(unprobed)} never probed - run tools/probe-blocked.py)",
              flush=True)
    print("", flush=True)
    for r in rows:
        blocks = ""
        if r["gameBlocks"]:
            blocks = "   GAME BLOCKS: " + ", ".join(r["gameBlocks"][:3])
            if len(r["gameBlocks"]) > 3:
                blocks += f" +{len(r['gameBlocks']) - 3}"
        print(f" {MARKS.get(r['verdict'], '  ')}{r['levelIndex']:5} "
              f"{r['levelId']:32} {r['guarded']:2} loc{blocks}", flush=True)
        if r["note"]:
            print(f"        {r['note'][:96]}", flush=True)
        elif r.get("question") and r["verdict"] == "pending":
            print(f"        hold {'+'.join(r['hold']) or 'nothing'}: {r['question'][:80]}",
                  flush=True)


def main():
    if "--build" in sys.argv:
        rows = build()
        show(rows)
        print("", flush=True)
        print(f"Done: queue at {os.path.relpath(QUEUE, e2e.REPO)}", flush=True)
        return 0

    rows = load()

    if "--answer" in sys.argv:
        i = sys.argv.index("--answer")
        if len(sys.argv) < i + 3:
            print("usage: --answer INDEX VERDICT [NOTE]", flush=True)
            return 2
        # INDEX, or INDEX:HOLD when one level has two runs (1209:Sticking).
        want, verdict = sys.argv[i + 1], sys.argv[i + 2]
        note = sys.argv[i + 3] if len(sys.argv) > i + 3 else ""
        for r in rows:
            if r.get("key") == want or (":" not in want
                                        and r["levelIndex"] == int(want)
                                        and r["verdict"] == "pending"):
                r["verdict"], r["note"] = verdict, note
                print(f"recorded {r['levelId']}: {verdict}", flush=True)
                break
        else:
            print(f"{want} is not a pending row in the queue", flush=True)
            return 2
        with open(QUEUE, "w", encoding="utf-8") as fh:
            json.dump(rows, fh, indent=1)
        left = sum(1 for r in rows if r["verdict"] == "pending")
        print(f"Done: {left} still pending", flush=True)
        return 0

    if "--next" in sys.argv:
        nxt = next((r for r in rows if r["verdict"] == "pending"), None)
        if nxt is None:
            print("Done: nothing pending", flush=True)
            return 0

        if nxt.get("hold") is not None:
            # An audit row is a locks-ON test holding exactly `hold`, which is
            # what handtest-level.py stands up. Booting it here would test
            # whatever seed happens to be served.
            print(f"  {nxt['levelId']}  index {nxt['levelIndex']}", flush=True)
            print(f"  run: py -3.13 tools/handtest-level.py "
                  f"{nxt['levelIndex']} {' '.join(nxt['hold'])}", flush=True)
            print(f"  ask: {nxt['question']}", flush=True)
            print(f"  then: --answer {nxt['key']} gated|free|partial|unclear "
                  f"NOTE", flush=True)
            print(f"Done: {nxt['levelId']} is next", flush=True)
            return 0

        e2e.dev(f"boot:{nxt['levelIndex']}", settle=8.0)
        e2e.dev("locks", settle=2.5)

        print("=" * 68, flush=True)
        print(f"  {nxt['levelId']}   index {nxt['levelIndex']}, "
              f"{nxt['guarded']} guarded location(s)", flush=True)
        print("=" * 68, flush=True)
        print(f"  suspect because: {nxt['gap'][:90]}", flush=True)
        print("", flush=True)
        if nxt["gameBlocks"]:
            print("  The GAME blocks these, with no ability lock at all:",
                  flush=True)
            for g in nxt["gameBlocks"]:
                print(f"     {g}", flush=True)
            print("  So something in the level gates them. Find out what.",
                  flush=True)
            print("", flush=True)
            # Measured 2026-09-22 across all ten non-drawer levels: solving
            # every controller in the level, one at a time, freed NOTHING.
            # So the gate is not a solved-event dependency and there is no
            # dependsOn edge to add - whatever holds these is driven by
            # interaction. Said here so nobody re-runs that experiment or,
            # worse, invents an edge to explain the block.
            print("  NOTE: probe-unblock already tried solving every "
                  "controller here. Nothing freed anything, so this is NOT a "
                  "solve-X-to-unlock-Y gate and no dependsOn edge will fix "
                  "it. What is needed is what the player has to DO.",
                  flush=True)
        else:
            print("  Nothing measurably blocked - but occlusion never shows "
                  "up as blocked, so this is narrowed, not cleared.",
                  flush=True)
        print("", flush=True)
        print("  Can each group be finished holding only what it declares?",
              flush=True)
        print(f"  Then: py -3.13 tools/handtest-queue.py --answer "
              f"{nxt['levelIndex']} gated|free|partial|unclear NOTE", flush=True)
        print(f"Done: {nxt['levelId']} is up", flush=True)
        return 0

    show(rows)
    print("", flush=True)
    print("Done: --next to set the next one up", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
