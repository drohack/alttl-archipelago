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


def _chapter_and_position(slot_index: int) -> str:
    seen = 0
    for chapter, size in enumerate(CHAPTER_SIZES, start=1):
        if slot_index < seen + size:
            return f"Ch.{chapter} Level {slot_index - seen + 1}"
        seen += size
    return f"Level {slot_index + 1}"


def decide(world) -> None:
    """Choose what is in the run. Must run before regions or items."""
    o = world.options

    puzzle_count = o.puzzle_count.value
    pack_size = min(o.pack_size.value, puzzle_count)

    # Clamped rather than rejected: a yaml asking to beat more puzzles than
    # exist should still generate, just with the goal it can actually offer.
    world.levels_to_beat = min(o.levels_to_beat.value, puzzle_count)

    source_weights = {
        "generator": o.generator_weight.value,
        "archive": o.archive_weight.value,
    }
    if not any(source_weights.values()):
        # Both zeroed. Generators are the only source that can always supply a
        # slot, so fall back to them rather than failing.
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

    ability_locks = bool(o.ability_locks.value)

    # An ability is in the pool if and only if some drawn level needs it. A
    # Jigsaw level with no Jigsaw item would be unsolvable; a Jigsaw item with
    # no Jigsaw level is a dead item taking a slot from something useful.
    live = slots.abilities_in(world.plan) if ability_locks else set()
    world.live_abilities = [a for a in data.ABILITIES if a in live]

    count = min(o.starting_abilities.value, len(world.live_abilities))
    world.starting_abilities = world.random.sample(world.live_abilities, count) \
        if count else []

    # The opening is free, but its puzzles can still be ability-locked.
    # Reorder so at least guaranteed_open_slots of them are solvable now.
    held = set(world.starting_abilities)
    world.plan = slots.open_the_start(
        world.random, world.plan, pack_size,
        o.guaranteed_open_slots.value, held)

    # Last resort: if the opening is still entirely locked, grant one more
    # ability rather than shipping a seed that cannot be started. Only reachable
    # when the player asked for zero starting abilities and every drawn level
    # needs one.
    if ability_locks and world.plan:
        window = min(max(pack_size, items.MIN_OPENING), len(world.plan))
        if not any(world.plan[i].level.abilities <= held for i in range(window)):
            needed = min(world.plan[:window], key=lambda s: len(s.level.abilities))
            granted = False
            for ability in sorted(needed.level.abilities):
                if ability not in world.starting_abilities:
                    world.starting_abilities.append(ability)
                    held.add(ability)
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

    traps = remaining * world.options.cat_trap_chance.value // 100
    for _ in range(traps):
        pool.append(world.create_item(items.CAT_TRAP))

    for name in filler_sequence(world, remaining - traps):
        pool.append(world.create_item(name))

    world.multiworld.itempool += pool


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
        "slots": [
            {
                "levelId": slot.level.level_id,
                "levelIndex": slot.level.level_index,
                "instance": slot.instance,
                "source": slot.level.source,
                "seed": slot.seed,
            }
            for slot in world.plan
        ],
        "pack_size": world.pack_size,
        "pack_total": world.pack_total,
        "levels_to_beat": world.levels_to_beat,
        "ability_locks": bool(world.options.ability_locks.value),
        "abilities": {a: data.ABILITY_CLASSES[a] for a in world.live_abilities},
        "starting_abilities": sorted(world.starting_abilities),
        "requirements": world.requirements,
        "cat_trap_chance": world.options.cat_trap_chance.value,
    }
