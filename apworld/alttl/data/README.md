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

`docs/data/controller-survey.tsv` is the older prefab-derived survey. It stays
as reference data for questions about level structure, but it is **not** the
source of truth for locations.
