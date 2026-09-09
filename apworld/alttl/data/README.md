# Shared data

`levels.json` is the single source of truth about the game's content, consumed
by **both** the Python apworld and the C# mod. It is generated, not hand
written: run the `levelsweep` command in ALTTLDevTools and copy
`BepInEx/alttl-levels.json` here.

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
recorded in the `phases` field, and exactly three levels in the game have one:
PawPrints (3), TupperwareNesting (6) and Radial Dance Party (10).

That turned the phased-versus-ghost question from a judgement call into a
deduction: anything in the prefab, absent at boot, and named in no phase list
is a ghost. Eleven controllers were restored on that basis and only three true
ghosts remain.

So when regenerating, do NOT assume a fresh sweep is complete - it still records
only phase one. Run the C# test suite: `SurveyCrossCheckTests` holds the new
table up against the prefab survey and pins the remaining gaps, so losing more
of them fails the build. `tools/classify-controllers.py` writes the full
per-controller classification to `docs/data/controller-classes.tsv`.

`docs/data/controller-survey.tsv` is the prefab-derived survey. It walks
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
