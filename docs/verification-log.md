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
