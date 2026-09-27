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

- **Steam's "cloud save not synced" message.** droha, 2026-09-27: every
  launch of the modded game shows it; stop it, and have the cloud back for
  play without the mod. What is known (Steam's `logs/cloud_log.txt`, app
  1629520): Auto-Cloud syncs every `*.json` in the save folder, the mod's
  `save_ap_*` and `alttl-last-session.json` included. The message is a
  conflict on the campaign save, `save1.json`: the local copy changed on
  2026-09-27 00:05 during testing, the cloud copy on 2026-09-25, and the game
  is started outside Steam (no launch record), so every exit's upload fails.
  Options: resolve it once in Steam (keep the local copy); untick "Keep game
  saves in the Steam Cloud" while modding; or a mod setting calling
  `SteamRemoteStorage.SetCloudEnabledForApp`, which Valve says must only
  follow an explicit player request, and a crash would leave it off.
  "Retry sync" in Steam does nothing (droha, 2026-09-27): the log answers
  "already marked as conflicting" each time; only launching from Steam
  offers the local/cloud choice. RESOLVED once the same day: droha launched
  from Steam and kept the local copy ("Upload complete, result OK",
  17:07). It comes back whenever testing rewrites `save1.json` outside Steam
  while the cloud copy differs, so what is still open is preventing it. The same log shows the mod's `save_ap_*`
  files putting the game over its cloud quota ("over quota. Removing from
  cloud") - naming them outside `*.json` would keep them out of the cloud.
- **A Background Reset Token.** droha, 2026-09-25: a new filler item, and a
  button in the pause menu (like the hint notepad's) that spends one to put
  the backgrounds back to default - "that way you have a way of resetting it,
  but it's not automatic. you know when you're doing it." A reset clearing
  the trap automatically was built and reverted the same day. What it
  showed: the backdrop colour is a function of the trap COUNT
  (Backgrounds.ColourFor), so clearing needs a count mark in the run file
  that survives the reconnect replay; ApplyToLevel writes into each
  LevelInterface without keeping the level's own colour, so every level it
  touched needs its colour put back, not just the one on screen; the pause
  screen's Background image and the level select's section colours need
  the same. New item names go last in their tables (ids are positional).
  The case that prompted it: droha's Telescope #2 (seed 1385976506) under a
  Background Change Trap colour, where the last star in one section could
  not be found in about 12 launches and was sent with `/send_location`.
- **Nothing playable: show the level select.** droha, 2026-09-26: when the
  next arrow, Play, or the automatic advance finds no playable puzzle - the
  rest shut behind packs, or every card left red because of abilities -
  bring the player to the level select rather than into a blocked puzzle,
  "it would be more visually better to know when you are blocked". Known so
  far: Track.NextUnfinishedSlot tries reachable work first and then falls
  back to any unfinished open slot, which is the case that opens a blocked
  puzzle; the fallback is where the level select would go instead (the
  arrow is Navigation.AfterGetNextLevelIndex, Play is TitleScreen, the
  panel's automatic arrow is RetryPanel). The daily guard already goes to
  the track by way of the title when nothing is playable.
- **YAML weights, clearer.** From the 0.4.2 handoff: the source weights are
  RELATIVE (80/10/10 is 8/1/1), each non-reserve slot rolls a source with
  weight/sum, the DLC weights add to the sum when a DLC is on (defaults with
  both DLCs: generator 67%, the others about 8% each), 0 means never, an
  exhausted one-shot source falls back to generators, and all zero means
  generators. Two confusing parts to fix: the four FIXED_LAYOUT DLC levels
  are drawn under generator_weight (source "generator") and should move under
  their DLC's weight, and player.yaml should put the DLC weights beside the
  other three and show the resulting percentages.
- **Achievements as locations.** droha, 2026-09-25: "adding in the
  achievements as locations (like water glasses removing all ice from
  cups). I'm not sure if that's easy to track or not." Known so far: the
  game has per-level achievement checker classes in Assembly-CSharp (for
  example `DLC2MusicBoxCounterAchievementChecker`). Not yet looked at: how
  many there are, what event or call marks one earned, and whether a run's
  save redirect keeps them from reaching the real profile.
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
  NOT this item: the DLC gates of 2026-09-27 opened the track twice after
  12 of 13 puzzles and then threw in `menu:title`, but that was the gate's
  own timing - `replayselect` sent while the retry panel was still coming
  in - fixed in the harness (`settle_post_level`).
