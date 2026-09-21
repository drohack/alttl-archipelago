"""Play the real seed on paper, in milliseconds.

WHY THIS FILE EXISTS. tools/test_scheduler.py covers choose_slot with
synthetic worlds, and it covers it well - but its whole-run model hands
the scheduler a world where every slot is open from round one and packs
do not exist. The real run opens five of eight slots and waits for a
Progressive Puzzle Pack sitting somewhere in the seed. A scheduler that
clears the synthetic world can still deadlock on the real economy, and
that difference is invisible to every test written before this one.

So this composes the two layers that can be tested without a game - DATA
reads the seed, CHOICE decides what to play - and asks the only question
worth asking before spending fifteen minutes: given this seed's actual
packs, abilities and Skips, is the run clearable at all?

    ALTTL_SEED_DIR=testserver/out-dlc py -3.13 tools/test_run_model.py

Seeds come from tools/make-seed.py, which needs no game.

WHAT THIS IS NOT. It is not the game. It models the harness's decisions,
not its hands: whether a controller can actually be forced is a DRIVE
question and tools/probe-slots.py answers it one level at a time. A pass
here means "the scheduling cannot deadlock on this seed", which is a
necessary condition for the gate and not a sufficient one.

A LIMITATION THAT WAS HERE AND IS NOW FIXED, kept because it explains
what this model is for. It used to call a slot `beaten` when every one
of its locations was collected; the MOD calls it beaten when the level
completes and files the token without a reachability check. So a card
carrying a drawer the harness cannot pull is beaten in the game and was
outstanding here - which meant this model could never produce a
beaten-but-incomplete slot, the ONE state the stall Skip exists for.
The stall path was therefore unreachable in the model, and two changes
that disabled it passed a green suite on 2026-09-21 and cost the base
gate the credits and the goal.

It now reads the Beaten token, like play() does. The lesson generalises:
when a change to the harness cannot be measured here, FIX THIS MODEL
FIRST - that is cheaper than one gate run, let alone three.

See tools/predict_gate.py, which turns this run into the gate's own
scorecard for the thirteen assertions that follow from it.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e

OUT = os.path.join(e2e.REPO,
                   os.environ.get("ALTTL_SEED_DIR",
                                  os.path.join("testserver", "out-dlc")))

#: Generous. The real gate gives up at MAX_ROUNDS = 60; anything needing
#: more than this on paper is a deadlock, not a slow run.
ROUNDS = 400


def load():
    zips = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not zips:
        raise unittest.SkipTest(
            f"no seed in {OUT} - make one with tools/make-seed.py")
    seed = max(zips, key=lambda f: os.path.getmtime(os.path.join(OUT, f)))
    plan = e2e.read_plan(OUT, seed)
    starting, placements = e2e.read_spoiler(OUT, seed)
    return plan, starting, dict(placements)


def container_excludes(where):
    """Locations in this seed the harness physically cannot earn.

    The gate's yaml lists fourteen drawer, cupboard and spring locations
    and keeps progression off them, because the harness solves by
    setting a controller's solved flag and a drawer is an interaction,
    not an arrangement. A PLAYER earns these normally - droha has played
    them by hand - so this is the harness declaring its own blind spot.

    THE MODEL USED TO IGNORE THEM, and that is why its first answer was
    "clears in 11 rounds with zero Skips" while the real run was at
    round 11 with 3 of 8 and a Skip already spent. A model that collects
    locations the harness cannot reach is a model of a different
    program. Read from the same yaml the gate generates from, so the two
    cannot drift.
    """
    excluded = {line.strip()[2:]
                for line in e2e.yaml_text(False, False, True).splitlines()
                if line.strip().startswith("- ")}
    present = {loc for locs in where.values() for loc in locs}

    # ALTERNATE SOLUTIONS ARE THE SAME BLIND SPOT, and this model did
    # not know it. The harness force-solves a controller, which
    # produces ONE arrangement; a level's second, third and fourth
    # solutions are different arrangements and it can never make them.
    # Like the drawers, a Skip grants them outright and nothing else
    # does.
    #
    # MEASURED, not assumed: on the DLC gate of 2026-09-21 `Containers`
    # sat on `Filing Cabinet - Solution 2`, the run collected only
    # Solution 1, and DLC1 Bathroom Cupboard - which needs Containers -
    # could never be beaten. It finished 7 of 8 while this model
    # predicted 8. The 19/25 run had reached Containers only because a
    # Skip happened to land on that card and granted Solutions 1-3.
    alternates = {loc for loc in present
                  if _solution_number(loc) is not None
                  and _solution_number(loc) >= 2}
    return (excluded & present) | alternates


def _solution_number(location):
    """N from '<level> - Solution N', or None."""
    marker = " - Solution "
    if marker not in location:
        return None
    tail = location.rsplit(marker, 1)[1].strip()
    return int(tail) if tail.isdigit() else None


def open_for(packs, boundaries):
    """How many slots are open holding `packs` Progressive Puzzle Packs.

    boundaries is cumulative: [5, 8] means five open free and the first
    pack opens the rest. Holding more packs than the seed has does not
    open more than every slot.
    """
    return boundaries[min(packs, len(boundaries) - 1)]


def play_on_paper(plan, starting, placements, unforceable=(), park=True,
                  rounds=ROUNDS, unearnable=None, known_unforceable=None):
    """Run choose_slot against the seed's real economy.

    `unforceable` names slots the harness cannot complete by forcing
    controllers - it has to spend a Skip. That is the harness's one real
    limitation and the reason KNOWN_UNFORCEABLE exists.

    Returns a dict of what happened, so a failing test can print it.
    """
    slots = plan["slots"]
    where = e2e.locations_for_slots(plan)
    boundaries = plan["boundaries"]
    e2e._load_ability_tables()
    if unearnable is None:
        unearnable = container_excludes(where)

    held = {i for i in starting if i in e2e._ABILITY_NAMES}
    skips = sum(1 for i in starting if i == "Skip")
    packs = sum(1 for i in starting if i == "Progressive Puzzle Pack")

    collected, beaten, skipped = set(), set(), set()
    barren, fruitless = {}, {}
    attempts = {i: 0 for i in range(len(slots))}
    idle, spent, credits = 0, 0, False
    trace = []
    spent_on = []
    refused_on = []
    if known_unforceable is None:
        known_unforceable = set(e2e.KNOWN_UNFORCEABLE)

    for _round in range(rounds):
        open_slots = open_for(packs, boundaries)
        slot, why, buy = e2e.choose_slot(
            slots, plan, where, open_slots=open_slots, beaten=set(beaten),
            attempts=attempts, barren=barren, held=held, collected=collected,
            skipped=skipped, credits=credits, idle=idle,
            skips_out=(spent >= skips))
        if slot is None:
            trace.append(f"stop: {why}")
            break

        attempts[slot] += 1
        got = set()
        forced = slot not in unforceable
        if forced:
            for loc in where[slot]:
                if loc in collected or loc in unearnable:
                    continue
                if set(plan["requirements"].get(loc, ())) <= held:
                    got.add(loc)

        # A Skip grants the whole card, which is the only way anything on
        # an unforceable level ever comes out.
        # MIRROR play()'s DEFERRAL or the prediction is fiction. An
        # unforceable level that is still ability-gated holds its Skip
        # while other slots are moving, and spends once the run is
        # stuck. STUCK_AFTER is read from the harness so the two cannot
        # drift.
        gated_now = any(
            not set(plan["requirements"].get(loc, ())) <= held
            for loc in where[slot] if loc not in collected)
        defer = gated_now and not buy and idle < e2e.STUCK_AFTER

        # MODEL THE REFUSAL TOO, or this cannot see a starved run.
        #
        # play() refuses a Skip on a level that reached the skip path
        # without the stall path choosing it and is not on
        # KNOWN_UNFORCEABLE. This model spent Skips freely and so
        # predicted a clean sheet while the real DLC run was refused a
        # Skip on DLC1 Filing Cabinet - the very card holding
        # `Containers` - and finished 2 of 8. KNOWN_UNFORCEABLE lists
        # two BASE-GAME levels, so in a DLC run every legitimate Skip
        # looks like a surprise.
        surprise = not buy and slots[slot][1] not in known_unforceable
        wants_skip = (buy or (not got and slot not in beaten)) \
            and not defer and not surprise
        if surprise and not buy:
            refused_on.append(slots[slot][1])
        if wants_skip and spent < skips and slot not in skipped:
            # RECORD WHAT THE GATE WILL JUDGE, not just the count. Two
            # of the 25 assertions are about WHICH level a Skip went to
            # and whether it was still ability-gated at the time, and
            # those are the two that have been discovered by running
            # the gate rather than predicted. Everything needed to
            # decide them is already here.
            spent_on.append({
                "slot": slot,
                "level_id": slots[slot][1],
                "reason": ("last-resort" if why == e2e.LAST_RESORT
                           else "unreachable-check" if buy
                           else "unforceable"),
                "gated": gated_now,
            })
            spent += 1
            skipped.add(slot)
            got |= {loc for loc in where[slot] if loc not in collected}

        for loc in sorted(got):
            collected.add(loc)
            item = placements.get(loc)
            if item in e2e._ABILITY_NAMES:
                held.add(item)
            elif item == "Progressive Puzzle Pack":
                packs += 1
            elif item == "Skip":
                skips += 1

        # BEATEN MEANS THE TOKEN, exactly as play() counts it.
        #
        # This used to mean "every location collected", which sounds
        # stricter and is simply a different thing: a card carrying a
        # drawer location the harness cannot pull is BEATEN in the game
        # and never complete here. The model therefore never produced a
        # beaten-but-incomplete slot - the one state the stall Skip
        # exists for - so it could not exercise the stall path at all.
        #
        # That blind spot let two regressions through a green suite on
        # 2026-09-21, both of which stopped the stall path finding a
        # candidate and cost the gate the credits and the goal.
        token = None
        for loc in where[slot]:
            if loc.endswith(" - Beaten"):
                token = loc
                break
        if token is None:
            if all(loc in collected for loc in where[slot]):
                beaten.add(slot)
        elif token in collected:
            beaten.add(slot)
        if len(beaten) >= len(slots):
            credits = True

        trace.append(
            f"r{_round:03d} slot {slot} {slots[slot][1]:30s} "
            f"{'skip ' if wants_skip else '     '}+{len(got):2d} "
            f"open={open_slots} packs={packs} beaten={len(beaten)}"
            f" why={why}")

        if got:
            idle = 0
            fruitless[slot] = 0
        else:
            idle += 1
            fruitless[slot] = fruitless.get(slot, 0) + 1
            if park and fruitless[slot] >= 2:
                barren[slot] = frozenset(held)

        if credits:
            break

    return {
        "beaten": len(beaten), "slots": len(slots), "attempts": attempts,
        "spent": spent, "spent_on": spent_on, "refused_on": refused_on,
        "packs": packs,
        "held": sorted(held),
        "collected": len(collected),
        # What the gate actually asserts is BEATEN TOKENS, not "every
        # location collected". A card with an unearnable drawer location
        # on it is still beaten; it is just not cleared.
        "tokens": sum(1 for loc in collected if loc.endswith(" - Beaten")),
        "unearnable": sorted(unearnable),
        "outstanding": sorted(set(plan["requirements"]) - collected),
        "trace": trace,
    }


class TestTheSeedIsClearableOnPaper(unittest.TestCase):
    """The pre-flight the DLC gate never had.

    Every one of the two dozen gate runs on 2026-09-20 asked this
    question by playing it for fifteen minutes. It costs milliseconds.
    """

    @classmethod
    def setUpClass(cls):
        cls.plan, cls.starting, cls.placements = load()
        cls.outcome = play_on_paper(cls.plan, cls.starting, cls.placements)

    def report(self):
        r = self.outcome
        return ("\n" + "\n".join(r["trace"][-25:])
                + f"\n\nbeaten {r['beaten']}/{r['slots']}  "
                  f"skips spent {r['spent']}  packs {r['packs']}  "
                  f"abilities {r['held']}\n"
                  f"outstanding: {r['outstanding'][:8]}")

    def test_the_run_reaches_every_slot(self):
        """A slot the packs never open cannot be beaten, however well the
        scheduler behaves. This is the deadlock the model was blind to."""
        self.assertEqual(self.outcome["slots"], self.outcome["beaten"],
                         self.report())

    def test_it_does_not_grind(self):
        """The gate gives up at 60 rounds. Spinning on paper is spinning."""
        worst = max(self.outcome["attempts"].values())
        self.assertLessEqual(worst, 6,
                             f"slot visited {worst} times: {self.report()}")

    def test_it_stays_inside_the_skip_supply(self):
        available = sum(1 for i in self.starting if i == "Skip") + \
            sum(1 for v in self.placements.values() if v == "Skip")
        self.assertLessEqual(self.outcome["spent"], available, self.report())

    def test_every_slot_banks_its_beaten_token(self):
        """What the gate actually asserts, and it is not the same thing.

        A card carrying a drawer location the harness cannot pull is
        BEATEN without being cleared. Asserting only "every location
        collected" would have this file disagreeing with the gate about
        what success means.
        """
        self.assertEqual(self.outcome["slots"], self.outcome["tokens"],
                         self.report())

    def test_the_container_locations_are_actually_modelled(self):
        """Guard against the optimism that made the first answer wrong.

        With these collected for free the model said 11 rounds and zero
        Skips while the real run was at round 11 with 3 of 8 and a Skip
        already spent. If this set ever comes back empty, the model has
        gone back to playing a game the harness cannot play.

        DLC seeds only. The excluded list is written by the --dlc half
        of the yaml, so a base-game seed legitimately has none and this
        would otherwise fail for being right.
        """
        if not any(level_id.startswith("DLC")
                   for _i, level_id in self.plan["slots"]):
            self.skipTest("base seed - the container excludes are DLC-only")
        self.assertTrue(self.outcome["unearnable"],
                        "no container locations modelled as unearnable - "
                        "either the yaml stopped excluding them or the "
                        "matching broke")

    def test_the_only_things_left_are_the_ones_it_cannot_earn(self):
        """Nothing outstanding except the harness's own blind spot.

        CORRECTED 2026-09-21. This used to assert "one Skip per
        container level", which was an artifact of the model calling a
        slot beaten only when EVERY location was collected. A card
        carrying a drawer the harness cannot pull is beaten in the game
        and its drawer location simply stays uncollected - and the
        gate's yaml keeps progression off exactly those locations, so
        no Skip is owed for them.

        What must hold is the weaker, true thing: anything still
        outstanding is something the harness was never able to earn.
        """
        leftover = set(self.outcome["outstanding"])
        unearnable = set(self.outcome["unearnable"])
        self.assertTrue(leftover <= unearnable,
                        f"left behind something it could have earned: "
                        f"{sorted(leftover - unearnable)[:5]}{self.report()}")

    def test_the_seed_really_needs_its_packs(self):
        """Guard against a vacuous pass.

        If every slot were open from the start, this whole file would be
        testing the same thing test_scheduler already does.
        """
        self.assertLess(self.plan["boundaries"][0], len(self.plan["slots"]),
                        "this seed opens every slot for free - it cannot "
                        "exercise the pack economy")


class TestTheModelNoticesADeadlock(unittest.TestCase):
    """A model that has never failed is a model nobody has checked.

    The offline simulation once approved a change that then cost a DLC
    gate 23/25 -> 19/25, because it had no cat traps and no phased
    reveals. These two make the model fail on purpose.
    """

    @classmethod
    def setUpClass(cls):
        cls.plan, cls.starting, cls.placements = load()

    def test_a_run_with_no_skips_and_an_unforceable_slot_is_caught(self):
        starting = [i for i in self.starting if i != "Skip"]
        placements = {k: v for k, v in self.placements.items() if v != "Skip"}
        out = play_on_paper(self.plan, starting, placements,
                            unforceable={0, 1, 2, 3, 4})
        self.assertLess(out["beaten"], out["slots"],
                        "the model cleared a run it had no way to clear")

    def test_withholding_the_pack_strands_the_closed_slots(self):
        """The deadlock this file exists for: the pack never collected."""
        placements = {k: ("Hint Page" if v == "Progressive Puzzle Pack" else v)
                      for k, v in self.placements.items()}
        out = play_on_paper(self.plan, self.starting, placements)
        self.assertLess(out["beaten"], out["slots"],
                        "slots behind a pack were beaten without the pack")


if __name__ == "__main__":
    unittest.main(verbosity=2)
