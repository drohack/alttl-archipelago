# Backlog

What has been asked for and not yet done, each with what was asked and what is
already known, so picking one up does not begin with rediscovery. New items go
here rather than in a message. A finished item leaves: how it was built and
checked is in the CHANGELOG.

## From the 0.3.0 to 0.4.4 audit (2026-09-29)

- **Seen, harmless so far:** on the post-level route with another slot
  playable, the game relaunches the finished DLC generator once, then goes
  to the next slot ("checks: now playing slot N" twice). It is the
  generator relaunch under "Open", now on a real run route: DLC1 Trophy
  Cabinet and DLC2 Water Glasses, 2026-09-29.

## Known, not built

- **The Seeing Stars Boss files two of its endings late.** Its endings are
  its phases: `DLC2Boss_Lock` and `DLC2Boss_Compass` are recorded in the
  save as those phases are solved, and only `DLC2Boss_Knife` comes with the
  level's completion (droha's hand test, 2026-09-28: 3 of 3 found in one
  playthrough, one check filed). The other two are filed when the mod next
  reads the save for that slot. Filing them as each phase is solved would
  need the game's phase event hooked; not built.
  **Filed at the finish now** (2026-09-29): `Checks.OnLevelCompleteEarly`
  reads the slot's save again (`SeedSolutionsFromSave`, leaving the id
  being completed to its own filing), so what the save recorded during
  play is filed before the retry panel counts the stars. Checked in a run
  with the two phase endings written into the save the game's way (DevTools
  `marksolved:1231:DLC2Boss_Lock`, then `..._Compass`) and the Boss
  finished with `complete`: "Solution: Lock was earned earlier in this play
  but never filed - sending it now", the same for Compass, Knife from the
  completion, and "3 of 3 solution(s) in ... -> next, without showing the
  panel". Still per phase only at the finish, not as each phase is solved.

## Open, waiting for a recurrence or the game

- **Trim Plant's grow animation threw once on a relaunch.** Quick gate,
  2026-09-30 12:25: after the route check left seeded Trim Plant for the
  level select and the card relaunched it, Unity's Player.log shows one
  `NullReferenceException` in `Sprite.get_bounds` from
  `Plant_LevelRandomizer+Plant.<Grow>` just before "Level Randomized: Trim
  Plant(Clone)". Once, not a storm; the level played and was beaten. No trap
  was near it. Again in the 0.4.5 full gate (13:10): the track launched
  Trim Plant and the gate went straight to `menu:title` and `boot:82`
  while it was still growing. Both times the gate left the level during its
  grow animation; the game's Grow coroutine outlives its level. A player
  leaving Trim Plant in its first seconds could see the same one-off.

- **A boot's teardown line does not prove the old level is gone.** In
  the item 13 hand tests (2026-09-29) DevTools `boot:1128` replaced an
  unfinished Junk Drawer 2 and printed its teardown ("tearing down 'DLC1
  Junk Drawer 2 Interface(Clone)'", "destroying orphan level"); the
  Boss's first solve then threw `NullReferenceException` in
  `LevelInterface.CheckWinCondition` (Unity's Player.log) and the Boss
  stopped there - its drawer never closed for the second key (droha:
  "in drawer with keys it's frozen?"). release-testing.md has the case
  where no teardown line prints; this one had it. The same level in a
  fresh launch played clean, which is why a hand test gets a fresh launch
  per level (CLAUDE.md, section 5). Harness only: a player never boots.
  Seen again in the 0.4.5 full gate (2026-09-30 13:06): `boot:1020` tore
  down an unfinished TupperwareNesting, and a tween callback of the old
  level threw `ArgumentException: An item with the same key has already
  been added` three times, keyed by its own Stack 1, Stack 2 and Tray.
  These reach Player.log only, so the gate's "no unexplained errors" check
  (LogOutput.log) does not see them.

- **Books (Randomized) finishing on its Draggables rule** (0.4.2 playtest;
  investigated 2026-09-28). Not a stray controller: on symmetric seeds the
  generator adds a second controller, `Draggables`, over the same books, and
  it checks the puzzle's second solution (`SolutionId` 1; `gensweep:40:995`,
  21 of 40 seeds) - the table lists only `Shuffle`, hence `CONTROLLER
  MISMATCH`. Its ending `Draggables_0` is real: the symmetric rule, first
  or second as the seed puts it, and now files as that rule (item 9 of the
  0.4.3 run, `Endings.Canonical`). Whether any
  finish came without the player moving a book is unproven: the logs carry
  no timestamp between the level opening and the finish (the second player's none at all),
  and 100 directly generated layouts (52 with the rule), the daily-guard
  route, a card click, locks on, and a level left alive underneath never
  finished on their own. If it recurs, note the time the level opened.

- **A forced finish relaunches a generator (harness only).** In every gate
  since at least 0.4.2's (release gate 2026-09-27 17:25 and both of
  2026-09-28), a generator the harness finishes with `solve:` goes straight
  on and, about a second later, the mod relaunches the SAME slot with its
  seed ("checks: now playing slot 1" after "navigation: next -> slot 2").
  Players do not see it: across both 0.4.2 playtest logs, 90 of 93
  straight-on finishes went to the Daily page and the daily guard opened the
  next slot. The harness boots each next level itself, so only the arrow
  check could notice, and it no longer starts on a generator. It also hits
  mid-solve on a seeded level with two controllers (DLC1 Trophy Cabinet, DLC
  gate 2026-09-28), which the harness now refunds. Unexplained: what calls
  StartLevel with the level's own index. Not the item alone, not `boot:`,
  not KeepRunningWhenUnfocused: the same seed served by hand, Stamps opened
  from the track or booted, forced, a Hint Page arriving - it went on to the
  next slot through the daily guard every time (2026-09-28).

- **The arrow session once solved Post-It Notes and got no completion.**
  Base gate, 2026-09-24 13:34: "the level moved on (1 controller(s), 1
  solved)", then "waiting for the completion", so both arrow checks failed.
  The same seed and slot passed in the runs before and after it, and in
  `--only-arrow`. That passing log shows the mod launching the slot twice
  with forceReload (13:36); a solve that lands on the first copy would be
  lost with it. Not supported so far: in the 103 kept logs, 215 of 1521
  boots launched twice and none had a solve between the two launches. The
  failing session's log was overwritten by the next launch; the gate now
  keeps it (`e2e-<stamp>-arrow.log`), so the next failure can be read.
  Possibly the same cause as DLC2 Broken Vases the same day, whose
  completion came 13 s after the solve against a 6 s wait; the wait
  (`COMPLETION_WAIT`) is now 30 s. If it recurs, it was not that.
- **A gate run can stop receiving game events for good.** Three runs on
  2026-09-24: base 10:24 (mid TupperwareNesting), DLC 14:43 (DLC1 Boss)
  and DLC 22:31 (DLC1 Sewing Box, stopping the gate at visit 18 of 25).
  From one moment on, solves set their flags but no PartSolved, check or
  completion arrived, and the next boot found timeScale 0 in the middle of
  a level. 4 of the 364 boots in that day's kept logs. What caused it is
  NOT known: the logs did not record it. Measured on one level: the game's
  own `GameManager.Pause(true)` holds every event, a clock reset does not
  release them and `Pause(false)` does. So `boot` now undoes that pause,
  DevTools logs every `Pause` call and focus change (`game:`) and every
  clock change (`time:`), and the gate warns for a solve sent while paused.
  The trace cannot name the caller (IL2CPP's stack walk returns 0 frames).
  Measured 2026-09-25: the title pauses the game on its own (pause-menu
  Exit, `replayselect` then `menu:title`) and `boot` finds and undoes that.
  Tried 8 times (two arrow sessions, three replays each of 22:31 and 14:43),
  seen 0 times. Then caught by the logs in the DLC gate of 2026-09-25
  (`e2e-20260925-101921.log`, visit 23): a Cat Trap reset DLC1 Craft
  Supplies, the game called `Pause(true)` itself (clock 0), the harness's
  four solves raised nothing, and about 8 s later the game's own
  `Pause(false)` released them all at once - the level completed. A reset
  that never unpaused would look exactly like the stops above; not seen.
- **Two level selects on screen after a DLC level.** droha, watching the
  DLC gate on 2026-09-24: "there was 2 level selects open at the same time
  there for a minute". The log: after the post-level Level Select, DlcGuard
  logs "opening the run's track"; the harness presses the level select's
  Close Button, the game heads back to the DLC's own select, and DlcGuard's
  Tick opens the run's track a second time. Not new: every kept DLC gate log
  since 2026-09-17 shows two guard openings per Close. DlcGuard.cs's own
  comment records the same double render on 2026-09-18, fixed then for the
  post-level route only. A player pressing Close there likely sees it too.
  **The Close half is fixed** (2026-09-24): Close from the run's track now
  goes to the title, checked in game twice. **Still open:** the post-level
  route still builds the DLC menu for a moment before the guard leaves it.
  Known: `ReplayMenu.LevelSelect` reads `IsDLCLevel` itself (DevTools
  `xrefs:ReplayMenu.LevelSelect`; the scan froze the game on its eighth
  call, so the rest of that list is unread). Not the route: a
  `GoToLevelSelectForLevel` prefix and a `ContextualState` postfix both
  installed and never ran on it. About 20 screenshots of that route and of
  Close, taken before the fix at up to two a second, never caught the two
  menus on screen together.
  Measured 2026-09-25 in a hand-test run: 18 post-level Level Selects on DLC
  puzzles all entered the DLC menu first (one guard opening each), 0
  exceptions; once, after DLC1 Boss, the track was built twice and BOTH
  level selects stayed on screen with clicks going nowhere - the first
  screenshot of it. Tried and reverted: blanking the finished level's
  `DLCDetails` for the length of `ReplayMenu.LevelSelect` (restored in a
  finalizer) - the DLC menu still opened. With the IsDLCLevel postfix that
  also changed nothing, the route is decided outside that call or by
  something the pointer scan (`xrefs:...|...`) cannot reach: it kills the
  game at the method's eighth reference. Not known what decides it.
  NOT this item: the DLC gates of 2026-09-27 opened the track twice after
  12 of 13 puzzles and then threw in `menu:title`, but that was the gate's
  own timing - `replayselect` sent while the retry panel was still coming
  in - fixed in the harness (`settle_post_level`).

## Seen, not fixed (visual only)

- **Seed Pods (65), Clover (68):** the game tints its pieces to 0.9 once,
  after the level's intro, and only the ones not locked then. A piece locked
  through the intro comes back at full white, 10% brighter than its
  neighbours.
  - Measured by the lock round-trip, 2026-09-30: in a fresh load every piece
    is (0.9, 0.9, 0.9); after lock and release it is 1.0.
  - Shells and Wreath use the same controller class with no intro, and
    pass.
  - The likely hook is the game's `LevelObject.SetInteractable` at the
    intro's end, the only colour-adjacent method on the level types (interop
    metadata).
  - A fix changes the unlock path every level shares (the order of
    `SetInteractable` and `Tint` in `AbilityLocks.ApplyOnce`), so it needs
    the whole round-trip sweep again, about 2 h.
- **Mirror (79)'s candle, Ghost Cat (1239)'s cats: not grey while locked,
  and correctly so.** Looked at 2026-09-30 (DevTools `tree:`, screenshots):
  - Mirror's candle is two groups. Moving it needs nothing; only lighting or
    putting it out needs Gadgets. Without Gadgets it is movable and drawn in
    colour ("0 of 19 dimmed"). Its flame's Animator only swaps flame
    pictures, and paints no colour.
  - Ghost Cat's flagged object is `CatManagement`, the cats its reveal
    animators fade in once found, not a piece anyone moves. With every
    ability withheld the level opens on an empty dark room.
  - droha: leave them (2026-09-30).
- **Robots (1230)'s little robot on a line:** grey with every ability
  withheld, but it moves, and its antennas toggle when clicked; nothing in the
  level can be solved that way, so no check comes early.

## Ideas, not asked for

- **Steam Cloud while testing:** harness runs that change `save1.json` outside
  a Steam launch still make Steam report a conflict; unticking "Keep game saves
  in the Steam Cloud" for the game while testing stops it.
