# Changelog

Every release ships three files that carry the same version number and are
meant to be used together: the mod zip, `alttl.apworld`, and the player yaml.
A mod and an apworld that disagree about the version disagree about the item
table, so the version is checked by `tools/check-version.py`, in CI, and again
before a release will build. Since 0.3.3 the mod also checks it at runtime and
refuses to connect to a seed a different apworld generated.

The format is loosely [Keep a Changelog](https://keepachangelog.com/).

## Unreleased

- **The Thunderstore package carries only this release's changelog section**, with a link to
  this file. Thunderstore refused the whole file ("CHANGELOG.md is too long, max: 100000";
  it is over 200000). `package-release.py` builds it that way and refuses a README or
  changelog over the limit. The 1.0.0 package was rebuilt so, from the published 1.0.0 DLLs.
- **The docs link the mod's Thunderstore page**
  (thunderstore.io/c/a-little-to-the-left/p/drohack/A_Little_to_the_Left_Archipelago/):
  the README, the setup guide and the installation page.

## 1.0.0 - 2026-10-02

The first release on Thunderstore.

**New seeds only.** Location ids moved: DLC1 107 -> 105, DLC2 124 -> 123.
0.4.6 seeds: finish them on 0.4.6, or regenerate.

- **A Skip no longer leaves the post-level panel over the next puzzle.**
  - On a level the game sends straight on with solutions left (Cupcakes,
    Water Glasses, Fountain Pens), the game's skip moves the level out by
    itself, and the panel the mod asks every run puzzle for came up on top:
    the next puzzle opened under it, in `RetryUI_GameState`. DevTools trace:
    SkipLevel, TransitionLevelOut, RetryUI, ShowMenu, the tween-out.
  - Found by the 1.0.0 DLC gate, which stopped on it. It was in every DLC
    gate since 2026-09-30 and in droha's morning log; the harness used to
    get out by a route no player has.
  - A Skip on such a level now keeps the game's own route (Core
    `AfterPuzzleRoute`, `Skips.FinishingTheLevel`): the next puzzle opens
    once, in play. A level built for the panel moves on through it as before.
  - The gate checks it: "no puzzle opened under the post-level panel" (full
    30, quick 28).

- **On Thunderstore.** The mod installs with r2modman or Thunderstore Mod
  Manager, BepInEx with it. The README now opens with how to install, in the
  order of Archipelago's own setup guides (required software, a mod manager
  first, then by hand, then joining), and the setup guide, installation page
  and package README carry the mod-manager steps.

From a sweep of the backlog (2026-10-01).

- **A finished generator no longer relaunches itself.**
  - The cause: the game asks for the level already running (`StartLevel`
    with no index and no reload) just after a puzzle finishes, and again as
    it goes back to gameplay when a level select closes over the level. The
    game does nothing with that call.
  - The mod's launch prefix read the call as a card click, took the running
    level, and made it that slot's seeded reload. So:
    - a finished generator came back about a second after "navigation:
      next": 3 to 5 times in every gate since 2026-09-27, and after DLC1
      Trophy Cabinet and DLC2 Water Glasses on the DLC route;
    - a seeded level started over when a player left it through the level
      select and closed back to it. Breadtags did this mid-solve in the
      quick gate.
  - The call is now left as the game asked (Core `LaunchRequest`; the log
    says `track: the game asked for the running level ... left as asked`).
  - The quick gate's seed, before the fix: 6 relaunches and 1 mid-solve
    refund. After it: none of them, 26/26.
- **The Seeing Stars Boss files each phase as it is solved.**
  - Its Lock and Compass endings reach the save as those phases are solved,
    with no completion of their own, so until now they were filed only at
    the finish.
  - The mod now reads the save again on the frame after the game records a
    solution (`SaveData.SaveLevelData`, `SaveSystem.SaveGame`). Once a
    completion has run on the slot, that completion keeps its own filing.
  - Checked with DevTools `marksolved:1231:DLC2Boss_Lock` and `..._Compass`
    on the Boss's run slot: each was filed at once, with no completion.
  - droha's playthrough (2026-10-01): Compass and Lock were each filed
    about 30 s after their phase was solved, when the game saved it, and
    Knife at the finish.
- **Seed Pods and Clover reload when Symmetry arrives mid-level**
  (`ObjectLock.ResetOnUnlockLevels`), as Cat Food Cans and the stacked levels
  do.
  - The game tints their pieces to 0.9 once, after the intro, and only the
    ones not locked then. Unlocked in place, every piece came back at 1.0,
    brighter than it should be.
  - The reload replays the intro. Every piece in both levels is Symmetry's,
    so it loses nothing.
- **Trophy Cabinet is just its Solution** (DLC1 ids after it move).
  - Its Cupboard Doors were a check of their own, where Clock Cupboard's
    and Tea Cabinet's are not. The doors are the way in (Gadgets), not a
    puzzle: droha's play on 2026-10-02 sent Cupboard Doors, Items
    Placements and Solution within 6 s.
  - The doors are `notALocation`. The level is then one group, so Items
    Placements' part check goes too; Gadgets is still required. DLC1
    locations 107 to 105.
- **Figurines' second sort is a Solution of its own** (DLC2).
  - droha did all three solutions on 2026-10-02 and the hover showed 2 of
    3. The two sorts are one controller's two arrangements
    (`SortingItemsDraggables_0` and `_1`); the ending table had only `_0`,
    plus `Groupables-(Achievement)_0`, an id no game has reported. So `_1`
    fell back to the first ending, Draggables, already in, and filed
    nothing.
  - Endings are now Draggables, Sorting Items 1 and Sorting Items 2. The
    Groupables (Achievement) controller is `notALocation`: never solved in
    play, so as an ending only a Skip could earn it. DLC2 still 124.
  - `fixtures/solution-ids-observed.tsv` re-harvested: Figurines is the
    only row that moved, and Core's every-observed-id test fails on the old
    table.
- **Canapes is just its three Solutions** (DLC2). Its Cracker Positions
  check fired before every solution, for the same three crackers each time;
  droha: "it's not really a mini solution". `solutionOnlyParts`; DLC2
  locations 124 to 123.
- **Every different ending files its own check, even where the ending table
  is wrong** (Core `CheckRouter.ForEnding`).
  - An id the table did not know filed the first ending in the list even
    when that one was already in; that is how Figurines lost its star.
  - Now each id found on a slot takes the ending it names if free, else the
    first free unseen one, else the first free one. A wrong guess in the
    table costs the check's name, never the check. The answer still depends
    only on the ids found before it, so it is the same at every launch.
  - Why it matters: a sweep of every level (2026-10-02) found no other
    wrong id, but 11 levels with several controllers list ids no game has
    reported yet (Bookshelf credits all three of its solutions to its top
    left shelf). Those are guesses until played; this makes them safe.
  - droha then played the ten (the Boss's Lock and Compass were filed from
    the save on 2026-10-01): all 23 unreported endings came in as the table
    had them, each its own check, so no name moved.
    `fixtures/solution-ids-observed.tsv` holds them now (223 of 244 ids name
    a controller).

- **Robots keeps every robot's state where it is without Ordering.**
  - Its Indexables group (Ordering) shares Robots 2, 3 and 7 with
    IndexedDraggables, which needs nothing. The lock frees a piece any free
    group holds, so those robots could be extended without Ordering.
  - Robot 9's antennas and Robot 1's arm buttons are locked, but they take
    clicks through colliders under their free robot (DevTools `objects:`,
    2026-10-01; droha: "not just the antenna guy").
  - Now, while Ordering is withheld, the colliders of every button
    (`IndexIncrementTrigger`) on the level's ten states are off, so no press
    reaches them. They come back on with Ordering.
    - A press that gets past anyway is refused and logged as a warning (the
      new `index hold` patch). There were 0 in the hand test.
    - Not a refused press, which two earlier builds made. A press refused
      mid-handling, whether its state change or the whole handler, left
      Robot 3's hearts container switched off once Ordering came, so no
      heart would go in. With no press while held they went in (droha's
      A/B).
    - Not the game's own `LockIndex`, which the first build set. Robot 2's
      key clears that when it comes out, and the head then moved.
  - The big robot's chest-and-arms layer, which carries its heart, is grey
    while its state is held (Core `ObjectLock.IndexHoldArt`). Its body stays
    lit.
  - The robots still drag: their group needs no ability.
  - droha's hand test, 2026-10-01:
    - Ordering withheld: every robot dragged, and none changed state,
      including the key robot with its key in and out.
    - Containers withheld: nothing went into a robot.
    - The big robot "looks perfect".
    - Ordering given back mid-level: every button worked, the hearts went
      in, and the level finished.
  - **Math Set too.** Its half circle and compass are shared with its free
    drag group the same way, and droha extended both without Ordering. Now
    neither changes; the Inside solution, both closed as they start, can
    still be finished, and its check waits for Ordering as the logic says.
  - Robots and Math Set only (Core `ObjectLock.IndexHoldLevels`):
    - Water Glasses and Music Box share no state piece, so the lock already
      held theirs (droha's hand test).
    - Junk Drawer Transforming and Combs have the pairing, but their logic
      asks only for Drawer. Junk Drawer Transforming was finished with
      Ordering locked.

- **No run puzzle goes to the Daily Tidy page any more, and every run level
  ends the same way.** droha: "If we know what levels go there can't we
  just prevent those levels from doing it? ... a set ending sequence for all
  levels so we are in control".
  - Until now every finished generator went to the game's Daily Tidy state,
    and the daily guard pulled the player out to the next slot: 105 times in
    the second player's 0.4.2 playtest, 32 in droha's, every generator in the gates.
  - Where the game decides: `LevelManager.OnLevelCompleteTweenOutComplete`,
    which every way out of a finished level passes through. It runs
    `IsDailyTidy`'s body inline (randomized or holiday-dated, and not an
    archive level), so the daily guard's postfix on the getter never saw
    it. Found from the interop's xref cache and the game's machine code,
    then seen with DevTools `trace:` on Stamps (Randomized): the tween-out,
    `get_IsArchiveLevel`, `StopGame(True)`, the Daily state.
  - The new `DailyDecision` answers that check's one real call,
    `get_IsArchiveLevel`, for the level leaving, inside that one call only,
    and the game takes the branch every other level takes. It logs `daily
    decision: <level> leaves as a run puzzle, not a daily`.
  - Generators now leave by the panel's route like every other level (Core
    `AfterPuzzleRoute`: the panel with solutions left, otherwise on to the
    next slot, or the track when nothing is playable).
  - On the quick gate's seed, 18 fresh runs of Stamps, Batteries and Post-It
    Notes in a row: 54 generator finishes, 0 Daily Tidy states, 0 rescues,
    the next slot opened each time.
  - Every generator, the player's way:
    - a run of only the 16 base generators, each finish moving on by itself:
      16 of 16 moved on with the decision, no Daily page. That includes
      Books, Pencils, Buttons and Spice Jars, which left by the panel's
      Continue with an ending still to find;
    - the 4 DLC generators, each from its card: 4 of 4.
  - droha's hand test, 2026-10-01: two generators in a row each opened the
    next puzzle directly, never a Daily page "not even for a flash"; the
    pause menu's Exit went to the title; Books showed the panel, and its
    arrow went on to the next puzzle. droha had never seen the page in recent
    releases either: it was happening unseen, and only the logs showed it.
  - The daily guard's rescue stays as a backstop and now warns. The gate has
    a new check, "the run never went to the Daily Tidy page" (quick 27,
    full 29).
  - The `ReactToDailyCompleting` postfix is gone. That getter decides
    nothing: the game reads the field directly. Its machine code is also
    shared with 14 other getters (`Level.get_HintUsed` among them), which
    the postfix risked answering false during a run.
  - The gate's arrow check may now start on a generator, since generators
    end the same way.
- **Post-It Notes (Randomized) no longer logs 15 render-texture errors when
  it finishes.**
  - Each note takes a texture from a shared pool about 5 frames after the
    level is built, and switches its line camera on to draw into it. In
    about 1 load in 3 the cameras stay on for good. Then, when the level is
    torn down, Unity logs `Releasing render texture that is set as
    Camera.targetTexture!` once per note.
  - That happened only when the level started straight after another level
    finished, never from a card. Measured with the new DevTools `cameras`,
    `values:` and `trace:` (7 of 7 runs that logged it had the cameras on at
    load, none of the others).
  - The mod now switches any note camera still drawing off as the level is
    torn down or rebuilt (`PostItCameras`). Six runs: it fired in the 3 with
    the cameras on, 0 errors in all 6.
  - Why the cameras stay on: not the notes. In every run with them on, each
    note had finished its snapshot (DevTools `values:NoteVisualSetter`, 2 of
    8 runs on the generator seed; the backup fired in both, 0 errors in all
    8). The game switches the note cameras off in one place,
    `PostItNote_LevelRandomizer.OnLineGenerationComplete`, after a frame
    count rather than when every note is done (read from its machine code),
    and that switch-off sometimes misses them. It is the game's own; a
    player sees nothing, as each note shows a copy of its picture.
- **A generator launched outside the run's slots is built, not empty.** The
  launch prefix gives such a level a stable seed of its own so it is not left
  empty. That seed was the level index times 2654435761 cast to an int,
  which wrapped negative for 6 of the 20 generators (Books, Batteries,
  Post-It Notes, Trim Plant, Microscope, DLC2 Figurines). A negative seed
  reads as "no seed", and Post-It Notes came up empty 9 of 9. Core
  `LaunchRequest.FallbackSeed` keeps it above 0 (the seeds that were positive
  are unchanged); booted outside the run, Books, Microscope and Calendar were
  each built (13, 5 and 24 objects). The log line now names the seed. Found
  by a DevTools boot; no player route to it is known.
- **The run's track is taken down if it is left up over the title** (DlcGuard
  `TickTrackLeftOver`). droha's screenshot from the DLC gate showed the
  cards, the strip and the Close X over the main menu. The route behind it
  was the harness's own (below), but a screen like that should not stay. The
  DLC gate on player routes (2026-10-01 23:10, 29/29): 161 menu listings,
  none with the track over the title, and the takedown never had to fire.
- **The gate moves only the way a player can.** Every visit leaves a level
  by the retry panel's Menu or the pause menu, then Levels, and opens the
  next puzzle by clicking its card on the run's track. Where the mod moves
  on by itself, the gate waits for it.
  - It used to leave by the post-level Level Select, Close and a forced
    title, then `boot:` the next level. It also pressed the post-level
    Level Select on a level the mod had already moved on from, which no
    player can. That built the track twice and left it over the title
    (droha: "you shouldn't be hitting it either. it's sloppy").
  - `test_scheduler.py` pins it: the gate's visit functions use no DevTools
    shortcut, and its next press is read from the open menus.
  - DevTools `pause` now enters `Menu_GameState` and pauses the game, as a
    player's Esc does: the same log lines as droha's Esc (2026-10-01), and
    a Reset from it closes the menu. It used to raise only the menu-open
    event. The state then stayed at Gameplay, so a Reset or a Skip from
    that menu went back to gameplay "already active", nothing took the menu
    down, and its Levels stopped answering - a pause no player can reach.
    The old form is `pause:event`.

- **The DLC navigation probe goes to the track the player's way, and its two
  one-off failures are explained.**
  - "The second session opened no card": the probe reached the track from
    the title with a forced `menu:levels`. The title stayed the menu
    system's active menu under the track (`menu=Title Menu`), and every card
    click selected its level and launched nothing (48 clicks, 0 launches,
    2026-10-02). It now presses the title's or the pause menu's own Levels.
    Three runs since: 30 of 30 checks, every card launched.
  - "retry panel: moving on failed: NullReferenceException": the probe
    pressed the hidden panel's post-level Level Select in the frame between
    the mod hiding the panel and moving on, then the mod's move-on called
    the panel's `NextLevel` over the track it had opened (seen once in those
    three runs). No player can press a hidden panel; the probe now presses
    it only when the panel is on screen. The mod's move-on also stands down
    when the game is already on the track, the DLC level select or the title
    ("left the post-level screen another way first"). Forced 8 times on the
    generator seed, it never threw (7 times the move-on came first; once the
    press came before the panel was hidden, so there was nothing to stand
    down).

### Written off

Tried again, in more than one way, and not seen again; gone from the
backlog.

- **Trim Plant's Grow NRE** (twice, 2026-09-30; `Sprite.get_bounds` in the
  plant's `Grow` coroutine).
  - By the player's own routes, 0 of 50: Esc then Levels, and Esc then
    Reset, 25 each, 0.5 to 3 s after it loaded (2026-10-02).
  - It does reproduce, 4 of 25 (4 of the 5 exits at 0.5 s), but only with a
    pause that keeps time running: DevTools' old event-only `pause`, now
    fixed to enter `Menu_GameState` as Esc does. A player's pause stops
    time, so the grow cannot run on into a level being torn down.
  - Both gate sightings also relaunched Trim Plant mid-grow (the relaunch
    above, now gone).
- **A boot over an unfinished Junk Drawer 2 breaking the DLC1 Boss's first
  solve** (harness only).
  - In game: 0 of 3. One part of Junk Drawer 2 solved, then `boot:1128` and
    a forced solve.
  - Gate logs: 0 in the 13 since.
- **Books (Randomized) finishing on its Draggables rule by itself.**
  - In game: 0 of 5 symmetric layouts left alone for 60 s. Three were booted
    and two booted twice, with no run connected so the layout was the
    seed's.
  - Also 2026-09-28's 100 layouts.
  - Every gate Books finish since ended on `Shuffle_0`.
- **The arrow session's lost Post-It Notes completion** (2026-09-24).
  - In game: three `--only-arrow` sessions passed both checks, each solve
    followed by PartSolved and LevelComplete.
  - Kept arrow logs: 32 of 33 completed. The other sent its solve while the
    game had paused itself on losing focus (the next item).
- **A gate run that stops receiving game events** (2026-09-24).
  - Cause: the game's own `Pause(true)` holds every event. `boot` undoes it,
    and the gate warns of a solve sent while paused.
  - No stop in the 67 gate logs since 2026-09-25.
- **Mirror's candle and Ghost Cat's cats not grey while locked**: correct,
  and droha left them (2026-09-30).
- **Held states drawn into a piece's one picture stay lit** (Robots' key
  robot and Robot 7, Math Set's half circle and compass). Their states do
  not change while Ordering is withheld; each is a single flipbook, so a
  tint greys all of it or none, and they still drag. droha: leave them
  (2026-10-01), no overlays.
- **Steam Cloud conflicts while testing**: the tip is in
  `docs/dev/testing.md`.
- **The DLC gate leaving its paper plan at visit 14** (2026-10-01 14:05).
  The DLC gate of 23:10, on player routes, followed its plan through all 22
  visits, 29/29.
- **A full gate's harness dying after visit 7** (2026-10-01 13:58, its
  traceback lost). The full gate of 22:46, stderr kept, ran to the end:
  29/29, nothing on stderr.

## 0.4.6 - 2026-09-30

- **The gate's lines say setup or gate**: `[setup 2/4 assets]` while it
  installs and seeds, `[gate 1/3 arrow]` and `[gate visit 4/26 | 2/15
  beaten]` while it tests (droha: "it should say it's doing setup then and
  actually doing the gate"). Its error census names the frame behind
  Rewired's handler errors, and lists as known the game's own
  `SkipTooltip.ControllerChanged`, which throws when an input device changes
  between one menu and the next (50 in the 0.4.6 full gate, from input
  reaching its window; DevTools `state` now reports `menu=`, and every route
  to the title leaves one active once it lands). Check 10 also reads Unity's
  Player.log, where a bare `NullReferenceException` keeps its stack, and
  lists as known the one DevTools `boot:` causes over a level a Skip
  finished (`TransitionLevelOut` under `LevelManager.StartLevel`; twice in
  the DLC gate), only when every one in Player.log is that.
- **Shared-piece checks count with either mechanic.** Spoons, Coins 1
  (Shape) and Nanopets each have two groups holding the same pieces, and the
  lock frees a piece when any group using it is unlocked, so either mechanic
  lets a player finish either group; the logic and the mod's held-back check
  asked for the group's own, so the second player's 0.4.5 run had Spoons' Size ending held
  back while holding Stacking (2026-09-30). A requirement can now carry
  `orAbilities`, other sets that meet it too, from a hand-authored `freedBy`
  in levels.json (`rules.or_abilities`); the generator, the mod's held-back
  check and card badges (Core `SlotProgress.IsReachable`) and the gate's
  harness (`release_e2e.need_met`) all read it. droha's hand test finished
  every one of the six groups with its own mechanic revoked, and in a seed
  with the fix the mod filed Spoons' Size holding only Stacking.
  - DevTools `indexables`: what an Indexables group is solved by (Nanopets:
    every pet to its last state).
- **The README says the project is vibe coded with AI**, and shows the two
  strip pictures side by side, retaken in the alphabetical order once the
  level select has finished its opening zoom (`capture-ability-strip.py`
  waits 20 s).
- **The level select's mechanic strip reads alphabetically**, Distributing
  last (Core `Abilities.StripOrder`). It was the item-id order. A seed still
  shows only its own mechanics, so the strip stays compact. Item ids are
  unchanged.
- **A Thunderstore package**, for r2modman and Thunderstore Mod Manager once
  Thunderstore opens the game's community (requested 2026-09-30).
  `tools/package-release.py` also writes
  `<out>/thunderstore/A_Little_to_the_Left_Archipelago-<version>.zip`: the
  same five DLLs, a generated manifest depending on
  BepInExPack_IL2CPP 6.0.755, and `thunderstore/icon.png` and `README.md`.
  It refuses a package Thunderstore would refuse
  (`tools/test_package_release.py`). The icon is the Archipelago ring with
  the game's own cat, cut from its exe icon. The game starts with the
  doorstop arguments r2modman passes: title screen in 15 s, no dialog.
- **Tested on BepInEx be.755**, the build Thunderstore's BepInExPack_IL2CPP
  6.0.755 ships, and the docs now name it. be.697 still works: 0.4.5 as
  released loads on be.755, and a be.755 build loads on be.697, every
  feature live either way. Checked on be.755 with its own
  `BepInEx.cfg`: `tools/probe-trap-window.py` 9 of 9, `tools/probe-dlc.py`,
  and the full gate 27 of 28, the miss being check 10 below.
  - The daily guard looks its three types up in the game's own assembly.
    `AccessTools.TypeByName` scanned every assembly, and be.755's generated
    UnityEngine.CoreModule holds three types that will not load, so every
    start logged a HarmonyX warning.
  - The gate lists as known the `same key has already been added` errors
    its own forced solves on TupperwareNesting cause. They happen on be.697
    too; the pack's `WriteUnityLog = true` is what brought them into
    LogOutput.log.

## 0.4.5 - 2026-09-30

From an audit of 0.3.0 to 0.4.4 against every surviving log (2026-09-29),
and droha's hand test of what it found (2026-09-30).

**New seeds only.** Location ids moved: base 385 -> 383, DLC1 110 -> 107.
0.4.4 seeds: finish them on 0.4.4, or regenerate.

- **Two parts that never fire are no longer checks.** They need no ability,
  so the fill could put early progression on them, which would then only
  arrive with the goal's release. droha played each whole with every
  ability held:
  - Kitchen Hanging Tools 1 completed on Hanging Objects, and "Guiding
    Targets" never reported a solve.
  - Kitchen Hanging Tools 2 completed on Hanging Objects alone, and
    "Draggables For Console", over the same 14 objects, never did. That is
    Workbench's "Draggables For Targets" of 0.4.0 again.
  - Both are `notALocation`. KHT2 is then one group, so its Hanging Objects
    part is its Solution.
- **Bowls is its two Solutions.** Its Crack and Pattern parts each fired in
  the same second as one ending: Pattern with `Match-(Draggables)_0`, Crack
  with `_1`. So each arrangement filed two checks, the Spoons case of 0.4.3,
  which Bowls escaped because its ending ids name neither group. Both are
  `solutionOnlyParts` (droha: fold them).

- **A Cat Trap no longer knocks over a finished puzzle, or the credits.**
  - The trap held itself while a level was transitioning before it asked
    whether the puzzle was finished. So a trap landing as a generator went
    straight on could come out after the 3 s grace and reset the finished
    puzzle, throwing the next slot away: four times in the second player's
    0.4.2 playtest log (Buttons, Trim Plant #2, Stamps (Randomized) #3,
    Clock).
  - A player sitting on the retry panel for more than 3 s was reset too.
  - The credits were reset by the traps the goal's release delivers. That
    happened in the second player's 0.4.3 ending, which then threw on the
    way out, and 12 times in gate logs.
  - Core `TrapTiming` now decides: a finished puzzle misses until something
    launches it again (a restart included), and the credits always miss.
  - Background Change Traps leave the credits' own colours alone, where
    they had fought the credits for the camera every frame.
  - Checked in game with the rebuilt `tools/probe-trap-window.py` (a real
    finish, not DevTools `complete`). The old build reset the puzzle on its
    retry panel and reset the credits. This one: 5 of 5 cases miss or
    spring as they should, and 1 credits repaint where there were 52.
- **A puzzle with its own cat sends that cat.** On Stamps, Shells, Place
  Setting and MerryMess_Crackers, which ship a cat that takes things
  (`CatGrab`), a Cat Trap now calls the game's cat: its paw reaches in and
  takes a piece (Place Setting's pulls the placemat), and the reset follows
  once it has gone, 1.6 to 2.3 s later. Everywhere else the overlay cat
  still goes.
  - `DoGrab()`, not the level's own event trigger, which plays the same grab
    but leaves the event stuck "in progress".
  - A puzzle finished, left or relaunched under the cat is not reset.
  - A cat whose paw never shows within 0.5 s gets ours instead: a seeded
    Shells can draw its leaf layout, where its cat takes nothing.
  - Tupperware Tower keeps the overlay cat: its climbing cat is the level's
    intro, and a climb takes 8 s.
  - Hand-tested by droha (2026-09-30), a Cat Trap sent from the server
    after moving pieces: on Stamps, Place Setting and MerryMess_Crackers
    the paw came in and grabbed a piece, then the level reset to its start;
    Shells (leaf layout) and Tupperware Tower showed our cat, then the
    reset.
  - `tools/probe-trap-window.py` case F does the same unattended: every
    piece shoved first (DevTools `shove`), then the layout after the reset
    compared with the opening one: 9 of 9 cases pass, and all 6, 4, 10 and
    14 shoved pieces were back where they opened on Stamps, Place Setting,
    MerryMess_Crackers and Shells.
- **Chocolate Bars files its Height ending as Height.** The game reports that
  ending as `Height-(Draggables)_0` on some plays and `_1` on others. The
  table knew only `_0`, so a `_1` finish filed "Design (Shuffle) 1" and
  "Solution: Height" never came on that play.
  - The ending now answers to both ids. So does Books 3's mirror pair, and
    so does Popcorn's `TopRow_1`, which was also seen.
  - Location names and ids are unchanged.
  - A Core test holds every id in `fixtures/solution-ids-observed.tsv` to
    one the table answers to.
- **A reconnect mid-level no longer leaves locked pieces dead.** A
  connection coming back (or a server taking over an offline run) cleared
  the lock's records while the open level's pieces were still frozen and
  grey. So when the ability arrived, the lock put back its own "not
  interactable", collider off and grey.
  - The reset now gives everything back through those records, then locks
    again from what the game really has. A drawer move the game asked for
    still waits for Drawer.
  - Checked in game with a hand-test server stopped and started under the
    open level, then the ability granted:
    - before: Books "9 of 9 dimmed, 9 not interactive" with Swapping held,
      54 fields off a fresh boot;
    - after: Books and DLC1 Nested Drawers (Drawer) both match a fresh
      boot field for field.
- **Each pack's star count on the level select is the run's.** The line
  under a section's title ("- 1/15 (7%)") is the game's own, counted from the
  save. Generators record no solution there, and a location the server sent
  never reaches it, so a pack of finished generators read "0/15 (0%)" with
  every card starred, beside a star goal counting the run's own. It is now
  the cards' hover stars summed (`SectionStars`, Core
  `CheckRouter.StarsOf`), rewritten after the game draws it and on the
  badges' refresh.
  - Checked in game, on a base run with the Opening's five Solutions sent
    from the server. The old build read "0/5 (0%)" on the Opening and
    "0/6" on Pack 1. This one reads "5/5 (100%)", then "1/6 (17%)", and
    turned into "2/6 (33%)" when another Solution arrived while it was on
    screen.
- **The notepad's note follows a Hint Page arriving while it is open.** It
  said "No Hint Pages" (or the old count) until the page was turned. Checked
  in game: "you have 1" became "you have 2" with the notepad open.
- **The credits-locked toast says stars on a star goal**, not "puzzle(s)".
- **The progression guard is gone.** It kept progression off a part check
  whose requirement nobody had established, and since every such level was
  hand-tested on 2026-09-23 it covered no location. No seed moved: the frozen
  draws in `test_regression` are unchanged. What is left is the data's
  tripwire, `test_requirements`: a group asking for less than its level
  needs a dated hand test in `proven-requirements.json`, or it goes red.
  `proven-requirements.json` loses its `suspect` list, and `add-edges.py` no
  longer refuses an edge on a level without an entry.
- **Tools:** DevTools `catevent` (a level's own cat: its config, and one
  started out of sequence) and `shove` (every piece off its spot, for a
  reset to undo); `watch-handtest.py --errors-only --wait-for-game` beside a
  probe; `probe-trap-window.py` numbers its cases and checks the four cat
  levels.

## 0.4.4 - 2026-09-29

**New seeds only.** The goal options changed and location ids moved: a
0.4.3 seed stays on 0.4.3.

- **The star goal counts stars** (droha: "levels_to_star is not right, it's
  number of solutions"). `goal: collect_stars` wants `stars_to_collect`
  stars, one per solution found - the stars the level select counts in each
  pack's header - instead of puzzles with every check done. The logic
  counts them with a Star event beside every solution, carrying that
  solution's rule (events have no id, so no location id moves); the mod's
  counter sums the cards' hover stars. `star_levels` still loads as an
  alias; `levels_to_star` is gone.
- **Defaults:** `levels_to_beat` 50 (was 40); `stars_to_collect` 65, what
  50 of a default run's 70 puzzles are worth - a default run holds 91.5
  stars on average (84 to 100 over 200 generated seeds). The most any run
  can hold is 279, so the range is 1 to 300, clamped to the run's stars.
- **`puzzle_count` comes first** in `player.yaml` and the options page,
  above the goal ("it's more important").
- **`player.yaml` is shorter, in a new order** (droha: the comments were
  "just enough to not want to read them fully"): the run (size, packs,
  goal), DLC, where the puzzles come from, abilities, then items and checks,
  243 lines to 166. The full text stays on the options page, whose groups
  now follow the same order. Then droha's review: that only a generated
  puzzle repeats is said once for every source, `mechanic_coverage` and
  `generator_repeat_limit` are plainer, and a Skip is every check on the
  puzzle, not only "beaten".
- **Achievements can hold progression** (droha: "why do achivements
  never hold anything the run needs? then what's the point of enabling
  them?"). With `achievements` on they are checks like any other: the
  fill may put anything there, a card's star and badge count them, and a
  Skip sends them, so a hard one never has to block a run. They were
  kept to filler until each had been seen to fire in a run; all 17 have
  (2026-09-28). Each still needs every mechanic its puzzle uses. Of six
  campaign-heavy both-DLC seeds, five put progression on them (Draw Me A
  Rainbow holding a Puzzle Pack, Exacting Eggs holding Stacking).
- **`generator_repeat_limit` is 8 by default**, the most there is; 0,
  the old default, still means 8, so no draw moves.

### From the 0.4.3 run (droha and a second player, 2026-09-28)

- **A pack not opened yet is outlines on the overview strip, copies too**
  (droha: "we fixed their big counterpart in the level select screen, but
  not the scroll bar"). The game draws each square from its level's save
  row, which every copy of a level shares, so a copy of an open puzzle was
  filled in a shut pack: all six filled squares in droha's shut packs were
  such copies. 0.4.3 fixed the cards; the strip now gets the same after the
  game draws it (`UpdateOverviewAppearance`, `OverviewItemUnlock`), keeping
  each square's chalk variant, in the game's plain colour. `features live`
  gains `overview strip`. Checked in game on the run's own seed, served
  fresh over droha's finished save (the opening open, 65 squares shut):
  0 filled and all white; before the colour, 35 of them were cream. And
  with a pack arriving mid-session, on the track and inside a puzzle
  (2026-09-29): the new pack painted, every square still shut an outline.
  The pack's rebuild runs the game's `LayoutOverview` and
  `UpdateOverviewAppearance` (DevTools `trace:`), so the redraw is done
  after the game's.
- **Books (Randomized) stays locked without Swapping on every seed.** On
  a seed with a symmetric rule its second controller, Draggables, holds
  the same 11 books and needs nothing, so it freed them all and a player
  without Swapping could do the puzzle ("1 locked, 1 open"; 21 of 40
  seeds). It now locks as Shuffle does (`ObjectLock.LockedAs`). Checked
  in game on the run's slot 62 (seed 945554386) without Swapping: "2
  locked, 0 open", all 22 book entries dimmed and not interactive; with
  Swapping sent, "0 locked, 2 open" and the colours back. droha's hand test
  there: the books greyed and would not move; with Swapping, each rule filed
  its own Solution.
- **Books (Randomized) files its symmetric rule where the seed put it**
  (droha: "i think it has the same id or something for both solutions").
  A seed with a *_SYMMETRIC rule checks that rule with a second controller,
  and its ending `Draggables_0` was always filed as Solution 2. 16 of 40
  swept seeds put the symmetric rule first, and there both rules landed on
  Solution 2: droha's and the second player's copies, both rules done, one
  check each (their seeds' rules read with the new DevTools `rules`). The
  mod now reads the seed's two rules from the level when it finishes
  (`Endings.Canonical`).
- **With a DLC on, the run may end on that DLC's credits** (droha: "if
  they are enabled it should randomize which credits is played at the
  end"). The game has three finales, `Credits`, `DLC1 Credits` and `DLC2
  Credits`; the generator picks one per seed among the base game's and the
  enabled DLCs' (slot_data `credits`, "Finale:" in the spoiler), from a
  generator of its own so no other draw moves, and the track's last card is
  that one. A 0.4.3 seed keeps the base game's. The release gate clicks the
  seed's finale. Checked in game (a 10-puzzle both-DLC seed picking DLC2
  Credits, Steam on): "the run ends on DLC2 Credits (level 1233)", the card
  plays, the goal is reported, and leaving it from the pause menu lands on
  the run's track. Played to their end, untouched, DLC1's credits (about
  three minutes) and DLC2's (217 s) land on the run's track too. Two bugs
  found on the way and fixed: the
  finale was read before the seed was set, and a DLC's credits are not in
  the list the base game's are found in.
- **The credits card starts as soon as the goal is met.** Met by a
  location the server sent while the player sat on the level select, the
  card selected and did not start until the menu was reopened: its save
  row, which is what lets it start, was written only on a pack, a menu
  opening or the run's start. It is written when the goal is met now, and
  never before: at a run's start the goal used to be read before the seed
  was set, which drew the card open with puzzles still owed. Checked in
  game on a DLC1 Credits seed: the server's location met the count, "the
  credits are playable - their card is open", and a click started them;
  holding Credits with the count unmet, the card stayed locked, across a
  relaunch too.
- **No "play them to finish the run" toast once the credits are played.**
  Every session start of a finished run said it, offline and online (the
  second player's offline start of their finished 0.4.2 run). Checked in
  game on the offline start of a finished run. The takeover's "the Credits
  item arrived" line, logged when the item had gone rather than come, is
  gone with the check that wrote it.
- **Nothing playable after a puzzle goes straight to the level select**
  (droha: "it first goes to the main menu? it should hopefully go
  directly to the level select"). The finished puzzle leaves through the
  game's own post-level Level Select, the same call the title's Levels
  makes, instead of the title screen and its Levels. A generator takes
  that route too when nothing else is playable, rather than the Daily
  page. The title route stays for the Daily page, and as the fallback
  when the level select does not open. Checked in game: hand-made
  puzzles (two in a row), generators and DLC1 puzzles each landed on the
  track with its Close button and no title under it, and the next card
  launched from there.
- **The Seeing Stars Boss files every ending it found at its finish.** Its
  Lock and Compass endings reach the save as those phases are solved, and
  only the Knife comes with the completion, so one playthrough found all
  three and filed one until the Boss was opened again (droha's hand test,
  2026-09-28). The finish now reads the save first, and the card shows
  3 of 3 with no retry panel. Checked in a run with the two phase endings
  written into the save the game's way (DevTools `marksolved:`).
- **Each finish logs the ending the game named and where it went**
  ("checks: slot N ended on '<id>' -> <location>", or "... again - nothing
  new"). The second player's Books (Randomized) finished twice with one Solution check
  in, and nothing in a player's log could say which ids came.
- **Four parts that never fire are no longer checks** (droha: "yes
  remove the ones that never fire"): Lunch Tray's Trays, Fossils' Drawers,
  Nesting Boxes' Boxes and the Boss's Drawer (Cupboards and Drawers).
  Their drawer controllers never report a solve in play (droha's
  recordings, both players' runs, and droha's full play of the Boss on
  2026-09-29), so what was placed there came only with the goal's
  release: the second player's green Fossils and Lunch Tray cards. Now
  `notALocation`, like the other 13 drawer controllers. Three other parts
  nobody had seen solve were played the same day and do fire: Fountain
  Pens' caps, Junk Drawer 2's Shuffleables, Material Drawers' Drawer
  Draggables.
- **Breadtags is just its Solution** (droha: "combine/remove the breadtags
  order, and just have the solution"). Crumbs and Interlocking are part
  of the Solution, no check of their own, so one finish is one check.
  Location ids moved: base 401 -> 385, DLC1 114 -> 110.

## 0.4.3 - 2026-09-28

**New seeds only.** A new item, new locations and a changed draw: a 0.4.2 seed
stays on 0.4.2.

### From the 0.4.2 two-player playtest (droha and a second player, 2026-09-27)

- **Nothing playable goes to the level select.** When no open card has
  anything the run can do, the next arrow, a finished puzzle and the daily
  guard now show the track, with "Nothing to play yet - waiting on items",
  instead of opening a blocked puzzle. The pickers moved to Core
  (`SlotPicker`). Checked in game with every ability revoked.
- **The title is no longer drawn under the level select.** Reproduced: after
  a generator finished on the Daily page, the menu system's active menu was
  still the Daily menu when Levels was pressed. The press now waits for the
  title, presses its own Levels, and a backstop takes a leftover title down.
- **A solution is judged by the part its ending names.** Every solution
  location carries the level's whole ability set, so Spoons' Size (Elastic)
  ending was withheld for Stacking. The ending's id names its controller for
  198 of 219 ids seen (`SolutionParts`); those file on that part's abilities.
- **Endings have fixed names** (droha: "yes we want fixed ending names"):
  "Spoons - Solution: Stacked", "Keys - Solution: Ordered 2", "Radial Dance
  Party - Solution", instead of "Solution 1, 2..." filled in the order found.
  The mod files the ending the game's solution id names
  (`CheckRouter.ForEnding`); an id the table does not know fills "Solution:
  Other 1, 2..." in the order found, the same at every launch. Generated
  puzzles too (droha: "don't generated puzzles still have fixed
  solutions?"): their ids are the same on every seed, by position, so
  "Pencils (Randomized) - Solution: Ordered 2" is the seed's second rule
  whichever is found first. Books (Randomized) checks its second solution
  with a second controller on symmetric seeds, so that ending answers to
  either id (`Shuffle_1|Draggables_0`, measured with `gensweep:40:995`, which
  now records each controller's solutions). The fourteen endings nobody had
  seen were played by hand the same day, so every ending has a real name;
  "Other" is only for an id the table does not know.
- **An ending is one check, not two** (droha: "No reason to give an ending
  2 locations"). On a level with several endings, a group that is itself an
  ending (Spoons' Stacked) has no part check. Its ending asks only for the
  group's abilities where forcing that group alone finished the level
  (`finishesAlone` in levels.json, eleven levels); every other ending asks for
  the whole level, as Sharp Pencils, DLC1 Fountain Pens, DLC2 Robots and DLC2
  Bells need. Base locations 423 -> 401, DLC1 115 -> 114, DLC2 141 -> 124.
- **Medicine Cabinet is checked per colour** (droha: "yes per color"): the
  seven groups of red items are one part, "Red Items". Mirror's big items
  in place are one, "Still Life" (jug, candle, dish, bottle, the books-and-box
  stack); its little things - the lemon wedge, what goes in the containers,
  the candle put out - are part of its Solution, no check of their own
  (droha; `solutionOnlyParts`). A part now files on its group's
  last controller, not its first: placing the cup no longer sent "Red Items"
  (checked in game, filed as the seventh went in).
- **Media Cabinet (Cupboards and Drawers) no longer waits for Drawer**
  (playtest item 9). The second player finished it without Drawer and its Solution was
  withheld; droha's hand test (2026-09-28, holding only Ordering and
  Stacking) finished all four parts and the level the same way. Drawer is
  now in `bypassedAbilities`: the Solution and Beaten need Ordering and
  Stacking.
- **Cards in packs not opened draw locked.** A repeated generator's later
  copy showed its art in colour because an earlier copy was played; the
  per-card rule ran before the card was in the track's list, so it now runs
  again once the track is built. Checked on Pack 5.
- **No retry panel pop-up when a finished puzzle moves on** (droha). With
  nothing left to find the panel is not shown and its own arrow is used; with
  solutions left it shows as before. Generators keep the game's straight-on
  route; every other level takes the panel's, since the straight-on route
  broke pause-menu Exit on Seed Pods and preceded the lost cursor after
  Tupperware Tower. `CursorGuard` puts the cursor back if anything hides it
  in a playable puzzle.

### New

- **Background Reset Token** (droha, 2026-09-25), a filler item: the pause
  menu's Reset Background entry spends one to put every backdrop back to the
  game's own colour until the next Background Change Trap. Never automatic.
  Half the filler left after traps and the hint floor (below).
- **The mod's files moved out of the Steam Cloud.** Steam syncs `*.json` in
  the save folder, four files at most, and every extra run went "over quota".
  Run saves, run files and the session cache now live in
  `<save folder>/Archipelago/`; a launch moves the old ones in. Steam's log
  now finds one file, `save1.json`.
- **Achievements as checks** (droha, 2026-09-28), `achievements`, off by
  default: 7 puzzle achievements in the base game, 17 with both DLCs, each a
  check only when its puzzle is in the run. Chosen from DevTools
  `achievements`, which reads the level every checker watches; the ones that
  name no puzzle (chapters, hints, the credits, a DLC1 cat interlude) or
  repeat a check the run has (Ghost Cat's finish, a Boss ending, Figurines'
  "Groupables (Achievement)" part, Paw Prints' leaves and spill in Path of
  Destruction, the Sharp Pencils shavings and Breadtags crumbs in Sweep Them
  On The Floor, which also waits for both puzzles) are left out, and so is
  Keep Away: droha met it four times on Place Setting and it never fired.
  Each needs its puzzle's whole ability set, holds no progression, and is
  outside the star and the Skip. One the Steam profile already holds is
  earnable too: the mod clears the game's in-memory "achieved" flags at each
  puzzle, never Steam's. droha's hand test (2026-09-28, an achievements
  seed) filed 14 as real checks in a run: Fun for Humans Too, Draw Me A
  Rainbow, Top Heavy Slice, Can Do Altitude, Rainbow To The Moon, Keeping
  Count, Where Is My Cap?, Bad Kitty, Show Off, Sticky Wand, A Balanced
  Meal, Grabbed the Wrong End, and - held on droha's profile - I'll Take My
  Water Neat and Harmonized Purr; Exacting Eggs, Unstable Stacker and Now
  You're Playing With Power fired on their puzzle in earlier runs. All 17
  seen.

### Generation and options

- **Filler** (droha, 2026-09-28): Cat Traps default to 15% of the filler
  (was 25); Background Change Traps take their own share,
  `background_trap_chance` (15); after the `hint_coverage` floor the rest is
  split evenly between Hint Pages and Background Reset Tokens ("have even
  hint and background reset tokens as the only fill").
- **Option ranges say what the generator does**, each end measured by
  generating it: `puzzle_count` 10 to 130 (a 130-puzzle run loaded in game;
  its overview strip shrank to fit but started 430 px off the left edge, so
  the first 50 cards were hidden - it now stays centred, with the ends a few
  pixels clear of the screen edges; and the bar it sits in, which carries
  the scrollbar, shrinks with it: before, the bar stayed 3037 wide on a 1920
  screen, so dragging it stopped short of both ends and its handle was drawn
  off-screen (droha, 2026-09-28)),
  `pack_size` 5 to 20 and `guaranteed_open_slots` 4 to 20: below 5 and 4 the
  generator already raised them. `guaranteed_open_slots` defaults to 5, the whole opening at the
  default pack size (droha: "in step with default pack size"). Tooltips and
  `player.yaml` lose the stale "five chapters", state each range once, and
  say the weights are relative and need not add up to 100; Mechanic
  Coverage 6 is no longer described as identical to 5 (it reserves one more
  stacking and container puzzle).
- **A hint says where the puzzle is on the track**: "Pack 2, puzzle 4" or
  "Opening, puzzle 3", the level select's own section names, instead of
  vanilla's "Ch.2 Level 3", which a run's track never showed.
- **The four fixed-layout DLC levels roll under their DLC's weight**, not
  `generator_weight`: with both DLCs on they were in every default run, and
  still in every run with both DLC weights at 0. `player.yaml` puts all five
  weights together and quotes each source's share for the defaults, pinned to
  the draw by a test.

### Tools and docs

- DevTools: `achievements` (with each achievement's description, and the
  level and settings every checker holds), `cursor`, `scrolltrack:<n>`;
  `watch:` prints the level's colour fields.
- DevTools restructured (droha: "make sure it's documented well, and
  structured well"): commands dispatch from one table, `help` lists them by
  area and an unknown command suggests the nearest; files regrouped by area
  with shared helpers; `docs/dev/devtools.md` rewritten to match the code
  row for row (fourteen rows had drifted), and `check-devtools.py` now
  compares every spelling and area with it. Nine finished-investigation
  commands deleted with their seven always-on patches (`showpause`,
  `launchtrace`, `reorder:`, `cardlabels`, `tint`, `inert:`, `lockcard:`,
  `marker:`, `resettest`). New: `objects:<file>` (`state:<file>` still
  works), `stop` ends a running sweep, `<name>:off` for every switch.
  `gensweep:<seeds>[:<index>]` - it read an index as a seed count, and
  `gensweep:1013` would have started 16,000 regenerations nothing could
  stop. No more `alttl-events.log` or launch auto-dump; `dump` records
  whether the mod was loaded and `check-game-facts.py` refuses one that was.
- Docs reorganised: `docs/dev/` (developer guide, DevTools, testing, release
  gate, level data, backlog), `docs/reference/`, `docs/history/`, and
  `tools/README.md` indexing every tool. Four superseded setup tools deleted
  (window-size, handtest-chalk, setup-handtest, setup-ability-handtest).
- `harness_env` restores only files whose bytes changed, and covers the new
  folder.
- Release gate: the patch census reads the feature list from `Plugin.cs`
  (four features had gone unasserted), the yaml asks for the pack size it
  gets (5), and `predict_gate.py` asks the gate's own pre-flight instead of a
  second model that disagreed with it on Desktop Computer. The arrow check
  never starts on a generator: with nothing left it moves straight on, so
  there is no arrow, and the new defaults put Stamps (Randomized) first; the
  seed walk passes over a seed whose opening has no other slot. A forced
  solve answering "no level running" without a LevelComplete is no longer
  counted as a completion: the pass is refunded, as for a cat trap, once the
  level is back (the DLC gate's Trophy Cabinet relaunched mid-solve).
- `handtest-level.py --achievements` for a seed with achievement checks.
- Every tool starts the game through `release_e2e.launch_game()`, muted from
  DevTools' first frame (droha: "you should be muting when you're testing in
  the background"); `probe-slots.py` and the hand-test setups played sound.
- Release gate and fixed endings: the paper plan files the ending forcing
  makes, which depends on what is held (Spoons without Stacking finishes on
  Size), and leaves every other ending to a Skip. The ids forced
  completions report are harvested from the gate logs into
  `fixtures/forced-endings.tsv` (`tools/harvest-forced-endings.py`). The
  spoiler reader split "Location: Item" at the first colon, which ending
  names now have, and called seed 20260909 a generation bug.

## 0.4.2 - 2026-09-27

**New seeds only.** The logic changes below change what a seed requires, so a
0.4.1 seed stays on 0.4.1.

### Logic from the first two-player multiworld

droha and a second player played a 0.4.1 multiworld on 2026-09-25 and found parts the card
offered that could not be done. Each now waits for what it physically needs
(`tools/add-edges.py`, evidence in `proven-requirements.json`):

- DLC1 Daggers: the loose daggers need Drawer (their Box is in the drawer).
- DLC2 Water Glasses: Water Level needs the ice cubes sorted first.
- DLC1 Lunch Tray: Tray Organizer needs the tray drawer (Drawer).
- DLC1 Sewing Box: Top Drawer and Curved Needles need Drawer.
- DLC1 Jewelry Box: Rings needs Drawer, so the whole level does.
- DLC1 Daggers has no part checks any more. Both needed Drawer, the same as
  its Solution, so they always arrived together (droha: "it's all or
  nothing"). Solution and Beaten still need Drawer.

Two more from droha's locks-on play with Drawer withheld (2026-09-26), where
the game's own hint puts the pieces inside the drawers:

- DLC2 Material Drawers: Drawer Draggables and the containable need Drawer,
  since the items are sorted into the drawers ("impossible without the
  drawers"). Drawer Draggables asked for nothing before, so a seed could put
  Drawer there and never reach it.
- DLC1 Nested Drawers: the peanut goes in the innermost drawer. The level has
  no part checks and its Solution already needed Drawer, so no requirement
  changes; the table now says what play found.

Three more from droha's drawer and door hand tests (2026-09-26), each played
with one ability revoked and then given back:

- DLC1 Jewelry Box: Watches, Radiolaria, Brooches, Gold Bars, Cameos and the
  Locket need the rings, and so Ordering. Finishing the rings is what opens
  the first drawer ("can't complete anything as it needs to be completed to
  get the first drawer open").
- DLC1 Tea Cabinet: Items Placements needs the cupboard doors (Gadgets). The
  items go behind them, and it asked for nothing before.
- DLC1 Sewing Box: Small Spools, Supplies and Top Drawer need Containers too.
  With the drawers open and Containers missing none of them finished; all
  three did within 11 s of Containers arriving.

The same tests found parts done without the Drawer the table asked for, with
nothing on screen greyed out, so a player could already do them out of
logic. On droha's word they no longer need Drawer:

- DLC1 Lunch Tray: Tray Organizer and Broccoli. The whole level finished
  without Drawer. The Trays part itself did not, and keeps it.
- DLC1 Jewelry Box: Rings.
- DLC1 Sewing Box: Large Spools.
- DLC1 Fossils: Fern and Snake, the two in the drawer that starts open.
- DLC1 Game Pieces: Heart and Center Tiles.

Four DLC levels are drawn at most once: DLC1 Trophy Cabinet and DLC2 Water
Glasses, Figurines and Bread Crusts. The game marks them randomizable, but the
seed does not change their layout - droha found duplicates "exactly the same",
though the second player's log shows each copy launched with its own seed. Measured on all
20 randomizable levels with two seeds each: only these four keep their layout. They still launch
seeded, as before; their later copies' locations are gone (DLC1 136 -> 115,
DLC2 260 -> 141), so every DLC location id after them moved.

The second player's log also showed Pantry and Trophy Cabinet finished with nothing locked
before Drawer arrived, and Figurines' solutions and sorting done before
Sticking, each Solution withheld. Their extra Drawer, and Figurines' Sticking,
are bypassed like Medicine Cabinet's below.

Medicine Cabinet goes the other way: its Solution and Beaten no longer ask for
Drawer. The second player finished the whole level without it, after droha's four runs had
fired every group without it. The Drawer is bypassed rather than deleted, so
the draw - and every frozen plan in `fixtures/plan-0.3.4.json` - is unchanged.

### Locked drawers, cupboards and doors stay solid

Tea Cabinet's locked doors let the items behind them be grabbed through them,
and Sewing Box's locked drawers let their contents be pulled out. A locked
object lost its collider and its rigidbody's simulation, and a cover without
either stops blocking the pointer (Unity: a collider on a body that is not
simulated is not seen by raycasts). A cover - an object only a drawer,
cupboard or door controller holds - now keeps both and is locked by the flags
and the grey alone; what it holds locks as before. A scrubbed-open door
(AnimScrubbables) is a cover only on the three cupboard levels: on Wilting
Flowers the same controller is the puzzle. A locked scrub object now refuses
the scrub itself (a freeze does not stop one), so the flowers cannot be stood
up without Gadgets (droha hand-tested it). They also show as locked now: each
flower is drawn by a separate animated sprite, and the grey went on the logic
object, which draws nothing. Checked by screenshot, grey without Gadgets and
full colour once it arrives.

A drawer piece that can neither slide nor be picked up is not locked at all.
Sewing Box's box is one (its drawers slide 1.86 and 1.89), and locking it
greyed the whole box at 60%, the closed drawers underneath showing through:
droha, "the whole box and drawers are greyed out". Now the box draws in full
colour and only the drawers are dimmed. droha hand-tested it: the drawers
cannot slide, the box is not greyed, and nothing locked in or on it blocks
things being put down. What tells the box from the trays and boxes a player
carries (Lunch Tray, Nesting Boxes), none of which slides either, is the
game's own "cannot be selected" flag on it; a sliding drawer always locks.
Tea Cabinet's doors hold too: dimmed, they do not move, and nothing behind
them can be grabbed.

A locked drawer does not move when the GAME moves it either. On Jewelry Box
without Drawer, solving the rings opened the greyed Brooches drawer, and it
could not be closed again. The game's own open and close now wait while the
drawer is locked, and the latest one happens when Drawer arrives. Closing
waits too: Kitchen Utensils swaps its drawers with a close and an open, and
refusing only the open left both shut. droha hand-tested both.

On Daggers the drawers slid open while their Box stayed locked. A drawer that
slides now follows its drawer lock even when another group frees it, so the
Box and its drawers lock together (droha: "i can't open the drawer, nor drag
the box"). Jewelry Box's Main Box, which cannot move at all, is no longer
greyed. A locked drawer's art is dimmed all the way down: on Bathroom Drawer
one handle stayed in full colour, because it is drawn by a child sprite.

Two faults left pieces in the drawers dead once their ability arrived, both
on Sewing Box. They were in colour but could not be picked up. First, pieces
got their physics back as recorded when first frozen, and some had recorded
it stopped. They now get it back (droha: "they are physicasble"). Second, a
drawer saves its contents' "can be picked up" state when it closes and puts
it back when it opens. Shut over a locked piece, it saved the lock's "no" and
gave it back after the unlock, and every later close saved it again (droha: "i
can't move the saftypins that are in drawers"). Now a piece saved while locked
is saved as free when it unlocks. Checked with DevTools `drawers`: lock,
close, unlock and open left Zipper (2) and SafetyPin (5) unpickable before
the fix, and pickable after it. The same steps free Paper Plane Supplies'
chalk shut in its drawer without Jigsaw. The fix is on the game's drawer
itself, so it holds on every level with drawers.

Pieces the game keeps fixed stay fixed when their ability arrives. The unlock
used to make every locked piece interactable, so Sewing Box's Curved Needles
Container could be moved around once Containers came in. The lock now keeps
the game's own "interactable" while it holds a piece, including a change the
game makes meanwhile, such as a phase starting, and puts that back.

A locked piece is grey all over. The grey used to go only on a piece's main
sprites, so Candles' candles, drawn by Body, Holder and Tip child sprites,
stayed in full colour while locked, as did Spice Jars' lids, Pencils' wood and
lead, and Lamp's LEDs. Now every sprite a piece shows is greyed; shadows are
not.

Clear sprites stay clear. Daggers' drawer masks and Books (Randomized)'s book
sprites are clear by design. Locked, the masks showed as grey boxes, and after
the unlock both came back solid. A clear sprite is no longer greyed. One that
fades in later is greyed then, and comes back fully opaque. A piece's colour
is put back only while the grey is still on it; if the game has painted it
since, the game's colour stands.

A piece whose collider or physics the game turns on after the lock took it
comes back with them on. PawPrints' paw prints and spills start without them
and get them from the game a moment later. The lock put back what it first
saw, so 19 pieces came back dead.

A cover stays solid with every ability missing. Sticky Drawer's gum and
stickers are held by another group while the level loads, and by the drawer
alone afterwards. Frozen as ordinary pieces at load, they stayed without a
collider once they counted as covers. A cover frozen earlier now gets its
collider and physics back.

A sticker's handle and PawPrints' cloth lock with what they move. A sticker
is peeled by a separate handle and PawPrints' mess is wiped by a cloth, and no
group lists either, so neither was ever locked. With Sticking missing, Sticky
Drawer's stickers could still be peeled and stuck through their gum; with
Tidying missing, the cloth cleaned PawPrints' first screen (droha, hand
tests). A sticker's handle now locks with its sticker, and a cloth with the
level's clearable pieces. Candles' match was the same: in no group, in full
colour, and once lit it grew and shrank the grey candles with Gadgets
missing. A match now locks with the level's candles.

Cat Food Cans, Boxes (Stacked) and Presents (Stacked) restart when their
ability arrives while they are open. Unlocked in place, the pieces the game
keeps fixed under others came back pickable but could not be put down (droha:
"they don't drop"). Every piece in these levels is locked without the ability,
so the restart loses nothing; a toast says why.

The lock also runs again when PawPrints' intro ends. The game switches the
cloth and 19 paw prints back on the frame after it, and picking the cloth up
does not check the lock's flags, so the grey cloth still cleaned the first
screen. The extra pass runs only on a level with a cloth, since nothing else
needs it.

Found with a new probe, `tools/probe-lock-roundtrip.py`. It loads a level with
every ability held, then locks it, moves its drawers, gives everything back,
and compares the two, object by object.

### The retry panel's arrow is pressed the way a player presses it

On a level whose own ending is the three-button panel, with nothing left to
find, the mod presses the arrow for you. It pressed `ReplayMenu.NextLevel`, a
menu that is not on screen, and the real panel stayed up over the next puzzle
(droha, twice; in the second player's log the automatic arrow reached the next puzzle 6
times in 37). Calling `RetryMenu.NextLevel` left it up too. The mod now waits
until the panel has finished showing, then clicks its Continue button through
the EventSystem, as a pointer does. Checked in game: Cookies Jigsaw, then
Spider Web with no panel. DevTools `next` does the same.

### Messages read like the text client

droha: "it should say whatever the normal text client shows." Item toasts are
now the server's own item messages for this slot, in the text client's colours
(who found what, for whom). The "Puzzle beaten (x/n)" toast is gone - the
level select already shows it - and "Found X" shows only while offline, where
no server message will come.

### Leaving the credits stops them

droha: "when i exit out of the credits, and hit play from the main menu, or
go to a level in level select, the credits still playing". The credits scene
has no ExitGameToTitle, so the pause menu's Exit forced the title state and
the credits sequence ran on underneath. Exit during the credits now ends them
with the game's own `Credits.CreditsComplete`, found through the running
level (the first try only searched active objects, missed it, and droha's
exit fell through to the old route). Checked in game: Exit lands on the
track, and the next puzzle opens with no credits over it.

The pause menu's Levels button ends them the same way; before, it went to the
track and left them running too.

The goal is still reported as the credits start, by design; the log line that
said "credits: played to the end" now says so.

### The credits card is on the track from the start

It appeared only once the Credits item arrived, and then already filled in:
The second player, with 68 of 40 beaten, never saw it until the item came (droha,
2026-09-26: "It should always show the outline version"). It is now the last
card from the start, drawn locked until the item is held and enough puzzles
are beaten; a click says which of the two is missing.

### No Steam achievements while the mod is loaded

The second player got Steam achievements playing the run's credits. A run plays the game out
of order, with skips and other players' items, so it earns nothing an
achievement claims. The mod now stops the game's achievements and Steam stats
at Steamworks (one call site each in the game, so every route), and logs what
it withheld. `[Steam] AllowAchievements` turns them back on.

### Checks sent for you mid-session reach the cards

droha sent a location for the second player with `/send_location` and their card did not
change until they reconnected: the server's checked list was read only at
login. The mod now takes the server's updates as they arrive. Checked in
game: a release mid-session arrived as "the server marked 35 more
location(s) checked", and every card with a Solution turned done.

A Solution counts the puzzle as beaten, however it arrived. droha: "I
wouldn't say 'beaten' is different from solving at least 1 solution for the
puzzle." Beaten is an event with no server address, so a Solution sent with
`/send_location` left the card green and the beaten count short forever. Any
slot with a Solution in now gets its Beaten filed too, at login, offline and
mid-session.

### A card in a pack not opened yet draws locked

The game draws a card's art from the level's save row, and every copy of a
generator in a run shares one level. So with Microscope open in an early
pack, droha's locked Pack 11 drew Microscope #3 and Clock #3 in full colour
beside three locked cards. Clicking them was already refused; now they also
draw locked until their pack opens. Not yet checked in game.

### The level select strip shows which packs are open

droha: "it's hard to tell how many packs you actually have open." A card in a
pack not yet opened painted the same red as a card locked by an ability. Those
dots now keep the game's own locked look. Not yet checked in game.

### A refused seed delivers nothing, and a reconnect no longer replays old cats

The server starts sending as soon as the socket is up, before the mod has
checked the seed. So a seed the mod refused (built for a DLC droha did not
have) still delivered its Jigsaw into the offline run on screen, with a toast.
And every reconnect handled the item replay before loading the run state
that counts traps already sprung: "26 cat(s) found nothing to knock over",
then "25 trap(s) already sprung" (droha; 40 in the second player's log). On the title
screen they hit nothing; in a level they would have reset it. The session's
events are now held until the login is accepted, then delivered in one step:
items, then the run, then the rest. Checked in game: a refused seed logged
three refusals and no item, toast or trap.

### A check withheld for a missing item is sent once it arrives

A part solved before its ability arrives is withheld, and the log promised
"it will be filed on a later visit once the item arrives". Nothing remembered
it: the game rebuilds the level unsolved, so a later visit had nothing to
file. The second player's Figurines "Sorting Items" was withheld on both copies and never
sent after Sticking came. Withheld checks are now kept in the run file and
sent as soon as the run can reach them, even without revisiting.

### A level outside the run no longer files the last puzzle's checks

Starting a level the run does not contain kept the slot played before it, so
that slot took the level's checks: finishing Wilting Flowers, which the seed
did not contain, right after Fruit Stickers sent "Fruit Stickers - Solution 2"
and showed the panel's arrow as if Fruit Stickers had its second solution.
Found with a DevTools boot on a local server. The run now leaves the slot when
such a level starts, and anything solved on it is filed nowhere: the same
steps after SpiderWeb logged "left slot 3, nothing on it is filed" and sent
nothing.

### Nothing left to play after a daily puzzle no longer strands the track

After a Daily Tidy puzzle (Procedural Grid Puzzle, the Randomized ones and
the other generators) with nothing playable left, the mod took the run to the
track from the Daily page. That track had no Close button and its cards never
launched: droha's first session ended there. It now goes to the title and
opens the track once the title has settled. Checked in game: the card
launches.

### Seeded puzzles show the randomizer's hint

droha: a randomized Calendar showed the hand-made Calendar's solution. The
hint menu picks the randomizer's pages when the level says it is a Daily
Tidy, and the run answers "no" to that everywhere to keep its puzzles off the
Daily page. It now answers truly while the hint pages are chosen. Checked in
game on all 20 randomizable levels, each opened from the run's track: the 16
that re-randomize show their randomizer's page (Calendar `Calendar-DT-hint 4`,
`hint-calendar` without the fix), the four fixed-layout DLC levels their own.
Before, the ten hand-made generators showed the hand-made solution.

### Ability locks do less work, and cannot run away

The lock pass re-ran on the game's ControllerChanged event, which is about
INPUT devices, not level objects: it fired 13 to 40 times a second while a
player moved the mouse (81,810 times in droha's run), each time a full pass
plus half a second of per-frame passes. It no longer listens to it. The pass
also writes the interaction flags only on objects it locks, and puts them back
once on unlock, instead of writing "interactable" to every free object on
every pass, and it has a re-entry guard.

This fixes a crash. Fruit Stickers crashed with a stack overflow within seconds
of dragging stickers: twice in droha's run, and again on 0.4.1 in a repro,
after 10 s with nothing locked. On this build droha stuck, peeled and dragged
stickers off for a minute with no crash (2026-09-26).

## 0.4.1 - 2026-09-25

**0.4.0 seeds: finish them on 0.4.0, or regenerate.** Nothing in the item or
location tables changed, but the mod refuses a seed made by a different apworld
version, as it has since 0.3.3.

### The retry panel shows while a level still has solutions to find

droha, 2026-09-25: show the three-button panel (restart, pause menu, next
arrow) when a level has several solutions and the run has not found them all,
and go on to the next puzzle otherwise. The game decides this per level:
`LevelSuccess.LevelComplete` reads `LevelInterface.ShowRetryMenu` (DevTools
`xrefs`), and re-read with both DLCs, 148 of its 186 levels show the panel and
every generator goes straight on (`docs/data/level-endings.tsv`, now 173 pool
levels; none of the 111 first read on 2026-09-13 changed). During a run the
mod now answers that getter for the running slot (`RetryPanel`): the panel
while the slot has more than one Solution location and they will not all be
in once this completion is filed. The screen asks at LevelCompleteEarly,
before the check is filed, so the completion in progress is counted when its
arrangement is new and earned (`SolutionOrdinals.Peek`). Checked in game on a
hand-test seed: Pencils (Randomized), which never showed a panel, showed it
after its first of two solutions and its arrow opened the next run slot;
Pencils with both solutions in went straight on. Not yet pressed: the panel's
restart button.

A level built for the panel with nothing left to find still shows it, and the
mod presses its arrow (ReplayMenu.NextLevel) the moment it is up. The first
version sent such a level down the game's own straight-on route instead, and
the DLC gate caught what that costs: afterwards the pause menu's Exit did
nothing. Reproduced by hand on Seed Pods (Exit never reached
MainMenu.ExitGame) while after Post-It Notes, a generator that goes straight
on by design, Exit worked; through the arrow, Seed Pods went on to the next
slot and Exit reached the title. The gate's arrow check now waits for the
mod's own press before pressing `next` itself.

### Release gates keep running when the game window loses focus

The game pauses itself when its window loses focus, and a paused game holds
every solve: the first DLC gate rerun for this release lost its arrow session
to one click on another window. DevTools gains `[Window]
KeepRunningWhenUnfocused`, which skips that pause; the harnesses set it and
harness_env puts it back. Checked: focus handed away, the game logged the
skip and stayed unpaused. DevTools is not part of the release.

### The stars on the level-complete screen are the run's

droha, 2026-09-25: the stars that pop when a puzzle is finished were always
empty, while the level select showed them right. The complete screen
(LevelSuccess) fills them from the level's save row, which a generator puzzle
never writes: every completion in droha's run logged `found=0`, Pencils'
second solution included. Measured on Pencils (Randomized): the stars pop
during LevelCompleteEarly, before LevelComplete files the Solution check. The
mod now sets them as they pop from the checks already in, and again the moment
this completion's check is filed or a Skip releases the card: one per Solution
location, lit once collected, as the card's hover stars are (`SuccessStars`).
Checked in game: Pencils showed one of its two stars filled after its first
solution, and Seed Pods, a base puzzle, its one star. droha, back in the live
run: "yes the stars are showing up correctly now."

### A drawer no longer unlocks what sits inside it

droha, 2026-09-25, in Paper Plane Supplies holding Drawer but not Containers:
the knife pieces, blades and pencils moved freely, and putting a knife handle
in its case turned the case grey while it still dragged. Measured with DevTools
`sharing:` and `locks` on a hand-test seed: those 14 pieces are held by
Containables and the Drawer Controller and by nothing else, and the dimmer let
any unlocked holder free an object, so holding Drawer freed them all (0 of 121
objects dimmed). A handle in the case makes the game add a new object, Knife
Case Bottom, to Containables; nothing else holds it, so it was locked and
greyed, and the case, a plain draggable, looked grey but moved.

droha: "i don't think the container should count as a drawer." A drawer or
cupboard (DrawerController, DrawerExpandableController, Cupboard) now decides
only an object no other controller holds (`ObjectLock`, tested). Checked by
hand on the same level: without Containers the pieces are grey and do not
move while both cases stay coloured and draggable; with Containers granted
they unlock at once, and a handle in the case leaves it coloured.

All 18 levels with a drawer or cupboard were re-swept offline the same day:
28 groups in 9 levels were freed only through a drawer, every object of each,
and none partly - Paper Plane Supplies (the holders and the eight chalk
groups), Tool Drawer's holders, Bathroom Drawer's bottle, and DLC1 Craft
Supplies, Fossils, Game Pieces, Jewelry Box and Sewing Box and DLC2 Material
Drawers. Every one of those checks already asks the logic for the group's own
ability, so no seed changes; the other 9 levels, including the hand-tested
bypasses on Junk Drawer Transforming and Combs, are unchanged.

## 0.4.0 - 2026-09-25

**0.3.4 SEEDS NEED REGENERATING.** The tables grew from 432 locations to 822
and from 18 items to 19 with both DLCs, and ids moved when parts the game
solves the moment a level opens, or never, stopped being locations. The mod
refuses a seed made by a different apworld version, as it has since 0.3.3.

### Starting a new seed no longer resets your resolution

Measured 2026-09-25: a new run's save file starts fresh - no campaign
progress, which is right - but with `resolution` 0 where the campaign had
19, and the game writes that file inside its own load. The settings mirror
then copied the 0 into `save1.json`, so every new seed reset the player's
resolution setting. That first save is no longer mirrored; the campaign's
settings are copied into the new file and it is loaded again. Checked in
game: after a new seed's first load `save1` is unchanged and the run's
settings match it. The comment that said a new run file was built from the
campaign data is corrected.

### No "failed to load" alarm for a chapter card

The base gate of 2026-09-25 beat all 15 puzzles and failed one check: the
mod's empty-level watch logged `LEVEL LOADED EMPTY: 01__Chapter_HomeSweetHome`
at the title, after a Skip's fallback went back to the track and the track
was closed. A chapter card never has objects or controllers; the game only
held it as the active level. The watch, which also shows the player a
"failed to load - please report this" toast, now ignores chapter cards
(`EmptyLevelWatch`, tested). Reproduced on Pencils before the fix, gone
after it.

### Locked and beaten cards are the same red square

droha, 2026-09-25: "can we just have a red square for both? The player can
always hover to see stars, or see x/n beaten in the top right corner." The
veil over a locked card is gone; the badge is red, red/green, green or a
star.

### A Skip works on every level

droha, 2026-09-25: skips must work on every level - the game's own skip, or
where it will not skip, release the locations, use the Skip and go back to
the level select. Measured first: with no run up, the game's SkipLevel
completes all 173 levels inside the call and raises LevelSkipped
(`probe-forceable.py --skip-all`, now the `skip` field of
`fixtures/forceability.jsonl`). In a run it does nothing on a generator
level: Pencils reads `Skippable` False there. The life of a Skip is now
Core's `SkipFlow` (tested): armed on the press, charged exactly once when the
game's LevelSkipped arrives (which is where the rest of the slot is sent), and
where the game has not skipped by the time SkipLevel returns and the level is
not skippable, the mod sends every location on the card, banks Beaten, spends
the Skip and goes back to the run's track. A skip that never lands is dropped
uncharged when the next level starts. The release gate may now buy a Skip on
any level, and stops if one does nothing. Checked in a run on all 20
generator and all 26 holiday levels: the flag matched every one - 30 skipped
by the game (the holiday levels and the four DLC generators), 16 released by
the mod (the base-game generators), none left waiting. Pencils through the
pause menu's own Skip button: Skip count 2 -> 1, all three locations sent,
card Complete with both stars, back on the track. Five levels read
`Skippable` False and are skipped by the game anyway (Radial Dance Party,
both Tupperware levels, DLC1 Boss, DLC2 Ghost Cat); a skip that lands inside
the call is final whatever the flag says.

The first DLC gate after this stopped at visit 19 of 25: a Skip on DLC1 Boss,
already beaten, completed it and released its three parts but banked no new
token, so the harness booted the next level straight over the finished one,
StartLevel did not take and the pre-Skip check stopped the run. The harness
now unwinds to the title after any spent Skip.

### A run's hover stars are the run's

droha, 2026-09-24: "if i hover over the level i don't see the alttl stars
filled in at all", and the rule: they start empty for each seed and fill as
its solutions are completed, never synced with the player's own save; a
skipped card's stars all fill. The game sets a card's stars from the level's
save row. Measured 2026-09-25: a base level finished in a run lights its star
(Cat Toys 1 of 1), but a generator puzzle records no solution (Spider Web,
finished, `found=0`, star off). The mod now sets every run card's stars
itself, one per Solution location, lit when collected
(`CheckRouter.SolutionStars`), after each `SetCompletionStars` (41 calls per
track build) and on the badge refresh. Checked in game: Spider Web lit, an
unfinished card empty, a Skip lit Presents, and hovering left both saves
unchanged. A solution the mod withholds for a missing ability stays unlit
until it is filed.

### Offline shows what online shows

The session cache now carries the locations the server had (collected, not
owed), and an offline start restores them as the login does, so cards, stars
and the goal count read the same offline. A check still owed is never cached
as the server's: the run state keeps sending it. A cache from before this
loads with none. Checked in game: seven cards, their stars and "4 / 40
beaten" were identical online and offline; from a cache without the list,
three finished cards read Doable with empty stars.

### `puzzle_count` starts at 10, up from 8 in 0.3.4

droha, 2026-09-25: the minimum is 10, two full packs at the pack size of 5.
A yaml asking for 8 or 9 no longer generates. During this release it was 15
while checks with unproven requirements had to be kept free of progression
(below); no part location is guarded any more. Measured over 200 seeds
each, the five 10-puzzle configurations (base, pack size 1, 20 skips,
Seeing Stars only, both DLCs) all filled: 0 refused, 0 fill failures, never
more than one draw. The release gate still plays 15.

### DevTools boot undoes the game's own pause

Three gate runs on 2026-09-24 stopped receiving game events for good, each
with the clock found at 0 in the middle of a level. What caused it is not
known. Measured on one level: the game's own `GameManager.Pause(true)` holds
every gameplay event, and the clock reset `boot` did never released them.
`boot` now calls `Pause(false)` first. DevTools logs every `Pause` call and
focus change (`game:` lines) and every change of the clock (`time: timeScale
changed`), so the next occurrence shows when and in what state; it cannot
name the caller, because IL2CPP's stack walk returns no frames here.

Measured since: going to the title pauses the game on its own (the pause
menu's Exit, `replayselect` then `menu:title`), and the next `boot` finds it
paused and undoes it. Neither stop reproduced in eight tries (both
`--only-arrow` sessions, three replays of the 22:31 stop, three of the 14:43
cat-trap stop). The release gate now WARNS, without failing, for every solve
it sends while the game is paused or its clock is at 0, and keeps the arrow
session's log (`testserver/logs/e2e-<stamp>-arrow.log`), which the main run's
launch used to delete. The next DLC gate caught one with its warning: a cat
trap's reset paused the game, four solves were held about 8 s, and the game's
own unpause released them.

### Every level measured alone, and the gate plans from the measurements

droha, 2026-09-24: "shouldn't we just run every single level now and build a
plan around them? that way we don't have to 'figure them out' on a gate
run?" `tools/probe-forceable.py --all` booted all 173 levels alone with
DevTools and forced each the way the harness does; `--recheck` re-forced,
pass after pass in a fresh game, every level that did not finish;
`--groups-rest` forced every group of every multi-group level alone and
recorded the controllers' order. The results are
`fixtures/forceability.jsonl`, and `release_e2e.py` builds its per-level
facts from it:

- 8 levels forcing never finishes are Skip-only: DLC2 Boss, Combs, Ghost
  Cat and Math Set, PawPrints, Record Player, plus Desktop Computer and
  TupperwareTower, which already were.
- 15 levels finish on one group, several on any of two or three
  (`KNOWN_COMPLETES_ON`). The paper plan now forces groups in controller
  order and stops where the game would: a group completing within a second
  strands the groups after it, and every part not solved by the completion
  waits for a Skip, since a revisit forces nothing.
- A per-level completion wait (`completion_wait`): 30 s, and 64 s for DLC2
  Curtains, which completes 49 s after it is forced.
- DLC2 Boss joins `KNOWN_TABLE_GAPS`.

The first pass had to be redone from level 15: the probe went back to the
title with a bare `menu:title`, and after 14 completions the game's own win
check threw on every level - the unwind `to_title` exists to avoid. The
recheck also caught one false "did not complete" (TrickOrTidy_Bats, a
transient error) and two staged levels a single pass cannot finish
(Place Setting, TupperwareNesting). `test_scheduler.py` fails if a list
disagrees with the fixture. The base gate's seed plans visit for visit as
before.

### Fruit Stickers could not be peeled holding Tidying alone

The full base gate stopped itself at visit 14 of 32: its paper plan had
collected `Fruit Stickers - Remove Stickers` holding Tidying, while the live
run found the stickers greyed out (`dimmed=12`, although the mod's summary
said "1 locked, 2 open, waiting on Sticking"). droha, holding only Tidying:
"stickers are greyed out and i can't peel them". So the part asked for less
than the player needs, and a seed could have put Sticking behind it. Remove
Stickers now depends on Match Stickers as well; the mutual pair is one group
needing Tidying + Sticking, the level has no part checks, and every base id
after them moved down by 2 (base 425 -> 423, id golden re-pinned). A scan of
101 kept game logs (40 levels caught part-locked) found no other open group
the lock greys. Only this level holds both a Pluckables and a Stickables
group.

The same stop showed the seed walk calling valid seeds "a generation bug":
`completion_plan` modelled requirements from names.json, which does not
subtract `bypassedAbilities`, so Workbench's Solution asked for Drawer.
Seeds 20260908 and 20260911 clear on their own requirements. It now reads
the seed's requirements, as `paper_run` and the mod do.

The next gate stopped at visit 11 of 21 on Radial Dance Party, which
registers no controller until a player starts a dance: the harness read
"controllers: 0 registered" and collected nothing where its paper plan had
forced the dances. It is now `KNOWN_NOTHING_TO_FORCE`, every location on it
is unforceable on paper, and a seed holding it is passed over. And the Skip
pre-flight counted a Skip on an alternate solution as a need (releasing a
Skip with a Skip gains nothing) and ignored a Skip on an unforceable level's
part check, which forcing does collect; seed 20260911 read 3 needed, 2 held,
where its paper plan clears with 2 of 3.

The third stopped at visit 20 of 24, the run AHEAD of its plan: Paper Plane
Supplies' Chalk DraggablesOrdered is locked (Ordering) but cannot go grey
(no renderer, every object shared with the open drawer), so the harness
forced it without Ordering and beat the level five visits early. The table
overstates it, which is safe. `solve_level` now also refuses a group the
seed's logic has not reached (`table_gated`), so the live run forces what
the paper plan does; a group greyed although the table calls it free still
stops the gate.

The DLC gate then stopped at visit 15 of 18: DLC2 Corn registers only a
Pannables Controller, which solve cannot force, so the live run spent a
Skip on it at visit 1 while the paper plan had forced it and came back at
visit 15. Levels whose every controller is scenery (`SCENERY_ONLY`,
derived from the table: DLC2 Corn, Drink Glasses, MerryMess_Presents) now
count as finished only by a Skip, on paper and in the verdict. The yaml's
starting Skips still follow `KNOWN_UNFORCEABLE`, so no seed moved; the
base gate's plan is visit for visit the one that passed 28/28.

Its rerun beat 15/15 and still never opened the credits: the Credits item
sat on Books Stacked (Seeing Stars) - Solution 2, an alternate solution
forcing never makes, and the paper plan had stopped at all-beaten on the
belief that the credits open with the last token. A seed now clears on
paper only if the run also collects the Credits item
(`credits_left_behind`); DLC seed 20260906 is passed over for 20260907.

That seed's arrow check then failed on DLC2 Broken Vases. Measured alone
with DevTools, no seed: forcing its Draggables completes it, but
LevelComplete comes 13 s later (its Pannables (Action Only) plays first),
and the harness waited 6 s. `COMPLETION_WAIT` is now 20 s; the DLC arrow
check alone then passed on it.

The run after that got ahead of its plan at visit 14: DLC2 Cupcakes
completes on its Colors group alone (droha's play said so on 2026-09-23),
the mod withholding Solution 1 until Swapping and banking the Beaten token,
while the paper plan's Beaten asked for Swapping. `KNOWN_COMPLETES_ON`
records it, and DLC1 Boss, whose later stages never register when forced,
joins `KNOWN_TABLE_GAPS`.

Every one of those stops was a fact about one level that a full gate found
one run at a time, although the seed, and so every level it visits, is
known before the gate starts. `tools/probe-forceable.py` now boots each
level of the chosen seed alone and reports where it disagrees with the
paper plan, and it is part of the pre-flight.

### Ability locks react to events instead of polling every second

An ability arriving now frees its objects on the frame it lands
(`AbilityState.Version` moves; a reconnect replay does not). Re-dimming after
the game rebuilds objects runs on its own events (drawer changed, phase
entered, level reset, randomized, controller changes, transition finished)
instead of a once-a-second pass. Each level logs which of those fired.

Played on Radial Dance Party, the level the old pass broke (2026-09-24,
droha): holding nothing, the pencils were greyed and could not be moved;
Rotating granted mid-level lit them at once ("0 locked, 1 open"), they
solved, and the Cat Toys dance followed normally.

### The DLC release gate could not generate a seed

Its yaml excluded 14 container locations that are now `notALocation`, and
Generate.py rejects a yaml naming an unknown location. The list is gone, and
`tools/test_scheduler.py` fails if an excluded name stops being a location.

### A Skip is spent only when the game actually skips

The mod charged a Skip before the game tried, so on a generator level -
where the game's own SkipLevel does nothing (measured on Pencils
(Randomized), beaten and not) - the player lost it for nothing. It now
charges only once the game has skipped: the completion and payout happen
inside SkipLevel, so the charge follows them. When nothing happened it
spends nothing and says "This puzzle can't be skipped - the Skip was not
used", and clears the skip flag, which left set would have paid the next real
completion out as a skip. Normal puzzles unchanged (Filing Cabinet: paid out).

### The release gate checks its seed on paper and stops when reality differs

It plays each candidate seed on paper with its own scheduler and takes the
first that clears every slot, naming the blocker of any it passes over; it
then reports every visit against that plan (`[visit 4/24 | 4/15 beaten]`)
and stops loudly at the first one that differs, at a Skip about to land on
the wrong level, or at a Skip the game could not use. It no longer forces a beaten level
before spending a Skip on it (that re-completed Pencils and the Skip landed
on Fruit Stickers), and it carries the arrow session's checks into the run
(it had revisited Stamps for nothing). A pass that reveals a progressive
level's next phase no longer spends the five-pass budget: TupperwareNesting
shows one controller per phase, needs eight passes, and was given up on at
Large Square. The arrow check now starts on an open slot it can finish. The
table audit reads the mod's `(at N controller(s) so far)` wording, so a
phased level on the known list no longer fails the run, and a part that
forcing cannot collect because the level completes first
(`KNOWN_EARLY_COMPLETE`) waits for a Skip; its one entry, Workbench's
Draggables For Targets, is now `notALocation` (below). The round loop
now reads the log before deciding to stop, and waits for the last token's
credits, instead of starting one more revisit on a stale flag.

### Workbench's Draggables For Targets check never fired

droha played Workbench on 2026-09-24: finishing it normally sends Tools and
Solution 1, never Draggables For Targets (it shares Tools' 21 objects and the
level completes first). It is `notALocation`, so Workbench is a single-part
level and its Tools check, the same event as Solution 1, goes too. Two fewer
base locations: every base id after them moved down by 2 (goldens and counts
re-pinned). The level still requires what it did.

The same session proved DLC2 Music Box by play: holding only Ordering, both
its parts fired and the level completed (`data/proven-requirements.json`).
`tools/handtest-level.py --grant <Ability>` now grants an ability to a
running hand test instead of a new seed.

### The full release gate could be handed a seed it cannot clear

It stalled at 12/15: TupperwareTower and Desktop Computer only finish by a
Skip, Symmetry sat on Pencils Solution 2 (also Skip-only for the harness), and
the pool held two Skips. The base yaml now starts with one Skip per
`KNOWN_UNFORCEABLE` level, and the Skip balance is printed at step 4 (the
refusal itself is the paper plan, above). The pre-flight also stopped judging
a `--quick` (locks off) seed by ability requirements, which had planned 3 of
15.

### A part location could ask for LESS than the puzzle physically needs

droha's DLC run ended on 2026-09-21: `Tupperware Nesting - Lids` declared
`['Containers']` and held a Progressive Puzzle Pack, but the lids cannot be
placed until the tupperware is nested (Stacking). Understating a requirement
softlocks a seed; overstating is safe.

It enters at one place. `levels.json` `controllers[].dependsOn` is harvested
from the game, so it cannot record a dependency the game does not express as
one, and everything downstream trusts it. This is a regression of the 0.3.0
drawer fix, which was applied as a hand-written 8-entry set across 4 base
levels and never generalised.

**53 dependsOn edges across 19 levels, none removed** - every change tightens.
38 are drawer levels; the rest came from droha playing Tool Drawer, Bathroom
Drawer, Paper Plane Supplies, Ink Bottles, Water Glasses, Music Box, Clock
Cupboard, Robots and Tupperware Nesting. The probes only chose which level to
play: three of four new detectors gave confident wrong answers that day.

### Requirements nobody has proven cannot hold progression

A part location whose abilities are a strict subset of its level's, in a level
with a stated structural reason to distrust it, may hold filler and never
progression. Implemented as `location.item_rule`; `LocationProgressType
.EXCLUDED` means "filler only" and broke five stress configurations.

The reason comes from five signals: a bespoke `levelClass`, a drawer whose
contents the table lacks, `extraAbilities`, a controller in neither the phase
list nor any dependency, or a hand entry in `data/proven-requirements.json`.
Guarding everything unproven was tried and was wrong - 183 of 358 locations,
leaving a 20-puzzle run two reachable openings out of twelve.

Writing correct edges can DELETE the guard, since closing a level's structural
gap makes its locations progression-eligible again (measured 114 down to 53),
so `add-edges.py` refuses to write unless the level is already listed suspect
or proven in the same change.

### The guard is no longer given back: the run is redrawn instead

**This changes generated seeds.** When a draw could not afford the whole guard,
`pool._affordable_guard` used to hand guarded locations back to the fill and
log it. Measured over 40 seeds a configuration, that happened on 35 to 40 of
every 40 eight-puzzle seeds, and on about 1 in 4 base-game seeds even at the
default 70. Progression then landed on a given-back location 108 to 212 times
per 40 small seeds, `Tupperware Nesting - Lids` 20 times, once the Credits.
No test saw it, because the only check read the guard that was KEPT.

Now `pool.decide` redraws the run (up to `DRAW_ATTEMPTS = 25`, from the same
`world.random`, so seeds stay reproducible) until nothing is given back, and
raises `OptionError` naming the fix if no draw is clean. A clean first draw is
unchanged. Over 1600 generations of small configurations: 0 refusals, at most
13 attempts.

**`puzzle_count` now starts at 15, not 8** (10 since, above). Base-game runs
of 8 or 10 puzzles gave guards back on 40 of 40 draws, so no number of redraws
could save them. The release gate and the probe tools moved to 15; the
`tiny run` stress configurations became `small run`; the frozen draw golden
now pins each seed's first draw and lists the two retired 8-puzzle
configurations by name.
`test_unproven.TestTheGuardIsNotGivenBack` checks every guard the table asks
for, and failed on the old behaviour before passing on this one.

### The opening stops counting checks the fill may not use

Most redraws came from the opening floor, and most of those from one level:
Medicine Cabinet's eight no-ability parts are all guarded, but
`pool._free_checks` counted them, so the grant loop saw a full opening and
granted nothing. It now skips guarded parts. First-draw redraws, 100 seeds a
setup:

| | 15 base | 15 both DLCs | 20 base | 70 base |
|---|---|---|---|---|
| before | 64% | 39% | 38% | 23% |
| after | 18% | 8% | 7% | 13% |

Two frozen draw entries moved on purpose (`short run` seeds 20260902 and
20260903, a guarded opening level swapped for a free one) and were listed by
name in `test_regression.MOVED_BY_DESIGN`, which asserts they still differ.
They moved back when Medicine Cabinet was proven (below), so it is empty again.
`TestTheOpeningCanAbsorbTheFirstItems` now checks the search the grant loop
really runs (one or two abilities); 23 openings in its sweep sit one check
short and need three or four abilities for a single Solution, and all fill.

### An audit of every part that asks for less than its level

82 part locations across 30 levels asked for less than their level and carried
no guard. 28 of those levels were first guarded (`suspect` entries in
`data/proven-requirements.json`, each with its reason; all since proven by
play, below) instead of being hand-tested: guarding all of them was measured
to cost a few points of
first-draw redraws, no refusals and no fill failures, where testing them meant
a game restart per ability set. The two left unguarded, Cleaning Supplies and
Fruit Stickers, were already settled; `test_unproven.UNGUARDED_UNDERSTATED`
pins exactly those so a new one fails a test instead of reaching a seed.

Two levels are now proven by play (2026-09-23): Medicine Cabinet, four runs
holding one set each and never Drawer, which takes 15-puzzle base redraws from
about 27% to 0%; and SomethingEggstra Fridge, which finishes holding only
Containers, so it does not need Stacking.

**Tupperware Nesting - Lids still asked for too little**, found by the same
runs (2026-09-23). Holding Containers+Stacking the lids stayed game-blocked
after every stack; holding Grids+Stacking "the lids showed up finally" only
after the food. `Lids` now depends on `Food` and needs Containers + Grids +
Stacking, the whole level, so the check that ended the 2026-09-21 run can no
longer understate. Its test pin now asserts that. The same runs showed
`(Large Square)` finishing without Grids - an overstatement, which is safe and
left alone.

With all of it in, first-draw redraws are 0% (15 base), 9% (15 both DLCs), 0%
(20 base) and 1% (70 base), from 64%, 39%, 38% and 23% at the start of the day.

### 36 guarded levels figured out by play, not left guarded

droha, 2026-09-23: "i don't want the guarded, that's stupid. and a bandaid...
i want them figured out". So every guarded level was played once with ability
locks OFF while `tools/record-unlocks.py` logged, per group, when it first
became movable and when it was solved. A group movable from the start needs
nothing else, whatever order it is solved in; a group stuck until another is
done depends on it. `tools/analyse-unlocks.py` compares that with the table.

- **54 levels are now proven** (`proven` in `data/proven-requirements.json`,
  each with its evidence) and **no level is guarded any more**: every part
  check can hold real items. Only Books (Randomized) stays listed as suspect,
  and it has no part that asks for less than its level.
- **7 edges added**, all tightening: Wilting Flowers' Cleanable after Upright;
  Mirror's skull into the stacking group's box (opened by "the latch in the
  mirror"); Tea Cabinet's teacups, cupcake and spoon behind the cupboard doors;
  Sewing Box's Large Spools and Daggers' drawer contents behind their drawers.
- **Cat Eyes corrected by two locks-on runs**: holding only Ordering nothing
  could be picked up; holding only Gadgets both groups finished. The eyes now
  follow the pieces (edge reversed by hand) and Ordering is bypassed.
- **DLC2 Boss rebuilt from play**: its Drawer Controller never solved, even in
  a full completion, and is removed; Locks, Compass and Knives registered and
  solved, so they are restored as checks in the order played. **DLC2 location
  ids moved** (267 -> 269); droha: "i do not care about seed ids moving ever".
- **Record Player, TupperwareTower and Bells** (one group each, so their
  Solution is the only check) were each finished holding exactly the table's
  requirement, with locks on.
- **First-draw redraws: 0 of 100** at 15, 20 and 70 puzzles, base and DLC.
- **Seven more locks-on runs** (`handtest-level.py`, holding exactly the
  table's requirement): Markers, Whistles, PawPrints (all five groups fire; the
  leaves and the spill must be done before the last paw prints, or on a
  replay), Radial Dance Party and DLC1 Boss all complete. Fruit Stickers'
  Match needs Tidying too (peel, then stick) and now depends on Remove.
  **DLC1 Boss** gained Dining Room, Parking Lot and Landscape, which register
  and solve in that order before the keys (DLC1 ids 146 -> 149).

### No check for opening a level, and none that can never be sent

Seventeen parts were sent the moment their level opened, because the game
reports them solved at load: every drawer or cupboard-door controller in
Drawer Chores and both DLCs (13 levels), Medicine Cabinet's Jar Lid and DLC2
Robots' spring pair. Measured by the new `tools/probe-solved-at-load.py`, which
boots each multi-part level with DevTools and reads `locks`. Two drawers had
the opposite problem and never solve, found by droha playing: DLC1 Kitchen
Utensils Drawers (the level ends before both can be shut) and DLC2 Junk Drawer
Transforming ("i can't place the last piece if the drawer is closed"); and
DLC2 Ink Bottles' GridPuzzleBase never fires on either solution. DevTools now
logs `PartSolved id=... part=...` for every part the game marks solved, so a
hand test sees which checks can fire without the level being in a seed.

A controller can now carry `"notALocation": true` in levels.json. It mints no
part, but its ability still reaches every group that depends on it and still
counts for the level, so no requirement dropped (checked part by part against
HEAD). Slot data gains `not_locations` so the mod's registration audit does not
call them unknown. Locations: base 431 -> 427 (425 after Workbench, above),
DLC1 149 -> 138, DLC2 269 -> 260; every id after them moved and the 0.3.4 id
golden was re-pinned with the reason. Kitchen Utensils Drawers, Junk Drawer
Transforming and Ink Bottles are proven (64 proven levels; 65 with Music Box).

### The ability locks made Radial Dance Party unwinnable

Holding Rotating, the only ability it needs, the Cat Toys dance loaded and its
toys vanished; with locks off it played to the end. `AbilityLocks.Paint`
recorded each object's colour the first time it saw it - for toys fading in,
transparent - and re-asserted that colour on EVERY object once a second,
unlocked ones included. It now restores only what it greyed out, once, when
the lock lifts, and records a mid-fade colour as opaque. Verified: the same
run completed at 21:13:07. The once-a-second pass itself has since been
replaced by event hooks (the first entry in this section).
- DevTools `boot` now lifts a Seeing Stars star gate in memory, as the mod's
  track does for run slots; a locked level used to load a chapter header.
- Found on the way: a DevTools `boot` of Game Pieces puts the heart on the
  board where it cannot be moved - loaded from the game's own menu it works.
  A boot after a finished level found the game clock paused (`timeScale 0`),
  which froze win animations; `boot` now resets it.

### Hand-test tooling

- `release_e2e.write_config` rewrote the whole mod config and wiped the
  remembered window size at every harness setup, so the next launch came up
  at 3840x2160. It now sets only its own keys (`set_cfg_keys`), and sets 720p
  only when no size is remembered. The devtools config writer too.
- `handtest-level.py` gains `--boot` (for a game the player opened: 720p, then
  the level), `--keep-game` (switch seeds from the mod's pane, no restart), a
  seed per held set so each test has its own save, a check that the seed
  grants no ability beyond the request (the Fridge seed had granted
  Stacking), and a live-level count after every boot that refuses to hand
  over anything but exactly one level.
- DevTools `boot` left a level alive when the player had exited it to the
  title: it tore down the interface, but the `Level` object stayed in the
  scene, and the next boot drew a second level on top. It now destroys any
  such leftover and refuses to boot if one survives. Verified on the case that
  broke: 1 leftover at the title, 1 level after the boot, a solve completes.

### A beaten card no longer reads the same as one never opened

`SlotStatus.Beaten`: beaten, still owing checks, none reachable. Candy Canes
read solid red after droha finished it, because its remaining checks need
Ordering. The card is no longer veiled. It first drew red with the star over
it; droha then ruled the star means everything is done ("it should just be
red, red/green, green, yellow star; no overlapping"), so it is plain red.
`Complete` still wins; a beaten card with reachable work left stays Doable or
Mixed; a slot with no Beaten location falls through to `Locked`.

### Close on the run's track after a DLC puzzle never left it

droha, watching the DLC gate on 2026-09-24: "there was 2 level selects open at
the same time". After a DLC puzzle the game opens that DLC's own level select
and `DlcGuard` swaps in the run's track. The track's Close went back to the
DLC menu, where the guard opened the track again: one reopen per press in
every kept DLC gate log, so Close never left, and the log shows the DLC menu
and a second track built in between. After a base-game puzzle the same Close
goes back to the completion screen (Cat Frame, measured the same session).
Close from the run's track now goes to the title; checked in game twice.

Still open: the post-level route builds the DLC menu for a moment before the
guard leaves it. `ReplayMenu.LevelSelect` reads `IsDLCLevel` itself (new
DevTools `xrefs:<Type>.<Method>`, which lists what a game method calls); a
`GoToLevelSelectForLevel` prefix and a `ContextualState` postfix both
installed and never ran on that route, so neither shipped.

### The empty completion star: narrowed, NOT fixed

Four theories dead by measurement: not the level's `source`, not the daily
pool, not `solutionCount`, and not `LevelInterface.CompleteLevel()`, which
writes nothing to the save. `tools/probe-star.py` solves every controller
through the game's own dispatcher and dumps the save:

| source | solution banked | solved / completed |
|---|---|---|
| archive (MerryMess_Crackers) | yes | true |
| base (Mirror) | yes | false |
| generator (Breadtags) | no | false |

Generators recording nothing is a lead, but the base row does not match what
droha sees, so no conclusion is drawn. Settling it needs one real play of a
generator level and a base level.

Measured 2026-09-24 with DevTools `xrefs`: a card's stars come from its save
row (`LevelIcon.SetCompletionStars` reads `GetLevelCompletionData` and sets
one toggle per star), while `LevelInterface.Solved` only checks a string the
loaded level holds, so the `solved` column above measured the wrong thing (Seed
Pods reads solved=True in the level and False back on the level select). In a
run's save, base puzzles record their solution (Seed Pods 1 of 1, Fruit
Stickers 1 of 2) and generator puzzles record none (Stamps (Randomized) 0
after completing it; every generator in droha's hand-test run 0). Still
unknown: why a base card whose row holds its solution shows no filled star.
droha's rule for the fix: the stars start empty for each seed and fill as its
solutions are completed, never synced with the player's own save.

`TrackCommands.DumpUnlocks` filtered to `index < 100` as "base campaign only",
which hid every level in the report - Procedural Grid is 1000, the randomized
puzzles 995 to 999, DLC 1100 up. It now dumps all levels and reports which
save store holds each row and how many solutions it has.

### Harness defects found by using it

- `SceneCommands.Freeze` walked `ManagedObjects` and disabled 1 collider on a
  controller holding 56. Fixed to `AllObjects`.
- `LevelCommands.Boot` tore down only levels active in the hierarchy, so a
  level exited through the menu stayed built and the next boot stacked on it.
- `playerPrefs.resolution` is a position in a per-monitor list, so the same
  number is a different size on each display. Tests now use `setres:1280x720`.

### Both DLCs, each behind its own toggle

`cupboards_and_drawers` and `seeing_stars`, **both off by default**, each with
its own weight beside the generator, archive and campaign ones. Turning one on
adds its puzzles to the draw; leaving both off is byte-for-byte the run 0.3.4
produced.

| | Cupboards and Drawers | Seeing Stars |
|---|---:|---:|
| puzzles | 25 | 37 |
| solutions | 32 | 100 |

The level table goes from 111 levels and 162 solutions to **173 and 294**, and
the location table from 432 to 845.

**No id moved.** Every one of the 432 location ids and 18 item ids from 0.3.4
still means exactly what it meant, which is checked rather than asserted:
`fixtures/id-table-0.3.4.json` is a frozen record of the pre-DLC tables and
`test_regression.py` holds the current ones against it. Two orderings make that
true - the Credits location is now emitted BEFORE the DLC block rather than
after every level, and DLC ability items are appended after Hint Page instead
of into the middle of the ability list. `fixtures/plan-0.3.4.json` pins the
same thing for the draw: with both toggles off, 114 configuration/seed pairs
draw byte-identical runs.

### What the DLCs turned out to need

- **A `dlc` field on each level, separate from `source`.** Four DLC levels
  carry the game's own randomizer flag - Trophy Cabinet, Water Glasses,
  Figurines and Bread Crusts - so they are generators and repeat with a fresh
  seed. Their source says `generator`, which means source alone cannot answer
  "does this need a DLC".
- **The scarce-mechanic list is now computed per yaml.** Trophy Cabinet is a
  drawer generator and Bread Crusts a jigsaw one, so a list derived from the
  whole catalogue would have dropped Drawer and Jigsaw for *every* player,
  including one who owns no DLC - quietly removing the guarantee
  `mechanic_coverage` exists to make.
- **One new ability item, Distributing**, for DLC2 Pizza. The other four new
  controller classes belong to abilities that already exist.
- **The five star-gated Seeing Stars puzzles are opened by the mod.** The game
  locks them behind 50 to 90 solution stars, a total a run never touches.

### Two bugs the DLC exposed in the sweep

- A numeric field that threw was written to the level table unquoted, as
  `<err:NullReferenceException>`, which made the whole 173-level file
  unparseable. One field cost every row.
- A level that never loaded still got a row, filled entirely with fallback
  values - an id of `?`, zero counts, no controllers. Five such rows were
  written and they looked like data. They came from the star-gated levels,
  which do not fail loudly: asking for a locked level does not throw, it
  silently redirects to the DLC level select.

### A guard that was never installed, and the comments written about it

`DlcGuard` shipped with two Harmony prefixes and was never added to the list
of classes `Plugin` patches, so **neither was ever applied**. Only its `Tick`,
which `Update` calls directly, ever ran - which is why the guard did work, and
why the measurements of it were real. What was not real was the explanation:
the file recorded that its `SetGameState` prefix "sees NOTHING on the DLC
route ... so every one of them came through the generic overload", which reads
like a measurement and was a story about a patch that had never been
installed. Two later comments were written on top of it.

The game said so at every launch and nothing read the line:

    features live: save redirect, connection pane, track, skips, hints,
                   navigation, daily guard, title screen

Eight names, and `dlc guard` was not among them. Adding it then produced
`PATCH FAILED, dlc guard IS DISABLED: IL Compile Error` and took the whole
class down, so the prefix is deleted rather than reinstated.

Two checks now cover it, and both are needed because each sees a failure the
other cannot: `tools/check-patches.py` reads the source and fails when a
Harmony class is never registered or a `Tick` is never called, and the release
gate reads the launch and fails on `PATCH FAILED` / a missing feature - a
class can be registered, carry attributes, and still be refused at runtime.
Every probe now makes the runtime check before it measures anything.

### After a DLC puzzle, two level selects rendered at once

The post-level routes send a DLC puzzle to its own DLC menu. `DlcGuard` caught
that and switched the state to `Levels_GameState`, which every log assertion
read as correct - and switching state does not close a menu the game has
already built. Both level selects drew on top of each other: the DLC's title
and its `1/17 (6%)` header over the run's track, two progress strips, two sets
of cards, with the finished level still loaded. The state was right and the
screen was wrong, and it took a screenshot to see it.

`DlcGuard.Open` now asks the game's own `GoToLevelSelectForLevel` for a
campaign level instead of switching state underneath. That routine does the
teardown, the transition and the menu setup - `Navigation`'s pause-menu route
already relied on exactly that. Measured over a full DLC gate run: the guard
fired fourteen times, with one track build per navigation instead of two, no
`card at position ... but the plan covers` warnings, and no exceptions.

### A Skip could be spent on a puzzle with nothing left to find

It was taken, written to disk with no refund path, and granted nothing -
`skip: spent one, 0 left` followed by `sent 0 remaining location(s)`.
`Skips.BeforeSkipLevel` now refuses instead, and says so.

The related claim that a Skip on an ALREADY-BEATEN puzzle grants nothing,
recorded in `release_e2e.py` and `manual-container-test.md`, is **false** and
both have been corrected. Re-entering a beaten puzzle reloads it, and a
reloaded level completes and skips like any other; measured against a pre-fix
build, skipping a beaten DLC1 Filing Cabinet sent Solutions 2 and 3. The
premise had to be wrong, because the only way to press Skip is to be standing
in a loaded level.

### Repeated generator instances were never broken

Carried as a known bug: "`#2` checks have never been earned in any run". The
evidence was that no `check:` line in any saved log contained a `#` - and every
one of those runs came from a gate yaml with `generator_weight: 0`, so no level
ever repeated in any of them. Absence of a second instance, not absence of a
payout. Measured against an unmodified build, a single-solution generator drawn
twice pays out both `<Level> - Solution 1` and `<Level> #2 - Solution 1`. Two
candidate fixes were written, measured to change nothing, and reverted rather
than shipped on a theory. `tools/probe-instance-checks.py` now pins the
behaviour.

### New probes

`probe-skip-beaten.py`, `probe-instance-checks.py` and `probe-dlc-nav.py`.
The last photographs the screen at each stage, because every log assertion it
makes passed while the game was visibly showing two level selects at once.

## 0.3.4 - 2026-09-16

**Location ids did NOT move.** Verified by building both id tables and
comparing them: 18 items and 432 locations, byte-identical to 0.3.3. A seed
generated with the 0.3.3 apworld still plays on this mod. Nothing needs
regenerating.

Two batches: an audit of the documentation, and the refactor it deferred.


A second audit, asked for in the same spirit as the one 0.3.3 shipped: is the
documentation true, is the project laid out sensibly, and is there dead code or
rotted commentary left behind. The same defect pattern turned up again - **a
correct fix applied to one of several copies** - five more times.

### The setup guide on archipelago.gg said the randomizer did not work

The worst of it, and public for the whole life of the project. The Multiworld
Setup Guide the webhost serves for this game opened with "**This randomizer is
not finished** ... there is nothing to connect with yet". It was written
2026-09-02 and never touched again; 0.3.0 shipped four days later.

Two more errors in the same file. It said the mod release includes BepInEx,
which it does not, and then never told the player to install BepInEx at all -
so following it exactly produced a mod that could not load, which is the exact
symptom its own troubleshooting section describes. And it still described packs
as widening as the run goes on, which 0.3.2 removed.

Nothing caught any of it because **nothing in this repo validated a single .md
file**, and Archipelago's own compliance suite only asserts that the tutorial
file exists, never what is in it. A wrong document passed CI, passed
compliance, and passed the 23/23 release gate.

`tools/check-docs.py` is the answer, and it runs in CI. It checks that every
relative markdown link resolves, that no member carries two `<summary>`
elements, that no comment cites a file by line number, and that prose naming a
version agrees with the manifest. Each of the four was proved to fail on a
deliberately broken tree before being trusted.

### "Packs widen as the run goes on" had four more copies

0.3.2 made packs uniform and fixed the player yaml and the game page. It missed
the setup guide, `items.PROGRESSIVE_PACK`'s docstring - which contradicted
`pack_boundaries` 180 lines below it in the same file - `rules.packs_needed`,
and a test named `test_packs_open_puzzles_in_strictly_growing_blocks` whose
assertions passed under both the old ramp and the uniform layout. That test
pinned neither design; it is now `test_every_pack_is_the_same_size`, plus one
that spells out the default layout of five free and thirteen packs of five.

### One real bug: "Reconnecting, attempt 4 of 0"

Found while checking a comment that described it in the past tense.
`Plugin.AttemptLabel()` guards the unlimited case; `RetryPolicy.Describe` is
the same string and was never fixed with it. The default policy IS unlimited,
so a stock install could show that line in the connection pane. Every existing
test passed a real attempt limit, which is why the one configuration every
player has was the one never covered.

### Comments that pointed at the wrong thing

Twenty-five members carried two `<summary>` elements - invalid doc XML, where
only the first binds. The cause is mechanical: a member moves and its doc
comment stays behind on whatever is now underneath. `Problems()` had lost its
docstring to `VersionMismatch()`, added by the previous audit;
`CampaignSelect()` carried two contradictory one-liners; `AddMenuIndicator()`
carried a description of an approach that the very next summary said had been
tried twice and rejected.

Three of seven `file:line` citations pointed at unrelated content, two of them
into an append-only 2,910-line log where the cited ranges had drifted to other
subjects entirely. The form is now banned rather than corrected, because
correcting the numbers only restarts the clock.

### Dead code, and two second opinions

`ALTTLArchipelago.Core.HintText`, `Chapters` and `MechanicCoverage` were C#
reimplementations of logic that actually ships in `pool.py` and `slots.py`,
reachable only from their own tests - and both pairs had **diverged**, which
makes them worse than dead: a second, wrong answer sitting next to the right
one. Deleted, with the hint-position assertions moved to the Python side where
they now pin the exact text rather than just its prefix.

Also removed: `Checks.LeaveSlot`, `SaveRedirect.IsRedirected`,
`ItemNames.IsSpecial`, `data.BASELINE`, `data.BASE`, `items.EVENT_ITEMS`, an
unread parameter on `TitleScreen.Show`, and six unused imports. There are no
TODO, FIXME, XXX or HACK markers anywhere, and no commented-out code; both were
checked exhaustively rather than assumed.

### Structure

`controller-survey.tsv` and `slot-data-example.json` moved out of `docs/data/`
into a new `fixtures/`. They are read by tests in BOTH languages - the Core
csproj reached two directories up into `docs/` for one of them - so editing
what looked like a document could turn the C# suite red.

Four spent probes deleted: their questions are settled and written up, and
`probe-tupperware-tower.py` printed verdicts its own docstring called wrong.
Their write-ups stay, and `credits-goal.md` gained the verdict it was missing -
it ended on a bare "no" describing a design that has since been replaced, which
read as an open bug.

The original design doc and the verification log are now labelled as history;
the first still said "the mod itself is not started". The four docs nothing
linked to are linked, `docs/devtools.md` gained the sixteen commands it was
missing, and the ASCII check no longer exempts `docs/data/`.

### And then the refactor the audit deferred

**The Core csproj had recorded a half-applied fix for two weeks.** It names
four Unity-free files left stranded in the plugin with no tests - the
connection, the inventory, the goal latch and the run state - and says two of
five bugs in a previous audit were in exactly that untested code. Two of the
four moved to Core. Two did not, and one of those, the run state, is where
0.3.3's worst bug lived.

`RunStateData` is in Core now with 20 tests, including one that reads a real
sidecar off disk written before `creditsPlayed` existed. `RunState` keeps the
path and the atomic write, the same split `SlotCache` already makes against
`CachedSession`.

`Connection` did NOT move, and the reason is worth keeping. It would have
compiled there - but reaching the part worth testing needs a live
`ArchipelagoSession`, whose nine helper properties all have to be stubbed
before one assertion can run. That buys compilation, not coverage. So the
DECISION came out instead: `ConnectGuard.Evaluate(slot, modVersion)` is now the
whole of what a successful login must pass, as a pure function, with 10 tests.
Both halves of that sequence had been added in separate releases for the same
reason each time - the guard existed and was skipped where it was
authoritative - and neither could have a test where it sat.

**Two files were several files.** `Badges.cs` (1849 lines) was seven unrelated
HUD overlays; `ALTTLDevTools/Plugin.cs` (4689) was a plugin and seventy
commands. Both split as PARTIAL CLASSES, so every name, accessibility and
static-field identity is unchanged and no caller needed editing - which matters
because CI cannot build either project and the release gate does not check
badges at all. Both verified line-for-line: 1609 in and out, 4121 in and out,
nothing lost or gained.

**Two DevTools commands could never run.** The dispatch ladder matches on
`StartsWith`, and `solve:` was claimed by one command 110 lines before a
different command asked for it - the second being the one `docs/devtools.md`
documented. It has its own token now. All 74 branches were checked; none is
shadowed.

**And the smaller ones.** The install path was declared in nine places and is
now in one, with `EXE` and `SCREEN_KEY` lifted with it rather than leaving half
the duplication behind. `release-e2e.py` and `offline-test.py` are libraries as
well as scripts, so they are `release_e2e.py` and `offline_test.py` and four
probes dropped their `importlib` blocks; two of those probes gained a
`__main__` guard, which they needed the moment they became importable - without
one, importing `probe-dead-controllers` launches the game. The plugin's
`Abilities` class shadowed `Core.Abilities` inside a file that imports it, and
is `AbilityLocks` now, which is what it does.

## 0.3.3 - 2026-09-16

**Location ids did NOT move**, unusually for this project - the tables and the
code that builds them are byte-identical to 0.3.2. A seed generated with the
0.3.2 apworld still plays on this mod: it carries no version field, so the new
pair check treats it as an unknown rather than a mismatch and allows it with a
warning. Nothing needs regenerating.

An audit droha asked for after the apworld manifest bug: "can you do a full
audit that we're not missing/half implementing other things like this?" The
bug had a shape - half of an external contract, checks that read the source
while the artifact shipped broken, a failure that only logged, and a declared
guarantee that silently did nothing - and the audit looked for all four.

### A run finished offline never reported its goal

The worst thing it found, and a regression from 0.3.2's own credits change.
Reporting the goal requires the credits to have been PLAYED, and that flag
lived only in GoalLatch - which is replaced wholesale on every reconnect and
every offline start. Finish the run offline, play the credits, reconnect: the
flag was gone, the goal was never sent, and the multiworld waited forever on a
slot that had genuinely finished. A relaunch between the two did the same.
Nothing logged it.

The flag is persisted in the run's sidecar file now. The latch also stopped
claiming it "closes on the acknowledgement" - the client library has no async
or callback form of SetGoalAchieved, so a send that left is all anyone knows.
What makes that safe is the persistence: each session re-owes the goal and
re-sends until one lands, and the server takes a repeat as idempotent.

Proved in a real game by `tools/probe-offline-goal.py`, which finishes a run,
kills the server, plays the credits offline, brings the server back and
watches the goal arrive.

### The mod refuses a seed built by a different apworld

`slot_data` carries `world_version` and the mod compares it with its own
assembly version at connect. Location ids move between releases, so a 0.3.1
mod on a 0.3.2 seed sent the wrong checks under the right names and said
nothing. check-version.py enforced the pact inside the repo, between commits,
and never between two installs.

Refused rather than warned: everything past that point builds a run on the
payload, and checks sent into other people's worlds cannot be walked back. A
seed too old to say which version made it is an unknown, not a mismatch, and
is allowed with a warning.

### The packaged apworld carries its container keys

The bug that started the audit. Archipelago's spec defines two manifests with
opposite rules - the source must not declare `version`/`compatible_version`,
the packaged archive must - and the packager copied the source verbatim, so
the artifact had neither. Every release since 0.3.0 shipped it.

It was not only a future problem. The failed parse left `minimum_ap_version`
unpopulated, and the loader's check is `if apworld.minimum_ap_version and ...`
- so the minimum version we declared was never enforced at all.

### Diagnostics that logged and did nothing now act

- the controller audit returned silently on a level with no controller_groups
  entry - the one case where NO group check can ever be collected. A level
  merely missing a few groups got the loud warning.
- `SlotData.Problems()` is "whether the payload is coherent enough to start a
  run on", and the offline start and cache write both refuse on it. The live
  connect path logged a warning and started anyway. It refuses now.
- `pack_size` defaulted to 4 against an option default of 5, and
  `cat_trap_chance` to 10 against 25, contradicting the invariant SlotData
  states about itself. The test that should have caught it was asserting the
  drifted values, so `tools/check-slot-defaults.py` reads both sides in CI.

### The release tooling checks the artifacts

`tools/check-release-assets.py` opens the three files a player downloads:
their four versions must agree, the apworld manifest must be readable, and the
yaml must parse and keep a `{number}` placeholder. `--expect` catches a stale
folder, which "they agree with each other" cannot.

The release gate runs it before installing. It defaulted to `release-test/`,
which holds the PREVIOUS release between releases, and green-lit 0.3.1's
artifacts twice during 0.3.2.

CI now generates from our own shipped yaml rather than Archipelago's template,
using two copies so the name placeholder is exercised, and asserts the
packaged world loads with no errors logged. `.apignore` makes Archipelago's
own packager produce the same archive ours does.

### Docs that contradicted the code

The game page said packs widen as you go (removed in 0.3.2), that the
multiworld holds a Hint Page for every page (the default is 50%, and the same
file said so seven lines earlier), and that the credits card needs only the
count. `docs/release-testing.md` gave a "features live" line missing `daily
guard`, so the string it told you to look for could never match.

## 0.3.2 - 2026-09-14

droha's 79-puzzle multiworld, and the playtest that followed it. **Location
ids move**, so a seed generated before this build will not match a mod built
after it.

### The fridge advertised work the puzzle never wanted

`Fridge (Something Eggstra)` had four locations and droha could only ever earn
two, leaving the card half red for good. It has one now, the Solution.

The level is an egg hunt and it ENDS when the carton is filled. Its other two
controllers - the shelf objects and the tupperware - are arrangements the game
does not ask for, so nothing ever solved them.

Two independent measurements, because the first thing I concluded here was
wrong. `tools/probe-dead-controllers.py` (new) force-solves every controller
and all three fire, so they are not dead in the sense TupperwareTower's
mechanism controllers were. Then `--only EggsContainable` solves the eggs and
NOTHING else, and the game raises `LevelComplete` regardless. droha's room log
agrees from the other side: `Solution 1` and `Eggs Containable` fired together
at 03:31 while holding both Containers and Stacking, and the other two were
never checked in the whole run.

**Stacking is deliberately NOT kept as an `extraAbility`** - contrast
TupperwareTower, where `Grids` is. The tupperware is dimmed without Stacking,
but the level finishes without touching it, so requiring it would hold this
level's only check behind an item the player never needs. The cross-check test
grew an `OptionalAbilities` list for exactly this shape, and its docstring
demands a measurement to join.

435 locations to 432.

### Packs are all the same size

droha: "the packs should all be the same size, the 4 minimum open just means
they have something to do in 4 levels at the start."

The free opening was pinned at `MIN_OPENING` while the packs widened past it,
so a default run opened 4 and then handed out 6 at a time. The floor applies
to the pack SIZE now and the opening is simply the first block, so every block
matches bar the remainder.

- `puzzle_count` 79 to **70**, `pack_size` 4 to **5**, `MIN_OPENING` 4 to 5.
  `levels_to_beat` stays 40. That is 70 + 13 dividers + credits = **84 cards**,
  one under the widest strip the game draws.
- `items.opening_size()` is now the single source of truth. `pool.py` and
  `slots.py` each had their own `max(pack_size, MIN_OPENING)`, which is wrong
  on any run long enough for the cap to widen the packs - they would have
  believed a 79-puzzle run opens 5 when it opens 6.

**Also fixed, and pre-existing:** the ability-granting loop is greedy one at a
time, so it stalled where an opening puzzle needs TWO abilities - neither pays
alone, so both looked worthless. It left openings a check short in 2 of 165
stress configurations. It looks one further now when a single grant stops
helping. Measured 2 before, **0 after**.

### Toasts stopped throwing messages away

droha: "I don't always see it showing my items being released/received."

Beyond five on screen the OLDEST was destroyed, often before it had rendered a
single frame - on screen, indistinguishable from never being raised. Finishing
a level can push six or more at once and a Skip reports every remaining
location on the slot.

Ten now, and overflow WAITS instead of dying; the hold drops from 8s to 2.5s
while a backlog exists so a burst clears rather than trickling. The eviction
loop is now a tripwire that warns if a future caller reaches `AddLine` without
checking for room.

Measured on MedicineCabinet's 13 parts: **5 toasts waited for space, 0 lost.**

Toasts already appeared bottom-left with the newest at the bottom and the
stack growing upward, so nothing moved.

### The level select strip fits the screen

79 puzzles builds 92 cards against a strip the game sizes for about 85, so it
ran off the edge. It is scaled to fit now, from a cached baseline so the
one-second poll cannot shrink it cumulatively.

The measurement that matters: the strip's own parent is content-sized and grew
with the dots - 2025 wide against a span of 2026 - so comparing against it
found no overflow and the first version of this silently did nothing. The room
is the NARROWEST ancestor, which is the real viewport at 1920. Measured: 92
dots span 2026 in 1920, scaled to 0.95, stable across repeated visits.

### The connection pane's mouse no longer sticks

droha: click once and the whole row highlights, then "moving the mouse around
highlights different things, like I'm dragging it", and a second click does
not put the caret where you clicked.

The scene's EventSystem carries the GAME's `RewiredStandaloneInputModule`, and
that module reads mouse buttons from Rewired's own `IMouseInputSource` rather
than `UnityEngine.Input`. `TypingGuard` switched off EVERY Rewired map while a
text box had focus, which blinded the module to the mouse RELEASE: its state
stayed pressed, so mouse movement kept sending drag events to the field and no
fresh press ever arrived.

The comment asserting "the mouse still works - it is Unity's pointer handling
that clicks the buttons, not Rewired's maps" was the load-bearing assumption
and it was false. Only the KEYBOARD's maps are switched off now, which is
where the cursor-drift bindings live.

**The first build of this fix did not work, and the log said it had.** The
lookup asked for `SetMapsEnabled(bool, ControllerType)`, which the map helper
does not have - its two-argument overloads take a category, and the
per-controller-type switch is `SetAllMapsEnabled(bool, ControllerType)`. So
the narrow bind failed, the code fell through to `SetAllMapsEnabled(bool)`,
and it went on disabling everything while printing "1 of them keyboard-only".
A whole round of testing was spent on the wrong conclusion. The log now prints
the bound SIGNATURE, because a label cannot be checked. Confirmed in play:
"the clicking and typing are working".

### The keyboard stopped driving the game from other windows

droha: "why is my keyboard still controlling the mouse in game while I'm not
focused on it? It often opens up the settings page and sometimes changes
settings."

`Application.isFocused` reported true with Notepad plainly in front - the
focus-change log never fired once, which can only happen if the value never
moved. Focus is asked of Windows now, via `GetForegroundWindow` and the owning
process id. It fails OPEN: if the call cannot be made it reports focused,
because suppressing input on a game that IS in front is worse than the bug.

### The cat trap no longer freezes the game

droha hit this three times, and the first report had no log left to read. It
is not a freeze - it is an exception storm. `Player.log` held **6,583**
identical `NullReferenceException`s in
`DragObject+<>c__DisplayClass94_0.<ObjectPlaced>b__1`, thrown from
`LeanTween.update`.

Dropping a piece starts a LeanTween settle tween whose callback closes over
the piece. Resetting the level destroys the piece while the tween is still
registered, so LeanTween calls a dead reference every frame, forever. The game
never hits it because nothing in the game resets a level mid-animation.

**Three fixes missed before this one, and each failed differently.**
`cancelAll()` stopped the exceptions and hung the load instead - the level
load is an async state machine that awaits its own tweens on `Main Camera` and
`Completion Stars`, so killing those meant `SetActiveLevel` never returned.
Cancelling the level's own objects left 4,638 exceptions; adding every
`DragObject` left 2,139. Dumping `LeanTween.tweens` ended the guessing:
`LeanTween.value` has no GameObject, so the tween is parked on an internal
`~LeanTween` holder and none of the three sweeps could ever have reached it.

`CancelDetached` takes those and leaves the load's own tweens alone. Verified
in play rather than by reasoning: three traps caught a live detached tween
with **zero** exceptions, alongside 42 clean resets.

Two smaller defects in the same moment. A trap landing just as a puzzle
finished relaunched the level that was on its way out and threw the queued
navigation away, so the run sat on a reset copy of the puzzle it had just
solved - that counts as a miss now. And the reset restored every object's own
colour, including ones an ability lock had dimmed, so locked pieces sat fully
lit until the once-a-second pass came round; `HoldDim` re-dims every frame for
half a second, which covers the rebuild.

### The cat trap holds itself while a level is loading

A second freeze, same trap, different cause - and this one was reported from
the released 0.3.1 build, which has none of the guards above.

The log ends like this:

    track: slot 53 Stamps (Randomized) launching with seed 307681145, forceReload
    trap: 1 cat(s) reset the puzzle
    <nothing>

`ActiveLevelInterface.Level` is already set partway through an async level
load, so a trap ticking in that window finds what looks like a perfectly good
puzzle and calls `ResetLevel` on it. The load is told to rebuild the level it
is still building. `SetActiveLevel` never returns, and the game stops - no
exception, no error, nothing to read. droha: "when finishing a level I got a
background change trap... it reset and when I clicked anywhere the game fully
froze. Had to alt+F4."

**Not the exception storm above.** That one drowned in 6,583
`NullReferenceException`s; all 1,549 lines of this log hold exactly one
exception, and it is a benign startup timeout. Both needed finding
separately.

A trap now asks the level what it is doing - `LevelIsLoaded` and
`IsTransitioning` - and **holds** rather than spending itself while a load is
in flight, springing on the first tick after the level settles. The completion
grace already in 0.3.2 does not cover this and could not: it measures time
since the last completion, and a level reloading for any other reason is not
one.

**Measured, with the guard deliberately removed to prove it matters.** The
freeze is a race that needs a click and resisted 29 direct attempts, so the
thing measured instead is the state it comes out of: two levels alive at once,
counted on every attempt. droha, watching an unguarded build: "oh god 2 levels
loaded at once", with a screenshot of one puzzle drawn through another.

Ten attempts per build, each finishing a real puzzle, with the trap delivered
by the server the moment the level is reported beaten and live settle tweens
held open across the window:

| Build | Clean | Two levels alive |
|---|---|---|
| guard off, grace off, `CancelAnimations` off - 0.3.1's `Spring` | 1 | **9** |
| the real build | 5 | 5 |
| the real build, no trap sent at all | 5 | 5 |

The unguarded build breaks on its FIRST trap and never recovers. The guarded
build's failures start after the harness abandons a half-solved
MedicineCabinet, and the third row is the control that proves it: with no trap
sent, the same build breaks in the same place the same way. On the real build
the trap never once reset a level inside a navigation - every attempt logged
`found nothing to knock over` rather than `reset the puzzle`.

**The hard freeze is still not reproduced.** The game kept running through all
of it, including four real clicks into the wreckage. Two live levels is a
state consistent with droha's hang, not a demonstration of it.
`docs/data/trap-freeze-repro.md` has the runs, the three wrong versions of the
measurement, and what is still untried.

Also measured: a trap arriving mid-load is now held and springs once the level
settles, where before it hit `level == null` and was silently spent - the
guard turns a lost trap into a fired one - and it cannot strand a trap on the
level select, because `ActiveLevelInterface` is null there.

### The background trap stopped strobing

Same report, same moment. "The background started kind of strobing/shifting
between multiple colors."

`Backgrounds.Tick` polls every frame and writes the camera back to the trap's
colour whenever it differs. That poll exists because the one-shot write kept
losing - the level's own setup runs after `StartLevel` returns and paints the
camera from its own colour - and outlasting the game is the right answer for
a settled level.

It is the wrong answer during a transition, which ANIMATES the backdrop and so
writes a new colour every frame. Two writers, sixty times a second, and the
player sees the flicker.

The poll now stands down while the level is loading or transitioning and picks
up the instant it settles, which is the only moment it was ever needed.

Measured afterwards, because "it writes every frame" deserved a number rather
than an argument: with a Background Change Trap held, ten seconds settled in a
puzzle is **zero** camera writes, a level load is zero, and a whole two-minute
session is **one**. The per-frame cost is a Color comparison; the write
happens about once per level, which is what it was always for.

### The vanilla title menu no longer flashes past

The Archipelago title appeared, then the original menu, then the Archipelago
one again. The menu was following the CONNECTION - hidden at startup, restored
when the first attempt failed, hidden again when the retry succeeded.

droha asked the question that settles it: when would we ever use the normal
menu with this mod installed? Only when you are not playing a multiworld,
which is exactly when there is no slot name. That is the whole condition now.

### Settings are shared between the campaign and a run

A run redirects every save write, `playerPrefs` included, so settings changed
while playing a multiworld landed in `save_ap_<slot>_<seed>.json` and the
campaign never saw them. droha: "the user settings should persist between
save1 and the Archipelago. They should be synced." They sync both ways now,
and only the `playerPrefs` key moves.

### The window opens at the size you chose

droha: "why is my game always opening in full screen mode? I set it to
windowed every time", and later "it's not remembering my selection".

It was never fullscreen. The game stores the display choice as
`Prefs.resolution`, an INDEX into a list `SettingsMenu` rebuilds from whichever
monitor it opened on, sorted largest first - so index 0 is that monitor's
native resolution, on any monitor. That index is applied at startup and
overrides Unity's own stored size: measured, with the registry holding
1280x720 and "use native" off, the game still opened 3840x2160. A windowed
window the size of the monitor is indistinguishable from fullscreen.

droha had already worked out why an index is the wrong thing to store: "the
game changes the resolution list depending on what monitor opened it, so a
number doesn't help me here." A save here held **35 against a list of 27** -
out of range, from a different display - and the game fell back to native.

`DisplayGuard` remembers the SIZE instead, in the mod's own config, and looks
its index up fresh against whatever list the current display produced. It
never picks a size: it re-applies the last size the game was actually running
at, does nothing at all if that size is not offered by this display, and steps
aside if the size has already been changed since startup. droha: "it should
not override to this always, it's whatever the user sets it to - it should
just keep that setting."

Measured end to end: sabotage the save back to index 0, launch, and the window
opens 3840x2160 and is corrected to 1280x720 within nine seconds, with the
save repaired so the next launch needs no correction at all. Change it to
1600x900 and relaunch, and 1600x900 is what comes back.

**One bug in the first version of this, found in play and worth
recording.** The guard sampled the window size on its first tick and
treated any later change as the player choosing one - so it would not
override a size someone had just picked. It could not tell that apart
from the GAME applying its own stored resolution, which happens a few
seconds into the boot. So the game's late apply read as a player choice,
the guard adopted it, and a remembered 1280x720 became 3840x2160 and
stayed there. The log line that gave it away: "the size was changed to
3840x2160 since startup, so that is what gets remembered".

The check is gone. Nothing is recorded until the settle is over and the
game has finished having opinions; a player cannot reach the settings
and pick a size in the first few seconds, so there was nothing being
protected. Verified after: "put the window back to 1280x720 (index 19 on
this display), which the game had opened at 3840x2160".

**And a second bug from the same root, reported as the game opening
behind other windows.** droha: "the game always opens up in the
background for some reason?" Measured on a fresh launch - the game held
the foreground at eight seconds and had lost it by twelve, which is
exactly when the correction lands. Changing the resolution makes Unity
rebuild the window, and Windows hands the foreground to whatever was
behind it.

The correction should not have been running at all. With the registry
and the save BOTH already naming the right size, the game reaches it on
its own; the guard was sampling at its six-second settle, seeing a window
the game had not got round to resizing yet, and racing it. So it now asks
the save first: if the stored index already resolves to the remembered
size, the game is left to apply it and nothing is touched. Verified - no
correction logged at all on a launch that used to log one, and the window
still ends up the right size.

A `SetForegroundWindow` after a real correction covers the case where one
IS needed. That half is unverified: a process started by a background
script is denied the foreground by Windows, so the harness cannot
reproduce the game taking focus in the first place. Only a launch started
by hand can confirm it.

### The harness stopped rewriting the display settings

It used to force the game windowed at 1280x720 before every launch. Those
values are the player's. droha, after it happened again: "why does it re-write
those? It shouldn't. That's the whole thing I've been trying to tell you."

`force_windowed` is gone, replaced by a read-only `describe_display`, and
`self_test` greps its own source for a registry write and fails if one comes
back. Reading is not enough on its own, because the game rewrites those values
every time it exits, so the snapshot now captures them into `SCREEN.json` and
restore puts back the ones that moved.

### The keyboard guard puts maps back as they were, not all on

Found by reviewing what the mod changes against stock rather than by a
report. Suppress called `SetAllMapsEnabled(false, Keyboard)` and Restore
called `SetAllMapsEnabled(true, Keyboard)` - which is not a restore. It
turned every keyboard map ON, including any the game had deliberately off,
and enabling and disabling map CATEGORIES per context is Rewired's normal
idiom.

It barely mattered while this only ran with a text box in our own dialog
focused. Suppression now also covers the game being alt-tabbed away from, so
it runs during ordinary play for as long as the player is in another window,
and a wrong restore stopped being a corner case.

`ControllerMap.enabled` survives Rewired's obfuscation in this build, so each
map's state is now recorded before it is touched and written back afterwards.
The blanket call remains for `Mode.All`, which is a deliberate bisect setting,
and as the fallback when no typed handle is available - which says so in the
log rather than doing it quietly.

Measured: two focus round-trips, "1 keyboard map(s) held individually, 0 of
them already off", zero exceptions. The count line is permanent, so any
context where the game DOES hold a keyboard map off will show up in the log.

### The campaign save is written atomically, and only where it should be

Also from the review, and the worse of the two. The settings mirror wrote
straight over `save1.json` with `File.WriteAllText`, so a crash or a power cut
partway through took the player's real progress with it. The inversion is the
tell: `RunState` and `SlotCache` - the mod's own scratch files - were already
writing temp-then-move, and the one write that touched something
irreplaceable was the one that was not.

It now writes a `.aptmp`, reads it back and checks it decodes to exactly what
went out, and only then moves it into place. A failure discards the temp file
and leaves the original untouched.

The same write also round-tripped the WHOLE document through Newtonsoft to
move one key, which puts every other value through a parse and a re-serialise.
`SettingsSplice` (new, in Core, fourteen tests) finds the span of the settings
object and replaces just that, so every other byte is copied through
unexamined. It refuses rather than guesses - a document where the key is
missing, duplicated, not an object, or unterminated returns null and the
caller falls back to the old rewrite, saying so.

Verified against the real save: only `resolution` changed, all fourteen
progress keys identical, and the 2340 bytes before the settings object byte
for byte the same. To be accurate about the severity, the drift was a latent
risk rather than observed damage - a full re-serialise of this save's current
shape happens to come out identical. The crash window was the real defect.

### The yaml option comments say what the options do

droha read through `player.yaml` during the playtest and four comments
did not survive the reading. None of this changes behaviour - the option
values are untouched - but a comment that misleads is worse than no
comment, and `test_player_yaml.py` skips comment lines, so nothing was
ever going to catch these.

- **`mechanic_coverage`** - "what is this? Should it be defaulted to 6 so
  it's as random as possible?" It is a RESERVE, so higher is *less*
  random. The comment now carries a measured table: 4 pulls in all four
  jigsaw puzzles the game has on every seed, 5 adds all five drawer ones,
  and **6 is identical to 5** because there is nothing left to reserve. 6
  is the least random setting available and buys nothing over 5.
- **`pack_size`** - "why does the comment say 1 to 10 when we want a
  minimum 4?" Because 1 to 10 is the option's range and the floor is
  applied afterwards. The comment now says so, and also admits the second
  adjustment it never mentioned: a run carries at most fourteen packs, so
  at `puzzle_count: 79` every pack is 6 whether you asked for 1 or 5. At
  the default 70 you get the 5 you asked for.
- **`archive_packs`** - "the comments should say the options." All six
  keys are now listed with their in-game names and puzzle counts. The
  jigsaw warning was vague ("turning enough of them off") and is now
  exact: jigsaws exist only in Good Tidings, Trick or Tidy, Merry Mess
  and Drawer Chores.
- **`generator_weight`** - "kind of a bad name, as it's generated random
  levels." Renaming moves an option key and breaks existing yamls, so the
  comment does the work: it says the weights are relative rather than
  percentages, and that "generator" is about where a puzzle comes from.

Left alone deliberately: `archive_packs` stays a block sequence rather
than the flow style droha suggested. `test_player_yaml.py`'s parser only
understands block sequences, so flow style would read as a string and
fail the defaults test - it needs a real YAML parse first, which is a
bigger change than a comment pass.

### Level endings: measured, and not ours

droha: some levels finish on the three-button panel - restart, pause menu,
next arrow - and others drop you straight into the next puzzle. "Why is
that? We might want to make that the same across all levels."

**It is the base game, and the mod is not involved.** Measured rather
than reasoned about, because the three previous guesses at this kind of
question were all wrong. A new DevTools `endings` command reads
`LevelManager.m_allLevelInterfaces` in one frame at the title screen -
every level's authored flags at once, with nothing loaded - and the
result is in `docs/data/level-endings.tsv`.

Of the 111 levels in the run pool, **92 show the panel and 19 do not**,
and the 19 are all sixteen generator levels plus Tupperware Nesting,
Tupperware Tower and Radial Dance Party. `generator_weight` defaults to
80, so most of a default run is generators - which is exactly why it
reads as inconsistent in play, and why the minority that DO show a panel
feel like the odd ones.

Two theories died here. The daily pool is not involved: `isDailyTidy`
reads false for every level in the table, and `DailyGuard`'s rescue -
the one mod path that genuinely skips a panel - leaves a log line that
never appeared. And the fix sketched before the measurement would not
have worked: generators already have `PreventRetryMenu` false, so
forcing that flag changes nothing. Making it uniform would mean patching
the `ShowRetryMenu` getter in either direction.

droha's call, with the numbers in hand: leave it alone. Making 92 levels
stop showing a screen the game wants to show, or making 19 show one they
were never built for, is a bigger change than the inconsistency costs.
The table stays as the record.

### The credits can be gated on STARS instead of completions

A new `goal` option. `beat_levels` is the default and unchanged;
`star_levels` counts a puzzle only when every check on it is done - every
solution and every part - which is the same star the level select already
draws on a card with nothing left to do. It has its own count,
`levels_to_star`, defaulting to 20, because starring is a great deal more
work than beating and the number that makes a good run is a different
number.

**It needed no new logic, and that is the interesting part.** The obvious
implementation is a second event item and a second event location per
level, which would shift every location id again. It is not necessary:
the Beaten event's requirement is already the STRICTEST on the slot - it
asks for the union of the level's abilities, every solution location asks
for the same union, and every part asks for a subset (pinned by
`test_tables.NoPartNeedsMoreThanItsLevel`). All locations on a slot share
one `packs` value. So a state that can reach N Beaten events can reach
every location on those N slots, and "N starred" is provably achievable
exactly when "N beaten" is. The completion condition already in place
proves the star goal too.

The difference between the goals is entirely how much work the PLAYER
does, not what the generator must prove. `test_generation.TestStarGoal`
asserts no Starred location is ever minted, so if a future change decides
it needs one, that is a deliberate decision with ids moving rather than a
surprise.

On the mod side the star predicate already existed as a private helper in
`Track`, doing exactly what the card's star does. It moved into
`CheckRouter` as `StarredCount` / `HasWorkLeft` so the goal and the level
select cannot drift apart about which puzzles are finished. Three places
read `LevelsToBeat` directly - the credits gate, the beaten toast, the
offline summary - and all three now go through one `Checks.GoalProgress`,
because adding a second goal to three call sites is how two of them end
up telling the player a different number. The toast reads "Puzzle beaten
(12/20 starred)" on a star seed.

**A Skip stars the puzzle it clears**, because it fills in every check on
it. That is consistent with skips already counting toward beating, which
was a deliberate 0.3.1 decision, so it is kept rather than special-cased -
but it does mean `skip_count` shortcuts a star goal at full strength. The
fill-stress sweep gained a no-skip star configuration for exactly that
reason, so twenty free stars cannot hide a broken goal.

Verified: 106 apworld tests and 260 C# tests, the star goal in four
fill-stress configurations across the seed span, and a real seed
generated from the player template reporting "Goal: Star Levels, Puzzles
To Star: 20" with the Beaten events unchanged.

### The level select says how far along you are

droha asked for a "levels beaten / needed" counter. The number existed
only in a toast that scrolls away, so the one screen where you decide
what to play next never said how close you were.

It reads the GOAL rather than the beaten count - a counter that always
said "beaten" would be quietly measuring the wrong thing on half the
seeds. Green once the count is met.

**The star goal shows the star, not the word.** droha: "for star goal we
should have 0/50 [star icon]s instead of it saying stars or beaten." It
is the game's own `LTL-LevelSelect-Star-solved`, the same art a card
wears when it has nothing left on it, so the counter and the cards are
plainly talking about the same thing - and it sits directly above the
chapter header's own star, which uses the same shape. The beaten goal
keeps its word: there is no icon in the game for "finished any one way",
and inventing a glyph would be less clear than the word, not more.

Parented to the toast overlay and gated on the run's own track being on
screen, both copied from the connected tag directly above it, and for the
same reasons: the track scrolls and is rebuilt, the overlay is the mod's
own canvas with no layout to lose to. It is in `RepaintSoon` so it does
not arrive a second after the screen has settled - the regression already
written up in that method.

### Which mechanics you hold, at a glance

droha: "show ability locks on the level select - icons for the twelve
mechanics, so you can see at a glance which you hold." Until now the only
way to find out was to open a puzzle and see what was greyed out.

A row of tiles across the top left: a borrowed item picture with a
three-letter label under it - SWP, STK, ORD and so on. Held is full
colour, locked is a dim grey version of the same art, and **all of them
are always shown** so the strip never changes width and you can see what
is still to come rather than only what you have. Pills for mechanics this
seed does not carry are omitted, and nothing is drawn at all when ability
locks are off - in both cases the strip would otherwise describe a
restriction that is not in force.

**The icons are real game art, twelve of them, shipped with the mod.**
There is no per-mechanic art in the game - abilities are the mod's
invention - so the hunt went two ways, and both are in
`docs/data/ability-icons.md`.

First, **badge elements**: the small item pictures that sit on a badge,
rather than an assembled badge. Two new DevTools commands made that
searchable - `sprites <filter>` dumps every loaded sprite name (998 of
them) and `spritegrid:<names>` draws a batch on screen at full size AND
at icon size, because a name says nothing about how something reads at
twenty pixels. Three rounds of that threw out everything thin or low
contrast: a hammer, nails, callipers, keys, dice and scissors all
disappear when small.

Then, better, **objects out of the puzzles themselves**. Puzzle art is
not loaded at the title screen, so `loadlevel:<index>` opens any level
outright, `newsprites` reports what that brought in, and
`spriteexport:<names>|<dir>` writes them out as PNGs - through a
RenderTexture, because the game's textures are not readable, and cropped
to `textureRect` because they are atlased. The first attempt handed back
a bottle opener instead of stacked books: `Graphics.Blit` flips
vertically on D3D and the crop has to invert y.

Objects were taken from **generator puzzles first, then campaign**, and
only from puzzles that use that mechanic and **nothing else** - so the
picture and the lock mean the same thing. droha picked the twelve:

    Swapping    Books          Badge1-Books2
    Stacking    Cartridges     Badge2-NES
    Ordering    Pencils        Badge1-Pencils
    Gadgets     Lightbulb      badge3-lightbulb
    Rotating    Record         Badge1-Record
    Sticking    Stickers       Badge1-Stickers
    Grids       GridTile       1x1-1, from Procedural Grid Puzzle
    Tidying     Breadtag       Breadtag-red, from Breadtags
    Containers  EggCarton      Carton-front copy, from the Fridge
    Furniture   Drawer         Drawer-Top+Bottom, from Tool Drawer
    Symmetry    Wreath         Wreath, from the Good Tidings wreath
    Jigsaw      Gingerbread    GingerbreadMan - the solved cookie, seams
                               and all

**They ship as PNGs inside the DLL**, 129 KB for all twelve. That is not
tidiness, it is the only thing that works: the six puzzle objects are
Addressable assets, loaded when a level opens and released when it
closes, so they are not in memory on the level select. Measured - six of
twelve resolved there and the other six drew as lettered plates. The
alternative was to catch each sprite as it passed and hold a reference,
which meant the strip filled in gradually as a player happened to visit
the right puzzles. droha: "can we just save those as png and use them in
game? That way we don't have to do all this run around."

Three dead ends worth recording so nobody repeats them. The Calendar's
stickers, the shells and the dirty paw prints all export **blank** -
they are white masks the game tints at runtime, so there is no colour in
the sprite to take. The Microscope has no microscope: its pieces are
crystal rings and a transparent lens, because the instrument is scenery.
And `badge7-spider` is not the symmetry puzzle - that is the wreath.

The letters stay under each icon. The mapping is a metaphor, not a fact,
and nobody would guess all twelve cold.

Laid out from droha's read of it in game: the block sits in the gap
between the level select's close button and the chapter heading rather
than at the left edge, where it covered the X; the two rows have air
between them; and the art is fitted to its own proportions and pinned to
a common baseline instead of centred in a square. That last one is why
the egg carton looked wrong - it is five times wider than it is tall, so
a square box with preserveAspect floated it in the middle of its tile
with a gap underneath.

Verified in play at 1280x720: "loaded 12 ability icon(s)", the strip
built twelve tiles with three in full colour for the abilities held and
nine dimmed, the counter read
"0 / 40 beaten" beneath the connection tag and "0 / 50" with the star on
a star-goal run, both survived a track rebuild, both were correctly
absent inside a puzzle and on the pause menu, and the session logged
zero exceptions. The seed used was generated before the `goal` option existed,
so it also demonstrates the payload-without-a-goal default. The
locks-off case is covered by the code path rather than by a run.

### The Furniture ability is now called Drawer

droha: "rename the ability to Drawer instead of Furniture - it's what we
were calling it before I knew the ability name." The item a player
receives should say the thing it opens, and every puzzle behind it is a
drawer or a cupboard.

The name is authored once, in `data/abilities.json`, which BOTH the
apworld and the C# mod read and which `AbilityCatalogTests` pins against
each other - so the rename is that one key and everything else follows.
Ability item ids are positional over that file's key order, so renaming
in place keeps the id and changes only the name.

**A seed generated before this carries the old name.** The mod only draws
a pill for an ability the seed's own catalogue contains, so an old save
shows eleven of twelve with Drawer missing - correct behaviour, not a
bug, and another reason 0.3.2 needs a fresh seed. Location ids had
already moved.

### The level select stopped saying "Chapter N"

The game writes that subtitle from the section index and only has names
for the five chapters it shipped with. A run has as many sections as it
has packs - fifteen at the default - so past the fifth the line had
nothing to say and read differently from every section before it. droha:
"the chapters at 5 don't have a name... just remove the chapter x, and
just have the - for all of them."

Blanked rather than renumbered, because the run's own name for the
section is already on screen directly underneath - "Opening", "Pack 3",
"The End" - and a chapter number above a pack name is two different
countings of the same thing. What is left is the dash and the star
count, identical on every section.

Held blank EVERY FRAME, not on a poll. The first version checked twice a
second, and the game rewrites the subtitle as each section scrolls under
the header - so the old chapter name showed until the next tick. droha:
"I see chapter 1/2/3/4/5 show up when I scroll over the chapter
markers." Only the SEARCH is throttled now; the label is remembered and
looked for again only when the reference has gone.

Only while the run track is up: the archive and daily menus use the same
header, and their chapter names are theirs to keep.

Verified alongside it that the sections line up with the packs, which is
what the headings claim: `Opening` holds the five free puzzles from track
position 0, then each `Pack N` starts at its own divider and holds five -
5 + 13 x 6 = 83 cards for a 70-puzzle seed with thirteen packs.

### Every mechanic is in the run, unless you ask for fewer

droha: "is there a reason why we would have less than the full 12
abilities? We should default try to have them in all runs, and have the
options to have less if we want a simpler run."

Measured first: at defaults, all twelve already appeared in twenty out of
twenty seeds. The gap was **short runs**. The coverage reserve protected
only the four mechanics no generator can make - stacking, containers,
drawers, jigsaws - on the reasoning that the other eight arrive on their
own. True at full length, false when the run is short: at
`puzzle_count: 20`, Rotating was absent from four seeds in eight,
Symmetry three, Gadgets one, all at the default coverage.

The reserve now takes one of EVERY mechanic first, then the extra copies
of those four. Re-measured: `puzzle_count: 20` gets all twelve every
seed, and the default is unchanged because it was already complete.

`mechanic_coverage: 0` is still the simpler run - it turns the whole
reserve off including the new floor. The other ways to lose a mechanic
are all content choices rather than accidents: dropping every event pack
removes jigsaws from the game, generators-only removes all four hand-made
mechanics, and an 8-puzzle run cannot hold twelve mechanics when several
of them exist only on single-mechanic puzzles.

Pinned by `test_every_mechanic_the_content_can_supply_is_in_the_run`,
which asserts against what the ENABLED content could supply rather than
against all twelve - with three exemptions it states outright: coverage
zero, runs under twenty puzzles, and ability locks off.

**A correction to something recorded earlier in this file.** The yaml
notes said dropping `drawer_chores` would gut the Drawer mechanic because
three of its five puzzles live in that pack. Measured, it does not - the
coverage reserve pulls in Workbench and Medicine Cabinet instead, and all
twelve still appear. That claim was reasoning, not measurement.

**And a latent test bug this exposed.**
`test_the_opening_holds_every_solvable_puzzle_it_can` computed the
opening as `max(pack_size, MIN_OPENING)`, the expression
`items.opening_size` exists precisely to replace - the opening is the
WIDENED size when the pack cap forces packs to grow. The test was looking
at five slots while `open_the_start` had filled six. It passed by luck
until a draw put the fourth solvable puzzle at index 5, and then read as
a regression in the reserve rather than as the stale window it was.

### The credits have to be played, not just unlocked

droha, mid-playtest: "i beat the level that had the credits unlock... That
instant it said i completed the game. i didn't have to go out and play the
credits at all."

The goal fired the moment the Credits ITEM arrived, which read as the run
ending without an ending. `GoalLatch.ShouldReport` now also requires the
credits to have been played, and there is deliberately **no fallback** -
droha: "there should be no fallback. the user needs to click on the credits
level to finish". A harness that never opens the card never sees a goal, which
is correct rather than a regression, and cost an e2e run to re-learn.

The card itself was also unreadable: a greyed hand-print with no label, which
droha could not identify as the credits at all. It gets a completion row like
any other card once it is playable, and a divider before it so it is visibly
the end of the track rather than part of the last pack. Not a chapter break -
that was tried and looked like gold-plating - just an ordinary card.

### A mechanic reserve that could eat a short run

The coverage reserve took one level per mechanic before anything else drew,
with nothing stopping it from taking the WHOLE run. At 8 puzzles it did: every
slot went to a different mechanic, so nearly every level needed abilities the
player could not yet hold, and the run deadlocked at 2 of 8.

The 0.3.2 release gate caught it and 0.3.1 passed the same gate 21/21 an hour
later - a real regression this release introduced. Nothing in the unit suites
saw it, because they ask whether a mechanic is PRESENT and every one of them
was. That was the whole problem.

The reserve is capped at half the slots, so the weighted draw always gets the
other half. At any realistic length it changes nothing: twelve abilities need
six to eight levels to cover, and half of a 40-puzzle run is twenty. It binds
only where it has to. `TestTheReserveLeavesRoomOnAShortRun` pins it from the
side that matters - at most half an 8-puzzle seed may need three or more
abilities.

### Scenery stopped being reported as a controller mismatch

`Pannables` carries no locations on any level, so every level holding one
logged the mod's loudest warning forever. It is skipped in the audit now.

Measured across all 39 controller types before hardcoding anything: Pannables
is the only one that appears (12 times) and never carries a location. The
apworld had already reached the same conclusion independently - `abilities.json`
lists it under `notPuzzles` - and `ControllerTypes` in Core is now the single
place that says so, with the count in its comment.

### Tools

`jiggle` settles pieces on demand, because reproducing the cat trap freeze
needed a trap to land inside the settle animation and droha asked the fair
question: "how do I time that? It needs to be timed to like the quarter
second." `resolutions` prints Unity's list, the GAME's list and the saved
index side by side; `setres 1280 720` sets a size by size, never by index.

`AppendLog = false` in `BepInEx.cfg` is why the first freeze report was
uninvestigable. Turn it on before hunting anything intermittent.

**The release gate now says what its Skips covered for.** A Skip banks the
slot's Beaten token, so a skipped level is indistinguishable from a solved one
in every count the gate prints - a run could go green having never solved a
quarter of its puzzles, with the only trace a line in the middle of a
fifteen-minute transcript. It keeps a ledger now and asserts two things: a
Skip was spent only on a level in `KNOWN_UNFORCEABLE`, and no Skip covered for
a level the mod was still ability-gating. The second is the one with teeth -
a gated level reaches the skip path looking exactly like an unfinishable one,
so without it a mod that wrongly withheld an ability would be paid past and
the run would pass. The reading is taken BEFORE the Skip is spent, because
spending it destroys the evidence.

**And it puts the player's environment back.** The gate deletes the mod config
and writes one pointing at localhost; it never restored either, and droha lost
their real server settings to it. Every exit path now goes through a restore,
and the game log is archived per run so an intermittent failure can be diffed
against a passing one instead of guessed at.

`harness_env.set_config` ADDS a missing key instead of warning about it. A key
is missing whenever the plugin has not written its config since the setting
was introduced, and set_config runs before the launch that would write it - so
`MuteAudio` was added, wired into five harnesses, and silently did nothing
every time, while droha listened to the game twice and said so.

Every line the gate prints also lands in `testserver/logs/e2e-progress.txt`,
at a fixed path, so a fifteen-minute run can be watched from an editor pane
that shows neither the background process nor its output.

## 0.3.1 - 2026-09-09

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

## 0.3.0 - 2026-09-06

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
  surveyed. (Both DLCs are supported as of the next release.)
