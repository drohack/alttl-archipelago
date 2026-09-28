# Developing

How the repository is laid out, how to build and test it without disturbing a
player's install, and where each kind of documentation lives. The rules an
agent works by - the test ladder, background runs, the game and the test
server - are in the repo's `CLAUDE.md`.

## Layout

| Path | What it is |
|---|---|
| `src/ALTTLArchipelago/` | The BepInEx mod that plays a seed. Deliberately thin: the Unity and Archipelago-client glue, and nothing decidable without them |
| `src/ALTTLArchipelago.Core/` | The mod's rules and state as pure C#, unit-tested. Must never reference Unity, BepInEx or the game's interop assemblies; CI builds it on a runner with no game. Ordinary .NET packages only by exception, each with its reason in the csproj |
| `src/ALTTLArchipelago.Core.Tests/` | Those tests (xUnit), including tests against the shipped `levels.json` and the fixtures |
| `src/ALTTLModKit/` | Pieces that know nothing about Archipelago or this game: toasts, a typing guard, a main-thread dispatch queue, a virtual-desktop helper. A separate project so a reference back into the mod cannot compile |
| `src/ALTTLDevTools/` | The research plugin: commands dropped into a file, answered in the log. Never shipped. Reference: `devtools.md` |
| `apworld/alttl/` | The Archipelago world (Python): options, items, locations, the draw, rules, slot_data |
| `apworld/alttl/data/` | The level table and its companions; how to maintain it: `level-data.md` |
| `apworld/alttl/docs/` | The game page and setup guide the Archipelago website serves. Names and folder fixed by the website |
| `tools/` | Build, release, harness, hand-test and probe scripts; index in `tools/README.md` |
| `fixtures/` | Data files tests read, in both languages; see `fixtures/README.md` |
| `docs/` | Player install guide at the top, then `dev/`, `reference/`, `history/` (below) |

## Building the mod

```
cp src/GameDir.props.example src/GameDir.props   # point it at your install
bash tools/deploy.sh --no-kill                   # compile only, never deploys
bash tools/deploy.sh                             # close the game, build, deploy
```

BepInEx must have been launched once so the interop assemblies exist. **A
plain `dotnet build src/ALTTLArchipelago` deploys into the game whenever it is
closed**, so while anyone is playing use `--no-kill` or
`-p:SkipDeploy=true`. `tools/package-release.py` builds with SkipDeploy.

The plugin is the one thing CI cannot build: it references interop assemblies
generated from a local install. That is why `ALTTLArchipelago.Core` exists
without game references - everything decidable without Unity lives there and
is tested in CI, including the slot_data contract against a payload the
generator really produced.

## The Archipelago world

`apworld/alttl/` needs an Archipelago checkout to run against; the repo
expects a clone at `Archipelago/`, which is gitignored.

```
powershell tools/ap-sync.ps1                              # copy the world in
cd Archipelago
py -3.13 -m unittest discover -s worlds/alttl/test -t .
ALTTL_STRESS_SEEDS=25 py -3.13 -m unittest worlds.alttl.test.test_fill_stress
```

The fill is seed-dependent, so a single seed proves very little; widen the
stress sweep (`ALTTL_STRESS_SEEDS=200`) before a release. `tools/mutate-apworld.py`
puts known DLC bugs back and fails if the tests miss one. To roll a real seed,
put a yaml in a folder and run `py -3.13 Generate.py --player_files_path <folder>`.

## Tests and gates

| Question | Where |
|---|---|
| Core logic | `dotnet test src/ALTTLArchipelago.Core.Tests` |
| The world | the unittest and stress commands above |
| The release gate's data and choices, on paper | `tools/make-seed.py`, `test_harness_data.py`, `test_scheduler.py`, `predict_gate.py` |
| One level, or one lock fix, in the game | `testing.md` |
| The release | `release-testing.md` |

The full ladder, with the cost of each rung, is in `CLAUDE.md`.

## CI

`.github/workflows/ci.yml` runs, with no game installed: Core built and
tested; the version check, SlotData defaults, `check-docs.py`,
`check-patches.py` and `check-devtools.py`; an ASCII-only scan of every
source and doc file; the world's unit tests and fill stress against the
minimum and latest Archipelago releases; Archipelago's own world compliance
suite; the packaged `.apworld` loading and generating a real seed; and a check
that packaging refuses a version mismatch.

## Documentation

| Folder | For |
|---|---|
| `README.md`, `docs/installation.md` | players |
| `apworld/alttl/docs/` | players and hosts, on the Archipelago website |
| `docs/dev/` | working on the mod: this guide, `devtools.md`, `testing.md`, `release-testing.md`, `level-data.md`, `backlog.md` |
| `docs/reference/` | facts about the game, some written by tools: content counts, icon provenance, level endings, controller classes |
| `docs/history/` | closed records kept because code comments cite them as evidence; not current guidance |
| `CHANGELOG.md` | what changed per release, and why |

Hand-test results go in `apworld/alttl/data/proven-requirements.json` and edges
in `levels.json` (`level-data.md`), not in a doc. New requests go in
`backlog.md`, each with what was asked and what is already known.
