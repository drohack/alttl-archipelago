# A Little To The Left - Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer for
[A Little To The Left](https://store.steampowered.com/app/1629520/).

**Status: a seed generates and the game connects to it.** Not playable yet -
the mod reports what the seed contains and does not touch the puzzles.

| Part | State |
|---|---|
| Research and the verification gate | done, [verification log](docs/verification-log.md) |
| Design | agreed, [spec](docs/superpowers/specs/2026-09-01-alttl-archipelago-design.md) |
| `apworld/alttl/` - the Archipelago world | **generates real seeds**, 91 tests |
| `src/ALTTLArchipelago.Core/` - Unity-free rules and state | 224 tests |
| `src/ALTTLArchipelago/` - the BepInEx mod | **plays a seed** |

Verified end to end on 2026-09-03: a generated seed served by `MultiServer.py`,
played in game to completion. The seed's puzzles replace the campaign on the
level select, packs reveal them in the order the generator planned, abilities
gate the objects, solving sends checks the server accepts, items arriving are
applied and announced, and the goal is reported back - the server declaring
"Team #1 has completed all of their games".

A run also survives the server going away. If no server answers at launch, the
mod resumes the last run from a cache of the slot data and the received items,
queues anything earned, and sends it on the next connection - verified in
[docs/verification-log.md](docs/verification-log.md) by `tools/offline-test.py`.

Known gaps are listed at the end of the
[verification log](docs/verification-log.md): a level that once loaded empty and
has not reproduced, and the Play button's behaviour in a run.

## Is the game moddable?

Yes, and unusually easily. This was the question the project started with, and
the answers below are what the rest of the work rests on.

- Unity 2020.3.26f1 / IL2CPP. **BepInEx 6 IL2CPP loads it cleanly.**
- Nothing is obfuscated - the interop assemblies decompile to 683 readable
  source files with real class and method names.
- The save file is plaintext JSON behind a +11 codepoint shift, with no
  checksum, and it already stores per-level unlock flags and the set of
  distinct solutions found.
- The game ships a typed event bus (`GameEventManager`) that publishes
  `GameEvent_LevelComplete` **with the solution id**, so location checks need
  no Harmony patching at all.
- The game ships a shuffled-level-order mode (`LevelManager.ShuffleActive`,
  `RefreshShuffleLevels`, `GetShuffledLevelIndex`) - a randomizer's level
  ordering engine, already written.
- Any level can be launched from anywhere with a forced procedural seed
  (`StartLevel(index, showTransition, forceReload, randomSeed)`), including
  daily-only and archive-only puzzles, regardless of lock state. **Verified.**

Full detail, including what is still unproven:
[docs/research-findings.md](docs/research-findings.md). What the game
*contains* - level counts, chapters, the unlock rule, what the level select
looks like, and what the daily and archive content actually is:
[docs/content-report.md](docs/content-report.md).

## Content inventory

186 level definitions ship in the build. With DLC1 owned and DLC2 not:
**136 playable puzzles carrying 194 distinct solutions.**

| Group | Levels | Solutions |
|---|---:|---:|
| Base campaign | 79 | 108 |
| Archive / event packs | 26 | 46 |
| DLC1 *Cupboards & Drawers* | 25 | 32 |
| Daily generators | 6 | 8 |
| DLC2 *Seeing Stars* (not owned) | 37 | 100 |

DLC2 is a third of the alternate-solution content. Worth buying before the
item pool is designed.

The 6 daily generators (Books, Batteries, Stamps, Post-It Notes, Pencils, and
a Procedural Grid Puzzle) accept an arbitrary seed, so they are an unbounded
supply of extra puzzles.

## The probe

`src/ALTTLDevTools/` is a BepInEx plugin. It reads and writes files; it does
not change the game. It is never shipped with the randomizer.

Build (game must be closed):

```
cp src/GameDir.props.example src/GameDir.props   # point it at your install
dotnet build src/ALTTLDevTools
```

Drive it by writing a command into `<game>/BepInEx/alttl-devtools-commands.txt`:

| Command | Effect |
|---|---|
| `dump` | Write the full level / daily / archive / DLC table to `BepInEx/alttl-dump.json` (also runs automatically at the main menu) |
| `solutions` | Load all 186 level prefabs and record their object controllers to `BepInEx/alttl-solutions.tsv` |
| `state` | Log the current game state and active level |
| `boot:<index>[:<seed>]` | Launch any level with an optional forced procedural seed |
| `complete` | Force-complete the active level |
| `menu:title` / `menu:levels` / `menu:archive` / `menu:daily` | Jump to a menu |
| `unlocks` | Write the campaign unlock/completion state and chapter membership |
| `sections` | Log the level-select sections and every track icon's lock state |
| `levelsweep` | Boot every level in turn and record its RUNTIME controllers to `apworld/alttl/data/levels.json`. This is the source of truth for the level table |
| `gensweep:<index>[:<n>]` | Regenerate one procedural puzzle n times and record how its layout varies |
| `cardlabels` / `cardlabels:off` | Put the level name under each level-select card |
| `solve:<index>[:<solutionId>]` | Write a completion entry into the save |
| `resetlevels` | Reset level completion data to a fresh save |
| `reorder:<i1,i2,...>` or `reorder:off` | Replace the level-select track with an arbitrary level list |
| `shot:<abs path>` | Screenshot |
| `inert:list` / `inert:<controller>` / `inert:off` | Dim and disable one controller's objects on the active level |
| `lockcard:<index>` / `lockcard:off` | Refuse launches of a level |
| `clickcard:<index>` | Invoke `LevelIcon.DoStartLevel` the way a real click does |
| `tint` / `tint:refresh` | Recolour every level-select card border, optionally forcing a repaint |
| `iconinfo:<index>` | Dump one level-select icon's child tree, with components, sizes and sibling order |
| `marker:states` / `marker:refresh` / `marker:off` | Cycle the four tracker-badge states across the cards: green, green/red split corner to corner, red, star. See S5 in the verification log |
| `unlockto:<n>` | Give the first n levels a completion entry, so the level select renders them unlocked |

Gameplay events land in `BepInEx/alttl-watch.log`, and only when the
`WatchEvents` config setting is on - `ObjectPlaced` alone fires hundreds of
times per level load, so it is off by default.

Curated copies of the probe output are in [docs/data/](docs/data/).

## Repository layout

- `src/ALTTLArchipelago/` - the BepInEx mod that plays a seed. Deliberately
  thin: it holds the Unity and Archipelago-client glue and nothing decidable
  without them
- `src/ALTTLArchipelago.Core/` - the mod's rules and state as pure C# with no
  Unity dependency, so all of it is unit-testable. It must never reference
  Unity, BepInEx or the game's interop assemblies, and CI builds it standalone
  on a runner with no game installed to keep it that way. Ordinary .NET packages
  are allowed by exception, each with a reason in the csproj - currently one,
  the Archipelago client, for the enums the protocol defines
- `src/ALTTLArchipelago.Core.Tests/` - those tests
- `src/ALTTLModKit/` - pieces that know nothing about Archipelago or this game:
  a toast overlay, a Rewired typing guard, a socket-thread-to-main-thread
  dispatch queue, and a Windows virtual-desktop helper. A separate project so a
  reference back into the mod cannot compile, which is what keeps them
  extractable
- `src/ALTTLDevTools/` - the research and survey plugin. Deliberately not part
  of the randomizer, installed separately, never shipped
- `apworld/alttl/` - the Archipelago world (Python)
- `docs/verification-log.md` - results of the Phase 0 verification gate
- `docs/in-game-testing.md` - how to test against the one real install without
  leaving a mess in it, and the harnesses that once measured nothing
- `docs/installation.md` - what a player does with the three release files
- `docs/release-testing.md` - checking a release actually works, automatically
  or by hand, and the log lines that tell you it did
- `docs/research-findings.md` - the modding surface: what was proven, and how
- `docs/content-report.md` - the content: base game, daily, archive, DLC
- `docs/data/` - the level table and controller survey the probe produced.
  Reference data only: the survey walks level *prefabs*, and the runtime
  controller set differs, so `apworld/alttl/data/levels.json` is the source of
  truth
- `tools/ap-sync.ps1` - copy `apworld/alttl` into the Archipelago clone
- `tools/offline-test.py` - five phases proving a run survives the server going
  away and rejoins when it comes back, including that a regenerated seed under
  the same slot name does not come up on the cached plan
- `tools/offline-reconnect-test.py` - the one claim that needs the pane:
  pressing Connect during an offline run against a server that is still down
  must leave the run alone
- `tools/harness_env.py` - snapshot the player's BepInEx config and save folder
  before a harness runs and restore them after, including on Ctrl-C. Wrap any
  new harness that writes either. `--restore-latest` recovers from a hard kill
- `tools/build_apworld.py` - package the world into a distributable
  `alttl.apworld`
- `tools/package-release.py` - build all three release assets and refuse if the
  version numbers disagree or the build produced a file it does not recognise
- `tools/check-version.py` - the version lives in three files; fail when they
  drift. `--set X.Y.Z` writes all three
- `CHANGELOG.md`, `docs/installation.md` - what shipped, and how to install it
- `.github/workflows/ci.yml` - Core built with no game installed, Core tests,
  the world's tests against the minimum supported Archipelago version, the
  packaged apworld generating a real seed, and an ASCII-only check

## Building the mod

```
cp src/GameDir.props.example src/GameDir.props   # point it at your install
dotnet build src/ALTTLArchipelago                # deploys into the game
```

The game must be closed, and BepInEx must have been launched once so the
interop assemblies exist.

**This is the one thing CI cannot build.** It references BepInEx interop
assemblies generated from a local install, so a runner with no game cannot
compile it. That is the whole reason `ALTTLArchipelago.Core` exists without any
game references: everything decidable without Unity lives there and IS tested in
CI, including the slot_data contract against a payload the generator really
produced. If you want CI to build the plugin too, the usual answer in the
BepInEx world is a separate repo publishing stripped reference assemblies as a
NuGet package, the way TromboneChamp and Outer Wilds do it.

To try it against a real seed, generate one, serve it with
`python Archipelago/MultiServer.py --port 38281 <seed>.zip`, then set Host,
Port and SlotName in `BepInEx/config/droha.alttl.archipelago.cfg` and launch.

## The Archipelago world

`apworld/alttl/` is the generator side, and it is the part that works today.
It needs an Archipelago checkout to run against; the repo expects a clone at
`Archipelago/`, which is gitignored.

```
powershell tools/ap-sync.ps1                              # copy the world in
cd Archipelago
python -m unittest discover -s worlds/alttl/test -t .     # 91 tests
```

The fill is seed-dependent, so a single seed proves very little - that is how a
broadly broken fill once sat behind a fully green suite. `test_fill_stress.py`
sweeps 32 option configurations across a span of fixed seeds; widen it before a
release:

```
ALTTL_STRESS_SEEDS=200 python -m unittest worlds.alttl.test.test_fill_stress
```

To roll a real seed, put a yaml in a folder and run
`python Generate.py --player_files_path <folder>`.
