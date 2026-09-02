# A Little To The Left - Archipelago randomizer design

Date: 2026-09-01
Status: design agreed, not implemented

Supporting research: [research-findings.md](../../research-findings.md) (the
modding surface) and [content-report.md](../../content-report.md) (what the
game contains). Every number below traces to
[docs/data/](../../data/) unless marked as an estimate.

Scope: **base game only.** DLC1 and DLC2 are designed around but not built.

---

## 1. What this is

An Archipelago multiworld randomizer for A Little To The Left.

The player gets a run shaped like the base campaign - a scrolling filmstrip of
85 cards - but the puzzles in it are mostly **procedurally generated**, drawn
from the game's 16 seeded generators, mixed with the seasonal event packs from
the Archive and a light sprinkling of base-campaign levels.

Two things are shuffled into the multiworld:

- **Puzzle packs** open four slots at a time instead of vanilla's one.
- **Abilities** gate individual object controllers, so a level can be
  partially solvable - you clear the parts you have the verbs for and come
  back for the rest.

The design assumes the player has already beaten the base game. Generators are
preferred for freshness and replayability, not to hide content.

## 2. Content sources

| Source | Distinct levels | Repeatable | Avg checks/level | Multi-controller |
|---|---:|---|---:|---:|
| Generators (16 seeded randomizers) | 16 | yes, new seed each time | 1.62 | 18% |
| Archive (6 seasonal event packs) | 26 | no | 3.46 | 42% |
| Base campaign (non-generator) | 69 | no | 2.62 | 30% |

The 16 generators are the 6 daily-exclusive ones (Books, Batteries, Stamps,
Post-It Notes, Pencils, Procedural Grid Puzzle) plus the 10 campaign puzzles
that carry a randomizer (Breadtags, Buttons, Calendar, Clock, Microscope,
Shells, Spice Jars, SpiderWeb, Telescope, Trim Plant).

Generator behaviour, measured over 8 seeds each (128 regenerations):

- **Solution count is fixed per generator, never varies by seed.** Books 2,
  Pencils 2, Buttons 2, Spice Jars 2, all others 1. `SolutionCount` is
  therefore reliable static metadata for logic.
- **Object count varies substantially** - Shells 16-35, Calendar 11-24, Trim
  Plant 18-26, Batteries 14-22, Books 7-12. This is the only difficulty signal
  the game offers; it ships no difficulty rating of any kind.
- **Books and Pencils pick two distinct sorting rules per seed** from sets of 7
  and 5 (`RandomizedBookSolutionType`, `RandomizedPencilSuccessType`), and
  those fields are public and settable.
- Generators self-validate, so a seed cannot produce an unsolvable puzzle.

## 3. Run structure

**85 cards, the same shape as vanilla:** 79 puzzle slots, 5 chapter markers,
credits last. Chapter markers keep their vanilla names and, at the default
`puzzle_count` of 79, their vanilla 20/16/16/15/12 sizes.

**The chapter sizes do not rescale.** A lower `puzzle_count` truncates the
track, so a short run simply ends part-way through a chapter. Keeping the
boundaries fixed means "chapter 2, third card" refers to the same position in
every run, which matters for the in-game display and for anyone reading a
tracker.

Each of the 79 slots is assigned at generation time by the apworld, sampled
with weights defaulting to **generator 60 / archive 30 / base 10**. A
generator slot records the generator plus a specific seed derived from the AP
seed. Archive and base slots draw without replacement.

**Location names are content-based, not positional** - "Cookies Jigsaw (Good
Tidings) - Match Reindeer", "Books (Randomized) #2 - Solution 2".

This is forced. Archipelago's `location_name_to_id` is a `ClassVar` folded
into the datapackage checksum, so the name set must be identical for every
seed of the game. Slot 7 holds a different puzzle in every seed, so anything
of the form "Slot 07 - ..." cannot exist. The set of *levels* is fixed, so
naming by content works; a generator occupying several slots is numbered
(" #2", " #3"), which caps repeats at `MaxGeneratorInstances`.

The game shows no level name anywhere, so the mod adds a label under each
card. Verified working: labels render in the game's own font beneath every
card in the level select.

**Unlocking:** one pack's worth of slots is open at start; the rest arrive as
`Progressive Puzzle Pack` items, each opening the next `pack_size` slots in
track order. The pack count is
`ceil((puzzle_count - pack_size) / pack_size)`, and the final pack opens
whatever remains rather than a full group. At the defaults that is 4 slots
free and **19 packs** - 18 of four and one of three. The filmstrip fills left
to right exactly like vanilla.

**Goal:** complete the credits card.

Credits is **its own progression item**, shuffled into the multiworld like any
other - it can come from this world or another player's, early or late. It is
not attached to a pack. When it arrives the card appears at the end of the
filmstrip, and it stays locked showing "beat N puzzles" until
`levels_to_beat` levels have been beaten (default 40, at least one solution
each; skipped levels do not count).

That makes the threshold the real gate rather than pack count, and keeps the
finale visually last however early its unlock turns up.

## 4. Locations

Two kinds, roughly 180 total at default weights.

| Kind | Rule | Requires |
|---|---|---|
| Solution | one per distinct solution on a level | `Pack(slot)` + **every** ability the level uses |
| Controller | one per controller GROUP, **only on levels with more than one** | `Pack(slot)` + that group's abilities, including its transitive dependencies |

A controller *group* rather than a controller: mutually dependent controllers
(`matchDependencySolutions`) are one puzzle wearing two hats and collapse into
a single location. Measured in Phase 0 - 9 of 380 controllers declare a
dependency, and 4 of the 5 affected levels are mutual pairs.

A solution is an alternate arrangement of the *whole* level, so it cannot be
recorded without finishing every controller - hence the stricter requirement.
Single-controller levels get no controller check, so nothing is double
counted: a plain generator pays exactly one check per solution.

Worked examples: a Post-It Notes generator instance pays 1. Books pays 2.
GoodTidings_Cookies (1 controller, 3 solutions) pays 3. MedicineCabinet (14
controllers, 1 solution) pays 15. Desktop Computer (7 real controllers, 1
solution) pays 8.

**Controller filtering.** Not every `ObjectController` is a puzzle.
`Pannables` and similarly-named "Manager" controllers are camera-pan helpers,
and some visual groups carry two controllers under one name (Radial Dance
Party lists "Radial Cat Toys 1" as both a `RadialDance` and a `Draggables`).
Both are excluded: filter the `Pannables` class, and de-duplicate controllers
sharing a GameObject name within a level.

## 5. Abilities

12 items, each gating one or more `ObjectController` classes. `Draggables` is
the baseline verb - "pick up and put down", taught in level 1 - and is never
an item.

| Ability | Controller classes | Levels gated (gen/arch/base) |
|---|---|---:|
| Swapping | Shuffleables, ShuffleablesRelative, ShuffleablesRepeatingPattern, CrackersShuffleables | 19 (2/7/10) |
| Stacking | StackablesY, StackablesZ, DraggablesStacked, TupperwareTower | 12 (0/2/10) |
| Ordering | DraggablesOrdered, Indexables | 11 (1/3/7) |
| Gadgets | RecordPlayer, ComputerErrorsController, HourglassController, MatchboxesObjectController, Toggleables, AnimScrubbables, Groupables, ScrollFieldGroupsController, TelescopeDraggables, CandlesObjectController | 10 (2/1/7) |
| Rotating | Rotateables, Frame_Rotateables, RadialDance | 9 (1/0/8) |
| Grids | GridPuzzle, StackableGrid | 9 (1/3/5) |
| Tidying | Removables, Pluckables, Dirtyables, Clearables, CleanablesController | 8 (2/0/6) |
| Containers | Containables, TupperwareLids, TupperwareNesting | 8 (0/4/4) |
| Furniture | DrawerController, Cupboard, HangingToolsController | 5 (0/3/2) |
| Sticking | Stickables, SortingItems_Draggables | 4 (2/0/2) |
| Symmetry | SymmetricalPlaceables | 4 (1/1/2) |
| Jigsaw | DraggablesJigsaw | 4 (0/4/0) |

The grouping exists because a raw class-per-ability fails badly: `Draggables`
touches 48 of 111 eligible levels, and 23 of the 41 classes touch exactly one
level each. Grouped this way every ability gates between 4 and 19 levels, so
all of them matter.

With `Draggables` free, **34 of 111 eligible levels are playable holding no
abilities at all**, including four generators (SpiderWeb, Batteries, Stamps,
Post-It Notes). 60 levels need exactly one ability, 11 need two, 6 need three
or four.

**In-level behaviour.** Opening a level you hold only some abilities for is
allowed. Objects belonging to a locked controller are dimmed and
non-interactive with a lock affordance. The player solves what they can; each
controller solved is a check. The level counts as beaten only once every
controller is solved.

## 6. Items

| Item | Count | Kind |
|---|---:|---|
| Progressive Puzzle Pack | 19 | progression |
| Abilities | up to 12 | progression |
| Skip | `skip_count`, default 5 | useful |
| Cat trap | `cat_trap_chance` of filler | trap |
| Cosmetic junk (title themes, colour schemes, badges) | remainder | filler |

**Cat traps** use the game's own `CatSwipe` / `CatEvent` /
`CatSwipe_DropObjects` - the cat walks in and knocks the arrangement over. It
costs time, never progress.

**Skips** use the built-in skip (`SkipTooltip`, `Skipped` in the save). They
are logic-neutral: packs arrive from the multiworld rather than from beating
levels, so a Skip cannot shortcut progression, and skipped levels do not count
toward `levels_to_beat`.

**Hints are deliberately not items.** They remain available in-game exactly as
vanilla. The reason is that a hint is a hand-drawn picture of the solution
under erasable scribble, and only Books and Pencils override
`GetRandomizerHints()` - the other 14 generators fall back to a generic
seed-independent drawing, because the arrangement changes every seed. On a
generator-heavy pool a Hint item would mostly buy a vague picture the player
can already open for free.

## 7. Logic and guarantees

**Sphere 1 must be forced.** At 60/30/10 roughly 25% of the pool is
zero-ability, so the chance that none of the opening four slots is playable is
about 32%. Nearly a third of seeds would open with nothing to do. Therefore:

- `starting_abilities` (default 1) grants random abilities at game start.
- `guaranteed_open_slots` (default 1) forces that many slots **in the opening
  pack** to be playable with what the player starts holding - either a
  zero-ability level, or one served by a granted starting ability.

Both are configurable, including to zero, but not both to zero, and
`guaranteed_open_slots` is clamped to `pack_size`.

**Four abilities have no generator at all**, measured from the level table:

| Ability | generators | archive | base |
|---|---:|---:|---:|
| Stacking | 0 | 2 | 10 |
| Containers | 0 | 4 | 4 |
| Furniture | 0 | 3 | 1 |
| Jigsaw | 0 | 4 | 0 |

At a 60% generator weight the draw can leave one token Jigsaw level or, with
archive set to zero, none - at which point the ability is pruned and a whole
family of puzzle vanishes. Jigsaw is the worst case: four levels, all archive.

So the draw **reserves slots for gap mechanics before the weights get a say**,
via `MechanicCoverage.Reserve`. It is cheap because these levels overlap
heavily - NeatStreak_Paper Plane Supplies alone covers Containers, Furniture
and Jigsaw - so guaranteeing three of each costs about 8 of 79 slots. The
option is `mechanic_coverage` (default 2). An unmeetable demand returns what
it can rather than failing generation, since Furniture only exists four times.

**General ability coverage must also be forced, and sometimes cannot be met.**
Therefore:

1. Seed the draw with one level per ability where the enabled sources allow.
2. Fill the remaining slots by weight.
3. Drop from the item pool any ability that still gates nothing.

Step 3 is the safety net that keeps "all abilities matter" true even under
hostile yaml.

**Slot validity.** Generators self-validate, but the world should still
confirm at slot-fill time that a chosen generator and seed produce a level.
One of 128 rows in the generator sweep failed to record; that was a probe
timing artifact rather than a bad seed, but it is the right place for a guard.

**Testing.** Three tiers, mirroring the CW4 project:

1. Pure C# unit tests for slot assignment, ability mapping, location naming,
   and the reachability rules - no game required.
2. apworld tests in an Archipelago clone: generation across many seeds, all
   yaml permutations, completion checks.
3. In-game batteries driven by the probe's file-command channel.

## 8. Level-select presentation

The pack lock keeps vanilla's language untouched: **silhouette** = slot not
yet opened, **full-colour art** = opened.

The ability marker rides on `LevelIcon.borderImage` (already a per-icon
recolourable Image, used for chapter borders), following the Archipelago
tracker convention:

| Border | Meaning |
|---|---|
| red | opened, but no ability it needs is held - nothing to do yet |
| yellow | partially doable, some controllers dimmed |
| green | fully doable with what is held |
| grey | every check on this card is collected |

The star row underneath keeps its vanilla meaning - one star per solution
found - which on single-controller levels already is the check counter.

The credits card uses the existing `lockedStarIcon` to show its requirement,
reusing the game's own locked-with-a-requirement idiom rather than inventing
one. DLC2's five bonus levels are the shipped precedent for this
(`IsManualUnlock`, `NumStarsReqToUnlock` of 50-90).

Level-select contents are replaced through `LevelSelect.SetLevels()` and
`LevelSelect.SetupSections()`, both virtual. This is the game's own extension
point - `ArchiveMenu` is a `LevelSelect` subclass overriding exactly these two
methods. Verified working: a 10-card track mixing archive, campaign and
daily-only generators rendered correctly, with the section title, star
fraction and scrollbar all adapting automatically.

Note the menu object is built once and cached, so the override must be in
place before first construction, or `SetLevels()` / `SetupSections()` /
`LevelsTrack.Init()` must be called to force a rebuild.

## 9. Yaml options

```yaml
A Little to the Left:
  # --- what goes in the run ---
  puzzle_count: 79              # slots on the track (excludes chapter markers + credits)
  pack_size: 4                  # slots per Progressive Puzzle Pack
  mechanic_coverage: 2          # min levels guaranteed per generator-less ability
                                # (Stacking, Containers, Furniture, Jigsaw)

  source_weights:               # relative weight per slot; set any to 0 to exclude
    generator: 60
    archive: 30
    base: 10

  generator_instance_cap: 0     # 0 = uncapped; else max repeats of any one generator

  archive_packs:                # which event packs may be drawn from
    good_tidings: true
    trick_or_tidy: true
    merry_mess: true
    snack_pack: true
    something_eggstra: true
    drawer_chores: true

  # --- abilities ---
  ability_locks: true           # false = start with all 12; packs are the only gate
  starting_abilities: 1
  guaranteed_open_slots: 1      # opening-pack slots that must be playable at start

  # --- goal ---
  levels_to_beat: 40            # levels beaten (>=1 solution) before credits unlocks

  # --- items ---
  cat_trap_chance: 10           # percent of filler slots that become cat traps
  skip_count: 5

  # --- dlc (not yet implemented) ---
  include_dlc1: false
  include_dlc2: false
```

"Only generated levels" is `generator: 100, archive: 0, base: 0`; the
ability-pruning rule handles the resulting Jigsaw and Furniture fallout.

Validation the world enforces:

- `levels_to_beat <= puzzle_count`
- at least one source weight non-zero
- at least one archive pack enabled when archive weight is non-zero
- enough distinct levels exist to fill `puzzle_count` under the weights and
  the instance cap
- `starting_abilities` and `guaranteed_open_slots` not both zero

## 10. What is verified and what is not

Verified in-game (see research-findings.md for method):

- BepInEx 6 IL2CPP loads; interop assemblies decompile with real names
- The full 186-level table is readable at the title screen
- Any level launches from anywhere with a forced seed, regardless of lock state
- `GameEvent_LevelComplete` reaches a mod listener
- The level-select track can be replaced with an arbitrary level list, and the
  title, star fraction and scrollbar follow
- Unlock state is exactly "this level has a `LevelCompletionData` entry"
- Generator solution counts are fixed per generator; object counts vary by seed
- Books and Pencils expose settable sorting-rule fields

**Not verified, and load-bearing:**

| Unproven | Why it matters |
|---|---|
| Dimmed, inert controllers | The entire partial-play mechanic rests on this. Nothing has been built or tested. **Highest risk item in the design.** |
| `GameEvent_ObjectControllerSolved` actually fires | Controller checks depend on it. It exists in the event table with `ObjectController.IsSolved` / `SetSolved`, but has not been observed firing. |
| A real solve reporting a real `SolutionId` | Solution checks depend on it. Forced completion bypasses the controllers and records nothing. Needs a human to solve one puzzle with the probe running. |
| Recolouring `borderImage` survives `RefreshIconAppearance` | The tracker marker depends on it. |
| Holding a level locked against the player | Reading `IsUnlocked` works; forcing a lock has not been attempted. |

The first three should be settled before implementation starts, because a
failure in any of them changes the design rather than the code.

## 11. Future work

- **DLC1 and DLC2** as opt-in sources. Both are defined in the base build
  whether installed or not, so this is a data change rather than a code
  change; availability must be checked via `DLCManager.DLCInfo[].Installed`
  because loading an uninstalled DLC's level throws.
- **More filler item kinds.** Three kinds is thin for roughly 150 slots.
- **Difficulty ramp.** The game ships no rating, but object count per seed is
  measurable and several generators expose size knobs directly
  (`GridPuzzleLayoutGenerator.BlockCountRange` and `GridSize`,
  `BooksRandomizerData.minimumBookCount` / `maximumBookCount`). A ramp across
  the five chapters is buildable, just not inheritable.
- **Forcing generator sorting rules.** `firstSolution` / `secondSolution` are
  settable on Books and Pencils, so an ability could in principle be a solving
  *rule* rather than a controller class. Rejected for v1 because only two
  generators expose rule enums.
- **`base_chapters` filter.** Rejected as spoiler control, but might be worth
  it for someone wanting a short run of specific levels.

## 12. Open risks

- **The dimmed-controller treatment is unbuilt and unproven.** If it turns out
  a controller cannot be cleanly disabled without the level's win check
  breaking, partial play collapses and abilities have to gate whole levels
  instead - which changes the location model too.
- **Repetition.** At default weights each generator appears about three times
  with identical card art. Distinct puzzles, same picture. May read as
  repetitive; `generator_instance_cap` exists as a mitigation but has no
  default.
- **Check density on generator-heavy configs.** A 100% generator run yields
  roughly 130 checks against 180 at default weights, and almost no
  multi-controller levels, so abilities gate whole levels rather than parts.
  It works, but it is a noticeably thinner game.
