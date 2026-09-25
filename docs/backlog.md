# Backlog

The five things droha raised on 2026-09-10 mid-playtest all landed in
0.3.2 - see the CHANGELOG for each:

- the levels beaten / needed counter, on the level select
- the credits gated on stars, as a `goal` option
- ability locks shown on the level select, as a strip of pills
- the yaml option comments, rewritten with measured numbers
- level endings, which turned out to be the base game's own behaviour and
  droha's call to leave alone; `docs/data/level-endings.tsv` is the record

New items go here rather than in a message, so picking one up does not
begin with rediscovery. Each should say what was asked and what is
already known about it.

## Open

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
  (`COMPLETION_WAIT`) is now 20 s. If it recurs, it was not that.
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
