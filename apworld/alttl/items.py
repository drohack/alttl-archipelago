"""The item table.

IDs ARE POSITIONAL AND MUST NEVER MOVE - same rule as locations, same reason.
Append only.

Item names carry no numbers. "Skip", never "Skip x3"; the amounts that matter
travel in slot_data, so ids stay identical whatever a player's yaml says.
"""

from typing import Dict, List, Set

from BaseClasses import ItemClassification

from . import data

BASE_ID = 4_050_000

#: Opens the next block of puzzles - every block the same size, including the
#: free opening; see pack_boundaries. One repeated item rather than distinct
#: named packs, so the track always fills left to right.
PROGRESSIVE_PACK = "Progressive Puzzle Pack"

#: Unlocks the credits card. Its own item rather than riding the last pack, so
#: it can arrive from any world at any point; the card still stays locked until
#: enough puzzles are beaten, which is what actually gates the goal.
CREDITS_ITEM = "Credits"

#: Clears a puzzle. Logic-neutral - packs arrive from the multiworld rather
#: than from beating anything. A skipped puzzle DOES count towards the credits
#: requirement - see SkipCount in options.py for why, and for the trade that
#: makes.
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

#: Abilities the DLCs introduce. Appended after every base item name below,
#: never merged into ABILITY_ITEMS: that list sits in the middle of the id
#: sequence, so a thirteenth name inside it would move Cat Trap, Background
#: Change Trap and Hint Page down one and repoint every seed in flight.
DLC_ABILITY_ITEMS: List[str] = list(data.DLC_ABILITIES)

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

#: Names a player can use anywhere a single item name works - !hint,
#: start_inventory, item_links, plando. Without them Archipelago supplies only
#: "Everything", so there was no way to say "any ability", which with twelve
#: ability items is the group someone actually reaches for.
#:
#: Built from the same lists the pool is, so a new trap or ability joins its
#: group without anyone remembering to add it here.
ITEM_NAME_GROUPS: Dict[str, Set[str]] = {
    "Abilities": set(ABILITY_ITEMS) | set(DLC_ABILITY_ITEMS),
    "Traps": set(TRAP_ITEMS) | {BACKGROUND_TRAP},
    "Progression": {PROGRESSIVE_PACK, CREDITS_ITEM},
    "Useful": {SKIP, HINT_PAGE},
}

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
    # DLC abilities last, after every base name. Ids are positional, so this
    # is the only place a new ability can go without moving an existing id.
    + DLC_ABILITY_ITEMS
)

ITEM_NAME_TO_ID: Dict[str, int] = {
    name: BASE_ID + i for i, name in enumerate(_ALL_NAMES)
}


def classification(name: str) -> ItemClassification:
    """What the fill algorithm may assume about an item.

    Abilities are progression whenever ability locks are on, and the world
    prunes any ability no drawn level needs before the pool is built - so an
    ability that reaches here always gates something real.
    """
    if name in (PROGRESSIVE_PACK, CREDITS_ITEM):
        return ItemClassification.progression
    if name in ABILITY_ITEMS or name in DLC_ABILITY_ITEMS:
        return ItemClassification.progression
    if name in TRAP_ITEMS:
        return ItemClassification.trap
    if name in (SKIP, HINT_PAGE):
        # Useful, not progression: nothing in the access rules asks whether a
        # hint has been read, so the fill is free to place these anywhere.
        return ItemClassification.useful
    return ItemClassification.filler


#: The smallest a pack may be. A run must not begin on a single puzzle that an
#: unlucky ability draw can lock, so this is the floor on the size itself.
#:
#: IT IS A FLOOR ON THE SIZE, NOT A SEPARATE OPENING. It used to be the latter:
#: the free opening was held at 4 while the packs widened past it, so a default
#: run opened 4 and then handed out 6 at a time. droha, seeing that: "the packs
#: should all be the same size, the 4 minimum open just means they have
#: something to do in 4 levels at the start, not that the starting levels in a
#: pack are all completable." The guarantee is kept by raising the SIZE to this
#: floor and letting the opening equal it, so every block matches.
MIN_OPENING = 5

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

    Callers now pass `opening == pack_size`, so every block is the same width.
    The parameter survives because the two are conceptually different - the
    opening is free and the packs are earned - and collapsing them into one
    argument would hide that from the next reader.
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

    THE FREE OPENING IS ONE OF THOSE BLOCKS, not an exception to them. It used
    to be pinned at MIN_OPENING while the packs widened around it, which is how
    a default run came out 4, 6, 6, 6 ... 3 and read as a bug. The opening
    follows the size now, including through the widening below, so the only
    short block is the remainder.
    """
    cap = _pack_cap(puzzle_count)

    # The floor applies to the SIZE, so the opening inherits it for free.
    size = min(max(pack_size, MIN_OPENING), puzzle_count)
    schedule = _schedule(puzzle_count, size, size)

    # Widen uniformly until the pack count fits. Solved directly rather than
    # by search: with the opening equal to the size, `cap` packs after it must
    # cover the rest, so the size that just fits is ceil(remaining / cap) - and
    # because the opening moves with it, one pass can leave one pack too many.
    # Loop rather than reason about the rounding.
    while len(schedule) - 1 > cap and size < puzzle_count:
        remaining = puzzle_count - size
        wanted = -(-remaining // cap) if cap > 0 else puzzle_count
        size = max(size + 1, wanted)
        schedule = _schedule(puzzle_count, size, size)

    return schedule


def opening_size(puzzle_count: int, pack_size: int) -> int:
    """How many puzzles are open before any pack arrives.

    Read off pack_boundaries rather than recomputed, and that matters: the
    opening is the WIDENED size now, so `max(pack_size, MIN_OPENING)` is wrong
    whenever the cap forces packs to grow. Two callers had that expression
    inline - pool.py's ability-granting window and slots.py's solvable-opening
    claim - and both would have believed a 79-puzzle run opens 5 when it opens
    6, seeding the run's difficulty against the wrong window.
    """
    return pack_boundaries(puzzle_count, pack_size)[0]


def pack_count(puzzle_count: int, pack_size: int) -> int:
    """How many pack items exist. The opening is free, so it is not one."""
    return len(pack_boundaries(puzzle_count, pack_size)) - 1
