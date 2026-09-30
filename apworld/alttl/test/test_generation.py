"""Generation and pool tests, one seed each.

The categories mirror cw4's, chosen because each one caught a real bug there:
the pool must be zero-sum against the locations, and anything the mod
dispatches on by string must be pinned here.

WHAT THIS FILE DOES NOT PROVE: that a configuration fills. Constructing the
class does run a fill, so a hard failure surfaces here - but on ONE seed, and
on 2026-09-02 every class in this file was green while the fill was broken for
nine configurations including the default. Seed-dependent behaviour belongs in
test_fill_stress.py, which sweeps the option surface across many seeds. Assert
seed-independent facts here, and put anything probabilistic there.
"""

from . import bases
from .. import data
from .. import items
from .. import locations
from .. import options as apoptions
from .. import pool
from .. import slots


def _addressed(test):
    return [l for l in test.multiworld.get_locations(test.player)
            if l.address is not None]


class TestDefaults(bases.ALTTLTestBase):
    options = {}

    def test_pool_is_zero_sum(self):
        """Every addressed location gets exactly one item."""
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))

    def test_the_run_is_the_default_length(self):
        """The plan holds exactly puzzle_count puzzles.

        Read from the option rather than written here as a number. It was 79
        as a literal, and when the default moved to 70 this failed while
        testing nothing that had broken - the name even said "full length",
        which stopped being what the default meant.
        """
        world = self.multiworld.worlds[self.player]
        self.assertEqual(apoptions.PuzzleCount.default, len(world.plan))
        # 13, not 70/5 and no longer 14. Packs are UNIFORM now - the ramp that
        # widened them as the run went on is gone, because a pack that varies
        # is not the guarantee a pack is supposed to be. At the default 70 the
        # layout is 5 open free then thirteen packs of 5; the size grows
        # instead, uniformly, when the cap demands it, which is what turns a
        # 79-puzzle run into blocks of 6. See items.pack_boundaries.
        self.assertEqual(13, world.pack_total)

        # And uniform means uniform: every pack the same but the remainder.
        from .. import items
        bounds = items.pack_boundaries(79, 4)
        sizes = [bounds[i] - bounds[i - 1] for i in range(1, len(bounds))]
        self.assertEqual(1, len(set(sizes[:-1])),
                         f"packs are not uniform: {sizes}")
        self.assertLessEqual(sizes[-1], sizes[0],
                             f"the last pack is not a remainder: {sizes}")

    def test_every_ability_in_the_pool_gates_something(self):
        world = self.multiworld.worlds[self.player]
        needed = set()
        for slot in world.plan:
            needed |= slot.level.abilities
        for ability in world.live_abilities:
            self.assertIn(ability, needed, f"{ability} gates nothing")

    def test_every_ability_a_level_needs_exists(self):
        """The converse, and the one that matters: a level whose ability is
        missing from the pool can never be finished."""
        world = self.multiworld.worlds[self.player]
        held = set(world.live_abilities) | set(world.starting_abilities)
        for slot in world.plan:
            for ability in slot.level.abilities:
                self.assertIn(
                    ability, held,
                    f"{slot.level.level_id} needs {ability}, not in the pool")

    def test_the_campaign_is_actually_reachable(self):
        """The defaults must be able to draw an ordinary campaign puzzle.

        THIS TEST REPLACES ITS OWN OPPOSITE. It used to assert that a base
        level appears ONLY when it supplies a gap ability, which was true and
        was the documented design - and the consequence, uncounted for months,
        was that 57 of the 69 campaign puzzles could never be drawn at all.
        Pass 2 fills by source, base was not a source, so the mechanic-coverage
        reserve was the only door and it only ever opens for four abilities.

        Now base_weight defaults to 10 and this asserts the opposite: at least
        one campaign puzzle that serves no gap ability is in a default run.
        """
        world = self.multiworld.worlds[self.player]
        gaps = set(data.gap_abilities(slots._eligible(world.options)))
        ordinary = [s.level.level_id for s in world.plan
                    if s.level.source == "base" and not (s.level.abilities & gaps)]
        self.assertTrue(
            ordinary,
            "no campaign puzzle outside the gap abilities was drawn; "
            "base_weight may have stopped reaching pass 2")

    def test_no_one_shot_level_repeats(self):
        world = self.multiworld.worlds[self.player]
        seen = set()
        for slot in world.plan:
            if slot.level.repeatable:
                continue
            self.assertNotIn(slot.level.level_id, seen)
            seen.add(slot.level.level_id)

    def test_generators_respect_the_hard_cap(self):
        world = self.multiworld.worlds[self.player]
        counts = {}
        for slot in world.plan:
            counts[slot.level.level_id] = counts.get(slot.level.level_id, 0) + 1
        for level_id, n in counts.items():
            self.assertLessEqual(n, data.MAX_GENERATOR_INSTANCES, level_id)

    def test_generator_slots_have_a_seed_and_others_do_not(self):
        world = self.multiworld.worlds[self.player]
        for slot in world.plan:
            if slot.level.repeatable:
                self.assertGreater(slot.seed, 0, slot.level.level_id)
            else:
                self.assertEqual(-1, slot.seed, slot.level.level_id)

    def test_repeated_generators_get_different_seeds(self):
        """Two instances of one generator must be two different puzzles.

        A shared seed would build the same layout twice, which is the one thing
        a repeat is not supposed to be. Drawn without replacement per level in
        slots.draw, so this is a guarantee rather than a probability.

        Note this is NOT the bug that produced identical envelope levels in the
        0.3.0 playtest: there the generation was fine and the MOD dropped the
        baked seed at launch, falling back to the generator's stock layout.
        Pinning it here keeps the two halves from being confused again.
        """
        world = self.multiworld.worlds[self.player]
        by_level = {}
        for slot in world.plan:
            if not slot.level.repeatable:
                continue
            by_level.setdefault(slot.level.level_id, []).append(slot.seed)

        for level_id, seeds in by_level.items():
            self.assertEqual(len(seeds), len(set(seeds)),
                             "%s reused a seed across instances: %r"
                             % (level_id, seeds))

    def test_a_drawer_cannot_be_emptied_before_it_opens(self):
        """Contents of a drawer must inherit the drawer's ability.

        The 0.3.0 logic said Tool Drawer's 47 draggables needed no items at all,
        while the game disabled the DrawerController until Drawer arrived -
        so the card read as playable and the drawer would not open. The sweep
        had recorded dependsOn: [] on every controller.

        Asserted on the derived requirements rather than on levels.json, because
        the edge only matters once it has propagated through names.json into the
        part locations. If this fails with the JSON already fixed, names.json
        needs regenerating: ALTTL_WRITE_GOLDEN=1 dotnet test.
        """
        # The groups whose objects live IN the drawer.
        #
        # THE CHALK JIGSAWS USED TO BE EXCLUDED HERE and the exclusion was
        # wrong. The comment said they "are assembled on the desk, so
        # requiring Drawer for them would mark a card blocked when it is
        # playable" - and nothing was ever observed to support it.
        # docs/history/verification-log.md records the opposite: "the drawer test
        # written alongside the fix was wrong... The test was corrected to
        # match the implementation, not the other way round."
        #
        # Settled by play on 2026-09-22, holding Jigsaw with Drawer withheld:
        #
        #   "the rest of the chalk pieces are behind the drawer, so i need to
        #    be able to close it to get to them... the ones i was able to get
        #    to just happened to be besides or inside the drawer"
        #
        # So all seven need Drawer. WHICH ones a player can touch is an
        # accident of where they sit relative to a drawer frozen open, and the
        # group cannot be finished either way. The mod reported every chalk
        # group blocked=0 dimmed=0 while this was true - the gate is
        # OCCLUSION, which no interactability census can see.
        #
        # The principle the old comment got backwards, kept because it is
        # right: a missing edge makes a seed unwinnable and a spurious one
        # only makes a card look busier. That argues for sweeping ambiguous
        # cases IN, not leaving them out.
        contents = {
            ("NeatStreak_Tool Drawer", "Draggables"),
            ("NeatStreak_Tool Drawer", "Containables"),
            ("NeatStreak_Bathroom Drawer", "Draggables"),
            ("NeatStreak_Bathroom Drawer", "Bottle"),
            ("NeatStreak_Bathroom Drawer", "Indexable"),
            ("NeatStreak_Paper Plane Supplies", "Draggables"),
            ("NeatStreak_Paper Plane Supplies", "Containables"),
            ("NeatStreak_Paper Plane Supplies", "Chalk"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Blue"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Green"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Mint"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Pink"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Purple"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Red"),
            ("NeatStreak_Paper Plane Supplies", "Chalk Yellow"),
            # ("Workbench", "Draggables For Targets") left on 2026-09-24:
            # droha played Workbench and that check never fired, so it is
            # notALocation and has no part requirement left to check.
        }

        seen = set()
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                if (level.level_id, part) not in contents:
                    continue
                seen.add((level.level_id, part))
                self.assertIn("Drawer", abilities,
                              "%s / %s can be done without opening the drawer"
                              % (level.level_id, part))

        self.assertEqual(contents, seen,
                         "a drawer-contents group is missing from the table")

    def test_slot_data_matches_the_rules(self):
        """The contract: the mod's marker cannot disagree with the generator,
        because both read the same computation."""
        world = self.multiworld.worlds[self.player]
        payload = world.fill_slot_data()
        self.assertEqual(world.requirements, payload["requirements"])
        self.assertEqual(len(world.plan), len(payload["slots"]))
        for ability in payload["abilities"]:
            self.assertIn(ability, world.live_abilities)

    def test_hints_carry_a_position(self):
        world = self.multiworld.worlds[self.player]
        hint_data = {}
        world.extend_hint_information(hint_data)
        entries = hint_data[self.player]
        self.assertTrue(entries)
        for text in entries.values():
            self.assertTrue(text.startswith(("Opening, puzzle ", "Pack "))
                            or "end of the track" in text, text)

    def test_the_position_names_the_track_section(self):
        """The text a player reads in a hint, pinned to the value not the shape.

        Named the way the level select titles its sections - "Opening", then
        "Pack 1", "Pack 2" - so a hint points at something on the screen. It
        read "Ch.2 Level 3" from vanilla's chapter sizes until 2026-09-28,
        which a run's track has never shown.
        """
        bounds = [5, 10, 15, 18]
        self.assertEqual("Opening, puzzle 1", pool._pack_and_position(0, bounds))
        self.assertEqual("Opening, puzzle 5", pool._pack_and_position(4, bounds))
        self.assertEqual("Pack 1, puzzle 1", pool._pack_and_position(5, bounds))
        self.assertEqual("Pack 2, puzzle 5", pool._pack_and_position(14, bounds))
        self.assertEqual("Pack 3, puzzle 3", pool._pack_and_position(17, bounds))
        # Past the last boundary cannot happen for a planned slot; it still
        # reads as something rather than raising.
        self.assertEqual("puzzle 19", pool._pack_and_position(18, bounds))

    def test_every_hint_position_matches_the_seeds_packs(self):
        """Every slot's hint names the block slot_data puts it in."""
        world = self.multiworld.worlds[self.player]
        bounds = pool.slot_data(world)["pack_boundaries"]
        hint_data = {}
        world.extend_hint_information(hint_data)
        entries = hint_data[self.player]
        for index, slot in enumerate(world.plan):
            block = next(k for k, end in enumerate(bounds) if index < end)
            want = "Opening" if block == 0 else f"Pack {block}"
            name = locations.names_for(slot.level, slot.instance)[0]
            address = locations.LOCATION_NAME_TO_ID[name]
            self.assertTrue(entries[address].startswith(want),
                            (index, entries[address], bounds))


class TestNoArchive(bases.ALTTLTestBase):
    """Jigsaw exists only in event packs, so it must prune rather than break."""

    options = {"archive_weight": 0, "archive_packs": []}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))

    def test_jigsaw_is_pruned_not_orphaned(self):
        world = self.multiworld.worlds[self.player]
        for slot in world.plan:
            self.assertNotEqual("archive", slot.level.source)
        self.assertNotIn("Jigsaw", world.live_abilities)


class TestGeneratorsOnly(bases.ALTTLTestBase):
    """The thinnest legal run: no archive, no mechanic coverage."""

    # base_weight too, or the name stops being true - the campaign became a
    # rollable source in 2026-09-09 and this run is meant to have exactly one.
    options = {"mechanic_coverage": 0, "archive_weight": 0, "base_weight": 0,
               "archive_packs": []}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))

    def test_only_generators_remain(self):
        world = self.multiworld.worlds[self.player]
        for slot in world.plan:
            self.assertEqual("generator", slot.level.source)

    def test_the_four_gap_abilities_are_gone(self):
        world = self.multiworld.worlds[self.player]
        for ability in data.gap_abilities(slots._eligible(world.options)):
            self.assertNotIn(ability, world.live_abilities)


class TestBothSourceWeightsZero(bases.ALTTLTestBase):
    """A yaml that asks for nothing must still generate.

    All THREE weights, since the campaign became a source. With only two
    zeroed this stopped exercising the fallback it was written for and quietly
    became an ordinary base-weighted run.
    """

    options = {"generator_weight": 0, "archive_weight": 0, "base_weight": 0}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestTinyRun(bases.ALTTLTestBase):
    """The smallest legal run: 10 puzzles since 2026-09-25 (15 from
    2026-09-23, 8 before that)."""

    options = {"puzzle_count": 10, "pack_size": 5, "levels_to_beat": 40,
               "stars_to_collect": 300}

    def test_the_floor_is_ten(self):
        from .. import options as apoptions
        self.assertEqual(10, apoptions.PuzzleCount.range_start)

    def test_goal_is_clamped_to_what_exists(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual(10, len(world.plan))
        self.assertLessEqual(world.levels_to_beat, 10)

    def test_the_star_goal_is_clamped_too(self):
        """Both counts are clamped, not just the one in use.

        slot_data carries both whichever goal is set, so an unclamped
        stars_to_collect would reach the mod as a target it can never hit -
        and on the goal the player is not even playing, which is exactly
        the kind of wrong number nobody looks at.
        """
        world = self.multiworld.worlds[self.player]
        solutions = [n for n in world.location_names_in_use if " - Solution" in n]
        self.assertEqual(len(solutions), world.stars_total)
        self.assertEqual(world.stars_total, world.stars_to_collect)

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestTheFloorIsTwoFullPacks(bases.ALTTLTestBase):
    """droha, 2026-09-25: the minimum is 10, two full packs at pack size 5."""

    options = {"puzzle_count": 10, "pack_size": 5}

    def test_the_run_opens_five_then_one_pack_of_five(self):
        from .. import pool
        world = self.multiworld.worlds[self.player]
        self.assertEqual(10, len(world.plan))
        self.assertEqual([5, 10], pool.slot_data(world)["pack_boundaries"])

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestStarGoal(bases.ALTTLTestBase):
    """droha, 2026-09-28: the star goal counts stars, "it's number of
    solutions" - not puzzles with every check done."""

    options = {"goal": "collect_stars", "stars_to_collect": 12}

    def test_the_goal_is_actually_stars(self):
        """Guards against the star configurations being green for the wrong
        reason: a `goal` that silently failed to apply would leave every star
        test passing while testing the beaten goal twice."""
        world = self.multiworld.worlds[self.player]
        self.assertTrue(world.goal_is_stars)
        self.assertEqual(12, world.stars_to_collect)

    def test_the_payload_says_so(self):
        from .. import pool
        payload = pool.slot_data(self.multiworld.worlds[self.player])
        self.assertEqual("collect_stars", payload["goal"])
        self.assertEqual(12, payload["stars_to_collect"])

    def test_one_star_event_beside_every_solution(self):
        world = self.multiworld.worlds[self.player]
        solutions = [n for n in world.location_names_in_use if " - Solution" in n]
        stars = [l for l in self.multiworld.get_locations(self.player)
                 if l.name.endswith(" (Star)")]
        self.assertEqual(sorted(s + " (Star)" for s in solutions),
                         sorted(l.name for l in stars))
        self.assertTrue(all(l.address is None and l.item.name == items.STAR_TOKEN
                            for l in stars))

    def test_the_credits_count_stars_not_beaten_puzzles(self):
        from BaseClasses import CollectionState
        world = self.multiworld.worlds[self.player]
        done = self.multiworld.completion_condition[self.player]
        state = CollectionState(self.multiworld)
        state.collect(world.create_item(items.CREDITS_ITEM), True)
        for _ in range(len(world.plan)):
            state.collect(world.create_event(items.BEATEN_TOKEN), True)
        for _ in range(11):
            state.collect(world.create_event(items.STAR_TOKEN), True)
        self.assertFalse(done(state), "every puzzle beaten and 11 of 12 stars")
        state.collect(world.create_event(items.STAR_TOKEN), True)
        self.assertTrue(done(state))

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestTheOldStarGoalNameStillLoads(bases.ALTTLTestBase):
    """0.4.3 yamls say `goal: star_levels`; it is an alias of collect_stars."""

    options = {"goal": "star_levels"}

    def test_it_is_the_star_goal(self):
        self.assertTrue(self.multiworld.worlds[self.player].goal_is_stars)


class TestNoAbilityLocks(bases.ALTTLTestBase):
    options = {"ability_locks": False}

    def test_no_ability_items_exist(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual([], world.live_abilities)
        names = {i.name for i in self.multiworld.itempool}
        for ability in data.ABILITIES:
            self.assertNotIn(ability, names)

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestTheFillerSplit(bases.ALTTLTestBase):
    """droha, 2026-09-28: each trap its own share of the filler, then the
    rest split evenly between more Hint Pages and Background Reset Tokens."""

    def _counts(self):
        names = [i.name for i in self.multiworld.itempool]
        return {name: names.count(name) for name in (
            items.CAT_TRAP, items.BACKGROUND_TRAP, items.HINT_PAGE,
            items.BACKGROUND_RESET, items.SKIP)}

    def test_each_trap_is_its_share_of_the_same_filler(self):
        from .. import pool
        world = self.multiworld.worlds[self.player]
        got = self._counts()
        filler = (got[items.CAT_TRAP] + got[items.BACKGROUND_TRAP]
                  + got[items.HINT_PAGE] + got[items.BACKGROUND_RESET])
        self.assertEqual(filler * 15 // 100, got[items.CAT_TRAP])
        self.assertEqual(filler * 15 // 100, got[items.BACKGROUND_TRAP])
        self.assertLessEqual(got[items.HINT_PAGE], pool.available_hint_pages(world))

    def test_the_rest_is_hint_pages_and_tokens_evenly(self):
        from .. import pool
        world = self.multiworld.worlds[self.player]
        got = self._counts()
        pages = pool.available_hint_pages(world)
        floor = pages * 50 // 100
        extra_hints = got[items.HINT_PAGE] - floor
        tokens = got[items.BACKGROUND_RESET]
        self.assertGreater(tokens, 0, "a default seed should hold tokens")
        if got[items.HINT_PAGE] < pages:
            # Not capped by the pages: an even split, the odd one a hint.
            self.assertIn(extra_hints - tokens, (0, 1))

    def test_no_other_filler_is_minted(self):
        from .. import pool
        world = self.multiworld.worlds[self.player]
        self.assertEqual([items.BACKGROUND_RESET] * 3, pool.filler_sequence(world, 3))

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestNoBackgroundTraps(bases.ALTTLTestBase):
    options = {"background_trap_chance": 0}

    def test_none_are_minted(self):
        names = [i.name for i in self.multiworld.itempool]
        self.assertNotIn(items.BACKGROUND_TRAP, names)
        self.assertIn(items.BACKGROUND_RESET, names)


class TestHintPagesStopAtThePages(bases.ALTTLTestBase):
    """A short run with no traps: more filler than pages, so the overflow of
    the even split is tokens, never a Hint Page with nothing to open."""

    options = {"puzzle_count": 10, "cat_trap_chance": 0, "background_trap_chance": 0,
               "hint_coverage": 100}

    def test_hint_pages_never_exceed_the_pages(self):
        from .. import pool
        world = self.multiworld.worlds[self.player]
        names = [i.name for i in self.multiworld.itempool]
        self.assertEqual(pool.available_hint_pages(world), names.count(items.HINT_PAGE))

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestAllTraps(bases.ALTTLTestBase):
    options = {"cat_trap_chance": 100}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestMaximumCoverage(bases.ALTTLTestBase):
    """4 exhausts Drawer's entire supply of four levels; 6 cannot be met at
    all and must degrade rather than fail."""

    options = {"mechanic_coverage": 6}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestTheReserveLeavesRoomOnAShortRun(bases.ALTTLTestBase):
    """The mechanic reserve must not consume every slot of a small seed.

    THIS IS THE 0.3.2 RELEASE-GATE REGRESSION, pinned. Reserving one level per
    ability sounds harmless and is, at length; on the smallest legal run it
    demanded twelve abilities plus extra copies of the gap four from eight
    slots, could never satisfy that, and spent all eight trying - picking at
    every step whichever level covered the MOST abilities, which is the most
    elaborate hand-made puzzle available.

    The result generated and passed every unit test, because a mechanic being
    PRESENT was all anything asked. What it could not do was be played: the
    e2e deadlocked at 2 of 8, waiting on four abilities behind levels it could
    not reach, and 11 of 21 checks failed where 0.3.1 scored 21/21.

    So the assertion is about BALANCE, not presence: pass 2's weighted draw
    has to get a share of the run, because that is where the ordinary,
    single-mechanic levels come from.
    """

    # The smallest legal run, which is 10 since 2026-09-25. The regression
    # below was found at the old floor of 8.
    options = {"puzzle_count": 10, "levels_to_beat": 10}

    def test_the_run_is_not_all_reserve_levels(self):
        levels = [s.level for s in self.multiworld.worlds[self.player].plan]

        # Half the run is the cap, so at least half must come from elsewhere.
        # Counted as "levels needing three or more abilities", which is what
        # the reserve reaches for and what a short run cannot bootstrap from.
        elaborate = [l for l in levels if len(l.abilities) >= 3]
        self.assertLessEqual(
            len(elaborate), len(levels) // 2,
            f"{len(elaborate)} of {len(levels)} levels need 3+ abilities: "
            f"{[l.level_id for l in levels]}")


#: A campaign-heavy run, so the draw reaches the puzzles that award achievements.
_ACHIEVEMENT_RUN = {"puzzle_count": 70, "base_weight": 100, "generator_weight": 1,
                    "archive_weight": 0, "mechanic_coverage": 0}


class TestAchievementsAreOffByDefault(bases.ALTTLTestBase):
    options = dict(_ACHIEVEMENT_RUN)

    def test_no_achievement_check_exists(self):
        world = self.multiworld.worlds[self.player]
        self.assertTrue(any(slot.level.achievements for slot in world.plan),
                        "the run drew no puzzle with an achievement, so this "
                        "test proves nothing")
        self.assertFalse([n for n in world.location_names_in_use
                          if " - Achievement: " in n])
        self.assertFalse([n for n in world.requirements if " - Achievement: " in n])


class TestAchievements(bases.ALTTLTestBase):
    options = dict(_ACHIEVEMENT_RUN, achievements=True)

    def _awarded(self):
        world = self.multiworld.worlds[self.player]
        return world, [n for n in world.location_names_in_use if " - Achievement: " in n]

    def test_every_drawn_puzzle_with_an_achievement_has_its_check(self):
        world, awarded = self._awarded()
        want = [locations.achievement_name(slot.level, slot.instance, display)
                for slot in world.plan for _id, display in slot.level.achievements]
        self.assertTrue(want, "the run drew no puzzle with an achievement")
        self.assertEqual(sorted(want), sorted(awarded))
        self.assertEqual(frozenset(awarded), world.achievement_locations)

    def test_an_achievement_needs_its_whole_level(self):
        """level.abilities, before bypasses: the widest set, never less than
        the level's Beaten event asks."""
        world, _awarded = self._awarded()
        for slot in world.plan:
            beaten = world.requirements[locations.beaten_name(slot.level, slot.instance)]
            for _id, display in slot.level.achievements:
                req = world.requirements[
                    locations.achievement_name(slot.level, slot.instance, display)]
                self.assertEqual(sorted(slot.level.abilities), req["abilities"])
                self.assertEqual(beaten["packs"], req["packs"])
                self.assertTrue(set(beaten["abilities"]) <= set(req["abilities"]))

    def test_an_achievement_can_hold_progression(self):
        """A check like any other once the option is on (droha, 2026-09-29:
        "then what's the point of enabling them?"); a Skip sends it, so a
        hard one never has to block a run."""
        world, awarded = self._awarded()
        progression = world.create_item(items.PROGRESSIVE_PACK)
        for name in awarded:
            location = self.multiworld.get_location(name, self.player)
            self.assertTrue(location.item_rule(progression), name)

    def test_their_ids_come_after_every_other_location(self):
        ids = locations.LOCATION_NAME_TO_ID
        last_other = max(i for n, i in ids.items() if " - Achievement: " not in n)
        first_awarded = min(i for n, i in ids.items() if " - Achievement: " in n)
        self.assertEqual(last_other + 1, first_awarded)
        self.assertIn("Achievements", locations.LOCATION_NAME_GROUPS)

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))
