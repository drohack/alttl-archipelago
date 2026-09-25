"""Regions.

Deliberately shallow: one Menu plus one region holding everything. The track is
not a graph - every slot hangs off the same Progressive Puzzle Pack count - so
a region per slot would add 79 nodes that all connect identically and explain
nothing. The pack requirement lives on the locations instead, where it sits
alongside the ability requirement it is always paired with.
"""

from BaseClasses import Region

from . import items
from .locations import LOCATION_NAME_TO_ID


def _no_progression(item) -> bool:
    """An item rule: anything but something the run depends on.

    NOT LocationProgressType.EXCLUDED, which was the first attempt and broke
    the fill. EXCLUDED means "filler only" and Archipelago balances it against
    the filler supply, so excluding 191 locations demands 191 filler items;
    a short DLC-only run has nowhere near that and generation died with
    "Not enough filler items for excluded locations. There are 16 more
    excluded locations than excludable items." Five configurations in
    test_fill_stress went red at once.

    An item rule is the narrower statement and the one actually meant: useful
    items are welcome here, traps and hints are welcome here, and only
    progression - the thing whose loss ends a run - is turned away. It costs
    the fill nothing but a little freedom.
    """
    return not item.advancement


def create_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    tidy = Region("The Tidying", world.player, world.multiworld)
    world.multiworld.regions += [menu, tidy]
    menu.connect(tidy)

    # Real, checkable locations. Credits is one of these - it is a level the
    # player actually plays, not a token.
    #
    # A location whose requirement nobody has established does not get to hold
    # anything a run depends on. It stays in logic, stays reachable under
    # accessibility: full, and stays worth checking - it just cannot be the
    # thing that strands a player. See rules.unproven_locations for why silence
    # about a requirement is not the same as confidence in it.
    unproven = getattr(world, "unproven_locations", frozenset())
    for name in world.location_names_in_use:
        location = world.create_location(name, LOCATION_NAME_TO_ID[name], tidy)
        if name in unproven:
            location.item_rule = _no_progression
        tidy.locations.append(location)

    # One event per level instance, granting a Level Beaten token towards the
    # credits requirement. Locked, so the fill never touches them, and they
    # cost no pool slots.
    for name in world.event_names_in_use:
        loc = world.create_location(name, None, tidy)
        loc.place_locked_item(world.create_event(items.BEATEN_TOKEN))
        tidy.locations.append(loc)
