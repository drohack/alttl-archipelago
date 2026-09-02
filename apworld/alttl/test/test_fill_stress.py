"""Every option configuration must fill, on every seed - not on one lucky one.

WHY THIS FILE EXISTS. Each class in test_generation.py generates a single seed.
On 2026-09-02 that suite was fully green while the fill was in fact broken for
nine of fifteen configurations, the DEFAULT one included: re-running the same
options on other seeds failed 0/3. A one-seed test cannot see a probabilistic
failure, and a fill failure is exactly that.

So the rule here is that a configuration is only supported if it fills across a
span of seeds. Seeds are fixed rather than random, so a failure is reproducible
and bisectable rather than a flake someone re-runs until it passes.

Set ALTTL_STRESS_SEEDS to widen the span before a release; the default is small
enough to keep the ordinary test run quick.
"""

import os
import unittest

from Fill import distribute_items_restrictive
from test.bases import WorldTestBase
from test.general import setup_multiworld

from .. import data, items, slots

#: Enough to catch a per-seed failure without slowing the everyday run. The two
#: causes found on 2026-09-02 both failed within the first three seeds.
DEFAULT_SEEDS = 5

#: Fixed so a failure names a seed you can re-run directly.
FIRST_SEED = 20260902


def seed_span() -> range:
    count = int(os.environ.get("ALTTL_STRESS_SEEDS", DEFAULT_SEEDS))
    return range(FIRST_SEED, FIRST_SEED + count)


#: The whole supported option surface. Anything added to options.py belongs
#: here too - this sweep is the only thing standing between a new option and a
#: fill failure a player finds first.
CONFIGURATIONS = {
    "defaults": {},
    "no archive": {"archive_weight": 0, "archive_packs": []},
    "one pack only": {"archive_packs": ["good_tidings"]},
    "generators only": {"mechanic_coverage": 0, "archive_weight": 0,
                        "archive_packs": []},
    "both weights zero": {"generator_weight": 0, "archive_weight": 0},
    "archive heavy": {"generator_weight": 10, "archive_weight": 90},
    "no ability locks": {"ability_locks": False},
    "no starting abilities": {"starting_abilities": 0},
    "many starting abilities": {"starting_abilities": 6},
    "no guaranteed open slots": {"guaranteed_open_slots": 0},
    "max guaranteed open slots": {"guaranteed_open_slots": 10},
    "pack size 1": {"pack_size": 1},
    "pack size 2": {"pack_size": 2},
    "pack size 10": {"pack_size": 10},
    "tiny run": {"puzzle_count": 8},
    "tiny run, pack size 1": {"puzzle_count": 8, "pack_size": 1},
    "short run": {"puzzle_count": 20},
    "no mechanic coverage": {"mechanic_coverage": 0},
    "max mechanic coverage": {"mechanic_coverage": 6},
    "repeat limit 2": {"generator_repeat_limit": 2},
    "all traps": {"cat_trap_chance": 100},
    "no traps": {"cat_trap_chance": 0},
    "no skips": {"skip_count": 0},
    "max skips": {"skip_count": 20},
    "beat everything": {"levels_to_beat": 79},
    "beat one": {"levels_to_beat": 1},
    # Deliberately hostile: the thinnest content with the tightest gates.
    "worst case": {"pack_size": 1, "starting_abilities": 0,
                   "guaranteed_open_slots": 0, "mechanic_coverage": 0,
                   "archive_weight": 0, "archive_packs": [], "skip_count": 0},
}


def _generate(options: dict, seed: int) -> WorldTestBase:
    """A world set up but NOT filled, so a caller can inspect it first."""

    class _Test(WorldTestBase):
        game = "A Little to the Left"
        auto_construct = False

    test = _Test()
    test.options = options
    test.setUp()
    test.world_setup(seed=seed)
    return test


class TestEveryConfigurationFills(unittest.TestCase):
    def test_all(self):
        failures = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                try:
                    test = _generate(options, seed)
                    distribute_items_restrictive(test.multiworld)
                except Exception as error:      # noqa: BLE001 - report, do not stop
                    failures.append(f"  {name} (seed {seed}): "
                                    f"{type(error).__name__}: {error}")
        # Every failing pair in one message. A sweep that stops at the first
        # one wastes the run and hides whether the cause is broad or narrow.
        self.assertFalse(failures, "configurations that did not fill:\n"
                                   + "\n".join(failures))


class TestTheOpeningIsUsable(unittest.TestCase):
    """The symptom that diagnosed the 2026-09-02 breakage, pinned.

    Sphere 1 held 2 addressed locations against 31 progression items. Checking
    the opening directly means a future regression of that shape reports itself
    as a thin opening rather than as an opaque FillError thousands of
    placements later - the difference between a five-minute diagnosis and an
    afternoon of bisecting.
    """

    def test_the_opening_holds_every_solvable_puzzle_it_can(self):
        """The guarantee itself, not a proxy for it.

        An earlier version of this test asserted a flat count of reachable
        locations and was wrong: an 8-puzzle run may simply not CONTAIN four
        levels the player can solve with one ability, and no amount of
        reordering invents them. So assert what open_the_start actually
        promises - the opening holds as many solvable puzzles as exist to put
        there, up to the floor.
        """
        missed = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                test = _generate(options, seed)
                world = test.multiworld.worlds[test.player]
                held = set(world.starting_abilities)
                opening = min(max(world.pack_size, items.MIN_OPENING),
                              len(world.plan))

                def solvable(slot):
                    return slot.level.abilities <= held

                available = sum(1 for s in world.plan if solvable(s))
                got = sum(1 for s in world.plan[:opening] if solvable(s))
                want = min(slots.MIN_SOLVABLE_OPENING, opening, available)
                if got < want:
                    missed.append(f"  {name} (seed {seed}): {got} solvable in "
                                  f"the opening, wanted {want} "
                                  f"({available} exist in the run)")
        self.assertFalse(missed, "openings thinner than the draw allows:\n"
                                 + "\n".join(missed))

    def test_something_is_always_reachable_from_nothing(self):
        """The collapse guard.

        Archipelago rejects a seed where an empty state reaches nothing, and a
        run the player cannot start is worthless even when the fill succeeds.
        One is the floor here because a thin run may honestly only offer one.
        """
        dead = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                test = _generate(options, seed)
                state = test.multiworld.state.copy()
                reachable = sum(
                    1 for location in test.multiworld.get_locations(test.player)
                    if location.address is not None and location.can_reach(state))
                if reachable < 1:
                    dead.append(f"  {name} (seed {seed}): nothing reachable")
        self.assertFalse(dead, "runs that cannot be started:\n" + "\n".join(dead))


class TestAlongsideOtherGames(unittest.TestCase):
    """Confirmation, not the gate.

    A multiworld with other games is generally EASIER to fill than a solo one,
    because other worlds supply locations for our progression to live in. So
    this cannot replace the solo sweep above - it exists to show we play well
    with others, and would catch us doing something that only works alone.
    """

    #: Small, dependency-free worlds that need no ROM or client install. Named
    #: rather than picked at random so a failure is reproducible, and asserted
    #: to exist rather than filtered away - an earlier version silently skipped
    #: a world that was not registered, leaving a "multiworld" test that ran
    #: with only one other game in it.
    NEIGHBOURS = ("VVVVVV", "ChecksFinder", "Wargroove")

    def test_generates_with_other_worlds(self):
        from worlds.AutoWorld import AutoWorldRegister

        registered = AutoWorldRegister.world_types
        for name in self.NEIGHBOURS:
            self.assertIn(name, registered,
                          f"{name} is not registered, so this test would not "
                          f"actually be testing a multiworld")

        worlds = [registered["A Little to the Left"]]
        worlds += [registered[name] for name in self.NEIGHBOURS]

        for seed in list(seed_span())[:3]:
            multiworld = setup_multiworld(worlds, seed=seed)
            ours = [player for player, world in multiworld.worlds.items()
                    if world.game == "A Little to the Left"]
            self.assertEqual(1, len(ours))
            self.assertTrue(multiworld.get_locations(ours[0]))

            # setup_multiworld stops at pre_fill, so the fill has to be asked
            # for. Without this the test proves only that four worlds can be
            # constructed side by side, which is not what it claims.
            distribute_items_restrictive(multiworld)
            unfilled = [location for location in multiworld.get_locations()
                        if location.item is None]
            self.assertFalse(unfilled, f"seed {seed} left {len(unfilled)} "
                                       f"locations empty")


class TestPartRequirementsStayNarrow(unittest.TestCase):
    """The correctness fix, pinned.

    Part locations take their OWN controller group's abilities, not the whole
    level's. Both numbers below are measured facts about the game's data, so a
    change here means the level table or the dependency graph moved - not that
    the expectation needs updating.
    """

    def test_no_group_needs_more_than_one_ability(self):
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                self.assertLessEqual(
                    len(abilities), 1,
                    f"{level.level_id} / {part} needs {sorted(abilities)}. "
                    f"If this is real, the fill assumptions in items.py and "
                    f"the narrow-requirement design need re-measuring.")

    def test_the_measured_split_holds(self):
        free = need_one = 0
        for level in data.LEVELS:
            if not level.has_parts:
                continue
            for abilities in level.part_abilities.values():
                if abilities:
                    need_one += 1
                else:
                    free += 1
        self.assertEqual((32, 74), (free, need_one),
                         "part requirement split changed; regenerate "
                         "names.json and re-measure before accepting")

    def test_a_part_never_asks_for_more_than_its_level(self):
        """The sanity direction: narrowing must not invent a requirement."""
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                self.assertTrue(
                    abilities <= level.abilities,
                    f"{level.level_id} / {part}: {sorted(abilities)} is not a "
                    f"subset of {sorted(level.abilities)}")
