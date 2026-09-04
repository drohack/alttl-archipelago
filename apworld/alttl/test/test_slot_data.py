"""The slot_data contract, pinned and exported for the mod to parse.

slot_data is the WHOLE handoff to the game: the mod cannot recompute the draw,
so anything not in this payload does not exist as far as the game is concerned.
That makes its shape a cross-language interface, and the two sides are written
in different languages by different tools.

So the producer writes a real example here and the C# side parses that exact
file in its own tests (SlotDataTests). If a field is renamed on this side, the
C# test fails - rather than the mod silently reading a null in someone's game.
Same discipline as names.json, for the same reason.

Regenerate after an intentional change with ALTTL_WRITE_GOLDEN=1.
"""

import json
import os
import unittest

from Fill import distribute_items_restrictive
from test.bases import WorldTestBase
from .. import data

#: Fixed so the example is stable; a churning golden file teaches people to
#: regenerate it without reading the diff, which defeats the point.
EXAMPLE_SEED = 20260902

def _example_path() -> str:
    """docs/data/, deliberately NOT apworld/alttl/data/.

    Everything under the world package is packaged into the shipped .apworld,
    and a 40KB test fixture has no business in a player's install. This lives
    with the other cross-language reference data instead.

    Found by walking up to the repo root, because the world is imported from a
    checkout here but from a zip in production, where __file__ has no usable
    parent on disk.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    while here != os.path.dirname(here):
        candidate = os.path.join(here, "docs", "data")
        if os.path.isdir(candidate):
            return os.path.join(candidate, "slot-data-example.json")
        here = os.path.dirname(here)
    raise RuntimeError("could not locate docs/data from " + __file__)


EXAMPLE_PATH = _example_path()


def _build() -> dict:
    class _Test(WorldTestBase):
        game = "A Little to the Left"
        options = {}
        auto_construct = False

    test = _Test()
    test.setUp()
    test.world_setup(seed=EXAMPLE_SEED)
    distribute_items_restrictive(test.multiworld)
    return test.multiworld.worlds[test.player].fill_slot_data()


class TestSlotDataExample(unittest.TestCase):
    def test_the_exported_example_is_current(self):
        built = json.dumps(_build(), indent=2, sort_keys=True)

        if os.environ.get("ALTTL_WRITE_GOLDEN") == "1":
            with open(EXAMPLE_PATH, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(built + "\n")
            return

        self.assertTrue(
            os.path.isfile(EXAMPLE_PATH),
            "slot_data_example.json missing. Regenerate with "
            "ALTTL_WRITE_GOLDEN=1 python -m unittest "
            "worlds.alttl.test.test_slot_data")
        with open(EXAMPLE_PATH, encoding="utf-8") as fh:
            self.assertEqual(built + "\n", fh.read())



class TestSlotDataShape(unittest.TestCase):
    """What the mod is entitled to assume. Each of these is something the C#
    side dispatches on, so breaking one breaks the game rather than a test."""

    @classmethod
    def setUpClass(cls):
        cls.payload = _build()

    def test_top_level_keys_are_exactly_these(self):
        self.assertEqual(
            {"slots", "pack_size", "pack_total", "pack_boundaries",
             "levels_to_beat", "ability_locks", "abilities",
             "starting_abilities", "requirements", "cat_trap_chance",
             "controller_groups"},
            set(self.payload))

    def test_every_slot_carries_what_the_mod_needs_to_launch_it(self):
        for slot in self.payload["slots"]:
            self.assertEqual({"levelId", "levelIndex", "instance", "source",
                              "seed"}, set(slot))
            self.assertIsInstance(slot["levelIndex"], int)
            self.assertIn(slot["source"], ("generator", "archive", "base"))
            # -1 means "not a generator, do not force a seed".
            self.assertTrue(slot["seed"] == -1 or slot["seed"] > 0)

    def test_generator_slots_have_a_seed_and_fixed_levels_do_not(self):
        for slot in self.payload["slots"]:
            if slot["source"] == "generator":
                self.assertGreater(slot["seed"], 0, slot["levelId"])
            else:
                self.assertEqual(-1, slot["seed"], slot["levelId"])

    def test_requirements_are_complete_entries(self):
        """Each entry is the WHOLE requirement for that location. A consumer
        must not AND it with anything else, so a missing key would silently
        loosen the in-game marker rather than tighten it."""
        for name, req in self.payload["requirements"].items():
            self.assertEqual({"packs", "abilities"}, set(req), name)
            self.assertIsInstance(req["packs"], int)
            self.assertIsInstance(req["abilities"], list)
            self.assertLessEqual(req["packs"], self.payload["pack_total"], name)

    def test_pack_boundaries_cover_the_run_exactly(self):
        """What the mod unlocks from must agree with what the logic gated on.

        Holding every pack has to open every slot - one short strands the end
        of the track behind an item that does not exist, one long mints a pack
        that reveals nothing.
        """
        bounds = self.payload["pack_boundaries"]
        self.assertEqual(len(self.payload["slots"]), bounds[-1])
        self.assertEqual(self.payload["pack_total"], len(bounds) - 1)
        self.assertEqual(sorted(bounds), bounds, "boundaries must not go backwards")
        self.assertGreaterEqual(bounds[0], 1, "the opening cannot be empty")

    def test_no_location_needs_more_packs_than_exist(self):
        for name, req in self.payload["requirements"].items():
            self.assertLessEqual(req["packs"], self.payload["pack_total"], name)

    def test_abilities_map_to_controller_classes(self):
        for ability, classes in self.payload["abilities"].items():
            self.assertTrue(classes, f"{ability} unlocks no controller class")
            for cls in classes:
                self.assertIsInstance(cls, str)

    def test_starting_abilities_are_all_real_abilities(self):
        for ability in self.payload["starting_abilities"]:
            self.assertIn(ability, self.payload["abilities"])

    def test_the_payload_is_json_round_trippable(self):
        """It travels over the wire as JSON, so anything that does not survive
        a round trip is a bug the mod would hit and the tests would not."""
        self.assertEqual(self.payload,
                         json.loads(json.dumps(self.payload)))

    def test_every_level_in_the_run_has_its_controller_map(self):
        """The mod resolves a solved controller through this map.

        A level missing from it means every group check on that level is
        silently unreportable - the event arrives, nothing matches, and no
        location is ever sent. That failure is invisible in game.
        """
        used = {slot["levelId"] for slot in self.payload["slots"]}
        groups = self.payload["controller_groups"]
        self.assertEqual(used, set(groups), "controller_groups misses a level")

    def test_no_controller_maps_to_a_group_that_is_not_a_location(self):
        """Group names must line up with the names locations were built from.

        Only checked on levels with more than one group: a single-group level
        mints no part location, because its group check and its first solution
        check are the same event.
        """
        requirements = self.payload["requirements"]
        by_level = {}
        for slot in self.payload["slots"]:
            by_level.setdefault(slot["levelId"], []).append(slot["instance"])

        for level_id, groups in self.payload["controller_groups"].items():
            names = set(groups.values())
            if len(names) < 2:
                continue
            for instance in by_level[level_id]:
                for group in names:
                    display = data.BY_ID[level_id].display
                    if instance > 1:
                        display = f"{display} #{instance}"
                    self.assertIn(f"{display} - {group}", requirements,
                                  f"{level_id} group {group} names no location")
