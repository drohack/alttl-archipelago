"""What will the release gate say? Answered in seconds, not fifteen minutes.

    py -3.13 tools/make-seed.py [--dlc]
    ALTTL_SEED_DIR=testserver/out-base py -3.13 tools/predict_gate.py

The gate controls every variable: the seed walk is fixed, the starting
abilities and Skips come out of the seed, and the scheduler is a pure
function. So the assertions that follow from the run itself can be
computed before the game is ever launched.

THE GATE'S OWN PRE-FLIGHT, NOT A SECOND MODEL. This asks
release_e2e.judge_seed - the production paper run the gate refuses a seed
on and then follows visit for visit - and preflight_skips. It used to run
tools/test_run_model.py's abstract model instead, which treated an
unforceable level as forcing nothing at all: on 2026-09-28 it predicted a
stall on Desktop Computer (whose parts the harness does force; only its
completion needs a Skip) for a seed the gate's pre-flight cleared 15/15.
Two models of one run can only ever disagree.

WHAT IT CANNOT PREDICT, listed rather than silently skipped: the
connection, the arrow, the pause exit, solve exceptions, the error census,
the controller table, save isolation, the patch census, and which slot a
Skip is spent on in the real run (the gate checks those live).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e

#: The gate's checks, for "the other N need the game".
GATE_CHECKS = 28


def seed_dir():
    """ALTTL_SEED_DIR, else the base seed make-seed writes. Absolute: the
    seed is read by a subprocess running inside Archipelago/."""
    folder = os.environ.get("ALTTL_SEED_DIR") or os.path.join(
        e2e.REPO, "testserver", "out-base")
    return os.path.abspath(folder)


def predict(out_dir, seed_zip):
    """The gate's pre-flight for this seed, as the assertions it implies.

    Returns (verdict lines, [(name, ok, detail)])."""
    clears, lines, plan = e2e.judge_seed(out_dir, seed_zip, True)
    demand, supply = e2e.preflight_skips(out_dir, seed_zip, plan)

    puzzles = len(plan["slots"])
    boundaries = plan["boundaries"]
    want_packs = len(boundaries) - 1
    opening = boundaries[0] if boundaries else puzzles
    return lines, [
        (f"the run is {puzzles} puzzles", puzzles == e2e.PUZZLES, ""),
        (f"the mod sees the generator's {want_packs} pack(s)",
         want_packs >= 1, ""),
        ("the run is more than its free opening", want_packs >= 1, ""),
        (f"the track opens with {opening} puzzles, not all {puzzles}",
         opening < puzzles, ""),
        (f"all {puzzles} puzzles beaten", clears, lines[0]),
        ("the mod agrees every puzzle was beaten", clears, ""),
        (f"packs opened all {puzzles} slots, not just the first {opening}",
         clears, ""),
        ("the credits unlocked", clears, ""),
        ("the mod reported the goal", clears, ""),
        ("the server agrees the goal is met", clears, ""),
        ("the Skip supply covers every level only a Skip finishes",
         demand <= supply,
         f"{demand} level(s) need a Skip, {supply} Skip(s) reachable"),
    ]


def main():
    out_dir = seed_dir()
    zips = [f for f in os.listdir(out_dir) if f.endswith(".zip")] \
        if os.path.isdir(out_dir) else []
    if not zips:
        print(f"no seed in {out_dir} - make one with tools/make-seed.py",
              flush=True)
        return 2
    seed_zip = max(zips, key=lambda f: os.path.getmtime(os.path.join(out_dir, f)))

    print(f"-- predicting the gate for "
          f"{os.path.relpath(os.path.join(out_dir, seed_zip), e2e.REPO)} --",
          flush=True)
    lines, predictions = predict(out_dir, seed_zip)
    for line in lines:
        print(f"   {line}", flush=True)
    print("", flush=True)

    bad = 0
    for name, ok, detail in predictions:
        bad += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}"
              + (f"  [{detail}]" if detail and not ok else ""), flush=True)

    predicted = len(predictions) - bad
    print(f"\nPredicted: {predicted}/{len(predictions)} of the assertions "
          f"that follow from the run", flush=True)
    print(f"  the other {GATE_CHECKS - len(predictions)} need the game: the "
          f"connection, the arrow, the pause exit, solve exceptions, the "
          f"error census, the controller table, where each Skip lands, save "
          f"isolation, the patch census", flush=True)
    print(f"Done: {bad} predicted failure(s); "
          f"{'run the gate' if not bad else 'FIX THESE BEFORE RUNNING THE GATE'}",
          flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
