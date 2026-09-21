"""What will the release gate say? Answered in a second, not fifteen minutes.

WHY THIS EXISTS, and it is the question droha asked on 2026-09-21 that
had no good answer: the gate controls every variable. The seed is fixed
at 20260906. The starting abilities and Skips come out of that seed. The
scheduler is a pure function. Cat traps were the only randomness and
--steady turns them off. The run is DETERMINISTIC - so the gate's
verdict is a thing that can be computed, and until now it was a thing
that was discovered by watching.

Four gate runs that day went 19/25, 23/25, 18/25, 20/25, and EVERY
failure in all four was one of the thirteen assertions below - all of
which follow from the simulated run. Not one of them needed the game to
be predicted. They were discovered by spending an hour of wall clock
because nothing composed the model with the verdict.

    ALTTL_SEED_DIR=testserver/out-dlc py -3.13 tools/predict_gate.py

WHAT IT CANNOT PREDICT, listed rather than silently skipped: twelve of
the twenty-five need the game - that the mod connected, that no solve
threw, the error census, the controller-table audit, the campaign-save
isolation, the patch census. Those are DRIVE facts and the gate is
still the thing that measures them.

AND ONE THING IT GETS WRONG, measured 2026-09-21. It models the Skip
ECONOMY - is the seed clearable, are there enough Skips - but NOT the
round-by-round attempt ordering, because attempt counts depend on
DRIVE outcomes it only approximates. `rank` is (attempts, plan order,
index), so WHICH beaten slot a Skip lands on can differ from the real
run. On the DLC gate the real run offered its Skip to DLC1 Daggers,
which had nothing left to find, while `Containers` sat unreachable on
`Filing Cabinet - Solution 2`; this model offered it to Filing Cabinet
and cleared. It predicted 8 of 8 and the run got 7.

So a clean prediction here is necessary and not sufficient. Anything
about WHICH slot is chosen belongs in tools/test_scheduler.py, where
choose_slot can be handed the exact shape directly - see
TestASkipGoesWhereSomethingIsOutstanding.

HOW TO USE IT. Run it before the gate. If the prediction is not a clean
sheet, fix that first - the gate will only tell you the same thing more
slowly. If the prediction is clean and the gate disagrees, that gap is
itself a finding: the model is wrong about something, and the model is
what every fast test depends on.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e
import test_run_model as model

#: Slots the harness genuinely cannot force, by level id. Measured by
#: tools/probe-slots.py one level at a time - this is the one DRIVE fact
#: the prediction needs, and it is measured once per level rather than
#: re-derived every run.
UNFORCEABLE = set(e2e.KNOWN_UNFORCEABLE)

def progression_items():
    """Items whose loss stalls a run, as opposed to filler.

    Built on CALL, not at import: _ABILITY_NAMES is filled by
    _load_ability_tables(), so a module-level set would be the two
    literals and no abilities at all - and would then quietly report
    zero stranded progression on a seed that strands four.
    """
    e2e._load_ability_tables()
    return {"Progressive Puzzle Pack", "Skip"} | set(e2e._ABILITY_NAMES or ())


def predict(plan, starting, placements):
    """The thirteen assertions that follow from the run itself."""
    slots = plan["slots"]
    unforceable = {i for i, (_index, level_id) in enumerate(slots)
                   if level_id in UNFORCEABLE}
    out = model.play_on_paper(plan, starting, placements,
                              unforceable=unforceable)

    puzzles = len(slots)
    boundaries = plan["boundaries"]
    want_packs = len(boundaries) - 1
    opening = boundaries[0]
    tokens = out["tokens"]
    spent_on = out["spent_on"]

    surprises = [s for s in spent_on
                 if s["reason"] == "unforceable"
                 and s["level_id"] not in UNFORCEABLE]
    # A gated Skip the harness could have avoided. A last-resort
    # one - nothing playable, every candidate gated - is reported
    # by the gate rather than failed, so predict the same.
    papered = [s for s in spent_on
               if s["gated"] and s["reason"] != "last-resort"]

    checks = tokens > 0 or out["collected"] > 0
    finished = tokens >= puzzles

    # CAN THE SKIP SUPPLY EVEN REACH THE PROGRESSION? A pre-flight the
    # gate never had.
    #
    # Some locations the harness can never earn by force-solving: an
    # alternate solution (it makes ONE arrangement) and the excluded
    # containers. A Skip grants a whole card, so the bill is the number
    # of distinct CARDS holding stranded progression, not the number of
    # items.
    #
    # Measured 2026-09-21: the DLC seed strands 4 progression items
    # across 3 cards against ~6 Skips, and the base seed strands NONE.
    # That asymmetry is why the base gate passed for months while the
    # DLC gate never did. It fits today; a seed that strands six would
    # stall with no warning, and this turns that into a line before the
    # run instead of a mystery after it.
    progression = progression_items()
    where = e2e.locations_for_slots(plan)
    unearnable = model.container_excludes(where)
    slot_of = {loc: i for i, locs in where.items() for loc in locs}
    stranded_cards = {slot_of[loc] for loc, item in placements.items()
                      if loc in unearnable and item in progression
                      and loc in slot_of}
    supply = sum(1 for i in starting if i == "Skip") + \
        sum(1 for v in placements.values() if v == "Skip")

    return out, [
        (f"the run is {puzzles} puzzles", puzzles == e2e.PUZZLES, ""),
        (f"the mod sees the generator's {want_packs} pack(s)",
         want_packs >= 1, ""),
        ("the run is more than its free opening", want_packs >= 1, ""),
        (f"the track opens with {opening} puzzles, not all {puzzles}",
         opening < puzzles, ""),
        (f"all {puzzles} puzzles beaten", finished,
         f"{tokens} of {puzzles} banked a Beaten token"),
        ("a Skip was spent only where one is known to be needed",
         not surprises,
         ", ".join(s["level_id"] for s in surprises)),
        ("no Skip covered for a level the mod was still gating",
         not papered,
         ", ".join(s["level_id"] for s in papered)),
        ("the mod agrees every puzzle was beaten", finished, ""),
        (f"packs opened all {puzzles} slots, not just the first {opening}",
         model.open_for(out["packs"], boundaries) >= puzzles, ""),
        ("checks reached the server", checks, ""),
        ("the credits unlocked", finished, ""),
        ("the mod reported the goal", finished, ""),
        ("the server agrees the goal is met", finished, ""),
        ("the Skip supply can reach the stranded progression",
         len(stranded_cards) <= supply,
         f"{len(stranded_cards)} card(s) hold progression the harness "
         f"cannot earn, {supply} Skip(s) available"),
    ]


def main():
    plan, starting, placements = model.load()
    out, predictions = predict(plan, starting, placements)

    where = os.path.relpath(model.OUT, e2e.REPO)
    print(f"-- predicting the gate for {where} --", flush=True)
    print(f"   {len(plan['slots'])} slots, boundaries "
          f"{plan['boundaries']}, starting {sorted(starting)}", flush=True)
    print("", flush=True)

    bad = 0
    for name, ok, detail in predictions:
        bad += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}"
              + (f"  [{detail}]" if detail and not ok else ""), flush=True)

    print("", flush=True)
    for skip in out["spent_on"]:
        print(f"   skip on slot {skip['slot']} {skip['level_id']} "
              f"({skip['reason']}"
              + (", STILL GATED" if skip["gated"] else "") + ")", flush=True)

    predicted = len(predictions) - bad
    print(f"\nPredicted: {predicted}/{len(predictions)} of the assertions "
          f"that follow from the run", flush=True)
    print(f"  the other {25 - len(predictions)} need the game: the "
          f"connection, the arrow, the pause exit, solve exceptions, the "
          f"error census, the controller table, save isolation, the patch "
          f"census", flush=True)
    print(f"Done: {bad} predicted failure(s); "
          f"{'run the gate' if not bad else 'FIX THESE BEFORE RUNNING THE GATE'}",
          flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
