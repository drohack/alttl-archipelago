# Test fixtures

Data files that tests and harness tools READ. They are not documentation and
not reference data - a wrong edit here turns a suite red.

| File | Written by | Read by |
|---|---|---|
| `slot-data-example.json` | `apworld/alttl/test/test_slot_data.py` with `ALTTL_WRITE_GOLDEN=1` | that same test, plus `ExampleSeed.cs` and `SlotDataTests.cs` in the Core suite |
| `controller-survey.tsv` | DevTools' prefab walk (`docs/dev/devtools.md`) | `SurveyCrossCheckTests.cs`, which the csproj copies to the test output, and `tools/classify-controllers.py` |
| `solution-ids-observed.tsv` | harvested from `LevelComplete` lines in play and gate logs (2026-09-27); re-harvest, never hand-edit | `SolutionPartsTests.cs`, which pins how many observed ending ids name a controller |
| `forceability.jsonl` | `tools/probe-forceable.py` (`--all`, `--recheck`, `--skip-all`) | `tools/test_scheduler.py`, `tools/probe-forceable.py`, `tools/release_e2e.py`; its `groups` also fed `finishesAlone` in levels.json (2026-09-28) |
| `forced-endings.tsv` | `tools/harvest-forced-endings.py`, from the gate logs; re-harvest, never hand-edit | `tools/release_e2e.py` (which ending a forced completion files) and `tools/test_scheduler.py` |
| `id-table-0.3.4.json` | frozen at 0.3.4 | `apworld/alttl/test/test_regression.py` |
| `plan-0.3.4.json` | frozen at 0.3.4 | `test_regression.py`, `test_dlc.py` |

**Why they are not in `apworld/alttl/data/`.** Everything under the world
package ships inside the `.apworld`, and a 40KB test fixture has no business in
a player's install.

**Why they are not in `docs/data/` any more.** They were, and it meant the C#
test project reached two directories up into `docs/` for a fixture - so editing
what looked like a document could turn the Core suite red. Reference dumps
live in `docs/reference/` now; fixtures stay here.

**`slot-data-example.json` is a golden file.** It is the guard against the
apworld's `fill_slot_data()` and the mod's `SlotData` drifting apart: Python
writes a real payload, C# parses that exact file, and a field renamed on one
side fails a test instead of reading as null in someone's game. Regenerate it
deliberately, and read the diff:

```
ALTTL_WRITE_GOLDEN=1 python -m unittest worlds.alttl.test.test_slot_data
```

The one field that is deliberately NOT pinned is `world_version`: it carries a
placeholder, because pinning the literal made every release bump fail this
test. That the field is present and matches the manifest is checked separately,
by reading the manifest.
