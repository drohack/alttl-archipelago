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
import json
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


def part_locations():
    """Every part location name the current tables mint.

    A single-part level mints none (data.Level.has_parts): its part check
    would be the same event as its Solution check.
    """
    with open(os.path.join(e2e.REPO, "apworld", "alttl", "data",
                           "names.json"), encoding="utf-8") as fh:
        levels = json.load(fh)["levels"]
    return {f"{lv['display']} - {part['display']}"
            for lv in levels.values() if len(lv["parts"]) > 1
            for part in lv["parts"].values()}


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

    def test_it_never_starts_on_a_level_forcing_cannot_finish(self):
        """Seed 20260907 put Desktop Computer first: one part needs nothing,
        so it "had work", but forcing never completes it and the arrow check
        must complete its level before it can press the arrow."""
        slots, plan, where = world(
            ["Desktop Computer", "TupperwareTower", "Open"],
            {"Desktop Computer - Computer Desktop": [],
             "Desktop Computer - Beaten": ["Gadgets"],
             "TupperwareTower - Solution 1": [], "TupperwareTower - Beaten": [],
             "Open - S": [], "Open - Beaten": []})
        self.assertEqual(2, e2e.arrow_slot(slots, plan, where))

    def test_the_arrow_session_can_be_run_on_its_own(self):
        """droha: "test each part of the e2e by itself". --only-arrow runs
        steps 1-5 - the real install, seed and arrow session - and stops with
        the arrow's two verdicts."""
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn('parser.add_argument("--only-arrow"', text)
        block = text[text.index("got, expected, arrow_text = check_arrow(log, plan)"):]
        block = block[:block.index('say(5, "launching a clean game and playing the run")')]
        self.assertIn("if ONLY_ARROW:", block)
        self.assertIn("return 0 if all(ok for _, ok in arrow_results) else 1", block)

    def test_it_counts_the_seeds_starting_abilities(self):
        """Precollected abilities arrive on connect in the arrow session too."""
        slots, plan, where = world(
            ["Stack", "Open"],
            {"Stack - S": ["Stacking"], "Stack - Beaten": ["Stacking"],
             "Open - S": [], "Open - Beaten": []})
        plan["starting_abilities"] = ["Stacking"]
        self.assertEqual(0, e2e.arrow_slot(slots, plan, where))

    def test_it_only_starts_on_a_slot_the_packs_have_opened(self):
        """A player cannot open a locked slot, and what the arrow does from
        one has never been measured."""
        slots, plan, where = world(
            ["Gated", "Open"],
            {"Gated - S": ["Drawer"], "Gated - Beaten": ["Drawer"],
             "Open - S": [], "Open - Beaten": []})
        plan["boundaries"] = [1, 2]
        self.assertEqual(0, e2e.arrow_slot(slots, plan, where),
                         "only slot 0 is open: fall back to it, not to slot 1")

    def test_the_plan_carries_the_starting_abilities(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = fh.read()
        self.assertIn("'starting_abilities': list(d.get('starting_abilities', []))",
                      text)

    def test_it_skips_a_level_whose_token_needs_an_ability(self):
        """A free part is work, but the level only completes with Drawer."""
        slots, plan, where = world(
            ["Gated", "Open"],
            {"Gated - P": [], "Gated - Beaten": ["Drawer"],
             "Open - S": [], "Open - Beaten": []})
        self.assertEqual(1, e2e.arrow_slot(slots, plan, where))

    def test_it_falls_back_rather_than_skipping_the_check(self):
        """Nothing solvable: run it on slot 0 and let it fail honestly."""
        slots, plan, where = world(
            ["A", "B"],
            {"A - S": ["Drawer"], "A - Beaten": ["Drawer"],
             "B - S": ["Drawer"], "B - Beaten": ["Drawer"]})
        self.assertEqual(0, e2e.arrow_slot(slots, plan, where))


class TestAProgressLineSaysHowFarAlong(unittest.TestCase):
    """droha, 2026-09-24: "why are there so many rounds, and slots and a
    beginning number, it's really hard to tell how far along the test is".

    `[6/7] round 4: slot 3 TupperwareTower beaten (4/15, 5 open)` had four
    numbers and only the last was progress. Then: "shouldn't we know
    exactly how many of each test/level/round/slots we are doing? This is a
    set seed". So every counter now carries its total - visits against the
    seed's paper plan, puzzles, steps by name, checks.
    """

    def said(self, phase, msg):
        import contextlib
        import io
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            e2e.say(phase, msg)
        return out.getvalue().strip()

    def tearDown(self):
        e2e.set_progress(None)

    def test_a_setup_step_names_itself(self):
        self.assertEqual("[step 2/7 assets] installing",
                         self.said(2, "installing"))

    def test_during_play_the_line_carries_visit_and_puzzle_totals(self):
        beaten = {0: "A", 3: "B"}
        visit = [4]
        e2e.set_progress(lambda: (len(beaten), 15, visit[0], 17))
        self.assertEqual(
            "[visit 4/17 | 2/15 beaten] waiting for the check (9s left)",
            self.said(6, "waiting for the check (9s left)"))
        beaten[5] = "C"                      # the counts follow the run
        visit[0] = 5
        self.assertTrue(self.said(6, "x").startswith("[visit 5/17 | 3/15 beaten]"))

    def test_before_the_first_visit_it_says_so(self):
        e2e.set_progress(lambda: (0, 15, 0, 17))
        self.assertTrue(self.said(6, "x").startswith("[visit -/17 | 0/15 beaten]"))

    def test_other_phases_ignore_the_play_count(self):
        e2e.set_progress(lambda: (4, 15, 4, 17))
        self.assertEqual("[step 7/7 save] checking", self.said(7, "checking"))

    def test_the_round_line_names_the_level_first(self):
        self.assertEqual("TupperwareTower beaten (slot 3, 5 of 15 open)",
                         e2e.round_line(3, "TupperwareTower", "beaten", 5, 15))

    def test_the_checks_are_numbered(self):
        self.assertEqual(["[check 1/2] PASS  a", "[check 2/2] FAIL  b"],
                         e2e.check_lines([("a", True), ("b", False)]))

    def test_play_publishes_its_counts(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("set_progress(lambda: (len(beaten), len(slots), "
                      "visit[0], len(planned)))", text)


class TestTheRunStopsWhenTheCreditsOpen(unittest.TestCase):
    """The 2026-09-24 full gate beat 15 of 15 at visit 25, then began a 26th
    - a pointless revisit - because the round loop tested a `credits` flag
    refreshed only inside an attempt, and `credits: unlocked` landed nine log
    lines after the last token. The paper plan ends where the credits open;
    so must the run."""

    def loop_top(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        top = text[text.index("    for step in range(1, MAX_ROUNDS + 1):"):]
        return top[:top.index("            break")]

    def test_the_log_is_read_before_the_stop_is_decided(self):
        top = self.loop_top()
        self.assertIn("transcript += log.new()", top)
        self.assertIn('if "credits: unlocked" in transcript:', top)

    def test_all_beaten_waits_for_the_credits(self):
        self.assertIn('log.wait(["credits: unlocked"]', self.loop_top())


class TestALevelThatCompletesFirstLeavesAPartBehind(unittest.TestCase):
    """The 2026-09-24 full gate, visit 21: Workbench's ToolsController and
    DraggablesForTargets share the same 21 objects, forcing the first
    completes the level, and the second's check never fires - so the real
    harness came back to Workbench where the paper plan did not.

    The list is empty since droha's play made that part notALocation, so
    these tests patch the old entry back in and the mechanism stays tested
    for the next part like it."""

    PART = "Workbench - Draggables For Targets"

    def setUp(self):
        self.real = e2e.KNOWN_EARLY_COMPLETE
        e2e.KNOWN_EARLY_COMPLETE = frozenset({self.PART})

    def tearDown(self):
        e2e.KNOWN_EARLY_COMPLETE = self.real

    def test_every_entry_is_still_a_location(self):
        """A stale entry reads as a known gap for a check that is gone."""
        stale = sorted(self.real - part_locations())
        self.assertFalse(stale, f"not locations any more: {stale}")

    def test_forcing_never_collects_it(self):
        reqs = {"Workbench - Tools": [], "Workbench - Draggables For Targets": [],
                "Workbench - Solution 1": [], "Workbench - Beaten": []}
        slots, plan, where = world(["Workbench"], reqs)
        self.assertIn("Workbench - Draggables For Targets",
                      e2e.harness_cannot_force(plan, where))

    def test_only_a_skip_can_finish_it(self):
        where = {0: ["Workbench - Tools", "Workbench - Draggables For Targets"]}
        self.assertTrue(e2e.only_a_skip_can_finish(
            0, where, {"Workbench - Tools"}))

    def test_the_plan_does_not_count_it_collected_by_forcing(self):
        reqs = {"Workbench - Tools": [], "Workbench - Draggables For Targets": [],
                "Workbench - Solution 1": [], "Workbench - Beaten": []}
        slots, plan, where = world(["Workbench"], reqs)
        final = {}
        _b, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {}, production=True, rounds=1, final=final,
            unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(3, visits[0]["got"])
        self.assertNotIn("Workbench - Draggables For Targets", final["collected"])


class TestTheTableAuditReadsTheModsCurrentWording(unittest.TestCase):
    """The 2026-09-24 full gate failed `the controller table matches every
    level played` on TupperwareNesting, which IS on KNOWN_TABLE_GAPS: the
    mod now writes `(at N controller(s) so far)` after the level's name."""

    PHASED = ("[Warning:A Little To The Left Archipelago] UNEARNABLE LOCATIONS on "
              "TupperwareNesting (at 6 controller(s) so far): the table expects "
              "Food, Layout (Grid), which this level has not registered - if the "
              "level is phased they should appear as you play")
    NEW = ("[Warning:A Little To The Left Archipelago] UNEARNABLE LOCATIONS on "
           "Workbench (at 2 controller(s) so far): the table expects Nothing Real")

    def test_a_known_phased_level_is_data(self):
        self.assertEqual([], e2e.table_audit(self.PHASED))

    def test_any_other_level_is_still_a_gap(self):
        self.assertEqual(1, len(e2e.table_audit(self.NEW)))


class TestAPhaseThatAppearsIsNotAWastedPass(unittest.TestCase):
    """The 2026-09-24 full gate, visit 19: TupperwareNesting registers one
    controller per phase (2, 3, 4, 5, 6 ... 8), so every pass revealed the
    next one - and the 5-pass budget ran out on Large Square. Forced with no
    budget the level completes (Layout (Grid), Food, Nested Tupperware)."""

    FIVE = ("[Info   :ALTTL Dev Tools] controllers: 5 registered on "
            "TupperwareNesting levelInstance=-125134 solvedNow=1\n"
            "[Info   :ALTTL Dev Tools]   [0] Lids type=TupperwareLids solved=True\n"
            "[Info   :ALTTL Dev Tools]   [1] Stack 1 type=StackablesZ solved=True\n"
            "[Info   :ALTTL Dev Tools]   [2] Stack 2 type=StackablesZ solved=True\n"
            "[Info   :ALTTL Dev Tools]   [3] Tray type=Draggables solved=True\n"
            "[Info   :ALTTL Dev Tools]   [4] Stack 3 type=StackablesZ solved=False\n")
    SIX = FIVE.replace("5 registered", "6 registered").replace(
        "Stack 3 type=StackablesZ solved=False", "Stack 3 type=StackablesZ solved=True") + (
            "[Info   :ALTTL Dev Tools]   [5] Draggables (Large Square) "
            "type=Draggables solved=False\n")

    def test_a_new_controller_is_progress(self):
        self.assertTrue(e2e.pass_progress(self.FIVE, self.SIX))

    def test_a_new_controller_alone_is_progress(self):
        """Registered grows, solved does not: the next phase appeared."""
        grown = self.FIVE.replace("5 registered", "6 registered") + (
            "[Info   :ALTTL Dev Tools]   [5] Draggables (Large Square) "
            "type=Draggables solved=False\n")
        self.assertEqual((6, 4), e2e.listing_counts(grown))
        self.assertTrue(e2e.pass_progress(self.FIVE, grown))

    def test_one_more_solved_is_progress(self):
        more = self.FIVE.replace("Stack 3 type=StackablesZ solved=False",
                                 "Stack 3 type=StackablesZ solved=True")
        self.assertTrue(e2e.pass_progress(self.FIVE, more))

    def test_the_same_listing_is_not(self):
        self.assertFalse(e2e.pass_progress(self.FIVE, self.FIVE))

    def test_solve_level_gives_such_a_pass_back(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("if attempt and pass_progress(last_listing, out)", text)


class TestWithLocksOffEveryAbilityCounts(unittest.TestCase):
    """--quick seeds turn ability locks off, and the mod then files every
    check whatever is held (SlotProgress.IsReachable). The paper plan treats
    every ability as held; play() must choose the same way, or the first
    visit it waits for an ability the paper did not is a false stop."""

    RECEIVED = "[Info   :A Little To The Left Archipelago] received item: Stacking\n"

    def test_locks_off_holds_everything(self):
        e2e._load_ability_tables()
        self.assertEqual(set(e2e._ABILITY_NAMES),
                         e2e.run_holds(self.RECEIVED, {"ability_locks": False}))

    def test_locks_on_holds_what_arrived(self):
        self.assertEqual({"Stacking"},
                         e2e.run_holds(self.RECEIVED, {"ability_locks": True}))

    def test_play_uses_it(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("held = run_holds(transcript, plan)", text)
        self.assertNotIn("held = abilities_held(transcript)", text)


class TestTheRunFollowsItsPaperPlan(unittest.TestCase):
    """droha: "if it does run and finds something it doesn't expect it
    should fail loudly". A visit that is not the planned one stops the run."""

    PLAN = [{"level": "Stamps"}, {"level": "Batteries"}]

    def test_the_planned_visit_is_fine(self):
        self.assertIsNone(e2e.off_plan(1, "Stamps", self.PLAN))

    def test_a_different_level_is_named(self):
        why = e2e.off_plan(2, "Pencils", self.PLAN)
        self.assertIn("Batteries", why)
        self.assertIn("Pencils", why)

    def test_a_visit_past_the_plan_is_named(self):
        self.assertIn("past the plan", e2e.off_plan(3, "Stamps", self.PLAN))

    def test_play_stops_on_it(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("astray = off_plan(visit[0], level_id, planned)", text)
        stop = text[text.index("            if planned and astray:"):]
        stop = stop[:stop.index("break") + len("break")]
        self.assertIn("OFF_PLAN_MARK", stop)
        self.assertIn('("the run followed its paper plan",', text)


class TestTheGateOnlyPlaysASeedItCanClear(unittest.TestCase):
    """droha: "We do occasionally change the ids and levels so seeds might
    be changed? The e2e should double check that before it runs". The seed
    number is fixed; what it generates is not."""

    def test_it_takes_the_first_seed_that_clears(self):
        seen = []
        verdicts = {10: (False, ["seed 10: 13/15"]), 11: (False, ["seed 11: 14/15"]),
                    12: (True, ["seed 12: 15/15"])}
        number = e2e.first_clearable(range(10, 20), verdicts.get, seen.append)
        self.assertEqual(12, number)
        self.assertEqual(["seed 10: 13/15", "seed 11: 14/15", "seed 12: 15/15"], seen)

    def test_none_clearing_is_none(self):
        self.assertIsNone(e2e.first_clearable(
            range(3), lambda n: (False, [f"seed {n}"]), lambda line: None))

    def pencils_seed(self, starting):
        """The 2026-09-24 seed: Symmetry on Pencils Solution 2."""
        reqs = {"Pencils (Randomized) - Solution 1": [],
                "Pencils (Randomized) - Solution 2": [],
                "Pencils (Randomized) - Beaten": [],
                "Shells - S": ["Symmetry"], "Shells - Beaten": ["Symmetry"]}
        slots, plan, where = world(["Pencils (Randomized)", "Shells"], reqs)
        placed = {"Pencils (Randomized) - Solution 2": "Symmetry"}
        final = {}
        e2e.paper_run(slots, plan, where, placed, starting=starting,
                      production=True, final=final,
                      unreachable=e2e.harness_cannot_force(plan, where))
        return e2e.why_unclearable(plan, where, placed, final)

    def test_it_says_why_a_seed_cannot_be_cleared(self):
        """Without a Skip, Pencils Solution 2 cannot be released."""
        why = self.pencils_seed([])
        self.assertEqual(1, len(why), why)
        self.assertIn("Shells needs Symmetry", why[0])
        self.assertIn("Pencils (Randomized) - Solution 2", why[0])
        self.assertIn("only a Skip releases", why[0])

    def test_a_skip_clears_that_seed_now(self):
        """With the Skip it held: since 2026-09-25 the mod releases Pencils
        itself, where the game's own skip does nothing."""
        self.assertEqual([], self.pencils_seed(["Skip"]))


class TestTheHarnessForcesOnlyWhatTheLogicHasReached(unittest.TestCase):
    """solve_level refuses a group the seed's logic has not reached yet,
    as well as one the mod greys.

    The third base gate of 2026-09-24 stopped at visit 20 of 24: Paper Plane
    Supplies' Chalk DraggablesOrdered (Drawer + Jigsaw + Ordering) was forced
    at visit 15 without Ordering, because its seven objects have no renderer
    of their own and are shared with the open drawer (dimmed=0), so the level
    was beaten five visits before the paper plan, which follows the table.
    An understated group (greyed although the table says free) still stops
    the gate; an overstated one no longer runs ahead of the plan.
    """

    PLAN = {"requirements": {
        "Paper Plane Supplies (Drawer Chores) - Chalk":
            ["Drawer", "Jigsaw", "Ordering"],
        "Paper Plane Supplies (Drawer Chores) - Chalk Blue":
            ["Drawer", "Jigsaw"],
        "Fruit Stickers - Solution 1": ["Sticking", "Tidying"]}}

    def test_a_group_whose_ability_is_missing_is_refused(self):
        refused = e2e.table_gated("NeatStreak_Paper Plane Supplies",
                                  self.PLAN, {"Drawer", "Jigsaw"})
        self.assertIn("Chalk DraggablesOrdered", refused)
        self.assertNotIn("ChalkBlue Jigsaw", refused)

    def test_nothing_is_refused_once_it_is_held(self):
        self.assertEqual(set(), e2e.table_gated(
            "NeatStreak_Paper Plane Supplies", self.PLAN,
            {"Drawer", "Jigsaw", "Ordering"}))

    def test_a_single_group_level_goes_by_its_solution(self):
        refused = e2e.table_gated("Fruit Stickers", self.PLAN, {"Tidying"})
        self.assertEqual({"Match Stickers", "Remove Stickers"}, refused)

    def test_play_passes_it_to_solve_level(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("refuse = table_gated(slots[current][1], plan, held)\n"
                      "            done, chunk = solve_level(\n"
                      "                log, refuse=refuse,",
                      text)


class TestThePlanAgreesWithEveryMeasuredLevel(unittest.TestCase):
    """The harness's per-level lists against fixtures/forceability.jsonl.

    droha, 2026-09-24: "shouldn't we just run every single level now and
    build a plan around them? that way we don't have to 'figure them out'
    on a gate run?" So every level was probed alone (tools/probe-forceable.py
    --all) and the results are a fixture. A list that disagrees with a
    measurement fails here, in milliseconds, instead of stopping a gate.
    """

    FIXTURE = os.path.join(e2e.REPO, "fixtures", "forceability.jsonl")

    def rows(self):
        if not os.path.isfile(self.FIXTURE):
            self.skipTest("no fixtures/forceability.jsonl - run "
                          "tools/probe-forceable.py --all")
        with open(self.FIXTURE, encoding="utf-8") as fh:
            return [json.loads(line) for line in fh if line.strip()]

    def test_every_level_forcing_cannot_finish_is_known(self):
        for row in self.rows():
            if row["all"] is None:
                self.assertTrue(row["levelId"] in e2e.UNFORCEABLE
                                or row["levelId"] in e2e.KNOWN_NOTHING_TO_FORCE,
                                f"{row['levelId']}: forcing never completes it")
            else:
                self.assertNotIn(row["levelId"], e2e.KNOWN_NOTHING_TO_FORCE)

    def test_no_measured_completion_outlasts_the_wait(self):
        """A multi-pass figure counts every pass, so it is not a delay."""
        for row in self.rows():
            if row["all"] is not None and (row.get("passes") or 1) <= 2:
                self.assertLessEqual(row["all"],
                                     e2e.completion_wait(row["levelId"]),
                                     f"{row['levelId']} took {row['all']}s")

    def test_every_early_finish_is_known(self):
        for row in self.rows():
            early = {g for g, s in row["groups"].items() if s is not None}
            known = set(e2e.KNOWN_COMPLETES_ON.get(row["levelId"], ()))
            self.assertEqual(early, known, row["levelId"])

    def test_every_level_short_at_load_is_a_known_gap(self):
        for row in self.rows():
            if row["atLoad"] < row["table"]:
                self.assertIn(row["levelId"], e2e.KNOWN_TABLE_GAPS,
                              f"{row['levelId']}: {row['atLoad']} controller(s)"
                              f" at load, the table lists {row['table']}")


class TestALevelThatFinishesOnOneGroup(unittest.TestCase):
    """DLC2 Cupcakes completes on its Colors group alone.

    The DLC gate of 2026-09-24, visit 14: Colors was forced, LevelComplete
    arrived (solutionId Draggables-Colors_0), the mod withheld Solution 1
    (it needs Swapping) and banked the Beaten token - while the paper plan,
    whose Beaten asks for Swapping, kept Cupcakes unbeaten until visit 27.
    droha's play said the same on 2026-09-23: "completed on the colours".
    """

    REQS = {"DLC2 Cupcakes - Colors": [],
            "DLC2 Cupcakes - Candles": ["Swapping"],
            "DLC2 Cupcakes - Solution 1": ["Swapping"],
            "DLC2 Cupcakes - Beaten": ["Swapping"],
            "Spice Jars - Solution 1": [], "Spice Jars - Beaten": []}

    def test_forcing_colors_beats_it_and_the_solution_waits(self):
        slots, plan, where = world(["DLC2 Cupcakes", "Spice Jars"], self.REQS)
        final = {}
        _beaten, _att, _spent, visits = e2e.paper_run(
            slots, plan, where, {}, production=True, final=final,
            unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(("DLC2 Cupcakes", True),
                         (visits[0]["level"], visits[0]["beaten"]))
        self.assertIn("DLC2 Cupcakes - Beaten", final["collected"])
        self.assertNotIn("DLC2 Cupcakes - Solution 1", final["collected"])

    def test_the_groups_after_the_first_finisher_are_stranded(self):
        """Spoons finishes on either group. Forcing goes in controller order,
        so the first ends the level and the second is never solved: no
        revisit files it, and only a Skip releases it."""
        reqs = {"Spoons - Stacked": [], "Spoons - Size (Elastic)": [],
                "Spoons - Solution 1": [], "Spoons - Beaten": [],
                "Pasta - Solution 1": [], "Pasta - Beaten": []}
        slots, plan, where = world(["Spoons", "Pasta"], reqs)
        saved = (dict(e2e.KNOWN_COMPLETES_ON), dict(e2e.CONTROLLER_ORDER),
                 dict(e2e.FORCEABILITY))
        try:
            e2e.KNOWN_COMPLETES_ON["Spoons"] = frozenset(
                {"Stacked", "Size (Elastic)"})
            e2e.CONTROLLER_ORDER["Spoons"] = ["Stacked", "Size (Elastic)"]
            e2e.FORCEABILITY["Spoons"] = {
                "groups": {"Stacked": 1, "Size (Elastic)": 1}}
            final = {}
            e2e.paper_run(slots, plan, where, {}, production=True,
                          final=final, rounds=6,
                          unreachable=e2e.harness_cannot_force(plan, where))
        finally:
            e2e.KNOWN_COMPLETES_ON.clear()
            e2e.KNOWN_COMPLETES_ON.update(saved[0])
            e2e.CONTROLLER_ORDER.clear()
            e2e.CONTROLLER_ORDER.update(saved[1])
            e2e.FORCEABILITY.clear()
            e2e.FORCEABILITY.update(saved[2])
        self.assertIn("Spoons - Beaten", final["collected"])
        self.assertIn("Spoons - Stacked", final["collected"])
        self.assertNotIn("Spoons - Size (Elastic)", final["collected"])

    def test_a_stage_that_never_registers_is_not_forced(self):
        """DLC1 Boss completes on its two load-time controllers before
        Dining Room, Parking Lot and Landscape register (DLC gate, 2026-09-24:
        the live run came back to it at visit 15 for exactly those three).
        Forcing can never collect them; only a Skip can."""
        reqs = {"DLC1 Boss - Keys": ["Drawer"], "DLC1 Boss - Drawer": [],
                "DLC1 Boss - Dining Room": ["Drawer"],
                "DLC1 Boss - Solution 1": ["Drawer"],
                "DLC1 Boss - Beaten": ["Drawer"]}
        _slots, plan, where = world(["DLC1 Boss"], reqs)
        cannot = e2e.harness_cannot_force(plan, where)
        if "DLC1 Boss" not in e2e.FORCEABILITY:
            self.skipTest("no fixtures/forceability.jsonl")
        self.assertIn("DLC1 Boss - Dining Room", cannot)
        self.assertNotIn("DLC1 Boss - Keys", cannot)
        self.assertNotIn("DLC1 Boss - Drawer", cannot)

    def test_a_staged_level_forced_pass_by_pass_is_not_affected(self):
        """TupperwareNesting's phases register as each is solved (six
        passes in the recheck), so forcing reaches them all."""
        self.assertNotIn("TupperwareNesting",
                         {lid for lid, _g in e2e.never_registered_parts()})

    def test_the_arrow_check_never_starts_on_an_early_finisher(self):
        """Its session is modelled as collecting everything reachable, which
        a level that finishes on one group does not do."""
        reqs = {"Spoons - Stacked": [], "Spoons - Size (Elastic)": [],
                "Spoons - Solution 1": [], "Spoons - Beaten": [],
                "Pasta - Solution 1": [], "Pasta - Beaten": []}
        slots, plan, where = world(["Spoons", "Pasta"], reqs)
        saved = dict(e2e.KNOWN_COMPLETES_ON)
        try:
            e2e.KNOWN_COMPLETES_ON["Spoons"] = frozenset({"Stacked"})
            self.assertEqual(1, e2e.arrow_slot(slots, plan, where))
        finally:
            e2e.KNOWN_COMPLETES_ON.clear()
            e2e.KNOWN_COMPLETES_ON.update(saved)

    def test_dlc1_boss_is_a_known_table_gap(self):
        """Forcing completes it before its later stages register."""
        self.assertIn("DLC1 Boss", e2e.KNOWN_TABLE_GAPS)


class TestASlowCompletionIsWaitedFor(unittest.TestCase):
    """A solved level may take a while to say so.

    Measured 2026-09-24 with DevTools alone: DLC2 Broken Vases raised
    LevelComplete 13 s after its Draggables were solved - its Pannables
    (Action Only) plays first. The 6 s wait called it exhausted, so the DLC
    arrow check failed on it and the run began off its plan.
    """

    def test_the_wait_covers_the_slowest_measured_completion(self):
        self.assertGreaterEqual(e2e.COMPLETION_WAIT, 13 + 5)

    def test_a_slow_level_gets_its_own_wait(self):
        """DLC2 Curtains completed 49 s after it was forced (recheck,
        2026-09-24), past the 30 s every other level fits in."""
        saved = dict(e2e.FORCEABILITY)
        try:
            e2e.FORCEABILITY["DLC2 Curtains"] = {"all": 49, "passes": 2}
            self.assertGreaterEqual(e2e.completion_wait("DLC2 Curtains"), 49 + 10)
            self.assertEqual(e2e.COMPLETION_WAIT, e2e.completion_wait("Pasta"))
        finally:
            e2e.FORCEABILITY.clear()
            e2e.FORCEABILITY.update(saved)

    def test_play_asks_for_the_levels_own_wait(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("wait=completion_wait(slots[current][1])", text)

    def test_solve_level_uses_it(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        body = text.split("def solve_level(", 1)[1].split("\ndef ", 1)[0]
        self.assertIn("COMPLETION_WAIT", body)


class TestTheSeedMustHandOverTheCredits(unittest.TestCase):
    """Beating every slot is not the goal: the Credits item must be held.

    The DLC gate of 2026-09-24 beat 15/15 with the Credits item still on
    Books Stacked (Seeing Stars) - Solution 2, an alternate solution forcing
    never makes. The paper plan had stopped at all-beaten, so the run went
    one visit past it and stopped, and the credits never opened.
    """

    PLACED = [("Books Stacked (Seeing Stars) - Solution 2", "Credits"),
              ("Credits", "Hint Page")]

    def test_credits_left_on_an_uncollected_location_is_named(self):
        self.assertEqual("Books Stacked (Seeing Stars) - Solution 2",
                         e2e.credits_left_behind(self.PLACED,
                                                 {"collected": set()}))

    def test_credits_collected_is_fine(self):
        self.assertIsNone(e2e.credits_left_behind(
            self.PLACED,
            {"collected": {"Books Stacked (Seeing Stars) - Solution 2"}}))

    def test_judge_seed_asks(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        body = text.split("def judge_seed(", 1)[1].split("\ndef ", 1)[0]
        self.assertIn("credits_left_behind(", body)


class TestAScenicLevelIsFinishedByASkip(unittest.TestCase):
    """A level whose every controller is scenery can only be skipped.

    The DLC gate of 2026-09-24: DLC2 Corn registers one Pannables
    Controller and nothing else, solve cannot force Pannables, so the live
    run spent a Skip on it at visit 1 - while its paper plan had forced
    Solution 1 and the Beaten token, kept the Skip, and came back to Corn at
    visit 15, where the run had nothing left to do there and stopped.
    """

    REQS = {"DLC2 Corn - Solution 1": [], "DLC2 Corn - Solution 2": [],
            "DLC2 Corn - Beaten": [],
            "Spice Jars - Solution 1": [], "Spice Jars - Beaten": []}

    def test_the_scenery_levels_are_derived_from_the_table(self):
        self.assertEqual({"DLC2 Corn", "Drink Glasses", "MerryMess_Presents"},
                         set(e2e.SCENERY_ONLY))
        self.assertTrue(e2e.SCENERY_ONLY <= e2e.UNFORCEABLE)

    def test_the_paper_run_skips_it_on_the_first_visit(self):
        slots, plan, where = world(["DLC2 Corn", "Spice Jars"], self.REQS)
        beaten, _att, spent, visits = e2e.paper_run(
            slots, plan, where, {}, starting=["Skip"], production=True,
            unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(2, beaten)
        self.assertEqual(1, spent)
        self.assertEqual(("DLC2 Corn", True),
                         (visits[0]["level"], visits[0]["skip"]))

    def test_it_is_a_skip_the_seed_must_hold(self):
        need, _have = e2e.skip_shortfall({"Corn (Seeing Stars)": "DLC2 Corn"},
                                         [], [], {"Symmetry"})
        self.assertEqual(1, need)


class TestALevelThatRegistersNothingIsNotForced(unittest.TestCase):
    """Radial Dance Party registers no controller until a dance is started.

    The 2026-09-24 base gate: visit 10 read "controllers: 0 registered on
    Radial Dance Party" five times and collected nothing, while the paper
    plan had forced its dances there (a cat trap and all), so visit 11
    differed and the run stopped. The paper must collect nothing there too,
    and so a seed holding it is not one the gate can clear.
    """

    REQS = {"Radial Dance Party - Radial Pencils 0": ["Rotating"],
            "Radial Dance Party - Solution 1": ["Rotating"],
            "Radial Dance Party - Beaten": ["Rotating"],
            "Spice Jars - Solution 1": [], "Spice Jars - Beaten": []}

    def test_forcing_never_collects_any_of_it(self):
        _slots, plan, where = world(["Radial Dance Party", "Spice Jars"],
                                    self.REQS)
        cannot = e2e.harness_cannot_force(plan, where)
        for loc in where[0]:
            self.assertIn(loc, cannot)
        self.assertNotIn("Spice Jars - Solution 1", cannot)

    def test_the_paper_run_does_not_beat_it_and_says_why(self):
        slots, plan, where = world(["Radial Dance Party", "Spice Jars"],
                                   self.REQS)
        final = {}
        beaten, _att, _spent, _visits = e2e.paper_run(
            slots, plan, where, {}, starting=["Rotating"], production=True,
            final=final, unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(1, beaten)
        why = e2e.why_unclearable(plan, where, {}, final)
        self.assertEqual(1, len(why), why)
        self.assertIn("Radial Dance Party registers nothing", why[0])


class TestTheCompletionPlanReadsTheSeed(unittest.TestCase):
    """completion_plan judges reachability on the SEED's requirements.

    It modelled them from names.json, which does not subtract
    bypassedAbilities, so Workbench's Solution asked for Drawer while the
    seed asks for nothing. With Drawer placed there, the 2026-09-24 base
    walk reported seeds 20260908 and 20260911 as "61 (53) location(s)
    unreachable in any order - a generation bug" although both clear on
    their own requirements.
    """

    def test_a_bypassed_ability_does_not_make_a_seed_unreachable(self):
        import tempfile
        import zipfile
        with tempfile.TemporaryDirectory() as out:
            with zipfile.ZipFile(os.path.join(out, "AP_1.zip"), "w") as z:
                z.writestr("AP_1_Spoiler.txt",
                           "Starting Items:\n\nLocations:\n"
                           "Workbench - Solution 1: Drawer\n"
                           "Workbench - Beaten: Nothing\n")
            plan = {"slots": [(50, "Workbench")], "boundaries": [1],
                    "requirements": {"Workbench - Solution 1": [],
                                     "Workbench - Beaten": []}}
            order, unreachable = e2e.completion_plan(out, "AP_1.zip", plan)
        self.assertEqual([], unreachable)
        self.assertEqual(["Workbench"], order)


class TestTheSeedHasTheSkipsItNeeds(unittest.TestCase):
    """Skip demand against supply, from the seed alone.

    The 2026-09-23 full gate: TupperwareTower and Desktop Computer need a
    Skip each, Symmetry sat on Pencils Solution 2, and two Skips were in the
    pool. 12 of 15, and nothing in the pre-flight said so.
    """

    RUN = {"Tupperware Tower": "TupperwareTower",
           "Desktop Computer": "Desktop Computer",
           "Pencils (Randomized)": "Pencils (Randomized)",
           "Seed Pods": "Seed Pods"}
    PLACED = [("Pencils (Randomized) - Solution 2", "Symmetry"),
              ("Seed Pods - Solution 1", "Skip"),
              ("Tupperware Tower - Solution 1", "Background Change Trap"),
              ("Paper Plane Supplies - Chalk Red", "Skip")]

    def test_the_gate_seed_of_2026_09_23_was_short(self):
        need, have = e2e.skip_shortfall(self.RUN, ["Stacking"], self.PLACED,
                                        {"Symmetry"})
        self.assertEqual((3, 2), (need, have))

    def test_skips_in_hand_count(self):
        need, have = e2e.skip_shortfall(self.RUN, ["Skip", "Skip"],
                                        self.PLACED, {"Symmetry"})
        self.assertGreaterEqual(have, need)

    def test_junk_on_an_alternate_solution_needs_no_skip(self):
        need, _ = e2e.skip_shortfall(
            {"Pencils (Randomized)": "Pencils (Randomized)"}, [],
            [("Pencils (Randomized) - Solution 2", "Hint Page")], {"Symmetry"})
        self.assertEqual(0, need)

    def test_a_skip_on_an_alternate_solution_is_no_need(self):
        """Releasing a Skip with a Skip gains nothing (base seed 20260911:
        Spice Jars Solution 2 held one and was counted as a third need)."""
        need, have = e2e.skip_shortfall(
            {"Spice Jars": "Spice Jars"}, [],
            [("Spice Jars - Solution 2", "Skip")], {"Symmetry"})
        self.assertEqual((0, 0), (need, have))

    def test_a_skip_on_an_unforceable_levels_part_is_supply(self):
        """Its parts are forced like any other (the gate logs `check:
        Desktop Computer - Notes`); only its completion needs a Skip.
        Seed 20260911 held a Skip on Desktop Computer - Clock hands."""
        need, have = e2e.skip_shortfall(
            {"Desktop Computer": "Desktop Computer"}, [],
            [("Desktop Computer - Clock hands", "Skip")], {"Symmetry"})
        self.assertEqual((1, 1), (need, have))

    def test_the_full_gate_yaml_covers_its_unforceable_levels(self):
        """Skips in hand for every KNOWN_UNFORCEABLE level, as --dlc has."""
        text = e2e.yaml_text(False, False, False)
        self.assertIn(f"    Skip: {len(e2e.KNOWN_UNFORCEABLE)}\n", text)


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
    # THE TOOL'S OWN paper_run, so every whole-run test below guards what
    # the gate plans with. The old semantics are kept: no stop at all
    # beaten until a visit comes back empty.
    beaten, attempts, spent, _visits = e2e.paper_run(
        slots, plan, where, placements, unreachable=unreachable,
        skips=skips, park=park, rounds=rounds, flaky=flaky,
        park_after=park_after, stop_when_all_beaten=False)
    return beaten, attempts, spent


class TestASkipLandsOnTheLevelItWasBoughtFor(unittest.TestCase):
    """The 2026-09-24 full gate, from its own log. The harness booted beaten
    Pencils to spend a Skip on its Solution 2 (Symmetry), forced it first,
    Pencils completed again, the mod moved on to Fruit Stickers, and the
    Skip landed there - reported as spent on Pencils. 13 of 15.
    """

    BOOTED = ("[Info   :ALTTL Dev Tools] state: gameState=Gameplay_GameState "
              "activeLevel=Pencils (Randomized) index=999 seed=145519409 "
              "solutionCount=2 found=0 solved=False unlocked=True loaded=True "
              "transitioning=False")
    MOVED_ON = ("[Info   :ALTTL Dev Tools] state: gameState=Gameplay_GameState "
                "activeLevel=Fruit Stickers index=29 seed=-1 solutionCount=2 "
                "found=1 solved=False unlocked=True loaded=False "
                "transitioning=False")

    def test_it_reads_the_running_level(self):
        self.assertEqual(("Pencils (Randomized)", 999, True),
                         e2e.active_level(self.BOOTED))

    def test_the_latest_state_line_wins(self):
        self.assertEqual(("Fruit Stickers", 29, False),
                         e2e.active_level(self.BOOTED + "\n" + self.MOVED_ON))

    def test_no_state_line_is_no_answer(self):
        self.assertIsNone(e2e.active_level("[Info   :x] boot: tore down 0"))

    def test_the_skip_goes_ahead_only_on_the_level_it_was_bought_for(self):
        self.assertTrue(e2e.skip_target_ok(self.BOOTED, 999))
        self.assertFalse(e2e.skip_target_ok(self.BOOTED + "\n" + self.MOVED_ON, 999))
        self.assertFalse(e2e.skip_target_ok(self.MOVED_ON, 29),
                         "a level still loading is not a target either")

    #: The gate's own lines: Pencils completed and the mod queued the next
    #: slot, while `state` - asked a moment later - still said Pencils.
    COMPLETED = (
        "[Info   :ALTTL Dev Tools] 00:18:19  LevelCompleteEarly  id=Pencils "
        "(Randomized)  index=999  solutionCount=2  found=0  seed=145519409  "
        "solutionId=Ordered_0\n"
        "[Info   :A Little To The Left Archipelago] navigation: next -> slot 4 "
        "(level 29)\n"
        "[Info   :ALTTL Dev Tools] 00:18:19  LevelComplete  id=Pencils "
        "(Randomized)  index=999  solutionCount=2  found=0  seed=145519409  "
        "solutionId=Ordered_0\n")

    def test_a_completion_this_visit_means_the_skip_would_land_elsewhere(self):
        """Measured by probe-skip-beaten --target pencils, 2026-09-24:
        `state` still reads Pencils, loaded, after it has completed - the mod
        opens the next slot only after the completion screen."""
        self.assertFalse(e2e.skip_target_ok(self.COMPLETED + self.BOOTED, 999))

    def test_a_level_that_completed_is_not_skipped_as_well(self):
        """A Skip after a completion lands on the next level, so a slot the
        forcing finished gets none this visit."""
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("if (forced and not done) or (not done and exhausted", text)

    def test_a_bought_skip_is_spent_without_forcing_first(self):
        """Forcing re-completes a beaten generator level and the mod moves
        on; a player releasing what is left just opens it and skips."""
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("if current in beaten and current in skip_anyway:", text)


class TestTheHarnessReadsWhatASkipDid(unittest.TestCase):
    """The mod charges a Skip only once the game has skipped: the completion
    and payout happen inside SkipLevel (measured 2026-09-24), so its verdict
    comes after them. droha: "should a skip be used, then refunded?
    shouldn't it just not spend it?" - it used to charge first and refund."""

    SPENT = ("[Info   :A Little To The Left Archipelago] checks: skipped slot 13, "
             "sent 1 remaining location(s)\n"
             "[Info   :A Little To The Left Archipelago] skip: spent one, 1 left\n")
    NOT_USED = ("[Info   :A Little To The Left Archipelago] skip: not used, the "
                "game skipped nothing here - 2 left\n")

    def test_a_skip_the_game_performed_is_spent(self):
        self.assertEqual("spent", e2e.skip_outcome(self.SPENT))

    def test_a_skip_the_game_did_not_perform_is_not_used(self):
        self.assertEqual("not used", e2e.skip_outcome(self.NOT_USED))

    def test_play_waits_for_the_mods_verdict(self):
        """Not for any `skip:` line: DevTools' own `skip: calling
        MainMenu.SkipLevel` matched that before the game had done anything."""
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn('more = log.wait(list(SKIP_VERDICTS), 12, 6, "the skip")', text)
        for verdict in e2e.SKIP_VERDICTS:
            self.assertNotIn(verdict, "[Info   :ALTTL Dev Tools] skip: calling "
                                      "MainMenu.SkipLevel")

    def test_a_refusal_is_a_refusal(self):
        for line in ("skip: refused, none held",
                     "skip: refused, slot 13 has nothing left to find"):
            self.assertEqual("refused", e2e.skip_outcome("[Info   :x] " + line))

    def test_silence_is_none(self):
        self.assertIsNone(e2e.skip_outcome("[Info   :x] boot: tore down 0"))

    def test_play_reads_it_through_skip_outcome(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn('outcome = skip_outcome(more)', text)
        self.assertNotIn('if "skip: spent one" in more:', text)

    # Since 2026-09-25 the mod never answers "not used": where the game will
    # not skip it releases the slot itself, and where the game may skip late
    # it says it is waiting (Skips.cs). A skip that then never lands is the
    # failure left to catch.

    WAITING = ("[Info   :A Little To The Left Archipelago] skip: waiting for the "
               "game to finish the skip\n")

    def test_a_skip_the_game_has_not_made_yet_is_waiting(self):
        self.assertEqual("waiting", e2e.skip_outcome(self.WAITING))

    def test_a_late_skip_that_lands_is_spent(self):
        self.assertEqual("spent", e2e.skip_outcome(self.WAITING + self.SPENT))

    def test_a_spent_skip_unwinds_before_the_next_boot(self):
        """DLC gate, 2026-09-25, visit 19: a Skip on DLC1 Boss, already
        beaten, completed it but banked no new token, so `done` stayed False
        and the next boot went straight over the finished level. StartLevel
        did not take, the game kept Boss, and the pre-Skip check stopped the
        run. A spent Skip always leaves a finished level (or the track)."""
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn('skip_spent = outcome == "spent"', text)
        self.assertIn("last_done = done or skip_spent", text)
        self.assertNotIn("        last_done = done\n", text)

    def test_play_waits_again_for_a_late_skip_and_stops_if_none_lands(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        body = text[text.index("outcome = skip_outcome(more)") - 900:
                    text.index("outcome = skip_outcome(more)") + 1600]
        self.assertIn('if skip_outcome(more) == "waiting":', body)
        self.assertIn('if outcome in ("not used", "waiting"):', body)


class TestTheArrowSessionsChecksCarryOver(unittest.TestCase):
    """The 2026-09-24 gate collected Stamps in the arrow session, and the
    play session never learned it: the mod logs `no location for it` when a
    location is already filed, so Stamps looked unfinished and was revisited
    for nothing at visits 12 and 21 (Fruit Stickers, Pencils and Spice Jars
    likewise)."""

    ARROW = (
        "[Info   :A Little To The Left Archipelago] received item: Stacking\n"
        "[Info   :ALTTL Dev Tools] 00:03:01  LevelComplete  id=Stamps (Randomized)"
        "  index=997\n"
        "[Info   :A Little To The Left Archipelago] check: Stamps (Randomized) - "
        "Solution 1\n"
        "[Info   :A Little To The Left Archipelago] beaten: Stamps (Randomized) - "
        "Beaten\n"
        "[Info   :A Little To The Left Archipelago] credits: unlocked\n")

    def test_the_checks_are_carried(self):
        self.assertEqual({"Stamps (Randomized) - Solution 1",
                          "Stamps (Randomized) - Beaten"},
                         e2e.collected_locations(e2e.carried_checks(self.ARROW)))

    def test_nothing_else_is(self):
        """Items are resent on connect and the rest belongs to that session;
        carrying them would double-count or end the play session early."""
        carried = e2e.carried_checks(self.ARROW)
        for other in ("received item", "LevelComplete", "credits:"):
            self.assertNotIn(other, carried)

    def test_play_starts_from_them(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        self.assertIn("transcript = carried_checks(earlier) + first", text)


class TestNoSkipIsBoughtWhereTheGameCannotSkip(unittest.TestCase):
    """probe-skip-beaten --target pencils [--unbeaten], 2026-09-24: on a
    generator level in a run the game's own SkipLevel does nothing, beaten or
    not. Since 2026-09-25 the mod releases such a slot itself, so the set is
    empty; the mechanism stays pinned for a level that ever comes back."""

    def world(self):
        return world(["Gen", "B"],
                     {"Gen - Solution 1": [], "Gen - Solution 2": [],
                      "Gen - Beaten": [], "B - S": ["Symmetry"],
                      "B - Beaten": ["Symmetry"]})

    def test_the_stall_path_skips_past_a_skipless_level(self):
        slots, plan, where = self.world()
        slot, why, buy = pick(
            slots, plan, where, beaten={0},
            collected={"Gen - Solution 1", "Gen - Beaten"},
            barren={0: frozenset()}, idle=e2e.STUCK_AFTER,
            skipless=frozenset({"Gen"}))
        self.assertFalse(buy, why)

    def test_without_the_set_it_would_buy_one_there(self):
        """The test above is only worth something if this one buys."""
        slots, plan, where = self.world()
        slot, why, buy = pick(
            slots, plan, where, beaten={0},
            collected={"Gen - Solution 1", "Gen - Beaten"},
            barren={0: frozenset()}, idle=e2e.STUCK_AFTER)
        self.assertEqual((0, True), (slot, buy), why)

    def test_no_level_is_skipless_now_the_mod_falls_back(self):
        """Since 2026-09-25 a Skip works on every level: where the game will
        not skip (Pencils, every generator level), the mod releases the slot
        itself (Skips.cs). The mechanism above stays for a level that ever
        comes back."""
        self.assertEqual(frozenset(), e2e.skipless_levels())


class TestThePaperRunIsTheGatesPlan(unittest.TestCase):
    """paper_run is what the gate reports visits against, so each seed fact
    it models is pinned here and mutation-checked."""

    def run_it(self, levels, reqs, placed, starting=(), unreachable=(),
               boundaries=None):
        slots, plan, where = world(levels, reqs)
        if boundaries:
            plan["boundaries"] = boundaries
        return e2e.paper_run(slots, plan, where, placed, starting=starting,
                             unreachable=set(unreachable))

    def test_an_unforceable_level_gives_up_its_item_to_a_skip(self):
        """TupperwareTower and Desktop Computer: forcing collects nothing,
        and the Stacking on the tower only ever comes out by a Skip."""
        reqs = {"U - S": [], "U - Beaten": [],
                "B - S": ["Stacking"], "B - Beaten": ["Stacking"]}
        beaten, _a, spent, visits = self.run_it(
            ["U", "B"], reqs, {"U - S": "Stacking"}, starting=["Skip"],
            unreachable={"U - S", "U - Beaten"})
        self.assertEqual((2, 1), (beaten, spent))
        self.assertTrue(visits[0]["skip"])

    def test_production_skips_an_exhausted_unforceable_level(self):
        """play()'s rule: TupperwareTower's parts force, its completion
        never comes, so with nothing locked a Skip is spent the same visit."""
        reqs = {"TupperwareTower - Tower": [], "TupperwareTower - Solution 1": [],
                "TupperwareTower - Beaten": [], "B - S": ["Swapping"],
                "B - Beaten": ["Swapping"]}
        slots, plan, where = world(["TupperwareTower", "B"], reqs)
        beaten, _a, spent, visits = e2e.paper_run(
            slots, plan, where, {"TupperwareTower - Solution 1": "Swapping"},
            starting=["Skip"], production=True,
            unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual((2, 1), (beaten, spent))
        self.assertTrue(visits[0]["skip"])

    def test_production_holds_the_skip_while_a_part_is_locked(self):
        """Desktop Computer at visit 9: `everything still unsolved here is
        locked`, and no Skip that visit."""
        reqs = {"Desktop Computer - Computer Desktop": [],
                "Desktop Computer - Keys": ["Containers"],
                "Desktop Computer - Solution 1": ["Containers"],
                "Desktop Computer - Beaten": ["Containers"]}
        slots, plan, where = world(["Desktop Computer"], reqs)
        _b, _a, spent, visits = e2e.paper_run(
            slots, plan, where, {}, starting=["Skip"], production=True,
            unreachable=e2e.harness_cannot_force(plan, where), rounds=1)
        self.assertEqual(0, spent)
        self.assertFalse(visits[0]["skip"])

    def test_an_attempt_that_does_not_finish_the_level_is_idle(self):
        """play(): `if done: idle = 0 else: idle += 1` - collecting parts is
        not finishing. The stall Skip waits on this count."""
        reqs = {"TupperwareTower - Tower": [], "TupperwareTower - Solution 1": [],
                "TupperwareTower - Beaten": []}
        slots, plan, where = world(["TupperwareTower"], reqs)
        final = {}
        e2e.paper_run(slots, plan, where, {}, production=True, rounds=1,
                      final=final,
                      unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(1, final["idle"])

    def test_a_revisit_leaves_idle_alone(self):
        """play()'s revisit branch ends in `continue`, before the idle rule."""
        reqs = {"A - S": [], "A - Late": ["Rotating"], "A - Beaten": [],
                "TupperwareTower - Tower": [], "TupperwareTower - Solution 1": [],
                "TupperwareTower - Beaten": []}
        slots, plan, where = world(["A", "TupperwareTower"], reqs)
        final = {}
        _b, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {"TupperwareTower - Tower": "Rotating"},
            production=True, opening=[0], rounds=2, final=final,
            unreachable=e2e.harness_cannot_force(plan, where))
        self.assertEqual(["TupperwareTower", "A"], [v["level"] for v in visits])
        self.assertEqual(1, final["idle"])

    def test_the_arrow_sessions_slot_with_work_left_is_replayed_first(self):
        """The 2026-09-24 full gate: `slot 0 Spice Jars was beaten in an
        earlier session - not demanding a second token`, then visit 1 was
        Spice Jars forced again and counted beaten. Restored is not beaten
        until a visit or the end-of-round reconcile says so."""
        reqs = {"A - Solution 1": [], "A - Solution 2": [], "A - Beaten": [],
                "B - S": [], "B - Beaten": []}
        slots, plan, where = world(["A", "B"], reqs)
        final = {}
        beaten, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {}, production=True, opening=[0],
            unreachable=e2e.harness_cannot_force(plan, where), final=final)
        self.assertEqual(2, beaten)
        self.assertEqual(("A", "attempt"), (visits[0]["level"], visits[0]["kind"]))
        self.assertFalse(visits[0]["skip"])
        self.assertTrue(visits[0]["beaten"])

    def test_a_replayed_arrow_slot_is_not_parked(self):
        """A completed attempt resets fruitless; only an empty REVISIT parks."""
        reqs = {"A - Solution 1": [], "A - Solution 2": [], "A - Beaten": [],
                "B - S": [], "B - Beaten": []}
        slots, plan, where = world(["A", "B"], reqs)
        final = {}
        e2e.paper_run(slots, plan, where, {}, production=True, opening=[0],
                      rounds=1, final=final,
                      unreachable=e2e.harness_cannot_force(plan, where))
        self.assertNotIn(0, final["barren"])
        # play(): the replay completes it and `done` resets idle, even with
        # no second token (`current not in restored`).
        self.assertEqual(0, final["idle"])

    def test_the_arrow_sessions_slot_is_beaten_before_visit_one(self):
        """The full gate's arrow check beats one slot in its own session;
        the run then starts from it rather than playing it again."""
        reqs = {"A - S": [], "A - Beaten": [], "B - S": [], "B - Beaten": []}
        slots, plan, where = world(["A", "B"], reqs)
        beaten, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {}, production=True, opening=[0])
        self.assertEqual(2, beaten)
        self.assertEqual(["B"], [v["level"] for v in visits])

    def test_a_pack_opens_the_next_slots_only_when_collected(self):
        reqs = {"A - S": [], "A - Beaten": [], "B - S": [], "B - Beaten": []}
        _b, _a, _s, visits = self.run_it(
            ["A", "B"], reqs, {"A - S": "Progressive Puzzle Pack"},
            boundaries=[1, 2])
        self.assertEqual(["A", "B"], [v["level"] for v in visits])

    def test_without_the_pack_the_second_slot_never_opens(self):
        reqs = {"A - S": [], "A - Beaten": [], "B - S": [], "B - Beaten": []}
        beaten, _a, _s, visits = self.run_it(
            ["A", "B"], reqs, {"A - S": "Hint Page"}, boundaries=[1, 2])
        self.assertEqual(1, beaten)
        self.assertNotIn("B", [v["level"] for v in visits])

    def test_a_collected_skip_adds_to_supply(self):
        reqs = {"A - S": [], "A - Beaten": [],
                "U - S": [], "U - Beaten": []}
        beaten, _a, spent, _v = self.run_it(
            ["A", "U"], reqs, {"A - S": "Skip"},
            unreachable={"U - S", "U - Beaten"})
        self.assertEqual((2, 1), (beaten, spent))

    def test_a_trap_on_a_part_resets_the_visit_that_sends_it(self):
        reqs = {"A - Part": [], "A - Solution 1": [], "A - Beaten": []}
        _b, _a, _s, visits = self.run_it(["A"], reqs,
                                         {"A - Part": "Cat Trap"})
        self.assertTrue(visits[0]["reset"])

    def test_production_marks_the_visit_a_part_trap_resets(self):
        """The gate reports passes from this: 2026-09-23's log shows every
        part trap resetting its level mid-solve (Candy Canes, Medicine
        Cabinet, Paper Plane Supplies, Desktop Computer)."""
        reqs = {"A - Part": [], "A - Solution 1": [], "A - Beaten": []}
        slots, plan, where = world(["A"], reqs)
        _b, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {"A - Part": "Cat Trap"}, production=True)
        self.assertTrue(visits[0]["reset"])

    def test_production_lets_a_solution_trap_miss(self):
        reqs = {"A - Part": [], "A - Solution 1": [], "A - Beaten": []}
        slots, plan, where = world(["A"], reqs)
        _b, _a, _s, visits = e2e.paper_run(
            slots, plan, where, {"A - Solution 1": "Cat Trap"}, production=True)
        self.assertFalse(visits[0]["reset"])

    def test_a_trap_on_a_solution_misses(self):
        """Sent at completion, inside the mod's completion grace."""
        reqs = {"A - Part": [], "A - Solution 1": [], "A - Beaten": []}
        _b, _a, _s, visits = self.run_it(["A"], reqs,
                                         {"A - Solution 1": "Cat Trap"})
        self.assertFalse(visits[0]["reset"])

    def test_it_stops_when_the_last_token_opens_the_credits(self):
        reqs = {"A - S": [], "A - Beaten": [], "B - S": ["Drawer"],
                "B - Beaten": []}
        _b, _a, _s, visits = self.run_it(["A", "B"], reqs, {},
                                         starting=["Drawer"])
        self.assertEqual(2, len(visits))


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

    def test_no_yaml_excludes_locations(self):
        """The 14 container excludes became notALocation, so none is left.

        A stale name makes Generate.py reject the whole yaml; see
        test_every_excluded_container_is_still_a_location.
        """
        for dlc_on in (False, True):
            self.assertNotIn("exclude_locations",
                             e2e.yaml_text(False, False, dlc_on))


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
        block = block[:block.index('outcome = skip_outcome(more)')]
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
        saved = e2e.CONTAINER_EXCLUDES
        e2e.CONTAINER_EXCLUDES = frozenset({self.COMBS})
        try:
            self.assertTrue(e2e.only_a_skip_can_finish(
                0, {0: [self.COMBS]}, set()))
        finally:
            e2e.CONTAINER_EXCLUDES = saved

    def test_a_finished_card_expects_nothing(self):
        where = {0: [self.FILING + "1", self.FILING + "2"]}
        self.assertFalse(e2e.only_a_skip_can_finish(0, where, set(where[0])))

    def test_solution_number_reads_the_ordinal(self):
        self.assertEqual(2, e2e.solution_number("X - Solution 2"))
        self.assertIsNone(e2e.solution_number("X - Beaten"))
        self.assertIsNone(e2e.solution_number("X - Solution zero"))

    def test_every_excluded_container_is_still_a_location(self):
        """Generate.py rejects the whole yaml over one unknown name.

        2026-09-23: all 14 had become notALocation in levels.json and every
        DLC seed failed to generate, which only the full gate would have hit.
        """
        stale = sorted(e2e.CONTAINER_EXCLUDES - part_locations())
        self.assertFalse(stale, f"not locations any more: {stale}")

    def test_the_container_list_comes_from_the_yaml(self):
        """Same source as the gate generates from, so they cannot drift."""
        self.assertEqual(0, len(e2e.CONTAINER_EXCLUDES))

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


class TestTheArrowSessionsLogIsKept(unittest.TestCase):
    """Every launch deletes LogOutput.log first (Log.before_launch), and the
    full gate launches twice, so only the main run's log was kept. The arrow
    session's log - the one the Post-It arrow flake is in - was gone."""

    def setUp(self):
        import tempfile
        self.dir = tempfile.mkdtemp()
        self.saved = (e2e.LOG, e2e.KEPT_LOGS)
        e2e.LOG = os.path.join(self.dir, "LogOutput.log")
        e2e.KEPT_LOGS = os.path.join(self.dir, "kept")

    def tearDown(self):
        import shutil
        e2e.LOG, e2e.KEPT_LOGS = self.saved
        shutil.rmtree(self.dir, ignore_errors=True)

    def _write_log(self, text):
        with open(e2e.LOG, "w", encoding="utf-8") as f:
            f.write(text)

    def test_the_copy_is_named_by_the_clock_and_the_tag(self):
        self._write_log("[Info] navigation: replay Next\n")
        kept = e2e.keep_log("arrow")
        self.assertRegex(os.path.basename(kept), r"^e2e-\d{8}-\d{6}-arrow\.log$")
        with open(kept, encoding="utf-8") as f:
            self.assertEqual("[Info] navigation: replay Next\n", f.read())

    def test_no_tag_keeps_the_old_name(self):
        """The end-of-run copy keeps the name older kept logs already use."""
        self._write_log("x\n")
        self.assertRegex(os.path.basename(e2e.keep_log()),
                         r"^e2e-\d{8}-\d{6}\.log$")

    def test_a_missing_log_is_reported_not_raised(self):
        """It runs in a finally and between sessions; it must never be the
        thing that ends a gate."""
        self.assertIsNone(e2e.keep_log("arrow"))

    def test_main_keeps_the_arrow_log_before_play_launches(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        body = text[text.index("def main():"):text.index("def main_restoring")]
        arrow = body.index("check_arrow(log, plan)")
        only_arrow_return = body.index("return 0 if all(ok for _, ok in "
                                       "arrow_results)")
        kept = body.index('keep_log("arrow")')
        launch = body.index("play(log, plan, arrow_text)")
        self.assertLess(arrow, only_arrow_return)
        self.assertLess(only_arrow_return, kept,
                        "--only-arrow already keeps it in main_restoring")
        self.assertLess(kept, launch,
                        "play() deletes the log when it launches the game")


class TestTheGateWarnsWhenTheGameWasPaused(unittest.TestCase):
    """A paused game holds every gameplay event (measured 2026-09-24), and
    the DLC gate lost its events for good with the clock at 0. droha: the
    gate warns when it finds the game paused; it does not fail.

    A pause alone is not the warning: going to the title pauses the game on
    its own and boot undoes it (measured 2026-09-25, replay of 22:31). What
    holds events is solving while paused, so that is what is reported."""

    DEV = "[Info   :ALTTL Dev Tools] "

    def lines(self, *bodies):
        return "".join(self.DEV + b + "\n" for b in bodies)

    def test_a_solve_sent_while_paused_is_reported(self):
        text = self.lines("game: Pause(True) -> Paused=True timeScale=0",
                          "command: solve:1")
        found = e2e.pause_warnings(text)
        self.assertEqual(1, len(found))
        self.assertIn("solve:1", found[0])
        self.assertIn("Paused=True", found[0])

    def test_a_title_pause_that_boot_undid_is_not(self):
        """Real lines, e2e-20260925-004849-replay2231.log 166-183."""
        text = self.lines(
            "command: menu:title",
            "game: Pause(True) -> Paused=True timeScale=0",
            "time: timeScale changed 1 -> 0 | Paused=True timeScale=0",
            "command: boot:1226",
            "boot: the game was paused (Paused=True timeScale=0)",
            "game: Pause(False) -> Paused=False timeScale=1",
            "boot: unpaused it -> Paused=False timeScale=1",
            "command: solve:1")
        self.assertEqual([], e2e.pause_warnings(text))

    def test_a_clock_stopped_mid_level_is_reported(self):
        text = self.lines(
            "game: Pause(False) -> Paused=False timeScale=1",
            "command: solve:0",
            "time: timeScale changed 1 -> 0 | Paused=True timeScale=0",
            "command: solve:1", "command: solve:2")
        self.assertEqual(2, len(e2e.pause_warnings(text)))

    def test_a_clock_set_to_zero_without_a_pause_is_reported(self):
        """Whether the clock alone holds events is not measured; warn."""
        text = self.lines(
            "time: timeScale changed 1 -> 0 | Paused=False timeScale=0",
            "command: solve:3")
        self.assertEqual(1, len(e2e.pause_warnings(text)))

    def test_it_is_a_warning_not_a_failed_check(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            text = code_only(fh.read())
        body = text[text.index("def main():"):text.index("def keep_log")]
        self.assertEqual(2, body.count("report_pauses("),
                         "--only-arrow and the full run both report")
        for line in body.splitlines():
            if "results.append(" in line or "arrow_results" in line:
                self.assertNotIn("pause", line.replace("pause menu Exit", ""))


class TestTheDocsDoNotLie(unittest.TestCase):
    """check-docs.py cannot catch a comment that is merely wrong."""

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               "release_e2e.py"), encoding="utf-8") as fh:
            return fh.read()

    def test_nothing_shortens_the_run(self):
        """PUZZLES is assigned once, at module level, and never again.

        --quick was documented as a three-puzzle run in three places for
        as long as puzzle_count has had a floor (8, then 15, now 10; the
        gate stays at 15). This asserts the
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
        self.assertEqual(15, e2e.PUZZLES)

    def test_the_quick_flag_admits_it_is_still_a_full_length_run(self):
        """The help is what someone reads before waiting fifteen minutes."""
        self.assertIn("Still fifteen puzzles", self.source())


class TestTheConfigWritersKeepThePlayersSettings(unittest.TestCase):
    """write_config used to overwrite the whole mod config.

    That wiped [Display] WindowSize at every harness setup, so the next
    launch came up at 3840x2160 (2026-09-23). Run against a copy of the real
    installed config, not a made-up one, since that file's shape is the point.
    """

    def setUp(self):
        import shutil
        import tempfile
        self.dir = tempfile.mkdtemp()
        self.path = os.path.join(self.dir, "droha.alttl.archipelago.cfg")
        if os.path.isfile(e2e.MOD_CONFIG):
            shutil.copyfile(e2e.MOD_CONFIG, self.path)
        else:
            with open(self.path, "w", encoding="utf-8") as f:
                f.write("[Display]\nWindowSize = 1920x1080\n")
        self.saved = e2e.MOD_CONFIG
        e2e.MOD_CONFIG = self.path

    def tearDown(self):
        import shutil
        e2e.MOD_CONFIG = self.saved
        shutil.rmtree(self.dir, ignore_errors=True)

    def _lines(self):
        with open(self.path, encoding="utf-8") as f:
            return f.read().splitlines()

    def test_a_chosen_size_survives(self):
        e2e.set_cfg_keys(self.path, "Display", {"WindowSize": "1920x1080"})
        e2e.write_config()
        self.assertEqual("1920x1080",
                         e2e.cfg_value(self.path, "Display", "WindowSize"))
        self.assertEqual("localhost", e2e.cfg_value(self.path, "Server", "Host"))
        self.assertEqual(str(e2e.PORT),
                         e2e.cfg_value(self.path, "Server", "Port"))

    def test_no_size_means_720p_never_4k(self):
        e2e.set_cfg_keys(self.path, "Display", {"WindowSize": ""})
        e2e.write_config()
        self.assertEqual("1280x720",
                         e2e.cfg_value(self.path, "Display", "WindowSize"))

    def test_every_other_line_is_kept(self):
        before = [l for l in self._lines()
                  if l.strip() and "=" not in l or l.lstrip().startswith("#")]
        e2e.write_config()
        after = self._lines()
        for line in before:
            self.assertIn(line, after)

    def test_a_missing_file_is_created(self):
        os.remove(self.path)
        e2e.write_config()
        self.assertEqual("true",
                         e2e.cfg_value(self.path, "Server", "AutoConnect"))
        self.assertEqual("1280x720",
                         e2e.cfg_value(self.path, "Display", "WindowSize"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
