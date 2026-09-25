"""The DLC path, tested a piece at a time.

WHY THIS FILE EXISTS. Every DLC behaviour here was, until 2026-09-20,
covered only by test_fill_stress - a 47-configuration sweep that proves
seeds fill and says nothing about what is in them - or by a fifteen-minute
release gate. Three seams had no test at all:

  * slots._eligible, the ONLY code that keys levels to DLCs, including the
    four DLC levels whose source is "generator" and which a source test
    would happily let into a base-game run.
  * bypassedAbilities and the enforced_* views, which landed the same day.
    The existing negative reachability test derives its expectation FROM
    world.requirements, so a wrong subtraction is self-consistent and
    invisible to it.
  * slot_data with a DLC switched on. The golden fixture is built with
    options={}, so data.classes_for("Distributing") had never once been
    reached through it.

These need no multiworld and no fill. They run in milliseconds.
"""

import unittest

from .. import data, items, locations, rules, slots


class _Opt:
    """One yaml option, as _eligible reads it: something with .value."""

    def __init__(self, value):
        self.value = value


class _Options:
    """The three options _eligible consults, and nothing else.

    A stub rather than a generated world: _eligible runs before any random
    call and touches exactly these three, so a real world would add a
    minute of fill to prove nothing extra.
    """

    def __init__(self, dlc1=False, dlc2=False, packs=()):
        self.cupboards_and_drawers = _Opt(dlc1)
        self.seeing_stars = _Opt(dlc2)
        self.archive_packs = _Opt(list(packs))


#: The four DLC levels the game itself marks randomizable. They are the
#: reason _eligible keys on `dlc` and never on `source`.
GENERATOR_SOURCE_DLC = {
    "DLC1 Trophy Cabinet",
    "DLC2 Water Glasses",
    "DLC2 Figurines",
    "DLC2 Bread Crusts",
}

#: Every level that declares an ability it does not actually need, with
#: the ability dropped. Pinned by name: each was established by droha
#: playing the level by hand, and "we measured this once" is not a thing
#: a future edit should be able to undo quietly.
BYPASSES = {
    "Books 3": {"Swapping"},
    "Workbench": {"Drawer"},
    "TrickOrTidy_ChocolateBars": {"Swapping"},
    "NeatStreak_Bathroom Drawer": {"Ordering"},
    "DLC1 Kitchen Hanging Tools 1": {"Drawer"},
    "DLC1 Kitchen Hanging Tools 2": {"Drawer"},
    "DLC2 Junk Drawer Transforming": {"Ordering"},
    "DLC2 Combs": {"Ordering"},
    # 2026-09-23, two locks-on runs: holding only Gadgets droha finished it
    # ("eyes appear, level completes, nothing is greyed out") with both groups
    # solved; holding only Ordering nothing could be picked up.
    "DLC2 Cat Eyes": {"Ordering"},
}


class TestTheContentGate(unittest.TestCase):
    """slots._eligible - the only thing that keys levels to a DLC."""

    def eligible(self, **kwargs):
        return {level.level_id for level in slots._eligible(_Options(**kwargs))}

    def test_no_dlc_level_survives_with_both_toggles_off(self):
        picked = self.eligible()
        leaked = {l.level_id for l in data.LEVELS
                  if l.dlc and l.level_id in picked}
        self.assertEqual(set(), leaked,
                         "a run for someone who owns neither DLC drew a DLC "
                         "level; the mod would refuse to connect")

    def test_the_generator_source_dlc_levels_are_excluded_too(self):
        """The trap this filter exists for.

        Four DLC levels carry source "generator" because the game marks
        them randomizable. A filter written against source instead of dlc
        lets DLC2 Bread Crusts into a base-game run, where it will not
        load. Named explicitly so that deleting the guard fails here
        rather than in somebody's seed.
        """
        picked = self.eligible()
        self.assertEqual(set(), GENERATOR_SOURCE_DLC & picked)
        both = self.eligible(dlc1=True, dlc2=True)
        self.assertEqual(GENERATOR_SOURCE_DLC, GENERATOR_SOURCE_DLC & both,
                         "owning both DLCs should make all four available")

    def test_each_toggle_admits_only_its_own_dlc(self):
        just_one = self.eligible(dlc1=True)
        self.assertTrue(any(data.BY_ID[i].dlc == "DLC1" for i in just_one))
        self.assertFalse(any(data.BY_ID[i].dlc == "DLC2" for i in just_one))

        just_two = self.eligible(dlc2=True)
        self.assertTrue(any(data.BY_ID[i].dlc == "DLC2" for i in just_two))
        self.assertFalse(any(data.BY_ID[i].dlc == "DLC1" for i in just_two))

    def test_both_toggles_admit_every_dlc_level(self):
        both = self.eligible(dlc1=True, dlc2=True)
        want = {l.level_id for l in data.LEVELS if l.dlc}
        self.assertEqual(want, want & both)

    def test_the_base_pool_does_not_move_when_a_dlc_is_added(self):
        """A DLC adds levels. It must never remove or alter one."""
        base = {i for i in self.eligible() if not data.BY_ID[i].dlc}
        for kwargs in ({"dlc1": True}, {"dlc2": True},
                       {"dlc1": True, "dlc2": True}):
            with self.subTest(**kwargs):
                got = {i for i in self.eligible(**kwargs)
                       if not data.BY_ID[i].dlc}
                self.assertEqual(base, got)


class TestTheBypassSubtraction(unittest.TestCase):
    """bypassedAbilities, and the two views it feeds.

    The split is the whole point: `abilities` drives the DRAW and is
    frozen by test_regression's goldens, `enforced_*` drives the
    REQUIREMENT. Subtracting in the wrong place either moves every seed
    or leaves the logic overstating what a level needs.
    """

    def test_the_bypass_table_is_exactly_the_hand_tested_eight(self):
        got = {l.level_id: set(l.abilities - l.enforced_abilities)
               for l in data.LEVELS
               if l.abilities != l.enforced_abilities}
        self.assertEqual(BYPASSES, got,
                         "the bypass list changed. Each entry means a human "
                         "played the level and proved the ability is not "
                         "needed - removing a requirement is the dangerous "
                         "direction, so a new one needs the same evidence")

    def test_enforced_abilities_is_the_declaration_minus_the_bypass(self):
        for level_id, bypassed in BYPASSES.items():
            with self.subTest(level_id):
                level = data.BY_ID[level_id]
                self.assertEqual(level.abilities - bypassed,
                                 level.enforced_abilities)

    def test_the_draw_view_still_carries_the_bypassed_ability(self):
        """Or every frozen plan in fixtures/plan-0.3.4.json moves.

        This is why the subtraction is not simply applied to `abilities`.
        """
        for level_id, bypassed in BYPASSES.items():
            with self.subTest(level_id):
                self.assertTrue(bypassed <= data.BY_ID[level_id].abilities)

    def test_no_part_requires_an_ability_the_level_bypasses(self):
        for level_id, bypassed in BYPASSES.items():
            level = data.BY_ID[level_id]
            for part, needs in level.enforced_part_abilities.items():
                with self.subTest(level_id=level_id, part=part):
                    self.assertEqual(set(), set(needs) & bypassed)

    def test_requirements_uses_the_enforced_view_not_the_draw_view(self):
        """The wiring. A correct subtraction nothing calls is worth nothing.

        Both DLC bypass shapes are covered: Kitchen Hanging Tools drops
        Drawer, Combs drops Ordering.
        """
        for level_id, bypassed in BYPASSES.items():
            level = data.BY_ID[level_id]
            plan = [slots.Slot(level=level, instance=1, seed=-1)]
            out = rules.requirements(plan, 2, True)
            solution = locations.solution_name(level, 1, 1)
            with self.subTest(level_id):
                self.assertIn(solution, out)
                self.assertEqual(
                    set(), set(out[solution]["abilities"]) & bypassed,
                    f"{solution} still demands {bypassed}, which the level "
                    f"was hand-tested not to need")

    def test_part_requirements_use_the_enforced_view_too(self):
        """The solution and the part read SEPARATE tables.

        Checking only the solution left rules.py free to read
        level.part_abilities for the parts, and a mutation doing exactly
        that went unnoticed - every one of the eight has at least one
        group that declares the bypassed ability, so this is not a
        vacuous pass.
        """
        tested = 0
        for level_id, bypassed in BYPASSES.items():
            level = data.BY_ID[level_id]
            # DLC2 Combs lost its Drawer part on 2026-09-23 (solved at load,
            # so notALocation) and has one group left: no part checks.
            if not level.has_parts:
                continue
            tested += 1
            exposed = {p for p, a in level.part_abilities.items()
                       if set(a) & bypassed}
            self.assertTrue(exposed, f"{level_id} would test nothing here")

            plan = [slots.Slot(level=level, instance=1, seed=-1)]
            out = rules.requirements(plan, 2, True)
            for part in exposed:
                name = locations.part_name(level, 1, part)
                with self.subTest(level_id=level_id, part=part):
                    self.assertIn(name, out)
                    self.assertEqual(
                        set(), set(out[name]["abilities"]) & bypassed,
                        f"{name} still demands {bypassed}")
        self.assertGreater(tested, 5)

    def test_a_level_without_a_bypass_keeps_every_ability_it_declares(self):
        """The control. A subtraction that fired on everything would pass
        every assertion above and quietly delete the logic."""
        plain = [l for l in data.LEVELS
                 if l.abilities and l.level_id not in BYPASSES][0]
        plan = [slots.Slot(level=plain, instance=1, seed=-1)]
        out = rules.requirements(plan, 2, True)
        name = locations.solution_name(plain, 1, 1)
        self.assertEqual(sorted(plain.abilities), out[name]["abilities"])

    def test_locks_off_means_no_ability_requirement_at_all(self):
        level = data.BY_ID["DLC2 Combs"]
        plan = [slots.Slot(level=level, instance=1, seed=-1)]
        out = rules.requirements(plan, 2, False)
        self.assertEqual([], out[locations.solution_name(level, 1, 1)]["abilities"])


class TestTheDlcAbility(unittest.TestCase):
    """Distributing: one ability, one level, and a nested table."""

    def test_distributing_resolves_to_its_controller_class(self):
        """data.classes_for exists because ABILITY_CLASSES alone KeyErrors
        on a DLC ability, which is what slot_data hands the mod."""
        self.assertEqual(["Distributables"], data.classes_for("Distributing"))

    def test_the_dlc_ability_table_is_nested_per_dlc(self):
        """{DLC key: {ability: [classes]}}, unlike the flat base table.

        Read flat, this yields the DLC keys themselves as ability names.
        """
        self.assertEqual(["Distributing"], data.DLC_ABILITIES)
        self.assertNotIn("DLC1", data.ALL_ABILITIES)
        self.assertNotIn("DLC2", data.ALL_ABILITIES)

    def test_every_ability_has_at_least_one_level_that_uses_it(self):
        """Including the DLC ones. test_tables asks this of the base twelve
        only, so an ability with no level would go unnoticed."""
        used = {a for level in data.LEVELS for a in level.abilities}
        for ability in data.ALL_ABILITIES:
            with self.subTest(ability):
                self.assertIn(ability, used)


class TestTheDlcIdsNeverMove(unittest.TestCase):
    """Ids are baked into every seed already generated and into saves.

    Frozen deliberately, on droha's call, while the DLC DRAW is left free:
    levels.json is still being corrected, so pinning which levels a seed
    picks would produce churn, but an id that shifts silently corrupts
    somebody's in-flight run.
    """

    def test_the_dlc_block_sits_after_credits(self):
        names = locations.ALL_NAMES
        credits_at = names.index(data.CREDITS)
        dlc_displays = tuple(l.display for l in data.LEVELS if l.dlc)
        for name in names[:credits_at]:
            self.assertFalse(name.startswith(dlc_displays),
                             f"{name} is a DLC location sitting among the "
                             f"base ids - every id after it has moved")

    def test_the_counts_are_pinned(self):
        per_dlc = {"": 0, "DLC1": 0, "DLC2": 0}
        for level in data.LEVELS:
            per_dlc[level.dlc] += (len(locations.names_for(level, 1))
                                   * level.max_instances)
        # 431 -> 427 on 2026-09-23: Medicine Cabinet's Jar Lid and the three
        # Drawer Chores drawers are solved the moment the level opens, so they
        # became notALocation. droha: "we shouldn't be sending checks for
        # opening a level".
        # 427 -> 425 on 2026-09-24: droha played Workbench and "Draggables
        # For Targets" never fired, so it is notALocation; the level is then
        # single-part, so its Tools part check (same event as Solution 1)
        # went too.
        # 425 -> 423 the same day: droha could not peel Fruit Stickers holding
        # only Tidying, so Remove Stickers depends on Match Stickers too; the
        # mutual pair is one group and the level has no part checks.
        self.assertEqual(423, per_dlc[""])
        # 146 -> 149 on 2026-09-23: DLC1 Boss's Dining Room, Parking Lot and
        # Landscape registered and solved in droha's play and were restored.
        # 149 -> 148 the same day: Kitchen Utensils Drawers' "Drawers" check
        # never fires (the level ends before both drawers can be shut), so it
        # is notALocation; its contents still need Drawer.
        # 148 -> 138: ten DLC1 drawers and cupboard doors are solved the
        # moment the level opens (tools/probe-solved-at-load.py), so they
        # are notALocation.
        self.assertEqual(138, per_dlc["DLC1"])
        # 267 -> 269 on 2026-09-23: DLC2 Boss lost its Drawer Controller (it
        # never solved, even in a full completion) and gained Locks, Compass
        # and Knives, which droha's play showed register and solve. Ids after
        # it moved; droha: "i do not care about seed ids moving ever" - a
        # version mismatch just means downloading the matching mod.
        # 269 -> 263: Material Drawers, Sticky Drawer and Combs' drawers and
        # Robots' spring pair are solved at load, so notALocation.
        # 263 -> 261: Junk Drawer Transforming's drawer can never be shut
        # before the last piece (droha, played), so it is notALocation and
        # the level is left with one merged group - no part checks.
        # 261 -> 260: Ink Bottles' GridPuzzleBase never fires on either
        # solution (droha, played both), so it is notALocation.
        self.assertEqual(260, per_dlc["DLC2"])
        self.assertEqual(822, len(locations.ALL_NAMES))

    def test_credits_is_the_last_base_id(self):
        self.assertEqual(423, locations.ALL_NAMES.index(data.CREDITS))

    def test_the_dlc_ability_item_id_is_pinned(self):
        """Appended after Hint Page, never inside the base twelve."""
        self.assertEqual(4_050_018, items.ITEM_NAME_TO_ID["Distributing"])
        base_ids = [items.ITEM_NAME_TO_ID[a] for a in data.ABILITIES]
        self.assertGreater(items.ITEM_NAME_TO_ID["Distributing"],
                           max(base_ids))


def _dlc_slot_data(options, seed):
    """slot_data from a real generated DLC run.

    The golden in test_slot_data is built with options={} - both DLCs OFF -
    so every DLC assertion over there can only ever prove a negative, and
    data.classes_for("Distributing") had never been reached through the
    payload the mod actually parses.
    """
    from Fill import distribute_items_restrictive
    from test.bases import WorldTestBase

    class _Test(WorldTestBase):
        game = "A Little to the Left"
        auto_construct = False

    _Test.options = options
    test = _Test()
    test.setUp()
    test.world_setup(seed=seed)
    distribute_items_restrictive(test.multiworld)
    return test.multiworld.worlds[test.player].fill_slot_data()


#: The gate's own DLC yaml, minus the harness-specific parts. Every
#: non-DLC source is off, so every slot must be a DLC puzzle.
DLC_ONLY = {
    "puzzle_count": 15,
    "levels_to_beat": 15,
    "cupboards_and_drawers": True,
    "seeing_stars": True,
    "cupboards_weight": 50,
    "stars_weight": 50,
    "generator_weight": 0,
    "archive_weight": 0,
    "base_weight": 0,
    "mechanic_coverage": 0,
    "archive_packs": [],
    "ability_locks": True,
}


class TestSlotDataCarriesTheDlc(unittest.TestCase):
    """What the mod actually receives for a DLC run.

    Slower than the rest of this file - it generates and fills - but still
    seconds, and it is the only place the DLC half of the handoff is
    exercised at all.
    """

    @classmethod
    def setUpClass(cls):
        cls.data = _dlc_slot_data(DLC_ONLY, 20260906)

    def test_both_dlc_flags_reach_the_mod(self):
        """The mod refuses to connect if these disagree with what is
        installed, so a dropped flag is a run nobody can play."""
        self.assertIs(True, self.data["cupboards_and_drawers"])
        self.assertIs(True, self.data["seeing_stars"])

    def test_every_slot_is_a_dlc_puzzle(self):
        got = {slot["dlc"] for slot in self.data["slots"]}
        self.assertTrue(got <= {"DLC1", "DLC2"},
                        f"a base-game level reached a DLC-only run: {got}")
        self.assertTrue(got, "no slots at all")

    def test_every_ability_a_location_needs_is_in_the_catalogue(self):
        """Otherwise the mod cannot tell which controllers to lock."""
        catalogue = set(self.data["abilities"])
        for name, req in self.data["requirements"].items():
            for ability in req["abilities"]:
                with self.subTest(name):
                    self.assertIn(ability, catalogue)

    def test_every_ability_maps_to_real_controller_classes(self):
        """The call that KeyErrors on a DLC ability if classes_for is
        bypassed - unreachable from the DLC-off golden."""
        for ability, classes in self.data["abilities"].items():
            with self.subTest(ability):
                self.assertTrue(classes, f"{ability} maps to no classes")
                self.assertEqual(sorted(data.classes_for(ability)),
                                 sorted(classes))

    def test_distributing_is_minted_exactly_when_a_level_needs_it(self):
        """The bug pool.py documents: filtering the live abilities through
        data.ABILITIES alone dropped Distributing while the rules still
        required it, leaving DLC2 Pizza unreachable.

        Stated as an invariant rather than pinned to a seed that happens
        to draw Pizza - it is the only level in the table that uses the
        ability, so a fixed seed would make this test one table edit away
        from silently testing nothing.
        """
        needed = any("Distributing" in req["abilities"]
                     for req in self.data["requirements"].values())
        present = "Distributing" in self.data["abilities"]
        self.assertEqual(needed, present,
                         "Distributing is required by a location but absent "
                         "from the catalogue" if needed else
                         "Distributing was minted for a run with no level "
                         "that uses it")

    def test_every_slot_has_a_controller_group_entry(self):
        groups = self.data["controller_groups"]
        for slot in self.data["slots"]:
            with self.subTest(slot["levelId"]):
                self.assertIn(slot["levelId"], groups)

    def test_this_seed_really_does_exercise_the_dlc_ability(self):
        """Guard against the invariant above passing vacuously.

        The gate's own seed draws DLC2 Pizza at slot 0, the only level in
        the table that uses Distributing. If a table change stops it being
        drawn here, the invariant test becomes "no level needs it, none
        was minted" - true, and worth nothing. This says so out loud.
        """
        self.assertIn("Distributing", self.data["abilities"],
                      "seed 20260906 no longer draws the one level that "
                      "uses Distributing, so the invariant test above is "
                      "now vacuous - pick a seed that does")


if __name__ == "__main__":
    unittest.main(verbosity=2)


class TestNotALocation(unittest.TestCase):
    """notALocation: a controller the level registers that is no check."""

    def test_it_is_never_a_part_but_still_gates(self):
        for level in data.LEVELS:
            with self.subTest(level=level.level_id):
                self.assertFalse(level.not_locations & set(level.controller_group))
        kitchen = data.BY_ID["DLC1 Kitchen Utensils Drawers"]
        self.assertEqual({"Drawers"}, set(kitchen.not_locations))
        self.assertEqual({"Top Drawer", "Bottom Drawer"}, set(kitchen.parts))
        for part in kitchen.parts:
            self.assertIn("Drawer", kitchen.enforced_part_abilities[part])

    def test_slot_data_names_them_for_the_run(self):
        seen = 0
        for seed in (1, 2, 3):
            sd = _dlc_slot_data(DLC_ONLY, seed)
            want = {lid: sorted(data.BY_ID[lid].not_locations)
                    for lid in sd["controller_groups"]
                    if data.BY_ID[lid].not_locations}
            self.assertEqual(want, sd["not_locations"])
            seen += len(want)
        self.assertGreater(seen, 0, "no run carried one, so this proved nothing")
