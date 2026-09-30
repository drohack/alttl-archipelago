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

    `boundaries` comes from items.pack_boundaries. A lookup rather than a
    division because the blocks are not all equal: the first is free, and the
    last is whatever remainder is left when the run does not divide evenly.
    """
    for held, opens_up_to in enumerate(boundaries):
        if slot_index < opens_up_to:
            return held
    return len(boundaries) - 1


def narrowed_group(level: data.Level, suffix: str):
    """The group an ending asks for instead of the whole level, or None.

    AN ENDING THAT IS ONE GROUP SOLVED needs that group's abilities, as its
    part check did: Spoons' Size ending does not touch the Stacked group. Only
    where that group finishes the level ALONE (Level.finishes_alone,
    measured): Sharp Pencils' pencil order is an ending yet the shavings must
    go too, and DLC2 Bells' one group sits in a drawer the level asks for.
    Every other ending - one that needs the whole level, or one nobody has
    seen - needs the level's whole set.
    """
    for _id, own, group in level.endings:
        if own == suffix:
            return group if group in level.finishes_alone else None
    return None


def ending_abilities(level: data.Level, suffix: str, ability_locks: bool) -> List[str]:
    """What this ending needs: its group's set (narrowed_group), else the level's."""
    if not ability_locks:
        return []
    group = narrowed_group(level, suffix)
    if group is None:
        return sorted(level.enforced_abilities)
    return sorted(level.enforced_part_abilities.get(group, level.enforced_abilities))


def requirements(plan: List[slots.Slot], pack_size: int,
                 ability_locks: bool, achievements: bool = False) -> Dict[str, dict]:
    """Location name -> {"packs": n, "abilities": [...]}.

    Omits nothing: a location with no ability requirement still records its
    pack count, because that is a real gate.

    `achievements`: include the achievement checks, each needing its level's
    abilities BEFORE bypasses (level.abilities) - the widest set, since what
    an achievement touches is not known object by object. That can be more
    than the Beaten event asks; the star goal counts solutions, so a card's
    star, which waits for its achievements (CheckRouter.ForSlot), is not in
    the logic.
    """
    out: Dict[str, dict] = {}
    boundaries = items.pack_boundaries(len(plan), pack_size)
    for index, slot in enumerate(plan):
        level = slot.level
        packs = packs_needed(index, boundaries)

        # A solution is an arrangement of the WHOLE level, so it needs every
        # ability the level uses. A controller group needs only its own.
        level_abilities = (sorted(level.enforced_abilities)
                           if ability_locks else [])
        for _id, suffix, _group in level.endings:
            out[locations.ending_name(level, slot.instance, suffix)] = {
                "packs": packs, "abilities": ending_abilities(level, suffix, ability_locks),
            }

        if level.has_parts:
            for part in level.part_locations:
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
                part_abilities = (sorted(level.enforced_part_abilities.get(part, ()))
                                  if ability_locks else [])
                out[locations.part_name(level, slot.instance, part)] = {
                    "packs": packs,
                    "abilities": part_abilities,
                }

        out[locations.beaten_name(level, slot.instance)] = {
            "packs": packs, "abilities": level_abilities,
        }

        if achievements:
            widest = sorted(level.abilities) if ability_locks else []
            for name in locations.achievement_names_for(level, slot.instance):
                out[name] = {"packs": packs, "abilities": widest}
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

    # The credits need their own item AND enough done. The count is carried
    # by event locations rather than pool items, so it costs no item slots
    # and structurally forces the fill to spread progression across the run
    # instead of letting it bunch at the start.
    #
    # THE STAR GOAL COUNTS STARS - solutions found, as the level select counts
    # them (droha, 2026-09-28: "it's number of solutions"). Each solution has
    # a Star event beside it carrying that solution's own rule, so N stars
    # are provably reachable exactly when N solutions are. Events have no id,
    # so no location id moves; they exist only under that goal.
    if world.goal_is_stars:
        for slot in world.plan:
            for solution in locations.ending_names_for(slot.level, slot.instance):
                req = reqs[solution]
                set_rule(world.get_location(locations.star_event_name(solution)),
                         lambda state, r=req: satisfied(state, r["packs"], r["abilities"]))
    needed = world.stars_to_collect if world.goal_is_stars else world.levels_to_beat
    token = items.STAR_TOKEN if world.goal_is_stars else items.BEATEN_TOKEN

    def can_finish(state, n=needed, t=token) -> bool:
        return (state.has(items.CREDITS_ITEM, player, 1)
                and state.has(t, player, n))

    set_rule(world.get_location(data.CREDITS), can_finish)

    # No separate victory token: reaching the credits IS the goal, and its
    # requirement is exactly what a token would have been gated on.
    world.multiworld.completion_condition[player] = can_finish
