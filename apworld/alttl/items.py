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

#: Opens the next block of puzzles - `pack_size` of them at first, widening as
#: the run goes on; see pack_boundaries. One repeated item rather than distinct
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


#: Puzzles open at the start, whatever the pack size. A run must not begin on a
#: single puzzle that an unlucky ability draw can lock: the opening exists so
#: the player always has somewhere to start, which is a different job from
#: pacing. At the default pack size of 4 this changes nothing.
MIN_OPENING = 4

#: Candidate ramps, gentlest first: "every N packs, the next one is a puzzle
#: wider". Packs widen as the run goes on because that is the pacing the game
#: wants - slow while you have few abilities, quick once you have many.
PACK_ACCELERATIONS = (3, 2, 1)

#: Hard ceiling on how many pack items a run may contain, and the reason the
#: ramp is chosen rather than fixed.
#:
#: Every pack is a progression item, and progression DENSITY is what decides
#: whether a seed can be filled. Flat packs at pack_size 1 meant 75 of them
#: against about 110 locations in a generators-only run - roughly a third of
#: the pool blocking its own placement - and generation failed outright.
#: Capping the count bounds that density for every option combination at once,
#: which is why it is a cap and not a tuning knob.
#:
#: Measured 2026-09-02 over 28 configurations x 15 seeds: uncapped, the
#: generators-only + pack_size 1 + no-starting-abilities combination filled
#: 9/12. Capped, every configuration filled every seed. 14 is the largest value
#: that does so, chosen so the default run's pacing is left alone.
MAX_PACKS = 14


def _schedule(puzzle_count: int, pack_size: int, acceleration: int) -> List[int]:
    opening = min(max(pack_size, MIN_OPENING), puzzle_count)
    out = [opening]
    step_index = 1
    while out[-1] < puzzle_count:
        step = pack_size + (step_index - 1) // acceleration
        out.append(min(out[-1] + step, puzzle_count))
        step_index += 1
    return out


def pack_boundaries(puzzle_count: int, pack_size: int) -> List[int]:
    """Cumulative puzzles unlocked after each pack.

    Element 0 is the free opening; element k is how many puzzles are open once
    k packs are held. packs_needed and pack_count both read this, so "which
    slot does this pack open" and "how many packs exist" cannot drift apart.

    Takes the gentlest ramp that stays under MAX_PACKS. Small pack sizes
    therefore widen faster - which is what the player asked for anyway, since
    they asked to start slow rather than to stay slow for eighty puzzles.
    """
    schedule = _schedule(puzzle_count, pack_size, PACK_ACCELERATIONS[-1])
    for acceleration in PACK_ACCELERATIONS:
        candidate = _schedule(puzzle_count, pack_size, acceleration)
        if len(candidate) - 1 <= MAX_PACKS:
            return candidate
    # Even the steepest ramp overflows the cap. Cannot happen for the option
    # ranges we ship; returning it beats returning nothing.
    return schedule


def pack_count(puzzle_count: int, pack_size: int) -> int:
    """How many pack items exist. The opening is free, so it is not one."""
    return len(pack_boundaries(puzzle_count, pack_size)) - 1
