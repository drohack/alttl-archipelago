"""Table invariants that need no generated multiworld.

These break seeds if they change, so they are pinned rather than merely
checked. A failure here means "you are about to invalidate every seed in
flight", and the fix is almost never to update the expectation.
"""

import unittest

from .. import data, items, locations


class TestTables(unittest.TestCase):
    def test_location_names_are_unique(self):
        self.assertEqual(len(locations.ALL_NAMES), len(set(locations.ALL_NAMES)))

    def test_event_names_are_unique_and_unaddressed(self):
        self.assertEqual(len(locations.EVENT_NAMES), len(set(locations.EVENT_NAMES)))
        self.assertFalse(set(locations.EVENT_NAMES)
                         & set(locations.LOCATION_NAME_TO_ID))

    def test_item_and_location_ids_do_not_overlap(self):
        self.assertFalse(set(items.ITEM_NAME_TO_ID.values())
                         & set(locations.LOCATION_NAME_TO_ID.values()))

    def test_ids_are_contiguous_from_the_base(self):
        """Positional ids. A gap would mean something was removed rather than
        deactivated, which shifts every id after it."""
        for table in (locations.LOCATION_NAME_TO_ID, items.ITEM_NAME_TO_ID):
            ids = sorted(table.values())
            self.assertEqual(list(range(ids[0], ids[0] + len(ids))), ids)

    def test_no_display_name_collides(self):
        names = [level.display for level in data.LEVELS]
        self.assertEqual(len(names), len(set(names)))

    def test_no_part_shadows_a_solution_or_beaten_name(self):
        """A controller called "Solution 1" would collide with its own level's
        first solution location."""
        for level in data.LEVELS:
            for part in level.parts:
                self.assertFalse(part.startswith(locations.SOLUTION + " "), part)
                self.assertNotEqual(locations.BEATEN, part)

    def test_gap_abilities_are_the_measured_four(self):
        self.assertEqual({"Stacking", "Containers", "Furniture", "Jigsaw"},
                         set(data.GAP_ABILITIES))

    def test_every_ability_has_at_least_one_level(self):
        for ability in data.ABILITIES:
            self.assertTrue(data.levels_with(ability), ability)

    def test_every_archive_level_belongs_to_a_pack(self):
        """Otherwise the archive_packs option could not exclude it."""
        for level in data.ARCHIVE:
            self.assertIsNotNone(data.pack_of(level), level.level_id)

    def test_pack_count_always_covers_the_run(self):
        """Holding every pack must open every puzzle, exactly - one short
        strands the end of the track, one long mints an item that opens
        nothing."""
        for puzzle_count in (8, 20, 40, 79):
            for pack_size in (1, 3, 4, 7, 10):
                bounds = items.pack_boundaries(puzzle_count, pack_size)
                self.assertEqual(puzzle_count, bounds[-1],
                                 (puzzle_count, pack_size))
                self.assertEqual(len(bounds) - 1,
                                 items.pack_count(puzzle_count, pack_size))

    def test_packs_open_puzzles_in_strictly_growing_blocks(self):
        """The pacing contract: never narrower than the player asked for, and
        widening as the run goes on."""
        for puzzle_count in (20, 79):
            for pack_size in (1, 3, 4, 10):
                bounds = items.pack_boundaries(puzzle_count, pack_size)
                steps = [b - a for a, b in zip(bounds, bounds[1:])]
                # The final step is a remainder and may be short.
                for step in steps[:-1]:
                    self.assertGreaterEqual(step, pack_size)
                self.assertEqual(sorted(steps[:-1]), steps[:-1])

    def test_the_opening_is_never_a_single_puzzle(self):
        """A run that starts on one puzzle can be locked out by one unlucky
        ability draw, whatever guaranteed_open_slots says."""
        for pack_size in (1, 2, 4, 10):
            bounds = items.pack_boundaries(79, pack_size)
            self.assertGreaterEqual(bounds[0], min(items.MIN_OPENING, 79))

    def test_the_levels_with_no_controller_groups_are_this_exact_set(self):
        """Pinned because our logic being LOOSER than the game is the one
        failure that generating seeds can never detect.

        These two carry no puzzle controller at all, so every check on them is
        unconditionally free in logic. That is correct only because the mod
        dims objects BY controller class - a level with no controllers has
        nothing to dim, so it stays fully playable and the check really is
        free.

        RADIAL DANCE PARTY USED TO BE IN THIS SET AND IT SHOULD NEVER HAVE
        BEEN. It reported zero controllers because the sweep boots a level and
        looks once, and that level reveals its ten rings one at a time as they
        are solved. The docstring here warned about exactly that - "if the game
        registers controllers at play time that the sweep did not see" - and
        then listed the level as a member anyway, twice, across two
        investigations. It declares its ten phases in
        RadialDanceParty.dances; reading that declaration settled in one sweep
        what two rounds of counting could not.

        Note this is NOT the same as "needs no ability" - 35 levels need none,
        because their controllers are Draggables, the free baseline verb. Those
        are ordinary. These two are the ones with no puzzle content at all.

        If this set changes, do not update the expectation - find out what the
        game actually does with the new member, and start with whether it
        declares phases.
        """
        ungrouped = {level.level_id for level in data.LEVELS if not level.parts}
        self.assertEqual(
            {"Drink Glasses", "MerryMess_Presents"},
            ungrouped,
            "the set of levels with no controller groups moved; see the docstring")

    def test_every_level_offers_at_least_one_check(self):
        """A level with no checks is a card that can never be collected, and
        it would silently shrink the pool the fill has to work with."""
        for level in data.LEVELS:
            self.assertGreater(
                len(locations.names_for(level, 1)), 0, level.level_id)

    def test_no_part_needs_more_than_its_level(self):
        """The safety direction: narrowing a part must never invent a
        requirement the level as a whole does not have."""
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                self.assertTrue(abilities <= level.abilities,
                                f"{level.level_id} / {part}")

    def test_single_group_levels_get_no_part_locations(self):
        """On a single-group level the group check and the first solution check
        are the same event; minting both would double count."""
        for level in data.LEVELS:
            names = locations.names_for(level, 1)
            if not level.has_parts:
                self.assertEqual(level.solution_count, len(names), level.level_id)

class TestItemNamesAgreeAcrossLanguages(unittest.TestCase):
    """The mod matches item names as literal strings.

    A mismatch does not raise anything - the item arrives, the log says so,
    and nothing happens. The mod shipped a build matching "Puzzle Pack"
    against an item actually called "Progressive Puzzle Pack", and every pack
    it received unlocked nothing at all.
    """

    def test_the_exported_item_names_are_the_ones_the_pool_uses(self):
        exported = data._NAMES_RAW["items"]
        self.assertEqual(
            {
                "pack": items.PROGRESSIVE_PACK,
                "credits": items.CREDITS_ITEM,
                "skip": items.SKIP,
                "catTrap": items.CAT_TRAP,
                "beatenToken": items.BEATEN_TOKEN,
                "hintPage": items.HINT_PAGE,
                "backgroundTrap": items.BACKGROUND_TRAP,
            },
            exported,
            "ALTTLArchipelago.Core.ItemNames and items.py disagree; regenerate "
            "names.json with ALTTL_WRITE_GOLDEN=1 dotnet test after checking "
            "which side is right",
        )
