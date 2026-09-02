"""The location table.

IDs ARE POSITIONAL AND MUST NEVER MOVE. They are derived from the order this
module builds names in, and that order is part of the datapackage checksum
every client verifies. Appending is safe; inserting, reordering or renaming
breaks every seed already in flight.

The table is static: it covers every location any yaml could ever produce, not
the ones a particular seed uses. Archipelago's location_name_to_id is a
ClassVar shared by all seeds of the game, so it cannot depend on options or on
the draw. A name that a given seed does not use simply never becomes a real
location.

Names are content-based rather than positional - "Medicine Cabinet - Blue
Bottles", not "Slot 07 - Part 3" - because slot 7 holds a different puzzle in
every seed. Position is carried per-seed by extend_hint_information instead,
so a hint reads "... at Medicine Cabinet - Blue Bottles in droha's World at
Ch.2 Level 3".
"""

from typing import Dict, List

from . import data

BASE_ID = 4_050_000
LOCATION_BASE_ID = BASE_ID + 1000

SOLUTION = "Solution"
BEATEN = "Beaten"


def instance_tag(level: data.Level, instance: int) -> str:
    """"Books (Randomized)" for the first, "Books (Randomized) #2" after."""
    return level.display if instance <= 1 else f"{level.display} #{instance}"


def solution_name(level: data.Level, instance: int, number: int) -> str:
    return f"{instance_tag(level, instance)} - {SOLUTION} {number}"


def part_name(level: data.Level, instance: int, part: str) -> str:
    return f"{instance_tag(level, instance)} - {part}"


def beaten_name(level: data.Level, instance: int) -> str:
    """An event location, address None, granting one Level Beaten token."""
    return f"{instance_tag(level, instance)} - {BEATEN}"


def names_for(level: data.Level, instance: int) -> List[str]:
    """The real, checkable locations one instance of a level contributes.

    One per distinct solution, always. Plus one per controller group, but only
    when the level has more than one - on a single-group level the group check
    and the first solution check are the same event, so minting both would
    double count.
    """
    out = [solution_name(level, instance, n)
           for n in range(1, level.solution_count + 1)]
    if level.has_parts:
        out += [part_name(level, instance, p) for p in level.parts]
    return out


def _build() -> List[str]:
    names: List[str] = []
    # Ordered by the game's own level index so the sequence is stable against
    # anything we might later change about display names or sorting.
    for level in sorted(data.LEVELS, key=lambda l: l.level_index):
        for instance in range(1, level.max_instances + 1):
            names.extend(names_for(level, instance))
    names.append(data.CREDITS)
    return names


ALL_NAMES: List[str] = _build()

LOCATION_NAME_TO_ID: Dict[str, int] = {
    name: LOCATION_BASE_ID + i for i, name in enumerate(ALL_NAMES)
}

#: Event locations carry no address, so they are deliberately absent from the
#: id table. One per level instance, granting a Level Beaten token towards the
#: credits requirement.
EVENT_NAMES: List[str] = [
    beaten_name(level, instance)
    for level in sorted(data.LEVELS, key=lambda l: l.level_index)
    for instance in range(1, level.max_instances + 1)
]
