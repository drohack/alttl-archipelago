"""Player options.

House rules, following cw4:

- The docstring IS the webhost tooltip, so it is written for a player rather
  than a maintainer.
- Nothing raises. A hostile or nonsensical yaml degrades into something that
  still generates, and the degradation is what gets tested. An option that
  refuses to generate is a worse failure than one that quietly does its best.
- Numbers that matter to the game travel in slot_data, never in item names, so
  item ids stay identical across yamls.
"""

from dataclasses import dataclass

from Options import (Choice, DefaultOnToggle, OptionGroup, OptionSet,
                     PerGameCommonOptions, Range)

from . import data


class PuzzleCount(Range):
    """How many puzzles the run contains.

    The full game is 79, laid out over five chapters exactly as vanilla. A
    lower number truncates the track rather than rescaling it, so a short run
    simply ends part way through a chapter.
    """
    display_name = "Puzzle Count"
    range_start = 8
    range_end = 79
    default = 79


class PackSize(Range):
    """How many puzzles the first Puzzle Packs unlock.

    Vanilla hands you one level at a time. Four means you always have several
    things to work on, so a single hard puzzle never stops the run.

    Packs widen as the run goes on, so this is where you start rather than the
    rate for the whole game - the opening is deliberately slow, and the last
    packs hand you a good deal more than this. Whatever you pick, the run opens
    with at least four puzzles so it cannot start locked.
    """
    display_name = "Puzzles Per Pack"
    range_start = 1
    range_end = 10
    default = 4


class GeneratorWeight(Range):
    """How strongly to favour procedurally generated puzzles.

    Sixteen of the game's puzzles are generators that build a fresh layout from
    a seed, so they are new even if you have finished the game. Weighed against
    the archive weight below.
    """
    display_name = "Generated Puzzle Weight"
    range_start = 0
    range_end = 100
    default = 70


class ArchiveWeight(Range):
    """How strongly to favour the seasonal event puzzles.

    Good Tidings, Trick or Tidy, Merry Mess, Snack Pack, Something Eggstra and
    Drawer Chores were limited-time events, so most players have never seen
    them. They are also the richest levels for checks.
    """
    display_name = "Event Puzzle Weight"
    range_start = 0
    range_end = 100
    default = 30


class MechanicCoverage(Range):
    """How many puzzles are guaranteed for each mechanic no generator can make.

    Stacking, containers, drawers and jigsaws only exist as hand-made puzzles.
    Without this they turn up by luck or not at all. Raising it makes the run's
    mechanics more even and brings in more base-game puzzles; 4 uses up every
    drawer puzzle in the game, so every run would contain all of them.

    Base-game puzzles enter the run ONLY through this setting. Set it to 0 and
    a run is generated and event puzzles only, at the cost of losing four
    mechanics entirely.
    """
    display_name = "Mechanic Coverage"
    range_start = 0
    range_end = 6
    default = 3


class GeneratorRepeatLimit(Range):
    """Most times any one generated puzzle may appear. 0 uses the maximum.

    Each appearance is a genuinely different layout, but the card art is the
    same, so a low limit trades variety of picture for variety of source.
    """
    display_name = "Generated Puzzle Repeat Limit"
    range_start = 0
    range_end = data.MAX_GENERATOR_INSTANCES
    default = 0


class ArchivePacks(OptionSet):
    """Which seasonal event packs may appear.

    Removing a pack removes its puzzles. Note the jigsaw mechanic exists only
    in event packs, so turning enough of them off drops jigsaws from the run
    and the ability along with them.
    """
    display_name = "Event Packs"
    valid_keys = sorted(data.ARCHIVE_PACKS)
    default = frozenset(data.ARCHIVE_PACKS)


class AbilityLocks(DefaultOnToggle):
    """Lock puzzle mechanics behind items.

    On, the objects for a mechanic you have not unlocked sit dimmed and
    untouchable, so a puzzle can be partly solved and returned to later. Off,
    every mechanic works from the start and the packs are the only gate.
    """
    display_name = "Ability Locks"


class StartingAbilities(Range):
    """How many mechanics you begin with."""
    display_name = "Starting Abilities"
    range_start = 0
    range_end = 6
    default = 1


class GuaranteedOpenSlots(Range):
    """How many of the opening puzzles must be solvable straight away.

    Insurance against an opening where everything needs a mechanic you do not
    have yet. Raise it to be handed a wider choice on the first screen.

    Four are always solvable whatever you set here - below that there is too
    little to do for a seed to be built at all - so this only has an effect
    above four.
    """
    display_name = "Guaranteed Open Puzzles"
    range_start = 0
    range_end = 10
    default = 4


class LevelsToBeat(Range):
    """How many puzzles to beat before the credits unlock.

    A puzzle counts once you have solved it any one way. Skipped puzzles do not
    count. Clamped to the puzzle count.
    """
    display_name = "Puzzles To Beat"
    range_start = 1
    range_end = 79
    default = 40


class CatTrapChance(Range):
    """Percentage of filler items that are the cat knocking your work over.

    Costs you time, never progress.
    """
    display_name = "Cat Trap Chance"
    range_start = 0
    range_end = 100
    default = 10


class SkipCount(Range):
    """How many Skip items are shuffled in.

    A Skip clears a puzzle you are stuck on. Skipped puzzles do not count
    towards the credits requirement.
    """
    display_name = "Skips"
    range_start = 0
    range_end = 20
    default = 5


@dataclass
class ALTTLOptions(PerGameCommonOptions):
    puzzle_count: PuzzleCount
    pack_size: PackSize
    generator_weight: GeneratorWeight
    archive_weight: ArchiveWeight
    mechanic_coverage: MechanicCoverage
    generator_repeat_limit: GeneratorRepeatLimit
    archive_packs: ArchivePacks
    ability_locks: AbilityLocks
    starting_abilities: StartingAbilities
    guaranteed_open_slots: GuaranteedOpenSlots
    levels_to_beat: LevelsToBeat
    cat_trap_chance: CatTrapChance
    skip_count: SkipCount


option_groups = [
    OptionGroup("Goal", [
        LevelsToBeat,
        PuzzleCount,
        PackSize,
    ]),
    OptionGroup("What Goes In The Run", [
        GeneratorWeight,
        ArchiveWeight,
        MechanicCoverage,
        GeneratorRepeatLimit,
        ArchivePacks,
    ], start_collapsed=True),
    OptionGroup("Abilities", [
        AbilityLocks,
        StartingAbilities,
        GuaranteedOpenSlots,
    ], start_collapsed=True),
    OptionGroup("Items", [
        SkipCount,
        CatTrapChance,
    ], start_collapsed=True),
]


options_presets = {
    # The lever is mechanic_coverage: it decides how much hand-made content is
    # pulled in to supply the four mechanics no generator can produce.
    "Fresh Tidying": {
        "mechanic_coverage": 2,
    },
    "Balanced": {
        "mechanic_coverage": 3,
    },
    "Every Mechanic": {
        "mechanic_coverage": 4,
    },
}
