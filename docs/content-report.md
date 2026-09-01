# Content report: what the game actually contains

Companion to [research-findings.md](research-findings.md), which covers the
modding surface. This one covers the *content* and the *player-facing flow*.

All numbers verified against build 24060652 / game version 3.6.1 on
2026-09-01. **DLC is excluded from every count unless a section says
otherwise.**

---

# Part 1 - The base game

## Size

| | Count |
|---|---:|
| Level-select track entries | **85** |
| Playable puzzles | **79** |
| Chapter marker cards (not playable) | 5 |
| Credits (playable, 0 solutions) | 1 |
| **Total distinct solutions** | **108** |

There are three `Credits` entries in the build, but only one belongs to the
base game (index 84). The other two are DLC1's and DLC2's.

There are **no cat interludes in the base game.** All four "Cat Moment"
entries (0 solutions, a short animation instead of a puzzle) are DLC1. The
base game has cat *events* inside ordinary puzzles - the cat walks in and
knocks something over - but they are not separate level entries.

## Chapters

The five chapters partition the campaign exactly, with no gaps:

| # | Title | Level indices | Puzzles | Solutions |
|---|---|---|---:|---:|
| 1 | Home Sweet Home | 1-20 | 20 | 26 |
| 2 | Lost Recipe | 22-37 | 16 | 23 |
| 3 | Nitty Gritty | 39-54 | 16 | 26 |
| 4 | Inner Nature | 56-70 | 15 | 20 |
| 5 | Near Earth Organizer | 72-83, +84 Credits | 12 | 13 |

The chapter marker itself occupies an index (0, 21, 38, 55, 71), which is why
the track has 85 cards for 79 puzzles.

This is the only grouping the game itself ships. It is thematic (kitchen,
workshop, nature, desk), not mechanical - see the mechanic taxonomy in
research-findings.md for the grouping that actually tracks puzzle *type*.

## Multiple solutions

**22 of 79 puzzles (27%) have more than one solution.** The base game caps at
three; four- and five-solution puzzles are DLC and event content only.

| Solutions | Puzzles |
|---:|---:|
| 1 | 57 |
| 2 | 15 |
| 3 | 7 |

The seven three-solution puzzles: Pencils 3, Jars, Coins 1 (Shape), Keys,
Coins 2 (Dirtyness), Leaves, Gems (Simple).

The fifteen two-solution puzzles: Books, Books 2, Books 3, Sharp Pencils,
Soup Cans, Spoons, Bowls, Spice Jars, Fruit Stickers, PaintCans, Buttons,
Storage Boxes, Boxes (Stacked), Rock Gradient, Candles.

Note the shape of that list: multiple solutions cluster on collections of
similar objects that can be sorted by more than one attribute (books by
height *or* colour, coins by shape *or* cleanliness). That is the same
property the procedural generators exploit.

## What the level select looks like

A single horizontally scrolling **filmstrip of framed cards**, one card per
entry, chapter markers inline.

- **Header**: `CHAPTER n - [star] 6/26 (24%)` and the chapter title. The
  fraction is **solutions found / solutions in this chapter**, not levels
  beaten. Completion percentage is a solution count throughout the game.
- **Unlocked card**: the puzzle's real artwork, in full colour, on a coloured
  ground.
- **Locked card**: the same artwork as a **white line-art silhouette** on the
  background colour. Locked does not mean hidden - you can see it is a stamp,
  or books, or a pin. It is a preview, not a mystery box.
- **Solution stars**: a row of stars under each card, one per solution found.
- **Bottom scrollbar**: one dot per entry across the whole campaign, filled
  for unlocked and hollow for locked, with yellow brackets marking the current
  chapter's span and yellow-outlined dots marking the chapter cards. The player
  can always see how long the run is and how far in they are.
- Top-left X closes back to the title.

## How unlocking works

Verified by resetting the save and watching the state change.

- **`LevelInterface.IsUnlocked` is exactly "this level has a
  `LevelCompletionData` entry in the save."** Nothing else feeds it.
- A fresh save has **one** entry (the chapter 0 marker). Opening the level
  select creates the entry for the first puzzle. So a new player starts with
  exactly one playable card and 84 silhouettes.
- Calling `SaveData.SaveLevelData(level, solutionId, ...)` on a level with
  **no** entry creates the entry and records no solution. Calling it on a level
  that **already has** an entry appends the solution. That two-step behaviour is
  the vanilla progression: finishing level N records N's solution and creates
  N+1's empty entry.
- **So yes: beat one, get one.** Strictly linear, in fixed track order, one
  card at a time.
- `unlockedOnLevelSelect` is a separate per-level flag meaning "this card has
  already played its unlock animation". `LevelsTrack.PerformLevelsUnlock`
  animates the queue in when you next open the menu; that is the little
  reveal you see after finishing a puzzle.
- Base-game levels have `NumStarsReqToUnlock = 0` and `IsManualUnlock = false`.
  Nothing in the base campaign is gated on a star total - but the mechanism
  exists and DLC2 uses it (see below).

## What winning looks like

There is no boss and no final puzzle in the usual sense. The campaign ends by
running out of track:

- Index 83 (TupperwareTower) is the last puzzle, and index **84 is Credits** -
  the final card in chapter 5, unlocked like any other. It is a playable
  "level" with 0 solutions: an interactive credits scene.
- `GameEvent_CampaignFinished` fires, and `CampaignFinishAchievementChecker`
  awards up to three achievements: **finish the campaign**, **finish with no
  hints**, and **finish with no skips**.
- Per chapter, `ChapterFinishTracker` awards two: chapter finished, and chapter
  **fully completed** (every solution found, not just one per level).
- `SaveData.newGamePlus` exists, so the game offers a replay after the credits.

Two natural completion bars, both already computed by the game:

1. **Finish** - every puzzle beaten one way: 79 levels.
2. **100%** - every solution found: 108 solutions. This is the number the
   header percentage tracks.

## Can we put an arbitrary level in an arbitrary slot?

**Yes, verified.** The screenshot test built a ten-card track containing a
Halloween archive level, a campaign puzzle from chapter 3, a Christmas archive
level, a daily-only procedural generator, a chapter-2 puzzle, a Snack Pack
level, and so on - an order and a mixture the game never produces.

Everything downstream adapted with no extra work: the section title read
"Randomized", the star counter recomputed to `0/22` (the total solutions of
the custom set), the bottom scrollbar shrank to exactly ten dots, and locked
entries rendered as silhouettes exactly as they do in the campaign.

The hook is `LevelSelect.SetLevels()` and `LevelSelect.SetupSections()`, both
`virtual`. This is not a hack: the game's own `ArchiveMenu` is a `LevelSelect`
subclass that overrides exactly these two methods to show a different level
set in the same track. A Harmony postfix on them is doing what the game
already does.

Two practical notes:

- The menu object is **built once and cached**. Reopening it does not re-run
  `Setup`, so the override has to be in place before the menu is first created,
  or `SetLevels()`/`SetupSections()`/`LevelsTrack.Init()` must be called
  explicitly to force a rebuild.
- The literal word `CHAPTER n` in the header comes from `SetMenuHeader` /
  `SetMenuTitle`, which are also virtual and also patchable.

---

# Part 2 - Everything that is not the base campaign

## Daily Tidy

**The daily pool is not 6 levels - it is 36.** The "6" is only the count of
levels that exist *nowhere else*. The daily draws from three sources:

| Source | Count | Notes |
|---|---:|---|
| Campaign puzzles that carry a randomizer | 10 | Breadtags, Clock, Microscope, Shells, Spice Jars, SpiderWeb, Telescope, Trim Plant, Buttons, Calendar |
| Daily-exclusive generators | 6 | Books, Batteries, Stamps, Post-It Notes, Pencils (all "(Randomized)"), plus Procedural Grid Puzzle |
| Holiday levels, pinned to calendar dates | 20 | The four seasonal packs, see below |

### Yes, the generators are far more than 6 puzzles

All 16 non-holiday daily levels are **procedural generators**, not fixed
puzzles. Each is a `*_LevelRandomizer` class driven by a
`ScriptableObject` of generation rules, seeded by `levelRandomSeed`.

`BooksRandomizerData`, for example, carries minimum and maximum book counts,
spine images, symbol sets, illegal colour pairings, incompatible solution
pairings, and width/height ranges. And crucially it varies **the sorting rule
itself**:

```
Books:   IMAGE, HEIGHT, HEIGHT_SYMMETRIC, WIDTH, WIDTH_SYMMETRIC, COLOUR, SYMBOL   (7)
Pencils: LENGTH, LEAD_SIZE, ERASER_SIZE, BODY_COLOUR, LEAD_HARDNESS                (5)
```

So "Books (Randomized)" is not one puzzle with a shuffled layout. Each seed
picks a different set of books *and* a different pair of attributes to sort
them by. The Procedural Grid Puzzle goes further and generates its layout by
recursive rectangle subdivision (`GridPuzzleGenerator.BasicRecursiveSubdivision`)
with a rules checker that rejects degenerate results.

**This is effectively an unbounded supply of puzzles**, and the seed is fully
under our control - `StartLevel(index, ..., randomSeed)` was verified to force
seed 424242 on Pencils (Randomized).

### How the calendar works

`DailyTidySequenceDetails` on this install: `randomSeed = 123456789`,
`sequenceCount = 100`, `levelRepeatMinDays = 6`. The game shuffles the eligible
level list into a 100-day sequence with a minimum six-day gap before a level
repeats, then indexes into it by date. Both the sequence seed and any single
day's `{ levelIndex, levelRandomSeed, isOverride }` can be overwritten, so we
are not limited to serving today's puzzle.

There is also a streak system: `CurrentStreak`, `LongestStreak`,
`StreakThresholds`, `StreakState` of `{ Active, Risk, Recovered, Broken,
Completed }`, and a badge collection.

## The Archive and the event packs

**Archive = the permanent home for limited-time seasonal events.**

Each pack was a `DailyTidySpecialEvent` with a `startDate` and `endDate`.
During its window its levels appeared as bonus dailies; afterwards they moved
into the Archive, which is a second `LevelSelect` (`ArchiveMenu`) reached from
the menu, grouped into named sections rather than chapters.

| Pack | Levels | Solutions | Calendar-pinned dailies? |
|---|---:|---:|---|
| Good Tidings | 6 | 8 | yes |
| Trick Or Tidy | 5 | 12 | yes |
| Merry Mess | 5 | 11 | yes |
| Something Eggstra | 4 | 7 | yes |
| Snack Pack | 3 | 5 | no |
| Drawer Chores (`NeatStreak_*`) | 3 | 3 | no |
| **Total** | **26** | **46** | 20 of 26 |

The 20 pinned ones are the holiday levels in the daily pool above. Snack Pack
and Drawer Chores ran as date-range events rather than fixed calendar dates,
so they are archive-only.

Archive levels are also the one place the game publishes solution id strings
directly, via `ArchiveManager.archiveLevelSolutionsDict`:

```
GoodTidings_Cookies        -> RowColSets_0 | RowColSets_1 | RowColSets_2
SomethingEggstra Egg Cups  -> Shuffleables-Eggs_0 | Shuffleables-Eggs_1 | Shuffleables-Eggs_2
```

## Bonus content totals (no DLC)

| Group | Levels | Solutions |
|---|---:|---:|
| Archive / event packs | 26 | 46 |
| Daily-exclusive generators | 6 | 8 (per instance, unbounded across seeds) |
| **Base campaign, for comparison** | **79** | **108** |
| **Non-DLC grand total** | **111** | **162** |

## DLC, noted and set aside

Both DLCs are defined in the base build whether or not they are installed, so
adding them later is a data change, not a code change. Their controllers are
all classes the base game already uses, plus a handful of bespoke ones.

| DLC | App ID | Owned here | Levels | Solutions | Notes |
|---|---|---|---:|---:|---|
| DLC1 *Cupboards & Drawers* | 2343790 | yes | 25 puzzles (+4 cat interludes, +1 credits) | 32 | |
| DLC2 *Seeing Stars* | 2828160 | no | 37 | 100 | 5 bonus levels gated on 50/60/70/80/90 solution-stars |

DLC2 is the interesting one for two reasons: it is more solutions than the
base campaign has, and its five bonus levels are the only shipped example of
a level locked behind a **total solution count**
(`IsManualUnlock = true`, `NumStarsReqToUnlock = 50..90`). That is a working,
in-engine precedent for a randomizer goal condition, with a "locked, needs N
stars" presentation already drawn.

Trying to load an uninstalled DLC's level throws `Il2CppException`, so
availability must be checked via `DLCManager.DLCInfo[].Installed` before a
level is offered.
