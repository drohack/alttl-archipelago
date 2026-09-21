"""The harness's scheduler, tested without a game.

WHY THIS FILE EXISTS. Every scheduling bug this harness has had was found by
running a fifteen-minute release gate and reading the wreckage afterwards -
about two hours of them in one afternoon, on 2026-09-20, for defects that are
pure arithmetic over sets.

A CORRECTION WORTH KEEPING: that afternoon I claimed repeatedly that the gate
generates a new seed each run and so runs were incomparable. It does not -
Generate.py is called with --seed 20260906 every time and slot 0 is the same
level in every transcript. The real variance was cat traps, which fire at 25%
and reset a puzzle mid-solve; `--steady` turns them off for exactly this
reason. Blaming the seed excused a lot of noise that was actually my own
changes.

Each test below is a failure that actually happened, reduced to the smallest
world that shows it. They run in milliseconds and they are deterministic.

    py -3.13 tools/test_scheduler.py
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e


def world(levels, requirements, order=None):
    """A fake run: slot list, per-location requirements, planned order."""
    slots = [(100 + i, name) for i, name in enumerate(levels)]
    plan = {
        "slots": slots,
        "boundaries": [len(levels)],
        "requirements": {loc: list(abilities)
                         for loc, abilities in requirements.items()},
        "order": order or list(levels),
    }
    # locations_for_slots maps by DISPLAY name; these fakes use the level id
    # as its own display, which names.json will not know about. Build the
    # mapping directly so the test does not depend on the real tables.
    where = {}
    for i, name in enumerate(levels):
        where[i] = [loc for loc in requirements if loc.startswith(name + " - ")]
    return slots, plan, where


def code_only(text):
    """The source with comment lines removed.

    USE THIS FOR EVERY "the source must not contain X" ASSERTION. Three
    times in one session a test matched its own explanation: a ban on
    "three puzzles" tripped on the note recording the correction, a
    check for no `continue` matched the comment saying why there is no
    `continue`, and a ban on `restored = set(range(n))` matched the
    comment naming the bug it replaced. A test you cannot document
    without breaking is measuring the prose.
    """
    return "\n".join(line for line in text.splitlines()
                     if not line.strip().startswith("#"))


def pick(slots, plan, where, **kwargs):
    kwargs.setdefault("open_slots", len(slots))
    kwargs.setdefault("beaten", set())
    kwargs.setdefault("attempts", {i: 0 for i in range(len(slots))})
    kwargs.setdefault("barren", {})
    kwargs.setdefault("held", set())
    kwargs.setdefault("collected", set())
    kwargs.setdefault("skipped", set())
    kwargs.setdefault("credits", False)
    kwargs.setdefault("idle", 0)
    kwargs.setdefault("skips_out", False)
    return e2e.choose_slot(slots, plan, where, **kwargs)


class TestItPlaysWhatItCan(unittest.TestCase):

    def test_a_level_needing_nothing_is_playable(self):
        slots, plan, where = world(
            ["A"], {"A - Solution 1": [], "A - Beaten": []})
        slot, why, _ = pick(slots, plan, where)
        self.assertEqual((0, "playable"), (slot, why))

    def test_a_level_whose_every_location_is_gated_is_held_back(self):
        slots, plan, where = world(
            ["A"], {"A - Solution 1": ["Swapping"], "A - Beaten": ["Swapping"]})
        slot, why, _ = pick(slots, plan, where)
        self.assertIsNone(slot, why)

    def test_partial_play_counts(self):
        """Holding one ability is enough to visit, even short of the union.

        Demanding the level's whole union before touching it is the stricter
        rule and it is wrong: a player walks in, tidies the group they can
        reach, and comes back. A gate run enforcing the union beat ONE puzzle
        of eight.
        """
        slots, plan, where = world(
            ["A"], {"A - Part": [], "A - Beaten": ["Swapping"]})
        slot, why, _ = pick(slots, plan, where)
        self.assertEqual((0, "playable"), (slot, why))


class TestItDoesNotSpin(unittest.TestCase):

    def test_attempts_rotate_before_the_planned_order(self):
        """The plan is a tiebreak, never an override.

        Sorting by planned rank first locked a run onto the lowest-ranked
        level and retried it every round: Desktop Computer from round 2 to
        the end, one puzzle beaten.
        """
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": [], "A - Beaten": [], "B - S": [], "B - Beaten": []},
            order=["A", "B"])
        # A has been tried once, B not at all - B must go next despite the
        # plan naming A first.
        slot, _, _ = pick(slots, plan, where, attempts={0: 1, 1: 0})
        self.assertEqual(1, slot)

    def test_a_slot_proved_empty_is_not_offered_again(self):
        """The barren guard, both halves.

        slot_has_work reads what a PLAYER could earn; the harness is weaker
        and cannot perform interactions. Where they disagree the slot looks
        permanently worth visiting - a run revisited Stamps from round 9 to
        round 52 collecting nothing each time.
        """
        slots, plan, where = world(["A"], {"A - S": [], "A - Beaten": []})
        slot, _, _ = pick(slots, plan, where, barren={0: frozenset()})
        self.assertIsNone(slot)

    def test_parking_lifts_when_an_ability_arrives(self):
        """Parked on what was held AT THE TIME, so new items un-park it."""
        slots, plan, where = world(["A"], {"A - S": [], "A - Beaten": []})
        slot, why, _ = pick(slots, plan, where,
                            barren={0: frozenset()}, held={"Drawer"})
        self.assertEqual((0, "playable"), (slot, why))


class TestItRecovers(unittest.TestCase):

    def test_a_stranded_item_buys_a_skip_even_with_every_slot_open(self):
        """Stuck is "not every puzzle beaten", not "slots still closed".

        A DLC gate ended at seven of eight with a Skip still unspent: all
        slots were open, so the old condition never fired, and Containers sat
        on a Filing Cabinet location the harness cannot force.
        """
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": [], "A - Beaten": [], "A - Stranded": [],
             "B - S": ["Containers"], "B - Beaten": ["Containers"]})
        slot, why, buy = pick(slots, plan, where,
                              beaten={0}, collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()})
        self.assertEqual((0, True), (slot, buy), why)
        self.assertIn("skip", why)

    def test_the_skip_path_is_not_swallowed_by_the_revisit_shortcut(self):
        """buy_skip must be reported, or the caller short-circuits the visit.

        The revisit branch returns early for any beaten slot. That swallowed
        the stuck path: the Skip was announced, solve_level never ran, and
        nothing was spent. Nineteen rounds at three of eight.
        """
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": [], "A - Beaten": [], "A - Stranded": [],
             "B - S": ["Containers"], "B - Beaten": ["Containers"]})
        _, _, buy = pick(slots, plan, where,
                         beaten={0}, collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()})
        self.assertTrue(buy)

    def test_a_skip_is_not_spent_twice_on_the_same_slot(self):
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": [], "A - Beaten": [], "A - Stranded": [],
             "B - S": ["Containers"], "B - Beaten": ["Containers"]})
        slot, why, _ = pick(slots, plan, where, beaten={0}, skipped={0},
                            collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()})
        self.assertIsNone(slot, why)

    def test_everything_beaten_but_no_credits_goes_back(self):
        slots, plan, where = world(["A"], {"A - S": [], "A - Beaten": []})
        slot, why, _ = pick(slots, plan, where, beaten={0},
                            collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()}, credits=False)
        self.assertEqual(0, slot)
        self.assertIn("revisit", why)

    def test_a_finished_run_stops(self):
        slots, plan, where = world(["A"], {"A - S": [], "A - Beaten": []})
        slot, why, _ = pick(slots, plan, where, beaten={0},
                            collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()}, credits=True)
        self.assertIsNone(slot)
        self.assertEqual("nothing left to try", why)


class TestTheAbilityTable(unittest.TestCase):

    def test_dlc_abilities_are_known(self):
        """dlcAbilities is keyed by DLC first, then by ability.

        Reading it flat walked the DLC KEYS as if they were abilities, so
        Distributing never entered the table. abilities_held could not see it
        arrive and every DLC-gated level was held back forever: two of eight
        beaten in 76 rounds, 17 Skips spent, on a seed that clears 42/42.
        """
        e2e._load_ability_tables()
        self.assertIn("Distributing", e2e._ABILITY_NAMES)
        self.assertIn("Swapping", e2e._ABILITY_NAMES)

    def test_held_reads_the_item_log(self):
        held = e2e.abilities_held(
            "[Info] received item: Distributing\n"
            "[Info] received item: Hint Page\n")
        self.assertEqual({"Distributing"}, held)

    def test_beaten_tokens_count_as_collected(self):
        """A Beaten token is logged as `beaten: X`, never `check: X`.

        Reading only `check:` left every Beaten location permanently
        uncollected, so its slot always looked like it had work.
        """
        done = e2e.collected_locations(
            "[Info] check: A - Part\n[Info] beaten: A - Beaten\n")
        self.assertEqual({"A - Part", "A - Beaten"}, done)


class TestAStubbornLevelDoesNotStarveTheRun(unittest.TestCase):
    """The Clock Cupboard block, and why the trigger is progress.

    A level can have work the seed's table says is earnable and the harness
    can never collect - it forces controller flags and cannot pull a drawer.
    That level stays a candidate every round, so a Skip path gated on "no
    candidates left" never fires. DLC1 Clock Cupboard did exactly this:
    three of eight beaten, four levels waiting on an item one Skip would
    have freed.
    """

    def stubborn(self):
        # A is beaten and skippable; B looks earnable forever.
        return world(["A", "B"],
                     {"A - S": [], "A - Beaten": [], "A - Stranded": [],
                      "B - S": [], "B - Beaten": []})

    def test_while_the_run_moves_the_skip_path_stays_out_of_the_way(self):
        slots, plan, where = self.stubborn()
        slot, why, buy = pick(slots, plan, where, beaten={0},
                              collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()}, idle=0)
        self.assertEqual((1, False), (slot, buy), why)

    def test_once_nothing_finishes_a_skip_is_bought(self):
        slots, plan, where = self.stubborn()
        slot, why, buy = pick(slots, plan, where, beaten={0},
                              collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()},
                              idle=e2e.STUCK_AFTER)
        self.assertEqual((0, True), (slot, buy), why)

    def test_a_stall_with_no_skip_left_still_offers_the_level(self):
        """Out of Skips is not a reason to stop trying."""
        slots, plan, where = self.stubborn()
        slot, why, buy = pick(slots, plan, where, beaten={0}, skipped={0},
                              collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()},
                              idle=e2e.STUCK_AFTER)
        self.assertEqual((1, False), (slot, buy), why)


class TestTheSchedulerIsActuallyWiredIn(unittest.TestCase):
    """A tested function nobody calls is worth nothing.

    choose_slot is pure and covered, but play() has to PASS it the run's
    state or the coverage is theatre. This has gone wrong three times in one
    session in different forms - an inline copy that drifted, a parameter
    left at its default, a helper the self-test could not see - so the
    wiring gets its own assertion rather than being assumed.

    Reading the source is crude. It is also the only way to check a call
    that needs a game, a server and a seed to execute.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_play_calls_choose_slot(self):
        self.assertIn("current, why, buy_skip = choose_slot(", self.source())

    def test_play_parks_a_slot_that_achieved_nothing(self):
        """The simulation models parking; only this checks play() does it.

        TestAWholeRun shows what the guard is worth - 2 attempts against
        200 when Skips run out - but it does that with its own `park` flag,
        not by running the harness. If play() stops parking, every one of
        those tests still passes and the real run grinds.
        """
        text = self.source()
        # THE CONDITION, not just the assignment. Disabling the guard
        # leaves `barren[current] = ...` sitting there unreachable, so an
        # assertion on the assignment alone passes while parking is dead -
        # which is exactly what the mutation run showed.
        self.assertIn("if not done and len(collected_locations(transcript))",
                      text,
                      "play() no longer parks a slot that achieved nothing")
        self.assertIn("barren[current] = frozenset(held)", text)

    def test_play_parks_only_after_two_consecutive_misses(self):
        """The threshold and the reset, both of which live in play().

        TestTransientFailuresDoNotEndTheRun proves what the threshold is
        worth, but it does so with the simulation's own park_after. If
        play() drops to one miss, a cat-trapped level gets abandoned and
        every one of those tests still passes - which is how a DLC gate
        ended at 2 of 8.
        """
        text = self.source()
        self.assertIn("if fruitless[current] >= 2:", text,
                      "play() parks on the first miss again")
        self.assertIn("fruitless[current] = 0", text,
                      "nothing resets the miss counter, so two misses a "
                      "whole run apart will park a working level")

    def test_play_records_a_refused_skip(self):
        """The supply flag has to be SET somewhere, not just read.

        choose_slot honours skips_out, and the tests above prove that. None
        of them can prove play() ever sets it - and if it does not, the
        recovery believes the supply is endless and loops on a Skip it
        cannot buy. That is the loop that ended the last DLC run.
        """
        text = self.source()
        self.assertIn("skips_out = True", text,
                      "nothing records the mod refusing a Skip")
        self.assertIn('"received item: Skip" in', text,
                      "nothing refills the supply when a Skip arrives")

    def test_play_passes_every_piece_of_state(self):
        text = self.source()
        call = text.split("current, why, buy_skip = choose_slot(", 1)[1]
        # To the blank line after the call, NOT to the first ")" - the
        # arguments contain set(beaten), so splitting on a paren truncates
        # the call and the assertions below pass or fail at random.
        call = call.split(os.linesep * 2, 1)[0].split("\n\n", 1)[0]
        # NAME=NAME, not just "name=". Checking only for the keyword lets
        # `idle=0` through, which passes the argument and disables the
        # feature - the exact shape of the bug this guards. `beaten` is the
        # one exception: play() holds it as a dict and passes set(beaten).
        for name in ("open_slots", "attempts", "barren", "held",
                     "collected", "skipped", "credits", "idle", "skips_out"):
            self.assertIn(f"{name}={name}", call,
                          f"play() no longer passes {name} through - the "
                          f"scheduler is being driven with stale or "
                          f"default state")
        self.assertIn("beaten=set(beaten)", call)


class TestTheArrowCheckPicksASolvableSlot(unittest.TestCase):
    """Two navigation assertions failed for want of this.

    The arrow session holds nothing and must finish a puzzle before it can
    press Next. Starting on slot 0 regardless meant a gated level there
    broke the check - a DLC seed put DLC2 Pizza first, behind Distributing.
    """

    def test_it_skips_a_gated_first_slot(self):
        slots, plan, where = world(
            ["Gated", "Open"],
            {"Gated - S": ["Distributing"], "Gated - Beaten": ["Distributing"],
             "Open - S": [], "Open - Beaten": []})
        self.assertEqual(1, e2e.arrow_slot(slots, plan, where))

    def test_it_uses_the_first_slot_when_that_one_is_fine(self):
        slots, plan, where = world(
            ["Open", "Other"],
            {"Open - S": [], "Open - Beaten": [],
             "Other - S": [], "Other - Beaten": []})
        self.assertEqual(0, e2e.arrow_slot(slots, plan, where))

    def test_it_falls_back_rather_than_skipping_the_check(self):
        """Nothing solvable: run it on slot 0 and let it fail honestly."""
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": ["Drawer"], "A - Beaten": ["Drawer"],
             "B - S": ["Drawer"], "B - Beaten": ["Drawer"]})
        self.assertEqual(0, e2e.arrow_slot(slots, plan, where))


class TestItDoesNotChaseASkipItCannotBuy(unittest.TestCase):
    """The recovery must know whether a Skip can actually be spent.

    It picks a beaten slot so solve_level will buy one. With the supply
    exhausted the mod answers "there is no Skip to spend yet", nothing
    happens, and the slot is never marked skipped - so it is chosen again
    next round. A DLC gate cycled that way from round 20, stuck at five of
    eight, until it was killed.
    """

    def stalled(self):
        return world(["A", "B"],
                     {"A - S": [], "A - Beaten": [], "A - Stranded": [],
                      "B - S": ["Containers"], "B - Beaten": ["Containers"]})

    def test_with_skips_available_it_buys_one(self):
        slots, plan, where = self.stalled()
        slot, why, buy = pick(slots, plan, where, beaten={0},
                              collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()},
                              idle=e2e.STUCK_AFTER, skips_out=False)
        self.assertEqual((0, True), (slot, buy), why)

    def test_with_none_left_it_stops_asking(self):
        slots, plan, where = self.stalled()
        slot, why, buy = pick(slots, plan, where, beaten={0},
                              collected={"A - S", "A - Beaten"},
                              barren={0: frozenset()},
                              idle=e2e.STUCK_AFTER, skips_out=True)
        self.assertFalse(buy, why)
        self.assertIsNone(slot, why)


def simulate(levels, requirements, placements, unreachable=(), skips=2,
             park=True, rounds=200, flaky=(), park_after=2):
    """Play a whole run on paper and report what it cost.

    `park` mirrors production, where an attempt that achieves nothing
    parks the slot until an item arrives. Pass False to measure what that
    guard is worth - see TestAWholeRun.

    `flaky` names slots that fail their FIRST attempt and work afterwards,
    which the real harness does to itself: a cat trap resets a puzzle
    mid-solve, and a phased level reveals its controllers only on a later
    visit. Without this the model has no transient failures at all, and it
    approved parking-on-first-miss - which then ended a DLC gate at 2 of 8
    where the same seed had reached 6 of 8 without it.

    Models the harness's ONE weakness: `unreachable` names locations it can
    never collect by forcing a controller, however reachable the seed's
    table says they are - a drawer it cannot pull, a switch it cannot flip.
    Everything else follows from choose_slot.

    Returns (beaten count, attempts per slot, skips spent).
    """
    slots, plan, where = world(levels, requirements)
    collected, beaten, skipped, barren = set(), set(), set(), {}
    fruitless = {}
    attempts = {i: 0 for i in range(len(slots))}
    held = set()
    idle = 0
    spent = 0
    e2e._load_ability_tables()

    for _ in range(rounds):
        slot, _why, buy = e2e.choose_slot(
            slots, plan, where, open_slots=len(slots), beaten=set(beaten),
            attempts=attempts, barren=barren, held=held, collected=collected,
            skipped=skipped, credits=False, idle=idle,
            skips_out=(spent >= skips))
        if slot is None:
            break

        attempts[slot] += 1
        got = set()
        if slot in flaky and attempts[slot] == 1:
            # First visit achieves nothing, exactly as a cat-trapped or
            # phased level does. Everything below is skipped.
            fruitless[slot] = fruitless.get(slot, 0) + 1
            idle += 1
            if park and fruitless[slot] >= park_after:
                barren[slot] = frozenset(held)
            continue
        for loc in where[slot]:
            if loc in collected:
                continue
            if not set(plan["requirements"][loc]) <= held:
                continue
            # A Skip grants the whole puzzle; otherwise the harness is
            # limited to what it can actually perform.
            if buy or loc not in unreachable:
                got.add(loc)

        # THE HARNESS HAS TWO SKIP MECHANISMS and a simulation with one is
        # a simulation of a different program.
        #
        #   buy      - choose_slot picked a BEATEN slot so a Skip releases
        #              items stranded on it.
        #   unforced - solve_level spends one on an UNBEATEN level it could
        #              not force at all, which is the only way an item on
        #              such a level ever comes out.
        #
        # Modelling only the first said the run could never clear a seed
        # whose key item sat on an unforceable unbeaten level - which the
        # real harness handles routinely.
        unforced = (not buy and not got and slot not in beaten
                    and spent < skips)
        if buy or unforced:
            spent += 1
            skipped.add(slot)
        if unforced:
            got = {loc for loc in where[slot] if loc not in collected}

        for loc in got:
            collected.add(loc)
            item = placements.get(loc)
            if item and item in e2e._ABILITY_NAMES:
                held.add(item)

        # BEATEN MEANS THE TOKEN, as play() counts it - not "every
        # location collected".
        #
        # The stricter reading kept this model from ever producing a
        # beaten-but-incomplete slot, which is the ONLY state the stall
        # Skip exists for. So the stall path was unreachable here and
        # two changes that disabled it passed a green suite on
        # 2026-09-21, costing the base gate the credits and the goal.
        # With the token reading, Stubborn below is beaten while its
        # stranded location is not, and only the stall path frees it.
        token = next((l for l in where[slot]
                      if l.endswith(" - Beaten")), None)
        if token is None:
            if all(loc in collected for loc in where[slot]):
                beaten.add(slot)
        elif token in collected:
            beaten.add(slot)

        # THE CREDITS OPEN WHEN EVERY PUZZLE IS BEATEN, and the loop ends
        # there. Holding credits=False forever made the revisit path fire
        # for the rest of the budget and reported 99 attempts per slot -
        # an artifact of the model, not of the scheduler.
        if len(beaten) >= len(slots) and not got:
            break

        if got:
            idle = 0
            fruitless[slot] = 0
        else:
            idle += 1
            fruitless[slot] = fruitless.get(slot, 0) + 1
            if park and fruitless[slot] >= park_after:
                barren[slot] = frozenset(held)

    return len(beaten), attempts, spent


class TestAWholeRun(unittest.TestCase):
    """What the rules do to a RUN, not to one decision.

    The scenario is the one the DLC gate keeps producing: a level whose
    remaining location the harness can never collect, holding an ability a
    later level needs. Only a Skip frees it.
    """

    LEVELS = ["Start", "Stubborn", "Locked"]
    REQS = {
        "Start - S": [], "Start - Beaten": [],
        # The harness can never collect this one, and Drawer sits on it.
        "Stubborn - Stuck": [], "Stubborn - Beaten": [],
        "Locked - S": ["Drawer"], "Locked - Beaten": ["Drawer"],
    }
    PLACED = {"Stubborn - Stuck": "Drawer"}
    BLOCKED = {"Stubborn - Stuck"}

    def test_the_run_finishes_by_spending_a_skip(self):
        beaten, _attempts, spent = simulate(
            self.LEVELS, self.REQS, self.PLACED, unreachable=self.BLOCKED)
        self.assertEqual(3, beaten, "the run did not clear the seed")
        self.assertGreaterEqual(spent, 1, "it never spent a Skip")

    def test_it_does_not_grind(self):
        """A bounded number of attempts per level, not dozens.

        Measured on a real DLC gate before this was tested: slot 2 took 8
        attempts and slot 4 took 7, out of 23 for eight levels. Two levels
        absorbed two thirds of the run.
        """
        _beaten, attempts, _spent = simulate(
            self.LEVELS, self.REQS, self.PLACED, unreachable=self.BLOCKED)
        worst = max(attempts.values())
        self.assertLessEqual(worst, 4,
                             f"a level was attempted {worst} times: "
                             f"{attempts}")

    def test_without_skips_it_stops_rather_than_spinning(self):
        beaten, attempts, spent = simulate(
            self.LEVELS, self.REQS, self.PLACED, unreachable=self.BLOCKED,
            skips=0)
        self.assertEqual(0, spent)
        self.assertLess(beaten, 3, "it should not have cleared the seed")
        worst = max(attempts.values())
        self.assertLessEqual(worst, 4,
                             f"it span {worst} times with no way forward: "
                             f"{attempts}")

    def test_parking_is_what_stops_the_grind(self):
        """The measurement that justifies the guard.

        Re-added after being reverted once: with Skips available it changes
        nothing, and with them exhausted it is the difference between two
        attempts and two hundred.
        """
        with_park = simulate(self.LEVELS, self.REQS, self.PLACED,
                             unreachable=self.BLOCKED, skips=0, park=True)
        without = simulate(self.LEVELS, self.REQS, self.PLACED,
                           unreachable=self.BLOCKED, skips=0, park=False)
        self.assertLessEqual(max(with_park[1].values()), 4)
        self.assertGreater(max(without[1].values()), 50,
                           "without parking this used to grind; if it no "
                           "longer does, the guard may be redundant")

    def test_parking_costs_nothing_when_skips_are_available(self):
        a = simulate(self.LEVELS, self.REQS, self.PLACED,
                     unreachable=self.BLOCKED, skips=2, park=True)
        b = simulate(self.LEVELS, self.REQS, self.PLACED,
                     unreachable=self.BLOCKED, skips=2, park=False)
        self.assertEqual((a[0], a[2]), (b[0], b[2]),
                         "parking changed the outcome or the Skip count - "
                         "that is what made it a regression last time")


class TestTransientFailuresDoNotEndTheRun(unittest.TestCase):
    """Cat traps and phased reveals, which the harness inflicts on itself.

    A level can achieve nothing on one visit and everything on the next
    with no item in between. Parking on a single empty attempt treats that
    as permanent and abandons the level - a DLC gate ended at 2 of 8 that
    way, against 6 of 8 with no parking at all.
    """

    LEVELS = ["Start", "Flaky", "Later"]
    REQS = {
        "Start - S": [], "Start - Beaten": [],
        "Flaky - S": [], "Flaky - Beaten": [],
        "Later - S": ["Drawer"], "Later - Beaten": ["Drawer"],
    }
    PLACED = {"Flaky - S": "Drawer"}

    def test_parking_on_the_first_miss_abandons_a_flaky_level(self):
        beaten, _a, _s = simulate(self.LEVELS, self.REQS, self.PLACED,
                                  flaky={1}, park_after=1, skips=0)
        self.assertLess(beaten, 3,
                        "if this now clears, the flaky model is not biting")

    def test_two_misses_lets_it_recover(self):
        beaten, attempts, _s = simulate(self.LEVELS, self.REQS, self.PLACED,
                                        flaky={1}, park_after=2, skips=0)
        self.assertEqual(3, beaten,
                         f"a level that works on its second visit was "
                         f"abandoned: {attempts}")

    def test_and_it_still_does_not_grind(self):
        _b, attempts, _s = simulate(
            self.LEVELS, self.REQS, self.PLACED, unreachable={"Flaky - S"},
            flaky={1}, park_after=2, skips=0)
        worst = max(attempts.values())
        self.assertLessEqual(worst, 5, f"it span {worst} times: {attempts}")


class TestTheFlagsDoWhatTheyPrint(unittest.TestCase):
    """The yaml the gate generates from, read as text.

    All three of these were found by reading the harness rather than by
    running it, which is the entire point of this file. --steady printed
    "cat traps off" and set a global nothing consulted; every comparison
    of two "repeatable" runs was made at a 25% trap rate.
    """

    def test_steady_turns_cat_traps_off(self):
        self.assertIn("cat_trap_chance: 0",
                      e2e.yaml_text(False, True, False),
                      "--steady printed 'cat traps off' and changed nothing")

    def test_the_release_gate_still_faces_cat_traps(self):
        """A trap-free gate is not the run players get."""
        self.assertIn("cat_trap_chance: 25", e2e.yaml_text(False, False, False))

    def test_steady_keeps_the_ability_gates(self):
        """This is the whole difference between --steady and --quick.

        A steady run faces every gate, it just faces them identically
        twice. If steady also switched locks off it would be --quick with
        a different name, and gating bugs would stop being reachable in
        the one mode used to compare harness changes.
        """
        text = e2e.yaml_text(False, True, False)
        self.assertIn("ability_locks: true", text)
        self.assertIn("starting_abilities: 1", text)

    def test_quick_turns_the_gates_off(self):
        text = e2e.yaml_text(True, False, False)
        self.assertIn("ability_locks: false", text)
        self.assertIn("cat_trap_chance: 0", text)

    def test_the_dlc_block_is_only_written_under_dlc(self):
        self.assertIn("seeing_stars: true", e2e.yaml_text(False, False, True))
        self.assertNotIn("seeing_stars", e2e.yaml_text(False, False, False))

    def test_the_container_excludes_ride_with_the_dlc_block(self):
        """The 14 locations the harness cannot earn, and only under --dlc.

        They are excluded because the harness sets a controller's solved
        flag and a drawer is an interaction, not an arrangement. droha
        played these by hand; the exclusion is the harness declaring its
        own blind spot, not a statement about the game.
        """
        dlc = e2e.yaml_text(False, False, True)
        self.assertEqual(14, dlc.count("\n    - "),
                         "the exclude list changed size")
        self.assertIn("Combs (Seeing Stars) - Drawer", dlc)
        self.assertNotIn("exclude_locations",
                         e2e.yaml_text(False, False, False))


class TestPlayCanReportItsOwnFailures(unittest.TestCase):
    """Every return from play() must carry the same number of values.

    The never-connected path returned five where the caller unpacks six,
    so the single path written to print "connected to the server: FAIL"
    raised ValueError instead. It became unreachable the moment `spent`
    was added to the successful return and nothing compared the two.

    Checked structurally rather than by calling play(), which needs a
    game, a server and a seed.
    """

    def arities(self, name):
        import ast
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "release_e2e.py")
        with open(path, encoding="utf-8") as fh:
            tree = ast.parse(fh.read())
        fn = next(n for n in ast.walk(tree)
                  if isinstance(n, ast.FunctionDef) and n.name == name)
        return [len(n.value.elts) for n in ast.walk(fn)
                if isinstance(n, ast.Return)
                and isinstance(n.value, ast.Tuple)]

    def test_every_return_from_play_is_the_same_width(self):
        widths = set(self.arities("play"))
        self.assertEqual(1, len(widths),
                         f"play() returns tuples of differing width {widths} - "
                         f"the narrow one crashes its caller")

    def test_play_returns_what_main_unpacks(self):
        """Read the caller's unpack width rather than hard-coding it.

        Pinning the number meant this failed the moment play() grew a
        seventh value legitimately, which teaches people to edit the
        expectation - and an expectation that gets edited on every change
        stops guarding anything. What matters is that the two AGREE.
        """
        import ast
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "release_e2e.py")
        with open(path, encoding="utf-8") as fh:
            tree = ast.parse(fh.read())

        unpacks = []
        for node in ast.walk(tree):
            if not isinstance(node, ast.Assign):
                continue
            call = node.value
            if (isinstance(call, ast.Call)
                    and isinstance(call.func, ast.Name)
                    and call.func.id == "play"):
                for target in node.targets:
                    if isinstance(target, (ast.Tuple, ast.List)):
                        unpacks.append(len(target.elts))

        self.assertTrue(unpacks, "nothing calls play() - the gate cannot run")
        self.assertEqual(set(self.arities("play")), set(unpacks),
                         f"play() returns {set(self.arities('play'))} values "
                         f"and its caller unpacks {set(unpacks)}")


class TestASurpriseSkipIsRefused(unittest.TestCase):
    """An unexpected Skip is a failure, and must not pay for itself.

    THE RUN THIS COMES FROM. 2026-09-21, the DLC gate: four Skips went to
    levels that are not on KNOWN_UNFORCEABLE, the supply of five ran out
    in round 15, and the run starved at 5 of 8. The report then listed
    SIX failures - every puzzle beaten, the mod agreeing, the credits,
    the goal, the server's goal, and the Skip surprise - of which five
    were consequences of the first. One defect, six red lines, and the
    only informative one was fifteen minutes up the log.

    Checked against play()'s source, because the alternative needs a
    game, a server and a seed.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_the_skip_is_refused_before_it_is_bought(self):
        """The refusal must come BEFORE dev("skip"), or it is not a
        refusal - it is a complaint about money already spent."""
        text = self.source()
        refuse = text.index("surprise = (not forced")
        buy = text.index('dev("skip", 1.5)')
        self.assertLess(refuse, buy,
                        "the Skip is spent before the surprise is noticed")

    def test_the_refusal_does_not_key_on_gated(self):
        """MEASURED, AND REVERTED. Refusing on `gated` took the base
        gate from 23/25 to 18/25, stalled at 1 of 8.

        `gated` is any("waiting on ") over the chunk, so it is true for
        a level that is only PARTLY gated - some controllers locked,
        others solvable, the level unable to complete either way.
        Refusing there makes the level unfinishable for the rest of the
        run rather than merely reporting a problem at the end. The
        verdict still fails on a gated spend; the place to prevent it
        is choose_slot, not the spend site.
        """
        text = code_only(self.source())
        self.assertIn("refuse = surprise", text)
        self.assertNotIn("refuse = gated or surprise", text)
        # The gated case is DEFERRED with an escape, not refused:
        # `idle >= STUCK_AFTER` spends it once nothing else moves,
        # which is what keeps a stranded ability from deadlocking.
        self.assertIn("defer = gated and not forced and idle < STUCK_AFTER",
                      text)
        self.assertIn("if not refuse and not defer:", text)

    def test_the_verdict_counts_gated_refusals(self):
        """Refusing must not make the check vacuous: a refused Skip
        never reaches `spent`, so reading only `spent` would go green
        the moment the refusal landed."""
        text = code_only(self.source())
        self.assertIn("refused_gated = [l for l, gated in surprises if gated]",
                      text)
        self.assertIn("not papered and not refused_gated", text)

    def test_a_forced_skip_is_never_a_surprise(self):
        """The stall path deliberately Skips a BEATEN slot to release an
        item stranded on it. That is the mechanism working, not a
        defect, and refusing it would strand the run for real."""
        self.assertIn("surprise = (not forced", self.source())

    def test_the_refusal_does_not_continue_the_round(self):
        """`continue` here skips the end-of-round `current = None` and
        spins the same slot forever - it cost 43 rounds once already."""
        text = self.source()
        block = text[text.index("surprise = (not forced"):]
        block = block[:block.index('if "skip: spent one" in more:')]
        # COMMENTS STRIPPED FIRST. The comment right there explains why
        # there is no `continue`, and a raw substring search matched its
        # own explanation - a test that fails when you document it is
        # measuring the prose, not the code.
        code = "\n".join(line for line in block.splitlines()
                         if not line.strip().startswith("#"))
        self.assertNotIn("continue", code)

    def test_the_verdict_counts_refusals_as_well_as_spends(self):
        """A refused Skip never reaches `spent`, so an assertion reading
        only `spent` would go GREEN the moment the refusal landed - the
        fix silently deleting the check that motivated it."""
        text = self.source()
        self.assertIn("not surprises and not bought", text)

    def test_play_hands_the_surprises_to_the_verdict(self):
        """A recorded surprise nobody returns is worth nothing."""
        text = self.source()
        self.assertIn("spent, surprises)", text)
        self.assertIn("surprises) = play(log, plan, arrow_text)", text)

    def test_each_surprise_is_announced_only_once(self):
        """A level that reaches this path every round for twenty rounds
        must not print twenty identical lines - that is how a report
        stops being read."""
        self.assertIn("if current not in surprised:", self.source())


class TestGatedIsScopedToTheLevelBeingSolved(unittest.TestCase):
    """The false positive that cost the base gate its 25th assertion.

    `gated` decided "was the mod still gating this level" by scanning
    the chunk for any line containing "waiting on ". That phrase comes
    from AbilityLocks' once-a-second summary of whatever level is
    ACTIVE, so a level loading behind the one being solved writes its
    own into the same window.

    Base gate, 2026-09-21. Stamps reported `locks: Stamps (Randomized)
    1 controller(s), 0 of 9 object(s) dimmed` and `abilities: 0 locked,
    1 open, 9 objects` - nothing gated. It was still recorded as
    "spent a Skip WHILE STILL ABILITY-GATED", because summaries reading
    18 and 24 objects landed in the same chunk. Stamps has nine. The
    Skip was right; the verdict was wrong.

    solve_level now publishes the loaded level's own locked indexes.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_gated_no_longer_scans_for_waiting_on(self):
        text = code_only(self.source())
        # Match the SHAPE, not one spelling of the loop variable - a
        # mutation using `l` instead of `line` slipped past the
        # narrower assertion.
        self.assertNotIn('any("waiting on "', text)

    def test_gated_reads_the_per_level_marker(self):
        text = code_only(self.source())
        self.assertIn("if LOCKED_MARK in line:", text)
        self.assertIn('gated = line.split(LOCKED_MARK, 1)[1].strip() != "none"',
                      text)

    def test_solve_level_publishes_the_marker_on_every_path(self):
        """Including the exhausted path - that is the one that spends."""
        text = code_only(self.source())
        self.assertIn('text += (f"\\n{LOCKED_MARK}"', text)

    def test_another_levels_summary_cannot_set_gated(self):
        """The exact shape of the 2026-09-21 chunk."""
        chunk = (
            "locks: Stamps (Randomized) 1 controller(s), 0 of 9 dimmed\n"
            + e2e.LOCKED_MARK + "none\n"
            "abilities: 2 locked, 5 open, 24 objects, waiting on Containers\n")
        gated = False
        for line in chunk.splitlines():
            if e2e.LOCKED_MARK in line:
                gated = line.split(e2e.LOCKED_MARK, 1)[1].strip() != "none"
        self.assertFalse(gated, "another level's summary set gated")

    def test_the_marker_still_reports_a_genuinely_locked_level(self):
        chunk = e2e.LOCKED_MARK + "0,2\n"
        gated = False
        for line in chunk.splitlines():
            if e2e.LOCKED_MARK in line:
                gated = line.split(e2e.LOCKED_MARK, 1)[1].strip() != "none"
        self.assertTrue(gated)


class TestASkipGoesWhereSomethingIsOutstanding(unittest.TestCase):
    """The bug that ended the DLC gate at 7 of 8 with Skips in hand.

    2026-09-21. `Containers` sat on `Filing Cabinet - Solution 2`, a
    location the harness can never earn - it force-solves a controller,
    which makes ONE arrangement, and a second solution is a different
    arrangement. Only a Skip grants it.

    At round 21 the run went looking for a slot to Skip and chose DLC1
    Daggers, which had nothing left to find; the mod answered "skip:
    refused, slot 7 has nothing left to find". The slot it needed was
    Filing Cabinet. The run ended 7 of 8 WITH THREE SKIPS UNSPENT,
    because the candidate list was "beaten and not yet skipped" and
    never asked whether a Skip there would grant anything.
    """

    def test_a_card_with_nothing_left_is_not_a_candidate(self):
        where = {0: ["A - S", "A - Beaten"]}
        self.assertFalse(e2e.has_uncollected(
            0, where, {"A - S", "A - Beaten"}))
        self.assertTrue(e2e.has_uncollected(0, where, {"A - S"}))

    def test_the_skip_goes_to_the_card_holding_the_stranded_item(self):
        """Two beaten slots, one exhausted and one still holding a
        location. The exhausted one must not absorb the Skip."""
        slots, plan, where = world(
            ["Done", "Holding", "Locked"],
            {"Done - S": [], "Done - Beaten": [],
             "Holding - S": [], "Holding - Beaten": [],
             "Holding - Solution 2": [],
             "Locked - S": ["Containers"], "Locked - Beaten": ["Containers"]})
        collected = {"Done - S", "Done - Beaten",
                     "Holding - S", "Holding - Beaten"}
        slot, why, buy = pick(slots, plan, where, beaten={0, 1},
                              collected=collected,
                              barren={1: frozenset()})
        self.assertTrue(buy, why)
        self.assertEqual(1, slot,
                         "the Skip went to a card with nothing left to find")

    def test_it_stops_asking_when_no_card_has_anything_left(self):
        """Otherwise the run spins asking for Skips the mod refuses."""
        slots, plan, where = world(
            ["Done", "Locked"],
            {"Done - S": [], "Done - Beaten": [],
             "Locked - S": ["Containers"], "Locked - Beaten": ["Containers"]})
        slot, why, buy = pick(slots, plan, where, beaten={0},
                              collected={"Done - S", "Done - Beaten"},
                              idle=e2e.STUCK_AFTER)
        self.assertFalse(buy, why)

    def test_both_skip_paths_require_outstanding_work(self):
        text = code_only(self.source())
        self.assertEqual(
            2, text.count("and has_uncollected(i, where, collected)]"),
            "the stall path and the no-candidates path must both check")

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()


class TestASkipIsExpectedWhereOnlyASkipWorks(unittest.TestCase):
    """The refusal's expectation, derived from the seed not a list.

    The surprise-Skip refusal asked `level_id not in KNOWN_UNFORCEABLE`.
    That list holds two BASE-GAME levels, so in a DLC run every
    legitimate Skip looked like a surprise. The gate of 2026-09-21
    refused one on DLC1 Filing Cabinet - the card holding `Containers`,
    the exact card the run needed - and finished 2 of 8, taking the DLC
    gate from 21/25 down to 18/25.

    Two kinds of location the harness can never force, both readable
    from the seed: an alternate solution (forcing makes ONE arrangement)
    and a container the yaml already excludes.
    """

    FILING = "Filing Cabinet (Cupboards and Drawers) - Solution "
    COMBS = "Combs (Seeing Stars) - Drawer"

    def test_an_outstanding_alternate_solution_expects_a_skip(self):
        where = {0: [self.FILING + "1", self.FILING + "2"]}
        self.assertTrue(e2e.only_a_skip_can_finish(
            0, where, {self.FILING + "1"}))

    def test_a_card_with_only_a_first_solution_does_not(self):
        where = {0: ["Daggers (Cupboards and Drawers) - Solution 1"]}
        self.assertFalse(e2e.only_a_skip_can_finish(0, where, set()))

    def test_an_excluded_container_expects_a_skip(self):
        self.assertIn(self.COMBS, e2e.CONTAINER_EXCLUDES)
        self.assertTrue(e2e.only_a_skip_can_finish(
            0, {0: [self.COMBS]}, set()))

    def test_a_finished_card_expects_nothing(self):
        where = {0: [self.FILING + "1", self.FILING + "2"]}
        self.assertFalse(e2e.only_a_skip_can_finish(0, where, set(where[0])))

    def test_solution_number_reads_the_ordinal(self):
        self.assertEqual(2, e2e.solution_number("X - Solution 2"))
        self.assertIsNone(e2e.solution_number("X - Beaten"))
        self.assertIsNone(e2e.solution_number("X - Solution zero"))

    def test_the_container_list_comes_from_the_yaml(self):
        """Same source as the gate generates from, so they cannot drift."""
        self.assertEqual(14, len(e2e.CONTAINER_EXCLUDES))

    def test_the_refusal_consults_it(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("and not only_a_skip_can_finish(", text)


class TestTheLastResortSkip(unittest.TestCase):
    """The one gated Skip the harness is allowed to spend.

    Base gate, 2026-09-21, round 5: one slot beaten, every other slot
    ability-gated, and the missing ability sitting on the beaten slot's
    own uncollected locations. A closed loop that only a Skip opens.
    Failing the run for that asks the harness to deadlock instead.

    So choose_slot names that case distinctly and the verdict reports
    it rather than failing - while a gated Skip the harness COULD have
    avoided still fails, which is the check worth keeping.
    """

    def test_it_says_last_resort_when_every_candidate_is_gated(self):
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": ["Jigsaw"], "A - Beaten": [],
             "B - S": ["Grids"], "B - Beaten": ["Grids"]})
        slot, why, buy = pick(slots, plan, where,
                              beaten={0}, collected={"A - Beaten"})
        self.assertEqual((0, True), (slot, buy), why)
        self.assertEqual(e2e.LAST_RESORT, why)

    def test_it_prefers_an_unlocked_candidate_and_does_not_say_last_resort(self):
        """With a choice, the gated slot is left alone."""
        slots, plan, where = world(
            ["A", "B", "C"],
            {"A - S": ["Jigsaw"], "A - Beaten": [],
             "B - S": ["Grids"], "B - Beaten": ["Grids"],
             "C - S": [], "C - Beaten": []})
        # C is PARKED - the harness proved it cannot collect what is
        # left there, which is the real shape of a stranded item: the
        # logic says reachable, the harness cannot perform it. Without
        # the park C is simply playable and the skip paths never run.
        slot, why, buy = pick(slots, plan, where, beaten={0, 2},
                              collected={"A - Beaten", "C - Beaten"},
                              barren={2: frozenset()})
        self.assertEqual((2, True), (slot, buy), why)
        self.assertNotEqual(e2e.LAST_RESORT, why)

    def test_the_verdict_excuses_only_the_last_resort(self):
        text = code_only(self.source())
        self.assertIn('if gated and reason != "last-resort"', text)
        self.assertIn('"last-resort" if current in last_resort', text)

    def test_the_last_resort_is_still_reported(self):
        """Excused is not silent. It is the one case where the harness
        buys past a gate, and the report has to say so."""
        self.assertIn("the only alternative was", code_only(self.source()))

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()


class TestTwoSkipRefusalsThatWereTriedAndReverted(unittest.TestCase):
    """Both looked right, both were measured, both made the run worse.

    Written as guards because the reasoning that produced them is
    persuasive and will be produced again. The base gate went

        23/25  surprise Skips refused (kept)
        18/25  ... and refused on `gated` too        -> reverted
        20/25  ... and the stall path filtered       -> reverted

    A Skip spent on a gated level is still reported and still fails the
    verdict. Trading a REPORTED problem for a run that cannot finish is
    not an improvement, and that is what both attempts did.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_the_spend_site_does_not_refuse_on_gated(self):
        """`gated` is any("waiting on ") over the chunk, so it is true
        for a PARTLY gated level - some controllers locked, others
        solvable. By the time the spend site sees it the level is open,
        and refusing leaves it unfinishable for the rest of the run."""
        text = code_only(self.source())
        self.assertIn("refuse = surprise", text)
        self.assertNotIn("refuse = gated or surprise", text)

    def test_the_stall_path_is_not_filtered_on_gating(self):
        """The stall Skip exists FOR beaten slots with uncollected
        locations, and those are uncollected precisely because they are
        ability-gated. Filter them out and there is almost never a
        candidate, so the run never finishes - it lost the credits, the
        goal and the server's goal, all of which had been passing."""
        text = code_only(self.source())
        self.assertIn("stalled = [i for i in sorted(beaten)", text)
        # The GATING filter must stay gone. Filtering on outstanding
        # work is a different thing and is required - a Skip on a card
        # with nothing left grants nothing.
        self.assertNotIn("and not waiting_on_an_ability(i, plan", text)
        self.assertIn("and has_uncollected(i, where, collected)]", text)
        # A PREFERENCE IS FINE, A FILTER IS NOT. The fallback is the
        # whole difference: `unlocked or stalled` can never empty the
        # candidate list, where the reverted filter did.
        self.assertIn("stalled = unlocked or stalled", text)

    def test_the_stall_path_still_offers_a_beaten_unskipped_slot(self):
        """The behaviour both attempts broke, asserted on the FUNCTION
        rather than on its source - a source check cannot tell you the
        run stalls, which is exactly how both regressions passed a
        green suite."""
        slots, plan, where = world(
            ["A", "B", "C"],
            {"A - S": ["Jigsaw"], "B - S": [], "C - S": ["Grids"]})
        slot, why, buy = pick(slots, plan, where,
                              beaten={0, 1}, idle=3, held=set())
        self.assertTrue(buy, f"the stall path offered nothing: {why}")
        self.assertIn(slot, (0, 1))


class TestASlotBeatenEarlierIsNotAskedAgain(unittest.TestCase):
    """The bug that cost the 2026-09-21 DLC gate two puzzles.

    The gate runs two launches, and the first - the arrow check - calls
    solve_level and really beats a level. The mod then does NOT re-file
    that level's Beaten location on the next connect, because its ledger
    already has it. That is correct.

    play() marked a slot beaten only on a FRESH `beaten:` line, with an
    escape hatch for slots the mod carried in. The escape hatch read

        restored = set(range(n))

    from the mod's "N puzzle(s) beaten" COUNT - i.e. "the first n slot
    indices". The arrow session had beaten slot 2 and n was 1, so the
    harness marked slot 0 and spent rounds 2, 6, 10 and 19 demanding a
    token slot 2 could never send. It parked the slot twice and bought a
    Skip for it, and the run reported 5 of 8 having really done 6.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_restored_is_not_guessed_from_a_count(self):
        """`set(range(n))` is "the first n slots", which is a different
        claim from "n slots" and is wrong unless slot 0 is one of them."""
        self.assertNotIn("restored = set(range(n))",
                         code_only(self.source()))

    def test_restored_is_read_from_the_earlier_transcript(self):
        text = self.source()
        self.assertIn("for i in beaten_slots(earlier, where):", text)
        self.assertIn("restored.add(i)", text)

    def test_beaten_slots_reads_tokens_rather_than_guessing(self):
        """The helper both cases now go through."""
        where = {0: ["Stamps (Randomized) - Beaten", "Stamps (Randomized) - A"],
                 2: ["Clock Cupboard (Cupboards and Drawers) - Beaten"]}
        text = "beaten: Clock Cupboard (Cupboards and Drawers) - Beaten\n"
        self.assertEqual({2}, set(e2e.beaten_slots(text, where)))
        self.assertEqual(set(), set(e2e.beaten_slots("", where)))
        # ONLY the Beaten location counts. Any other location of the
        # slot appearing after "beaten: " must not mark the slot done -
        # the Beaten token is the mod's statement that the PUZZLE is
        # finished, and a part check is not that.
        self.assertEqual(set(), set(e2e.beaten_slots(
            "beaten: Stamps (Randomized) - A\n", where)))

    def test_a_token_for_another_slot_is_not_misattributed(self):
        """Longest-first display matching means one level's name can be a
        prefix of another's; the token must land on its own slot."""
        where = {0: ["Books (Seeing Stars) - Beaten"],
                 1: ["Books Stacked (Seeing Stars) - Beaten"]}
        got = set(e2e.beaten_slots(
            "beaten: Books Stacked (Seeing Stars) - Beaten\n", where))
        self.assertEqual({1}, got)

    def test_every_round_reconciles_against_the_tokens(self):
        """A token can land while a DIFFERENT slot is current - a Skip
        payout, or a revisit that finishes a level nobody asked about.

        The base gate on 2026-09-21 had the mod banking 8 and the
        harness counting 7 for exactly this reason, so "all 8 puzzles
        beaten" failed in the same report where "the mod agrees every
        puzzle was beaten" passed.
        """
        text = code_only(self.source())
        self.assertIn("for slot_index, token in beaten_slots(transcript, "
                      "where).items():", text)
        self.assertIn("if slot_index not in beaten:", text)

    def test_play_is_given_the_earlier_transcript(self):
        """A parameter nothing passes is worth nothing - this is the
        third time a fix in this file was correct and unwired."""
        text = self.source()
        self.assertIn('def play(log, plan, earlier=""):', text)
        self.assertIn("play(log, plan, arrow_text)", text)

    def test_the_count_disagreeing_is_reported(self):
        """The mod's count is the only independent check on this. If it
        says 1 and the transcript names 0, the run is about to waste
        rounds on a slot it cannot satisfy and must say so."""
        text = code_only(self.source())
        # THE CONDITION, not the message. Disabling the check leaves the
        # warning string sitting there unreachable, and an assertion on
        # the text alone passes while nothing can ever print it - which
        # is exactly what the mutation run showed.
        self.assertIn("if n != len(restored):", text)
        self.assertIn("WARNING: the mod carried", text)

    def test_the_downgrade_still_consults_restored(self):
        """The mod's word stays final for every slot NOT carried in."""
        self.assertIn('if done and "beaten:" not in (chunk + tail) '
                      'and current not in restored:', self.source())


class TestTheGateLaunchesTheGameTwice(unittest.TestCase):
    """The one gate assertion that is about the HARNESS, not the game.

    "two game launches, no more" counts
    "A Little To The Left Archipelago loaded" in the whole transcript and
    expects exactly two: the arrow session and the main run. An extra
    launch means the game restarted itself - Steam relaunching it, or a
    crash - and that reads as a mod fault when it is a rig fault.

    It is the last of the twenty-five that had never been checked
    outside a full run, and it needs no game at all: there is exactly
    ONE place that starts the exe, so the claim reduces to how many
    times that place is called.
    """

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_there_is_exactly_one_place_that_starts_the_game(self):
        """Another Popen would make the count unverifiable from here,
        and would also be a second thing to keep in step with
        ensure_no_steam_relaunch."""
        text = code_only(self.source())
        self.assertEqual(1, text.count("subprocess.Popen([EXE]"))

    def test_the_launcher_is_called_exactly_twice(self):
        """Once by check_arrow, once by play. --quick skips the arrow
        session and its assertion expects one, which is why the count
        lives in the assertion rather than being hard-coded here."""
        import ast
        tree = ast.parse(self.source())
        calls = [n for n in ast.walk(tree)
                 if isinstance(n, ast.Call)
                 and isinstance(n.func, ast.Name)
                 and n.func.id == "launch_and_connect"]
        self.assertEqual(2, len(calls),
                         f"{len(calls)} call(s) to launch_and_connect - the "
                         f"gate expects the game to start exactly twice")

    def test_every_launch_guards_against_steam_restarting_it(self):
        """A Steam relaunch is the thing that makes the count wrong, and
        it is silent: the game reopens and the harness sees a third
        'loaded' line it did not ask for."""
        text = code_only(self.source())
        launcher = text[text.index("def launch_and_connect"):]
        launcher = launcher[:launcher.index("subprocess.Popen([EXE]")]
        self.assertIn("ensure_no_steam_relaunch()", launcher)


class TestTheDocsDoNotLie(unittest.TestCase):
    """check-docs.py cannot catch a comment that is merely wrong."""

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_nothing_shortens_the_run(self):
        """PUZZLES is assigned once, at module level, and never again.

        --quick was documented as a three-puzzle run in three places for
        as long as puzzle_count has had a floor of 8. This asserts the
        fact the prose got wrong rather than banning the wrong words:
        a substring ban trips on its own correction note, and a test
        that cannot survive being explained is not measuring anything.
        """
        import ast
        tree = ast.parse(self.source())
        at_module = [n for n in tree.body if isinstance(n, ast.Assign)
                     for t in n.targets
                     if isinstance(t, ast.Name) and t.id == "PUZZLES"]
        self.assertEqual(1, len(at_module), "PUZZLES is set more than once")

        inside = [fn.name for fn in ast.walk(tree)
                  if isinstance(fn, ast.FunctionDef)
                  for n in ast.walk(fn) if isinstance(n, ast.Assign)
                  for t in n.targets
                  if isinstance(t, ast.Name) and t.id == "PUZZLES"]
        self.assertEqual([], inside,
                         f"{inside} reassigns PUZZLES - a mode that really "
                         f"did shorten the run would change what the "
                         f"end-of-run assertions mean")
        self.assertEqual(8, e2e.PUZZLES)

    def test_the_quick_flag_admits_it_is_still_a_full_length_run(self):
        """The help is what someone reads before waiting fifteen minutes."""
        self.assertIn("Still eight puzzles", self.source())


if __name__ == "__main__":
    unittest.main(verbosity=2)
