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

## The title screen in a run

**Play opens the FARTHEST open slot that still has something doable in it.**
Vanilla Play resumes the campaign, which in a run meant landing on a puzzle
already beaten. Farthest rather than first because the track only moves
forwards - everything behind is finished or waiting on an item, so the useful
place to be dropped is the edge of your progress. "Doable" is stricter than
"unfinished": a slot whose remaining checks are all blocked by an ability or a
pack is skipped, and if nothing anywhere is doable the track is opened instead
of dropping the player into a puzzle they cannot progress.

**Everything that leads outside the run is hidden while connected.** Decided by
dumping the title screen's whole tree rather than by going through what was
visible, which is how the DLC block turned up - it lives in its own container
and a sweep of the main menu missed it entirely.

| Entry | Hidden | Why |
|---|---|---|
| Archive | yes | its puzzles ARE the run's, reached through the track; the page is itself a level select, which is why landing on it read as a broken track |
| Daily Tidy | yes | same |
| Shuffle | yes | endless random campaign levels, no checks. Inactive unless the save has New Game Plus, so it would have been missed by looking |
| DLC block | yes, entire | no DLC level can appear in a run - the table is base, archive and generator only. Hidden as a whole, heading and arrows included, rather than button by button, which would leave a heading over nothing |
| Play, Levels, Settings, Quit, Archipelago | no | all meaningful in a run |

Both verified in game, in both states:

| | Play | Levels | Daily Tidy | Archive | DLC | Archipelago |
|---|---|---|---|---|---|---|
| connected | opens slot 29, the farthest playable | shown | **hidden** | **hidden** | **hidden** | shown |
| not connected | vanilla, untouched | shown | shown | shown | shown | shown |

The hiding is applied on `SetupTitleScreen` and again on connect and
disconnect, because connecting happens AT the title screen - the screen has
already been built by then, so without the second call the menus would stay
visible until something else rebuilt it, which for someone connecting and
pressing Play is never.

## Opening a puzzle always rebuilds it

Measured by logging the `Level` instance id on entry, leaving, and entering
again:

```
Stamps (Randomized)  levelInstance=-35000  ->  -43310
TrickOrTidy_Candy    levelInstance=-51620  ->  -60172
```

A new object both times, for a generator level and a normal one alike. So
re-opening a puzzle you were part way through resets it regardless of anything
the mod does.

**That is why a cat trap now MISSES rather than queueing.** A trap held until
the next puzzle opens would undo work the game had already discarded, and
announce a setback that never happened. Arriving outside a puzzle it is spent
with a line in the log; arriving while one is open it lands. Verified in all
three states: at the title screen it misses, opening a puzzle springs nothing,
and sent mid-puzzle it undoes a group.

## Play starts the level through the game, not around it

The first version called `LevelManager.StartLevel` directly. That loaded a
level and left the title screen up - Play looked like it did nothing at all,
while the log cheerfully reported a launch each time it was pressed.

`TitleMenu.PlayGame` does the state transition, and the level it loads comes
from `Gameplay_GameState.GetLevelIndex`. Answering that question, and letting
the button run untouched, is what makes Play open the run's puzzle by the
game's own path - the same shape that fixed the exit routes.

## A test tool that clicked the wrong way

Clicking a pack divider left the player on a blank screen. The guard against it
already existed and fired correctly - and the bug was real anyway, because the
guard was on `LevelIcon.DoStartLevel` while a real click enters through
`OnPointerClick`, which also selects the card and drives a transition. A chapter
card has no puzzle behind it, so that transition ends nowhere.

The reason this was not caught: the `clicktrack` dev command called
`DoStartLevel` directly. Under test the guard fired, the track stayed intact,
and everything looked correct. With a mouse it broke. **The command now calls
`OnPointerClick`**, so the tool takes the same path a player does, and the
divider is refused at the click.

A second safety net came out of the same report. The track rebuild hung off
`LevelsTrack.MenuActivated`, and that event does not fire on every route into
the level select - entering it from the TITLE does not raise it. When that
happened the `SetLevels` and `SetupSections` postfixes still replaced the data,
so the menu held the run's entries and sections, but nothing called
`LevelsTrack.Init` and no icons were built: a level select that is correct
underneath and invisible on top. A once-a-second check now compares the cards on
screen against the plan and rebuilds on a mismatch, which covers whichever route
was taken including ones nobody has found.

## The pause menu hides Levels on generator puzzles

Opening a generator puzzle and pressing escape gave: Resume, Hint, Settings,
Reset, Exit. No Levels, no Skip - so the only way back to the track was quitting
to the title.

Confirmed by dumping the pause menu WHILE OPEN, which mattered: dumped closed it
reports every button active, and the two are hidden only as the menu opens.
Three separate attempts to check this from a closed menu all said everything was
fine.

```
open:    Resume=True  Skip=False  Hint=True  Levels=False  Settings=True ...
closed:  Resume=True  Skip=True   Hint=True  Levels=True   Settings=True ...
```

The cause is that generator puzzles come from the Daily Tidy pool, where hiding
both is correct - a daily has no campaign track to return to and cannot be
skipped. A run draws from that pool, so both need to come back. A postfix on
`MainMenu.ShowHideMenuItems` restores them; whether a skip is allowed is decided
by holding a Skip item, not by which pool the puzzle came from.

**Not verified by the agent.** The buttons are only hidden during a real open,
which needs a keypress, and calling ShowHideMenuItems directly does not
reproduce the hiding. Confirmed by the player instead.

## The cat trap did nothing for three rounds

`ObjectController.Reset` does not move objects. Measured directly - record
positions, displace everything, reset, read again:

```
before=(-4.2, -1.3)  displaced=(-3.4, -0.9)  afterReset=(-3.4, -0.9)
Reset restores positions = False
```

It clears solved flags and nothing else. So the trap logged "the cat undid N
groups", played the paw and showed a toast, while the puzzle sat untouched.

**It survived testing because every check was made on a puzzle with no progress
to undo.** The Popcorn screenshot showed three intact lines and was read as
"the reset behaved"; all it showed was that nothing had broken. A trap that does
nothing and a trap that undoes nothing look identical when there is nothing to
undo. Two earlier designs - random displacement, then swapping positions between
objects - were rejected for damaging puzzles, and this one was accepted for not
damaging them, which was never the test that mattered.

The trap now snapshots every object's position and rotation when a level opens
and restores that on firing. It is safe by construction: the layout is one the
level itself produced, and since levels are rebuilt from scratch on every entry,
what is on screen at load IS the base state. Reset is still called afterwards so
the solved flags agree with the screen.

Verified by displacing a live puzzle, firing a real trap, and reading the
positions back - the objects returned to their opening coordinates.

## The offline queue dropped checks when the server vanished (2026-09-04)

The queue exists so that solving a puzzle while disconnected is not lost work.
It did not survive its first real test.

**Reproduction.** Open a puzzle while connected, kill the server, solve it, quit
the game, restart the server, reconnect. The check earned offline was gone:

```
check: Telescope - Solution 1          <- earned
socket error: ArchipelagoSocketClosedException
run.json: {"owed":[], ...}             <- never written
(after reconnect) run state: 0 check(s) still owed
```

**Cause.** `FlushChecks` wrote the owed list on the two paths that knew where
they stood - an explicit offline branch, and after a successful send:

```csharp
if (_session == null || !_session.Connected) { RunState.SetOwed(owed); return; }
var sent = _session.SendChecks(owed);
if (sent.Count == 0) return;            // wrote nothing
```

The path between them is the one that happens. When a server disappears the
client goes on reporting `Connected` for a while, so the offline branch is
skipped; the send then moves nothing and returns early, having written nothing.
The ledger still held the check in memory, and the process then exited.

**Fix.** Write what is owed BEFORE attempting to send, unconditionally. The
guarantee no longer depends on correctly detecting a disconnection, which is
the part that cannot be relied on. `SetOwed` skips a write that would change
nothing, so an offline session does not re-save the same list on every flush.

**Verified end to end**, same reproduction, on the fix:

```
run.json (offline)  {"owed":["Candy (Trick or Tidy) - Solution 1"], ...}
                    ... survives the quit ...
(reconnect)         run state: 1 check(s) still owed
                    checks: sent 1, 0 still owed
(server)            droha sent Cat Trap to droha (Candy (Trick or Tidy) - Solution 1)
```

The last line is the one that matters: the check reached the server and paid out
its item. Our own log saying "sent" would not have been evidence.

### Also found while testing

- **`boot:` leaked levels.** It called `StartLevel` with `forceReload` but never
  destroyed the previous level, so its listeners stayed on the global event bus.
  Booting Radial Dance Party and then hopping away left
  `RadialDanceParty.CheckWinCondition` subscribed, and the next solve anywhere
  threw inside it and was lost - two levels and several minutes from the cause.
  `boot:` now tears down first, exactly as the level sweep already did.
- **`showpause` is not a pause-menu test.** It invokes
  `MainMenu.ShowHideMenuItems(null)` directly and the game dereferences the null
  `GameEventData`. Testing a trap "with the pause menu open" still needs a real
  input path.
- **Radial Dance Party has 0 controllers and TupperwareNesting has 2** - the
  true registered counts, re-measured by booting each with the sweep's own flags
  and waiting 25 seconds. The comment in `DataTable.cs` claiming 13 and 9 was
  wrong and has been corrected. The table matches the running game and the
  runtime-vs-table guard stays quiet.
- **Only one level has no controllers**, not the three the plan flagged. Drink
  Glasses and MerryMess_Presents each have a single `Pannables` controller,
  which `abilities.json` lists under `notPuzzles`.

## Skipping a puzzle counted toward the credits goal (2026-09-04)

`Credits.cs` says plainly: "Beaten means completed, not skipped. The count comes
from Level Beaten ... a skipped one contributes nothing, and the mod does not
need its own rule to" enforce it. It did need one.

**Reproduction.** Enter a level, receive a Skip, use it:

```
skip: spent one, 0 left
LevelCompleteEarly  id=MerryMess_CandyCanes
check: Candy Canes (Merry Mess) - Solution 1
beaten: Candy Canes (Merry Mess) - Beaten      <- the credits token
LevelComplete       id=MerryMess_CandyCanes
LevelSkipped        id=MerryMess_CandyCanes
```

**Cause.** The game reports a skipped level as COMPLETE. `LevelComplete` fires
for a skip exactly as for a win, and `LevelSkipped` arrives afterwards, so
`OnLevelComplete` could not tell the two apart and granted the Beaten token
unconditionally. Enough Skip items therefore reached the goal with nothing
solved - the opposite of the documented design.

**Fix.** The SkipLevel prefix sets a flag before the game's own skip work
begins, so it is set before the completion fires; `OnLevelComplete` consumes it
and withholds the token. The SOLUTION check still fires - getting past the
puzzle is what a Skip is for - so only the goal is protected. The flag is also
cleared whenever a slot is entered, so a skip that never completes cannot sit
set and silently swallow the next genuine token.

**Verified both directions**, which matters more than verifying the fix:

```
skip     -> "checks: skipped, so no Beaten token", no beaten line
solve    -> "beaten: Bats (Trick or Tidy) - Beaten"
```

A fix that suppressed the token everywhere would have looked identical if only
the skip case had been checked.

## The test harness clicked a track that was not there (2026-09-04)

Automating a full run surfaced three harness faults in a row, each hiding the
next. None was a product bug; all three would have produced confident wrong
answers.

**1. The wrong track.** `clicktrack` used `FindObjectOfType<LevelsTrack>()`,
which returns one arbitrary ACTIVE instance. The scene holds more than one, and
it kept picking an empty one, so every position reported "not on the track" -
including positions clicked successfully a minute earlier, while the mod's own
log said it had built 35 items.

**2. A stale track.** Widening the search to `FindObjectsOfTypeAll` and taking
the fullest track fixed that and broke something quieter. The scene keeps dead
tracks around, so after a few menu transitions the fullest one is a LEFTOVER.
Clicking its icons resolved the level name perfectly - the log read
`clicktrack: position 1 is Telescope` - and then did nothing whatsoever. This
was strictly worse than the bug it replaced: a warning became a silent no-op.

**3. Clicking before the track exists.** With live-vs-dead reported, the real
timing showed up. After a level completes, the game reports
`Levels_GameState` while every `LevelsTrack` is still inactive. A click in that
window lands on nothing. It only appeared in a scripted run because a person
takes longer than 4 seconds to choose the next card.

`clicktrack` now prefers a live track, refuses to click when only a dead one
exists, and says which it used; the driver waits for a live track and retries
rather than guessing a delay. That is the same conclusion the level sweep
already reached about controller counts: wait for the state you need, do not
assume a delay is long enough.

**The pattern, for the third time in this feature.** `FindObjectOfType` is the
common thread - it also failed to find the pause menu for the skip test, because
the menu is inactive while closed. Any probe that reaches for "the" instance of
something is asserting there is exactly one and that it is active, and in this
game neither holds.

## Beating a level can drop the run onto the Daily Tidy page (2026-09-04)

Reported in play as "sometimes when I beat a level it brings me to the daily
tidys page" and never pinned down, because it is not random.

**Trigger.** It happens when the NEXT track slot is a daily-pool level.
Reproduced deterministically:

```
navigation: next -> slot 10 (level 1000)
state: gameState=DailyTidy_GameState activeLevel=none
```

Level 1000 is Procedural Grid Puzzle, `isDailyTidy: true`. `GetNextLevelIndex`
is answered correctly - the right slot, the right index - and the game then
routes by the level's KIND, sending daily levels to the daily screen instead of
loading them inside the run.

**Why the other routes are fine.** Clicking a track card works because it goes
through `StartLevel(index, showTransition, forceReload, seed)`, which loads the
level directly rather than asking the game where a level of that kind belongs.
That is also why this never showed up in card-driven testing.

**Not yet fixed.** The fix belongs with the other launch paths - launch daily-
pool levels ourselves rather than handing the index back and letting the game
choose a state - but it wants doing carefully: an earlier attempt at exactly
this shape (calling StartLevel from Play) loaded a level underneath a title
screen that never went away.

## What automating a full run cost, and what it was worth

The goal was a synthetic playthrough to the credits, driving the game's own
controls. It did not get there. What it produced instead was five harness
faults and one product bug, which is a fair trade but not the intended one.

Harness faults, in the order they hid behind each other:

1. `FindObjectOfType<LevelsTrack>()` returned an empty track, so every card
   reported "not on the track".
2. Widening to `FindObjectsOfTypeAll` and taking the fullest track picked a
   STALE one. Clicking its icons resolved the card name and did nothing - a
   warning replaced by a silent no-op, which is worse.
3. After a completion the game reports `Levels_GameState` while every track is
   still inactive, so a click lands on nothing. Only visible in a scripted run,
   because a person takes longer than four seconds to pick the next card.
4. `leave` from the TITLE screen is not a route a player has. It reported
   Levels_GameState, found a live track, resolved a card and logged "now
   playing slot 0", and no level ever launched.
5. `clickbutton` invokes `Button.onClick`, which is not a click. The level
   select tutorial's confirm IS a Button with nothing on onClick: four invokes
   reported four successes while the modal sat on page 1 of 3. A real pointer
   click through `ExecuteEvents` dismissed it first time. Added as `press:`.

**The pattern behind all five**: each one produced a confident, plausible log
line while doing nothing. That is the same failure as the cat trap announcing a
reset it had not performed, and it is why every probe here now reports which
thing it found and whether the game accepted it.

**Where it stands.** The loop reaches a level, solves it and banks the checks,
then cannot reliably get back to another level: the post-completion `RetryUI`
offers only Continue, Retry and Menu, Continue hits the daily bug above, and
setting `Title_GameState` directly reaches a title where `PlayGame` never asks
`GetLevelIndex`, so our answer is not used. Finishing this needs the daily
routing fixed first.

## The blocking screen over-reports (2026-09-04)

`bounds:<tag>` dumps every managed object's world renderer bounds by controller,
and `tools/blocking.py` flags locked objects overlapping free ones. It runs, and
on MedicineCabinet with 9 locked and 17 free objects it reported 21 overlaps.

That number should not be believed. The dump records x and y only, and a
bathroom cabinet is full of objects legitimately sitting in FRONT of each other;
without depth, "overlaps" and "is in the way" are indistinguishable. It is a
candidate list for a person to review, and a noisy one. Recording z and the
sorting order would sharpen it; knowing where a free piece needs to travel would
be needed to settle it.

## The blank-level soak found nothing, in 60 valid loads (2026-09-04)

`tools/emptysoak.py` boots levels in a deliberately hostile order - heaviest and
lightest interleaved, shuffled, with re-visits - and watches for the armed
`LEVEL LOADED EMPTY` line.

**The first run was worthless and said it was fine.** It moved on after 0.4 to
1.2 seconds per level, and the watchdog only reports a level empty after it has
stayed empty for a CONTINUOUS 6 seconds. The detector could not fire however
broken a level was, and 40 loads of "0 empty" measured nothing. The dwell is now
7.5 seconds, above the threshold, and is a named constant next to a comment
saying why.

**The real run: 60 loads, 0 empty.** That is a genuine negative, not a silent
one - but a weak one. It says the bug does not reproduce by loading levels
quickly and repeatedly on this save, which leaves the routes it was actually
reported from untested: entering a later card after moving around the menus, and
whatever state the game was in at the time. The bug stays parked, the watchdog
stays armed, and the soak is worth re-running after any change to level loading.

## Badges and the credits card (2026-09-04)

**A cat trap changes no badge.** Read every open slot's badge from the track,
fired a real trap at Telescope, came back and read them again: byte-identical.
Resetting a puzzle undoes the arrangement and nothing about what has been
collected, which is what the badge reports.

```
why: position 0 = slot 0 TrickOrTidy_Candy #1, badge Doable, 3 packs held
... trap: 1 cat(s) reset the puzzle ...
(identical for all four open slots)
```

**The credits card appears when the Credits item arrives.** Sending it logged
"track: the credits card is now on the track" and the card showed as
`track[35] Credits unlocked=False unlockable=False` - present but locked, with
the goal at 10 puzzles beaten.

**Clicking it while locked refuses, but says nothing.** The state stayed on the
level select and no level launched, which is the important half. The intended
explanation did not appear: `Track.cs` has
`"track: credits locked, {left} puzzle(s) to go"` and it never fired, because
the game's own card lock refuses first - the card has no completion data, so the
click never reaches our prefix.

So a player who clicks the credits card early gets silence rather than "beat
four more". Worth fixing when the credits path is next touched; it is a missing
message, not a broken gate.

## Audit findings: three reconnect bugs, one hiding the next (2026-09-04)

An audit of the mod against Archipelago's client documentation turned up three
faults in the reconnect path. They were stacked: the first made the second
unreachable, which is why testing had never seen either.

### The client never noticed the server was gone

`Connection` wired its reconnect to `Socket.SocketClosed`. Measured: killing the
server mid-session raises `ErrorReceived` and **not** `SocketClosed`, so nothing
ever invoked the retry. The log showed a single `socket error:` line and then
silence - the client sat believing it was connected, indefinitely, with no
retry, no reconnect and nothing on screen to say otherwise. Checks earned after
that went to the offline queue and stayed there, which is why the queue looked
like it was working: it was catching the throw, not detecting a disconnection.

Fixed by treating a closed socket reported through `ErrorReceived` as a drop,
via a shared `Drop()` that is idempotent through the `Connected` flag so either
event can raise it. `ArchipelagoSocketClosedException` is not always what
arrives - the case that went unnoticed was a plain `WebSocketException`
carrying "closed without completing the close handshake".

Verified: killing the server now logs `disconnected:`, then `connection lost:`,
then retries at 3s and 6s, then `giving up after 3 attempts`. Before the fix,
none of those lines appeared at all.

### The item list was cumulative across a reconnect

`Inventory._received` was cleared only by `End()`, which runs on an explicit
disconnect. Every other route to `OnReady` called `Begin`, which deliberately
does not clear - for a real reason: precollected items arrive a step ahead of
`slot_data`, and clearing there threw them away. So the server's replay was
appended to the previous session's list and every count doubled. Packs and
Skips silently; traps loudly, since owed is `TrapsReceived - TrapsSprung`.

The file's own invariant named the case it failed: *"calling it twice with the
same list must leave the game in the same place, because that is exactly what a
reconnect does"*. On a reconnect the list was not the same, it was doubled.

Fixed with an explicit session boundary, `Inventory.NewSession()`, called from
`Plugin.Attempt` before the socket can deliver anything - so the pre-`Ready`
window that motivated the original design still works.

**This is why the two are one entry.** The doubling was latent: the only path
that reached it was the auto-retry, which never fired because of the bug above.
Fixing the reconnect would have activated it.

Verified: reconnect with 105 items replayed into a session that already held 96.
The track stayed at 4 open / 5 packs, and exactly 5 cats fired - the genuinely
unaccounted ones, with `trapsSprung` going 61 to 66 against 66 items received.
A doubled list would have been 201 entries and roughly 71 cats.

### Beaten progress was never persisted

`LevelsBeaten` counts collected EVENT locations - the Beaten tokens. Those have
no address, so the server never lists them back at login, and they are
deliberately never owed because sending one can only be rejected. But
`RunState` persisted only `owed`, `skipsUsed` and `trapsSprung`, and
`Checks.Begin` builds a fresh ledger restored solely from the owed queue and the
server's list. Event locations are in neither, so the count returned to zero on
every login: **the credits goal was only reachable inside one unbroken session.**

This was visible the day before and misread. During the badge test, four slots
beaten in earlier sessions all reported `badge Doable`. It was noted as odd and
passed over.

Fixed by tracking locally-recorded checks as their own set in `CheckLedger`
(`LocalForSaving` / `RestoreLocal`, with two Core tests), persisting them in
`RunState` as `beaten`, and restoring them in `OnReady` before the server's list
is adopted.

Verified end to end: beat a puzzle, `run.json` gains
`"beaten":["Fridge (Something Eggstra) - Beaten"]`, restart the game, and the
run state reports `1 puzzle(s) beaten`. Before the fix it reported none.

## Sending checks, and refusals that cannot be retried (2026-09-04)

Two more from the same audit, both in `Connection`.

### The send was never confirmed

`SendChecks` returned the names it had looped over, and `FlushChecks` cleared
the ledger on that. A throw kept them owed, so the window was narrow - the
socket dying after the `Connected` test but before the frame went out - but in
that window the check vanished from the server and from the queue at once. The
blocking overload also ran on the Unity main thread.

Now `SendChecksAsync` uses `CompleteLocationChecksAsync` and acknowledges only
when the task completes without faulting, with one send in flight at a time so
the 5s flush cannot stack duplicates underneath a slow acknowledgement.

**A correction worth recording**: the audit claimed the library offers
`CompleteLocationChecksAsync(Action<bool>, long[])`, and it does not. The real
signature is `CompleteLocationChecksAsync(long[])` returning a Task, and the
library has no acknowledgement callback at all - the task completing means the
send left, not that the server recorded it. So this is an improvement rather
than a guarantee: a dead socket now faults the task where the blocking call
returned normally. The remaining gap is closed by the server's own list at the
next login and by duplicates being explicitly harmless.

Verified server-side rather than from our own log, which is the point:
`(Team #1) droha sent Colour Scheme to droha (Medicine Cabinet - Swabs)`.

### A wrong slot name is not a flaky network

`LoginFailure.ErrorCodes` was discarded and every failure went into the same
backoff, so a typo in the slot name was retried three times over nine seconds
before the player was told anything - and it could never have succeeded.
Terminal codes (`InvalidSlot`, `InvalidGame`, `InvalidPassword`,
`IncompatibleVersion`, `InvalidItemsHandling`) now stop immediately and say why.
`SlotAlreadyTaken` is deliberately still retried: the usual cause is a previous
socket of ours that has not timed out, and waiting is what helps.

Separately, no failure path closed its session. Every attempt builds a new
`Connection`, and the server keeps the socket open after a refusal expecting
another `Connect` on it, so each refused login left a live socket with
`ItemReceived` still wired to the live `Inventory`. `Abandon()` now unwires and
disconnects on every failure path.

Verified with a deliberately wrong slot name, on both routes:

```
connecting to localhost:38281 as notaplayer
login refused (will not retry): The slot name did not match any slot on the server.
not retrying: The slot name did not match any slot on the server.
```

One attempt, no backoff, and the reason on screen. The manual Connect press is
the case that actually regressed - at launch the auto-connect path already
skipped retries, so testing only that would have proved nothing.

## Stage 2: what the per-frame passes were actually doing (2026-09-04)

The tick tree is fifteen calls a frame, and most were correctly gated. The cost
was concentrated in a few places, and the largest was doing all of its work
while the player could not see any of it.

**The badge refresh ran every second inside a puzzle.** The level select is
built once and cached, so `trackItems` stays populated for the whole session -
and `Badges.Refresh` had no visibility check, unlike `Track.TickTrackIntegrity`,
which has always had one. Every second, for 79 slots: a scene scan, a list
allocation and a LINQ `Distinct` per slot, and several hundred name
constructions each running a compiled regex, a split and a join. All of it to
recompute an answer fixed by the seed, and all of it thrown away because no card
was on screen.

Three changes, in order of payoff:

- `Badges.Refresh` returns when the track is not `activeInHierarchy`. Safe
  because the badges are repainted from scratch on the next tick once it is up,
  which already had to work: the game destroys them when it rebuilds the icons.
- `CheckRouter.ForSlot` memoizes per slot. Which locations a slot CAN produce is
  fixed at generation; only whether they are collected changes, and that is the
  caller's question.
- `DisplayNames.For` memoizes per level id. Pure function, fewer than two
  hundred inputs in the whole game.

**Smaller ones, same shape - work repeated for an answer that had not changed:**

- `Abilities.Apply` asked `GetIl2CppType().Name` for every controller every
  second, for a value fixed for the object's lifetime; now resolved once per
  instance id and cleared with the rest of the run state, since ids get reused.
  It also wrote every renderer's colour unconditionally on a settled level -
  a few hundred interop writes a second to set a colour to what it already was.
- `Checks.TickEmptyLevelWatch` polled three interop properties deep every second
  for the whole process lifetime, including through plain vanilla play with the
  mod idle. Now gated on a run being active. Its `_emptyFor > 6.5f` upper bound
  was unreachable - the counter moves in whole interval steps, so the window was
  only ever hit exactly - and it hardcoded the interval it was counting in.
- `Badges.TickWhy` did a `Path.Combine` and a `File.Exists` every second,
  forever, for a diagnostic nothing in the shipping mod reads. It is the
  DevTools command-file pattern copied into player-facing code. Now behind a
  config flag, `Diagnostics.BadgeWhyProbe`, default off - a switch rather than a
  deletion, because it is genuinely the only way to argue with a wrong badge.
- `ConnectionPane.FocusedField` allocated an array every frame - sixty a second
  for the session, pane open or not - because the typing guard calls it from
  `Update`.
- `Toasts.FindFont` ran a full-scene `FindObjectsOfType<TextMeshProUGUI>` per
  toast LINE, despite `Build()` having already resolved one. A reconnect replays
  the whole item list at once, so that was a scene scan per item in one frame.

**Not changed, and why.** Every timer zeroes its accumulator instead of
subtracting, so each period is really the interval plus one frame. That was
fixed only in the empty-level watch, where the accumulator is used
arithmetically to measure elapsed time and the drift changed what the number
meant. Elsewhere it is a sub-2% imprecision on a poll whose exact period does
not matter, and rewriting all of them would be churn for no behaviour change.

Verified in game: entered a puzzle, returned to the track, and the badges were
correct (red on the Pack 5 cards, which are blocked) with names intact and no
exceptions logged. The badge state surviving the visibility gate is the part
that could have broken.

## Why exits take over the button (moved out of Navigation.cs, 2026-09-04)

Kept here rather than in a 33-line comment on a 20-line method. `Navigation.cs`
now carries the conclusion and a pointer to this, because the risk is someone
replacing `GoToTrack` with something that looks cleaner - and all four cleaner
things were tried and failed in the running game.

Leaving a puzzle landed on the Archive instead of the run's track. Four
approaches were tested and disproved:

1. **Patch the navigation calls.** The exit buttons reach the generic
   `SetGameState<T>`, which cannot be patched under IL2CPP. The non-generic
   overload and `Back` were never called at all.
2. **Patch `LevelInterface.IsArchived` for the exit.** Still the Archive.
3. **Hold that patch for the whole level lifetime**, in case the destination is
   cached at level start. Still the Archive.
4. **SET the property on the live interface** - it has a setter - confirmed by a
   probe reporting `archived=False` while the level ran. Still the Archive.

And separately, filing the run's completion data in the CAMPAIGN list rather
than the archive one, in case routing followed the save rather than the level.
Also the Archive.

So the destination is chosen by something that is neither the level's flags nor
its save list. Taking over the button and calling the game's own
`GoToLevelSelectForLevel` - handing it a campaign level so it picks the campaign
track - is the fix. It is exhaustive rather than piecemeal because exactly three
classes define `LevelSelect`: `MainMenu`, `ReplayMenu` and `TitleMenu`, and
`TitleMenu` already goes to the campaign track.

A `SetGameState` trace patch was kept alongside this while it was being
investigated. It fired on every state transition for the whole run and its
entire body was a log line, so it has been removed now the question is settled.

## Stage 3: removing what was not being read (2026-09-04)

**Twenty-two counters with no readers.** Every one carried a variant of "counts
X, so a battery can assert this ran" or "so a test can assert the patch FIRED",
and nothing anywhere read a single one - verified by script across the mod,
Core, the tests, DevTools and the Python tools, not by eye. The instrumentation
that would have justified retiring the safety polls was written and never
connected, so it was dead weight making a promise it did not keep.

Removed rather than wired up, because wiring them would need a harness that can
read mod state, and DevTools deliberately shares no code with the mod. Three of
them (`Skips.Refusals`, `Track.Refusals`, `Toasts.Shown`) initially looked live:
their only apparent uses were the words "Refusals" and "Shown" appearing in
unrelated comments.

Kept: `Inventory.PacksHeld`, `SkipsHeld`, `TrapsReceived`, `LevelsBeaten`. Those
are state the game reads, not counters.

**A trace patch that outlived its investigation.** `Navigation
.BeforeSetGameState` was a Harmony prefix on every game state transition whose
entire body was a log line, kept while working out which call an exit button
takes. That question is settled, so it fired on every transition of every run
for nothing.

**Comments that had stopped being true.** The worst was the `Plugin.cs` header,
which still said "Nothing yet touches the level select or the puzzles - that is
the next piece of work" in a mod that replaces the level select, gates the
puzzles, sends checks and reports the goal. Also: a `Track.cs` comment citing
"our 35 entries and six pack sections" for a seed with 79 slots, made
seed-neutral; and a `TypingGuard.cs` doc saying the Rewired lookup is "found
once and cached" twelve lines above code explaining it is deliberately retried
and NOT cached on failure - the comment documented the bug that was fixed.

**Four orphaned `<summary>` blocks** sitting above the wrong member, where a
method moved and left its doc behind. Each is a silent XML-doc collision: the
second summary wins and the first is dead text. Fixed in `AbilityState`,
`TrackState`, `CheckLedger` and `Navigation`.

**Deliberately kept**: the design rationale at the top of `Traps.cs`, which is
longer than the code it documents. It is the record of three abandoned scatter
implementations, and it is what stops the cat trap being "improved" back into
one of them. The `Navigation` equivalent was moved here instead, because at 33
lines on a 20-line method it had outgrown the file - but its conclusion and a
pointer stayed behind, since the point of that comment is to be read by someone
about to do the wrong thing.

Verified after the deletions: all six feature patches live, connection up, run
state intact, into a puzzle and back out, zero exceptions.

## Stage 4: what came out into src/ALTTLModKit (2026-09-04)

878 lines that never knew what Archipelago was, now in their own project with
zero references back into the mod - verified by grep, including the one
remaining mod-specific string (the overlay's GameObject name, now a settable
property).

| Moved | Lines | What it was coupled by |
|---|---|---|
| `TypingGuard` | 295 | `Plugin.Logger` x5 |
| `Toasts` (minus the cat) | 290 | `Plugin.Logger` x6, two palette constants |
| `VirtualDesktop` | 231 | nothing but its namespace |
| `Hub` | 62 | one log line |

All four now take their logger as a delegate. The toast colours are properties
the mod sets from `ApPalette` at startup, so a message still reads the same as
the same message in the Archipelago text client, but the kit ships plain
defaults and does not know what a palette is.

**A project rather than a folder, deliberately.** A reference back into the mod
will not compile, so "just read it from Plugin" stops being available and the
coupling cannot creep back.

**The cat came out of the toast system.** `SweepPaw`, `TickPaw` and
`FindPawSprite` - about 110 lines - lived in `Toasts` because they borrow its
canvas, which made a general notification system carry a cat animation and the
only IL2CPP dependency it otherwise had no need of. They are in `Traps.cs` now,
drawing onto `Toasts.OverlayRoot`: still one canvas and the right sort order,
no cat in the kit.

**One honest compromise.** `TypingGuard` needs
`Rewired.Integration.UnityUI.RewiredStandaloneInputModule`, and in this game
that type is compiled into Assembly-CSharp rather than Rewired_Core or
Rewired_Windows - checked, the name appears only in the former. So the kit has
exactly one game reference, for exactly one type, and the csproj says so. A port
to another Rewired game re-points that and nothing else.

**A deploy list is not a project reference.** The first attempt loaded nothing:
`DeployToGame` names each DLL explicitly, so `ALTTLModKit.dll` was built, was
not copied, and the plugin failed to load with no plugin-side error to read -
the only symptom was the game never reaching "overlay ready". Both plugins'
deploy lists now name it.

Verified in game after the split: both plugins load, six feature patches live,
the run connects, and a cat trap fired end to end - the paw drawn from `Traps`
onto the kit's canvas, over toasts in the Archipelago palette, with the puzzle
reset behind it.

## Stage 5: the Core boundary, and what moved in behind it (2026-09-04)

The rule was "no `<Reference>` and no `<PackageReference>`", enforced by a CI
job that builds Core alone on a runner with no game. The job is the real
guarantee and is unchanged. The blanket package ban was not: it was stricter
than the boundary it defended and it cost coverage. Four Unity-FREE files - the
connection, the inventory, the goal latch and the run state - sat in the plugin
project untested, and two of the five bugs found this day were in them.

The rule is now "never Unity, BepInEx, or the game's interop assemblies;
ordinary .NET packages by exception, each with a reason". Core takes exactly one
package, `Archipelago.MultiClient.Net`, for the enums the protocol defines. No
socket is opened from Core. It still builds standalone with no game installed.

Moved in, each with tests it did not have:

| New in Core | Tests | What it encodes |
|---|---|---|
| `InventoryCounts` | 6 | counting a received-item list |
| `SolutionOrdinals` | 5 | the Nth distinct solution id is the Nth solution location |
| `Refusals` | 8 | which login refusals are worth retrying |
| `GoalLatch` | 6 | announce once, report until the send actually lands |

Core tests: 164 to 190.

**One test is deliberately a counterexample.** `ADoubledListDoublesTheCounts`
asserts that feeding the counter a doubled list reports doubled packs - the
exact corruption the reconnect bug produced. It is there to record that counting
is NOT what protects a reconnect; the session boundary is. If someone later
teaches the counter to dedupe, that test fails and tells them they are hiding
the bug rather than fixing it. Two packs really are two packs.

**What did not move, and why.** `Connection` itself stays in the plugin. Its
decision logic - the refusal classification - is out and tested, but the rest is
socket lifecycle: creating a session, wiring events, marshalling to the main
thread. Moving that would relocate code without making it testable, since what
would need mocking is the library. `RunState` stays too: it is an atomic
temp-file-then-rename over `System.Text.Json`, and its one real decision (the
change-check that stops a timed flush rewriting an unchanged file) is three
lines. The half worth testing was the ledger's, and that is already covered by
`LocallyRecordedChecksSurviveARestart`.

Verified in game after all four moves: plugins load, six patches live, the run
connects with its restored state, and solving new groups still produces
`check: Medicine Cabinet - Blue Bottles` followed by `checks: sent 1, 0 still
owed`. Re-solving groups collected in earlier sessions correctly produced
nothing, which is the ledger doing its job rather than a regression.

## Beating a level no longer drops the run onto Daily Tidy (2026-09-04)

The bug reported in play as "sometimes when I beat a level it brings me to the
daily tidys page", reproduced deterministically and now fixed.

**Why it looked intermittent.** It fires when the NEXT track slot happens to be
a daily-pool level - one of the sixteen with `isDailyTidy`, including the six
generator levels at 995-1000. `AfterGetNextLevelIndex` answered with the right
index; the game then routed by the level's KIND and sent it to the Daily Tidy
page instead of loading it into the run.

**The fix is to stop answering and start launching.** `RetryMenu.NextLevel` and
`ReplayMenu.NextLevel` - the Continue buttons - are taken over the same way the
exit buttons already were. `Track.ArmSlot` sets the pending slot for the frame
so `Track.BeforeStartLevel` fills in the seed and forceReload, then
`StartLevel` loads it. That is exactly what clicking a card does, and the card
path never had this problem: `StartLevel` loads a level rather than asking
where a level of that kind belongs.

The gameplay state is set BEFORE the load. Skipping that is what once left a
level running underneath a title screen that never went away, and the screen
here is the post-level retry UI.

**Verified on the failing case.** Completed MedicineCabinet, whose next slot is
25 - Spice Jars, level 26, `isDailyTidy: true`:

```
navigation: post-level Continue -> slot 25 (level 26), launching it
state: gameState=GameplayModal_GameState activeLevel=Spice Jars index=26
```

Before the fix that read `DailyTidy_GameState activeLevel=none`. Screenshot
confirms the puzzle on screen, behind the game's own one-time Colour Assist
prompt - which is what `GameplayModal_GameState` is, not a stuck screen.

**What is NOT independently verified, stated plainly.** The takeover applies to
every Continue, not only ones landing on a daily-pool level, so ordinary
destinations go through the new path too. That case has not been exercised end
to end: two attempts to finish an ordinary level for the purpose failed to
complete it (TupperwareNesting reported `solved=False` after both its
controllers were forced, and Spice Jars has two solutions so its retry screen
offers something other than Continue). The residual risk is low but real -
`GoToNext` has no branch on level kind, so an ordinary destination differs only
in the index handed to `StartLevel`, which is the card-click call that is
exercised constantly. Worth watching on the next real playthrough.
