"""Per-seed decisions: the draw, the item pool, hint text and slot_data.

Everything here runs once per seed. Nothing raises on a hostile yaml - a bad
combination degrades into something that still generates, and the degradation
is what the tests pin. An option that refuses to generate is a worse failure
than one that quietly does its best.
"""

from typing import Any, Dict, List, Mapping

from . import data, items, locations, rules, slots

#: Abbreviated because it sits inside an already long hint line:
#: "... at Medicine Cabinet - Blue Bottles in droha's World at Ch.2 Level 3."
CHAPTER_SIZES = (20, 16, 16, 15, 12)

#: Checks the free opening should offer before the run is handed to the fill.
#:
#: Not a difficulty knob - it is the room the fill needs to place its first
#: progression items. Below this the placement cascade cannot start, and on a
#: small run it stalls outright. Six was the lowest value that cleared every
#: measured configuration.
OPENING_FLOOR = 6


def _chapter_and_position(slot_index: int) -> str:
    seen = 0
    for chapter, size in enumerate(CHAPTER_SIZES, start=1):
        if slot_index < seen + size:
            return f"Ch.{chapter} Level {slot_index - seen + 1}"
        seen += size
    return f"Level {slot_index + 1}"


def _free_checks(plan_slice, held) -> int:
    """Addressed checks in these slots that need no further ability.

    Mirrors what rules.requirements will say for pack-free locations: a
    solution needs the whole level's abilities, a part only its own group's.
    """
    total = 0
    for slot in plan_slice:
        level = slot.level
        if level.abilities <= held:
            total += level.solution_count
        if level.has_parts:
            for part in level.parts:
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
    world.levels_to_star = min(o.levels_to_star.value, puzzle_count)
    world.goal_is_stars = o.goal.value == o.goal.option_star_levels

    source_weights = {
        "generator": o.generator_weight.value,
        "archive": o.archive_weight.value,
        # The campaign. Absent from this dict until 2026-09-09, which meant 57
        # of the 69 campaign puzzles could never be drawn at all: pass 2 fills
        # by source, so a source with no weight is a source that never appears,
        # and the only other door was the mechanic-coverage reserve in pass 1.
        "base": o.base_weight.value,
    }
    # The DLC sources, and ONLY when their toggle is on. A weight left here
    # for content _eligible has filtered out makes pass 2 roll a source with
    # no candidates and fall through to the repeatable-generator backstop,
    # which quietly skews the mix away from what the yaml asked for.
    if o.cupboards_and_drawers.value:
        source_weights["dlc1"] = o.cupboards_weight.value
    if o.seeing_stars.value:
        source_weights["dlc2"] = o.stars_weight.value
    if not any(source_weights.values()):
        # ALL of them zeroed. Generators are the only source that can always
        # supply a slot - they repeat, the other two are one-shot - so fall
        # back to them rather than failing.
        source_weights = {"generator": 1}

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
    world.levels_to_beat = min(world.levels_to_beat, actual)
    world.levels_to_star = min(world.levels_to_star, actual)

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

    world.requirements = rules.requirements(world.plan, pack_size, ability_locks)

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
    for slot in world.plan:
        world.location_names_in_use += locations.names_for(slot.level, slot.instance)
        world.event_names_in_use.append(
            locations.beaten_name(slot.level, slot.instance))
    world.location_names_in_use.append(data.CREDITS)

    world.pack_size = pack_size


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
    traps = remaining * world.options.cat_trap_chance.value // 100
    for _ in range(traps):
        pool.append(world.create_item(items.CAT_TRAP))
    remaining -= traps

    hints = min(available_hint_pages(world) * world.options.hint_coverage.value
                // 100, remaining)
    for _ in range(hints):
        pool.append(world.create_item(items.HINT_PAGE))
    remaining -= hints

    for name in filler_sequence(world, remaining):
        pool.append(world.create_item(name))

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
    """Cosmetic junk, chosen at random. Never empty for count > 0."""
    if count <= 0:
        return []
    return [world.random.choice(items.FILLER_ITEMS) for _ in range(count)]


def hint_information(world, hint_data: Dict[int, Dict[int, str]]) -> None:
    """Per-seed "where is it" text, rendered as the entrance of a hint.

    Location names carry content and cannot carry position, because slot 7
    holds a different puzzle in every seed. This is Archipelago's own answer to
    that, and it means a hint reads:

        droha's Swapping is at Medicine Cabinet - Blue Bottles
        in droha's World at Ch.2 Level 3.
    """
    entries: Dict[int, str] = {}
    for index, slot in enumerate(world.plan):
        where = _chapter_and_position(index)
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
        "goal": "star_levels" if world.goal_is_stars else "beat_levels",
        "levels_to_beat": world.levels_to_beat,
        # Sent whichever goal is in use, so the mod can show the other number
        # if it ever wants to and so a payload is readable on its own.
        "levels_to_star": world.levels_to_star,
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
    }
