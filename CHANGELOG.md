# Changelog

Every release ships three files that carry the same version number and are
meant to be used together: the mod zip, `alttl.apworld`, and the player yaml.
A mod and an apworld that disagree about the version disagree about the item
table, and nothing detects that at runtime - so the version is checked by
`tools/check-version.py`, in CI, and again before a release will build.

The format is loosely [Keep a Changelog](https://keepachangelog.com/).

## 0.3.1 - unreleased

Fixes from droha's first full 79-puzzle playthrough of 0.3.0. The BepInEx log
from that session survived and is the evidence for most of what follows;
several reports that read as separate bugs turned out to share a cause.

### The one that caused three of the reports

The log carried two warnings:

    track: ignoring a pending slot 22 set 9 frames ago
    track: ignoring a pending slot 39 set 8 frames ago

`Navigation.AfterGetNextLevelIndex` is a postfix on `GetNextLevelIndex`, which
the game also calls while building the post-level UI - so finishing a puzzle
armed a slot speculatively. `Track.BeforeStartLevel` then cleared the arm
*before* testing its age, so the next unrelated `StartLevel` consumed it and
fell back to `randomSeed = -1`, which for a generator level means its stock
layout.

- **Two instances of the same generator no longer come out identical.** The
  reported "two of the same envelope level" was both instances losing their
  baked seed and landing on the same stock layout.
- **A Cat Trap no longer swaps the puzzle underneath you.** The trap's restart
  was one of the launches eating the arm, so the reset rebuilt the level from
  the stock layout instead of the one being played. Not reported; the log
  caught it.
- **A generator level can no longer load empty.** The same early return skipped
  `forceReload`, which produces a flat single-colour screen with no way out.

The frame-age heuristic is gone. `Track.ResolveSlotFor` matches on the level
index `StartLevel` was actually handed, which is a fact rather than a guess.

### Two background items became one: Background Change Trap

droha: "I think we can change the name of the menu background item to just
Background Change Trap (as there's sometimes it can hide items which is still
funny)", and then "and it should change the level select menu background as
well".

- **`Level Background` and `Menu Background` are now one item, `Background
  Change Trap`.** They were two items with two counters recolouring two screens
  from the same palette, so the pause screen routinely sat several colours
  behind the puzzle in front of it. One item, one counter, one colour
  everywhere.
- **It now recolours the level select too**, which neither of the old items
  did. The track's section colours are ROTATED by how many traps you hold
  rather than flattened to one - sections exist to be told apart, and a level
  select painted all one colour would cost more than the trap gains.
- **A trap that lands while you are standing in the level select repaints it
  immediately.** `Track.RepaintSections` re-runs `SetupSections` rather than
  the full `Rebuild`, which would re-lay-out the track and throw away your
  scroll position for the sake of a colour.
- Every colour is still a pure function of the received count, never stepped on
  arrival, so Archipelago's replay of the whole item list on each connect lands
  exactly where the player already was.

**This changes item ids: seeds generated before this build will not match a mod
built after it.**

### Level select

- **Clicking a locked card no longer soft-locks the game.** The locked-slot and
  locked-credits refusals sat on `DoStartLevel`, which runs after
  `OnPointerClick` has already selected the card and begun the transition -
  refusing there left the player in a transition to nothing. Both refusals moved
  up to the click, where the divider refusal already lived, and a locked card
  now explains itself with a toast instead of doing nothing.
- **A locked instance of an unlocked level looks locked.** The game's own
  line-art "locked" look is driven by `LevelInterface.IsUnlocked`, which is one
  row per level id with no per-slot scoping - so unlocking one instance turned
  every card for that level to full colour. The save cannot express the
  difference, so it is drawn: locked cards are veiled.
- **Repeated levels are numbered from #1.** They were numbered from #2, so the
  first card of a pair read as an unnumbered duplicate. Cosmetic only - the
  Archipelago location names still leave the first instance unnumbered, because
  location ids are positional and renaming them would invalidate every seed in
  flight.
- **Pack dividers no longer run out.** The game has five chapter cards and a
  default run wants fifteen, so packs 5 to 14 - fifty-eight cards - ran together
  as one unbroken block. The cards now cycle; section headings were already
  titled per pack and stay correct.
- **The badge repaint cache is keyed by slot, not track position.** Positions
  shift when a pack inserts a divider.
- **New: the level select says whether the run is connected**, with the same
  four states as the main menu.
- **New: the overview strip along the bottom is colour-coded** by card state.

### Daily Tidy

- **Finishing a daily-pool puzzle keeps you in the run.** Six of the levels a
  seed can draw are the game's daily generators, and at the default weighting
  they are the most common cards in a run. Vanilla routes their completion
  straight to the Daily page, and the existing redirects patch post-level
  BUTTONS - which are never shown for these, so there was nothing to intercept.
- **A run no longer writes to your real daily progress.** The same routing ran
  the game's return-from-daily sequence, advancing the genuine completion count
  and streak and firing the badge prompt. The save redirect scopes level data
  but not the profile's daily counters, so a run had been quietly crediting
  dailies that were never played. This also removes the badge popups.

### Logic

- **Drawer contents now require the drawer.** `Tool Drawer`, `Bathroom Drawer`,
  `Paper Plane Supplies` and `Workbench` recorded no dependency between their
  contents and the container, so logic said the 47 tools in Tool Drawer needed
  no items at all while the game kept the drawer shut until `Furniture` arrived.
  Eight one-way `dependsOn` edges added. **This changes generated seeds.**
- **Two instances of one generator cannot draw the same seed.** Vanishingly
  unlikely rather than observed, but it is now a guarantee.
- **Arranging a set now requires having assembled it.** `Candy Canes` has five
  jigsaw pairs and an `Ordered` group over the five finished canes, and logic
  believed `Ordered` needed only `Ordering` - so a player holding `Ordering`
  and not `Jigsaw` got a card advertising available work and a level with
  nothing on screen to touch. The canes do not exist until they are matched.
  Same shape on `Paper Plane Supplies`, whose seven chalks are ordered after
  seven jigsaws, and on `GoodTidings_Cookies (Jigsaw)`, which needs `Jigsaw`
  on both sides and so changes no requirement. Sixteen `dependsOn` edges added.
  **This changes generated seeds.**

  Found structurally rather than by luck: the arranging group's object count
  equals the number of assembling groups, because its objects are their
  outputs. That query now has only these three hits across all 111 levels.
- **Four levels no longer hide an ability they need.** `TupperwareNesting`,
  `Record Player`, `Radial Dance Party` and `MedicineCabinet` reveal
  controllers only as the player solves the previous group, so the sweep that
  built the table never saw their later phases - and logic believed the levels
  were finishable without `Grids`, `Gadgets`, `Rotating` and `Furniture`
  respectively. Progression could be placed behind a puzzle the player could
  not finish. Found when droha solved five groups on `TupperwareNesting` and
  received nothing for them. **This changes generated seeds** - it changes
  logic only, so no location moved and no id shifted.

### TupperwareTower had two locations that could never be earned

droha finished it, got the in-game star, and the card stayed green. Only the
`Tower` check had fired; `Foundation` and `Falling Blocks` had not - and both
were unlocked and in active use at the time, since droha holds Grids and was
dragging the falling blocks onto the tower.

They are the tower's MECHANISM, not objectives: the base it sits on, and the
queue of blocks you place. Neither raises a solved event, so both were dead
locations - the card could never go gold, and fill could have put progression
on one. Corroborating: in the only two other levels using `StackableGrid` it is
the SOLE controller, where it plainly is the puzzle. And droha's own read -
"as far as I know there's only 1 solution so i don't think there's really mini
solutions for this level" - matches the table, which records one solution and
no declared phases.

Both removed. **Location ids shift**; 438 to 435.

`Grids` is still required and is now recorded as an `extraAbility`. The
cross-check test caught that removal dropping it, which would have been the
worse bug: the falling blocks are dimmed without Grids, and a tower cannot be
built out of blocks you cannot pick up.

### An ability lock cannot gate a group solved with someone else's objects

Worth writing down as a limit rather than a defect. The lock works by dimming
the objects a controller manages. On TupperwareTower the `Tower` group needs
Stacking, droha does not have Stacking, and the Tower check fired anyway -
because the tower is built by dragging `Falling Blocks` objects, which belong
to a different, unlocked group. Only two objects are shared between the two, so
this is not the shared-object rule letting go; it is that dimming a group's own
objects says nothing about whether its solution can be reached through another
group's.

No fix attempted. It leaks a check in the player's favour rather than stranding
one, which is the safe direction, and any real fix would mean gating on
something other than object interactivity.

### Three more levels where a locked group blocked a free one

The SomethingEggstra Fridge turned out not to be a one-off. droha hit a second
one - "Fridge Inside has half green/half red. some of the items are greyed out
and can't move and i think you need all of them to do the level" - so rather
than wait for the rest to bite, the whole watch list was measured with the same
spatial test: does the gated group's objects sit INSIDE the free group's field?

| Level | Gated among the free ones | Verdict |
|---|---|---|
| Fridge Inside | 2 tupperware in 4 shelf items | **blocked** |
| Breadtags | 9 crumbs over 9 tags | **blocked** |
| MerryMess_Crackers | 5 crackers in the train | **blocked** |
| Cleaning Supplies | none | separated, fine |
| Desktop Computer | none | separated, fine |
| Record Player | no free/gated mix | fine |

Three `dependsOn` edges added. The free group on each now requires what the
locked one needs, so it cannot be handed out as the only reachable check.

Worth noting the shape has now produced **four** real cases out of ten
candidates, so "shares the shape" is a much stronger signal than it looked when
the Fridge was a single data point.

### One-off prompts came back on every new seed

droha: "shouldn't that be gone once i select the option? or since my main save
doesn't have them chosen it still pops those up?" - the second guess, exactly.

The game stores "you have seen the colour assist prompt" in the SAVE, and a run
gets its own save file, so each new seed looked like a fresh install.
`SaveRedirect` already copied the flags forward from the campaign save, but
droha's campaign save has all five set false, so there was nothing to copy and
the prompts returned on every seed - and this project generates a lot of seeds.

New `PromptMemory`: the mod keeps its own note of which prompts have been
answered in ANY run, and unions it into each new run save. Not by writing to
the campaign save - a run must never open that for writing, and "these flags
are harmless" is exactly the argument that would erode the guarantee the save
redirect exists to provide.

### Clicking a card launched the wrong layout

droha: "when I click Play it opens up a different Calendar level than when I go
to level select and click the first level." Play was right; **every card click
was wrong**.

`BeforeStartLevel` applies the slot's baked generator seed, and it needs to know
which level is starting. The arrow route passes a real index. The click route
passes 0, so the prefix fell back to the active level interface - on the
reasoning, written in the comment, that "the card selection already made the
level active". It does not: opening the level select tears the previous level
down, so by the time a card click reaches `StartLevel` the active interface is
NULL. `target` came out -1, the prefix returned before applying anything, and
the level built itself from no seed.

Measured either side. Before: Play gave `StartLevel(index=12, forceReload=True,
seed=715837904)` and a 12-object Calendar; the card gave `StartLevel(index=0,
forceReload=False, seed=-1)` and a 13-object one. After: both give
`seed=715837904, forceReload=True` and byte-identical layouts.

The click prefix already knew exactly which card was clicked, so the target now
comes from that arm - trusted only while it is fresh, the same staleness rule
that governs slot resolution - falling back to the active interface as before.

Why it survived: it is invisible on a hand-made level, which is most of them,
and the release harness drives the next-level arrow rather than card clicks, so
the one route that passes a real index is the one under test.

- **New: `launchtrace` in DevTools.** Logs every call into `StartLevel`,
  `SetActiveLevel` and `RestartLevel` with its arguments. This bug was two
  guesses deep until the trace showed `index=0, seed=-1` in plain text.
- **A Harmony class nobody registers is silently never applied.** DevTools
  calls `PatchAll(Type)` per class. `RegistrationLog` was written, shipped and
  reported "zero registrations across 111 levels" - read at the time as
  evidence about the game, when it was really evidence the patch did not exist
  at runtime. That conclusion is retracted in the verification log. Both
  tracer classes are registered now.

### An egg hunt logic could not see

`SomethingEggstra Fridge` hides six eggs among the 24 items on its shelves, and
the puzzle is to find them and get them into the carton. Logic recorded its
three groups as independent, so `Standard Objects` - a plain Draggables group -
looked free. It is not: the shelf cannot be made tidy while six eggs are
sitting in it, and the eggs need `Containers`.

droha hit this with `Containers` unheld and found the run down to that single
reachable check, with the eggs greyed out and the hint showing a solution made
entirely of moves they could not make. **Effectively a softlock.**

`StandardObjects` now depends on `EggsContainable`. Measured either side: three
eggs sit up among the shelf items and three down by the carton, which is the
hunt the hint describes.

Two things this did NOT come from, and both are worth recording. The game
declares no dependency here - `dependsOn` is empty and `dependenciesPlacedFirst`
is 0 on every object - so the authored data that settled the phase audit is
silent on this one. And the prefab survey shows nothing either. The dependency
is implicit in what "tidy" means for the level, and only playing it reveals
that.

Nine other levels share the shape - a baseline Draggables group beside a gated
one, with no recorded dependency - and four are now cleared: `Books 3` and
`TrickOrTidy_ChocolateBars` by construction (their groups share every object),
`MedicineCabinet` and `Mirror` by play. The remaining six are listed under
Known.

Which levels a human has actually played is now recorded in
`tools/tested-levels.txt` rather than inferred, and the playtest seed generator
reads it to bias new seeds towards untouched puzzle types.

### The main campaign was unreachable

**57 of the game's 69 campaign puzzles could never appear in a seed.** Not a
bug in the usual sense - a design decision with a consequence nobody had
counted. `slots.py` fills slots by source, `source_weights` held only
`generator` and `archive`, so a campaign level could enter only through the
mechanic-coverage reserve, which opens for exactly four abilities. Twelve
levels qualified. The rest were dead content.

It surfaced trying to playtest Radial Dance Party after the phase audit took it
from 1 location to 11: eighteen rolls could not place it, and no amount of
weighting the seed picker could have helped.

- **New `base_weight` option**, and the default split becomes **80 generator /
  10 archive / 10 base**. **Every generated seed changes.** Setting it to 0
  reproduces the old behaviour exactly.
- Measured over 5 seeds at 79 slots: **60.2 generator / 10.0 archive / 8.8
  base**, of which 4.8 are ordinary campaign puzzles that previously could not
  appear at all, and 28 distinct campaign levels across the five seeds against
  7 before. Generator draws fall from 66.6 to 60.2, so a long run leans less on
  repeating the same generator.
- Campaign levels are drawn uniformly. Weighting towards the 45 that teach a
  mechanic was considered and dropped: at a 10% share it is not worth the
  special case, and the naive version of it would have been an exclusion rather
  than a preference.
- The old design is recorded rather than deleted, including which half of the
  original objection to a base weight was a real property and which half was a
  bug that has since been fixed.

Two latent problems that only mattered once campaign levels became common:

- **The pause-menu route home could steal a check.** `Navigation.CampaignLevel`
  cached the first campaign level it found - index 1, Cat Frame - and never
  released it. It is handed to `GoToLevelSelectForLevel` on every pause-menu
  Levels press, so once that level is also one of the run's cards, a later
  seedless `StartLevel` could resolve to its slot and file a check against a
  puzzle nobody opened. It now prefers a level the run does not contain, and
  the cache is cleared when a run ends.
- **The cat sound was decided once per session.** The search latched on the
  first attempt whatever the outcome, so a trap sprung on a level with no cat
  settled it permanently. **11 of the 13 levels carrying a cat are campaign
  levels**, which is exactly why nobody had noticed. It now re-searches while
  the clip is still null.

A third suspected problem turned out not to be one, and is recorded because
the reasoning nearly produced a change. Vanilla creates its own completion row
whenever a level is beaten - "finish N, create N+1" - and `ApplyUnlocks` skips
rows it did not create, so those keep `unlockedOnLevelSelect` false. That
looked like the replayed-unlock-animation bug returning once campaign levels
became common. Measured instead of assumed: reading the save either side of
opening the level select shows the flags unchanged, so nothing clears or
re-sets them, and a replay needs a re-set. A vanilla row also cannot make a
locked card playable - `IsRefused` gates on the run's pack state and never
reads the save. No change made.

`Track.LaunchSlot` is deleted. It had no callers and passed `forceReload:
false`, which a generator slot survives - `BeforeStartLevel` upgrades it - and
a campaign slot does not, because it takes the `Seed < 0` early return first.

### Every phase now pays

The audit proved eleven groups real that mint no location; they are restored,
and the table has nothing left that the game says exists.

- **Radial Dance Party: 1 location -> 11.** All ten declared dances, in the
  order `RadialDanceParty` declares them, each depending on the one before.
  This level had contributed a single check for a ten-ring puzzle since the
  beginning. Its `extraAbilities` override for `Rotating` is gone - the
  `RadialDance` controllers supply it themselves now.
- **TupperwareNesting gains `Food`,** the sixth and final phase.
- **`BespokeLevels` is empty.** Radial Dance Party was excused from "every
  level registers a controller" on the strength of a count the sweep could not
  take correctly. There is no longer any level that needs the exception.

**This changes location ids and invalidates every earlier seed.** 427 locations
to 438. 0.3.1 is unreleased, so it rides with it.

### Books (Randomized) is seed-varying, and the code says why

Recorded here because the first answer came from the wrong place. The absence
of this controller in some seeds was noticed by generating eight of them and
counting - which is how you CHECK a rule, not how you find one.

`Books_LevelRandomizer` holds it in a field called
`DraggablesForSymmetricSolutions`, and the level draws from a seven-value
solution enum of which two, `HEIGHT_SYMMETRIC` and `WIDTH_SYMMETRIC`, are
symmetric. The controller appears exactly when a symmetric solution is rolled.
A location there would be unearnable in any seed that rolls none, so it stays
out - now for a reason rather than a frequency.

Every other generator's controller fields are unconditional, and all of them
are already recorded, so this is the only level of its kind.

### The phase and dependency audit

Three playtests in a row hit the same class of bug - a card offering work the
level would not give - and each was patched by hand from the report. droha
called it: the guessing had to stop. It turns out the game DECLARES all of it,
and nothing here had ever read the declarations.

- **`PhasedLevel.phases`, `TupperwareNesting.GetPhaseControllers()` and
  `RadialDanceParty.dances` are authored, ordered lists** naming exactly which
  controllers a level reveals and in what order. The sweep now records them,
  along with `levelType` (the game's own class for the level) and each
  drawer's `UnlockOnSolvedControllers`. Three levels in the entire game declare
  phases: `PawPrints` (3), `TupperwareNesting` (6) and `Radial Dance Party`
  (10). That is the complete answer to "which puzzles have mini solutions",
  measured rather than inferred.
- **The TupperwareNesting phase order was wrong, in both directions.** It had
  been guessed as "every later group depends on `Lids` and `Stack 1`", the two
  that register at boot. The game declares a CHAIN that does not involve `Lids`
  at all: `Stack 1 -> Stack 2 -> Tray -> Stack 3 -> Layout (Grid) -> Food`. The
  guess invented a `Containers` requirement on three groups and pushed a fourth
  to needing three abilities. Corrected, no group needs three again.
- **`Food` is not a ghost.** It was written off as prefab dead weight because it
  never appeared in play; it is the sixth and final phase. The run never got
  that far.
- **`Radial Dance Party`'s ten rings are real.** The level registers nothing at
  boot, which had been read as "no puzzle content"; it declares ten phases,
  `Radial Pencils 0` through `Radial Chess 9`. It currently mints one location
  for all ten.
- **New: `docs/data/controller-classes.tsv` and `tools/classify-controllers.py`.**
  Every one of the 223 controllers classified as always-on, phase-revealed,
  phase-driver, mutual, gated, seed-varying, ghost or non-puzzle, with the
  evidence for each. Only **three** true ghosts survive the audit.

### Shared objects were being locked by the wrong controller

- **An object owned by two controllers is no longer locked by whichever ran
  last.** The ability pass walked controllers in order and wrote each one's
  objects, so on a level where two groups move the SAME pieces, a locked group
  silently re-locked everything an unlocked one had just released - leaving a
  level with nothing to touch while its card honestly reported work available.
  droha hit this on `Coins 1 (Shape)`, whose `Ordered` and `Stacked` groups
  share all six coins.

  An object is now locked only if EVERY controller that owns it is locked.
  **Five levels are affected**: `Coins 1 (Shape)` (6 shared), `Spoons` (7),
  `Books 3` (17), `TrickOrTidy_ChocolateBars` (9) and `Workbench` (21) - found
  by recording managed-object instance ids in the sweep, not by guessing.
  Verified in game: with Swapping locked, Books 3's seventeen books stay in
  full colour and remain movable for the height ordering that is unlocked.

  The reported object count in the log is now DISTINCT objects rather than
  controller-object pairs, so it reads lower on exactly these levels.

- **A dependency chain no longer breaks at a non-puzzle controller.**
  `ControllerGroups` skipped both the abilities and the onward edges of any
  dependency target it had filtered out, so a chain through a `Pannables` would
  silently lose everything past it - and lose it in the direction that makes a
  seed unfinishable. No level has that shape today; the trap is closed anyway.

### TupperwareNesting pays for every phase, not just the first

The level reveals its groups as you solve them, so the boot-time sweep that
built the table saw two of them and recorded a three-location puzzle. droha
solved five more groups and got nothing for any of them.

- **Five phased groups restored, 3 locations to 7.** `Stack 2`, `Stack 3`,
  `Tray`, and the merged `Layout (Grid)` / `Draggables (Large Square)` pair now
  mint their own checks. **This changes location ids and invalidates older
  seeds.**
- **Evidence, not inference.** These five are exactly the ones the mod's
  runtime audit watched register during the playtest. The prefab lists two
  more - `Food` and `Nested Tupperware` - which never appeared even through
  every phase of real play, so they are treated as prefab ghosts and left out.
  A location behind a controller that never registers can never be checked,
  and fill will happily put progression on it.
- **The phase order is recorded as dependencies.** Each restored group depends
  on `Lids` and `Stack 1`, the two the level offers at the start, so logic
  cannot believe a later phase is reachable before the first one is. That makes
  the grid group the first in the game to need three abilities, which is why
  the narrow-requirement bound moved from two to three and the fill sweep was
  re-measured rather than the check relaxed.
- **`extraAbilities` dropped from this level.** `Grids` was recorded there
  because no controller revealed it; `Layout (Grid)` now does, and two sources
  for one fact is how they drift apart.

The other five under-captured levels are unchanged and still listed under
Known. Only this one has runtime evidence, and the boot-time census run on
2026-09-08 confirmed the rest register nothing extra at load - so for them the
question of phased-versus-ghost is still open.

### Solution checks were lost across sessions

droha reported Snow Globes showing all three stars in game while the card said
there was still something to do. There was: two of its three checks had never
been sent.

- **The solution counter no longer restarts every launch.** Which location a
  completion files is decided by an ordinal - the first new arrangement of a
  slot files Solution 1, the second Solution 2 - and that counter lived only in
  memory. Closing the game reset it, so the next arrangement re-filed Solution
  1, a location already collected, and went nowhere. Any level with more than
  one solution lost checks unless every arrangement was found in a single
  sitting. It is now seeded from the game's own save, which records the
  solutionId of each arrangement found.
- **Checks already earned are handed back.** Seeding alone would have frozen
  the loss in place: with every arrangement now recognised, no further check
  could ever be filed for them. Entering a slot now files any solution the
  player has demonstrably earned and not received, subject to the usual
  reachability rule, so a run that already lost checks repairs itself.
  Confirmed on droha's run: Snow Globes handed back Solutions 2 and 3.
- **Archive levels are read from the right list.** The save keeps campaign and
  archive progress apart and 26 of the 111 levels in the pool are archive
  levels. The first version of this fix read only the campaign list, found
  nothing for Snow Globes, and silently did nothing - which looked exactly like
  the fix working.

### The Daily Tidy page, for the third time

droha finished the Spider Web puzzle, pressed the next arrow, and landed on the
Daily Tidy page. Nothing appeared in the log, because none of the navigation the
mod had patched was involved.

- **A third of the pool are daily levels, not six.** `DailyGuard` was written
  believing six levels could do this - the "(Randomized)" generators. The real
  figure is **36 of 111**: 16 in the everyday rotation, including ordinary
  looking puzzles like Spider Web, Buttons, Shells and Telescope, plus 20
  seasonal holiday levels. Sizing the guard for six is why this took three
  attempts to fix. The correct number was already written in
  `docs/content-report.md`; nothing connected it to the code.
- **No level is a daily while a run owns the game.** `LevelInterface.IsDailyTidy`
  and `IsHolidayDaily` now answer false during a run, which stops the routing
  happening rather than undoing it afterwards. Read-only, and vanilla behaviour
  returns the moment the run ends.
- **New: a watchdog on the state itself.** If the game reaches the Daily page by
  any route at all, being there is the trigger: the run opens its own next
  puzzle instead. A guard that watches the destination cannot be defeated by a
  route nobody found yet, which is what "never go to this page" actually needs.
  It leaves through the game's own state transition and gives up loudly after
  three attempts rather than retrying.
- **New: the daily pool is data.** `isDailyTidy` and `isHolidayDaily` are
  recorded per level in `levels.json` and pinned by `DailyPoolTests`, so the
  count cannot drift from the game again without failing the build.

### Presentation

- **The level select no longer pops its Archipelago furniture in late.** The
  connected tag polls every half second and the overview dots every second, on
  free-running timers that knew nothing about the menu opening - so the badges
  could arrive up to a second after the screen had settled. Building the menu
  now marks those polls due immediately. Polling stays: there is no single
  reliable event for every route into that menu, which is why the track rebuild
  is on a timer too. What was wrong was letting a poll that exists to CATCH
  later changes also decide when the first paint happens.

### Robustness and diagnostics

- **One failing tick step no longer silences the ones below it.** The per-frame
  update ran nineteen bare calls with no `try` anywhere, and `Toasts.Tick` is
  tenth - so a fault above it stopped toasts appearing with nothing in the log.
  Each step is isolated and names itself once if it throws. This is the most
  likely cause of "the toast message doesn't always pop up".
- **The connection pane puts the game's modal back.** It borrows the shared
  singleton modal and replaced its confirm button's whole click event, destroyed
  its localiser and overwrote its caption, restoring none of it - so after one
  visit to the pane, every other dialog in the game had a Confirm button that
  ran an Archipelago connect. Everything is now recorded and undone on close.
- **The pane binds to the live input fields.** A leftover copy from an earlier
  open could be bound instead, which draws but refuses focus - the reported
  "sometimes can't click the text box".
- **A re-firing controller no longer floods the log.** 723 of 736
  "no location for it" lines in the playtest were two controllers re-raising
  their solved event every frame, burying the eleven that were real.
- **New: unearnable locations are reported.** The controller audit only checked
  for controllers missing from the table; a location in the table with no
  controller behind it can never be earned and pins a card for the whole run.
- **New: a probe on the pause menu Exit button.** Two candidate causes, and they
  are separable from one log line - see `Navigation.BeforeExitGame`.
- **The controller audit looks more than once.** It latched after its first
  successful pass, so on a level that reveals its pieces phase by phase it
  looked before anything had been revealed and then never again. It now
  re-audits whenever the level has grown, and reports per controller rather
  than per level so a second batch is not hidden by the first.

### Testing

- **The campaign-save check no longer fails once a day.** It hashed the whole
  file, so it caught the game rolling its daily calendar forward at launch -
  which happens before a session exists and which vanilla does anyway - and
  reported a clean run as a save leak. It now compares campaign progress with
  the timestamp and daily calendar excluded, and separately pins the daily
  completion count, which IS ours to protect. Four negative controls.
- **The narrow-requirement invariant pins a set, not a bound.** Four groups now
  legitimately need two abilities; the test lists them and still fails on a
  fifth, or on one going missing.
- **New: `tools/check-game-facts.py`.** Holds `levels.json` up against a fresh
  DevTools dump and fails on any disagreement. This project has repeatedly
  shipped a confidently wrong count in a comment - how many levels are dailies,
  how many are randomizable, how many carry hints - and prose cannot fail a
  build. The script also records which of the game's own numbers are unstable
  (they answer "what is true today", not "what is true of this level") and
  insists the dump be taken with the mod OFF, because the daily guard changes
  the answers it reports.
- **New: the shipped table is cross-checked against the prefab survey.** The
  two files answer different questions - what registered at boot, versus what
  the level was authored to contain - so this pins the six levels where they
  disagree, controller by controller, rather than demanding they match. A new
  disagreement fails the build and has to be explained as a phased controller
  or a prefab ghost. It also asserts the thing that actually bites: no level
  may need an ability it does not declare.
- **A wrong controller table now fails the e2e.** `CONTROLLER MISMATCH` and
  `UNEARNABLE LOCATIONS` were warnings and the census counts only errors, so a
  run could pass with the table wrong - which is how six under-captured levels
  survived every release gate so far. The six known ones are allowed by name;
  any other fails the run.

### Known

- **Six levels may hide the same trap as the Fridge.** `Breadtags`,
  `Cleaning Supplies`, `Desktop Computer`, `Fridge Inside`, `Record Player` and
  `MerryMess_Crackers` each pair a no-ability Draggables group with a gated one
  and record no dependency between them. That is only a shape, not a defect -
  most such pairs are genuinely independent. Telling them apart needs the
  spatial check that settled the Fridge: boot the level and see whether the
  gated group's objects sit among the free group's.

  Two of the ten that share the shape are cleared by construction: `Books 3`
  and `TrickOrTidy_ChocolateBars` have groups that share every object, so the
  free arrangement is independent. Two more are cleared by play:
  `MedicineCabinet` was hand-solved during Phase 0, and **`Mirror` is the
  useful one** - droha earned all five of its no-ability part checks while its
  Containers, Gadgets and Stacking groups were locked, which is direct proof
  the free groups do not wait on the gated ones.
- **Nothing is missing any more.** The eleven the audit proved real are
  restored above, and `tools/classify-controllers.py` reports zero
  phase-revealed controllers absent from the table.

  The audit closed the question the other way for four controllers.
  `Desktop Computer/Hourglass`, `MedicineCabinet/Cupboard` and
  `Record Player/RecordPlayer` are **ghosts**: in the prefab, named in no
  level's phase declaration, never registering. `Books (Randomized)/Draggables`
  is **seed-varying** - present in 6 of 8 sampled seeds - which is a category
  of its own and is waiting on a wider generator sweep before any policy is
  set. None of the four should become a location.
- **Backgrounds** now step past a palette colour the pieces would vanish into,
  by measuring contrast against the renderers on screen. The threshold is a
  first estimate and may want adjusting.

## 0.3.0

The first release. Everything below is what "it works" currently means.

### The run

- **The seed replaces the campaign.** The level select shows the run's puzzles
  in the order the generator planned, and the campaign save is never opened
  while a session is active - the game is pointed at `save_ap_<slot>_<seed>`
  instead. Isolation by construction, so a crash or an alt-F4 cannot leak a
  randomized run into the player's own progress.
- **Progressive Puzzle Packs** reveal puzzles in widening groups. The ramp is
  decided at generation and sent in slot data rather than recomputed in the
  game, so the two cannot disagree about what is reachable.
- **Ability locks.** Objects for a mechanic you have not unlocked are dimmed
  and immovable, so a puzzle can be partly solved and returned to.
- **Checks** are the distinct solutions of each puzzle, plus a controller-group
  check where a level has separable parts.
- **The goal** is beating a configurable number of puzzles, which unlocks the
  credits card.

### Items beyond progression

- **Skips** clear a puzzle you are stuck on. A skipped puzzle does not count
  towards the goal.
- **Hint Pages** unlock a page of the in-game notepad. Without one the notepad
  still opens and the hint is visible as a scribble - you just cannot erase it.
  How many exist is a percentage of the pages the seed actually drew.
- **Cat Traps** knock your work over. They cost time, never progress, and the
  number already sprung is persisted so a reconnect does not fire them again.
- **Level and Menu Backgrounds** recolour the game, one shade per item.

### Connection

- **Offline play.** If no server answers at launch, the run resumes from a
  cache of the slot data and the received items rather than dropping to the
  vanilla game. Checks earned offline are queued and sent on the next
  connection. A seed regenerated under the same slot name gets its own save
  and replaces the cache, so a stale cache cannot write into the wrong run.
- **A mid-session drop keeps playing.** Progress is kept and pushed, never
  reverted.
- **Retries are unlimited** by default, backing off 5, 10, 20, 40 then 60
  seconds, and stop only on Cancel or Disconnect - matching Archipelago's own
  reference client. A refused login (wrong slot name or password) is never
  retried, because retrying cannot fix it.
- **CONNECT / CANCEL / DISCONNECT** in the in-game pane, three states for three
  situations. A press during an attempt supersedes it rather than being
  ignored.

### Known gaps

- A level that once loaded empty and has never reproduced. A watchdog logs
  `LEVEL LOADED EMPTY` if it happens again.
- The Play button's behaviour partway through a run - see the verification log.
- Leaving an offline run for the vanilla campaign means turning `AutoConnect`
  off in the Archipelago dialog and relaunching. There is no in-session route.
- DLC2 *Seeing Stars* is not supported; it was not owned when the content was
  surveyed.
