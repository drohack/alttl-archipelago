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
                     PerGameCommonOptions, Range, Toggle)

from . import data


class PuzzleCount(Range):
    """How many puzzles the run contains, 10 to 130.

    The run has no chapters: its puzzles are drawn from the sources under
    "Where The Puzzles Come From" and opened pack by pack. The smallest run, 10, is
    two full packs of 5. The largest, 130, is where the level select's
    overview strip reaches the smallest it will draw; the game's own campaign
    is 79.

    THE DEFAULT IS 70, NOT 79, AND THAT IS ABOUT THE LEVEL SELECT. The game
    lays out one dot per card along the bottom of that screen, sized for its
    own widest campaign - about 85. A run adds a pack divider between blocks,
    so 79 puzzles builds 92 cards and the strip runs off the edge of the
    screen. 70 puzzles at the default pack size is 70 + 13 dividers + the
    credits = 84, which fits. Turn it up if you want the whole game; the strip
    scales itself down past that, it just gets smaller.
    """
    display_name = "Puzzle Count"
    range_start = 10
    range_end = 130
    default = 70


class PackSize(Range):
    """How many puzzles each Puzzle Pack unlocks, 5 to 20.

    Vanilla hands you one level at a time. Five means you always have several
    things to work on, so a single hard puzzle never stops the run. It is also
    the least the run opens at a time, so the run cannot start on a puzzle an
    unlucky ability draw has locked.

    EVERY BLOCK IS THIS SIZE, including the free opening - only the last is
    short, because a run rarely divides evenly. If the run is long enough that
    packs of this size would need more packs than it can carry, they all grow
    together rather than the run growing a ramp: at 79 puzzles every pack is
    6. So this is a floor on how much the run opens at a time, not the rate
    for the first pack only.
    """
    display_name = "Puzzles Per Pack"
    range_start = 5
    range_end = 20
    default = 5


class GeneratorWeight(Range):
    """How strongly to favour procedurally generated puzzles, 0 to 100.

    Sixteen of the game's puzzles are generators that build a fresh layout from
    a seed, so they are new even if you have finished the game.

    This and the four weights below are RELATIVE, compared with each other:
    they do not have to add up to 100, and 80/10/10 means the same as 8/1/1.
    A weight of 20 is twice as likely as 10; 0 means never. Every puzzle the
    mechanic reserve does not place rolls one source, each weight divided by
    the sum of those in play, so turning a DLC on adds its weight to the sum
    and every other share shrinks a little. With the defaults a puzzle rolls a
    generator 80% of the time, 73% with one DLC on and 67% with both.

    A one-shot source that runs out falls back to generators, and if every
    weight is 0 the run is all generators.
    """
    display_name = "Generated Puzzle Weight"
    range_start = 0
    range_end = 100
    default = 80


class ArchiveWeight(Range):
    """How strongly to favour the seasonal event puzzles, 0 to 100.

    A relative weight, compared with the other puzzle weights (see Generated
    Puzzle Weight). Good Tidings, Trick or Tidy, Merry Mess, Snack Pack, Something Eggstra and
    Drawer Chores were limited-time events, so most players have never seen
    them. They are also the richest levels for checks.
    """
    display_name = "Event Puzzle Weight"
    range_start = 0
    range_end = 100
    default = 10


class BaseWeight(Range):
    """How strongly to favour puzzles from the main campaign, 0 to 100.

    A relative weight, compared with the other puzzle weights (see Generated
    Puzzle Weight). The 69 hand-made campaign puzzles are by far the largest
    pool of one-shot levels in the game - more than the events and generators
    together - so this is what stops a long run repeating the same generator
    over and over.

    Set it to 0 and the campaign appears only where Mechanic Coverage below
    drags a level in.
    """
    display_name = "Campaign Puzzle Weight"
    range_start = 0
    range_end = 100
    default = 10


class CupboardsAndDrawers(Toggle):
    """Include the Cupboards and Drawers DLC.

    Twenty-five more puzzles, built around cupboards, drawers and nested
    containers. Off by default because a seed containing them cannot be played
    without the DLC - the mod refuses to connect rather than hand you a puzzle
    that will not load.

    Turn this on only if you own it. Whoever generates the multiworld does not
    have to.
    """
    display_name = "Cupboards and Drawers DLC"


class SeeingStars(Toggle):
    """Include the Seeing Stars DLC.

    Thirty-seven more puzzles and a hundred more solutions - more alternate
    solutions than the entire base campaign has, which makes this the DLC that
    changes a Star Levels goal most. Five of its puzzles are the ones the game
    itself locks behind a star total; the mod opens those for you.

    Off by default because a seed containing them cannot be played without the
    DLC. Turn it on only if you own it.
    """
    display_name = "Seeing Stars DLC"


class CupboardsWeight(Range):
    """How strongly to favour Cupboards and Drawers puzzles, 0 to 100.

    Ignored unless that DLC is turned on. A relative weight, compared with the
    other puzzle weights (see Generated Puzzle Weight). Covers all 25 of its
    puzzles,
    Trophy Cabinet included - the DLC marks it randomizable, but the seed does
    not change its layout, so it is drawn once like the rest.
    """
    display_name = "Cupboards and Drawers Weight"
    range_start = 0
    range_end = 100
    default = 10


class StarsWeight(Range):
    """How strongly to favour Seeing Stars puzzles, 0 to 100.

    Ignored unless that DLC is turned on. A relative weight, compared with the
    other puzzle weights (see Generated Puzzle Weight). Worth more than its share if you are playing a Star Levels
    goal, since its puzzles carry most of the game's alternate solutions.
    Covers all 37 of its puzzles, Water Glasses, Figurines and Bread Crusts
    included - marked randomizable, but the seed does not change their layout,
    so each is drawn once like the rest.
    """
    display_name = "Seeing Stars Weight"
    range_start = 0
    range_end = 100
    default = 10


class MechanicCoverage(Range):
    """How many puzzles are guaranteed for each mechanic no generator can make,
    0 to 10.

    Some mechanics only exist as hand-made puzzles, so without this they turn
    up by luck or not at all. Raising it makes the run's mechanics more even
    and brings in more hand-made puzzles; without a DLC, 4 uses up every
    jigsaw puzzle and 5 every drawer one, so every run would contain all of
    them. Stacking and containers have more puzzles than that, and a DLC adds
    drawer and jigsaw puzzles, so higher numbers still reserve more.

    The scarce ones are stacking, containers, drawers and jigsaws, with or
    without the DLCs: their four puzzles the game calls randomizable keep the
    same layout whatever the seed, so each appears once and cannot stand in
    for the hand-made ones. Seeing Stars also adds distributing, which
    nothing generates.

    ABOVE ZERO, EVERY MECHANIC IS GUARANTEED AT LEAST ONE PUZZLE - not only
    the scarce ones. The rest come from generators and turn up on their own
    in a full-length run, but a short one can miss them by chance: at 20
    puzzles, Rotating was absent from half the seeds measured. This number is
    the count for the scarce ones; the rest get one each.

    Set it to 0 for a simpler run: that turns the whole reserve off, including
    the one-of-each floor, and leaves every mechanic to the weighted draw.

    This is a floor, not the only door: campaign puzzles are also drawn by the
    campaign weight above.
    """
    display_name = "Mechanic Coverage"
    range_start = 0
    range_end = 10
    default = 3


class Achievements(Toggle):
    """Make the game's puzzle achievements checks: 7 in the base game, 17
    with both DLCs. Off by default.

    Each belongs to one puzzle - Exacting Eggs to Eggs, Draw Me A Rainbow to
    Junk Drawer, I'll Take My Water Neat to Water Glasses - and is a check only
    when that puzzle is in the run, earned the way the game awards it, whether
    or not your Steam profile already has it. Achievements that name no
    puzzle (chapters, hints, finishing the campaign), that need two puzzles
    (Sweep Them On The Floor) or that repeat a check the run has (Path of
    Destruction) are not included - their clean-up checks (crumbs, shavings,
    leaves, spill) stay - and neither is Keep Away, which never fired in a
    run.

    An achievement check is a check like any other: it can hold anything,
    including what your run needs, and it needs every mechanic its puzzle
    uses. A puzzle's star waits for its achievements, and a Skip sends them,
    so a hard one never has to block you. Steam achievements themselves stay
    off while the mod is loaded.
    """
    display_name = "Achievements"


class GeneratorRepeatLimit(Range):
    """How many times one generated puzzle may appear in a run, 1 to 8.

    Only the 16 generated puzzles can repeat, each copy a new layout
    ("Pencils (Randomized)", "Pencils (Randomized) #2", ...); every other
    puzzle appears at most once. The card art is the same on every copy, so
    a low limit trades variety of picture for variety of source. 0, from
    older yamls, is read as 8.
    """
    display_name = "Generated Puzzle Repeat Limit"
    range_start = 0
    range_end = data.MAX_GENERATOR_INSTANCES
    default = data.MAX_GENERATOR_INSTANCES


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
    """How many mechanics you begin with, 0 to 13 (every mechanic).

    The run may start you with more if its opening would otherwise have too
    little to do.
    """
    display_name = "Starting Abilities"
    range_start = 0
    range_end = 13
    default = 1


class GuaranteedOpenSlots(Range):
    """How many of the opening puzzles must be solvable with the abilities you
    start with, 4 to 20.

    Insurance against an opening where everything needs a mechanic you do not
    have yet. The default, 5, is the whole opening at the default pack size.

    At least 4: below that there is too little to do for a seed to be built.
    At most the size of the opening, which is Puzzles Per Pack - a higher
    number means all of it. Ignored when Ability Locks is off.
    """
    display_name = "Guaranteed Open Puzzles"
    range_start = 4
    range_end = 20
    default = 5


class Goal(Choice):
    """What unlocks the credits.

    Beat Levels counts a puzzle once you have finished it any one way -
    every solution the level has, or a Skip.

    Collect Stars counts stars: one for every solution found, the stars the
    level select counts in each pack's header. A puzzle with three solutions
    holds three.

    A Skip fills in every check on the puzzle it clears, its solutions
    included, so a skipped puzzle's stars count too. That matches how skips
    count toward beating - but it does mean skip_count is as much a shortcut
    to a star goal as to a beaten one.
    """
    display_name = "Goal"
    option_beat_levels = 0
    option_collect_stars = 1
    alias_star_levels = 1      # the 0.4.3 name
    default = 0


class StarsToCollect(Range):
    """How many stars to collect before the credits unlock, 1 to 300: one
    star per solution found.

    Only used when the goal is Collect Stars; ignored otherwise. A default
    run of 70 puzzles holds about 91 stars (84 to 100, measured over 200
    seeds), so the default of 65 is what beating 50 of them is worth. The
    most any run can hold is 279. Clamped to the stars the run holds.
    """
    display_name = "Stars To Collect"
    range_start = 1
    range_end = 300
    default = 65      # 50/70 of the 91.5 stars a default run holds


class LevelsToBeat(Range):
    """How many puzzles to beat before the credits unlock, 1 to 130.

    A puzzle counts once you have solved it any one way, or skipped it. Skips do
    count. Clamped to the puzzle count.
    """
    display_name = "Puzzles To Beat"
    range_start = 1
    range_end = 130
    default = 50      # of the default 70-puzzle run; droha's call, not a ratio


class CatTrapChance(Range):
    """Percentage of filler items that are the cat knocking your work over,
    0 to 100.

    Costs you time, never progress. 0 means no Cat Traps.
    """
    display_name = "Cat Trap Chance"
    range_start = 0
    range_end = 100
    default = 15


class BackgroundTrapChance(Range):
    """Percentage of filler items that are Background Change Traps, 0 to 100.

    A Background Change Trap recolours every backdrop - the puzzle, the pause
    screen and the level select - and a puzzle's pieces can hide against the
    new colour. A Background Reset Token, spent from the pause menu, puts them
    back. 0 means no Background Change Traps.

    Taken from the same filler as the Cat Traps. Whatever filler is left after
    the traps and the Hint Pages is split evenly between more Hint Pages and
    Background Reset Tokens.
    """
    display_name = "Background Change Trap Chance"
    range_start = 0
    range_end = 100
    default = 15


class HintCoverage(Range):
    """Percentage of this seed's hint pages that get a Hint Page item, 0 to
    100.

    A percentage rather than a count, because how many pages a run contains
    depends on what it drew: every puzzle in the game has at least one, most
    have exactly one, and some run to five. This is a percentage of whatever
    your seed actually holds, so 50 means about half its pages - enough to
    open the hints
    you actually reach for, without the pool being nothing but hints.

    Turn it up if you want the reassurance of knowing every notepad in the run
    can be opened. Note that at 100 the hint items crowd out almost everything
    else, and can exceed the number of item slots the seed has to give.

    A floor, not the only source: the filler left after the traps and these
    is split evenly between more Hint Pages and Background Reset Tokens, so a
    seed usually holds more.

    Without a Hint Page the notepad still opens, so you can see that a hint
    exists and how long it is - you just cannot erase the scribble.
    """
    display_name = "Hint Coverage"
    range_start = 0
    range_end = 100
    default = 50


class SkipCount(Range):
    """How many Skip items are shuffled in, 0 to 50.

    A Skip clears a puzzle you are stuck on outright: every solution, every
    controller group, its achievements and the "beaten" credit are all sent,
    so a skipped puzzle is finished and its card completes.

    Skipped puzzles DO count towards the credits requirement, so a large number
    of Skips is a shortcut to the goal. That is the trade for a skipped card
    being able to complete at all - withholding the credit left the card one
    location short for the rest of the run. Lower this if the shortcut bothers
    you.
    """
    display_name = "Skips"
    range_start = 0
    range_end = 50
    default = 5


@dataclass
class ALTTLOptions(PerGameCommonOptions):
    puzzle_count: PuzzleCount
    pack_size: PackSize
    generator_weight: GeneratorWeight
    archive_weight: ArchiveWeight
    base_weight: BaseWeight
    mechanic_coverage: MechanicCoverage
    generator_repeat_limit: GeneratorRepeatLimit
    achievements: Achievements
    archive_packs: ArchivePacks
    cupboards_and_drawers: CupboardsAndDrawers
    seeing_stars: SeeingStars
    cupboards_weight: CupboardsWeight
    stars_weight: StarsWeight
    ability_locks: AbilityLocks
    starting_abilities: StartingAbilities
    guaranteed_open_slots: GuaranteedOpenSlots
    goal: Goal
    levels_to_beat: LevelsToBeat
    stars_to_collect: StarsToCollect
    cat_trap_chance: CatTrapChance
    background_trap_chance: BackgroundTrapChance
    skip_count: SkipCount
    hint_coverage: HintCoverage


option_groups = [
    OptionGroup("The Run", [
        PuzzleCount,
        PackSize,
        Goal,
        LevelsToBeat,
        StarsToCollect,
    ]),
    OptionGroup("DLC", [
        CupboardsAndDrawers,
        SeeingStars,
    ], start_collapsed=True),
    OptionGroup("Where The Puzzles Come From", [
        GeneratorWeight,
        ArchiveWeight,
        BaseWeight,
        CupboardsWeight,
        StarsWeight,
        ArchivePacks,
        MechanicCoverage,
        GeneratorRepeatLimit,
    ], start_collapsed=True),
    OptionGroup("Abilities", [
        AbilityLocks,
        StartingAbilities,
        GuaranteedOpenSlots,
    ], start_collapsed=True),
    OptionGroup("Items And Checks", [
        SkipCount,
        HintCoverage,
        CatTrapChance,
        BackgroundTrapChance,
        Achievements,
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
