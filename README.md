# A Little To The Left - Archipelago (research)

Feasibility research for an [Archipelago](https://archipelago.gg) multiworld
randomizer for [A Little To The Left](https://store.steampowered.com/app/1629520/).

**Status: research only.** Nothing is designed or built yet. What exists is a
BepInEx probe that proves the game is moddable and answers what a randomizer
would be able to drive.

## The short answer

Yes, and unusually easily.

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

`src/ALTTLProbe/` is a BepInEx plugin. It reads and writes files; it does not
change the game.

Build (game must be closed):

```
cp src/GameDir.props.example src/GameDir.props   # point it at your install
dotnet build src/ALTTLProbe
```

Drive it by writing a command into `<game>/BepInEx/alttl-probe-commands.txt`:

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
| `solve:<index>[:<solutionId>]` | Write a completion entry into the save |
| `resetlevels` | Reset level completion data to a fresh save |
| `reorder:<i1,i2,...>` or `reorder:off` | Replace the level-select track with an arbitrary level list |
| `shot:<abs path>` | Screenshot |
| `inert:list` / `inert:<controller>` / `inert:off` | Dim and disable one controller's objects on the active level |
| `lockcard:<index>` / `lockcard:off` | Refuse launches of a level |
| `clickcard:<index>` | Invoke `LevelIcon.DoStartLevel` the way a real click does |
| `tint` / `tint:refresh` | Recolour every level-select card border, optionally forcing a repaint |

Gameplay events land in `BepInEx/alttl-events.log`.

Curated copies of the probe output are in [docs/data/](docs/data/).

## Repository layout

- `src/ALTTLArchipelago/` - the BepInEx mod (ships in releases)
- `src/ALTTLArchipelago.Core/` - the mod's rules and state as pure C# with no
  Unity dependency, so all of it is unit-testable. It has zero references by
  design and CI builds it standalone to keep it that way
- `src/ALTTLArchipelago.Core.Tests/` - those tests
- `src/ALTTLDevTools/` - the research and survey plugin. Deliberately not part
  of the randomizer, installed separately, never shipped
- `apworld/alttl/` - the Archipelago world (Python)
- `docs/verification-log.md` - results of the Phase 0 verification gate
- `docs/research-findings.md` - the modding surface: what was proven, and how
- `docs/content-report.md` - the content: base game, daily, archive, DLC
- `docs/data/` - the level table and controller survey the probe produced
