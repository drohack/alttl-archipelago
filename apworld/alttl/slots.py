"""Deciding what puzzle goes in each slot of the run.

Two passes, in this order and for this reason:

  1. Cover the mechanics no generator can produce. Four abilities - Stacking,
     Containers, Furniture and Jigsaw - exist only on hand-made levels, so if
     the draw does not go looking for them they arrive by luck or not at all.
     Ties are broken at random so the same levels do not turn up every seed.

  2. Fill the rest by source, generator 70 / archive 30, preferring the
     least-used generator so no one puzzle type dominates.

Base-campaign levels are NOT a rollable source. They enter through pass 1
alone, and only when they are the only way to supply a mechanic. That is the
whole point of the split: a base level is in the run because it brings
something no generator can, never because a percentage said so.

Measured over 10 seeds at 79 slots with mechanic_coverage 3:
51 generator / 24 archive / 3 base, zero base levels serving no gap ability,
gap coverage 5/3/4/4, no generator repeating more than 4 times, 43 distinct
levels, about 181 checks.

Two earlier designs were measured and rejected, recorded so they are not
retried. A flat 10% base weight put 9-12 base levels in of which only 3 served
a gap ability - and the same 3 every seed, because the reserve was greedy
without random tie-breaks. Giving every ability an equal share was worse
still: four abilities have exactly one generator each (Grids only has
Procedural Grid Puzzle, Ordering only Pencils, Rotating only Clock, Symmetry
only Shells), so equal shares forced those four to repeat about eight times,
roughly 32 of 79 slots being four puzzle types.
"""

from typing import Dict, List, NamedTuple, Set

from . import data, items


#: Solvable puzzles the opening must contain, whatever guaranteed_open_slots
#: says. This is a floor rather than a preference, which is why a player cannot
#: set it lower: below it there is not enough reachable at the start for the
#: generator to place anything, and the seed fails to build rather than merely
#: opening awkwardly. Measured 2026-09-02 over 28 configurations x 15 seeds -
#: a floor of 1 failed 8 seeds, 2 failed 2, and 4 failed none.
#:
#: guaranteed_open_slots still does its job above this line: it is what a player
#: raises to be handed a wider opening than generation strictly needs.
MIN_SOLVABLE_OPENING = 4


class Slot(NamedTuple):
    """One card in the run."""

    level: data.Level
    #: 1-based; only generators ever exceed 1.
    instance: int
    #: Procedural seed for a generator slot, -1 for a fixed level.
    seed: int


def _eligible(world_options) -> List[data.Level]:
    """Levels this yaml permits at all."""
    enabled = set(world_options.archive_packs.value)
    out = []
    for level in data.LEVELS:
        if level.source == "archive" and data.pack_of(level) not in enabled:
            continue
        out.append(level)
    return out


def draw(random, slots: int, coverage: int, source_weights: Dict[str, int],
         instance_cap: int, options=None) -> List[Slot]:
    """Assign every slot. Deterministic for a given `random` state."""

    cap = min(instance_cap or data.MAX_GENERATOR_INSTANCES,
              data.MAX_GENERATOR_INSTANCES)
    pool = _eligible(options) if options is not None else list(data.LEVELS)

    used: Set[str] = set()              # one-shot levels already placed
    instances: Dict[str, int] = {}      # generator id -> times placed
    picked: List[data.Level] = []

    def take(level: data.Level) -> None:
        if level.repeatable:
            instances[level.level_id] = instances.get(level.level_id, 0) + 1
        else:
            used.add(level.level_id)
        picked.append(level)

    def available(level: data.Level) -> bool:
        if level.repeatable:
            return instances.get(level.level_id, 0) < cap
        return level.level_id not in used

    # --- pass 1: cover the mechanics generators cannot make -----------------
    need = {a: coverage for a in data.GAP_ABILITIES}
    while any(v > 0 for v in need.values()) and len(picked) < slots:
        wanted = {a for a, v in need.items() if v > 0}
        candidates = [l for l in pool if available(l) and (l.abilities & wanted)]
        if not candidates:
            # The table cannot supply what was asked - Furniture only exists on
            # four levels, so a demand of five is unmeetable. Take what there
            # is rather than failing generation; a thin run beats no run.
            break
        best = max(len(l.abilities & wanted) for l in candidates)
        candidates = [l for l in candidates if len(l.abilities & wanted) == best]
        level = random.choice(candidates)
        for ability in level.abilities & wanted:
            need[ability] -= 1
        take(level)

    # --- pass 2: fill the rest by source ------------------------------------
    sources = [s for s, w in source_weights.items() if w > 0]
    weights = [source_weights[s] for s in sources]

    while len(picked) < slots:
        candidates: List[data.Level] = []
        if sources:
            source = random.choices(sources, weights=weights)[0]
            candidates = [l for l in pool if l.source == source and available(l)]
        if not candidates:
            # That source is exhausted (or none were enabled). Generators can
            # always repeat, so they are the fallback that keeps the run full.
            candidates = [l for l in pool if l.repeatable and available(l)]
        if not candidates:
            break

        if candidates[0].repeatable:
            fewest = min(instances.get(l.level_id, 0) for l in candidates)
            candidates = [l for l in candidates
                          if instances.get(l.level_id, 0) == fewest]
        take(random.choice(candidates))

    # --- number the instances and pick generator seeds ----------------------
    seen: Dict[str, int] = {}
    result: List[Slot] = []
    for level in picked:
        seen[level.level_id] = seen.get(level.level_id, 0) + 1
        seed = random.randrange(1, 2_000_000_000) if level.repeatable else -1
        result.append(Slot(level, seen[level.level_id], seed))
    return result


def open_the_start(random, plan: List[Slot], pack_size: int, wanted: int,
                   held: Set[str]) -> List[Slot]:
    """Make sure the run opens with something the player can actually do.

    The opening costs no items, but its puzzles can still be locked behind
    abilities. With ability locks on and few starting abilities, an unlucky
    draw gives a player a first screen where nothing is solvable - which
    Archipelago's own test_empty_state_can_reach_something rightly rejects.

    Reorders rather than redraws: a solvable slot from later in the run is
    swapped forward, so the run's contents are untouched and only the order
    changes.

    THIS TOUCHES THE OPENING AND NOTHING ELSE, on purpose. An earlier version
    also forced a solvable puzzle into each of the first six packs, to keep the
    fill from stalling. That was scripting the run's shape to compensate for a
    logic bug - part locations were being given their whole level's abilities
    instead of their own - and it cost the variety a randomizer exists for.
    With the requirement fixed at source in rules.py the staircase measured as
    unnecessary: every option configuration fills without it. Pacing is meant
    to come from abilities arriving, not from a scripted opening, so if the
    fill ever stalls again the bug is in the logic and not here.
    """
    if not plan:
        return plan

    def solvable(slot: Slot) -> bool:
        return slot.level.abilities <= held

    # Free slots anywhere later in the run, to swap forward.
    spare = [i for i in range(len(plan)) if solvable(plan[i])]
    random.shuffle(spare)

    def claim(window_start: int, window_end: int, need: int) -> None:
        """Ensure `need` slots in [start, end) are solvable with what is held."""
        have = [i for i in range(window_start, window_end) if solvable(plan[i])]
        for i in have:
            if i in spare:
                spare.remove(i)
        blocked = [i for i in range(window_start, window_end) if i not in have]
        for target in blocked:
            if len(have) >= need:
                return
            # Only swap in something from OUTSIDE this window.
            candidates = [i for i in spare if not (window_start <= i < window_end)]
            if not candidates:
                return
            source = candidates[0]
            spare.remove(source)
            plan[target], plan[source] = plan[source], plan[target]
            have.append(target)

    # The free opening, which is at least MIN_OPENING puzzles however small the
    # pack size - the same window items.pack_boundaries treats as free.
    first = min(max(pack_size, items.MIN_OPENING), len(plan))
    claim(0, first, min(max(wanted, MIN_SOLVABLE_OPENING), first))

    # If nothing anywhere is solvable we return what we have. That can only
    # happen when every drawn level needs an ability and the player asked for
    # zero starting abilities; the caller grants one to break the tie.
    return plan


def abilities_in(plan: List[Slot]) -> Set[str]:
    """Abilities some placed level actually needs.

    An ability is in the item pool if and only if this returns it. That is a
    correctness requirement rather than tidiness: a Jigsaw level with no Jigsaw
    item would be unsolvable, and a Jigsaw item with no Jigsaw level is a dead
    item taking a slot from something useful.
    """
    found: Set[str] = set()
    for slot in plan:
        found |= slot.level.abilities
    return found
