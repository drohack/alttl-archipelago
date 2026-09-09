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

## 2026-09-09 - the phase audit, and the campaign nobody could reach

Three playtests in a row had hit the same shape of bug - a card offering work
the level would not give - and each was patched by hand from the report.
droha called it: "I really need you to go through every single puzzle type,
every iteration, figure out which puzzles have dependencies on abilities, and
which ones have mini solutions. Double check all of your current work."

### The game declares all of it

A metadata read of Assembly-CSharp found authored, ordered, DIRECTED data for
the three things that had been inferred:

| Question | The game's answer |
|---|---|
| Which levels are phased | `PhasedLevel.phases`, each naming its `PhaseController` |
| TupperwareNesting's order | `TupperwareNesting.GetPhaseControllers()` |
| Radial Dance Party's order | `RadialDanceParty.dances` / `RadialDance.nextDance` |
| What a drawer gates | `Drawer.UnlockOnSolvedControllers` (with a solution id) |
| Assemble-before-arrange | `LevelObject.dependenciesPlacedFirst` |

The levelsweep now records `levelClass`, `phases`, `drawers`, per-controller
`objectIds` and `objectsGatedFirst`. **Exactly three levels in the game declare
phases**: PawPrints (3), TupperwareNesting (6), Radial Dance Party (10). That
is the complete answer to "which puzzles have mini solutions", measured.

### Three of my own conclusions were wrong

- **The TupperwareNesting phase order.** Guessed as "every later group depends
  on Lids and Stack 1", the two that register at boot. The game declares a
  CHAIN that does not involve Lids at all: Stack 1 -> Stack 2 -> Tray ->
  Stack 3 -> Layout (Grid) -> Food. The guess invented a Containers
  requirement on three groups and pushed a fourth to needing three abilities.
- **`Food` is not a ghost.** Written off because it never appeared in play; it
  is the sixth and last phase. The run never got that far.
- **Radial Dance Party's ten rings are real.** Registering nothing at boot had
  been read as "no puzzle content" for weeks, and the level was excused from
  the every-level-has-a-controller test on that basis.

Eleven locations restored: Radial's ten and Tupperware's Food. 427 -> 438.

### Shared objects were locked by whichever controller ran last

Recording managed-object instance ids exposed a second, unrelated bug. The
ability pass walked controllers in order and wrote each one's objects, so where
two groups own the SAME objects a locked group re-locked what an unlocked one
had freed. Five levels: Coins 1 (Shape) 6 shared, Spoons 7, Books 3 17,
TrickOrTidy_ChocolateBars 9, Workbench 21.

This is what droha actually hit on Coins - "nothing I can do in the level" -
and it is NOT the dependency bug that produced the identical symptom on Candy
Canes. An object is now locked only if every owner is locked. Verified in game:
with Swapping locked, Books 3's seventeen books stay full-colour and movable.

### 57 of 69 campaign levels could never appear

`slots.py` fills by source and `source_weights` held only generator and
archive, so a campaign level entered only through the mechanic-coverage
reserve, which opens for four abilities. Twelve qualified; the rest were dead
content. Found by trying to playtest Radial Dance Party: eighteen rolls could
not place it, and no scoring weight could have.

`base_weight` added, defaults now 80/10/10. Measured over 5 seeds at 79 slots:
60.2 generator / 10.0 archive / 8.8 base, of which 4.8 are ordinary campaign
puzzles that previously could not appear; 28 distinct campaign levels against
7. Proven reachable at 95% campaign weight: all 69 drawn every seed.

### Two process traps

- **Renaming a plugin folder does not disable it.** BepInEx loads any DLL under
  `plugins/`, so `_ALTTLArchipelago.off` loaded as normal. A sweep run that way
  came back with all 36 daily flags false - contamination produced by following
  the very warning meant to prevent it. `check-game-facts.py` now says to move
  the folder out of the tree and to check for zero mod lines in the log.
- **RETRACTED: "`Level.RegisterObjectController` is not the funnel".** The patch
  that "recorded zero events across 111 levels" was never applied. DevTools
  registers Harmony classes one at a time with `PatchAll(Type)`, and
  `RegistrationLog` was not on the list - so the silence was the patch missing,
  not the method going uncalled. Found on 2026-09-09 when a second tracer class
  was equally silent for the same reason. Which method is the real funnel is
  once again unknown, and the tracer now actually runs.

### A suspected bug that was not one

Vanilla creates a completion row whenever a level is beaten - "finish N, create
N+1" - and `ApplyUnlocks` skips rows it did not create, so those keep
`unlockedOnLevelSelect` false. That looked like the replayed-unlock-animation
bug returning once campaign levels became common.

Measured instead: reading the save either side of opening the level select
shows the flags unchanged, 8 true and 1 false both times. Nothing clears or
re-sets them, and a replay needs a re-set. A vanilla row also cannot make a
locked card playable - `IsRefused` gates on the run's pack state and never
reads the save. **No change made.** Recorded because the reasoning nearly
produced one, and because the comment in Track.cs that prompted it - "the track
plays its unlock animation and then CLEARS it" - is wrong and has been
corrected.

### Two background items became one, and it reaches the level select

droha: "I think we can change the name of the menu background item to just
Background Change Trap (as there's sometimes it can hide items which is still
funny)", then "and it should change the level select menu background as well".
Asked which of the two to rename, droha chose both, merged.

`Level Background` and `Menu Background` were separate items with separate
counters doing the same job on different screens, which meant the pause screen
was routinely several palette entries behind the puzzle behind it. They are now
one item, `Background Change Trap`, and one counter, and it paints three
surfaces:

| surface | where | colour |
|---|---|---|
| the puzzle backdrop | `Backgrounds.ForLevel`, held per frame against the camera | game palette, `Legible` steps past a colour the pieces would vanish into |
| the pause screen | `Navigation.TintMenuBackground` | game palette, same index |
| the level select track | `Track.SectionColour(section + count)` | the six chapter colours, ROTATED by the count |

The level select is the new one, and it rotates rather than flooding: sections
exist to be told apart, so painting them all one colour would cost more than
the trap gains. `Track.RepaintSections` re-runs `SetupSections` and
`SetSectionBackgroundColor` when the count moves - deliberately NOT `Rebuild`,
which re-lays-out the track and would yank the scroll away from whatever card
the player was looking at for the sake of a colour.

Every index is still a function of the received COUNT, never stepped on
arrival. That is the rule the original two items were built on and it survives
the merge intact: Archipelago replays the whole item list on every connect, so
anything that advanced per arrival would put the player on a different colour
every login.

**The measured filler table above, dated to when it was taken, still lists the
two old rows.** It is left as measured; the shares it records now belong to one
item rather than two.

Caught by the compiler rather than by review: `Inventory.Backgrounds` shadowed
the `Backgrounds` CLASS inside `Inventory.cs`, so `Backgrounds.ApplyToLevel()`
stopped resolving. The counter is `Inventory.BackgroundTraps` now, which says
what it counts.

**This changes item ids and invalidates seeds in flight** - one fewer name in
the table, and `Hint Page` sits after the filler list, so its id moved from
4050018 to 4050017. `items.py` already carried the warning that shortening
FILLER_ITEMS does exactly this. No version bump: 0.3.1 is unreleased, so the
shift is contained inside a version nobody has generated against.

**Verified in the running game**, not from the build. A probe connected to a
real seed, opened the level select, read the seven section colours, cheat-sent
one `Background Change Trap` and read them again:

    before  Opening 59546b  Pack1 3d5c57  Pack2 66524d  Pack3 4d4f66  ...
    after   Opening 3d5c57  Pack1 66524d  Pack2 4d4f66  Pack3 42574d  ...

Every section moved exactly one place along the palette and Pack 6 wrapped back
to the Opening's old colour, which is the six-entry cycle behaving. Section
titles, counts and track starts unchanged; no scroll armed; no exceptions.

Two things this caught that a build could not:

- `AfterSetupSections` arms the opening scroll unconditionally, so the recolour
  would have thrown the player off whatever card they were looking at - the
  exact yank RepaintSections exists to avoid. Guarded with `_recolouring`.
- The new `sections` dump printed `bg=<err:IndexOutOfRangeException>` on all
  seven rows. **`ColorUtility.ToHtmlStringRGB` again**, the same interop trap
  already written up in `Backgrounds.Palette` a fortnight earlier, and it reads
  as the section lookup failing rather than the formatter. DevTools formats the
  hex by hand now.

---

## 2026-09-08 - the controller table under-records phased levels

### RETRACTION: "Radial Dance Party has 0 controllers and TupperwareNesting 2"

Two entries below say this, and both are wrong. They are left in place rather
than edited away, because how the wrong answer was reached is the useful part.

While playtesting the 40-puzzle 0.3.1 seed droha reached `TupperwareNesting`,
solved several groups and got no checks for them. The mod said why:

    CONTROLLER MISMATCH on TupperwareNesting: 7 registered, 2 in the table,
    not recognised: Stack 2, Tray, Stack 3, Draggables (Large Square),
    Layout (Grid)

Seven controllers registered where the table records two. The prefab survey
had said nine all along.

**Why the earlier measurements agreed with each other and were both wrong.**
The first claim (13 and 9) came from the prefab. It was "refuted" by booting
each level with the sweep's own flags and waiting 25 seconds, which gave 0 and
2 - so 0 and 2 were written down as the true registered counts. But booting a
level and waiting IS what the sweep does. The re-measurement was the same
measurement, and its agreement was mistaken for confirmation.

These levels reveal controllers as the player SOLVES the previous group. No
delay reveals anything: 20 frames and 25 seconds observe the identical state.
Only play, or the prefab, can see the rest.

### Scope, measured across all 111 levels

The table is a strict subset of the survey everywhere - it never has a
controller the survey lacks. Six levels are short, 24 controllers in total
(**all but three resolved on 2026-09-09** - see the entry above):

| Level | table | survey | missing |
|---|---:|---:|---:|
| Radial Dance Party | 0 | 13 | 13 |
| TupperwareNesting | 2 | 9 | 7 |
| Record Player | 1 | 3 | 2 |
| MedicineCabinet | 13 | 14 | 1 |
| Desktop Computer | 7 | 8 | 1 |
| Books (Randomized) | 1 | 2 | 1 |

### Two separate problems, and only one of them was worth breaking seeds over

- **Lost part locations.** Those 24 controllers mint no checks. Annoying, and
  it strands a level card on a green/red split, but nothing becomes
  unwinnable.
- **Lost ABILITY requirements.** A level whose hidden phase needs Grids, and
  whose table never saw the grid controller, is a level the generator believes
  is finishable without Grids - so it will happily put progression behind it.
  That is a seed that cannot be completed.

Only the second can lock a player out, and the two have very different costs
to fix. Location ids are positional, so ADDING controllers shifts every id
after the first changed level - measured at ~91% of the table, which breaks
every seed in flight. Recording an ABILITY changes only logic gating and moves
no ids at all.

So the ability half was fixed now and the location half deliberately was not.

### The fix: extraAbilities, on exactly four levels

`LevelInfo.ExtraAbilities` records an ability a level needs that its registered
controllers do not reveal. Classifying every survey controller into its ability
and differencing against the table found four levels short:

| Level | ability added |
|---|---|
| TupperwareNesting | Grids |
| Record Player | Gadgets |
| Radial Dance Party | Rotating |
| MedicineCabinet | Furniture |

The scan now reports zero levels under-recording an ability.

**Requirements and coverage had to be split.** The first attempt fed
`extraAbilities` into the single `AbilitiesForLevel` accessor and broke the
mechanic-coverage tests - it made MedicineCabinet count as a place to LEARN
Furniture. It is not: a phase-only controller is unreachable until the player
already has the ability, so guaranteeing coverage through it would guarantee
nothing. `ControllerGroups` now has two accessors, `AbilitiesForLevel` for what
a level requires and `AbilitiesTaughtBy` for what it can teach, and
`MechanicCoverage` uses the second.

**MedicineCabinet is the interesting one.** Its `Cupboard` is a documented
prefab GHOST that never registers, so it must never be given a location - but
the ability entry is still correct, because requiring one ability too many can
only make logic more conservative. Requirements can be safe where locations
cannot.

### Three tripwires, so this cannot come back quietly

- **`SurveyCrossCheckTests`** (new) pins the six known table-vs-survey
  disagreements controller by controller, and separately asserts that no level
  can need an ability it does not declare. Verified by stripping
  `extraAbilities` from the table and watching it fail.
- **The e2e now fails on an unexplained audit complaint.** `CONTROLLER
  MISMATCH` and `UNEARNABLE LOCATIONS` were warnings, and the census only
  counts errors, so a run could go green with the table wrong - which is how
  six levels survived every previous release gate. `table_audit()` allows the
  six known levels and fails on any other.
- **`Checks.TickAudit` re-audits as a level grows.** It latched after the first
  successful pass, so on a phased level it looked exactly once, before anything
  had been revealed. `_audited` is now `_auditedCount`.

`DataTable.cs` now states plainly that the sweep is lossy on phased levels and
that the prefab survey is the authority for what a level contains.

### Still open

The 24 part locations. Restoring them needs runtime evidence per controller -
the survey cannot distinguish a phased controller from a ghost, and restoring
a ghost mints a location nobody can ever check, which is the same defect in
reverse. It also shifts ~91% of location ids, so it is a breaking change that
should ride with a version bump rather than a patch.

---

## 2026-09-07 - the 0.3.1 playtest fixes

droha played 0.3.0 to 79 puzzles on another machine and reported 13 bugs and 3
improvements. The BepInEx log survived, and it is the reason most of what
follows is measured rather than argued.

### Three reports, one defect - CONFIRMED FROM THE LOG

    track: ignoring a pending slot 22 set 9 frames ago
    track: ignoring a pending slot 39 set 8 frames ago

Both lines sit between `received item: Cat Trap` and `trap: 1 cat(s) reset the
puzzle`. `Navigation.AfterGetNextLevelIndex` is a postfix on
`GetNextLevelIndex`, which the game also calls while building the post-level
UI, so finishing a puzzle armed a slot speculatively; `BeforeStartLevel` then
cleared the arm BEFORE testing its age, so the trap's restart consumed it and
rebuilt with `randomSeed = -1` - the generator's stock layout.

That one defect produced the duplicate "envelope" levels, the single-colour
soft lock, and a Cat Trap silently swapping the puzzle mid-solve. The third was
never reported; only the log shows it.

Replaced by `Track.ResolveSlotFor`, which accepts an arm on either of two
independent proofs - the level index matching, or the arm being fresh. Two
proofs rather than one because the index route rests on
`ActiveLevelInterface` already pointing at the new level when a click reaches
`StartLevel`, which the surrounding code implies but nothing measured.

### The mod was writing to the player's real Daily Tidy save

Not reported, and worse than what was. Six of the levels a seed can draw are
the game's daily generators, and completing one routed into
`DailyTidy_GameState`, which runs the return-from-daily sequence: completion
count, streak, badge award. `SaveRedirect` scopes level data, not the profile's
daily counters. `DailyGuard` now answers false to
`LevelInterface.ReactToDailyCompleting` while a run is active.

### The connection pane broke every other modal in the game

`ConnectionPane` borrows `gm.menuManager.modalWindow` - a SINGLETON - and
replaced its confirm button's whole `ButtonClickedEvent`, destroyed its
`LocalizeStringEvent` and overwrote its caption, restoring none of it. After
one visit to the pane, any later dialog's Confirm ran `OnConfirm`. Now recorded
and undone on close.

### The campaign-save check was a once-per-day false positive - CONTROLLED

The first e2e run reported 14/15: "the campaign save is byte-identical" FAILED.
It was not a regression. Decoding the save (UTF-8 BOM, every codepoint shifted
up by 11) and diffing it showed `levelCompletionData` **identical**, with only
`saveTimestamp` and `dailyTidyProgress` moved - the latter having gained one
history entry per calendar day since the last write (09-03 to 09-07). The game
rolls its daily calendar at LAUNCH, before a session exists and therefore
before the redirect is armed; vanilla does it with or without the mod.
`CompleteCount` was 0 in both, and the 09-07 entry read
`opened: False, complete: False`, so nothing had credited a daily.

Predicted that a second run the same day would pass, because 09-07 was now
already in the history. It did: **15/15**. So the check was green except on the
first run after midnight - the pattern that teaches people to ignore a red
result.

Rewritten to compare campaign progress with `saveTimestamp` and
`dailyTidyProgress` excluded, plus a separate check that the daily
`CompleteCount` did not move - that one IS ours to protect. Four negative
controls, because green alone is not evidence here and this project has already
shipped one check that would have passed forever:

| Control | Result |
|---|---|
| round trip is stable | PASS |
| a new day does not trip it | PASS |
| a leak into `levelCompletionData` trips it | PASS |
| a credited daily trips the count check (0 -> 1) | PASS |

### What the suites said

| Gate | Result |
|---|---|
| Mod build, Release | 0 warnings, 0 errors |
| Core tests | 224 pass |
| apworld tests | 98 pass (96 + 2 new) |
| Fill stress, 25 seeds (5x default) | pass |
| Version consistency | 0.3.1 across 3 files |
| ASCII | clean |
| Release e2e, in game | 8/8 puzzles, 0 solve exceptions, no stale-slot warning |

### The invariant the drawer fix broke, and why it was widened rather than dropped

`test_no_group_needs_more_than_one_ability` asserted no controller group needs
two abilities. The eight new `dependsOn` edges create four that do - a bottle
inside a container inside a closed drawer genuinely needs `Containers` and
`Furniture`. Relaxing the bound to "no more than two" would have stopped it
catching what it exists to catch, so it now pins the exact set of four and
fails in either direction, including an edge being lost.

Checked first that nothing depended on the old bound: `items.py` has no
max-one assumption and `rules.py` uses `HasAll`. Then widened the fill sweep to
25 seeds as evidence rather than trusting that reading.

Also: the drawer test written alongside the fix was wrong - it asserted every
group in Paper Plane Supplies needs `Furniture`, but the chalk jigsaws were
deliberately excluded (assembled on the desk, not stored in the drawer). The
test was corrected to match the implementation, not the other way round.

---

## 2026-09-01

### S4 - how common are controller dependencies? PASS

Folded into the solution survey as a `dependsOn` column
([docs/data/controller-survey.tsv](data/controller-survey.tsv)).

**Only 9 of 380 controllers have any dependency, across 5 levels** - and most
are *mutual* pairs, which is more interesting than a dependency chain:

> **SUPERSEDED 2026-09-09.** True of the PREFAB survey, which is what this
> section measured, and misleading about the shipped table. Dependencies are
> wired at registration rather than serialised on the prefab, so the runtime
> sweep sees more than the prefab walk does; and containment, assembly and
> phase edges have since been authored on top. The table now carries **51
> edges across 13 levels**. The same wrong figure was quoted in
> ControllerGroups.cs, where it would have told a reader the dependency graph
> barely mattered.

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

**RETRACTED 2026-09-08 - see the entry at the top of this file.** These
counts are what the sweep SEES, not what the levels have; both reveal
controllers as the player solves them, and TupperwareNesting was watched
registering 7 in real play. The paragraph below is kept for the record and is
wrong.

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
prefabs. (**219 as of 2026-09-09** - eleven restored by the phase audit, plus
earlier restorations. The 334 also spans 186 levels including DLC; the
in-scope survey rows are 225.) Most of the difference is legitimate - unregistered components,
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
- **Radial Dance Party has 0 controllers and TupperwareNesting has 2** -
  **RETRACTED 2026-09-08, see the entry at the top of this file.** Waiting 25
  seconds is the same measurement the sweep makes, so its agreement proved
  nothing. Both levels reveal controllers as the player solves them; the
  prefab counts of 13 and 9 were right.
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
whatever state the game was in at the time.

**Closed on 2026-09-04, unresolved.** Dropped from the open list at droha's call:
it has not been seen in a long time and cannot be reproduced, so carrying it
around as an open item costs attention and buys nothing. The watchdog stays
armed and `tools/emptysoak.py` stays in the tree - between them, a recurrence
will announce itself with a log line and a toast rather than needing to be
described from memory. Reopen it if that line ever appears.

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

**The ordinary destination, verified separately.** The takeover applies to every
Continue, not only ones landing on a daily-pool level, so that case needed its
own test. Two earlier attempts failed for want of a level I could finish -
TupperwareNesting reported `solved=False` with both controllers forced, and
Spice Jars has two solutions so its retry screen offers something other than
Continue.

Picking the source properly rather than hoping fixed it. `NextUnfinishedSlot`
scans forward from the current slot, so the test needs a source that is
completable AND whose following slot is ordinary and unfinished. Reading the
track dump against the level table gives one: TrickOrTidy_Bones, one controller
and none of it ability-locked, followed by MerryMess_CandyCanes.

```
navigation: post-level Continue -> slot 7 (level 1015), launching it
state: gameState=Gameplay_GameState activeLevel=MerryMess_CandyCanes index=1015
controllers: 6 registered on MerryMess_CandyCanes
```

Loaded straight into gameplay with its six controllers registered, no modal, no
exceptions, and a screenshot of a playable puzzle. Both destinations - daily
pool and ordinary - now go through the takeover and land correctly.

## The pause-menu trap test, finally run (2026-09-04)

Parked since the cat-trap work because a scripted run cannot press Escape. It
took three attempts to open the menu, and the first two are worth recording
because both reported success:

1. `MenuManager.PostOpenMenuEvent(menu, null)` - accepted silently, opened
   nothing.
2. The same with a real `new MenuData()` - accepted silently, opened nothing.
3. Raising the game's own `GameEvent_MenuOpen` through
   `GameEventManager.AddGameEvent`, the way `solve:` raises
   `ObjectControllerSolved` - opened it.

That is the same "reported success, did nothing" shape as every other harness
fault in this feature, which is why the check is a `buttons` dump afterwards
rather than the call's return value. Added as the DevTools `pause` command.

Opening it also confirms something previously only argued: the pause menu on a
generator level carries all seven entries - Resume, Let It Be, Hint, Levels,
Settings, Reset, Exit - so the restore of Levels and Skip is right, seen rather
than inferred.

**The test itself passes.** With the menu open on Post-It Notes (Randomized),
a real Cat Trap sent from the server:

- the trap fired and reset the puzzle;
- the layout afterwards is byte-identical to the opening layout, parents and
  placed flags included;
- the seed survived (492449292), so the rebuild is the same puzzle;
- the state is still `Gameplay_GameState` on the same level, not dropped
  somewhere;
- the pause menu is still up and still has its seven entries, confirmed by
  screenshot - the rebuild underneath it did not leave the menu holding
  references to a destroyed level.

No exceptions logged. This closes the last item that was blocked purely on not
being able to drive input.

## Other players' names were markup (2026-09-04)

Found while answering an unrelated question, which is worth admitting: nothing
was looking for it.

`ApPalette.Paint` wrapped text in a TextMeshPro `<color>` tag and handed it to a
label. The text is not ours. Item names come from other games' apworlds, and the
SENDER's name is a slot name or an alias the other player chose and can change
mid-game with `/alias`. TMP parses rich text, so a player calling themselves
`<size=400%>` resized someone else's notifications, and one calling themselves
`</color><color=#FF0000>` could recolour the rest of the line.

Presentational rather than dangerous - TMP tags cannot do anything but draw -
but trivially abusable between friends, and cheap to close.

**The first fix was wrong, and only looking at the screen caught it.** Replacing
`<` with the numeric reference `&#60;` is safe and passed six unit tests. On
screen it rendered as the literal text `&#60;size=400%>` - TMP does not decode
numeric references. Safe and unreadable.

The fix is `<noparse>`, which I had rejected in a comment an hour earlier as
unsafe because content carrying its own closing tag breaks out of it. That is
true and it is also fixable: strip `</noparse>` from the content first, and
there is nothing left to escape with. Text containing no `<` is returned
untouched, so the overwhelmingly common case carries no wrapper at all.

Verified by rendering both cases and reading them:

```
Received <size=400%>BIG from a<3b
```

The tag shows literally at normal size rather than being obeyed, `a<3b` reads
correctly, and the colours still apply because the `<color>` wrapper sits
outside the `noparse`. Ordinary toasts are pixel-identical to before.

Eight tests in `ApPaletteTests`, including the one that matters: the tag TEXT
survives, because it is someone's name and they should see it. "Does not contain
`<size`" would have been the wrong assertion - it would also pass if the name
had been silently mangled.

## Connecting announced your whole item history (2026-09-04)

Reported in play: "every time you open up the game i see cat traps being sent,
are they being sent multiple times?"

They are not. Archipelago resends the WHOLE item list on every connect, by
design, and the mod already refuses to re-apply it - the counts are rebuilt from
the list rather than incremented, and traps are checked against `trapsSprung`
so none re-fire. The delivery was correct. The ANNOUNCING was not: one toast per
item meant launching the game produced a wall of "Received Cat Trap from Server"
for cats sent hours earlier, which looks exactly like being spammed with traps.

Now the toasts are suppressed for a three-second window that opens with the
session - before Ready, because items start arriving ahead of slot_data - and
one line is shown when the burst stops:

```
Connected to Archipelago - 30 puzzles
Restored 128 item(s) from the server
```

Silence would have been worse than the spam: someone reconnecting deserves to
know their things came back. A genuinely new item landing inside the window is
folded into the summary rather than lost - it is still applied either way.

Verified on a connect that replays 128 items: two lines on screen, and the log
still records every individual item for diagnosis.

## Hints: a claim made from three levels, corrected by sweeping 111 (2026-09-05)

I reported that generated levels have no hints, on a sample of one randomized
level. droha pushed back that the table looked odd. It was.

The sweep now records `hintAvailable` and `hintImages` per level, and the real
distribution is:

| group | levels | with hint images |
|---|---|---|
| base campaign | 69 | 69 |
| archive (event packs) | 26 | 26 |
| randomizable (generated) | 16 | **10** |

**Only six levels in the whole game have no hint images**, and they are exactly
the six daily-exclusive generator puzzles: Books, Batteries, Stamps, Post-It
Notes, Pencils and Procedural Grid. Every other level has at least one, most
have one, and some have up to five.

The ten OTHER randomizable levels - Telescope, Buttons, Calendar, Clock,
Microscope, Shells, Spice Jars, SpiderWeb, Trim Plant, Breadtags - are campaign
levels that also carry a randomizer, and they DO have hints. Generalising from
Stamps to "generated levels" swept those ten up wrongly.

**`HintAvailable` is not the flag I took it for.** The sweep reads it as true
for all 111 levels including the six with no images, while my single-level probe
read it as false for Stamps. Whatever it means, it is not "a hint exists" and it
is state-dependent; `hintImages` is the concrete signal and is what the table
now carries.

Measured against the draw rather than the level list, since the six can repeat:
a default 79-puzzle seed has **about 20 hintless puzzles, 25%** - not the 64% I
claimed from the wrong grouping.

That changes the conclusion. A hint item is not dead weight on two-thirds of a
run; it is useful on three-quarters of it. Whether it is worth minting is now a
design question rather than a technical veto.

---

## Hint Pages, 2026-09-05

Filler that does something. `Hint Page` is a new item that uncovers one page of
one puzzle's hint notepad, minted from the drawn plan at `hint_coverage`
percent (default 100). Measured over 8 default seeds: **86 Hint Pages per
seed**, and dead filler falls from **74% of the pool to 24%**.

### Answered by decompiling, not by guessing

`ilspycmd` against `BepInEx/interop/Assembly-CSharp.dll` settled in minutes what
had been open questions for two sessions. Worth remembering as a technique:
these are managed stub assemblies, so every signature is readable even though
no method body is.

- **Pages are separately unlockable.** `HintMenu.HintPages` is an
  `Il2CppReferenceArray<HintPage>`, each `HintPage` owns its own
  `CleanableSurfaceUI`, and `GetHintPageIndexFromSurface(CleanableSurface)`
  exists - the game itself must map a scrubbed surface back to a page. One item
  per page is correct.
- **`RandomizerHints` belongs to `LevelRandomizer`**, as a serialized
  `List<Sprite>` plus `virtual GetRandomizerHints()`. It is a DIFFERENT source
  from `LevelInterface.HintImages`, which the sweep reads, and Books and
  Pencils override it. So two of the six "hintless" generators may not be.
  Still open; worth at most 2 levels of 111.
- **Fractional erasing is feasible** - `SurfaceDetails.CleanNormal`,
  `SetCleanNormal(float)`, `ResetSurfaceDirt(surface)` all exist. Still not
  recommended: the player picks WHERE to scrub, so a partial allowance makes
  the reward luck.
- `CleanableSurfaceUI : CleanableSurface` and `CanBeWiped` is non-virtual, so
  one patch on the base covers the hint pages.

### Four defects, each found by measuring rather than by reasoning

Every one of these produced healthy-looking logs while being wrong.

1. **Charged for pages that were not on screen.** `HintMenu.HintPages` is a
   fixed pool of **eight** `HintPage` objects reused across levels, not one per
   page - a one-page level still has seven inactive behind it. Anything
   touching those surfaces billed for all eight. Guarded with
   `m_currentHintIndex`.

2. **Charged for merely opening the notepad.** `CanBeWiped` is polled the
   instant the notepad opens, not while the eraser moves. Caught by screenshot:
   the notepad open, scribble intact, and "Hint Page used - 0 left" already
   toasting in the corner. A player could not check whether a hint existed and
   back out.

3. **A silent, instant process death.** Fixing (2) by gating on
   `IsBeingWiped()` killed the game the moment the notepad opened - no managed
   exception, nothing in BepInEx's log, nothing in Player.log, no crash dump.
   `IsBeingWiped` reaches back into `CanBeWiped`, so the prefix called itself
   until the stack was gone. **A stack overflow in a Harmony prefix presents as
   the process vanishing, not as an error.** A re-entrancy guard stays in place
   permanently: the hazard is structural, not specific to that one call.

4. **`IsCleaned` does not mean the surface is clean.** Guarding on it looked
   obviously right and silently disabled the entire gate - it reports `true` for
   a page whose scribble is fully drawn on screen, so every page waved straight
   through. Almost certainly reads through a `SurfaceDetails` that is not
   populated until the surface registers with the manager. This is the same
   trap as `HintAvailable` above, twice in the same subsystem: **a boolean named
   after the question you are asking is not evidence it answers it.**

### The shipped design

`CanBeWiped` only ever says NO, and only when the player holds nothing. The
charge lands in a postfix on `LevelInterface.HintTaken`, the game's own event
for a hint genuinely uncovered past `HintManager.hintUsedAtNormal`. Opening a
notepad and closing it therefore never costs anything, which is what (2)
demanded, and there is no reliance on catching a wipe mid-poll, which is what
(3) proved unworkable.

Spend is recorded as a set of `"slot:page"` keys in the run file rather than a
counter, so a page you have paid for stays free forever - a counter would have
re-charged for re-reading your own hint. Keyed on slot, not level, because a
generator can be drawn several times into one run.

### Proven in play

- Both suites green: **200** Core, **91** apworld, including five new
  `hint_coverage` fill-stress configurations (0 / 50 / 100, 100% against a
  generators-only plan whose notepads are mostly empty, and 100% hints with
  100% traps and max skips competing for the same residual).
- **100% means exactly the pages the seed holds.** A 14-puzzle seed summing to
  12 pages minted 12 items; a 20-puzzle seed summing to 22 minted 22. The two
  generator slots in the first seed contributed zero, so the pool can never
  hold a page there is nowhere to spend.
- All three patches apply: `CleanableSurface.get_CanBeWiped`,
  `LevelInterface.HintTaken`, `HintMenu.Init`.
- Item received and counted; the pause-menu count moved 0 to 1.
- Pause menu renders `0 Let It Be` and `1 Hint` as small dim counts, and does
  not stack the tag across four consecutive opens. Confirms again that the
  entry named `Skip Button` reads **"Let It Be"** - match the object name,
  never the caption.
- Persistence round-trips: `hintPages: ["5:0"]` written to disk, and a run file
  predating the field loads as `0 hint page(s) opened`.
- Refusal and per-page charging were both observed directly in an earlier build
  (`hint: opened page 5:0, 0 left` followed by `hint: refused, none held` for
  page 1), which is what proves pages are billed one at a time.

### Still unproven, and it needs a human

**The charge on `HintTaken` has not been seen to fire.** Erasing cannot be
driven from the harness: a synthetic pointer drag through `ExecuteEvents` on
`HintMenu.Eraser` reaches the drag handlers but never puts the surface into a
wiping state, so the game never raises the event. The `erase` DevTools command
added for this does what it claims and is still not enough.

So one manual check remains: on a puzzle with a hint, hold one Hint Page, rub
the scribble off, and confirm the toast fires once and the count drops. Then do
it holding none and confirm the scribble will not come off. Until that is done,
treat the charging half as written-and-plausible rather than verified - the
refusing half and everything above it are measured.

---

## Hint Page polish, and backgrounds as real filler, 2026-09-05

droha reviewed the Hint Page numbers and found three real defects plus one
piece of work that had been described and never built. All four are now done.

### Traps back to meaning 25%

Hint Pages were drawn before traps, so `cat_trap_chance = 25` applied to the
small residual left after ~88 hint pages and bought **12** traps. Reordering so
traps come first restores it to **34**. Hint pages are unaffected at 88, because
100% coverage is capped by the pages the seed contains rather than by what is
left over - the traps come out of the filler underneath.

Measured over 8 default seeds after both changes:

| | count | share | does something |
|---|---|---|---|
| progression | 26 | 15% | yes |
| skips | 5 | 3% | yes |
| cat traps | 34.6 | 20% | yes |
| hint pages | 86.2 | 50% | yes |
| Level Background | 11.1 | 6.5% | yes |
| Menu Background | 8.4 | 4.9% | yes |
| **filler that does nothing** | **0** | **0%** | - |

Down from 74% at the start of this work.

### Why 88 pages on 79 puzzles

Worth writing down because it reads as an error: 61 of 79 slots have a hint,
and those 61 carry 88 pages between them - 45 slots with one page, 9 with two,
4 with three, 2 with four, 1 with five. Repeats count separately; that seed
drew Buttons three times and Spice Jars four, each its own notepad.

### The notepad now says what it costs

droha's ask: a note that scrubbing spends a page, a count on the page, and a
way to tell a page you have already paid for. All three are one label under the
paper, refreshed on `HintMenu.Init` and on `SwitchToHint` so turning to page
two updates it. Screenshotted in three states:

- `Rubbing this out uses a Hint Page - you have 1`
- `Already uncovered - reading this again is free`
- `No Hint Pages - find one to uncover this hint`

The fourth, `This puzzle has no hint`, shares its condition with the pause
menu's "no hint" tag and was NOT exercised - neither has been seen on a
generator with an empty notepad.

### The charge is finally proven

The postfix on `LevelInterface.HintTaken` had never been observed firing. A new
DevTools `hinttaken` command calls the method directly, and the whole loop now
has evidence:

    hinttaken: calling HintTaken on NeatStreak_Tool Drawer
    hint: page 5:0 read, 0 left
    -> run file: hintPages: ["5:0"]
    -> note changes to "Already uncovered - reading this again is free"

What remains unproven is only whether the GAME calls `HintTaken` when a human
scrubs. `HintsTakenCounter.CheckHintTaken` subscribes to the same event to keep
a Steam stat, so it demonstrably does; a synthetic pointer drag still cannot
reproduce it, because it reaches the eraser's drag handlers without ever
putting the surface into a wiping state.

### Backgrounds: three wrong hooks before the right one

`Level Background` and `Menu Background` replace Title Theme, Colour Scheme and
Daily Badge, which are deleted. The colour is `palette[(count - 1) % 10]` from
the game's own `ColorSchemesData` - a function of the count, never a reaction
to an arrival, so Archipelago's replay of the whole item list on every connect
lands on the colour the player already had.

**The camera is what renders the backdrop**, not `LevelInterface
.BackgroundColor`. Both are plain fields, so writing them always appears to
work; the tell was `BackgroundColor` reading back as the requested colour while
`Camera.main.backgroundColor` still held the level's own. A postfix on
`LevelManager.StartLevel` lost, and so did a call from `Checks.EnterSlot` - the
game paints the camera later in level setup than either. It is now held by a
per-frame comparison in the Ticker, which outlasts whenever the game writes
instead of trying to name the moment.

**The pause menu background is not a sibling of the buttons.** The layout is
`Main Menu/Buttons` beside `Main Menu/Theme/<theme>/Background`, so walking up
from `ButtonsContainer` and checking each ancestor's direct children never
reaches it. There are also three full-screen Images called `Background`, one
per theme, so a search including inactive objects picks by hierarchy order.
Asking only for objects live in the hierarchy leaves the one theme on screen.

### An exception that named the wrong call

Reading the background palette failed with `Index was outside the bounds of the
array` on element zero, through four different access paths, while `Length` and
`Count` both reported 10. None of the indexers was at fault: the throw came
from `ColorUtility.ToHtmlStringRGB` in the LOGGING, and interop surfaced it as
an array bounds error. It cost a rewrite of working code and a false comment
claiming the `BackgroundColors` indexer was broken, which has been corrected.

**Do not read an exception's text as naming the call that raised it** - through
IL2CPP interop it may not.

### Also worth keeping

- A build failure hid behind `deploy.sh >/dev/null`, and the game then ran the
  previous DevTools DLL while its probe output looked merely unhelpful rather
  than stale. deploy.sh's own header warns about exactly this; redirecting its
  output defeats it.
- A stale MultiServer from the previous day held port 38281. The new one prints
  "Hosting game at ..." and exits; the game connects to the OLD seed, which
  presents as a puzzle count that does not match the yaml. The line that proves
  a real bind is "server listening on 0.0.0.0:38281".

### Suites

202 Core tests (new: both background counts, the replay invariant, and that the
two are counted separately), 91 apworld tests including five `hint_coverage`
fill-stress configurations.

### Pause-menu counts: centred, live, and no longer permanent, 2026-09-05

droha asked for the numbers to be vertically centred, and asked whether they
had ever been seen to go UP. The honest answer to the second was no - the only
evidence was a `buttons` log line reading 0 then 1, never a screenshot. Chasing
it found two real bugs.

**The counts were being erased and nobody noticed.** Writing them once in the
`MainMenu.ShowHideMenuItems` postfix is not enough: the entries carry a
`LocalizeStringEvent` that refreshes AFTER the postfix and rewrites the label
with the plain caption. With the localiser left intact the probe read `Hint`
and `Let It Be` with no tag at all. The original build only worked because it
destroyed the localiser.

**Destroying the localiser was worse than it looked.** These are permanent
objects under `Menus/Main Menu`, not rebuilt per open, so destroying their
localiser freezes both entries in whatever language was loaded, for the rest of
the session. And because `AfterShowHideMenuItems` returned early when no run
was active, a count written during a run stayed baked into the label
afterwards, with nothing able to remove it.

Both are fixed by re-applying rather than destroying: `Navigation
.TickMenuCounts` rewrites the two cached entries each frame while a run is on
and the menu is open, so the localiser may win a frame and we win the next. A
language change is then picked up rather than fought, because `Caption()`
re-reads the entry whenever it does not find our own markup. The no-run branch
now restores the remembered captions instead of returning early.

Measured consequence, and a better answer to droha's question than the one it
started with: **the counts now update live.** Sending a Hint Page with the
pause menu already open took the entry from 3 to 4 without it being reopened.

**Centring** is `<voffset=0.18em>` wrapped OUTSIDE the `<size=55%>`, so em means
the entry's own font size and the shift holds at any menu scale. Inline text of
a smaller size shares the big text's baseline, which is why the count read as a
subscript before. Screenshotted at `2 Let It Be` / `3 Hint`.

### Where else those captions appear: one other place

A new DevTools `findtext:<substring>` scans every `TextMeshProUGUI` in the
loaded scene, inactive included, and reports path, liveness and whether a
localiser is attached. Asked about "Let It Be", "Hint" and "skip":

- `Menus/Main Menu/Buttons/Skip Button/SkipText` and `.../Hint Button/HintText`
  are the only labels carrying those captions, and are the two we write. Both
  still report `localised=yes`, confirming the localiser now survives.
- `Menus/Level Select/Levels Track/Skip Tooltip/Container/Skip Text`, and a DLC
  twin, matched a search for "skip" and are **nothing to do with skipping a
  puzzle**. Forced on screen with a new `skiptip` probe: it is the
  `Esc / [mouse] Skip` prompt in the top-right corner of the level select,
  which skips the track's intro ANIMATION. `SkipTooltip` carries a sprite per
  input device and a 3 second `skipExpireTime`, which is the shape of a
  transient input hint, not a puzzle affordance. No count belongs on it and
  the mod correctly does not touch it.

**Two corrections to the first pass on this, both from the same root cause:**
reading a label out of the object tree is not reading what a player sees.

1. The text is **"Skip"**, not "Skipppable". The probe reported the latter
   because the object had never been activated, so its LocalizeStringEvent had
   never run and the label still held the placeholder baked into the prefab.
   Activating it resolved the string. A label that reports `live=False` is
   reporting its editor placeholder, not its caption.
2. It was described here as "the one other surface that tells a player about
   skipping [a puzzle]". It is not; it is an animation-skip prompt. That was
   inferred from the object's name and the word "skip", which is the same
   inference-from-names trap that produced the bad `HintAvailable` and
   `IsCleaned` readings earlier in this work.

Nothing else in any loaded menu shows either caption, so the label rewrite has
no other surface to leak onto.

### Three defects from droha's manual test, 2026-09-05

The first real play session found three things no probe had. Worth recording
what each actually was, because in all three cases the code looked right.

**1. Two seconds of scrubbing before anything acknowledged you.** The charge
rides on the game's own HintTaken, which fires once the scribble is cleaned
past `HintManager.hintUsedAtNormal`. Measured, that threshold is **0.25** - a
quarter of the page - and nothing on screen moves while the game makes up its
mind, so it reads as the feature being broken. Lowered to **0.12**. Not to
zero: the merest brush of the eraser would then spend a page, which is the
opposite trap.

Getting the value applied took two wrong attempts, both of which failed
SILENTLY:

- A postfix on `HintManager.Start` guarded by `Track.Active`. The patch applies
  and the method exists, but Start runs as the scene loads, which is before the
  client has connected - so the guard skipped it every single time.
- `FindObjectOfType<HintManager>()` as a fallback. That sees only objects
  active in the hierarchy, and the manager sits inactive until a notepad wants
  it. `Resources.FindObjectsOfTypeAll` finds it.

The value is now applied from `LevelStarted`, which runs per level with a run
known to be on.

**2. The note label leaked one copy per level.** droha reported the text under
the page as "overlapping itself" and unreadable. It was four labels stacked:
`Menus/Hint Menu/Notepad` OUTLIVES the level, and `LevelStarted` cleared the
cached reference without destroying the object - so the next level could not
find the old label and built another over the top. The comment on that line
asserted the notepad "is rebuilt with the level", which is exactly the false
belief that caused it; both the code and the comment are corrected.
`NoteLabel` now looks for an existing child by name before making one.
Confirmed: three levels visited, one label.

**3. The Hint entry was missing if you paused during the load.** The game hides
it while a level is still coming up, and `AfterShowHideMenuItems` restored only
Levels and Skip. Pausing early therefore showed a menu with no Hint at all, and
it appeared only after resuming and pausing again - which reads as a bug in the
mod rather than as timing. Hint is now restored alongside the other two.
Confirmed by pausing with zero settle time after a level boot.

**Also verified here, at last: the "no hint" pair.** On Procedural Grid Puzzle
(one of the six generators with an empty notepad) the pause menu reads
`no hint  Hint` rather than a count. That was the last state in the Hint Page
work never to have been seen.

### Every level has a hint after all, 2026-09-05

droha opened the hint on Pencils (Randomized) - a level this project had called
hintless all the way through - and got a real hint. Measured immediately after:

| level | HintImages | RandomizerHints pool | this layout uses |
|---|---|---|---|
| Books (Randomized) | 0 | 7 | 2 |
| Pencils (Randomized) | 0 | 5 | 2 |
| Batteries / Stamps / Post-It / Grid | 0 | 1 | 1 |

**Levels in the game with no hint from any source: zero.** The claim that six
were hintless, repeated in this log, in the WebHost page and in three commit
messages, was wrong from the first sweep. All of them are corrected.

The cause was named a session earlier and not acted on. `LevelRandomizer` has
its own `List<Sprite> RandomizerHints` and a virtual `GetRandomizerHints()`,
which the static decompile showed Books and Pencils overriding. That was
written down as "worth at most 2 levels of 111, so it does not block Stage 1"
and left as an open question. It was worth all six, and the consequences were
not cosmetic:

- the generator minted NO Hint Page for those slots while a player could spend
  up to two on each, so the pool was short
- the pause menu and the notepad both told the player the puzzle had no hint

Both sides now read the larger of the two sources: `Hints.PagesHere` at
runtime, `max(hintImages, randomizerHints)` in `data.py`. The level sweep
records `randomizerHintPool` and `randomizerHints` separately, because they
disagree - the field is the authored list, the method is what the generated
layout selected from it.

**The lesson is not that the symbol was missed - it was found, and written
down.** It was ranked as small on a guess about how many levels it touched,
without measuring, and the guess was out by a factor of three. An open question
about a data source is worth the ten minutes to close before building numbers
on top of it.

### Rebalancing that fell out of it

Pages per default seed went from ~88 to ~112, which no longer fits: at 100%
coverage the hint tier was clamped, about 8 pages per seed had no item anywhere
in the multiworld, and backgrounds were squeezed to zero. Measured:

| coverage | hints | traps | backgrounds | pages with no item |
|---|---|---|---|---|
| 100% | 103.0 | 33.8 | 0.0 | **8** |
| 85% | 94.2 | 33.8 | 8.8 | 0 |
| 70% | 77.5 | 33.8 | 25.5 | 0 |
| 55% | 60.8 | 33.8 | 42.2 | 0 |

droha chose **50%** as the new default, on the reasoning that nobody uses a
hint on every level and the reassurance of full coverage is not worth the pool
being nothing else. A default seed now reads:

| | count | share |
|---|---|---|
| progression | 26.0 | 15% |
| skips | 5.0 | 3% |
| cat traps | 34.6 | 20% |
| hint pages | 55.8 | 32% |
| backgrounds | 50.0 | 29% |

### Erase threshold

Lowered again after droha tried 0.12 and still found it slow: `hintUsedAtNormal`
is now **0.05**, down from the game's own 0.25. Confirmed in the log as
`taken-threshold 0.25 -> 0.05`.

---

## Measured against the CW4 mod, 2026-09-06

`cw4-archipelago` is the same shape of project - a BepInEx IL2CPP mod owning
both halves of an Archipelago integration - and further along. Reading its
history as a list of mistakes already paid for turned up two live defects here
and confirmed four things we do correctly.

### We were violating the apworld specification

Archipelago ships a generic compliance suite, `test/general`, that every world
must pass. This project had never run it. Run for the first time: **322 tests,
four minutes, one failure, and the failure was ours.**

    archipelago.json for 'A Little to the Left' must not define 'version',
    see apworld specification.md.

`test_no_container_version` forbids both `version` and `compatible_version` in
a world manifest - they describe the .apworld CONTAINER and belong to whatever
builds it. Ours declared both, from the first commit, and nothing objected
because nothing ran the test that exists to catch it. CW4's manifest carries
exactly the four legal keys.

Deleted; the suite is now green at 322/322 and runs in CI beside the other
apworld jobs.

**On its four-minute runtime.** It iterates every installed world, and there is
no way to scope it at 0.6.7. `AP_TEST_WORLDS` - a comma-separated list that
restricts world auto-loading, keeping only the named worlds plus the suite's
own fixtures - was added in **0.6.8**
(`worlds/__init__.py`, `_SUITE_FIXTURE_WORLDS`). droha suggested it existed and
was right; a first look for it here concluded it did not, from grepping the
pinned 0.6.7 tree and not upstream. When the minimum moves to 0.6.8 or later:

    AP_TEST_WORLDS=alttl python -m unittest discover -s test/general -t .

**Do not use `unittest -k "A Little to the Left"` as a substitute.** It appears
to do the job - three seconds instead of four minutes - and does not. Only the
manifest tests generate a class per world, so `-k` matches three tests and
silently skips `test_fill`, `test_ids` and `test_reachability`, which loop over
worlds inside the test body. Measured: 3 tests instead of 322. That is not a
100x speedup, it is a 100x reduction in coverage wearing one.

### The version was already wrong, and we broke it ourselves

One number lives in three files. During the Hint Page work `world_version` was
bumped twice for id-table changes and the other two were not touched:

    csproj <Version>                 0.1.0
    Plugin.cs [BepInPlugin]          0.1.0
    archipelago.json world_version   0.3.0

The comment beside the csproj version read *"in sync only by care"*. Care is not
a mechanism. CW4 shipped this exact bug twice - once across twelve commits that
included an item rename, leaving a player holding a mod and an apworld that
agreed on a version and disagreed about what the items were called.

All three are now 0.3.0, and `tools/check-version.py` fails the build when they
disagree or when the manifest carries a container key. Verified both ways: it
passes on the tree and fails when a version is edited out of step.

### The connect button was dead exactly when it was needed

Three defects, one family, all of which CW4 hit first:

- **The click was swallowed.** `Plugin.Attempt` returned early while
  `_connecting`, so pressing Connect during an attempt did nothing at all - one
  log line and no status change. It now supersedes instead.
- **There was no CANCEL.** The label was `IsConnected ? "Disconnect" :
  "Connect"` - two states for three situations. During an attempt or a backoff
  it read "Connect" and did nothing, and a retry countdown could only be
  escaped by quitting the game. Three states now, with CANCEL clearing the
  countdown.
- **A superseded attempt could install itself.** The connect callback set
  `_session` unconditionally on success, so a slow attempt landing after a
  cancel would connect anyway - the one outcome the player had just declined.
  Every attempt now carries a generation, bumped by each connect, cancel and
  disconnect; a stale one closes its own socket and returns.

### Retry now matches the reference client

`RetryPolicy` allowed three attempts and stopped. Archipelago's own client is
unbounded, and giving up quietly was worse than it sounded given the button to
start again was itself broken. Now unlimited by default at 5, 10, 20, 40, 60
seconds. `maxAttempts: 0` means unlimited; a positive value still bounds it.

A refused login is still never retried - that check was already right and
survives unchanged.

Measured in game against a dead port: label reads **Cancel** while busy, the
status line reads `attempt 2` with no denominator, the delays logged 5s then
10s then 20s, and pressing Cancel produced `connection attempt cancelled`
followed by **zero connect attempts in the next twelve seconds** - with a 20s
retry pending that would otherwise have fired.

### One consequence for existing installs

BepInEx persists config, so an install that has already run keeps
`MaxRetries = 3` and will still give up. The new default only reaches fresh
installs. Not migrated automatically: silently rewriting a player's config is
worse than the bounded policy. Worth a line in the release notes.

### What we already do correctly - deliberately unchanged

Two of these are places CW4 got it wrong first, so "fixing" ours would have
introduced their bug:

- **State cannot cross multiworlds.** CW4 guarded only on slot name, and
  location names are identical across seeds, so a check earned in one seed was
  accepted by the next joined under the same name. We key on (slot, seed) via
  `SaveRedirect`, so it cannot happen by construction.
- **A deliberate disconnect stays disconnected.** CW4's close handler consumed
  its intentional flag and the close event arrives more than once. Ours guards
  on `Connected`, which `Disconnect()` clears first.
- A refused login is not retried; offline progress is queued and pushed, never
  reverted.

### And a lesson taken from their docs rather than from a bug

CW4's `docs/in-game-testing.md` has a section called *"Put the player's
environment back when the harness exits"*. Ours does not, and it showed: this
session changed `TargetVirtualDesktop` in the DevTools config and repeatedly
rewrote the player's `.run.json`, announcing both rather than restoring them.
The config was snapshotted and restored by hand for the retry test above. That
should be the harness's job, not a habit.

---

## 2026-09-06

### Stage 7 - harnesses now put the environment back. PASS

`tools/harness_env.py`: an `Environment` context manager that snapshots both
plugin configs and every file in the game's save folder on entry, and on exit
restores them and deletes anything the run created. `tools/playthrough.py` and
`tools/emptysoak.py` are wrapped in it. Written up in
[docs/in-game-testing.md](in-game-testing.md).

Verified against the real install rather than a fixture, by fingerprinting all
17 protected files, mutating them the way a harness does, and comparing after:

| Case | Mutation | Result |
|---|---|---|
| Normal exit | config repointed to `127.0.0.99:39999`, `AutoConnect=false`, `MaxRetries=7`, `TargetVirtualDesktop=3`, a junk `.run.json` created, `save1.json` appended to | 17 put back, 1 removed, **all 17 hashes identical to before** |
| `KeyboardInterrupt` | as above | identical |
| Exception | as above | identical |
| Hard kill, then `--restore-latest` | config left at `Host = dead.example`, orphan run file left | `Host = localhost`, `AutoConnect = true`, orphan gone |

The campaign save was deliberately corrupted in the first case, because the
file the mod must never touch is the one worth proving recoverable.

### The bug the round-trip test found, which nothing else would have

The first version resolved the save folder as
`~/AppData/LocalLow/Max Inferno/A Little to the Left` - the studio name as it
is written everywhere a person reads it. The real folder is
`maxinferno/A Little To The Left`.

**Nothing failed.** `glob` matched no files, the snapshot contained the two
config files, `_files_to_snapshot()` returned 2 instead of 17, and the harness
printed that it had protected the environment. A harness whose whole purpose is
protecting saves would have been protecting no saves, and saying so cheerfully.

It is now found by glob. `_require_save_dir()` also refuses to run when it
cannot be found, because restore deletes save-folder files absent from its
manifest, and an empty `SAVE_DIR` makes that glob relative to the working
directory - which is the repo.

Same shape as the `emptysoak` dwell-time bug and the `-k` near-miss: the
harness reported a clean result while being unable to produce a dirty one.

### Stage 4 - playable with the server down. PASS

An unreachable server at launch used to mean no run at all: `Ready` never
fired, so the track never went up and the player got the vanilla game with a
finished campaign in a save they could not reach. A mid-session drop already
kept playing, so the two cases disagreed for no reason.

`CachedSession` (Core, tested) holds the slot name, the seed, the draw and the
**received item list**; `SlotCache` (mod) writes it to
`alttl-last-session.json` beside the run files, debounced to at most one write
every two seconds. `Plugin.StartOffline` mirrors `OnReady` minus the two steps
that need a socket.

Caching the item list is the half that is easy to miss. Without it the run
comes back with zero packs and no abilities - a locked track, which is worse
than no track.

Measured by `tools/offline-test.py`, five phases against the real game and a
real MultiServer, **7/7**:

| Phase | Claim | Result |
|---|---|---|
| 1 ONLINE | a real connection writes a cache | seed 11789846964930290912, 24 puzzles, 21 items |
| 2 OFFLINE | no server at all, the same run returns | same seed, same 24 puzzles, same 21 items, 12 packs held, `track: 24 puzzles, 10 open` |
| 3 EARNED | a check solved offline is queued | 1 owed in the run file |
| 4 REJOINED | the next connection sends it | `checks: sent 1, 0 still owed` |
| 5 NOT STALE | a regenerated seed under the same slot name wins | new seed 43809784604865360243, 18 puzzles, cache replaced |
| 5 NOT STALE | the old run's save survives it | present and untouched |

Phase 5 is the hazard the plan singled out, and it holds by construction
rather than by check: the save is named `save_ap_(slot)_(seed)`, a regenerated
draw fingerprints differently, so a stale cache can be wrong about the content
of a run but cannot write into a run it does not belong to.

### The deferred inventory clear, and a claim withdrawn

`Inventory.NewSession()` cleared the item list at the top of every connection
ATTEMPT. That list is now written to disk, so it must never be briefly empty
while a live run holds items - the clear is therefore deferred to the first
thing the new session produces, an item or `Begin`.

**The comment first written for that change described a failure that does not
happen**, and it took three control runs of
`tools/offline-reconnect-test.py` to establish it:

1. **Immediate clear plus a recount, built on purpose. PASSED.** The test was
   reading only the log after `connecting to localhost`, and the wipe is
   logged just before it. The evidence sat in the window the test discarded.
   Fixed to read both windows; the same control then failed, as it must.
2. **The EXACT original clear, with no recount. PASSED - and that is the real
   answer.** Nothing recounts, because the plain `Clear()` never called
   `Apply()`, so the displayed state stays stale-correct. There was no track
   collapse and no lost abilities. The comment claiming otherwise has been
   corrected in the source.
3. **The deferred clear that ships. PASSES.**

So the change stands - a value written to disk should never be briefly wrong,
and the residual exposure is a cache write already pending from a recent item
serialising the emptied list - but it is a precaution taken when items started
being cached, not a fix for a bug anybody saw.

The first control passing is the finding worth keeping. A negative control is
not a formality: it caught a test that would have reported green forever while
measuring the wrong region of the log.

### Two harness traps hit again on the way

- **`SKIP_REQUIREMENTS_UPDATE=1` is needed locally too, not only in CI.**
  `MultiServer.py` runs `ModuleUpdate.update()`, which PROMPTS rather than
  failing when a requirement drifts - here `platformdirs 4.10.1` against a
  pinned `4.9.4`. With no console the read is an EOFError and the server dies
  before binding, showing only `No response from localhost:38281`. A whole
  phase was lost to it. Server readiness is now the port accepting, not a
  sleep, and the server's stderr goes to its own file.
- **BepInEx truncates `LogOutput.log` on every launch.** A probe that recorded
  the log size before launching and then seeked to it read nothing at all and
  printed no lines, which looked like the command having had no effect.

### Stage 6 - a release a player can install. PASS

Three assets, one version, built by `tools/package-release.py`:

    ALTTLArchipelago-0.3.0.zip   396,349 bytes   the BepInEx plugin
    alttl.apworld                 40,717 bytes   the world
    A Little to the Left.yaml      3,862 bytes   the player template

Plus `CHANGELOG.md` and [docs/installation.md](installation.md).

**The packager refuses rather than warns.** Both refusals were tested by
causing them:

- Version drift. `world_version` was set to 0.9.9 against 0.3.1 elsewhere; the
  script printed all three and exited 1 without building anything.
- An unrecognised file in the build output. The plugin contents are an
  ALLOWLIST, not a glob, because the build sits beside interop assemblies
  derived from the game which must never be redistributed. Verified by
  listing the zip: exactly the five expected DLLs and a README, no interop.

**The artifact was installed the way a player installs it**, not merely built.
This matters because `tools/deploy.sh` only ever builds Debug, so the Release
DLLs that actually ship had never been run. Extracting the zip into the game
folder and launching: **5/5** - plugin loaded, all seven feature groups
patched, none disabled, no exception, and the Archipelago menu entry present.

The first run of that check reported the menu entry FAIL. It was the test:
it sampled the log as soon as `features live:` appeared, and the entry is
added later when the title screen builds. Same shape as the reconnect test -
a harness reading the wrong window.

### The player template is pinned to the options

`apworld/alttl/player.yaml` ships as a release asset and is the file nothing
else touches, so it is the one that rots. A template omitting an option is
silent - generation succeeds on the default and the player never learns the
setting exists. One naming a REMOVED option is worse: Archipelago rejects the
whole yaml.

`test_player_yaml.py` pins it both directions, plus that every value is the
documented default and that `requires: version` equals `minimum_ap_version`.
It is deliberately excluded from the `.apworld` - it is handed to the player
separately - but lives in the package so the test can find it.

Verified by generating a real seed from the shipped file untouched: 177 items,
`AP_43809784604865360243.zip`.

Its first version failed by demanding a description for `plando_items`:
`ALTTLOptions` inherits the whole of `PerGameCommonOptions`, and those belong
to Archipelago, not to this template. Now scoped by subtracting
`PerGameCommonOptions.type_hints`.

### The release, installed and played the way a player would. PASS

`tools/release-e2e.py`: clean the install to vanilla, install the mod from its
zip, install the world from its `.apworld` with no loose copy, generate an
8-puzzle two-pack seed, play it to the credits against a real MultiServer, and
check the campaign save was never written.

The `.apworld` was **downloaded from CI** (run 34039766248, sha256
`91c29ce527a4c587`), not built locally. The mod zip cannot be: it needs the
game's interop assemblies, which a public runner does not have and which must
never be committed.

**12/12.** 8 puzzles beaten, packs opened 4 -> 6 -> 8, credits unlocked,
`save1.json` byte-identical before and after, and the run in its own
`save_ap_droha_43809784604865360243.json`.

The goal is asserted from BOTH ends, and the second end was missing at first.
The original run checked only `goal: reported to the server` - the mod saying
it had sent one, which is the mod grading its own work. MultiServer's stdout
was going to /dev/null. It is now captured to
`testserver/logs/e2e-server.log` and the server's own words are an assertion:

    Notice (all): droha (Team #1) has completed their goal.
    Notice (all): Team #1 has completed all of their games! Congratulations!

Both match strings were taken from `MultiServer.on_goal_achieved` rather than
guessed.

The slot rotation earned its place twice over. In the first full run slot 1
(`Mirror`) failed to open and completed five rounds later; in the second,
`MerryMess_CandyCanes` was unfinishable in round 6 and completed in round 9.

### The CI and local .apworld builds are not byte-identical

Three JSON files differ, each by exactly its line count:
`archipelago.json` 118 bytes from CI against 124 locally, on six lines. Git
checks out CRLF on Windows and LF on the Linux runner, so the Windows build
embeds CRLF.

The parsed content is identical and both load and generate. But
`build_apworld.py`'s docstring says a fixed timestamp and sorted order make
the output byte-identical, "which makes 'did the world actually change?'
answerable" - and that only holds per platform. Not fixed, recorded.

### boot: is reliable exactly once per game launch

The finding that cost the most, and it is not the mod.

The first level booted after a launch solves cleanly. Every later one in the
same session dies inside the GAME's own code:

    System.NullReferenceException
      at LevelInterface.CheckWinCondition (GameEventManager+GameEventData)
      at GameEventManager.TryDispatchEvent

The controller flags still flip to `solved=True` and the checks still fire, so
it presents as the mod refusing to notice a finished puzzle.

**Measured across two runs: booted first, 21 solves and 0 throws; booted
later, 0 clean and 48 throws.** None of the mod's Harmony patches appear in
that stack, and the same levels complete normally when they are the first one
booted - `Workbench` did, in an earlier run that reached it another way.
Escaping the completion screen with `replayselect` first does not help: the
split is per LAUNCH, not per completion.

So the harness plays one puzzle per game launch. Not pure cost - every puzzle
now also exercises a reconnect, the item replay and the flush of anything
owed, eight times over.

Menu navigation was the alternative and all three routes failed. The Menu
button in `RetryUI_GameState` opens the pause menu rather than leaving;
`menu:title` sets the state without a title scene, so Play reports "no live
TitleMenu"; and `clicktrack` resolved all ten card names correctly while
starting nothing, which is the stale-icon no-op DevTools' own comment warns
about.

### Five harness bugs, and the one that matters

Every failure in building this harness was in the harness. Recorded because
the shapes recur:

1. **A log offset taken before a launch.** BepInEx truncates `LogOutput.log`,
   and resetting only when the file shrinks is not enough - the new log can
   pass the old size before the first sample. It sat waiting for a
   `connected.` line already on disk. Now the log is deleted before launching.
2. **A log line's first bracket is the BepInEx prefix.** Parsing
   `[3] ChalkPurple ... solved=False` by splitting on `[` yields
   `Info   :ALTTL Dev Tools`, `int()` throws, every line is skipped, and the
   caller concludes nothing is unsolved. **Two runs issued no solve command at
   all.** That parser now has a self-test that runs before the game is
   launched - the check that would have saved two four-minute round trips.
3. **Waiting for a header is not waiting for the list.** `controllers:`
   appears before the per-controller lines.
4. **A line you care about consumed by an unrelated wait.** The pack
   announcement arrives mid-puzzle, so the harness re-derives progress from
   the whole transcript rather than the newest chunk.
5. **One pass of solves is not enough.** A Cat Trap resets the puzzle
   mid-level; the solver now re-reads what is unsolved, up to five times.
   Traps are left at their default rate deliberately.

### The game opening, closing and opening again. FIXED

Not the harness, though the harness was making it worse.

Running the exe directly makes Steam's DRM stub call
`SteamAPI_RestartAppIfNecessary`, which relaunches the game through Steam and
exits the process that was started. On screen that is the window appearing,
vanishing and coming back. **Measured: PID 70840 at t+0, replaced by PID 76964
at t+4s.**

Fixed with `steam_appid.txt` containing `1629520` in the game folder - the App
ID read from `steamapps/appmanifest_1629520.acf` rather than the store URL.
Retested: one PID, no relaunch. `harness_env.ensure_no_steam_relaunch()`
writes it before any launch and it is deliberately left in place.

The harness was also launching twice on purpose: phase 5 opened the game to
check the connection, closed it, and phase 6 opened it again immediately.
That is gone - `play()` owns every launch and the first one it opens is what
the connection assertions are made against.

### One session plays the whole run. The "limit" was three attempts short

**This section replaces an earlier conclusion that was wrong.** It said the
game could only manage about two scripted puzzles per session and that
relaunching was the workaround. droha asked why the harness could not simply
go back to the level select or the main menu instead of tearing the level down
by hand. It can, and that is the fix.

The working exit is the FULL unwind, run after every puzzle:

    replayselect  ->  menu:levels  ->  menu:title

Measured, five levels per attempt:

| Exit after each puzzle | Result |
|---|---|
| nothing, rely on boot:'s teardown | 2 clean, then 28 exceptions |
| `leave` - the pause menu's Level Select | 2 clean, then 16. After a BEATEN level it does nothing at all: the state stays `RetryUI_GameState` |
| `replayselect` alone | worse; a level that had been passing began failing |
| **`replayselect` -> `menu:levels` -> `menu:title`** | **5 clean, 0** |

The game wants the whole stack unwound, not merely the level destroyed. Every
route tried before had stopped one screen short, and each partial result was
read as evidence of a hard limit rather than of an incomplete attempt.

Verified end to end afterwards: **13/13, one game launch, 8 puzzles beaten in
8 rounds**, with the launch count now an assertion rather than a note.

### The DevTools bug found on the way, which is the other half



`boot:` tore down only `ActiveLevelInterface`. A level you have FINISHED is no
longer the active one, so its `CheckWinCondition` stayed subscribed to the
global event bus and the next level's solves died inside it. Fixed to tear
down whatever is `activeInHierarchy`, which is exactly the running level.

That fix is correct and necessary, and on its own it is not sufficient - it
moved the failure from the second puzzle to the third and no further. Paired
with the unwind above it holds for a whole run.

### Two wrong teardown filters, and the second one was the bug

Worth writing down because the failure imitated the thing it was meant to fix.

- `gameObject.scene.IsValid()` - the filter used elsewhere in DevTools - matched
  all 293 LevelInterface objects.
- Excluding the 186 prefabs by identity still matched 107.

Those 107 are `<level> Interface(Clone)` objects the level select keeps POOLED
in MainScene, every one inactive. One of them is
`NeatStreak_Bathroom Drawer Interface(Clone)`. Destroying it during the first
boot is exactly why booting that level third came up unwired and threw on
every solve - so for two rounds the half-fix WAS the third-level failure, and
it looked like evidence that the fix was insufficient rather than harmful.

`activeInHierarchy` is the right discriminator: prefabs and pooled clones are
inactive, the level being played is not. It tears down exactly one.

**Verified after all of it: 12/12, 9 launches for 9 rounds, and the server
confirming the goal.**

### Player-facing post-puzzle navigation. PASS

Separate from anything the harness does, because the harness drives the game
with `boot:` and `solve:` and a player uses neither.

**The next-level arrow works.** Verified on a clean run: finishing slot 0 and
pressing it gave

    navigation: replay Next -> slot 1 (level 79), launching it
    controllers: 8 registered on Mirror levelInstance=-29974

Eight controllers registered is a real, loaded, interactive level - it is the
next UNFINISHED slot in the run, and it is not the Daily Tidy page. That last
part is the whole point of the patch: the game routes by level KIND, so a
daily-pool level as the next slot would drop the player out of their run
entirely.

**The pause menu's Level Select works** - confirmed by droha in actual play on
2026-09-06, which is the test that counts.

### DevTools `replayselect` is not a reliable way to verify Level Select

A probe of the COMPLETION screen's Level Select reported it stuck: the mod's
handler fired (`navigation: post-level menu -> the run's track`) and the level
select never came up.

**Do not read that as a product finding.** `replayselect` locates its
`ReplayMenu` with `Resources.FindObjectsOfTypeAll` filtered on
`gameObject.scene.IsValid()`, and that filter is known-wrong in this game - it
is the same one that matched 107 pooled `Interface(Clone)` objects in the boot
teardown, and the same class of fault as `clicktrack` resolving every card name
correctly while starting nothing. It is very likely calling `LevelSelect()` on
a menu that is not the one on screen.

The pause menu's Level Select and the completion screen's run through the SAME
helper, `Navigation.GoToTrack`, which calls the game's own
`GoToLevelSelectForLevel`. The pause menu route being confirmed in play is
therefore evidence that the helper works, and that the probe was measuring its
own stale object.

If this ever needs settling properly, press the button by hand. Scripted
menu-object lookup has been wrong about this game three times now.

### Why mixing harness routes throws: SOLVED

**One arrow press poisons the rest of the game session.** Measured as a
controlled pair on a fresh multiworld, everything else identical:

| Run | Result |
|---|---|
| with a single arrow press | 2 of 8 beaten, **103** exceptions, 9/15 |
| without one | 8 of 8 beaten, **0** exceptions, 15/15 |

The mechanism, from the log rather than from reasoning:

1. The arrow launches through the mod's `GoToNext`, which calls `StartLevel`
   without releasing the level just finished.
2. Leaving a finished puzzle through the MENUS deactivates its GameObject
   without destroying it.
3. `boot:`'s teardown filters on `activeInHierarchy`, so it skips that level.
   The log shows it plainly - the boot after a menu exit prints **no teardown
   line at all**, where the boot after an arrow prints one.
4. The skipped level's `CheckWinCondition` stays subscribed to the global
   event bus, and the next level's synthetic solves die inside it.

That is why neither route failed alone: the arrow leaves the old level ACTIVE
so the teardown catches it, the menus leave it INACTIVE so it does not. Only
the combination exposes the hole.

**The fix is to not combine them in one session.** `check_arrow()` verifies
the arrow in a throwaway game and closes it; the run then gets a clean one.
Two launches, about ninety seconds, and the arrow - the route a player uses -
stays asserted.

Widening the teardown rule was tried instead and is WORSE. `Level != null`
catches chapter headers, which are `LevelInterface`s too: the run began
loading and "completing" `01__Chapter_HomeSweetHome`, and exceptions went from
5 to 8. Do not reach for that again without a way to tell a chapter from a
puzzle.

**Verified after the fix: 15/15, 8 of 8 beaten, 0 exceptions, 2 launches, the
server confirming the goal.**

### Four hypotheses ruled out on the way there

The symptom: a full eight-slot run that alternates the next-level arrow with
the unwind-and-boot fallback stalls with NullReferenceExceptions inside
`LevelInterface.CheckWinCondition` - 14 in one attempt, 5 by round four in
another, both stuck around 2 of 8. Entering every level the same way does not
do this: 13/13 and 8 of 8, repeatedly.

Harness-only. It needs `boot:` and synthetic `ObjectControllerSolved`
dispatches, and a player uses neither.

**Ruled out, each by measurement rather than argument:**

| Hypothesis | Test | Result |
|---|---|---|
| The arrow leaves the old level alive | 5 puzzles chained by the arrow alone | 5/5, **0 exceptions** |
| `GoToNext` needs to release the current level | added the release to the mod | `GoToNext` ran 4 times, released **0** - the finished level is not `activeInHierarchy` then. Reverted rather than ship a no-op |
| Leaving via the MENUS deactivates a level without destroying it, so the teardown skips it and it stays subscribed | probe counting inactive LevelInterfaces that still hold a loaded Level, across boot / arrow / menus / unfinished, six combinations | **0 leaked** and **0 exceptions** in all six |
| Solving an ability-LOCKED controller is what throws | fresh multiworld, solved every controller of two genuinely gated levels (3 and 5 abilities locked) | 0 of 3 and 0 of 12 threw |

None of these was it, and the reason none of the probes reproduced anything
is that none of them pressed the arrow AND then used the menus - see above.
The revisit probe in particular ran 20 visits with partial solves, real
ability gating and menu exits, and stayed clean, because it never touched the
arrow.

### The probes were quietly testing a fully unlocked multiworld

Found while chasing the above, and it invalidated two probe results before it
was noticed.

`clean()` removed the local save but not the `.apsave` beside the seed, which
is **MultiServer's** record of what has been checked and sent. With it in
place, connecting replays every item ever collected, so a run that looks fresh
has every ability already unlocked. A probe written to ask about ability locks
reported "0 locked" twice before the cause was spotted.

`generate()` happens to clear it by emptying the output folder, so the full
test was never affected - only probes calling `clean()` on its own.
`clean()` now removes it too.

### A verified fix deleted by a careless revert

`git checkout src/ALTTLDevTools/Plugin.cs` was used to drop a temporary
diagnostic and took the `activeInHierarchy` teardown fix with it - the file
had both, and only one was wanted. Caught by grepping for the fix rather than
trusting the checkout, restored, and confirmed present in the deployed DLL.

Worth the note because the same command will do the same thing next time: a
whole-file revert is not a way to remove one edit from a file that has two.
