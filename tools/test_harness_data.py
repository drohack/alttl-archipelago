"""The harness's data layer, tested against a real seed. No game needed.

THE HARNESS HAS THREE LAYERS and only one of them was ever tested:

  1. DATA   - read the seed: slots, per-location requirements, spoiler
              placements, and the map from location name to slot. Pure
              functions over files. THIS FILE.
  2. CHOICE - which slot to play next. tools/test_scheduler.py, with
              tools/mutate-scheduler.py keeping it honest.
  3. DRIVE  - boot a level and solve it. Needs the game, one level at a
              time: tools/probe-slots.py.

Layer 1 sits underneath both the others, so a fault here looks like a
scheduling bug or a mod bug. `locations_for_slots` matching by display
name is exactly that shape: a level whose display name does not line up
silently contributes no locations, and the scheduler then believes it has
no work and skips it forever.

    py -3.13 tools/test_harness_data.py
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import release_e2e as e2e

#: Which seed to read. Defaults to the gate's own output directory, but
#: SAY WHICH rather than taking whatever is lying there: these tests
#: passed for weeks against a base-game seed while the DLC run was the
#: thing failing, because out-e2e held whichever scenario ran last and
#: nothing recorded which that was.
#:
#:     ALTTL_SEED_DIR=testserver/out-dlc py -3.13 tools/test_harness_data.py
#:
#: tools/make-seed.py writes out-base and out-dlc without needing the game.
OUT = os.path.join(e2e.REPO,
                   os.environ.get("ALTTL_SEED_DIR",
                                  os.path.join("testserver", "out-e2e")))


def newest_seed():
    zips = [f for f in os.listdir(OUT) if f.endswith(".zip")] \
        if os.path.isdir(OUT) else []
    if not zips:
        raise unittest.SkipTest(
            f"no seed in {OUT} - make one with tools/make-seed.py")
    return max(zips, key=lambda f: os.path.getmtime(os.path.join(OUT, f)))


class TestReadingTheSeed(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        cls.seed = newest_seed()
        cls.plan = e2e.read_plan(OUT, cls.seed)

    def test_the_plan_has_slots_and_boundaries(self):
        self.assertTrue(self.plan["slots"])
        self.assertTrue(self.plan["boundaries"])
        for index, level_id in self.plan["slots"]:
            self.assertIsInstance(index, int)
            self.assertTrue(level_id)

    def test_every_location_carries_a_requirement(self):
        """Missing requirements read as "needs nothing", which is the
        dangerous default: the scheduler would play a gated level early and
        the mod would withhold the checks."""
        reqs = self.plan["requirements"]
        self.assertTrue(reqs)
        for name, abilities in reqs.items():
            self.assertIsInstance(abilities, list, name)

    def test_every_location_belongs_to_exactly_one_slot(self):
        """THE MAPPING IS BY DISPLAY NAME and that is fragile.

        A level whose display name does not match contributes no locations;
        slot_has_work then says it has nothing to do and the scheduler
        never plays it. Silent, and it looks like a gating bug.
        """
        where = e2e.locations_for_slots(self.plan)
        owned = [loc for locs in where.values() for loc in locs]
        self.assertEqual(len(owned), len(set(owned)),
                         "a location was claimed by two slots")

        unmapped = set(self.plan["requirements"]) - set(owned)
        self.assertFalse(unmapped,
                         f"{len(unmapped)} location(s) map to no slot, so "
                         f"their levels look empty to the scheduler: "
                         f"{sorted(unmapped)[:5]}")

    def test_every_slot_owns_at_least_one_location(self):
        where = e2e.locations_for_slots(self.plan)
        empty = [i for i in range(len(self.plan["slots"]))
                 if not where.get(i)]
        self.assertFalse(
            empty,
            f"slot(s) {empty} own no locations - the scheduler will never "
            f"see work on them: "
            f"{[self.plan['slots'][i][1] for i in empty]}")

    def test_a_renamed_dlc_level_still_maps_to_its_locations(self):
        """DLC levels do not go by their own names.

        The mod rewrites `DLC1 Trophy Cabinet` into
        `Trophy Cabinet (Cupboards and Drawers)`, and the slot-to-location
        mapping matches on the DISPLAY name. The suffix was the leading
        suspect for the DLC gate stalling at 23/25 - a slot that maps to
        nothing is invisible to the scheduler, which looks exactly like a
        stall.

        MEASURED 2026-09-21 against a real DLC seed: it maps correctly,
        and the suspicion was wrong. Kept as a named test so the next
        person does not have to suspect it twice.
        """
        where = e2e.locations_for_slots(self.plan)
        renamed = [(i, level_id)
                   for i, (_index, level_id) in enumerate(self.plan["slots"])
                   if level_id.startswith("DLC")]
        if not renamed:
            self.skipTest("not a DLC seed - point ALTTL_SEED_DIR at one")
        for i, level_id in renamed:
            self.assertTrue(where.get(i),
                            f"slot {i} ({level_id}) maps to no locations; "
                            f"its display name carries a DLC suffix")

    def test_the_dlc_seed_really_is_a_dlc_seed(self):
        """Guard against every DLC assertion passing vacuously.

        Whatever seed sits in the directory is what these tests read. For
        weeks that was whichever scenario ran last, and nothing recorded
        which - so a green DATA suite said nothing about the DLC run that
        was actually failing.
        """
        if "out-dlc" not in OUT.replace("\\", "/"):
            self.skipTest("only meaningful for the DLC seed directory")
        levels = [level_id for _i, level_id in self.plan["slots"]]
        base = [l for l in levels if not l.startswith("DLC")]
        self.assertFalse(base,
                         f"a DLC-only seed drew base-game levels: {base}")


class TestReadingTheSpoiler(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        cls.seed = newest_seed()
        cls.plan = e2e.read_plan(OUT, cls.seed)
        cls.starting, cls.placements = e2e.read_spoiler(OUT, cls.seed)

    #: Locations that are not "<level> - <part>". Credits is the goal
    #: event; it belongs to the run rather than to any one puzzle.
    EVENTS = {"Credits"}

    def test_it_finds_placements(self):
        self.assertTrue(self.placements)
        for location, item in self.placements:
            self.assertTrue(item)
            if location in self.EVENTS:
                continue
            self.assertIn(" - ", location)

    def test_every_placement_is_a_real_location(self):
        """A parse that drifts silently produces plausible nonsense."""
        known = set(self.plan["requirements"])
        strays = [loc for loc, _ in self.placements
                  if loc not in known and loc not in self.EVENTS]
        self.assertFalse(strays,
                         f"the spoiler names locations the seed does not "
                         f"have - the parser has drifted: {strays[:5]}")

    def test_the_seed_can_actually_be_cleared(self):
        """The pre-flight. If this fails the seed is unwinnable and no
        amount of harness work will finish it."""
        order, unreachable = e2e.completion_plan(OUT, self.seed, self.plan)
        self.assertFalse(unreachable,
                         f"{len(unreachable)} location(s) unreachable in "
                         f"any order: {unreachable[:5]}")
        self.assertEqual(len(self.plan["slots"]), len(order),
                         "the completion plan does not name every slot")

    def test_the_paper_plan_clears_every_slot(self):
        """judge_seed, the gate's own pre-flight, on this seed. The arrow
        session is modelled unless the seed is a locks-off (--quick) one,
        which is the only kind the gate runs without it."""
        arrow = self.plan_locks()
        clears, lines, plan = e2e.judge_seed(OUT, self.seed, arrow)
        self.assertTrue(clears, "\n".join(lines))
        self.assertTrue(plan["visits"])

    def plan_locks(self):
        return e2e.read_plan(OUT, self.seed).get("ability_locks", True)

    def test_the_seed_holds_the_skips_only_a_skip_can_replace(self):
        """What completion_plan cannot see: levels only a Skip finishes."""
        need, have = e2e.preflight_skips(OUT, self.seed, self.plan)
        self.assertGreaterEqual(have, need,
                                f"{need} level(s) need a Skip, {have} in the seed")


class TestTheAbilityTables(unittest.TestCase):

    def test_every_controller_class_maps_to_a_known_ability(self):
        e2e._load_ability_tables()
        self.assertTrue(e2e._CLASS_ABILITY)
        for cls, ability in e2e._CLASS_ABILITY.items():
            self.assertIn(ability, e2e._ABILITY_NAMES,
                          f"{cls} maps to {ability}, which is not an ability")

    def test_the_dlc_ability_is_present(self):
        e2e._load_ability_tables()
        self.assertIn("Distributing", e2e._ABILITY_NAMES)


if __name__ == "__main__":
    unittest.main(verbosity=2)
