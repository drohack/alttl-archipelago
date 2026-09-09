"""Access rules, and the slot_data that mirrors them.

THE CONTRACT: requirements() is called by both set_all_rules and
fill_slot_data. The in-game tracker therefore cannot disagree with the
generator, because they are reading the same computation rather than two
implementations of the same intent. This is the single best idea carried over
from the cw4 world and it is worth protecting.

Each entry is the COMPLETE requirement for that location. A consumer must not
AND it with anything else - a controller group is often solvable long before
the level as a whole is, and combining the two would hide that.
"""

from typing import Dict, List

from worlds.generic.Rules import set_rule

from . import data, items, locations, slots


def packs_needed(slot_index: int, boundaries: List[int]) -> int:
    """How many Progressive Puzzle Packs open the slot at this position.

    `boundaries` comes from items.pack_boundaries - the opening is free, and
    packs widen as the run goes on, so this is a lookup rather than a division.
    """
    for held, opens_up_to in enumerate(boundaries):
        if slot_index < opens_up_to:
            return held
    return len(boundaries) - 1


def requirements(plan: List[slots.Slot], pack_size: int,
                 ability_locks: bool) -> Dict[str, dict]:
    """Location name -> {"packs": n, "abilities": [...]}.

    Omits nothing: a location with no ability requirement still records its
    pack count, because that is a real gate.
    """
    out: Dict[str, dict] = {}
    boundaries = items.pack_boundaries(len(plan), pack_size)
    for index, slot in enumerate(plan):
        level = slot.level
        packs = packs_needed(index, boundaries)

        # A solution is an arrangement of the WHOLE level, so it needs every
        # ability the level uses. A controller group needs only its own.
        level_abilities = sorted(level.abilities) if ability_locks else []
        for n in range(1, level.solution_count + 1):
            out[locations.solution_name(level, slot.instance, n)] = {
                "packs": packs, "abilities": level_abilities,
            }

        if level.has_parts:
            for part in level.parts:
                # A controller group's OWN requirement, from Core, not the
                # level's. The difference is large and it is not cosmetic:
                # across the 200 groups the level-wide set demands up to four
                # abilities where the widest single group needs two, and 57
                # groups need none at all. Paper Plane Supplies asked for four
                # abilities per part when its largest group needs one. That
                # over-approximation was strangling the fill as well as lying
                # to the tracker.
                #
                # Numbers re-measured 2026-09-09. They previously read "106
                # part locations" and "no group anywhere needs more than one",
                # both of which had gone stale as dependencies and restored
                # phases pushed requirements up.
                part_abilities = (sorted(level.part_abilities.get(part, ()))
                                  if ability_locks else [])
                out[locations.part_name(level, slot.instance, part)] = {
                    "packs": packs,
                    "abilities": part_abilities,
                }

        out[locations.beaten_name(level, slot.instance)] = {
            "packs": packs, "abilities": level_abilities,
        }
    return out


def set_all_rules(world) -> None:
    player = world.player
    reqs = world.requirements
    pack = items.PROGRESSIVE_PACK

    def satisfied(state, packs: int, abilities: List[str]) -> bool:
        if packs and not state.has(pack, player, packs):
            return False
        return all(state.has(a, player, 1) for a in abilities)

    for name, req in reqs.items():
        try:
            location = world.get_location(name)
        except KeyError:
            continue        # a name this seed did not use
        set_rule(location, lambda state, r=req: satisfied(
            state, r["packs"], r["abilities"]))

    # The credits need their own item AND enough puzzles beaten. The count is
    # carried by event locations rather than pool items, so it costs no item
    # slots and structurally forces the fill to spread progression across the
    # run instead of letting it bunch at the start.
    needed = world.levels_to_beat

    def can_finish(state, n=needed) -> bool:
        return (state.has(items.CREDITS_ITEM, player, 1)
                and state.has(items.BEATEN_TOKEN, player, n))

    set_rule(world.get_location(data.CREDITS), can_finish)

    # No separate victory token: reaching the credits IS the goal, and its
    # requirement is exactly what a token would have been gated on.
    world.multiworld.completion_condition[player] = can_finish
