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
                        "base_weight": 0, "archive_packs": []},
    # All three, since the campaign became a rollable source. With only two
    # zeroed this stopped exercising the fallback it was written for.
    "all weights zero": {"generator_weight": 0, "archive_weight": 0,
                         "base_weight": 0},
    "archive heavy": {"generator_weight": 10, "archive_weight": 90,
                      "base_weight": 0},
    # The campaign as the dominant source. 69 one-shot levels is the largest
    # pool in the game, so this is the configuration most likely to exhaust a
    # source and fall through to the repeatable-generator backstop.
    "campaign heavy": {"generator_weight": 10, "archive_weight": 0,
                       "base_weight": 90, "archive_packs": []},
    # And the old behaviour, which must keep generating: no campaign except
    # what mechanic coverage drags in.
    "no campaign": {"base_weight": 0},
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
    "no hint pages": {"hint_coverage": 0},
    "half hint pages": {"hint_coverage": 50},
    "every hint page": {"hint_coverage": 100},
    # Hint Pages are minted from the drawn plan rather than from a flat count,
    # so the interesting case is a run whose plan has almost no pages to sum:
    # generators-only draws the six levels with empty notepads. Full coverage
    # of nearly nothing must still fill.
    "every hint page, generators only": {
        "hint_coverage": 100, "mechanic_coverage": 0,
        "archive_weight": 0, "base_weight": 0, "archive_packs": []},
    # And the other end: every dial that competes for the same residual turned
    # up at once. Hints are taken before traps, so this is the configuration
    # where traps could be squeezed to nothing.
    "every hint page and every trap": {
        "hint_coverage": 100, "cat_trap_chance": 100, "skip_count": 20},
    "beat everything": {"levels_to_beat": 79},
    "beat one": {"levels_to_beat": 1},
    # Deliberately hostile: the thinnest content with the tightest gates.
    "worst case": {"pack_size": 1, "starting_abilities": 0,
                   "guaranteed_open_slots": 0, "mechanic_coverage": 0,
                   "archive_weight": 0, "base_weight": 0,
                   "archive_packs": [], "skip_count": 0},
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


class TestEverySeedIsWinnable(unittest.TestCase):
    """Filling is not the same as being completable.

    A seed can fill perfectly and still strand a location behind a requirement
    nothing satisfies, or place the goal out of reach. The fill only promises
    it found somewhere for each item; these assert the player can actually
    finish. Added after a randomised audit showed the committed tests checked
    placement but never reachability.
    """

    def test_every_location_is_reachable_with_everything(self):
        stranded = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                test = _generate(options, seed)
                distribute_items_restrictive(test.multiworld)

                state = test.multiworld.get_all_state()
                for location in test.multiworld.get_locations(test.player):
                    if location.address is None:
                        continue
                    if not location.can_reach(state):
                        stranded.append(f"  {name} (seed {seed}): {location.name}")
                        break
        self.assertFalse(stranded, "locations unreachable even holding every "
                                   "item in the multiworld:\n"
                                   + "\n".join(stranded))

    def test_the_goal_is_always_achievable(self):
        unwinnable = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                test = _generate(options, seed)
                distribute_items_restrictive(test.multiworld)

                state = test.multiworld.get_all_state()
                if not test.multiworld.has_beaten_game(state, test.player):
                    unwinnable.append(f"  {name} (seed {seed})")
        self.assertFalse(unwinnable, "seeds whose goal cannot be reached:\n"
                                     + "\n".join(unwinnable))


class TestTheOpeningCanAbsorbTheFirstItems(unittest.TestCase):
    """The floor that pool.decide grants abilities to reach.

    A small, starved run - few puzzles, one event pack, no mechanic coverage -
    could offer two or three ability-free checks and leave the fill nowhere to
    put its first progression items. Measured at about one generation in forty
    for that combination before the floor existed.
    """

    def test_openings_meet_the_floor_where_the_content_allows(self):
        from .. import items as apitems, pool as appool

        thin = []
        for name, options in CONFIGURATIONS.items():
            for seed in seed_span():
                test = _generate(options, seed)
                world = test.multiworld.worlds[test.player]
                if not world.options.ability_locks.value:
                    continue

                window = min(max(world.pack_size, apitems.MIN_OPENING),
                             len(world.plan))
                held = set(world.starting_abilities)
                free = appool._free_checks(world.plan[:window], held)

                # Below the floor is only acceptable when granting every
                # remaining ability would still not reach it - a run that thin
                # has nothing more to give.
                if free < appool.OPENING_FLOOR:
                    everything = held | set(world.live_abilities)
                    best = appool._free_checks(world.plan[:window], everything)
                    if best >= appool.OPENING_FLOOR:
                        thin.append(f"  {name} (seed {seed}): {free} free checks, "
                                    f"{best} reachable by granting more")
        self.assertFalse(thin, "openings left thinner than the content allows:\n"
                               + "\n".join(thin))


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

    #: The groups that legitimately need two abilities. Two shapes, both real,
    #: both found the same way - a playtester sat on a card that logic said had
    #: work available and the game gave them nothing to touch.
    #:
    #: CONTAINED: something inside a container inside a closed drawer. You
    #: cannot reach the container until the drawer opens. Added in 0.3.1 after
    #: Tool Drawer showed as completable while the game kept the drawer shut
    #: until Furniture arrived.
    #:
    #: ASSEMBLED: a group that ARRANGES what other groups BUILD. Its objects are
    #: their outputs, so it cannot begin until they are finished. droha hit this
    #: on Candy Canes: holding Ordering but not Jigsaw, the card offered work and
    #: the level had nothing on screen to interact with - the five canes to order
    #: do not exist until the five jigsaw pairs are matched.
    #:
    #: The assembled ones are detectable structurally: the arranging group's
    #: object count equals the number of assembling groups, because it acts on
    #: one output from each. Three levels have that shape; the third,
    #: GoodTidings_Cookies (Jigsaw), needs Jigsaw on both sides and so does not
    #: appear here.
    TWO_ABILITY_GROUPS = {
        # contained
        ("NeatStreak_Tool Drawer", "Containables"),
        ("NeatStreak_Bathroom Drawer", "Bottle"),
        ("NeatStreak_Bathroom Drawer", "Indexable"),
        ("NeatStreak_Paper Plane Supplies", "Containables"),
        # assembled
        ("MerryMess_CandyCanes", "Ordered"),
        ("NeatStreak_Paper Plane Supplies", "Chalk"),
        # PHASED: TupperwareNesting reveals its later groups only as the
        # earlier ones are solved, so a group late in the chain needs
        # everything earlier in it.
        #
        # The chain is READ FROM THE GAME - TupperwareNesting.GetPhaseControllers()
        # declares Stack 1 -> Stack 2 -> Tray -> Stack 3 -> Layout (Grid) ->
        # Food. An earlier version of this set was larger because the chain had
        # been GUESSED as "every later group depends on Lids and Stack 1", the
        # two that happen to register at boot. That guess was wrong twice over:
        # Lids gates nothing at all, and the shape is a chain rather than a fan.
        # It invented a Containers requirement on Stack 2, Stack 3 and Tray that
        # the game does not have, and pushed the grid group to three abilities.
        # Reading the declaration instead of inferring it took all of that back.
        ("TupperwareNesting", "(Large Square)"),
        # Phase six, the last link in the chain. Food is a plain Draggables and
        # needs nothing of its own; it carries Grids and Stacking because it
        # cannot be reached until Layout (Grid) is done.
        ("TupperwareNesting", "Food"),
    }

    def test_only_the_known_groups_need_two_abilities(self):
        """Pinned as an exact set, not as a bound.

        This used to assert no group needed more than one ability. Relaxing
        that to "no more than two" would have stopped catching the thing it
        exists to catch, so the four that legitimately need two are listed and
        anything else still fails - in either direction, since an entry
        disappearing from the set means an edge was lost.
        """
        found = set()
        for level in data.LEVELS:
            for part, abilities in level.part_abilities.items():
                self.assertLessEqual(
                    len(abilities), 2,
                    f"{level.level_id} / {part} needs {sorted(abilities)}. "
                    f"If this is real, the fill assumptions in items.py and "
                    f"the narrow-requirement design need re-measuring.")
                if len(abilities) >= 2:
                    found.add((level.level_id, part))

        self.assertEqual(self.TWO_ABILITY_GROUPS, found,
                         "the set of multi-ability groups moved; re-measure "
                         "before accepting")

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
        # Was (32, 74) before 0.3.1, then (28, 78). Four groups that needed
        # nothing at all - the loose contents of the three drawer levels and
        # Workbench's draggables - now inherit Furniture from the container
        # they live in, so they moved from free to needing something.
        #
        # Now (28, 82): TupperwareNesting gained four groups, all of which
        # need at least one ability, so only need_one moves. Those four are the
        # phased reveals the boot-time sweep never saw - the level went from 3
        # locations to 7 once droha's playtest proved they register.
        #
        # Reading the real phase chain later changed WHICH abilities several of
        # those groups need, but not whether they need any, so this split did
        # not move again.
        #
        # need_one counts "at least one", so a group going from one ability to
        # two or three does not move between these buckets; those are pinned
        # separately above.
        # Now (28, 93). Restoring the eleven locations the audit proved real -
        # Radial Dance Party's ten declared dances and TupperwareNesting's Food
        # - added eleven groups, every one of which needs at least one ability,
        # so only need_one moved. The dances need Rotating through their own
        # RadialDance components, which is also why Radial Dance Party no
        # longer carries an extraAbilities override.
        # Now (27, 94). SomethingEggstra Fridge's StandardObjects moved from
        # free to needing Containers: the level is an egg hunt, its six eggs
        # are scattered among the 24 shelf items, and the shelf cannot be made
        # tidy until they are cleared into the carton. Logic had it as a plain
        # Draggables group needing nothing, which left droha with that check as
        # the ONLY reachable one in the run and no way to earn it.
        # Now (24, 97). Three more free groups turned out to be gated in
        # practice, all the same shape as the SomethingEggstra Fridge: a
        # no-ability Draggables group with a locked group's objects scattered
        # through it, so the arrangement cannot be completed while those are
        # frozen. Breadtags (crumbs over the tags), Fridge Inside (tupperware
        # among the shelf items) and MerryMess_Crackers (crackers in the train).
        # Found by measuring object positions, after droha hit two of them.
        # Now (24, 94). TupperwareTower lost three groups and gained one back:
        # its Foundation and Falling Blocks StackableGrids are the tower's
        # mechanism, not objectives, and never raise a solved event, so they
        # were dead locations. Removing them takes their two Grids entries out
        # of need_one; the level keeps requiring Grids through extraAbilities,
        # because the falling blocks are dimmed without it.
        self.assertEqual((24, 94), (free, need_one),
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
