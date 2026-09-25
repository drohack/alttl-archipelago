"""Per-seed decisions: the draw, the item pool, hint text and slot_data.

Everything here runs once per seed. A bad combination degrades into something
that still generates, and the degradation is what the tests pin - with ONE
deliberate exception: a run too small to keep progression off its unproven
checks raises OptionError rather than ship a seed that can softlock. A refusal
names what to change; a softlock is found hours into a run. See decide().
"""

from typing import Any, Dict, List, Mapping

from Options import OptionError

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

#: How many times decide() draws the run looking for one that can carry the
#: whole unproven-location guard. A draw costs milliseconds. Measured
#: 2026-09-23 over 200 seeds: at 15 puzzles, base game only, about two
#: draws in three need a give-back, and a cap of 10 still refused 5 seeds in
#: 200. Every DLC configuration and every run of 20 or more cleared within 8.
#: Since every multi-part level was proven later that day, no part location
#: is guarded and the first draw always stands; the loop stays for a guard
#: that comes back.
DRAW_ATTEMPTS = 25


def _chapter_and_position(slot_index: int) -> str:
    seen = 0
    for chapter, size in enumerate(CHAPTER_SIZES, start=1):
        if slot_index < seen + size:
            return f"Ch.{chapter} Level {slot_index - seen + 1}"
        seen += size
    return f"Level {slot_index + 1}"


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

    GUARDED PARTS DO NOT COUNT, since 2026-09-23. The opening floor exists so
    the fill has somewhere to put its first progression items, and a guarded
    part may hold filler only - so counting it measured room that was not
    there. Medicine Cabinet was the worst case: eight no-ability parts, all
    guarded, so the grant loop saw an opening that was already full and
    stopped granting, and the draw then had to be thrown away. Measured over
    100 seeds a setup, skipping them cut first-draw redraws from 64% to 18%
    (15 base), 39% to 8% (15 both DLCs) and 23% to 13% (70 base).
    """
    total = 0
    for slot in plan_slice:
        level = slot.level
        if level.abilities <= held:
            total += level.solution_count
        if level.has_parts:
            guarded = level.unproven_parts
            for part in level.parts:
                if part in guarded:
                    continue
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

    # REDRAW RATHER THAN GIVE A GUARD BACK. A guard the pool cannot afford is
    # a part location that may ask for less than the player needs, handed to
    # the fill as a home for progression - the shape that ended a run on
    # 2026-09-21. The logic cannot catch it: it trusts the table, and the guard
    # exists because the table may be wrong. So a draw that has to give
    # anything back is thrown away and the run is drawn again from the same
    # world.random, which keeps the seed reproducible. A clean first draw is
    # untouched, so most seeds come out exactly as before.
    world.draw_attempts = 0
    world.first_plan = []
    fewest = None
    for attempt in range(1, DRAW_ATTEMPTS + 1):
        world.draw_attempts = attempt
        dropped = _draw_once(world, puzzle_count, pack_size, source_weights)
        if attempt == 1:
            world.first_plan = list(world.plan)
        fewest = dropped if fewest is None else min(fewest, dropped)
        if not dropped:
            break
    else:
        raise OptionError(
            f"[A Little to the Left - '{world.player_name}'] {DRAW_ATTEMPTS} "
            f"draws of {puzzle_count} puzzles all had unproven checks with "
            f"nowhere safe to put progression (the best still had {fewest}), "
            f"so any seed would risk a softlock. Raise "
            f"puzzle_count (20 or more draws cleanly almost every time), or "
            f"turn off ability_locks.")


def _draw_once(world, puzzle_count, pack_size, source_weights) -> int:
    """One draw of the run. Returns how many guards it had to give back."""
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
    world.levels_to_star = min(o.levels_to_star.value, puzzle_count, actual)

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

    # Which part locations may not hold progression. Decided here rather than
    # beside the requirements because affording the guard needs the full
    # location list, which is only built above.
    world.unproven_locations, dropped = _affordable_guard(
        world, rules.unproven_locations(world.plan, ability_locks))
    return dropped


def _affordable_guard(world, at_risk: List[str]):
    """As much of the guard as this seed can carry, and how much it gave back.

    Returns (guarded, dropped). decide() redraws the run while `dropped` is
    non-zero, so a give-back is never shipped - see DRAW_ATTEMPTS.

    THE GUARD HAS A PRICE AND IT IS NOT ZERO. Keeping progression off a
    location removes it as a home for an ability, a pack or the Credits. On a
    70-puzzle run that is invisible - 77 per cent of the table is still open.
    On an 8-puzzle DLC run it is fatal: the drawn levels are exactly the ones
    full of guarded groups, and `distribute_items_restrictive` came back with
    "No more spots to place 8 items" across four configurations.

    So the guard is best-effort and ordered. `at_risk` arrives worst-first, and
    this keeps as much of the front of it as the seed can pay for, leaving room
    for every progression item plus the opening the fill needs to get started.

    A SHORTFALL IS NEVER SHIPPED. This used to hand the smallest-gap groups
    back to the fill and log it, and measured 2026-09-22 that happened on 35 to
    40 of every 40 eight-puzzle seeds, with progression - once the Credits -
    landing on a given-back location. Now the count comes back to decide(),
    which redraws the run until nothing is given back.
    """
    if not at_risk:
        return frozenset(), 0

    reqs = world.requirements
    held = set(world.starting_abilities)

    def free_now(name: str) -> bool:
        """Checkable on turn one: no packs, no ability the player lacks."""
        req = reqs.get(name)
        return (req is not None and not req["packs"]
                and set(req["abilities"]) <= held)

    # THE COUNT THAT MATTERS IS THE OPENING, NOT THE TABLE. A first attempt
    # budgeted against total locations and still failed five configurations,
    # because the shortage is not of locations but of locations the fill can
    # use YET. Measured on a 20-puzzle run: 80 locations, 46 guarded, and of
    # the twelve checks reachable on turn one only TWO were left unguarded,
    # with twenty progression items waiting for a home.
    #
    # That is not bad luck. A group with a small requirement is both the
    # cheapest check in the run and the most likely to be understating, so the
    # guard aims squarely at the opening every time.
    # ONLY THE FREE-NOW GUARDS ARE WORTH GIVING BACK. A guard on something the
    # player cannot reach yet does not constrain the opening at all, so
    # surrendering it buys nothing and costs the protection.
    #
    # An earlier version popped the whole list indiscriminately and, on a seed
    # where few of the guarded groups were free on turn one, gave back EVERY
    # guard to gain nothing - `unproven_locations` came back empty and the
    # policy silently switched itself off. It was caught by
    # test_unproven_locations_refuse_progression, which asserts the guard is
    # doing something at all, and only on some seeds. A safety net that can
    # evaporate quietly is worse than none, because nothing downstream would
    # ever say so.
    at_risk_set = set(at_risk)
    free_guards = [name for name in at_risk if free_now(name)]
    held_back = [name for name in at_risk if name not in set(free_guards)]

    free_kept = sum(1 for name in world.location_names_in_use
                    if free_now(name) and name not in at_risk_set)

    # `at_risk` is worst-first, so give back from the END: the groups whose
    # claim differs from their level's by the least.
    dropped = 0
    while free_guards and free_kept < OPENING_FLOOR:
        free_guards.pop()
        dropped += 1
        free_kept += 1

    guarded = held_back + free_guards

    # AND A SECOND FLOOR, on the pool rather than the opening. Protecting the
    # opening is necessary and turns out not to be sufficient: widening the
    # guard from 114 locations to 122 on 2026-09-22 failed four of 2820 stress
    # runs, all of them 8-puzzle seeds, with "No more spots to place 6 items" -
    # a shortage that bites well after the cascade has started, so a
    # first-round measure never sees it.
    #
    # Everything the fill must place has to have SOME unguarded home, and on a
    # tiny run the guarded locations can outnumber the rest. Give back from the
    # least dangerous end until there is room for every progression item plus
    # the opening.
    needed = (world.pack_total
              + 1                                          # Credits
              + len(world.live_abilities) - len(world.starting_abilities)
              + world.options.skip_count.value
              + OPENING_FLOOR)
    total = len(world.location_names_in_use)
    order = {name: i for i, name in enumerate(at_risk)}
    guarded.sort(key=lambda name: order.get(name, 0))
    while guarded and total - len(guarded) < needed:
        guarded.pop()
        dropped += 1

    return frozenset(guarded), dropped


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
        # Controllers the level registers that are no location on purpose, so
        # the mod's registration audit does not report them as unknown.
        "not_locations": {
            level_id: sorted(data.BY_ID[level_id].not_locations)
            for level_id in sorted({slot.level.level_id for slot in world.plan})
            if data.BY_ID[level_id].not_locations
        },
    }
