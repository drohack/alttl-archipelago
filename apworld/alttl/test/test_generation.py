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


def _addressed(test):
    return [l for l in test.multiworld.get_locations(test.player)
            if l.address is not None]


class TestDefaults(bases.ALTTLTestBase):
    options = {}

    def test_pool_is_zero_sum(self):
        """Every addressed location gets exactly one item."""
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))

    def test_the_run_is_the_full_length(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual(79, len(world.plan))
        # 13, not 79/4 and no longer 14. Packs are UNIFORM now - the ramp that
        # widened them as the run went on is gone, because a pack that varies
        # is not the guarantee a pack is supposed to be. The size grows instead
        # when the cap demands it: 4 open free, then thirteen packs of 6 with a
        # short last one. See items.pack_boundaries.
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
        gaps = set(data.GAP_ABILITIES)
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
        while the game disabled the DrawerController until Furniture arrived -
        so the card read as playable and the drawer would not open. The sweep
        had recorded dependsOn: [] on every controller.

        Asserted on the derived requirements rather than on levels.json, because
        the edge only matters once it has propagated through names.json into the
        part locations. If this fails with the JSON already fixed, names.json
        needs regenerating: ALTTL_WRITE_GOLDEN=1 dotnet test.
        """
        # The groups whose objects live IN the drawer. Deliberately not every
        # group in these levels: the chalk jigsaws in Paper Plane Supplies are
        # assembled on the desk, so requiring Furniture for them would mark a
        # card blocked when it is playable. A missing edge makes a seed
        # unwinnable and a spurious one only makes a card look busier, so the
        # ambiguous cases are listed rather than swept in.
        contents = {
            ("NeatStreak_Tool Drawer", "Draggables"),
            ("NeatStreak_Tool Drawer", "Containables"),
            ("NeatStreak_Bathroom Drawer", "Draggables"),
            ("NeatStreak_Bathroom Drawer", "Bottle"),
            ("NeatStreak_Bathroom Drawer", "Indexable"),
            ("NeatStreak_Paper Plane Supplies", "Draggables"),
            ("NeatStreak_Paper Plane Supplies", "Containables"),
            ("Workbench", "Draggables For Targets"),
        }

        seen = set()
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                if (level.level_id, part) not in contents:
                    continue
                seen.add((level.level_id, part))
                self.assertIn("Furniture", abilities,
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
            self.assertTrue(text.startswith("Ch.") or "end of the track" in text,
                            text)


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
        for ability in data.GAP_ABILITIES:
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
    options = {"puzzle_count": 8, "pack_size": 4, "levels_to_beat": 40}

    def test_goal_is_clamped_to_what_exists(self):
        world = self.multiworld.worlds[self.player]
        self.assertEqual(8, len(world.plan))
        self.assertLessEqual(world.levels_to_beat, 8)

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


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


class TestAllTraps(bases.ALTTLTestBase):
    options = {"cat_trap_chance": 100}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))


class TestMaximumCoverage(bases.ALTTLTestBase):
    """4 exhausts Furniture's entire supply of four levels; 6 cannot be met at
    all and must degrade rather than fail."""

    options = {"mechanic_coverage": 6}

    def test_pool_is_zero_sum(self):
        self.assertEqual(len(_addressed(self)), len(self.multiworld.itempool))
