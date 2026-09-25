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
        level_abilities = (sorted(level.enforced_abilities)
                           if ability_locks else [])
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
                part_abilities = (sorted(level.enforced_part_abilities.get(part, ()))
                                  if ability_locks else [])
                out[locations.part_name(level, slot.instance, part)] = {
                    "packs": packs,
                    "abilities": part_abilities,
                }

        out[locations.beaten_name(level, slot.instance)] = {
            "packs": packs, "abilities": level_abilities,
        }
    return out


def unproven_locations(plan: List[slots.Slot], ability_locks: bool) -> List[str]:
    """Part locations whose requirement nobody has established.

    These should not hold progression. Not because they are known wrong - most
    of them are fine - but because a wrong one is silent, and the failure it
    causes is a dead run rather than an error. See Level.unproven_parts.

    ORDERED MOST AT RISK FIRST, because the caller cannot always afford to
    guard all of them. On a short run the guarded groups can outnumber the
    places progression could otherwise go, and the fill fails outright -
    measured, not feared: an 8-puzzle DLC run has Medicine Cabinet's eleven
    groups, Desktop Computer's six and Paper Plane Supplies' ten all guarded at
    once, leaving nowhere for the abilities. pool.decide trims the tail.

    Risk is the size of the gap: how many abilities the level needs that the
    group does not name. A group missing one is a group where the player is
    probably holding almost everything anyway; a group naming NOTHING inside a
    level that needs four is the shape that killed a run, so it is guarded
    first and dropped last. Ties break on the name, so the order is stable
    across runs and a seed is reproducible.

    Solution and Beaten locations are never in here. They already require the
    level's whole enforced set, so they cannot ask for less than the level
    does, which is the only understatement this guards against.

    With ability_locks off there is nothing to understate: every requirement is
    empty, so no group can need strictly less than its level, and this is empty
    too. That falls out of Level.unproven_parts rather than being special-cased
    here, but it is worth saying out loud because "the safety net is off" would
    be an alarming thing to discover by reading a diff.
    """
    if not ability_locks:
        return []

    scored = []
    for slot in plan:
        level = slot.level
        if not level.has_parts:
            continue
        whole = level.enforced_abilities
        for part in level.unproven_parts:
            gap = len(whole - level.enforced_part_abilities[part])
            scored.append((gap, locations.part_name(level, slot.instance, part)))

    scored.sort(key=lambda row: (-row[0], row[1]))
    return [name for _gap, name in scored]


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

    # The credits need their own item AND enough puzzles finished. The count
    # is carried by event locations rather than pool items, so it costs no
    # item slots and structurally forces the fill to spread progression
    # across the run instead of letting it bunch at the start.
    #
    # THE STAR GOAL USES THE SAME TOKEN, and that is not a shortcut - it is
    # the correct rule. Starring a puzzle means collecting every location on
    # it, and the Beaten event's own requirement is already the STRICTEST of
    # them: rules.py above gives Beaten the union of the level's abilities,
    # every solution location the same union, and every part a subset of it
    # (pinned by test_tables.NoPartNeedsMoreThanItsLevel). All locations on a
    # slot share one packs value. So a state that can reach N Beaten events
    # can reach every location on those N slots, and "N starred" is provably
    # achievable exactly when "N beaten" is.
    #
    # Which means no second event item, no second event location, and no
    # shift in location ids for a goal that is materially harder to play.
    # The difference between the two goals is entirely how much work the
    # PLAYER does, not what the generator must prove.
    needed = world.levels_to_star if world.goal_is_stars else world.levels_to_beat

    def can_finish(state, n=needed) -> bool:
        return (state.has(items.CREDITS_ITEM, player, 1)
                and state.has(items.BEATEN_TOKEN, player, n))

    set_rule(world.get_location(data.CREDITS), can_finish)

    # No separate victory token: reaching the credits IS the goal, and its
    # requirement is exactly what a token would have been gated on.
    world.multiworld.completion_condition[player] = can_finish
