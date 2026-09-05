"""The game's content, loaded from the shared data files.

Three files, all in data/, all shared with the C# mod so the generator and the
game cannot disagree about what exists:

  levels.json     measured from the game by the DevTools levelsweep command
  abilities.json  the authored ability grouping
  names.json      exported from ALTTLArchipelago.Core

Nothing here reimplements naming or grouping logic. That was deliberate: the
display-name rules are fiddly (camel splitting, pack prefixes moving to
suffixes, a per-level collision fallback) and a second implementation in
another language would drift. These strings ARE Archipelago location names, so
drift would mean the generator and the mod disagreeing about what a check is
called.
"""

import json
import pkgutil
from typing import Dict, FrozenSet, List, Optional


def _load(name: str) -> dict:
    """Read one of the shared data files.

    Via pkgutil, NOT open(). A shipped world is a zip-imported .apworld, where
    these files have no path on disk - open() raises FileNotFoundError and the
    whole world fails to load. It works in a development checkout either way,
    so the bug is invisible until someone installs the packaged build, which is
    exactly how it was found.
    """
    raw = pkgutil.get_data(__package__, f"data/{name}")
    if raw is None:      # pragma: no cover - a packaging error, not a run-time one
        raise FileNotFoundError(f"{name} is missing from the alttl world package")
    return json.loads(raw.decode("utf-8"))


_LEVELS_RAW = _load("levels.json")
_ABILITIES_RAW = _load("abilities.json")
_NAMES_RAW = _load("names.json")

#: Highest number of times one generator may appear in a run. The static
#: location table names every instance up front, so this is a correctness
#: bound rather than a preference.
MAX_GENERATOR_INSTANCES: int = _NAMES_RAW["maxGeneratorInstances"]

CREDITS: str = _NAMES_RAW["credits"]

#: Ability name -> the ObjectController classes it unlocks.
ABILITY_CLASSES: Dict[str, List[str]] = _ABILITIES_RAW["abilities"]

#: Ability names in a stable order. Item ids hang off this, so it must not be
#: reordered once shipped.
ABILITIES: List[str] = list(ABILITY_CLASSES)

_CLASS_TO_ABILITY = {c: a for a, cs in ABILITY_CLASSES.items() for c in cs}

#: Classes that need no ability - the baseline verbs, never items.
BASELINE: FrozenSet[str] = frozenset(_ABILITIES_RAW["baseline"])

#: Classes that are not puzzles at all, such as camera-pan helpers.
NOT_PUZZLES: FrozenSet[str] = frozenset(_ABILITIES_RAW["notPuzzles"])


class Level:
    """One level, with everything the generator needs to place it."""

    __slots__ = ("level_id", "level_index", "source", "solution_count",
                 "display", "parts", "part_abilities", "abilities",
                 "controller_group", "hint_images")

    def __init__(self, raw: dict):
        self.level_id: str = raw["levelId"]
        self.level_index: int = raw["levelIndex"]
        self.source: str = raw["source"]          # generator | archive | base
        self.solution_count: int = raw["solutionCount"]

        names = _NAMES_RAW["levels"][self.level_id]
        self.display: str = names["display"]

        # Controller groups, in the same order Core produces them, so "the Nth
        # group" means the same thing on both sides. dict preserves insertion
        # order, and Core emits them sorted.
        parts_raw = names["parts"]
        self.parts: List[str] = [p["display"] for p in parts_raw.values()]

        # What each group needs ON ITS OWN, which is far less than the level as
        # a whole: measured across all 106 groups, 32 need nothing and 74 need
        # exactly one ability - none needs two. Core computed these, including
        # the transitive closure over one-way dependencies, so the requirement
        # is not re-derived here. Keyed by display name because that is what
        # the location name is built from; verified unique within a level.
        self.part_abilities: Dict[str, FrozenSet[str]] = {
            p["display"]: frozenset(p["abilities"]) for p in parts_raw.values()
        }

        # Which group each controller belongs to, keyed by the controller's
        # GameObject name. This is what the mod resolves a solved-controller
        # event with: the event carries a GameObject, not a group, and a
        # mutually-dependent pair is two controllers wearing one group.
        # Exported by Core alongside the names so the two cannot disagree.
        self.controller_group: Dict[str, str] = {
            member: p["display"]
            for p in parts_raw.values()
            for member in p["members"]
        }

        # How many hint pages this level's notepad holds. Not one per level:
        # 74 levels have one, 31 have between two and five, and six have none.
        # The Hint Page item is minted per PAGE, so this is summed over the
        # drawn plan rather than derived from a puzzle count. Six generators
        # read zero here because the sweep reads LevelInterface.HintImages;
        # LevelRandomizer.GetRandomizerHints is a separate source that two of
        # them override, which is a known open question, not a missing field.
        self.hint_images: int = raw.get("hintImages", 0)

        self.abilities: FrozenSet[str] = frozenset(
            _CLASS_TO_ABILITY[c["type"]]
            for c in raw["controllers"]
            if c["type"] in _CLASS_TO_ABILITY and c["type"] not in NOT_PUZZLES
        )

    @property
    def repeatable(self) -> bool:
        """Generators produce a fresh puzzle per seed, so they may repeat."""
        return self.source == "generator"

    @property
    def max_instances(self) -> int:
        return MAX_GENERATOR_INSTANCES if self.repeatable else 1

    @property
    def has_parts(self) -> bool:
        """Whether this level contributes controller checks.

        Only levels with more than one group do. On a single-group level the
        group check and the first solution check are the same event, and
        minting both would double count.
        """
        return len(self.parts) > 1

    def __repr__(self) -> str:      # pragma: no cover - debugging aid
        return f"<Level {self.level_id!r} {self.source}>"


LEVELS: List[Level] = [Level(r) for r in _LEVELS_RAW["levels"]]
BY_ID: Dict[str, Level] = {l.level_id: l for l in LEVELS}

GENERATORS: List[Level] = [l for l in LEVELS if l.source == "generator"]
ARCHIVE: List[Level] = [l for l in LEVELS if l.source == "archive"]
BASE: List[Level] = [l for l in LEVELS if l.source == "base"]

#: Abilities some generator can produce. Everything else can only come from a
#: hand-made level, which is why the draw reserves slots for them.
GENERATOR_ABILITIES: FrozenSet[str] = frozenset(
    a for l in GENERATORS for a in l.abilities
)

#: Stacking, Containers, Furniture and Jigsaw in the base game. Derived rather
#: than hardcoded so adding DLC - which does have stacking and container
#: generators - corrects it by itself.
GAP_ABILITIES: List[str] = [a for a in ABILITIES if a not in GENERATOR_ABILITIES]


def levels_with(ability: str) -> List[Level]:
    return [l for l in LEVELS if ability in l.abilities]


def get(level_id: str) -> Optional[Level]:
    return BY_ID.get(level_id)


#: The seasonal event packs, and the level-id prefix identifying each. Used by
#: the archive_packs option; the display names already read as "(Good Tidings)".
ARCHIVE_PACKS: Dict[str, str] = {
    "good_tidings": "GoodTidings_",
    "trick_or_tidy": "TrickOrTidy_",
    "merry_mess": "MerryMess_",
    "drawer_chores": "NeatStreak_",
    "something_eggstra": "SomethingEggstra ",
    "snack_pack": "SnackPack ",
}


def pack_of(level: Level) -> Optional[str]:
    for key, prefix in ARCHIVE_PACKS.items():
        if level.level_id.startswith(prefix):
            return key
    return None
