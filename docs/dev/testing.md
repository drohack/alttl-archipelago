# Testing in the game

The mod cannot be tested without the game, and the game is installed once, for
one person, with their saves and settings in it. This is how to test against
that install without leaving anything for them to clean up, how hand tests are
run, and the in-game checks that stand behind each feature.

Which test to reach for first is the ladder in the repo's `CLAUDE.md`. The
release gate is `release-testing.md`. Every tool named here is in
`tools/README.md`.

## Harness safety

### Put the player's environment back when the harness exits

A harness needs settings the player did not choose - a local server, a fixed
slot, auto-connect - and writes them into the config the player uses. It plays
through real saves, so it leaves run files behind. Announcing that is not
restoring it. Wrap every harness body in `tools/harness_env.py`:

```python
from harness_env import Environment

with Environment("my-harness") as env:
    env.configure(Host="localhost", Port=38281, SlotName="droha",
                  AutoConnect="true")
    ...
```

It snapshots both plugin configs, the display settings and every save file
(the save folder and its `Archipelago/` subfolder) on entry, and on exit puts
back what changed and deletes what the run created - on a normal exit, an
exception or Ctrl-C. A file whose bytes did not change is not rewritten: a
`save1.json` rewritten outside a Steam launch is what Steam Cloud reports as
"not synced". `env.configure()` takes effect at the next launch, so configure
before launching. A run that does change `save1.json` outside a Steam launch
still makes Steam report a conflict; unticking "Keep game saves in the Steam
Cloud" in the game's Steam properties while testing stops it.

A killed process unwinds nothing. Every snapshot stays on disk:

    py -3.13 tools/harness_env.py --list
    py -3.13 tools/harness_env.py --restore-latest

`--restore-latest` is right straight after a hard kill and wrong if anyone has
played since; check `--list` first. Restore closes the game first, because
BepInEx writes its configs on shutdown and would overwrite a restore made under
a running game.

The save folder is found by glob (`<LocalLow>/maxinferno/A Little To The
Left`), not spelled out, and `_require_save_dir()` refuses to run without it:
the restore sweep deletes files not in its manifest, and an empty path would
make that sweep relative to the repo.

### The mod's files

The mod keeps no log of its own; it writes into BepInEx's. Its state files:

| File | Where | What |
|---|---|---|
| `save_ap_<slot>_<seed>.json` | save folder, `Archipelago/` | the run's own game save (the redirect) |
| `save_ap_<slot>_<seed>.run.json` | save folder, `Archipelago/` | skips and traps spent, beaten tokens, owed and withheld checks, background resets |
| `alttl-last-session.json` | save folder, `Archipelago/` | the offline slot cache: seed, plan, items |
| `alttl-prompts.json` | BepInEx root | which one-off prompts were answered |

`Archipelago/` keeps all of them out of Steam Auto-Cloud, which syncs `*.json`
in the save folder itself and holds four files. A launch moves any the older
builds left in the top level into it.

### Keep the log across launches

BepInEx truncates `LogOutput.log` on every start. Before chasing anything
intermittent set, in `BepInEx/config/BepInEx.cfg`:

    [Logging.Disk]
    AppendLog = true

`harness_env` does not snapshot that file, so the setting sticks.

Thunderstore's BepInExPack_IL2CPP also sets `WriteUnityLog = true` there,
which copies Unity's own log, the game's exceptions included, into
`LogOutput.log`. Under it the gate's "no unexplained errors" check sees
game exceptions that otherwise reach only Player.log; `KNOWN_ERRORS` in
`release_e2e.py` lists the ones the harness itself causes.

### Launching and deploying

Run the executable directly, with no arguments. `steam://` refuses this
family-shared copy ("no license"), and the game pops a dialog for Unity
arguments; both look like a hang. Doorstop's own `--doorstop-*` arguments,
which r2modman passes, do not (2026-09-30: title screen in 15 s). `harness_env.ensure_no_steam_relaunch()`
writes `steam_appid.txt` (1629520) so Steam's DRM stub does not relaunch the
process under a new PID.

Start it with `release_e2e.launch_game()`, which every tool uses: that Steam
guard, then the DevTools session `mute` as its first command, so a test never
plays sound on droha's speakers (droha, 2026-09-28: "you should be muting when
you're testing in the background"). The session mute ends with the game;
never set the `MuteAudio` config for this, which once outlived a test and
silenced droha's own play. DevTools `unmute` for a hand test played with sound.

`tools/deploy.sh` closes the game before copying, because Windows will not
overwrite a loaded DLL and the build would otherwise leave the old plugin in
place behind a green "0 Error(s)". `tools/deploy.sh --no-kill` compiles only
and never deploys - the check to use while someone is playing. A plain
`dotnet build` deploys whenever the game is closed.

### A harness measures nothing until it has been seen to fail

Three harnesses here have reported clean results while unable to report a dirty
one: `emptysoak.py` left each level before the watchdog's six-second window,
a compliance filter silently skipped the tests that loop over worlds, and
`offline-reconnect-test.py` read the log after the line it was looking for.
Put the bug back and run the harness once before trusting it.

Two traps that make a harness look broken when it is not:

- `SKIP_REQUIREMENTS_UPDATE=1` is needed locally too. `MultiServer.py` and
  `Generate.py` otherwise prompt about requirements, die on EOF with no
  console, and the harness only sees `No response from localhost:38281`.
- `LogOutput.log` is truncated per launch, so a reader must reset when the
  file shrinks (`release_e2e.Log`); `Log.before_launch()` deletes it outright.

## Hand tests

A person plays when the question is "can a player do X in level Y": the probes
choose which level, and the playing answers. Set everything up first; the
player only opens the game and plays.

- One level, holding exactly the abilities named:
  `tools/handtest-level.py <index> [ability ...]` (`--grant <Ability>` adds
  one mid-test). A queue of them: `tools/handtest-queue.py --build`, `--next`,
  `--answer`.
- To take abilities away without a new seed, serve one seed holding every
  ability and use DevTools `revoke:<Ability>[,...]` (`revoke:none` gives them
  back). `traps:off` stops Background Change Traps recolouring the level.
- Before the player starts, run `tools/watch-handtest.py --until <regex>` in
  the background: it wakes on a crash, an error line or the step's `PartSolved`.
- `handtest-level.py` deletes every run save, a live seed's too: take a
  `harness_env` snapshot first and restore it after.
- Results go in `apworld/alttl/data/proven-requirements.json`, edges in
  `levels.json` through `tools/add-edges.py` (see `level-data.md`).

## Ability locks

The lock (`AbilityLocks.cs`, with the vote in Core `ObjectLock.cs`) greys and
freezes a group whose ability the run does not hold, and gives it back when the
ability arrives.

### The round-trip probe

`tools/probe-lock-roundtrip.py` checks it without hands on every level with a
locked group (132 levels, about 2.5 h):

1. Load the level with every ability held: the control.
2. Load it with abilities withheld (all; on levels with drawers or doors also
   only Drawer or Gadgets, and all but those). Each locked piece must be frozen
   or, for a drawer or door, solid; every visible sprite grey; nothing clear
   greyed into view; locked drawers refuse to move; locked doors and flowers
   refuse a drag.
3. Run a lock pass, move the drawers as a player would, give everything back,
   put the drawers as they started and open them all.
4. Compare with the control piece by piece: selectable, collider, physics,
   every sprite's colour, drawer state, what each drawer saved.

Run it on the levels a change can touch, or all of them:

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py 1121 1124
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py --all [--resume]

It needs the hand-test server with a seed holding every ability, and starts the
game itself. Results: `testserver/logs/lock-roundtrip/results.jsonl`. Every fix
below was seen to fail the probe when undone (17 of 17 that it can see).

The whole sweep on 2026-09-30 (0.4.4 plus the audit's fixes): 115 configs
passed, 12 to review, 9 failed, 1 skipped (TrickOrTidy_Bones' control).
- Known, and the same as before the 0.4.2 fixes: Mirror's candle, Ghost
  Cat's cats, Tupperware Tower's colliders (hand tests say its flags hold),
  Robots' two containers inside robots. Seed Pods and Clover's intro tint
  passes since fix 22 (2026-10-01).
- Trim Plant's leaves and DLC2 Bells' three compartment bells, first seen
  on this sweep, were settled by droha's hand test (2026-09-30), as was
  DLC2 Pizza, the only Distributing level. Each was grey and would not
  move with its abilities withheld, and moved and finished with them given
  back. So "collider still on" there is the probe's alarm, not a leak: the
  lock's interaction flags hold the piece, as on Tupperware Tower.

### The fixes it guards (2026-09-25 to 27)

| # | Fix | Found on | Probe check |
|---|---|---|---|
| 1 | A locked drawer, cupboard or door keeps its collider and physics | Tea Cabinet | a locked cover lost its collider/body |
| 2 | Scrubbed objects are doors only on Clock Cupboard, Tea Cabinet, Trophy Cabinet | Wilting Flowers | hand-tested |
| 3 | A locked door or flower refuses a drag | Tea Cabinet, Wilting Flowers | DevTools `scrub:` logs the refusal |
| 4 | Wilting Flowers' flowers grey through the sprite that draws them | Wilting Flowers | locked, not grey |
| 5 | A drawer piece that cannot move is not locked or greyed | Sewing Box, Jewelry Box | hand-tested |
| 6 | A locked drawer does not move when the game moves it; the move happens on unlock | Jewelry Box, Kitchen Utensils | a locked drawer moved |
| 7 | A sliding drawer locks with its set even when another group frees it | Daggers | hand-tested |
| 8 | A locked drawer's art is greyed at any depth | Bathroom Drawer | part of fix 13 |
| 9 | Drawer-set pieces get their physics back on unlock | Sewing Box | fallback behind fix 15 |
| 10 | A piece a drawer saved while locked comes out free | Sewing Box, Paper Plane | a saved piece not interactable |
| 11 | Flags only on locked pieces; no re-entered pass; no pass on ControllerChanged | Fruit Stickers crash | no re-entered pass in any log |
| 12 | Unlock gives back the game's own "interactable" | Sewing Box | selectable where the game did not allow it |
| 13 | Every sprite a locked piece shows is greyed | 11 levels | locked, not grey |
| 14 | Clear sprites stay clear; a fading one is greyed when it shows | Daggers, Books (Randomized) | a clear sprite greyed into view |
| 15 | A collider or physics the game turns back on comes back on | PawPrints | collider/physics not back |
| 16 | A cover frozen before it counted as one gets its collider back | Sticky Drawer | fallback since fix 17 |
| 17 | A sticker's peel handle locks with its sticker | Sticky Drawer | handles in EXPECT |
| 18 | A rag locks with the level's clearable pieces | PawPrints | handles in EXPECT |
| 19 | A lock pass when the intro ends, on a level with a rag | PawPrints | should be frozen |
| 20 | A match locks with the level's candles | Candles | handles in EXPECT |
| 21 | Cat Food Cans, Boxes and Presents (Stacked) reload when their pieces unlock mid-level | Cat Food Cans | Core `ObjectLockTests` |
| 22 | Seed Pods and Clover reload too, so the game tints their pieces again (after the reload their tint is the new load's: `TINT_PER_LOAD`) | Seed Pods, Clover | colour not back |
| 23 | Robots' indexed states do not change while Ordering is withheld: their buttons' colliders are off (`AbilityLocks.HoldButtons`; Core `ObjectLock.IndexHoldLevels`, its art `IndexHoldArt`), and a press that gets past is refused (`IndexHold`) | Robots | hand-tested; the warning `a press reached held` must not appear |

The hand checks behind these (collider-back-on levels, second colliders,
pieces the game keeps fixed, Radial Dance Party, Fruit Stickers) were all
answered on 2026-09-27; `history/manual-lock-test.md` keeps each answer. Fruit Stickers is
still worth a minute of sticking and peeling each release. The visual-only
cases seen and not fixed are in `backlog.md`.

## Hint Pages

Everything about Hint Pages is automated except the erasing: a synthetic drag
never puts the notepad's `CleanableSurface` into a wiping state, so one human
scrub closes the gap. DevTools `hinttaken` exercises the charge itself.

Set up with `tools/handtest-level.py` on a seed that holds Hint Pages, then on
a level with a hint:

1. **Erasing charges one page, once.** The note reads `Rubbing this out uses a
   Hint Page - you have N`. Rub the scribble off: one toast, `Hint Page used -
   N-1 left`, and the note becomes `Already uncovered - reading this again is
   free` - once, not once per stroke.
2. **A paid page stays paid.** Leave and come back: still uncovered, still N-1.
3. **Page two costs its own page** on a level with two (Buttons, Spice Jars).
4. **With none held** the notepad still opens, reads `No Hint Pages - find one
   to uncover this hint`, and the scribble will not come off.
5. **Generators have hints too:** the pause menu shows a count, not `no hint`.

The log carries `hint: page <slot>:<page> read, N left` per charge, and the run
file records paid pages as `"slot:page"` keys.

## Background Change Trap and Reset Token

- The trap recolours the puzzle, the pause screen and the level select from the
  game's own palette, as a function of the count received, so a reconnect lands
  on the same colour. DevTools `watch:<s>` prints the camera and the level's
  colour fields, `menubg` the pause screen, `sections` the track.
- A Background Reset Token is spent from the pause menu's Reset Background
  entry (its count beside it). Checked 2026-09-27: the level, camera and pause
  screen come back to their own colours; with no token, or nothing to undo, it
  refuses and spends nothing; the reset survives a relaunch; the next trap
  paints again. The camera is restored to the level's `ActiveBackgroundColor`,
  which on a generator is its randomized scheme, not the colour field.

## Cat traps

A Cat Trap plays the paw, then calls the game's own `LevelManager.ResetLevel()`
- the pause menu's Reset. On a level with its own `CatGrab` (Stamps, Shells,
Place Setting, MerryMess_Crackers) the game's cat goes instead (`DoGrab()`,
never the Interlude's `OnTrigger()`, which leaves the level's event stuck in
progress), and the reset waits until its paw has gone (Core
`TrapTiming.AfterCat`, 4 s at most; DevTools `catevent` shows a grab).
The reset rebuilds the level (the `Level` instance id
changes) with the same seed, so no attachment - surfaces, drawers, grids,
nesting, stacks - can survive it, on any puzzle type. Three designs that put
pieces back by hand broke puzzles; `history/cat-trap-tests.md` records them.

Proven in play and to stay true: the layout after a trap is the opening layout
(DevTools `layout:<tag>` diffs, parent and placed flag included); a generator
keeps its seed; a trap outside a puzzle is spent, not queued; traps do not
re-fire on reconnect (`trapsSprung` in the run file); dimming re-arms after the
rebuild; a held piece and a stamp posted into an envelope both come free; the
pause menu survives it. The spoiler says which check sends which trap, so on a
part location it resets that level mid-solve (the harness refunds the pass) and
anywhere else it misses. When it goes off is Core `TrapTiming`: a finished
puzzle misses until something launches it again (the retry panel's restart
included), however long its exit or the panel lasts, and so do the credits;
`tools/probe-trap-window.py` checks a settled puzzle, a load, a straight-on
finish, the panel, the four levels' own cats and the credits in game (9 of 9,
2026-09-30).

Two harness rules from this feature: position alone is not evidence (a piece
at the right coordinates can be attached to the wrong thing), and test the path
a player takes, not a shortcut that skips it.

## Navigation after a puzzle

- Play, the next arrow and the finish of the last playable puzzle all go to the
  level select when nothing is playable, with the toast `Nothing to play yet -
  waiting on items`. After a puzzle it is the game's own post-level Level
  Select (`navigation: the level select is up`); Play is already on the title
  and presses its Levels. Checked
  2026-09-28 on seeds of 10 puzzles in one pack, every location but the
  targets' sent from the server (`/send_location`) and the later targets held
  back with DevTools `revoke:`: two hand-made puzzles in a row, a generator
  and a DLC1 puzzle each landed on the track with its Close button, no title
  under it (`menus`), and the next card launched from it with one live level
  (`livelevels`).
  Checked 2026-09-29 on the kinds that were not, every location sent first
  and each target forced:
  - a holiday puzzle (GoodTidings Wreath);
  - a DLC2 hand-made puzzle (Pressed Leaves);
  - both DLC generators that repeat at most once (DLC1 Trophy Cabinet, DLC2
    Water Glasses).
  Each landed on the track, the DLC ones through their own level select,
  which `DlcGuard` turns into the run's track. With other slots playable,
  Trophy Cabinet, Water Glasses, Wreath and Pressed Leaves went on to the
  next slot with one live level. After the holiday puzzle the finished level
  stays loaded, unseen, under the track until the next launch; the DLC route
  tears it down. Sending only some slots' locations also delivers the packs
  placed there, so a "nothing playable" setup sends every location in the
  run.
- The game asks for the level already running, with no index and no reload,
  just after a puzzle finishes and as it goes back to gameplay under a
  closing level select. The mod leaves that call alone (`track: the game
  asked for the running level ... left as asked`; Core `LaunchRequest`).
  Every launch line says what the caller asked (`asked index=.. forceReload=..
  seed=..`). A reload the game means passes forceReload and keeps the slot's
  seed, as measured 2026-10-01 on DLC2 Water Glasses, slot 5, seed 1975104794:
  - a Cat Trap's reset asks index 1209 with forceReload;
  - Retry sends the empty call first, then index 1209 with forceReload;
  - the panel's Next goes on to the next slot, one live level, no relaunch.
- The retry panel's Retry Button (DevTools `press:Retry Button`) relaunches
  the same slot with its own seed: Pencils (Randomized) with one of two
  solutions found, slot 0, seed 1165097864 both times (2026-09-30).
- A finished run puzzle with nothing left to find moves on without showing the
  retry panel; one with solutions left shows it. Generators too, since
  2026-10-01: one ending for every run level (Core `AfterPuzzleRoute`).
  DevTools `trace:RetryMenu.ShowMenu,RetryMenu.NextLevel` shows the route.
- No run puzzle goes to the Daily Tidy page. The game decides that page in
  `LevelManager.OnLevelCompleteTweenOutComplete`, which every way out of a
  finished level passes through, from `IsDailyTidy`'s body compiled inline
  (so a postfix on the getter never sees it): randomized or holiday-dated,
  and not an archive level. `DailyDecision` answers that check's one real
  call, `get_IsArchiveLevel`, for the level leaving, and logs `daily
  decision: <level> leaves as a run puzzle, not a daily`. Seen with
  `trace:LevelManager.OnLevelCompleteTweenOutComplete,LevelManager.StopGame,LevelInterface.get_IsArchiveLevel`:
  on a generator before the fix, the tween-out, the getter, `StopGame(True)`,
  then the Daily state. A daily-guard rescue now logs a warning, and the
  gate fails a run that reached the page at all.
- DevTools `cursor` reads the game's own cursor state; a scripted test has no
  pointer over the window, so a screenshot cannot show it.
