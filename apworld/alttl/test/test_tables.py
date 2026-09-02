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
        for puzzle_count in (8, 20, 40, 79):
            for pack_size in (1, 3, 4, 7, 10):
                packs = items.pack_count(puzzle_count, pack_size)
                self.assertGreaterEqual(pack_size + packs * pack_size, puzzle_count)

    def test_single_group_levels_get_no_part_locations(self):
        """On a single-group level the group check and the first solution check
        are the same event; minting both would double count."""
        for level in data.LEVELS:
            names = locations.names_for(level, 1)
            if not level.has_parts:
                self.assertEqual(level.solution_count, len(names), level.level_id)
