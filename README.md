# A Little To The Left - Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer for
[A Little To The Left](https://store.steampowered.com/app/1629520/), the
tidying puzzle game.

**Status: playable.** A seed generates, the game connects to it, and the run
plays through to the credits.

## What gets randomized

The run is shaped like the base campaign - a scrolling filmstrip of cards -
but most of the puzzles in it are **procedurally generated**. Sixteen of the
game's puzzles build a fresh layout from a seed, so they are new even if you
have finished the game. They are mixed with the seasonal Archive puzzles and
with hand-made campaign puzzles where those are the only source of a mechanic.

**Locations.** Every distinct solution of every puzzle is its own check, and
puzzles with several arrangements give several. Finishing a puzzle is a check
in its own right, and so is each group of objects you tidy inside one.

**Items.** Puzzle Packs, the twelve mechanic abilities, the Credits, Skips,
Hint Pages, and Background Change Traps. A Cat Trap knocks your arrangement
over - it costs time, never progress.

**The goal.** Beat a set number of puzzles, or star them, and the credits card
appears at the end of the track. Playing it finishes the run.

Two things gate progress. **Puzzle Packs** open the next block of cards.
**Abilities** unlock the mechanics themselves, and objects belonging to a
mechanic you have not found sit dimmed and cannot be moved - so a puzzle can
be partly solved, left, and come back to when the missing ability arrives.

### Ability locks

Twelve of the game's own mechanics are items. The level select carries all
twelve as a strip, so what is still out there is visible rather than something
to keep a list of.

Dim is a mechanic you have not found yet:

![Twelve ability icons on the level select, all dimmed except Gadgets](docs/images/ability-strip-locked.png)

Lit is one you hold:

![The same twelve icons, all in full colour](docs/images/ability-strip-held.png)

The icons are the game's own art rather than anything drawn for the mod - a
badge element, a puzzle piece or a level's object, one per mechanic, chosen to
be told apart at that size. Provenance for all twelve:
[docs/data/ability-icons.md](docs/data/ability-icons.md).

**Neither DLC is implemented.** No DLC puzzle is placed in a run, whichever
ones you own.

Full detail, including every option and what each item does:
[the world's game page](apworld/alttl/docs/en_A_Little_to_the_Left.md).

## Install

1. **BepInEx 6 (IL2CPP)**: unzip
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2)
   into the game folder - the one containing `A Little To The Left.exe`.
2. **First launch**: start the game, wait for the main menu, quit. This launch
   is slow because BepInEx is generating interop assemblies.
3. **Mod**: unzip `ALTTLArchipelago-X.Y.Z.zip` from the
   [releases page](../../releases) into the same game folder.
4. **Archipelago host**, only if you are generating the multiworld: install
   [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   0.6.7 or newer, put `alttl.apworld` (same release) into its
   `custom_worlds/` folder, and `A Little to the Left.yaml` into `Players/`.

The three release files ship together and carry the same version number. A mod
and an apworld that disagree about the version disagree about the item table.

Details, the yaml options and troubleshooting:
[docs/installation.md](docs/installation.md).

## Connect

Launch the game and use the **Archipelago** button on the main menu: server
address, port, slot name, password. The run appears on the level select once
you are connected.

A run also survives the server going away. If nothing answers at launch, the
mod resumes the last run from a cache of the slot data and the received items,
queues anything you earn, and sends it on the next connection.

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
  of the randomizer, installed separately, never shipped. Its commands are in
  [docs/devtools.md](docs/devtools.md)
- `apworld/alttl/` - the Archipelago world (Python)
- `docs/installation.md` - what a player does with the three release files
- `docs/research-findings.md` - the modding surface: what was proven, and how.
  This is where "is the game moddable" is answered, at length
- `docs/content-report.md` - the content: base game, daily, archive, DLC, with
  level and solution counts
- `docs/verification-log.md` - results of the Phase 0 verification gate, and
  the known gaps
- `docs/in-game-testing.md` - how to test against the one real install without
  leaving a mess in it, and the harnesses that once measured nothing
- `docs/release-testing.md` - checking a release actually works, automatically
  or by hand, and the log lines that tell you it did
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
- `tools/release-e2e.py` - the release gate: clean the install to vanilla,
  install the release assets, generate a seed, and play it through
- `tools/build_apworld.py` - package the world into a distributable
  `alttl.apworld`
- `tools/package-release.py` - build all three release assets and refuse if the
  version numbers disagree or the build produced a file it does not recognise
- `tools/check-version.py` - the version lives in three files; fail when they
  drift. `--set X.Y.Z` writes all three
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

`apworld/alttl/` is the generator side. It needs an Archipelago checkout to run
against; the repo expects a clone at `Archipelago/`, which is gitignored.

```
powershell tools/ap-sync.ps1                              # copy the world in
cd Archipelago
python -m unittest discover -s worlds/alttl/test -t .
```

The fill is seed-dependent, so a single seed proves very little - that is how a
broadly broken fill once sat behind a fully green suite. `test_fill_stress.py`
sweeps option configurations across a span of fixed seeds; widen it before a
release:

```
ALTTL_STRESS_SEEDS=200 python -m unittest worlds.alttl.test.test_fill_stress
```

To roll a real seed, put a yaml in a folder and run
`python Generate.py --player_files_path <folder>`.

## License

[MIT](LICENSE).

The BepInEx interop assemblies this repo builds against are generated from the
game, and are neither included here nor redistributable. You need your own copy
of the game to build the mod.
