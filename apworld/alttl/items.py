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

#: Uncovers one page of one puzzle's hint notepad. The game's own unit: a
#: level's notepad is an array of HintPage objects, each with its own erasable
#: scribble, so "a page" is a thing the game already counts rather than a
#: currency invented here. Without one the notepad still opens - the player can
#: see a hint exists and how many pages - but the scribble will not wipe.
HINT_PAGE = "Hint Page"

ABILITY_ITEMS: List[str] = list(data.ABILITIES)

#: Recolours every backdrop in the game - the puzzle, the pause screen and the
#: level select - using the game's own palette so the result never looks
#: foreign.
#:
#: ONE ITEM, NOT TWO. This was "Level Background" and "Menu Background", two
#: filler items doing almost the same thing, and the level select was not
#: covered at all. droha asked for them merged and named as what they actually
#: are: the puzzle backdrop can land on a colour close to the pieces and hide
#: them, which is a trap and, in droha's words, "still funny".
BACKGROUND_TRAP = "Background Change Trap"

#: Filler that actually does something.
#:
#: This list used to read Title Theme, Colour Scheme and Daily Badge, and all
#: three were names with no code behind them - about three quarters of a
#: default seed paid out in items that did nothing at all. They are deleted
#: rather than kept alongside this one, because filler that does nothing
#: dilutes filler that does.
#:
#: A list of one is fine and filler_sequence handles it - random.choice over a
#: single name is that name. It is a LIST rather than a bare constant because
#: the shape is the extension point, and because every id after it is
#: positional in _ALL_NAMES (see the note there).
FILLER_ITEMS: List[str] = [
    BACKGROUND_TRAP,
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
    # Appended last, and new names must keep being appended last. Ids are
    # positional: slipping HINT_PAGE in beside SKIP where it reads better would
    # renumber every ability, trap and filler after it and silently repoint
    # every seed already in flight.
    #
    # Note that dropping a name from FILLER_ITEMS above does exactly that to
    # HINT_PAGE - it sits after them, so shortening that list by one moves its
    # id down by one. That is why world_version is bumped alongside; there is
    # no way to shrink an earlier list and leave later ids alone.
    + [HINT_PAGE]
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
    if name in (SKIP, HINT_PAGE):
        # Useful, not progression: nothing in the access rules asks whether a
        # hint has been read, so the fill is free to place these anywhere.
        return ItemClassification.useful
    return ItemClassification.filler


#: Puzzles open at the start, whatever the pack size. A run must not begin on a
#: single puzzle that an unlucky ability draw can lock: the opening exists so
#: the player always has somewhere to start, which is a different job from
#: pacing. At the default pack size of 4 this changes nothing.
MIN_OPENING = 4

#: Packs used to WIDEN as the run went on - "every N packs, the next one is a
#: puzzle wider" - so a 40-puzzle run unlocked 4, 4, 4, 5, 5, 6, 6, 6. That was
#: chosen to fit the cap below while starting gently, and it was the wrong
#: trade: a pack is supposed to be a guarantee, and "four puzzles, usually"
#: is not one. droha put it plainly - packs should be uniform, and if the size
#: has to change for generation to work then change the SIZE, not the shape.
#:
#: So the ramp is gone. When the requested size needs more packs than the cap
#: allows, every pack grows by the same amount instead, and only the last one
#: is short because a run rarely divides evenly.

PACKS_PER_PUZZLE = 0.18
MAX_PACKS = 14


def _pack_cap(puzzle_count: int) -> int:
    return max(1, min(MAX_PACKS, round(puzzle_count * PACKS_PER_PUZZLE)))


def _schedule(puzzle_count: int, pack_size: int, opening: int) -> List[int]:
    """Cumulative boundaries for uniform packs of `pack_size` after `opening`.

    The opening is passed in rather than derived, because widening the packs
    to fit the cap must not also widen the free start - a player who asked for
    packs of 4 and got 6 should still begin with 4 open, not 6.
    """
    out = [min(opening, puzzle_count)]
    while out[-1] < puzzle_count:
        out.append(min(out[-1] + pack_size, puzzle_count))
    return out


def pack_boundaries(puzzle_count: int, pack_size: int) -> List[int]:
    """Cumulative puzzles unlocked after each pack.

    Element 0 is the free opening; element k is how many puzzles are open once
    k packs are held. packs_needed and pack_count both read this, so "which
    slot does this pack open" and "how many packs exist" cannot drift apart.

    EVERY PACK IS THE SAME SIZE. That is the whole point of a pack - it is a
    guarantee about how much the run opens up, and a guarantee that varies is
    not one. The only short pack is the last, because a run rarely divides
    evenly, and that one is the remainder rather than a choice.

    The cap below is real and has to be respected, so when the requested size
    would need more packs than the run can carry, the SIZE grows - uniformly,
    for every pack - until it fits. A player who asks for packs of 2 in a
    40-puzzle run gets packs of 6, not packs of 2, 4, 6, 8, 10.
    """
    cap = _pack_cap(puzzle_count)

    size = max(1, pack_size)
    opening = min(max(size, MIN_OPENING), puzzle_count)

    schedule = _schedule(puzzle_count, size, opening)

    # Widen uniformly until the pack count fits. Solved directly rather than
    # by search: after the opening there are (puzzle_count - opening) puzzles
    # to hand out over at most `cap` packs. The opening does not move.
    if len(schedule) - 1 > cap:
        remaining = puzzle_count - opening
        if remaining > 0 and cap > 0:
            size = max(size, -(-remaining // cap))      # ceil
        schedule = _schedule(puzzle_count, size, opening)

    return schedule


def pack_count(puzzle_count: int, pack_size: int) -> int:
    """How many pack items exist. The opening is free, so it is not one."""
    return len(pack_boundaries(puzzle_count, pack_size)) - 1
