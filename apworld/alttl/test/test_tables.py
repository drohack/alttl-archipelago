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
        """Without DLC, the scarce mechanics are still exactly these four.

        This is the assertion the per-yaml gap set had to keep passing
        UNCHANGED. Deriving the set from the whole catalogue instead would
        have dropped Drawer and Jigsaw from it - DLC1 Trophy Cabinet is a
        drawer generator and DLC2 Bread Crusts a jigsaw one - and silently
        taken the guaranteed drawer and jigsaw puzzles away from every player
        who owns no DLC.
        """
        base = [l for l in data.LEVELS if not l.dlc]
        self.assertEqual({"Stacking", "Containers", "Drawer", "Jigsaw"},
                         set(data.gap_abilities(base)))

    def test_a_dlc_generator_removes_its_mechanic_from_the_scarce_set(self):
        """And with DLC on, the set corrects itself rather than over-reserving.

        The other half of the same rule: once a generator exists for a
        mechanic, reserving hand-made levels for it is wasted pinning.
        """
        base = [l for l in data.LEVELS if not l.dlc]
        with_dlc1 = base + [l for l in data.LEVELS if l.dlc == "DLC1"]
        with_dlc2 = base + [l for l in data.LEVELS if l.dlc == "DLC2"]

        self.assertNotIn("Drawer", data.gap_abilities(with_dlc1))
        self.assertNotIn("Jigsaw", data.gap_abilities(with_dlc2))

        # And an ability no level in the pool has is absent, not scarce:
        # reserving for it is a request the draw can never satisfy.
        self.assertNotIn("Distributing", data.gap_abilities(base))
        self.assertIn("Distributing", data.gap_abilities(with_dlc2))

    def test_every_ability_has_at_least_one_level(self):
        """ALL_ABILITIES, not the base twelve.

        Asked of ABILITIES alone this never looked at Distributing, so a
        DLC ability that lost its only level - it has exactly one - would
        have been minted as an item nothing could ever use.
        """
        for ability in data.ALL_ABILITIES:
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

    def test_every_pack_is_the_same_size(self):
        """The pacing contract, and the whole point of a pack: it is a
        guarantee about how much the run opens up, and a guarantee that varies
        is not one. The free opening is one of those blocks rather than an
        exception to them, and only the LAST may be short, because a run
        rarely divides evenly.

        WHY THIS REPLACED A WEAKER TEST. It was called
        `test_packs_open_puzzles_in_strictly_growing_blocks` and asserted only
        that the blocks never shrank - which the removed ramp ("every third
        pack is one puzzle bigger") and the uniform layout that replaced it in
        0.3.2 BOTH satisfy. So it passed unchanged across the very change it
        looks like it exists to police, and its name and docstring went on
        describing the old design. A test that cannot fail on the behaviour it
        names is not pinning it.
        """
        for puzzle_count in (20, 70, 79):
            for pack_size in (1, 3, 4, 5, 10):
                bounds = items.pack_boundaries(puzzle_count, pack_size)
                blocks = ([bounds[0]]
                          + [b - a for a, b in zip(bounds, bounds[1:])])
                where = (puzzle_count, pack_size, blocks)
                self.assertEqual(1, len(set(blocks[:-1])), where)
                # Never narrower than asked for. The size may GROW - uniformly,
                # for every block - when the pack cap demands it.
                self.assertGreaterEqual(blocks[0], pack_size, where)
                # The remainder: short is allowed, wider or empty is not.
                self.assertTrue(0 < blocks[-1] <= blocks[0], where)

    def test_the_default_run_opens_five_at_a_time(self):
        """The layout a player actually gets, spelled out, so a change to
        MIN_OPENING or to the pack cap has to come here and say so.

        Five free, then thirteen packs of five. Documented in player.yaml and
        on the setup guide, both of which said something else until 0.3.4.
        """
        bounds = items.pack_boundaries(70, 5)
        blocks = [bounds[0]] + [b - a for a, b in zip(bounds, bounds[1:])]
        self.assertEqual([5] * 14, blocks)
        self.assertEqual(13, items.pack_count(70, 5))

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

        DLC2 CORN JOINED IT ON 2026-09-17, and was checked against exactly
        that instruction rather than pasted in:

        - levelClass is plain `Level` and it declares no phases, so it is not
          a Radial Dance Party hiding behind a single-look sweep.
        - the PREFAB survey - which walks inactive children too, so it cannot
          miss a late arrival - lists one controller for it and one only:
          `Pannables Controller/Pannables`. Nothing is being revealed later,
          because there is nothing else there.

        That is the same shape as the other two, down to the controller class.
        Its four solutions are unconditionally free in logic, which is correct
        for the reason above: with no puzzle controller there is nothing for
        the mod to dim.
        """
        ungrouped = {level.level_id for level in data.LEVELS if not level.parts}
        self.assertEqual(
            {"Drink Glasses", "MerryMess_Presents", "DLC2 Corn"},
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

    def test_every_dependson_target_names_a_controller_on_its_own_level(self):
        """A dependency that resolves to nothing drops silently, and it drops
        in the UNDERSTATING direction.

        ControllerGroups.WithDependencies walks the edge list with an exact
        ordinal lookup. A target that matches no controller name is not an
        error there - it contributes nothing, so the closure stops early and
        the group's requirement comes out SMALLER than the level demands. That
        is the shape that put a Progressive Puzzle Pack on a location the
        player could not reach.

        Zero dangling targets today. This exists so the first one fails here,
        offline and in milliseconds, instead of in a seed.
        """
        for raw in data._LEVELS_RAW["levels"]:
            names = {c["name"] for c in raw["controllers"]}
            for c in raw["controllers"]:
                for target in c.get("dependsOn") or []:
                    self.assertIn(
                        target, names,
                        f"{raw['levelId']}: {c['name']} dependsOn "
                        f"{target!r}, which is not a controller on this level")

    def test_controller_names_with_surrounding_whitespace_are_these_exact_two(self):
        """A TRAP, PINNED. Do not "tidy" these names.

        Two controller names in levels.json carry a trailing space, harvested
        that way from the game's own GameObject names:

            Fridge Inside   "Stackables Tupperware Controller "
            Mirror          "Books + Box StackablesY "

        The first is also a dependsOn target, and it resolves only because the
        space is present identically on both sides of the comparison. The mod
        matches a solved controller by the same exact string against the live
        GameObject name, so trimming the table without trimming the game would
        break the match - and both failures are silent.

        So the rule is: this set may SHRINK only alongside a re-sweep that
        shows the game's own name changed. It must never grow by accident,
        which is what a whitespace-insensitive editor pass produces.
        """
        dirty = {
            (raw["levelId"], c["name"])
            for raw in data._LEVELS_RAW["levels"]
            for c in raw["controllers"]
            if c["name"] != c["name"].strip()
        }
        self.assertEqual(
            {
                ("Fridge Inside", "Stackables Tupperware Controller "),
                ("Mirror", "Books + Box StackablesY "),
            },
            dirty,
            "the set of controller names carrying surrounding whitespace "
            "moved; see the docstring before changing the expectation")

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
