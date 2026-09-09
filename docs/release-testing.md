# Testing a release

How to check that what a player downloads actually works, automatically or by
hand. Both routes install the same three files the same way.

Do this before tagging anything. Every other test in this project tests the
working tree; this is the only one that tests the artifacts.

## When to run it, and when not to

**This is a release gate, not an iteration loop.** A full run is about fifteen
minutes: a real game, a real MultiServer, eight puzzles played to the credits.

On 2026-09-08 it was run roughly twelve times to land one set of playtest
fixes. In about ten of those the question was only "did this small change break
progression or add an error", which needs none of the eight puzzles, the
throwaway arrow session, or the goal report. It also reads badly at that size:
cat traps and a live server make every run vary, so a flaky arrow session cost
one run outright and muddied two others - noise mistaken for signal, because
the instrument was much larger than the question.

What to reach for instead:

| Question | Tool | Cost |
|---|---|---|
| Did the logic change? | `dotnet test src/ALTTLArchipelago.Core.Tests` | ~1s |
| Did generation or the id tables change? | the apworld suite | ~7s |
| Does the option surface still fill? | `ALTTL_STRESS_SEEDS=25` fill stress | ~30s |
| Did I break the run or add errors? | a small reproducer, see below | minutes |
| Is the release good? | this, in full | ~15 min |

**A short reproducer must do REAL solves.** Three were written during that
session and all three used DevTools' `complete` instead of solving the
controllers. `complete` does not produce the post-level state a real solve
does, so `replayselect` did not land where it lands in a real run, and all
three came back clean while the bug reproduced every time in the full e2e.
Three false negatives in a row is what drove the twelve full runs.

A `--quick` mode - two puzzles, no arrow session, keeping the error census and
the mod-vs-harness reconciliation - would answer the common question in three
or four minutes. It has not been built.

## The automatic route

```
PYTHONUNBUFFERED=1 py -3.13 -u tools/release-e2e.py 2>/dev/null
```

Seven phases: clean the install to vanilla, install the mod from its zip,
install the world from its `.apworld`, generate an 8-puzzle two-pack seed,
launch and connect, play the run to the credits, and check the campaign save
was never written.

The whole run happens in **one game launch**, and that is asserted rather than
hoped for - it took two fixes to get there and both are easy to undo by
accident.

It **cleans first, not after**, so re-running is always valid whatever the
last run left behind, and it leaves a working install to poke at.
`--clean-only` puts the install back to vanilla.

`--assets DIR` says where the three release files are. The default is
`release-test/`.

### Getting the assets

The `.apworld` can come straight from CI, which is the closest thing to what a
player downloads:

```
gh run list --limit 5
gh run download <run-id> --name alttl-apworld --dir release-test
```

The mod zip cannot: it needs the game's interop assemblies, which a public
runner does not have and which must never be committed. Build it locally:

```
py -3.13 tools/package-release.py --out release-test
```

That also writes an `.apworld` - overwriting a downloaded one. If you want to
test the CI artifact specifically, copy it back over afterwards.

**The two builds are not byte-identical**, and that is worth knowing before it
surprises you. Three JSON files differ by exactly their line count: git checks
out CRLF on Windows and LF on the Linux runner, so the Windows build embeds
CRLF. The parsed content is identical and both load fine. It does mean
`build_apworld.py`'s "byte-identical output" only holds per-platform.

## The manual route

Same thing, by hand. Takes about ten minutes.

**1. Clean the install.**

```
py -3.13 tools/release-e2e.py --clean-only
```

This removes the mod, its config, every `save_ap_*` run file and the installed
`.apworld`. It does **not** touch `save1.json` - your campaign - and it leaves
DevTools alone.

**2. Install the mod.** Extract `ALTTLArchipelago-<version>.zip` into the game
folder, the one with `A Little To The Left.exe` in it. Files should land in
`BepInEx/plugins/ALTTLArchipelago/`.

**3. Install the world.** Put `alttl.apworld` in `Archipelago/custom_worlds/`,
and make sure there is no `Archipelago/worlds/alttl/` folder - a loose copy
satisfies the import and the packaged world never gets exercised.

**4. Generate.** Copy `apworld/alttl/player.yaml`, set `puzzle_count: 8`,
`levels_to_beat: 8`, `pack_size: 2` for a short run, and:

```
cd Archipelago
SKIP_REQUIREMENTS_UPDATE=1 python Generate.py --player_files_path <yaml dir> --outputpath <out dir>
```

`SKIP_REQUIREMENTS_UPDATE=1` is not optional. Without it `ModuleUpdate` prompts
to install a drifted requirement and dies on EOF, which looks exactly like an
unrelated failure.

**5. Serve.**

```
SKIP_REQUIREMENTS_UPDATE=1 python Archipelago/MultiServer.py --port 38281 <out dir>/AP_*.zip
```

**6. Play.** Launch the game by running the exe directly, no arguments. Open
the Archipelago entry on the main menu, enter `localhost:38281` and your slot
name, and press Connect.

## What to look for

| Claim | Where you see it |
|---|---|
| the mod loaded | `features live: save redirect, connection pane, track, skips, hints, navigation, title screen` in `BepInEx/LogOutput.log`, and no `FEATURES DISABLED` |
| the seed came through | `connected. 8 puzzles, 2 packs of 2, beat 8 to unlock the credits` |
| the track is gated | `track: 8 puzzles, 4 open, 2 packs` - four of eight, not all eight |
| checks reach the server | `checks: sent 1, 0 still owed` |
| packs open more | `track: 1/2 packs, 6 puzzles open (+2)` |
| the goal is reported | `goal: reported to the server`, and the server prints that the slot has completed |
| the campaign is untouched | `save1.json` unchanged; the run is in `save_ap_<slot>_<seed>.json` |

## Two things that look like bugs and are not

**A puzzle you cannot finish.** `abilities: 1 locked, 3 open, 42 objects,
waiting on Containers` means the level has a controller behind an ability you
have not received. It pays out its other checks and cannot be completed until
the item arrives. That is the ability lock working; move on and come back.

**A puzzle that resets itself.** `trap: 1 cat(s) reset the puzzle` is a Cat
Trap. It costs time, never progress.

## Notes on the harness, if you extend it

`tools/release-e2e.py` drives the game through DevTools' command file, and
nearly every bug found while writing it was in the harness rather than the
mod. The ones worth knowing about, all recorded in comments at the site:

- **BepInEx truncates `LogOutput.log` on every launch.** An offset taken before
  a launch points into a different file. The harness deletes the log before
  launching instead.
- **A log line's first bracket is the BepInEx prefix**, not your data.
  Parsing `[3] ChalkPurple ... solved=False` by splitting on `[` yields
  `Info   :ALTTL Dev Tools`. There is a self-test for that parser now, run on
  every start.
- **Waiting for a header is not waiting for the list.** `controllers:` appears
  before the per-controller lines.
- **A log line you care about can be consumed by an unrelated wait.** The pack
  announcement arrives mid-puzzle, so the harness re-derives progress from the
  whole transcript rather than the newest chunk.
- **Menu navigation after finishing a puzzle is the hard part.** The Menu
  button opens the pause menu, `menu:title` does not instantiate a title
  screen, and `clicktrack` resolves card names correctly while starting
  nothing. `boot:<levelIndex>` is the route that works from anywhere.
- **Never press the next-level arrow in the same session as the run.** One
  press poisons everything after it. Measured as a pair, everything else
  identical: with a single press, 2 of 8 beaten and 103 exceptions; without
  one, 8 of 8 and none.

  The arrow launches through the mod's `GoToNext`, which starts a level
  without releasing the previous one; leaving a puzzle through the MENUS then
  deactivates it without destroying it, and `boot:`'s teardown only sees
  ACTIVE levels, so it skips it. The skipped level's `CheckWinCondition` stays
  subscribed to the event bus and the next level's solves die inside it. The
  log shows it exactly: the boot after a menu exit prints no teardown line.

  So `check_arrow()` verifies the arrow in a throwaway game and closes it, and
  the run gets a clean one. Two launches. Do not merge them back together to
  save ninety seconds.

- **Unwind to the TITLE after every puzzle, not just out of the level.**
  `replayselect` -> `menu:levels` -> `menu:title`. Shorter exits do not work:
  nothing at all, or the pause menu's Level Select, both leave the run
  throwing after two puzzles, and `replayselect` alone made a passing level
  start failing. After a BEATEN level the pause menu's Level Select does
  nothing at all - the state stays `RetryUI_GameState`.

- **`boot:` tears down the level that is `activeInHierarchy`**, not
  `ActiveLevelInterface`. A finished level is no longer the active one, so its
  handler stayed subscribed. Necessary but not sufficient - it is the other
  half of the unwind above.
  Do not widen the filter further: the scene holds 186 level prefabs and 107
  pooled `<level> Interface(Clone)` objects, all inactive, and destroying those
  breaks the levels they belong to. Filtering on `scene.IsValid()` destroys all
  293 and filtering out only the prefabs still destroys the 107 - both were
  tried, and the second one WAS the third-level failure for a while.
