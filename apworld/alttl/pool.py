"""Per-seed decisions: the draw, the item pool, hint text and slot_data.

Everything here runs once per seed. A bad combination degrades into something
that still generates, and the degradation is what the tests pin.
"""

import random
from typing import Any, Dict, List, Mapping

from . import data, items, locations, rules, slots

#: The game's finales (DevTools level dump, `isCredits`): the campaign's and
#: one per DLC. A run ends on one of them, picked per seed among the base
#: game's and the DLCs it is built for (droha, 2026-09-28: "if they are
#: enabled it should randomize which credits is played at the end").
FINALES = {"": "Credits", "DLC1": "DLC1 Credits", "DLC2": "DLC2 Credits"}


def pick_finale(world) -> str:
    """This seed's finale, from a generator of its own seeded by this world's
    seed, so the pick moves nothing world.random draws - the plan, the fill,
    the regression goldens. The same seed always ends the same way."""
    o = world.options
    choices = [FINALES[""]]
    if o.cupboards_and_drawers.value:
        choices.append(FINALES["DLC1"])
    if o.seeing_stars.value:
        choices.append(FINALES["DLC2"])
    return random.Random(f"{world.multiworld.seed}:{world.player}:finale").choice(choices)

#: Checks the free opening should offer before the run is handed to the fill.
#:
#: Not a difficulty knob - it is the room the fill needs to place its first
#: progression items. Below this the placement cascade cannot start, and on a
#: small run it stalls outright. Six was the lowest value that cleared every
#: measured configuration.
OPENING_FLOOR = 6


def _pack_and_position(slot_index: int, boundaries: List[int]) -> str:
    """Where a slot sits on the track, named as the level select titles its
    sections: "Opening, puzzle 3", "Pack 2, puzzle 4". `boundaries` is
    items.pack_boundaries for the run - element k is where block k ends."""
    start = 0
    for block, end in enumerate(boundaries):
        if slot_index < end:
            section = "Opening" if block == 0 else f"Pack {block}"
            return f"{section}, puzzle {slot_index - start + 1}"
        start = end
    return f"puzzle {slot_index + 1}"


def _free_checks(plan_slice, held) -> int:
    """Addressed checks in these slots that need no further ability.

    Same SHAPE as rules.requirements for pack-free locations - a solution
    needs the whole level's abilities, a part only its own group's - but
    deliberately the DRAW view, not the enforced one.

    THE DOCSTRING USED TO SAY "mirrors what rules.requirements will say" AND
    IT DOES NOT. rules.py:49 and :71 read enforced_abilities and
    enforced_part_abilities, which have bypassedAbilities subtracted; the two
    lines below read level.abilities and level.part_abilities, which do not.
    They disagree on the 8 levels carrying a bypass (Books 3, Workbench,
    TrickOrTidy_ChocolateBars, NeatStreak_Bathroom Drawer, both DLC1 Kitchen
    Hanging Tools, DLC2 Junk Drawer Transforming, DLC2 Combs) and on the 9
    controller groups inside them.

    The direction is safe: a bypassed requirement still counted is a check
    this function does NOT call free, so it under-counts, and the opening the
    player gets is at least the opening this measured. It is left alone rather
    than corrected because the correction is not cosmetic - it would grant
    different starting abilities, so it changes generated seeds, and the
    golden in test_regression pins the DRAW and not the grant, so nothing in
    the suite would notice. Changing it is a deliberate seed-affecting call.

    If rules.py's view of a requirement ever changes, change this with it or
    the two drift further apart.
    """
    total = 0
    for slot in plan_slice:
        level = slot.level
        if level.abilities <= held:
            total += level.solution_count
        if level.has_parts:
            for part in level.part_locations:
                if level.part_abilities.get(part, frozenset()) <= held:
                    total += 1
    return total


def _plateau_escape(world, window, held):
    """The ability worth granting when no single one pays on its own.

    Returns the first half of the best-scoring PAIR, or None when even a pair
    cannot beat what is already open. See the caller for why this exists.
    """
    rest = [a for a in world.live_abilities if a not in held]
    if len(rest) < 2:
        return None

    now = _free_checks(world.plan[:window], held)
    best, score = None, now
    for i, first in enumerate(rest):
        for second in rest[i + 1:]:
            opened = _free_checks(world.plan[:window], held | {first, second})
            if opened > score:
                best, score = first, opened
    return best


def decide(world) -> None:
    """Choose what is in the run. Must run before regions or items."""
    o = world.options

    puzzle_count = o.puzzle_count.value
    pack_size = min(o.pack_size.value, puzzle_count)

    # Clamped rather than rejected: a yaml asking to beat more puzzles than
    # exist should still generate, just with the goal it can actually offer.
    # Both counts are clamped even though only one is in use, so slot_data
    # never carries a number the run cannot honour.
    world.levels_to_beat = min(o.levels_to_beat.value, puzzle_count)
    world.stars_to_collect = o.stars_to_collect.value   # clamped once drawn
    world.goal_is_stars = o.goal.value == o.goal.option_collect_stars
    world.credits_level = pick_finale(world)

    source_weights = slots.source_weights(
        o.generator_weight.value, o.archive_weight.value, o.base_weight.value,
        dlc1=o.cupboards_weight.value if o.cupboards_and_drawers.value else None,
        dlc2=o.stars_weight.value if o.seeing_stars.value else None)

    _draw(world, puzzle_count, pack_size, source_weights)


def _draw(world, puzzle_count, pack_size, source_weights) -> None:
    """The run: which puzzles, in what order, and what the player starts with."""
    o = world.options

    world.plan = slots.draw(
        world.random,
        slots=puzzle_count,
        coverage=o.mechanic_coverage.value,
        source_weights=source_weights,
        instance_cap=o.generator_repeat_limit.value,
        options=o,
    )

    # The draw can come up short only if the enabled content cannot fill the
    # run; take what there is.
    actual = len(world.plan)
    world.pack_total = items.pack_count(actual, pack_size)
    world.levels_to_beat = min(o.levels_to_beat.value, puzzle_count, actual)

    ability_locks = bool(o.ability_locks.value)

    # An ability is in the pool if and only if some drawn level needs it. A
    # Jigsaw level with no Jigsaw item would be unsolvable; a Jigsaw item with
    # no Jigsaw level is a dead item taking a slot from something useful.
    live = slots.abilities_in(world.plan) if ability_locks else set()
    # ALL_ABILITIES, so a DLC mechanic can become an item. Filtering through
    # data.ABILITIES alone would silently drop Distributing from the pool
    # while rules.py still required it, leaving DLC2 Pizza unreachable.
    world.live_abilities = [a for a in data.ALL_ABILITIES if a in live]

    count = min(o.starting_abilities.value, len(world.live_abilities))
    world.starting_abilities = world.random.sample(world.live_abilities, count) \
        if count else []

    # The opening is free, but its puzzles can still be ability-locked.
    # Reorder so at least guaranteed_open_slots of them are solvable now.
    held = set(world.starting_abilities)
    world.plan = slots.open_the_start(
        world.random, world.plan, pack_size,
        o.guaranteed_open_slots.value, held)

    # Make sure the opening offers enough to DO, not merely something.
    #
    # An earlier version only rescued an opening that was entirely locked. That
    # is too weak for a small, starved run: a 79-puzzle seed has plenty of
    # ability-free checks lying around, but 8 puzzles drawn from one event pack
    # with no mechanic coverage may offer two or three, and the fill has
    # nowhere to put its first items. Measured - that exact combination failed
    # about one generation in forty, and relaxing ANY single one of those
    # settings fixed it.
    #
    # So abilities are granted until the free opening holds OPENING_FLOOR
    # checks, or until granting stops helping. Each grant is the ability that
    # opens the most, so the fewest are needed.
    if ability_locks and world.plan:
        window = min(items.opening_size(len(world.plan), pack_size),
                     len(world.plan))
        granted = False

        for _ in range(len(world.live_abilities)):
            if _free_checks(world.plan[:window], held) >= OPENING_FLOOR:
                break

            best, gain = None, 0
            for ability in world.live_abilities:
                if ability in world.starting_abilities:
                    continue
                opened = _free_checks(world.plan[:window], held | {ability})
                if opened > gain:
                    best, gain = ability, opened

            # A PLATEAU IS NOT A CEILING. Granting one ability at a time is
            # greedy, and greedy stalls where an opening puzzle needs TWO
            # abilities: neither alone opens anything, so both look worthless
            # and the loop stops one short of the floor.
            #
            # Measured at 2 of 165 (config, seed) pairs in the stress sweep
            # once the default run shrank to 70 puzzles - "5 free checks, 6
            # reachable". Both were exactly this shape: single-step gain 5,
            # best pair 6.
            #
            # So when no single grant helps, look one further. If some PAIR
            # does better, grant the first half; the next pass then sees the
            # second half pay and takes it normally. Only runs on the stall,
            # and only over the live abilities, so the cost is a few hundred
            # set lookups once per generation.
            if best is None or gain <= _free_checks(world.plan[:window], held):
                best = _plateau_escape(world, window, held)
                if best is None:
                    break

            world.starting_abilities.append(best)
            held.add(best)
            granted = True

            # Reorder AGAIN with the wider ability set. The grant can make
            # levels elsewhere in the run solvable too, and without a second
            # pass they stay stranded behind packs while the opening keeps the
            # single puzzle that forced the grant. Caught by the stress sweep:
            # 8-puzzle runs held two solvable levels and opened with one.
            if granted:
                world.plan = slots.open_the_start(
                    world.random, world.plan, pack_size,
                    o.guaranteed_open_slots.value, held)

    achievements = bool(o.achievements.value)
    world.requirements = rules.requirements(world.plan, pack_size, ability_locks,
                                            achievements)

    # Nothing forces abilities early any more, and that is deliberate.
    #
    # An earlier version reserved early locations for two ability items to break
    # a fill deadlock. It became the leading CAUSE of fill failures instead: the
    # opening holds only a handful of locations, so reserving them left nowhere
    # for the pack items that actually open the run, and generation warned
    # "Ran out of early locations for early items" on the way to a FillError.
    # Measured 2026-09-02 - removing it took "no archive" from 2/4 seeds to 4/4
    # and "one pack only" from 1/4 to 4/4.
    #
    # The deadlock it was papering over was the over-approximated part
    # requirements in rules.py, now fixed at the source.

    world.location_names_in_use = []
    world.event_names_in_use = []
    solutions = []
    for slot in world.plan:
        world.location_names_in_use += locations.names_for(slot.level, slot.instance)
        world.event_names_in_use.append(
            locations.beaten_name(slot.level, slot.instance))
        solutions += locations.ending_names_for(slot.level, slot.instance)
    world.location_names_in_use.append(data.CREDITS)

    # A star is a solution found, as the level select counts them. Clamped to
    # the stars this run holds, so slot_data never carries a number the run
    # cannot honour; the events that count them exist only under that goal.
    world.stars_total = len(solutions)
    world.stars_to_collect = min(o.stars_to_collect.value, world.stars_total)
    world.star_event_names = ([locations.star_event_name(s) for s in solutions]
                              if world.goal_is_stars else [])

    world.pack_size = pack_size

    # Achievement checks hold progression like any check (droha, 2026-09-29:
    # "then what's the point of enabling them?"): all 17 were seen firing in a
    # run on 2026-09-28, each needs its puzzle's every mechanic, and a Skip
    # sends them, so a hard one never has to block a run.
    world.achievement_locations = frozenset()
    if achievements:
        awarded = [name for slot in world.plan
                   for name in locations.achievement_names_for(slot.level, slot.instance)]
        world.location_names_in_use += awarded
        world.achievement_locations = frozenset(awarded)


def create_items(world) -> None:
    pool: List = []

    for _ in range(world.pack_total):
        pool.append(world.create_item(items.PROGRESSIVE_PACK))
    pool.append(world.create_item(items.CREDITS_ITEM))

    # Starting abilities are granted outright rather than shuffled, so they
    # are removed from the pool instead of placed.
    for ability in world.live_abilities:
        if ability in world.starting_abilities:
            world.multiworld.push_precollected(world.create_item(ability))
        else:
            pool.append(world.create_item(ability))

    unfilled = len(world.multiworld.get_unfilled_locations(world.player))
    remaining = max(0, unfilled - len(pool))

    skips = min(world.options.skip_count.value, remaining)
    for _ in range(skips):
        pool.append(world.create_item(items.SKIP))
    remaining -= skips

    # Traps are taken BEFORE hint pages, and the order is the whole meaning of
    # cat_trap_chance. Hint Pages are far and away the largest tier - about 88
    # of 168 slots in a default seed - so drawing them first left the trap
    # percentage applying to a small residual, and cat_trap_chance = 25 bought
    # twelve traps rather than thirty-four. The dial stopped meaning what its
    # name says. Taking traps from the wider pool first restores that, and
    # costs the hints nothing: full coverage is capped by the pages the seed
    # actually contains, so it is the do-nothing filler underneath that
    # shrinks.
    # Both traps are shares of the same filler, so each dial means what it
    # says whatever the other is set to. Together they cannot take more than
    # there is.
    filler = remaining
    cats = filler * world.options.cat_trap_chance.value // 100
    backdrops = min(filler * world.options.background_trap_chance.value // 100,
                    filler - cats)
    for _ in range(cats):
        pool.append(world.create_item(items.CAT_TRAP))
    for _ in range(backdrops):
        pool.append(world.create_item(items.BACKGROUND_TRAP))
    remaining -= cats + backdrops

    pages = available_hint_pages(world)
    hints = min(pages * world.options.hint_coverage.value // 100, remaining)
    remaining -= hints

    # THE REST, SPLIT EVENLY between more Hint Pages and Background Reset
    # Tokens (droha, 2026-09-28). Hint Pages stop at one per page the seed
    # has - a page with no notepad to open is nothing - and the overflow is
    # tokens.
    more_hints = min((remaining + 1) // 2, pages - hints)
    hints += more_hints
    remaining -= more_hints
    for _ in range(hints):
        pool.append(world.create_item(items.HINT_PAGE))
    for _ in range(remaining):
        pool.append(world.create_item(items.BACKGROUND_RESET))

    world.multiworld.itempool += pool


def available_hint_pages(world) -> int:
    """How many hint pages this seed actually contains.

    Summed over the DRAWN plan, not over the level table, and per page rather
    than per puzzle - both of which matter. A level's notepad holds anywhere
    from zero to five pages, each with its own erasable scribble, so a puzzle
    count would be wrong in both directions. Summing per slot also handles a
    repeated generator correctly, since each instance is its own slot with its
    own notepad.

    The consequence worth stating: a slot whose level has no hint contributes
    nothing, so at 100% coverage the pool holds exactly one item per page that
    exists and can never mint a page there is nowhere to spend.
    """
    return sum(slot.level.hint_images for slot in world.plan)


def filler_sequence(world, count: int) -> List[str]:
    """Filler for Archipelago to hand out beyond this world's own pool (item
    links, plando): Background Reset Tokens, which are never wasted."""
    if count <= 0:
        return []
    return [items.BACKGROUND_RESET for _ in range(count)]


def hint_information(world, hint_data: Dict[int, Dict[int, str]]) -> None:
    """Per-seed "where is it" text, rendered as the entrance of a hint.

    Location names carry content and cannot carry position, because slot 7
    holds a different puzzle in every seed. This is Archipelago's own answer to
    that, and it means a hint reads:

        droha's Swapping is at Medicine Cabinet - Blue Bottles
        in droha's World at Pack 2, puzzle 4.
    """
    entries: Dict[int, str] = {}
    boundaries = items.pack_boundaries(len(world.plan), world.pack_size)
    for index, slot in enumerate(world.plan):
        where = _pack_and_position(index, boundaries)
        for name in locations.names_for(slot.level, slot.instance):
            address = locations.LOCATION_NAME_TO_ID.get(name)
            if address is not None:
                entries[address] = where

    credits_address = locations.LOCATION_NAME_TO_ID[data.CREDITS]
    entries[credits_address] = "the end of the track"
    hint_data[world.player] = entries


def slot_data(world) -> Mapping[str, Any]:
    """The whole handoff to the mod. It cannot recompute the draw.

    slot_requirements comes from the same function that built the access
    rules, so the in-game marker cannot disagree with the generator.
    """
    return {
        # The pair check. The mod refuses a seed whose apworld version does
        # not match its own, because location ids move between releases and a
        # mismatched pair sends the wrong checks without ever saying so.
        # check-version.py binds this to the mod's assembly version at build
        # time; this is what carries it to the player's machine.
        "world_version": data.WORLD_VERSION,
        "slots": [
            {
                "levelId": slot.level.level_id,
                "levelIndex": slot.level.level_index,
                "instance": slot.instance,
                "source": slot.level.source,
                # Which DLC the level needs, "" for base content. Separate
                # from source because the four randomizable DLC levels are
                # sourced "generator" and would otherwise look like base
                # content to the mod.
                "dlc": slot.level.dlc,
                "seed": slot.seed,
            }
            for slot in world.plan
        ],
        "pack_size": world.pack_size,
        "pack_total": world.pack_total,
        # Cumulative puzzles open after each pack, sent rather than recomputed.
        #
        # The mod needs to know which slots a pack reveals, and the ramp that
        # decides that is not trivial - a gentlest-fit acceleration under a
        # cap proportional to run length. Reimplementing it in C# would be two
        # implementations of one rule, and the moment they disagreed the game
        # would unlock a different set of puzzles than the logic assumed
        # reachable. Sending it keeps one source of truth.
        "pack_boundaries": items.pack_boundaries(len(world.plan), world.pack_size),
        # THE GOAL, as a string rather than the option's integer. The mod
        # dispatches on it, and a name that reads the same in the payload and
        # in the C# is one fewer thing to keep in step than 0 and 1.
        "goal": "collect_stars" if world.goal_is_stars else "beat_levels",
        "levels_to_beat": world.levels_to_beat,
        # Sent whichever goal is in use, so the mod can show the other number
        # if it ever wants to and so a payload is readable on its own.
        "stars_to_collect": world.stars_to_collect,
        "ability_locks": bool(world.options.ability_locks.value),
        "abilities": {a: data.classes_for(a) for a in world.live_abilities},
        "starting_abilities": sorted(world.starting_abilities),
        "requirements": world.requirements,
        "cat_trap_chance": world.options.cat_trap_chance.value,
        # Which DLCs this seed was built for. The mod checks these against
        # what the player actually has installed and refuses to connect on a
        # mismatch, because a DLC level that is not installed does not throw -
        # it silently redirects to the DLC level select, which would look like
        # the mod failing to launch the puzzle.
        "cupboards_and_drawers": bool(world.options.cupboards_and_drawers.value),
        "seeing_stars": bool(world.options.seeing_stars.value),
        # The finale the run ends on (FINALES), launched by the mod in place of
        # the base game's credits. Absent from a 0.4.3 seed: the base game's.
        "credits": world.credits_level,
        # Controller GameObject name -> group display name, for the levels this
        # run actually uses.
        #
        # Sent for the same reason as pack_boundaries: the grouping rule merges
        # mutually-dependent controllers into one checkable unit, and the mod
        # only ever sees a solved GameObject. Recomputing the merge in C# would
        # be a second implementation of a rule that decides what a location IS.
        # Keyed by level rather than by slot because it is a property of the
        # level, and 16 of 79 slots in a typical run are repeats.
        "controller_groups": {
            level_id: dict(sorted(data.BY_ID[level_id].controller_group.items()))
            for level_id in sorted({slot.level.level_id for slot in world.plan})
        },
        # Each drawn level's endings, so the mod files a completion by its
        # solution id (Core CheckRouter.ForEnding). Absent for a generated
        # puzzle, which the mod numbers in the order found.
        "endings": {
            level_id: [{"id": ending_id, "location": suffix}
                       for ending_id, suffix, _group in data.BY_ID[level_id].endings]
            for level_id in sorted({slot.level.level_id for slot in world.plan})
            if data.BY_ID[level_id].fixed_endings
        },
        # Controllers the level registers that are no location on purpose, so
        # the mod's registration audit does not report them as unknown.
        "not_locations": {
            level_id: sorted(data.BY_ID[level_id].not_locations)
            for level_id in sorted({slot.level.level_id for slot in world.plan})
            if data.BY_ID[level_id].not_locations
        },
    }
