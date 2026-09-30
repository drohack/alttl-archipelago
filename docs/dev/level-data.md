# Level data

`apworld/alttl/data/` holds what both the apworld and the mod know about the
game. This is how it is made and maintained.

| File | Made by | What it is |
|---|---|---|
| `levels.json` | the DevTools sweep, merged (below) | every level: controllers, phases, drawers, hints, cats |
| `abilities.json` | hand-authored | which controller classes each ability unlocks; the base twelve's key order is frozen (item ids hang off it), DLC abilities under `dlcAbilities` |
| `names.json` | Core `NamesExportTests` (`ALTTL_WRITE_GOLDEN=1 dotnet test`) | location and item names exported from C#, pinned by the Python tests |
| `proven-requirements.json` | hand-authored | dated hand-test evidence per level; read by `test_requirements`, never by the generator; see its `_comment` |

`levels.json` is the single source of truth about the game's content, consumed
by **both** the Python apworld and the C# mod. It is generated, not hand
written: `py -3.13 tools/levelsweep.py` runs the sweep with the mod correctly
parked and tells you where it put the result.

**MERGE that result, do not copy it over this file.** `tools/merge-levels.py`
appends only levels the table does not already have, and refuses to write if an
existing row would change. A wholesale copy is a REGRESSION, not an update -
see "The sweep is LOSSY on phased levels" below, and the five rows it disagreed
with the shipped table about on 2026-09-17, where the shipped side was right
every time.

The file's canonical form is `json.dumps(indent=2)`, so a script that reads it
and writes it back changes nothing but what it meant to change.

### Fields worth knowing about

- `source` is which pool a level comes from: `generator`, `archive`, `base`,
  `dlc1` or `dlc2`. A `generator` level is launched with a baked seed.
- `dlc` is which DLC the player must own, or absent for base content. **It is
  not derivable from `source`**: four DLC levels carry the game's own
  randomizer flag, so their source is `generator` while they still need the DLC
  installed. Eligibility and the draw's weights both key off `dlc`
  (`Level.draw_source`); the seed off `source`.
- `controllers[]`: `name`, `type` (the controller class, which `abilities.json`
  maps to an ability), `objects`, `dependsOn` (controllers that must be solved
  before this one can be), and `notALocation` for a group solved at load or
  never solvable (`tools/probe-solved-at-load.py`).
- `extraAbilities` is the hand-authored escape hatch for an ability a level
  needs but its registered controllers do not reveal; `bypassedAbilities` the
  reverse, an ability a level declares but a hand test showed it does not need.
- `drawers[]`: each drawer or cupboard, what it contains and what opens or
  unlocks it, harvested from the game's own Drawer component
  (`tools/merge-drawers.py` backfills rows that predate it).
- `endings` (2026-09-28): the solution id of each ending, in the table's
  order, `null` for one nobody has seen yet. Core's `Endings` names them
  from the ids ("Solution: Stacked", "Solution: Ordered 2", "Solution: Other
  1" for an unseen one) and matches each to the group its id names. Ids come
  from `LevelComplete  id=... solutionId=...` lines in play and gate logs
  (`fixtures/solution-ids-observed.tsv`), and from the game's own save,
  which records every id found (`levelCompletionData[].solutions[]
  .solutionId`, decoded by `harness_env._decode_save`) - the Seeing Stars
  Boss's phase endings (Lock, Compass) only ever show up there. Every
  ending has been seen since droha's hand tests of 2026-09-28; a new one
  goes in place of a `null`, never reordering. Generators too:
  their ids are the same on every seed, by position ("Ordered_0",
  "Ordered_1"), even where the seed picks which sorting rules count. An
  entry may answer to several ids, `|`-separated: Books (Randomized)'s
  second is `"Shuffle_1|Draggables_0"`: a symmetric seed checks its
  symmetric rule with a second controller over the same books (`gensweep`
  shows each controller's solutions after generation). That rule may be
  either of the two, so the mod first turns `Draggables_0` into the
  symmetric rule's own entry from the seed's rules (`Endings.Canonical`);
  the alternative answers only when they cannot be read.
- `mergedParts`: `{"part name": [controllers]}`, several groups checked as
  one part (Medicine Cabinet's "Red Items", Mirror's "Still Life" and
  "Little Things").
- `solutionOnlyParts`: part names done only as part of the Solution - still
  a group, so the level keeps its other part checks and the group's
  abilities stay in the Solution, but no check of its own (Mirror's "Little
  Things", Breadtags' "Crumbs" and "Interlocking", droha 2026-09-28). Not `notALocation`: a level left with one group
  mints no part check at all.
- `finishesAlone`: controllers whose group, forced alone, finished the level
  (`groups` in `fixtures/forceability.jsonl`). Only there does an ending
  named for a group ask for just that group's abilities
  (`rules.narrowed_group`); anywhere else it asks for the whole level.
  Re-measure with `tools/probe-forceable.py` before adding one.
- `phases`, `cats`, `levelClass`, `hint*`: see below and `DataTable.cs` in
  DevTools.

Hand-test results go in `proven-requirements.json`, and a missing ordering
between groups as a `dependsOn` edge through `tools/add-edges.py`, which
refuses anything it cannot justify. Understating a requirement softlocks a
seed; overstating is safe.

It is a **runtime** sweep, and that matters. Walking a loaded level prefab with
`GetComponentsInChildren<ObjectController>` finds a different set than
`Level.objectControllers` reports once the level is running - MedicineCabinet
shows 14 versus 13 - and only the registered set raises
`GameEvent_ObjectControllerSolved`. A location built from the prefab set would
include one that can never be checked.

Regenerate it whenever the game updates, and diff the result: a changed
controller name is a changed location name, which breaks existing seeds.

## Take the dump with the mod OFF

The DevTools dump is the ground truth this table is checked against
(`tools/check-game-facts.py`), and the mod changes what it reports.

`DailyGuard` answers `LevelInterface.IsDailyTidy` and `IsHolidayDaily` with
false while a run is active - deliberately, so the game stops routing the player
to the Daily Tidy page mid-run. A dump taken with the mod loaded therefore says
no level is a daily, which looks exactly like proof that none are. That result
was very nearly written into this file.

Move `BepInEx/plugins/ALTTLArchipelago` aside, launch, let the dump write, close
and move it back. DevTools alone produces the dump.

Two of the game's numbers also answer "what is true right now" rather than "what
is true of this level", and must not be turned into constants:

- `DailyTidyManager.GetDailyTidyLevels(true)` is today's rotation. It returned
  36 entries in one dump and 16 an hour later, on the same day.
- `isUnlocked`, `numSolutionsFound` and a cold read of `isDailyTidy` depend on
  which save is loaded.

`dailyDateCount` does not move, so that is what `isHolidayDaily` is derived
from.

## The sweep is LOSSY on phased levels

Being a runtime sweep costs something, and the cost went unnoticed until
2026-09-08. Some levels reveal their controllers as the player SOLVES the
previous group. The sweep boots a level and reads it once, so it sees only the
first phase - and no amount of waiting helps, because the later phases are
gated on a solve rather than on time.

**The level knows, and the sweep now asks it.** PhasedLevel.phases,
TupperwareNesting.GetPhaseControllers() and RadialDanceParty.dances are ordered,
authored lists naming exactly which controllers a level reveals. They are
recorded in the `phases` field, and four levels have one: PawPrints (3),
TupperwareNesting (6), Radial Dance Party (10) and DLC2 Ghost Cat (9).

That turned the phased-versus-ghost question from a judgement call into a
deduction: anything in the prefab, absent at boot, and named in no phase list
is a ghost. Eleven controllers were restored on that basis and only three true
ghosts remain.

So when regenerating, do NOT assume a fresh sweep is complete - it still records
only phase one. Run the C# test suite: `SurveyCrossCheckTests` holds the new
table up against the prefab survey and pins the remaining gaps, so losing more
of them fails the build. `tools/classify-controllers.py` writes the full
per-controller classification to `docs/reference/controller-classes.tsv`.

`fixtures/controller-survey.tsv` is the prefab-derived survey. It walks
`GetComponentsInChildren<ObjectController>(true)` - note the `true`, so it
includes inactive children - which makes it the authority on what a level was
authored to CONTAIN, including every phase.

It is still **not** the source of truth for locations, and for a reason that
cuts the other way: it also lists controllers that never register at all.
`MedicineCabinet/Cupboard` is the known example. Restoring one of those would
mint a location nobody can ever check.

The two files therefore answer different questions, and neither can be derived
from the other:

| | levels.json | controller-survey.tsv |
|---|---|---|
| measures | what registered at boot | what the prefab contains |
| misses | later phases | nothing |
| over-reports | nothing | controllers that never register |
| authority for | locations | ability requirements |

Where a level needs an ability its registered controllers do not reveal, that
is recorded as `extraAbilities` on the level. Requirements can safely be a
superset - too many is merely conservative - which is why the ability half of
this problem could be fixed without touching a single location id.
