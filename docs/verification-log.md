# Verification log

Results of the Phase 0 gate from the implementation plan. Each entry records
what was asked, what happened, and what it changes.

**Phase 0 verdict: all six checks pass.** The design stands as specified. Four
findings adjust details, all recorded below: dependency components, the
runtime-vs-prefab controller set, the invisible border on unlocked cards, and
the opaque solution id.

Game build 24060652, version 3.6.1. Probe commands used are in
[the README](../README.md).

---

## 2026-09-01

### S4 - how common are controller dependencies? PASS

Folded into the solution survey as a `dependsOn` column
([docs/data/controller-survey.tsv](data/controller-survey.tsv)).

**Only 9 of 380 controllers have any dependency, across 5 levels** - and most
are *mutual* pairs, which is more interesting than a dependency chain:

| Level | Controllers | Shape |
|---|---|---|
| Chess Shadows (74) | Shuffleables Pieces <-> Shuffleables Shadows | mutual |
| Spice Jars (26) | SpiceJarShuffleables <-> ..._BottomShelf | mutual |
| TupperwareNesting (82) | Layout (Grid) <-> Draggables (Large Square) | mutual |
| SomethingEggstra Egg Cups (108) | Shuffleables Cups <-> Shuffleables Eggs | mutual |
| Desktop Computer (76) | Computer Errors -> Computer Desktop | one-way |

A mutual pair is `matchDependencySolutions`: two controllers whose solutions
must agree, which makes them one joint puzzle rather than two.

**Consequence for the design.** The rule "one controller check per real
controller" would mint two inseparable checks for each mutual pair. Replace it
with: **compute connected components over the dependency graph, and make each
component one location.** Chess Shadows then correctly becomes a single
puzzle, and Spice Jars - which is a *generator* - stops claiming two
independent checks it cannot deliver.

The one-way case (Desktop Computer) still needs the transitive closure for its
ability requirement, as the plan says.

### S5 - does a borderImage recolour survive RefreshIconAppearance? PASS, with a problem

Tinted all 85 card borders red/yellow/green/grey, then called
`RefreshIconAppearance()` on every icon. **The tint survived.**

**But the border is invisible on unlocked cards.** A locked card is a
silhouette on the background colour, so its border reads clearly. An unlocked
card's full-colour artwork fills the frame and covers the border entirely -
and unlocked cards are exactly the ones the marker is for, since locked ones
cannot be entered anyway.

So the tracker marker needs a different carrier on unlocked cards. Options not
yet tested: tint the `background` RectTransform behind the card, add a small
badge into the `stars` row, or draw an outline outside the art. Resolve before
Phase 6.

### S6 - can a card be held locked? PASS

`LevelIcon.DoStartLevel` prefixed and refused. Invoking it via `clickcard:1` -
the exact method every launch path funnels into - logged the refusal, advanced
the refusal counter 0 -> 1, and left the game in `Levels_GameState` with no
active level. No soft-lock, no exception.

### S3 - can a controller be made inert without breaking the level? PASS

Mechanism verified on MedicineCabinet (index 49). `inert:Swabs Containables`
disabled 6 objects and `inert:Creams Draggables Stacked` disabled 4, via
`LevelObject.SetInteractable(false)` + `SetPreventSelection(true)` plus a tint
of `renderer` and `subrenderers`. `inert:off` restored all 13 controllers and
26 objects. **No exceptions in either direction.**

Before/after screenshots confirm the objects visibly change - the cotton swab
tips and the cream tubes desaturate.

**Confirmed by hand-play.** With Swabs Containables and Creams Draggables
Stacked disabled, the player reported the locked objects could not be moved
while everything else behaved normally, solved the other 11 controllers, and
**the level correctly refused to complete**. After `inert:off` restored all 26
objects to full colour the remaining two were solvable and the level completed
normally.

The player's verdict on the dimming was "seems fine for now" - it read clearly
as locked. My worry that `(0.55, 0.55, 0.55, 0.6)` was too subtle was
unfounded; recording their judgement rather than mine. A lock affordance may
still be worth adding, but it is not blocking.

### New finding - the runtime controller set differs from the prefab

MedicineCabinet has **14** controllers when walking the loaded prefab with
`GetComponentsInChildren<ObjectController>`, but **13** in
`level.objectControllers` once the level is running. The prefab walk sees a
`Cupboard` controller that never registers at runtime.

Only the registered set raises `GameEvent_ObjectControllerSolved`, so the
registered set is what a location must be built from.

**Consequence:** `apworld/alttl/data/levels.json` must be generated from a
**runtime sweep** (boot each level, read `level.objectControllers`), not from
the prefab survey. The existing `controller-survey.tsv` is prefab-derived and
is therefore reference data, not the source of truth. Phase 1 needs a new
DevTools sweep command.

### S1 - does GameEvent_ObjectControllerSolved fire? PASS

Emphatically. Hand-solving MedicineCabinet produced **16 events across 13
distinct controllers**, each naming its own controller and type with
`isSolved=True`:

```
ObjectControllerSolved  controller="Blue Bottles Draggables"  type=DraggablesOrdered  isSolved=True
ObjectControllerSolved  controller="Oral Containables"        type=Containables       isSolved=True
```

**The event repeats for the same controller.** "Oral Containables" fired twice,
once as the toothbrush went in and again for the toothpaste; "Creams
Draggables Stacked" fired twice in the same millisecond. So a check must
dedupe - which is what `MarkChecked` in Core exists for.

### S2 - does a real solve report a real SolutionId? PASS, with a caveat

```
LevelComplete  level="MedicineCabinet"  found=1/1  SolutionId="Draggables_0"
```

Non-empty, and `found` tracks correctly.

**But the id is opaque.** `Draggables_0` matches none of the level's 13
controller names ("Jar Lid Draggable", "Trinkets Draggables", "Cup
Draggables"...). The earlier guess that a solution id is
`<controllerName>_<index>` held for the archive levels but does not hold here.

This **confirms rather than threatens** the design: locations already use
**ordinal** solution numbering - the Nth distinct solution on a level - so the
internal string is never needed. `NumSolutionsFound` is the reliable counter
and the game dedupes into `solutions[]` itself. Do not build anything on the
id string.
