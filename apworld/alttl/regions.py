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


def create_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    tidy = Region("The Tidying", world.player, world.multiworld)
    world.multiworld.regions += [menu, tidy]
    menu.connect(tidy)

    # Real, checkable locations. Credits is one of these - it is a level the
    # player actually plays, not a token.
    for name in world.location_names_in_use:
        tidy.locations.append(
            world.create_location(name, LOCATION_NAME_TO_ID[name], tidy))

    # One event per level instance, granting a Level Beaten token towards the
    # credits requirement. Locked, so the fill never touches them, and they
    # cost no pool slots.
    for name in world.event_names_in_use:
        loc = world.create_location(name, None, tidy)
        loc.place_locked_item(world.create_event(items.BEATEN_TOKEN))
        tidy.locations.append(loc)
