# Verification log

Results of the Phase 0 gate from the implementation plan. Each entry records
what was asked, what happened, and what it changes.

**Phase 0 verdict: all six checks pass.** The design stands as specified. Four
findings adjust details, all recorded below: dependency components, the
runtime-vs-prefab controller set, the tracker marker needing its own object
rather than `borderImage` (S5, resolved 2026-09-02), and the opaque solution
id.

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

### S5 - can a tracker marker be shown on a card? PASS (resolved 2026-09-02)

Tinted all 85 card borders red/yellow/green/grey, then called
`RefreshIconAppearance()` on every icon. **The tint survived.**

**But the border is invisible on unlocked cards** - which are exactly the ones
the marker is for, since locked ones cannot be entered anyway.

#### Resolved 2026-09-02: use a badge, and the original diagnosis was wrong

**The border is not covered by the artwork. It is switched off.** `iconinfo`
dumps the icon tree and shows LevelIcon carries two complete presentations,
with unlocking swapping which is active:

```
Cat Frame Interface            [LevelIcon,Image,Button]      unlocked=True
  Icon Container
    Default Level Icon    active=False        <- borderImage lives in here
      Background / Border / Icon
    Unlocked Level Icon   active=True         <- and has NO Border child
      Stars / Background / Icon
```

On a locked card (index 10) the two are exactly reversed: `Default Level Icon`
active, `Unlocked Level Icon` inactive. So `borderImage` points into a subtree
that is disabled on some cards. No repaint or ordering trick can rescue it; it
is the wrong object.

**Where the border DOES render it looks good** - a clean red frame around the
card, better looking than the badge. The problem is that it renders on only
*some* cards. A throwaway compare mode put a different carrier on each
consecutive card in the same red, and two cards reporting the same presentation
came out differently: one silhouette-style card showed a crisp red frame,
another showed nothing at all. That mode has since been removed along with the
rejected carriers; `marker:states` is what remains.

**Neither available predicate explains which.** `LevelIcon.isUnlocked` reported
false for cards plainly drawing in full colour. `SaveData.LevelHasCompletionData`
reported true for cards drawing as silhouettes. Reading the active presentation
subtree directly still left two cards labelled the same and rendering
differently.

That unexplained inconsistency is itself the argument. A tracker marker that
appears on an unpredictable subset is worse than useless, and the badge - which
we own outright - renders on every card in every state without needing to
understand the game's presentation rules at all. If the border is ever wanted
for looks, the remaining unknown is which icons have a null or inactive
`borderImage` and why.

Three carriers were built and compared in one session (`marker:` in
DevTools/Markers.cs):

| Carrier | Verdict |
|---|---|
| **badge** - our own 30x30 Image, top-right, last sibling | **Chosen.** Legible on every card in every state, artwork untouched, and it survived both `RefreshIconAppearance` and a full title-to-levels menu round-trip |
| outline - a larger Image behind the card | Rejected by the user: it swamps the card instead of ringing it |
| background - recolour the game's own background Image | Rejected by the user as confusing - it repaints the whole card, so the art that tells cards apart is lost. Also **destructive and does not heal**: neither `RefreshIconAppearance` nor leaving and re-entering the menu restores the original colour, because the level select is built once and cached |

That last row is the durable lesson: **anything that recolours one of the
game's own Images must cache and restore it by hand.** Our own child objects
have no such problem, which is the real argument for the badge over any tint.

#### The four states

Decided by the user, and about what is LEFT to do on the card rather than about
the card as a whole - a card with three checks, two collected and one
reachable, is green rather than mixed:

| Badge | Meaning |
|---|---|
| green | everything still to do here is doable now |
| green upper-left / red lower-right, split corner to corner | something is doable, something is still locked |
| red | everything still to do is locked |
| star | all done |

**No text on cards.** A card shows its level name and its badge, nothing else.
An earlier diagnostic wrote the carrier and lock state under each card so a
comparison screenshot could not be miscounted; that was for the comparison and
is gone.

#### Star art: three sprites, and two wrong ones

Every card carries three star sprites, and picking the wrong one fails
silently:

```
LTL-LevelSelect-Star-solved     the earned star   <- the one to use
LTL-LevelSelect-Star-unsolved   an empty outline
LTL-LevelSelect-Star-locked     a drab grey star
```

Both first attempts were wrong. Walking a fixed path reached `-locked` and drew
a grey smudge; "any star that is not locked" reached `-unsolved` and drew a
hollow outline. Note `"unsolved".Contains("solved")`, so the suffix has to be
matched exactly.

The sprite is a **white silhouette the game tints at runtime**, so drawing it
untinted gives a white star that disappears against the pale cards. It is drawn
gold. **The game's own earned-star tint has not been sampled**: that star is
only drawn on a SOLVED level, and `solve:` writes a found-count without setting
solved, so no live example was available. Sample it and replace the constant if
a solved card ever comes to hand.

Polish left for Phase 6: the badge does not rotate with the card, and locked
cards are drawn at a slight angle, so it sits square against a tilted frame.
The star also renders a little smaller than the solid squares because it
preserves its aspect inside the same box.

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

---

## 2026-09-01, later - Phase 1 findings

Generating the shared level table needed a runtime sweep over all 111 levels,
which loads levels back to back far harder than a player ever would. Two
fragilities surfaced that the mod will also have to respect, because it
switches levels constantly too.

### Loading levels without tearing down the previous one corrupts state

Relying on `SetActiveLevel(..., forceReload: true)` alone leaves the old level
alive. `DraggablesOrdered.SetupElasticTargets` then does a `Dictionary.Add`
keyed by GameObject name against a dictionary that already holds the key:

```
ArgumentException: An item with the same key has already been added.
Key: Targets (UnityEngine.GameObject)
  at DraggablesOrdered.SetupElasticTargets
```

Twenty of them, after which the game sat at 100% CPU inside `LeanTween` with
the sweep wedged - responsive, not frozen, and producing nothing.

**Always call `LevelInterface.ReleaseAssetsAndDestroyLevel()` before loading
the next level.** The earlier prefab survey only escaped this because it
released every level explicitly.

### Two levels are bespoke and register almost no controllers

**Radial Dance Party registers 0 controllers, and TupperwareNesting 2 of the
9 its prefab shows.**

I guessed twice and was wrong twice, so the record is worth keeping. First
guess: the controllers register during the intro animation, which
`doTransitionIn: false` skips. Second guess: the sweep read them too early.
Neither held - enabling transitions changed nothing, and waiting for the
controller count to "stop changing" settles instantly when the count is stably
**zero**, so that fix looked plausible while fixing nothing.

What settled it was testing through the ordinary gameplay path instead:
`boot:81` and `boot:82`, which set `Gameplay_GameState` and run the full
transition exactly as a player does, produce the same 0 and 2. These levels
are bespoke `Level` subclasses (`RadialDanceParty`, `TupperwareNestingLevel`)
whose puzzle pieces are simply not registered `ObjectController`s.

**The consequence is benign and self-correcting.** They contribute their
solution checks and few or no controller checks, and since nothing on them is
ability-gated they stay fully playable. `LevelTableTests` names Radial Dance
Party as a known exception so that a *new* zero-controller level still fails.

Two real improvements came out of the wrong guesses and were kept: the sweep
now refuses to settle on a zero controller count until a hard timeout, and it
logs every level rather than every tenth - when it first hung, the last log
line named a level ten before the culprit.

### The runtime set is much smaller than the prefab set

201 registered controllers across 111 levels, against 334 found by walking the
prefabs. Most of the difference is legitimate - unregistered components,
camera-pan helpers, duplicates on one GameObject - but it is a reminder that
`docs/data/controller-survey.tsv` is reference data and
`apworld/alttl/data/levels.json` is the source of truth.

## Where a puzzle returns to, and four ideas that did not work

Finishing or leaving a puzzle dropped the player on the **Archive** page rather
than the run's track. The Archive is a level select, so it reads as "the track
is broken - no chapters, wrong levels" rather than as a different screen, which
is how it was first reported.

The cause is that the game picks the destination from the level you are
leaving: archive levels go to the Archive, daily ones to Daily Tidy, campaign
ones to the track. Correct in vanilla; wrong here, because a run mixes all
three sources.

### What the menus actually are

Dumped from inside a base level (Calendar), an archive level (Popcorn) and a
generator level (Batteries). All three give the same twelve menus, and they are
**singletons** - one `MainMenu` (the pause menu), one `ReplayMenu` (the
post-level screen), one `LevelSelect`, one `ArchiveMenu`, one `DailyTidyMenu`.
There is no per-level-type pause menu, so there is only ever one of each to
take over.

Exactly three classes in the game define a `LevelSelect()` method: `MainMenu`,
`ReplayMenu` and `TitleMenu`. The third already goes to the campaign track.
Those two facts together are what makes the fix complete rather than a
patchwork - it is not "the buttons we found", it is all of them.

### Four disproved hypotheses

Each was checked in the running game, not reasoned about:

| Idea | Result |
|---|---|
| Patch `LevelInterface.IsArchived`'s getter while the exit is decided | Archive |
| Hold that patch for the whole level lifetime, in case the destination is cached at level start (`GameManager.SetGameStateCache` exists) | Archive |
| **Set** `IsArchived` on the live interface - it has a setter - confirmed by a probe reporting `archived=False` mid-level | Archive |
| File the run's completion data in `levelCompletionData` instead of `archiveCompletionData`, in case routing followed the save | Archive |

`IsCredits`, `LevelType`, `ReactToDailyCompleting` and `IsRandomizable` are all
settable too and none is the discriminator; `LevelManager` has no level-scope
field to set. Whatever picks the destination is neither the level's flags nor
its save list, and it is reached through the GENERIC `SetGameState<T>`, which
cannot practically be patched under IL2CPP - the non-generic overload and
`GameManager.Back` are never called on these routes at all.

### What is in place

`MainMenu.LevelSelect` and `ReplayMenu.LevelSelect` are taken over, and each
calls the game's own `LevelManager.GoToLevelSelectForLevel` with a campaign
level. That routine does the teardown, the transition and the menu setup; the
only thing supplied is which track. Two earlier attempts that set the state
directly left the puzzle loaded behind the menu, and then left it tinting the
background - the level select recolours per section, and synthetic sections
need `Section.BackgroundColor` set or they inherit whatever the puzzle painted.

`LevelManager.GetNextLevelIndex` is answered with the next OPEN, unfinished slot
**forward** from the current one. Answering with the first unfinished slot sent
"next" back to the start of the run after finishing a puzzle near the end.

Verified by playing each route and looking at the screen: pause mid-level; beat
then post-level Level Select; beat then hamburger then Level Select; beat then
the Next arrow.

## Parked: a level that loads empty

Reported once - a puzzle opened to a blank screen that ignored input, needing a
restart. One cause was found and fixed: a generator level launched from the
level select is never populated unless the launch passes both its seed and
`forceReload`, and the boot command used in testing always passed both, which
is why scripted testing never saw it.

Whether that was the whole of it is unknown; it has not reproduced since, and
a sweep of all 36 cards in a run found every playable one loading. Parked
rather than closed. A watchdog now logs `LEVEL LOADED EMPTY: <id>` and shows a
toast if a level sits with no objects and no controllers for six seconds, so
the next occurrence names itself instead of needing to be reproduced.
