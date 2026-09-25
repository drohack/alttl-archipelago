"""Progression must not land on a requirement nobody has established.

WHY THIS FILE EXISTS, and it is one real dead run rather than a theory.

On 2026-09-21 droha played a 24-puzzle DLC seed. The generator put a
Progressive Puzzle Pack on `Tupperware Nesting - Lids`, which declares
`['Containers']`. The lids cannot be placed until the tupperware is nested, and
nesting is `Stack 1` (`StackablesZ`, Stacking). droha held Containers and not
Stacking. It was the only load-bearing check left in the run, there were no
Skips, and the run was over. The log shows 1m50s inside the level with `Lids`
open and undimmed and no check ever firing.

The requirement was not wrong by accident in a way anyone could see. It comes
from `levels.json` `controllers[].dependsOn`, harvested from the running game,
and `Lids` is not in that level's phase list at all - so it got `dependsOn: []`
and the closure had nothing to walk. A missing edge is invisible to every
consumer downstream, because the data is the only record of what the game
requires.

THIS IS A REGRESSION, WHICH IS THE PART THAT MATTERS. The same shape was found
in 0.3.0 and hand-patched: see
`test_generation.test_a_drawer_cannot_be_emptied_before_it_opens`, whose
docstring describes Tool Drawer's 47 draggables needing no items at all while
the game held the DrawerController shut. That fix was a literal eight-entry set
across four base-game levels. Every DLC level added afterwards walked straight
back into it, and nothing noticed for two releases.

WHY THE EXISTING TESTS DID NOT CATCH IT, stated plainly because it is the
lesson. `test_fill_stress.test_a_location_is_unreachable_without_the_ability_
it_names` derives BOTH its ability list and its location set from
`world.requirements` - the same dict `set_all_rules` builds the access rules
from. A location requiring `[]` is skipped by its own filter on every
iteration. It proves the rules honour the requirements they are given, which is
true and is not the question.

So this file does not ask whether a requirement is right. It asks whether we
have any business betting a run on it.
"""

import unittest

from BaseClasses import ItemClassification
from Fill import distribute_items_restrictive

from . import bases
from .test_fill_stress import _generate, seed_span
from .. import data
from .. import items
from .. import rules


#: The location that actually killed a run, by level id and group. Pinned by
#: name so a future table correction that makes it proven has to come past
#: this test and say so out loud.
THE_ONE_THAT_BIT = ("TupperwareNesting", "Lids")

NOTHING_GUARDED = ("no level has a guarded part location - nothing for the "
                   "guard to refuse, so a sweep here would pass on nothing")


def _guardable():
    """Levels with a part location the guard applies to."""
    return [l for l in data.LEVELS if l.has_parts and l.unproven_parts]


class TestTheGuardKnowsWhatItDoesNotKnow(bases.ALTTLTestBase):
    """Table-level facts. No fill needed."""

    options = {}

    def test_the_location_that_killed_a_run_needs_its_whole_level(self):
        """Lids now asks for everything its level does, from play.

        It was pinned as UNPROVEN until 2026-09-23, when droha played it
        three ways: holding Containers+Stacking the lids stayed game-blocked
        after every stack, and holding Grids+Stacking "the lids showed up
        finally" only after the food. So Lids depends on Food, and needs
        Containers + Grids + Stacking - the whole level - which means it can
        no longer ask for less than the player needs. If this ever loosens,
        the check that ended a run is back.
        """
        level_id, part = THE_ONE_THAT_BIT
        level = next(l for l in data.LEVELS if l.level_id == level_id)

        self.assertIn(part, level.enforced_part_abilities,
                      "%s no longer has a group called %s - if the table was "
                      "corrected, say so here rather than deleting the pin"
                      % (level_id, part))
        self.assertEqual(
            level.enforced_abilities, level.enforced_part_abilities[part],
            "%s / %s asks for %s while its level needs %s. droha found on "
            "2026-09-23 that the lids only appear after the food; do not "
            "loosen this without new play."
            % (level_id, part,
               sorted(level.enforced_part_abilities[part]),
               sorted(level.enforced_abilities)))
        self.assertEqual({"Containers", "Grids", "Stacking"},
                         set(level.enforced_part_abilities[part]))

    def test_fruit_stickers_cannot_be_peeled_without_sticking(self):
        """Every Fruit Stickers part needs Tidying AND Sticking, from play.

        droha, 2026-09-24, holding only Tidying: "stickers are greyed out and
        i can't peel them". The mod's Sticking lock reaches the peel as well
        (the full gate saw Remove Stickers dimmed=12 while its own summary
        said "1 locked, 2 open"), so Remove Stickers asking for Tidying alone
        let a seed put Sticking behind a check that needs Sticking. Holding
        only Sticking the peel works and the stick does not (2026-09-23), so
        neither part is free of the other: they are one mutual group.
        """
        level = next(l for l in data.LEVELS if l.level_id == "Fruit Stickers")
        self.assertEqual({"Sticking", "Tidying"}, set(level.enforced_abilities))
        for part, own in level.enforced_part_abilities.items():
            self.assertEqual(
                level.enforced_abilities, own,
                "Fruit Stickers / %s asks for %s; the stickers cannot be "
                "peeled without Sticking (droha, 2026-09-24)"
                % (part, sorted(own)))

    def test_a_group_needing_everything_its_level_needs_is_never_unproven(self):
        """The guard is about UNDERSTATEMENT and nothing else.

        A group asking for its level's whole ability set cannot be asking for
        less than the level does, so there is nothing to be wrong about. If
        this ever fails the rule has become "distrust everything", which is a
        different and much more expensive policy.
        """
        for level in data.LEVELS:
            whole = level.enforced_abilities
            for part, own in level.enforced_part_abilities.items():
                if own == whole:
                    self.assertNotIn(part, level.unproven_parts,
                                     "%s / %s needs the whole level's set and "
                                     "is still called unproven"
                                     % (level.level_id, part))

    def test_a_proven_level_has_no_unproven_groups(self):
        """PROVEN_LEVELS is the whole exemption, and it is all-or-nothing.

        Per level rather than per group on purpose: the evidence that settles
        one of these is almost always "somebody swept or played this level",
        which settles all of its groups at once. A half-proven level would
        record a distinction nobody actually made.
        """
        for level in data.LEVELS:
            if level.level_id in data.PROVEN_LEVELS:
                self.assertEqual(frozenset(), level.unproven_parts,
                                 "%s is listed as proven" % level.level_id)

    def test_the_proven_list_names_levels_that_exist(self):
        known = {l.level_id for l in data.LEVELS}
        for level_id in data.PROVEN_LEVELS:
            self.assertIn(level_id, known,
                          "proven-requirements.json names %s, which is not a "
                          "level. A typo here silently proves nothing."
                          % level_id)


class TestTheGapSignalsAreTheOnesWeMeasured(bases.ALTTLTestBase):
    """Each signal that makes a level suspect, pinned to what it found.

    These are canaries as much as assertions. Every one of them describes a
    hole in the harvested data, so when the sweep is re-run and the hole
    closes, the test fails and says "re-measure" rather than letting a stale
    hedge sit in the generator forever.
    """

    options = {}

    def test_the_phase_orphan_detector_names_exactly_these(self):
        """A controller in neither the phase list nor any dependency.

        Four across 173 levels when this was written, which is why the
        detector is worth having: no false-positive flood, and one of the four
        was the group that ended a run.

        TUPPERWARENESTING/LIDS IS GONE FROM THIS LIST, and that is the
        detector working rather than breaking. Lids was the orphan - absent
        from the level's own phase chain, so the sweep recorded dependsOn []
        and every consumer downstream believed it needed only Containers. On
        2026-09-22 it gained a dependency on Stack 1, from two independent
        signals: droha played the level and could not place the lids, and
        probe-occlusion measured Lids at 100 per cent inside Stack 1's
        footprint. It now needs Containers + Stacking, which is what was
        actually experienced, so it is no longer an orphan.

        If this list GROWS, a new level has arrived with an unrecorded phase
        order. If it shrinks again, somebody fixed one - say which, here.
        """
        found = {}
        for level in data.LEVELS:
            for reason in level.structural_gap.split("; "):
                if "appear in neither the phase list" in reason:
                    found[level.level_id] = reason.split(" appear in")[0]

        self.assertEqual(
            {
                "PawPrints": "Coffee Spill, Free Leaves",
                "DLC2 Ghost Cat": "Indexables",
            },
            found)

    def test_ghost_cat_phase_list_is_still_a_placeholder(self):
        """DLC2 Ghost Cat has nine phases all harvested as "(none)".

        A sweep that writes a placeholder rather than failing produces data
        that READS as complete, which is the worst possible outcome. Pinned
        here so re-running the sweep either fixes it or proves it is the game's
        own answer.
        """
        level = next(l for l in data.LEVELS if l.level_id == "DLC2 Ghost Cat")
        self.assertIn("(none)", level.structural_gap)

    def test_extra_abilities_makes_a_level_suspect(self):
        """extraAbilities repairs the level union and never a part.

        bypassedAbilities, its mirror, IS applied per part (data.py, "subtracted
        LAST"). The asymmetry runs only in the dangerous direction: the one
        mechanism the project has for "the sweep saw too little" cannot reach
        a part requirement at all. So a level carrying extras is a level
        already caught under-reporting, whose parts were never corrected.
        """
        with_extras = {l.level_id for l in data.LEVELS
                       if "extraAbilities patched" in l.structural_gap}
        self.assertEqual(
            {"MedicineCabinet", "Record Player", "TupperwareTower",
             "DLC1 Clock Cupboard", "DLC1 Tea Cabinet", "DLC1 Pantry",
             "DLC1 Media Cabinet", "DLC1 Trophy Cabinet", "DLC2 Bells",
             "DLC2 Boss"},
            with_extras)

    def test_dlc1_edges_are_only_the_ones_we_added_by_hand(self):
        """THE CANARY, and it has already fired once - correctly.

        It used to assert ZERO dependsOn edges across all 24 DLC1 levels,
        which was true and was the point: every other source has some (archive
        7 of 26, base 7 of 69, dlc2 10 of 34), DLC1 is the source richest in
        drawers, and a whole-source absence is a harvest failure rather than
        24 levels that happen to be ungated.

        On 2026-09-22 it went red because EDGES WERE ADDED BY HAND, from
        tools/probe-blocked.py plus a play test - not because the sweep was
        re-run. So the assertion is now the exact set of levels carrying
        edges. A level appearing here that nobody added is the sweep finally
        recording what it always should have, and that is the moment to
        re-measure the guard and move what it settles into
        data/proven-requirements.json.

        The nine below are drawer levels corrected in one pass. The remaining
        15 DLC1 levels STILL have no edges at all, so the harvest gap this was
        written to watch is still open.
        """
        added = {
            "DLC1 Boss", "DLC1 Craft Supplies", "DLC1 Fossils",
            "DLC1 Game Pieces", "DLC1 Jewelry Box",
            "DLC1 Kitchen Utensils Drawers", "DLC1 Lunch Tray",
            "DLC1 Sewing Box",
            # Added 2026-09-22 from droha's own play: "i can move the clocks,
            # but i can't open the cubbord to put the clocks in". Its opener
            # is AnimScrubbables, which maps to Gadgets rather than Drawer -
            # so every drawer-shaped detector built that day was structurally
            # incapable of finding it.
            "DLC1 Clock Cupboard",
            # Added 2026-09-23 from the locks-off sweep: Tea Cabinet's
            # contents wait on its cupboard doors, Daggers' on its drawers.
            "DLC1 Tea Cabinet", "DLC1 Daggers",
        }
        dlc1 = [raw for raw in data._LEVELS_RAW["levels"]
                if raw["source"] == "dlc1"]
        self.assertEqual(24, len(dlc1))

        with_edges = {raw["levelId"] for raw in dlc1
                      if any(c.get("dependsOn") for c in raw["controllers"])}
        self.assertEqual(
            added, with_edges,
            "the set of DLC1 levels carrying dependency edges moved. If the "
            "sweep was re-run, re-measure the guard and prove what it "
            "settles; if an edge was LOST, that is the direction that ends "
            "runs.")

    def test_every_guarded_level_says_why(self):
        """No guard without a stated reason.

        The whole policy is "follow evidence of a gap, not absence of
        evidence", and a guarded level with an empty reason would be exactly
        the thing that policy rejects.
        """
        for level in data.LEVELS:
            if level.unproven_parts:
                self.assertTrue(
                    level.structural_gap,
                    "%s has guarded groups and no reason" % level.level_id)


#: Every part that asks for less than its level AND is not guarded. Progression
#: can land on these, so each one is a bet that its group is independent, and
#: the tag says what the bet rests on (settled = played or measured separate).
#:
#: The 2026-09-23 audit found 82 such locations across 30 levels. 28 of those
#: levels were guarded first, then every one of the 30 was played and moved
#: to `proven` in data/proven-requirements.json the same day. So this set is
#: empty, and a new entry is a new unreviewed bet.
UNGUARDED_UNDERSTATED = {
    # Cleaning Supplies left on 2026-09-23: played locks-on and proven, so the
    # proven-level skip covers it.
    # Fruit Stickers left this list on 2026-09-23: a locks-on run showed
    # Match needs Tidying too, so it gained an edge and is now proven.
}


class TestEveryUnderstatedPartIsAccountedFor(bases.ALTTLTestBase):
    """The guard covers levels with a reason for doubt. This covers the rest.

    A part asking for strictly less than its level, on a level the guard does
    not cover, is progression-eligible on nothing but the table's word. The
    2026-09-22 audit found 82 such locations across 30 levels and listed every
    one above with what its bet rests on. So a new level, a re-sweep or an
    edge that creates another one goes red here instead of going into a seed
    unreviewed - and one that disappears (an edge added, the guard widened)
    goes red too, so the list cannot rot into a stale excuse.
    """

    options = {}

    def test_the_unguarded_understated_set_is_the_reviewed_one(self):
        found = {}
        for level in data.LEVELS:
            if not level.has_parts:
                continue
            whole = level.enforced_abilities
            # A PROVEN level is settled by definition: its evidence lives in
            # data/proven-requirements.json, checked below.
            if level.level_id in data.PROVEN_LEVELS:
                continue
            parts = sorted(
                part for part, own in level.enforced_part_abilities.items()
                if own < whole and part not in level.unproven_parts)
            if parts:
                found[level.level_id] = parts

        pinned = {lid: sorted(parts)
                  for lid, (_tag, parts) in UNGUARDED_UNDERSTATED.items()}
        self.assertEqual(
            pinned, found,
            "a part asking for less than its level and carrying no guard "
            "appeared or disappeared. New: play it or guard it before it "
            "goes in a seed. Gone: say why in UNGUARDED_UNDERSTATED.")

    def test_every_proven_level_carries_its_evidence(self):
        """Proving a level lifts its guard, so the entry must say how."""
        raw = data._PROVEN_RAW.get("proven", {})
        for level_id in data.PROVEN_LEVELS:
            evidence = raw.get(level_id, "")
            self.assertIn("2026", evidence,
                          "%s is proven without a dated record" % level_id)
            self.assertGreater(len(evidence), 60,
                               "%s: 'it looks fine' is not evidence" % level_id)

    def test_every_entry_says_what_it_rests_on(self):
        for lid, (tag, _parts) in UNGUARDED_UNDERSTATED.items():
            self.assertIn(tag, {"settled"},
                          lid)


class TestNothingLoadBearingLandsOnAGuess(unittest.TestCase):
    """The actual contract, on a PINNED seed with both DLCs on.

    Both DLCs because that is where the unproven groups are: the drawer and
    cupboard levels are almost all DLC1, and the phase-driven ones DLC2. A
    base-only seed passed this for months while the DLC seed was the one
    ending runs.

    PINNED, and that is not incidental. This class used to take
    ALTTLTestBase's unpinned seed and failed about one run in five, because
    the pool then gave guards back when the opening was too thin to fill
    (since 2026-09-23 it redraws the run instead), so "the guard is non-empty"
    was a property of the seed rather than of the code. A fixed seed asks the
    question actually worth asking: on THIS world, is every unproven location
    refusing progression.
    """

    SEED = 20260902
    OPTIONS = {"cupboards_and_drawers": True, "seeing_stars": True}

    @classmethod
    def setUpClass(cls):
        cls.test = _generate(cls.OPTIONS, cls.SEED)
        cls.multiworld = cls.test.multiworld
        cls.player = cls.test.player

    def _world(self):
        return self.multiworld.worlds[self.player]

    def test_unproven_locations_refuse_progression(self):
        """The marking, asserted directly on the rule the fill consults.

        Deterministic, unlike the sweep below: it does not depend on the fill
        choosing to try a progression item here. Both are needed - this one
        catches the guard not being applied, the sweep catches it being applied
        and ignored.
        """
        # Since 2026-09-23 every multi-part level is proven; the levels still
        # carrying a structural gap have one part each and mint no part
        # location. Say so rather than pass on an empty loop - and before
        # generating 60 seeds to find out.
        guardable = _guardable()
        if not guardable:
            self.skipTest(NOTHING_GUARDED)
        # The pinned seed may draw no guarded level; search forward for one.
        test = self.test
        for seed in range(self.SEED, self.SEED + 60):
            test = _generate(self.OPTIONS, seed)
            if test.multiworld.worlds[test.player].unproven_locations:
                break
        world = test.multiworld.worlds[test.player]
        unproven = world.unproven_locations
        self.assertTrue(unproven,
                        "60 DLC seeds drew no guarded location although %s "
                        "have one - the guard is not running"
                        % [l.level_id for l in guardable])

        progression = world.create_item(items.PROGRESSIVE_PACK)
        self.assertTrue(progression.advancement)

        for location in test.multiworld.get_locations(test.player):
            if location.name in unproven:
                self.assertFalse(
                    location.item_rule(progression),
                    "%s is unproven but would accept a Progressive Puzzle "
                    "Pack" % location.name)

    def test_solution_and_beaten_locations_are_never_unproven(self):
        """They already require the level's whole enforced set.

        Which is why the guard costs as little as it does: the fill keeps every
        Solution and every Beaten location, and those are the majority. If this
        fails, rules.requirements has started deriving them from something
        narrower and the guard's cost assumption is stale.
        """
        world = self._world()
        for name in world.unproven_locations:
            tail = name.rsplit(" - ", 1)[-1]
            self.assertFalse(
                tail.startswith("Solution ") or tail == "Beaten",
                "%s is a solution or beaten location and should never need "
                "the guard" % name)

    def test_the_guard_leaves_room_to_place_progression(self):
        """A safety net that strangles the fill is not a safety net.

        Measured 2026-09-22: 191 of 825 part locations are unproven with an
        empty proven list, leaving 77 per cent of the table open. Asserted as a
        ratio rather than a count so correcting the tables moves it in the
        right direction without failing here.
        """
        world = self._world()
        total = len(world.location_names_in_use)
        blocked = len(world.unproven_locations)
        self.assertLess(
            blocked, total * 0.5,
            "%d of %d locations refuse progression as unproven. Past about half, "
            "the guard is the thing breaking seeds rather than the bug it "
            "guards against - prove some levels instead of widening it."
            % (blocked, total))


class TestNoProgressionOnAGuess(unittest.TestCase):
    """The contract, swept across seeds rather than asserted on one.

    THIS IS THE TEST, and the single-seed version of it was worthless. Whether
    the fill happens to put progression on any particular location is a
    property of the seed, not of the code: with the guard deliberately removed,
    one seed came back clean and the suite went green over a bug that had
    already ended a real run. A contract you can satisfy by being lucky is not
    a contract.

    The sweep reuses test_fill_stress's `_generate` and `seed_span` so the two
    files cannot disagree about what a seed is, and so ALTTL_STRESS_SEEDS
    widens this as well before a release.
    """

    #: Both DLCs on. The unproven groups live almost entirely in DLC content -
    #: drawers and cupboards in Cupboards and Drawers, phase-driven levels in
    #: Seeing Stars - which is exactly why a base-only seed passed for months
    #: while the DLC seed was the one killing runs.
    OPTIONS = {"cupboards_and_drawers": True, "seeing_stars": True}

    def test_no_progression_item_sits_on_an_unproven_location(self):
        if not _guardable():
            self.skipTest(NOTHING_GUARDED)
        failures = []
        for seed in seed_span():
            test = _generate(self.OPTIONS, seed)
            distribute_items_restrictive(test.multiworld)
            world = test.multiworld.worlds[test.player]
            unproven = world.unproven_locations

            for location in test.multiworld.get_locations(test.player):
                if location.name not in unproven:
                    continue
                item = location.item
                if item is None:
                    continue
                if item.classification & ItemClassification.progression:
                    failures.append("  seed %d: %s @ %s"
                                    % (seed, item.name, location.name))

        # Every offending pair across every seed, in one message. Stopping at
        # the first hides whether the leak is one location or the whole policy.
        self.assertEqual(
            [], failures,
            "progression landed on a location whose requirement nobody has "
            "established:\n" + "\n".join(failures))


class TestTheGuardIsNotGivenBack(unittest.TestCase):
    """The sweep above checks the guard that SURVIVED. This checks the one asked for.

    TestNoProgressionOnAGuess reads `world.unproven_locations`, which is what
    pool._affordable_guard KEPT. A guard it gave back is simply absent from
    that set, so the test cannot see it - and on default-size seeds it rarely
    happens, so the test has never been near the case.

    Measured 2026-09-22 over 40 seeds each: an 8-puzzle seed gives guards back
    on 35 of 40 (both DLCs) and 40 of 40 (base), and progression then lands on
    a given-back location 108 and 212 times. Tupperware Nesting - Lids, the
    location that ended a run, took progression 20 times.

    It failed exactly that way until 2026-09-23, when pool.decide stopped
    shipping give-backs: it redraws the run instead (pool.DRAW_ATTEMPTS), and
    the option floor rose to 15 (10 since 2026-09-25). It runs at the floor
    with both DLCs, the tightest shape a player can ask for.
    """

    OPTIONS = {"cupboards_and_drawers": True, "seeing_stars": True,
               "puzzle_count": 10}

    def test_no_progression_on_a_guard_the_pool_gave_back(self):
        if not _guardable():
            self.skipTest(NOTHING_GUARDED)
        failures = []
        for seed in seed_span():
            test = _generate(self.OPTIONS, seed)
            world = test.multiworld.worlds[test.player]
            asked = set(rules.unproven_locations(
                world.plan, world.options.ability_locks.value))
            distribute_items_restrictive(test.multiworld)
            for location in test.multiworld.get_locations(test.player):
                item = location.item
                if (location.name in asked and item is not None
                        and item.classification
                        & ItemClassification.progression):
                    failures.append("  seed %d: %s @ %s"
                                    % (seed, item.name, location.name))
        self.assertEqual([], failures,
                         "progression on a guard the pool gave back:\n"
                         + "\n".join(failures))


class TestTheOpeningCountsOnlyUsableChecks(unittest.TestCase):
    """The opening floor is measured in checks the fill may actually use.

    pool._free_checks decides whether the opening needs another starting
    ability. It used to count guarded parts, which may hold filler only, so an
    opening built on Medicine Cabinet's eight guarded no-ability parts read as
    full, nothing was granted, and the draw then had to be thrown away. That
    was 64% of first draws at 15 puzzles, base game, measured 2026-09-23.
    """

    def test_a_guarded_part_is_not_a_free_check(self):
        """Every level that still has a guard, holding every ability.

        Written against all guarded levels rather than one example, because
        the examples kept getting proven out from under it on 2026-09-23
        (Medicine Cabinet, Lunch Tray, First Aid Kit, DLC2 Boss). Holding
        everything, a guarded part is the only thing that can differ.
        """
        from .. import pool, slots
        everything = frozenset(data.ALL_ABILITIES)
        guarded = [l for l in data.LEVELS if l.unproven_parts]
        if not guarded:
            # Every level proven by play on 2026-09-23. The check stays so a
            # guard that comes back is covered from its first day; it was
            # seen to fail on the bug (reverting the skip counted Cat Eyes'
            # guarded eyes as free) before the last guard was lifted.
            self.skipTest("no guarded level left - nothing to count")
        for level in guarded:
            expected = (level.solution_count
                        + sum(1 for p in level.parts
                              if p not in level.unproven_parts))
            self.assertEqual(
                expected,
                pool._free_checks([slots.Slot(level, 1, -1)], everything),
                "%s: a guarded part was counted as a free check"
                % level.level_id)

    def test_redraws_stay_rare_on_the_smallest_runs(self):
        """Pinned seeds, so the count is exact rather than a rate.

        Measured 2026-09-23 on seeds 1 to 40: 9 redrawn at 15 puzzles base
        (about 25 before the fix) and 5 with both DLCs, after 28 audit levels
        were guarded (it was 6 and 2 before that). The ceilings leave a
        little room for honest table changes and none for the old bug.
        """
        for options, ceiling in (
                ({"puzzle_count": 15}, 12),
                ({"puzzle_count": 15, "cupboards_and_drawers": True,
                  "seeing_stars": True}, 8)):
            redrawn = sum(
                1 for seed in range(1, 41)
                if _generate(options, seed).multiworld.worlds[1]
                .draw_attempts > 1)
            self.assertLessEqual(redrawn, ceiling,
                                 "%s: %d of 40 first draws redrawn"
                                 % (options, redrawn))


class TestTheGuardIsOffWhenThereIsNothingToGuard(bases.ALTTLTestBase):
    """Ability locks off means every requirement is empty.

    No group can then need strictly less than its level, so there is no
    understatement to hedge against and the guard costs nothing. This falls out
    of the subset test rather than being special-cased, and that is exactly why
    it is worth pinning: a future rewrite that starts distrusting locations by
    NAME instead of by requirement would quietly excise a chunk of a
    locks-off seed's item pool for no reason at all.
    """

    options = {"ability_locks": False}

    def test_nothing_is_unproven_without_ability_locks(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual(frozenset(), world.unproven_locations)

    def test_the_helper_agrees(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual([], rules.unproven_locations(world.plan, False))
