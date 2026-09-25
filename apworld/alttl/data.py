"""The game's content, loaded from the shared data files.

Four files, all in data/. The first three are shared with the C# mod so the
generator and the game cannot disagree about what exists:

  levels.json     measured from the game by the DevTools levelsweep command
  abilities.json  the authored ability grouping
  names.json      exported from ALTTLArchipelago.Core
  proven-requirements.json   which levels' part requirements are established

The fourth is generator-side only and deliberately so: it records what we have
CHECKED, not what the game contains, and the mod has no business trusting it.

Nothing here reimplements naming or grouping logic. That was deliberate: the
display-name rules are fiddly (camel splitting, pack prefixes moving to
suffixes, a per-level collision fallback) and a second implementation in
another language would drift. These strings ARE Archipelago location names, so
drift would mean the generator and the mod disagreeing about what a check is
called.
"""

import json
import pkgutil
from typing import Dict, FrozenSet, Iterable, List, Optional


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


def _world_version() -> str:
    """This world's version, from the manifest that ships with it.

    Read rather than hardcoded so it cannot drift: archipelago.json is one of
    the three files tools/check-version.py pins together, so this is the same
    number the mod's assembly carries. The mod compares them at connect -
    location ids move between releases, and a mismatched pair plays a subtly
    wrong game otherwise.

    pkgutil, not open(), for the same reason as _load: a shipped world is a
    zip and its files have no path on disk. An unreadable manifest yields ""
    rather than raising - the mod treats an unknown version as "cannot say"
    and allows the run, and failing to generate over a missing string would
    be far worse than the mismatch it guards against.
    """
    try:
        raw = pkgutil.get_data(__package__, "archipelago.json")
        if raw is None:
            return ""
        return str(json.loads(raw.decode("utf-8")).get("world_version", ""))
    except Exception:       # pragma: no cover - a packaging error only
        return ""


WORLD_VERSION: str = _world_version()

_LEVELS_RAW = _load("levels.json")
_ABILITIES_RAW = _load("abilities.json")
_NAMES_RAW = _load("names.json")
_PROVEN_RAW = _load("proven-requirements.json")

#: Levels whose PART requirements somebody has actually established, either by
#: measuring the containment out of the game or by playing the level holding
#: exactly what a part declares. See data/proven-requirements.json for why an
#: absent level is not the same as a correct one.
PROVEN_LEVELS: FrozenSet[str] = frozenset(_PROVEN_RAW.get("proven", {}))

#: Highest number of times one generator may appear in a run. The static
#: location table names every instance up front, so this is a correctness
#: bound rather than a preference.
MAX_GENERATOR_INSTANCES: int = _NAMES_RAW["maxGeneratorInstances"]

CREDITS: str = _NAMES_RAW["credits"]

#: Ability name -> the ObjectController classes it unlocks. The BASE GAME's
#: twelve only; DLC mechanics are below.
ABILITY_CLASSES: Dict[str, List[str]] = _ABILITIES_RAW["abilities"]

#: Ability names in a stable order. Item ids hang off this, so it must not be
#: reordered once shipped.
ABILITIES: List[str] = list(ABILITY_CLASSES)

#: DLC key -> the abilities that DLC introduces -> their controller classes.
#: A DLC with no new mechanic is present with an empty mapping rather than
#: absent, so "this DLC adds nothing" is recorded rather than missing.
DLC_ABILITY_CLASSES: Dict[str, Dict[str, List[str]]] =     _ABILITIES_RAW.get("dlcAbilities", {})

#: Every DLC ability, base-twelve order first then DLC by key. This ORDER is
#: what items.py appends after every base item name, so it carries the same
#: no-reordering rule for the same reason.
DLC_ABILITIES: List[str] = [
    a for key in sorted(DLC_ABILITY_CLASSES)
    for a in DLC_ABILITY_CLASSES[key]
]

#: Base twelve, then DLC. Anything asking "what abilities exist" wants this;
#: anything allocating an item id wants the two lists separately.
ALL_ABILITIES: List[str] = ABILITIES + DLC_ABILITIES

_ALL_ABILITY_CLASSES: Dict[str, List[str]] = dict(ABILITY_CLASSES)
for _key in sorted(DLC_ABILITY_CLASSES):
    _ALL_ABILITY_CLASSES.update(DLC_ABILITY_CLASSES[_key])

#: Controller class -> the ability that gates it. A class ABSENT from this map
#: needs no ability: abilities.json's "baseline" list (Draggables,
#: GenericLevelObjects) is the pick-up-and-put-down verb every player always
#: has, so it is never an item and deliberately has no entry here.
#:
#: There used to be a BASELINE constant naming them. It was removed when
#: nothing read it and its docstring was left stranded below this line,
#: describing a binding that no longer existed - which reads as documentation
#: for whatever happens to follow it.
_CLASS_TO_ABILITY = {c: a for a, cs in _ALL_ABILITY_CLASSES.items() for c in cs}

#: Classes that are not puzzles at all, such as camera-pan helpers.
NOT_PUZZLES: FrozenSet[str] = frozenset(_ABILITIES_RAW["notPuzzles"])

#: The ability that OPENS things. A drawer or cupboard is not a puzzle you
#: solve beside the others - it is the lid on top of them, and what is inside
#: cannot be touched until it is open. Named here rather than inlined because
#: the structural-gap test below is the only place the distinction matters and
#: it should be findable.
OPENER_ABILITY = "Drawer"

#: Levels whose part requirements are known to be incomplete for a reason
#: levels.json cannot show, because the sweep never recorded the thing that
#: proves it. Keys are level ids, values say what is missing.
#:
#: These come from docs/data/controller-classes.tsv and from play logs, both of
#: which know about controllers the shipped table does not have. They are
#: listed rather than derived because the derivation needs data that is
#: currently thrown away - see tools/merge-levels.py, which drops the sweep's
#: `drawers` field, and src/ALTTLDevTools/DataTable.cs DrawersOf, which
#: measures it. Recovering that makes most of this list redundant.
SUSPECT_LEVELS: Dict[str, str] = _PROVEN_RAW.get("suspect", {})


class Level:
    """One level, with everything the generator needs to place it."""

    __slots__ = ("level_id", "level_index", "source", "dlc", "solution_count",
                 "display", "parts", "part_abilities", "abilities",
                 "enforced_abilities", "enforced_part_abilities",
                 "controller_group", "not_locations", "hint_images",
                 "level_class", "structural_gap", "_behind_an_opener",
                 "_gap_is_level_wide")

    def __init__(self, raw: dict):
        self.level_id: str = raw["levelId"]
        self.level_index: int = raw["levelIndex"]
        # generator | archive | base | dlc1 | dlc2
        self.source: str = raw["source"]

        # Which DLC the player must own, or "" for base content. NOT derivable
        # from source: four DLC levels carry the game's randomizer flag, so
        # their source is "generator" while they still need Seeing Stars or
        # Cupboards and Drawers installed.
        self.dlc: str = raw.get("dlc", "")

        self.solution_count: int = raw["solutionCount"]

        names = _NAMES_RAW["levels"][self.level_id]
        self.display: str = names["display"]

        # Controller groups, in the same order Core produces them, so "the Nth
        # group" means the same thing on both sides. dict preserves insertion
        # order, and Core emits them sorted.
        parts_raw = names["parts"]
        self.parts: List[str] = [p["display"] for p in parts_raw.values()]

        # What each group needs ON ITS OWN, which is far less than the level as
        # a whole. Re-measured 2026-09-09 across all 200 groups: 57 need
        # nothing, 135 need exactly one ability and 8 need two.
        #
        # The old figures here - "106 groups, 32 nothing, 74 one, none needs
        # two" - were true when written and had drifted badly by the time
        # anyone looked. Both halves moved for real reasons: the drawer,
        # assembly and phase dependencies each push a group's requirement up
        # through the transitive closure, and the phase audit restored eleven
        # groups the sweep had never seen.
        #
        # Core computes these, including the closure over one-way dependencies,
        # so the requirement is not re-derived here. Keyed by display name
        # because that is what the location name is built from; verified unique
        # within a level.
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

        # Controllers the game registers that are deliberately no location
        # (notALocation in levels.json): solved the moment the level opens, or
        # never reported solved at all. Sent so the mod's audit does not call
        # them a mismatch.
        self.not_locations: FrozenSet[str] = frozenset(
            c["name"] for c in raw["controllers"] if c.get("notALocation"))

        # How many hint pages this level's notepad holds. Not one per level:
        # most have one, 31 have between two and five.
        #
        # The MAXIMUM of two sources, and it has to be. LevelInterface
        # .HintImages misses the six generator puzzles entirely - they report
        # zero there and still hand the player a real notepad, because a
        # LevelRandomizer keeps its own supply and answers GetRandomizerHints()
        # instead. Reading only the first source minted no Hint Page for those
        # six while the player could spend up to two on each, and had the mod
        # telling them the puzzle had no hint at all. Found in play, not by
        # reasoning: droha opened Pencils and got a hint.
        #
        # randomizerHints is what the generated layout actually uses;
        # randomizerHintPool is the larger authored list it draws from. The
        # used count is the honest one - Books holds seven and shows two - and
        # since pages are fungible across the whole run, being a page light
        # occasionally costs far less than inflating every seed.
        self.hint_images: int = max(raw.get("hintImages", 0),
                                    raw.get("randomizerHints", 0))

        # WHAT THE GAME ACTUALLY ENFORCES, which is not always what the
        # table measures - and it is kept SEPARATE from `abilities` on
        # purpose.
        #
        # `abilities` feeds the DRAW: coverage, gap-filling, which level
        # teaches what (slots.py). test_regression pins that draw as a
        # frozen record of the 0.3.4 world, because location and item ids
        # are positional and a seed in flight is a contract between a
        # generator and a mod that are no longer running. Changing what a
        # level is worth to the draw breaks that contract, and the goldens
        # are explicitly never to be regenerated.
        #
        # The REQUIREMENT is a different question, and only rules.py asks
        # it. A gated group whose every object is also held by a baseline
        # group is freed by the dimmer's unlocked-wins merge, so demanding
        # its ability is simply false - and now that the mod withholds
        # checks the logic calls unreachable, a false requirement means a
        # player who earned something fairly is told to wait for an ability
        # the puzzle never needed.
        #
        # ONLY WHERE A PERSON HAS CONFIRMED IT. Removing a requirement is
        # the DANGEROUS direction: overstating gates harder than necessary,
        # understating lets the fill put an item somewhere unreachable. The
        # sharing dump proves the DIMMER does not gate; it cannot see a shut
        # drawer. So bypassedAbilities carries only what someone has played
        # - Books 3, where droha held zero abilities, moved all 17 books and
        # completed the Swapping arrangement.
        #
        # EIGHT LEVELS NOW CARRY IT, all played and applied: Books 3,
        # Workbench, TrickOrTidy_ChocolateBars, NeatStreak_Bathroom Drawer,
        # both DLC1 Kitchen Hanging Tools, DLC2 Junk Drawer Transforming and
        # DLC2 Combs. This comment said "the rest are candidates awaiting
        # exactly that" long after they had stopped being candidates. One
        # candidate is left and it is untestable: DLC2 Math Set, whose
        # Indexables group reports solved at load, so there is no arrangement
        # left to perform.
        bypassed = frozenset(raw.get("bypassedAbilities", []))

        # Every ability the level needs to be FINISHED - the union over its
        # registered controllers, PLUS anything the sweep could not see.
        #
        # A phased level registers only its first phase when the sweep boots
        # it, so the union from `controllers` alone is short by whatever the
        # later phases need. rules.py makes a solution location require this
        # set, so a short union tells the generator a level is finishable
        # without an ability it actually needs. See LevelInfo.ExtraAbilities
        # in Core for why this is recorded as abilities rather than as extra
        # controllers.
        # BYPASSED ABILITIES ARE SUBTRACTED LAST, after extras are added.
        #
        # extraAbilities exists because the sweep sees too LITTLE - a phased
        # level hides its later controllers. bypassedAbilities is the mirror:
        # the sweep sees a gated controller that gates nothing, because the
        # dimmer merges per object and lets UNLOCKED WIN, so a group whose
        # every object is also held by a baseline group is freed no matter
        # what the table says.
        #
        # Measured, not reasoned: tools/probe-object-sharing.py dumps which
        # controllers hold which objects and works out what frees what, and
        # docs/gate-sharing.md lists the results. Confirmed by hand on
        # Books 3 - droha held ZERO abilities, moved all 17 books and
        # completed the Swapping-gated arrangement.
        #
        # Correcting the table matters beyond tidiness now that the mod
        # withholds checks the logic calls unreachable: leave it wrong and a
        # player who legitimately earns one of these is told to wait for an
        # ability the puzzle never needed.
        self.abilities: FrozenSet[str] = frozenset(
            [
                _CLASS_TO_ABILITY[c["type"]]
                for c in raw["controllers"]
                if c["type"] in _CLASS_TO_ABILITY and c["type"] not in NOT_PUZZLES
            ]
            + list(raw.get("extraAbilities", []))
        )

        #: The requirement view. See the note above bypassed.
        self.enforced_abilities: FrozenSet[str] = self.abilities - bypassed
        self.enforced_part_abilities: Dict[str, FrozenSet[str]] = {
            part: a - bypassed for part, a in self.part_abilities.items()
        }

        self.level_class: str = raw.get("levelClass", "Level")

        # Groups sitting behind a drawer or cupboard that nothing records them
        # as being behind. The sweep harvests each controller's dependsOn, but
        # a drawer's CONTENTS are not a dependency the game expresses that way
        # - it authors them on the Drawer component instead, which
        # tools/merge-levels.py discards. So a level can hold an opener and ten
        # groups that all claim to need nothing, and the table cannot tell
        # which of the ten are inside it.
        openers = frozenset(
            c["name"] for c in raw["controllers"]
            if _CLASS_TO_ABILITY.get(c["type"]) == OPENER_ABILITY
        ) if OPENER_ABILITY not in bypassed else frozenset()
        opener_groups = {self.controller_group[name] for name in openers
                         if name in self.controller_group}
        self._behind_an_opener: FrozenSet[str] = frozenset(
            group for group in self.enforced_part_abilities
            if group not in opener_groups
            and OPENER_ABILITY not in self.enforced_part_abilities[group]
        ) if openers else frozenset()

        # WHY THIS LEVEL'S PART REQUIREMENTS ARE NOT TRUSTWORTHY, or "" when
        # nothing says they are suspect. See unproven_parts.
        reasons = []
        if self.level_class != "Level":
            reasons.append("bespoke level class %s, so its phase order is not "
                           "expressible as dependsOn" % self.level_class)
        if self._behind_an_opener:
            reasons.append("a drawer or cupboard whose contents the table does "
                           "not record")

        # extraAbilities is the project's own marker for "the sweep saw too
        # little here" - a phased level registers only its first phase when the
        # sweep boots it, so somebody added back what the union was missing.
        # It is applied to the LEVEL union (self.abilities above) and never to
        # a part, while bypassedAbilities, its mirror, IS applied to parts. The
        # asymmetry runs in the dangerous direction: the one mechanism for
        # under-measurement cannot repair a part location at all. So a level
        # carrying extraAbilities is a level that has already been caught
        # under-reporting, and its parts have not been corrected.
        if raw.get("extraAbilities"):
            reasons.append("extraAbilities patched the level union for "
                           "something the sweep missed, and extras never "
                           "reach a part requirement")

        # A controller that appears in neither the authored phase list nor any
        # dependency is a controller whose place in the order is unrecorded.
        # Over all 173 levels this names four, and one of them is the `Lids`
        # that killed a run.
        phases = raw.get("phases") or []
        if phases:
            named = {name for phase in phases
                     for name in (phase if isinstance(phase, list) else [phase])}
            depended = {d for c in raw["controllers"]
                        for d in (c.get("dependsOn") or [])}
            orphans = sorted(
                c["name"] for c in raw["controllers"]
                if c["name"] not in named and c["name"] not in depended
                and not (c.get("dependsOn") or []))
            if orphans:
                reasons.append("%s appear in neither the phase list nor any "
                               "dependency" % ", ".join(orphans))
            if any(name == "(none)" for name in named):
                reasons.append("the sweep wrote \"(none)\" instead of a phase "
                               "name, so the phase list records nothing")

        if self.level_id in SUSPECT_LEVELS:
            reasons.append(SUSPECT_LEVELS[self.level_id])
        self.structural_gap: str = "; ".join(reasons)

        # An opener LOCALISES the doubt: only what could be inside it is in
        # question, and the rest of the level is as trustworthy as any other.
        # Every other signal is about the level's ORDER, or about an ability
        # the sweep missed entirely, and neither says WHICH group is affected -
        # so those taint all of the level's groups.
        self._gap_is_level_wide: bool = bool(
            len(reasons) - (1 if self._behind_an_opener else 0))

    @property
    def unproven_parts(self) -> FrozenSet[str]:
        """Groups whose requirement nobody has established.

        A group that needs STRICTLY LESS than its level says the level needs
        is claiming it can be finished early. Sometimes that is true - most of
        a level's groups really are independent, and requiring the whole
        level's ability set for each one strangled the fill when it was tried.
        Sometimes it is false, and then it is the worst kind of false: the
        generator puts progression behind something the player cannot touch,
        and the run ends.

        The difference is not visible in the data. It comes from
        controllers[].dependsOn, harvested from the running game, and an edge
        the game does not express is simply absent. `Tupperware Nesting - Lids`
        declares ['Containers'] with dependsOn: [] and cannot be touched
        without Stacking; that is what killed the 2026-09-21 run. Exactly the
        same shape was found and hand-patched for four base-game levels in
        0.3.0 - see test_a_drawer_cannot_be_emptied_before_it_opens - and every
        DLC level added since walked straight back into it.

        WHICH LEVELS, and this is a narrowing that was measured rather than
        chosen. Distrusting every unverified group covers 183 of the 358 part
        locations, and that is not affordable: on a 20-puzzle run it left TWO
        reachable openings out of twelve with twenty progression items to
        place, and five configurations of test_fill_stress could not generate
        at all. The most-suspect groups are also the cheapest checks, so
        guarding all of them guts exactly the opening the fill needs.

        So the guard needs a REASON, not merely an absence of one. A level
        qualifies when something says its phase or containment data is
        incomplete: a bespoke level class whose phase order dependsOn cannot
        express, a drawer or cupboard whose contents the table does not
        record, or an entry in SUSPECT_LEVELS. That is 122 locations across 31
        levels, and it still covers both cases confirmed in play -
        TupperwareNesting and DLC2 Boss.

        THE REST ARE NOT SAFE, THEY ARE UNCHECKED, and the difference matters.
        A level with no structural gap is one where nothing has contradicted
        the table yet, which is a weaker claim than "somebody looked". The
        sweep in Phase 1 is what turns the rest from unchecked into known; this
        is a hedge for the time before that, not a substitute for it.
        """
        if self.level_id in PROVEN_LEVELS or not self.structural_gap:
            return frozenset()

        whole = self.enforced_abilities
        understated = frozenset(
            part for part, own in self.enforced_part_abilities.items()
            if own < whole
        )

        # A bespoke class or a listed suspicion taints the whole level: the
        # phase order is unknown, so any group could be behind any other. An
        # opener taints only what could be inside it.
        if self._gap_is_level_wide:
            return understated
        return understated & self._behind_an_opener

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

#: Abilities some generator can produce. Everything else can only come from a
#: hand-made level, which is why the draw reserves slots for them.
GENERATOR_ABILITIES: FrozenSet[str] = frozenset(
    a for l in GENERATORS for a in l.abilities
)


def gap_abilities(pool: Iterable[Level]) -> List[str]:
    """Abilities no generator IN THIS POOL can make.

    Stacking, Containers, Drawer and Jigsaw for the base game. The draw
    reserves slots for these, because a mechanic only hand-made levels have
    turns up by luck or not at all.

    A FUNCTION OF THE POOL, not a constant over the whole catalogue, and the
    DLCs are why. DLC1 Trophy Cabinet is a drawer generator: computed over
    every level in the table it would take Drawer out of this list for
    everyone, so a player who owns no DLC would silently lose the guaranteed
    drawer puzzle that mechanic_coverage promises them. Computed over what a
    yaml actually enabled, it shrinks only for the players who really did gain
    a generator for it.
    """
    present = frozenset(a for l in pool for a in l.abilities)
    made = frozenset(a for l in pool if l.repeatable for a in l.abilities)
    # PRESENT AND NOT MADE. An ability no level in the pool has at all is not
    # scarce, it is absent, and reserving for it is a request the draw can
    # never satisfy - the reserve loop would then run to its cap chasing it.
    # Distributing is exactly that case for any run without Seeing Stars.
    return [a for a in ALL_ABILITIES if a in present and a not in made]


def classes_for(ability: str) -> List[str]:
    """The ObjectController classes an ability unlocks, base or DLC.

    ABILITY_CLASSES alone would KeyError on a DLC ability, and slot_data asks
    this for every ability the run actually uses.
    """
    return _ALL_ABILITY_CLASSES[ability]


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
