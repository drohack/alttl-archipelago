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

from . import data


class Slot(NamedTuple):
    """One card in the run."""

    level: data.Level
    #: 1-based; only generators ever exceed 1.
    instance: int
    #: Procedural seed for a generator slot, -1 for a fixed level.
    seed: int


def _eligible(world_options) -> List[data.Level]:
    """Levels this yaml permits at all."""
    enabled_packs = {
        key for key in data.ARCHIVE_PACKS
        if getattr(world_options, f"pack_{key}", None) is None
        or bool(getattr(world_options, f"pack_{key}").value)
    }
    out = []
    for level in data.LEVELS:
        if level.source == "archive":
            pack = data.pack_of(level)
            if pack is not None and pack not in enabled_packs:
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
