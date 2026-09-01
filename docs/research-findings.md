# A Little To The Left - modding research findings

Everything here was verified against the real game on 2026-08-31, not inferred
from documentation. Where something is unproven it says so.

Game build: Steam app **1629520**, build 24060652, game version **3.6.1**.

## Is it moddable? Yes

| Fact | Value |
|---|---|
| Engine | Unity **2020.3.26f1** |
| Scripting backend | **IL2CPP** (`GameAssembly.dll`, `il2cpp_data/Metadata/global-metadata.dat`) |
| Asset system | Addressables 1.20.5, one bundle per level |
| Mod loader | **BepInEx 6 IL2CPP** - loads cleanly, verified `6.0.0-be.697` |
| Interop generation | 92 assemblies, ~40 seconds on first launch |
| Existing mods | **None found.** No Nexus page, no Thunderstore package, no Archipelago apworld |

There is no prior art, so everything below is first-hand.

### Installing BepInEx

Unzip a BepInEx 6 IL2CPP win-x64 build into the folder containing
`A Little To The Left.exe`, launch once, quit. That generates
`BepInEx/interop/*.dll` - the managed shims the mod compiles against. They are
derived from the game and must never be committed or redistributed.

`BepInEx/interop/Assembly-CSharp.dll` decompiles to **683 source files** with
full class, method and field names. Nothing is obfuscated. (Method *bodies*
are absent - interop assemblies are signature-only stubs - so behaviour has to
be established by experiment, which is what the probe is for.)

## The save file is plaintext JSON behind a one-character cipher

`%USERPROFILE%\AppData\LocalLow\maxinferno\A Little To The Left\save1.json`

Each character of the JSON has its **Unicode codepoint shifted by +11**, then
the result is written as UTF-8. Decode with:

```python
s = open(path, encoding='utf-8').read()
plain = "".join(chr(ord(c) - 11) for c in s)
```

There is no checksum and no signature, so the save can be read *and written*
by an external tool. Decoded shape (fields that matter):

```json
{
  "guid": 980171684,
  "version": "3.6.1",
  "dailyTidyProgress": {
    "CompleteCount": 0,
    "CurrentStreak": 0, "LongestStreak": 0,
    "m_dailyTidyHistory": [
      { "levelIndex": 12, "opened": false, "complete": false,
        "dateString": "2024-11-17", "levelRandomSeed": 2004642816,
        "isOverride": false }
    ]
  },
  "levelCompletionData": [
    { "levelId": "01__Chapter_HomeSweetHome", "numSolutions": 0,
      "solutions": [], "unlockedOnLevelSelect": false,
      "hintUsed": false, "skipped": false }
  ],
  "archiveCompletionData": [],
  "installedDlc": ["DLC1"],
  "lastPlayedLevels": [],
  "playerPrefs": { }
}
```

Three fields carry the whole randomizer:

- `levelCompletionData[].unlockedOnLevelSelect` - per-level unlock flag.
- `levelCompletionData[].solutions[]` - the set of distinct solutions found,
  each `{ "solutionId": "..." }`. The game dedupes; we do not have to.
- `dailyTidyProgress.m_dailyTidyHistory[]` - `levelIndex` + `levelRandomSeed`
  + `isOverride` per calendar date, i.e. which daily puzzle a date serves.

## The runtime API

`GameManager.Instance` is the root and holds `levelManager`, `DailyTidyManager`,
`ArchiveManager`, `DLCManager`, `HintManager`, `menuManager`, `ZoomManager`.

### LevelManager - the piece that matters most

```csharp
Il2CppReferenceArray<LevelInterface> LevelInterfaces;
LevelInterface  GetLevelInterface(int levelIndex);
LevelInterface  GetLevelInterface(string id);
Il2CppReferenceArray<LevelInterface> AllLevelInterfaces(bool availableOnly = false);
List<LevelInterface> GetAvailableLevelInterfaces(bool unlockedOnly = true);

Task StartLevel(int startLevelIndex = 0, bool showTransition = true,
                bool forceReload = false, int randomSeed = -1);
Task SetActiveLevel(int levelIndex, bool doTransitionIn = true,
                    bool forceReload = false, int randomSeed = -1);
void LevelComplete(LevelInterface levelInterface, string solutionId);
bool GameCompleteCheck();

// The game already ships a shuffled-level-order mode:
bool ShuffleActive;
int  GetShuffledLevelIndex(bool isShuffleInit = false);
void RefreshShuffleLevels(List<LevelInterface> levelsToShuffle);
```

`RefreshShuffleLevels` + `GetShuffledLevelIndex` is, essentially, a
randomizer's level-order engine already written and shipped. Feeding it the
set of unlocked levels is the natural integration point.

### LevelInterface - per-level state

`LevelId`, `LevelIndex`, `LevelType` (`Puzzle` / `Chapter`), `AddressablesKey`,
`SolutionCount`, `NumSolutionsFound`, `AllSolutionsFound`, `Solved`,
`Completed`, `Skipped`, `HintUsed`, `IsUnlocked`, `IsManualUnlock`,
`NumStarsReqToUnlock`, `RandomSeed`, `IsDailyTidy`, `IsHolidayDaily`,
`IsArchived`, `IsRandomizable`, `IsCredits`, `ChapterDetails`, `DLCDetails`,
plus `LoadLevel()`, `CompleteLevel()`, `Restart()`, `GetLevelCompletionData()`.

### GameEventManager - a typed pub/sub bus, no Harmony needed

```csharp
static void AddEventListener<T>(Il2CppSystem.Action<GameEventData> callback) where T : GameEvent;
static void RemoveEventListener<T>(Il2CppSystem.Action<GameEventData> callback) where T : GameEvent;
```

73 event types. The ones a randomizer wants:

`GameEvent_LevelSelected`, `GameEvent_LevelComplete`,
`GameEvent_LevelCompleteEarly`, `GameEvent_LevelSkipped`,
`GameEvent_LevelRandomized`, `GameEvent_LevelHintTaken`,
`GameEvent_LevelExited`, `GameEvent_CampaignFinished`,
`GameEvent_DLCFinished`, `GameEvent_ObjectControllerSolved`.

`GameEventData` carries `LevelInterface` **and `SolutionId`**, which is exactly
the (level, which-solution) pair a location check needs.

**Trap:** the IL2CPP side holds the callback through a weak wrapper. A listener
that is not rooted on the managed side stops firing after the first GC. Keep
the `Il2CppSystem.Action` in a static field or list.

### DailyTidyManager - the daily is fully steerable

```csharp
Il2CppReferenceArray<LevelInterface> GetDailyTidyLevels(bool includeHolidays = false);
void Init(DateTime date, DailyTidySequenceDetails sequenceDetails, bool resetHistory);
void SetDailyTidySequenceDetails(DailyTidySequenceDetails details); // { randomSeed, sequenceCount, levelRepeatMinDays }
void SetActiveDailyTidyDate(DateTime date);
void SetDailyDetails(DailyTidyDetails details);   // { levelIndex, levelRandomSeed, isOverride, ... }
int  GetLevelIndexAtDate(DateTime date);
static List<int> BuildLevelSequence(DailyTidySequenceDetails, LevelInterface[]);
```

Live values on this install: `sequenceRandomSeed = 123456789`,
`sequenceCount = 100`, `levelRepeatMinDays = 6`. The daily calendar is a
deterministic shuffle of the daily-eligible level list, and both the sequence
seed and any individual day's `levelIndex` / `levelRandomSeed` can be
overridden. **We are not limited to "today's" puzzle.**

### ArchiveManager

`ArchiveGroups` (title + level list + availability date) and
`archiveLevelSolutionsDict : Dictionary<string, string[]>` - the one place the
game spells out solution id strings, e.g.

```
GoodTidings_Cookies           -> RowColSets_0 | RowColSets_1 | RowColSets_2
SomethingEggstra Egg Cups     -> Shuffleables-Eggs_0 | Shuffleables-Eggs_1 | Shuffleables-Eggs_2
GoodTidings_Presents (Stacked)-> Stacked-Boxes_0
```

A solution id is `<ObjectController GameObject name>_<index>`.

## The content inventory

186 `LevelInterface` objects exist in the build, whether or not the DLC that
owns them is installed.

| Group | Levels | Solutions |
|---|---:|---:|
| Base campaign (idx 1-83) | 79 | 108 |
| Archive / event packs (Good Tidings, Snack Pack, Something Eggstra, Trick or Tidy, Merry Mess, Drawer Chores) | 26 | 46 |
| DLC1 *Cupboards & Drawers* (idx 1100-1129) | 25 | 32 |
| Daily generators (idx 995-1000) | 6 | 8 |
| DLC2 *Seeing Stars* (idx 1200-1239) | 37 | 100 |
| Chapters / cat interludes (0 solutions) | 10 | 0 |
| Credits | 3 | 0 |
| **Total** | **186** | **294** |

Solution-count spread: 111 levels have 1, 25 have 2, 22 have 3, 8 have 4,
7 have 5.

**With DLC1 owned and DLC2 not: 136 playable puzzles, 194 solutions.**
DLC2 adds 37 puzzles and 100 solutions - it is more than a third of the total
alternate-solution content, and worth buying before designing the item pool.

### The daily pool

36 daily-eligible levels: 10 base-campaign puzzles reused as dailies
(Breadtags, Clock, Microscope, Shells, Spice Jars, SpiderWeb, Telescope,
Trim Plant, Buttons, Calendar), 6 **procedural generators** (Books, Batteries,
Stamps, Post-It Notes, Pencils, and a Procedural Grid Puzzle), and 20 holiday
levels tied to specific dates.

The 6 generators take an arbitrary `levelRandomSeed` and are therefore an
effectively unlimited supply of distinct puzzles.

### The puzzle-mechanic taxonomy is authored data

Every puzzle is built out of `ObjectController` subclasses, and the *class* is
the game's own answer to "what kind of puzzle is this". Across the 136
currently-playable puzzles the survey found 334 controllers in 45 distinct
classes. Grouped into player-facing families (a level can belong to more than
one):

| Mechanic family | Levels | Solutions | Controller classes |
|---|---:|---:|---|
| Place & arrange | 74 | 86 | `Draggables`, `SortingItems_Draggables`, `TelescopeDraggables` |
| Cupboards & drawers | 22 | 22 | `Cupboard`, `DrawerController`, `DrawerExpandableController` |
| Swap & shuffle | 21 | 48 | `Shuffleables`, `ShuffleablesRelative`, `ShuffleablesRepeatingPattern` |
| Stacking | 17 | 21 | `DraggablesStacked`, `StackablesY/Z`, `StackableGrid`, `TupperwareTower/Nesting` |
| Ordering & sequencing | 17 | 29 | `DraggablesOrdered`, `Indexables` |
| Containers & lids | 15 | 18 | `Containables`, `TupperwareLids` |
| Rotation & alignment | 13 | 13 | `Rotateables`, `Frame_Rotateables`, `RadialDance`, `SymmetricalPlaceables` |
| Panning & framing | 11 | 17 | `Pannables` |
| Cleaning & clearing | 8 | 12 | `Clearables`, `Removables`, `Pluckables`, `Dirtyables` |
| Grid puzzles | 7 | 13 | `GridPuzzle` |
| Jigsaw fitting | 5 | 5 | `DraggablesJigsaw` |
| Set-piece / bespoke | 4 | 6 | `RecordPlayer`, `HourglassController`, `ComputerErrorsController`, ... |

Raw data: [docs/data/controller-survey.tsv](data/controller-survey.tsv) and
[docs/data/level-table.json](data/level-table.json).

This is the taxonomy to hang "group puzzles by type and those are the unlock"
on - it is real authored structure, not a hand-made guess. Note the long tail:
`Draggables` covers over half the game, so it needs subdividing (probably by
chapter or by solution count) before it can carry an item.

### DLC2's star gate is a shipped precedent for a goal condition

DLC2's five bonus levels have `IsManualUnlock = true` and
`NumStarsReqToUnlock` of 50, 60, 70, 80, 90. The game already knows how to
lock a level behind a total-solutions count and show it as locked.

## What was proven by experiment

The probe (`src/ALTTLProbe/`) is a BepInEx plugin driven by a file-command
channel at `BepInEx/alttl-probe-commands.txt`. Commands: `dump`, `solutions`,
`state`, `boot:<index>[:<seed>]`, `complete`.

| Claim | Result |
|---|---|
| BepInEx 6 IL2CPP loads and injects a MonoBehaviour | **Works** |
| Full 186-level table readable from the title screen | **Works** - `alttl-dump.json` |
| Every level prefab can be loaded and inspected without playing it | **Works** - 186/186, DLC2's 37 throw `Il2CppException` because it is not installed |
| Launch an arbitrary level from the title screen | **Works** - `boot:1013` put an archive-only Halloween level straight into `Gameplay_GameState` |
| Launch a **daily-only** level on a non-matching day | **Works** - `boot:999:424242` launched *Pencils (Randomized)* with `unlocked=False`; the lock state does not gate a programmatic start |
| Force a specific procedural seed | **Works** - `state` reported `seed=424242` on the running level |
| `GameEvent_LevelComplete` reaches a mod listener | **Works** - fired with the `LevelInterface`, after `GameEvent_LevelCompleteEarly` |

### Still unproven

- **A real solve reporting a real `SolutionId`.** The forced `CompleteLevel()`
  path completes the level but records no solution (`found=0`), because no
  ObjectController actually solved. Needs a human to solve one puzzle with the
  probe running and read `alttl-events.log`.
- **Whether the level-select UI honours a forced lock.** `IsUnlocked` can be
  read; nothing has yet tried to *hold a level locked* against the player.
- **Re-entering a solved level to find its other solutions.** The game clearly
  supports it (`MultipleStarTutorial`, `LevelCompletionStar`,
  `SaveData.solutions[]`, `AllSolutionsFound`), but the flow has not been
  driven end to end.
- **Enumerating solution ids ahead of time for every level.** See below.

### Solution id enumeration is only partly solved

Walking a loaded level prefab with `GetComponentsInChildren<ObjectController>`
finds all 334 controllers and their names. But the count of alternate
solutions lives in a *different field per controller subclass* -
`Draggables.solutionSets.sets`, `Shuffleables.solutions`,
`GridPuzzle.solutions`, `Distributables.DistributionSolutions` - so a single
`GetComponent<SolutionSets>()` only resolves 24 of 186 levels.

This is a solvable mapping job, but it may not be worth doing: see the
ordinal-location idea in the design notes.

## Gotchas worth remembering

- **Build with the game closed.** The deploy step cannot overwrite a loaded
  DLL (MSB3021).
- **Launch with no command-line arguments.** Custom Unity args make Steam pop
  a confirmation dialog and the launch appears to hang.
- **`level.objectControllers` is empty until a level actually starts** -
  controllers self-register in their own `Start`. Walk the prefab instead.
- **Root your event-listener delegates** (see GameEventManager above).
- `ilspycmd -l` writes nothing when its stdout is redirected to a file. Use
  `-p -o <dir>` to decompile to a project instead.
