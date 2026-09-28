# Backlog

What has been asked for and not yet done, each with what was asked and what is
already known, so picking one up does not begin with rediscovery. New items go
here rather than in a message.

## Built 2026-09-27 and 2026-09-28, waiting on a release

Each is in the CHANGELOG's Unreleased section, with how it was checked.

- **Achievements as checks** (droha, 2026-09-25; built 2026-09-28): the
  `achievements` option, off by default, 17 puzzle achievements
  (`AchievementChecks` in Core says which and why the rest are left out;
  Sweep and Path of Destruction went the same day, being part checks the
  run already has, and Keep Away, which never fired in a run with its
  condition met). Ones the Steam profile holds are earnable too: the mod
  clears the game's in-memory "achieved" flags at each slot entry.
  Checked in game on Eggs (withheld without Ordering, filed when it arrived).
  They hold no progression until each has been seen to fire in a run - see
  "Waiting on droha".
- **Option ranges and defaults** (droha, 2026-09-28): every range measured at
  and past its ends; `puzzle_count` 10 to 130 (130 puzzles checked in game:
  146 cards, the overview strip at 0.59 of its 0.55 floor and now centred),
  `pack_size` 5 to 20, `guaranteed_open_slots` 4 to 20 and 5 by
  default, `starting_abilities` 0 to 13, `mechanic_coverage` 0 to 10,
  `skip_count` 0 to 50; Cat Traps 15%, Background Change Traps their own 15%,
  the rest of the filler split between Hint Pages and tokens; the "79" repeats and
  "five chapters" gone from `player.yaml` and the tooltips. Hints name the
  track section ("Pack 2, puzzle 4").
- **The release gate brought up to date**: features read from `Plugin.cs`,
  pack size 5, `predict_gate.py` on the gate's own pre-flight.

- **Nothing playable: the level select, never a blocked puzzle** (playtest item
  1; droha, 2026-09-26). Core `SlotPicker`; the arrow, the daily guard and a
  finished puzzle with nothing playable all go to the track by way of the title.
- **Solutions judged by the part their ending names** (playtest item 2): Spoons,
  Coins 1, Figurines file without the abilities their ending does not use.
- **The cursor after Tupperware Tower** (playtest item 7): droha's hand
  test (2026-09-28) finished the Tower in a run; the mod moved on without the
  panel and the cursor was there on Tacks (DevTools `cursor`: active,
  alpha 1). `CursorGuard` never had to act.
- **Media Cabinet without Drawer** (playtest item 9): droha's hand test
  (2026-09-28) finished it holding only Ordering and Stacking, as Kat did;
  Drawer is bypassed (proven-requirements.json has both).
- **Fixed ending names, one check per ending, Medicine Cabinet per colour**
  (playtest items 3-5, droha 2026-09-28); the review is under "Waiting on
  droha". The gate's paper plan knows which ending forcing files
  (`fixtures/forced-endings.tsv`).
- **Title menu drawn under the level select** (playtest item 6): reproduced;
  the Levels press now waits for the title to be the active menu, with a
  backstop that takes a leftover title down.
- **Filled icons on cards in packs not opened** (playtest item 8): reproduced
  on Pack 5 and fixed; the per-card rule could not place a card while the track
  was being built.
- **The retry panel's pop-up when a finished puzzle moves on** (droha,
  2026-09-27): not shown any more; the panel's own arrow is used unseen.
- **Background Reset Token** (droha, 2026-09-25), **YAML weights** (the
  fixed-layout DLC levels roll under their DLC's weight; player.yaml quotes
  the shares), **Steam Cloud** (the mod's files moved into
  `<save folder>/Archipelago/`; Steam now finds only `save1.json`).

## Waiting on droha

- **The fixed endings, reviewed and every ending seen** (droha,
  2026-09-28; the fourteen unseen endings played by hand): Medicine Cabinet's
  "Red Items" confirmed; Mirror's big items in place are "Still Life" (jug,
  candle, dish, bottle, books-and-box stack) and its little things (lemon
  wedge, lemon, frond and skull in their containers, candle put out) are
  folded into the Solution ("for mirror just fold the little things into the
  solution"). Generated puzzles got fixed endings too (droha: "don't
  generated puzzles still have fixed solutions?"). Every ending as built:
  `docs/reference/ending-names-review.md`.

- **The Seeing Stars Boss files two of its endings late.** Its endings are
  its phases: `DLC2Boss_Lock` and `DLC2Boss_Compass` are recorded in the
  save as those phases are solved, and only `DLC2Boss_Knife` comes with the
  level's completion (droha's hand test, 2026-09-28: 3 of 3 found in one
  playthrough, one check filed). The other two are filed when the mod next
  reads the save for that slot. Filing them as each phase is solved would
  need the game's phase event hooked; not built.

- **Achievements seen firing in a run - all 17** (hand test, 2026-09-28).
  droha's achievements seed (130 puzzles holding all of them) filed 14 as
  real checks, among them I'll Take My Water Neat and Harmonized Purr, held
  on droha's profile, once the held flags were cleared; Exacting Eggs,
  Unstable Stacker and Now You're Playing With Power fired on their puzzle
  in earlier runs. A missed one is retried by opening the finished card
  (Place Setting reopened as the same slot). They still hold no progression,
  as the option promises: a hard one never blocks a run.

- **The overview strip's drag stopped short of both ends** (droha,
  2026-09-28, the 130-puzzle achievements seed; fixed the same day, droha
  dragged it end to end: "that's way better"). Measured with DevTools
  `uitree:Levels Overview Scrollbar`: the
  Unity `Scrollbar` and its `Scroll Handle` live on 'Levels Overview
  Scrollbar' itself, 3037 wide on the 1920 screen, while the mod's fit
  scaled only its child 'Overview Container' (the dots, 0.624), so the
  bar's ends were 558 past each edge. FitStrip now narrows the bar's width
  by the same factor (not its scale, which the game squeezes on a grab):
  1894 wide, the handle on screen at both ends of the track (screenshots
  after `scrolltrack:0` and `scrolltrack:137`).

## Open, waiting for a recurrence or the game

- **Books (Randomized) finishing on its Draggables rule** (0.4.2 playtest;
  investigated 2026-09-28). Not a stray controller: on symmetric seeds the
  generator adds a second controller, `Draggables`, over the same books, and
  it checks the puzzle's second solution (`SolutionId` 1; `gensweep:40:995`,
  21 of 40 seeds) - the table lists only `Shuffle`, hence `CONTROLLER
  MISMATCH`. Its ending `Draggables_0` is real and now files "Solution:
  Shuffle 2" (the entry answers to `Shuffle_1|Draggables_0`). Whether any
  finish came without the player moving a book is unproven: the logs carry
  no timestamp between the level opening and the finish (Kat's none at all),
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
- **Mirror (79)'s candle, Ghost Cat (1239)'s cats:** their animations repaint
  them every frame, so they show in colour while locked. They are left as the
  animation has them on unlock.
- **Robots (1230)'s little robot on a line:** grey with every ability
  withheld, but it moves, and its antennas toggle when clicked; nothing in the
  level can be solved that way, so no check comes early.

## Ideas, not asked for

- **A level's own cat for the Cat Trap.** On the four `CatGrab` levels (Place
  Setting, Shells, Stamps, MerryMess_Crackers), call the game's `DoGrab()`
  before the reset instead of the overlay paw - cosmetic, since the reset does
  the work. Unchecked: whether `DoGrab()` is safe out of sequence, and whether
  the animation survives the rebuild. `history/cat-trap-tests.md` has the
  survey.
- **Steam Cloud while testing:** harness runs that change `save1.json` outside
  a Steam launch still make Steam report a conflict; unticking "Keep game saves
  in the Steam Cloud" for the game while testing stops it.
