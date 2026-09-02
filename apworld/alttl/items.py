"""The item table.

IDs ARE POSITIONAL AND MUST NEVER MOVE - same rule as locations, same reason.
Append only.

Item names carry no numbers. "Skip", never "Skip x3"; the amounts that matter
travel in slot_data, so ids stay identical whatever a player's yaml says.
"""

from typing import Dict, List

from BaseClasses import ItemClassification

from . import data

BASE_ID = 4_050_000

#: Opens the next `pack_size` puzzles. One repeated item rather than distinct
#: named packs, so the track always fills left to right.
PROGRESSIVE_PACK = "Progressive Puzzle Pack"

#: Unlocks the credits card. Its own item rather than riding the last pack, so
#: it can arrive from any world at any point; the card still stays locked until
#: enough puzzles are beaten, which is what actually gates the goal.
CREDITS_ITEM = "Credits"

#: Clears a puzzle. Logic-neutral - packs arrive from the multiworld rather
#: than from beating anything, and a skipped puzzle does not count towards the
#: credits requirement.
SKIP = "Skip"

#: The cat walks in and knocks your arrangement over. The game's own idea of an
#: interruption. Costs time, never progress.
CAT_TRAP = "Cat Trap"

ABILITY_ITEMS: List[str] = list(data.ABILITIES)

FILLER_ITEMS: List[str] = [
    "Title Theme",
    "Colour Scheme",
    "Daily Badge",
]

TRAP_ITEMS: List[str] = [CAT_TRAP]

#: One copy per level instance, granted by an event location. The credits gate
#: counts these, which spends no pool slots and forces the fill to spread
#: progression across the run rather than bunching it.
BEATEN_TOKEN = "Level Beaten"

_ALL_NAMES: List[str] = (
    [PROGRESSIVE_PACK, CREDITS_ITEM, SKIP]
    + ABILITY_ITEMS
    + TRAP_ITEMS
    + FILLER_ITEMS
)

ITEM_NAME_TO_ID: Dict[str, int] = {
    name: BASE_ID + i for i, name in enumerate(_ALL_NAMES)
}

#: Events carry no id, so they stay out of the table above.
EVENT_ITEMS: List[str] = [BEATEN_TOKEN]


def classification(name: str) -> ItemClassification:
    """What the fill algorithm may assume about an item.

    Abilities are progression whenever ability locks are on, and the world
    prunes any ability no drawn level needs before the pool is built - so an
    ability that reaches here always gates something real.
    """
    if name in (PROGRESSIVE_PACK, CREDITS_ITEM):
        return ItemClassification.progression
    if name in ABILITY_ITEMS:
        return ItemClassification.progression
    if name in TRAP_ITEMS:
        return ItemClassification.trap
    if name == SKIP:
        return ItemClassification.useful
    return ItemClassification.filler


def pack_count(puzzle_count: int, pack_size: int) -> int:
    """How many pack items exist.

    One pack's worth of puzzles is open at the start, so the rest have to be
    bought. The final pack opens whatever remains rather than a full group.
    """
    remaining = max(0, puzzle_count - pack_size)
    return -(-remaining // pack_size)      # ceil
