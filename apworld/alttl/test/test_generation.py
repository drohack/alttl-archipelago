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
        # 14, not 79/4: packs widen as the run goes on, so the default run is
        # covered by fewer of them. See items.pack_boundaries.
        self.assertEqual(14, world.pack_total)

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

    def test_base_levels_only_appear_to_supply_a_mechanic(self):
        """The whole point of the two-pass draw."""
        world = self.multiworld.worlds[self.player]
        gaps = set(data.GAP_ABILITIES)
        for slot in world.plan:
            if slot.level.source == "base":
                self.assertTrue(
                    slot.level.abilities & gaps,
                    f"{slot.level.level_id} is base and serves no gap ability")

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

    options = {"mechanic_coverage": 0, "archive_weight": 0, "archive_packs": []}

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
    """A yaml that asks for nothing must still generate."""

    options = {"generator_weight": 0, "archive_weight": 0}

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
