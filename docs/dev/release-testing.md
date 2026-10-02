# Testing a release

How to check that what a player downloads actually works, automatically or by
hand. Both routes install the same three files the same way.

Do this before tagging anything. Every other test in this project tests the
working tree; this is the only one that tests the artifacts.

## When to run it, and when not to

**This is a release gate, not an iteration loop.** A real game, a real
MultiServer, fifteen puzzles played to the credits: about 10 minutes (9.9 on
2026-09-27, down from 18.9 that morning; see "Where the time went" below).

### What each mode covers

The full run is a superset: nothing `--quick` or `--only-arrow` checks is
missed by it.

| | full | `--quick` | `--only-arrow` |
|---|---|---|---|
| install from the release files, seed checked on paper | yes | yes | yes |
| arrow session: the next-level arrow, the pause-menu Exit, two launches | yes | no | yes, and only this |
| 15 puzzles played to the credits, goal reported and seen by the server | yes | yes | no |
| ability locks on: gates met and refused (5 on the 2026-09-27 seed) | yes | no | - |
| cat traps on: puzzles knocked over mid-solve (2 on that seed) | yes | no | - |
| checks asserted | 30 | 28 | 2 |

`--quick` exists for iterating on the harness, where locks and traps are
noise. For a release, or after any change to the locks or traps, run the
full gate.

### Where the time went

Timed line by line on 2026-09-27 (18.9 minutes, 24 visits). Most of it was
fixed pauses and waits that could not end early:

- **Waits that watched only for NEW log lines, for a line already read.** The
  mod files a level's checks and Beaten token as it completes, so the solve
  had read them, and the wait after it sat out its full 10 s on 20 of 24
  visits. The goal report waited 60 s for a line the credits click had read.
  Both now look in what was read first.
- **Fixed settles.** 6 s after every boot (now: poll until the level runs and
  the lock's passes go quiet), 1.5 s after every controller or lock listing
  (now: until the header's rows have all landed), 0.9 s after every forced
  solve (0.5), polls every 0.25 to 0.5 s (0.05 to 0.1).
- **A progressive level needs the next listing to see its next phase.**
  TupperwareNesting registers one controller per phase; with listings no
  longer 2.5 s apart the harness read "6 of 6 solved" before the seventh
  registered, so the completion wait now re-lists every 3 s and goes back to
  solving when a phase appears.

## It moves only the way a player can

Since 2026-10-01 every visit leaves and enters a level by the player's own
buttons, never a DevTools shortcut (`boot:`, `menu:title`, `replayselect`):

- a finished level: the retry panel's Menu, then the pause menu's Levels;
- a running level: the pause menu, then Levels;
- the title: its Levels;
- the next puzzle: a click on its card on the run's track (`clicktrack`);
- where the mod moves on by itself (a generator with nothing left), the
  gate waits for it, and plays on in the level the mod opened if it is the
  planned one.

The old route (post-level Level Select, Close, a forced `menu:title`, then
`boot:`) also pressed the post-level Level Select on a level the mod had
already moved on from - a button no player can reach. It built the track
twice and left it drawn over the title (the DLC gate of 2026-10-01). A test
of a route no player takes is not a test of the mod. `boot_level` and
`to_title` stay in `tools/release_e2e.py` for the probes, which measure one
level; the gate's own functions (`play`, `check_arrow`, `to_track`,
`open_slot`) are pinned by `test_scheduler.py` to use none of them.

On 2026-09-08 it was run roughly twelve times to land one set of playtest
fixes. In about ten of those the question was only "did this small change break
progression or add an error", which needs none of the fifteen puzzles, the
throwaway arrow session, or the goal report. It also reads badly at that size:
cat traps and a live server make every run vary, so a flaky arrow session cost
one run outright and muddied two others - noise mistaken for signal, because
the instrument was much larger than the question.

What to reach for instead is the smallest rung of the test ladder in
`CLAUDE.md` that can see the bug: Core tests, the apworld suite, the fill
stress sweep, a probe on one level, then `--quick`, then this in full.

## Before it launches: the seed is checked on paper

The seed NUMBER is fixed; what it generates moves whenever levels.json or the
ids change. So step 4 generates from seed 20260906 up and plays each on paper
first: the harness's own scheduler (`choose_slot`) over the seed's spoiler,
with the arrow check's slot already beaten, packs, Skips and cat traps where
the spoiler puts them. It takes the first seed that clears every slot and
says why it passed over any other, for example:

    seed 20260906: the paper plan clears only 13/15
       Shells needs Symmetry, on Pencils (Randomized) - Solution 2: an
       alternate solution, which only a Skip releases
    seed 20260907: the paper plan clears 15/15 in 24 visit(s), 2 Skip(s),
       3 cat-trap reset(s)

With the arrow check in the run (not `--quick`), a seed must also open with a
slot that ends on the panel's arrow: one the session can finish. Generators
count since 2026-10-01: every run level now ends the same way, by the
panel's route, and the forced finish no longer relaunches a generator. A seed
whose opening holds only levels forcing cannot finish is skipped:

    seed N: no slot in the opening ends on the arrow (every one is a level
       forcing cannot finish), so the arrow check would test nothing

`tools/make-seed.py` uses the same walk, so `test_harness_data.py` reads
exactly the seed the gate will play. No seed in 20 clearing refuses the run.
`tools/predict_gate.py` then reads that seed's pre-flight as the assertions
it implies.

The paper plan's per-level facts come from `fixtures/forceability.jsonl`:
every level forced alone by `tools/probe-forceable.py --all` (then
`--recheck` and `--groups-rest`, about four hours on 2026-09-24): which
levels forcing cannot finish, how long each takes to complete, which finish
on one group and in what controller order, which register fewer controllers
at load than the table lists. Every stop of the 2026-09-24 gates was one of
those, found one full run at a time; `test_scheduler.py` now fails in
milliseconds if the harness disagrees with the fixture. Re-measure when
levels.json changes; `tools/probe-forceable.py [--dlc]` re-checks just one
seed's levels. `--skip-all` adds each level's `skip` field: what the game's
own SkipLevel does to it alone, with no run up.

`--only-arrow` runs steps 1-5 for real (clean install, assets, world, seed,
the arrow session) and stops with the arrow session's two checks: the gate's
in-game setup on its own, in about three minutes.

### It never goes to the Daily Tidy page

A check of its own since 2026-10-01: the run's log must hold no
`SetGameState: DailyTidy_GameState` and no daily-guard rescue. A player
cannot open that page in a run, and every finished generator used to go
there and be pulled out (the quick gate of 2026-10-01: 6 of 6). The mod now
answers the game's own decision (`DailyDecision`), so the rescue is a
backstop that should never run, and it warns when it does.

### No puzzle opens under the post-level panel

A check of its own since 2026-10-02: no `Initialized Level:` while the game
is still in `RetryUI_GameState`. Every route off the panel leaves that state
first; a Skip on a level the game sends straight on (Cupcakes, Water
Glasses) did not, and the next puzzle opened with the panel over it. The
13:33 DLC gate that day passed with two of them because the harness
recovered; the next stopped on one. Core `AfterPuzzleRoute` now keeps the
game's own route for such a Skip.

## Reading it while it runs

Every counter carries its total. Setup steps are `[setup 2/4 assets]`, the
test's own `[gate 1/3 arrow]`; during play every line is
`[gate visit 4/24 | 4/15 beaten]`, the visit counted against the
paper plan; the verdicts are `[check 12/28] PASS ...`.

It stops itself, loudly, instead of improvising: a visit that is not the
planned one, a Skip about to land on a level other than the one it was bought
for, or a Skip that did nothing. Each writes a marker and fails a named
check. Unit test what it found, fix it, then run again.

It WARNS, without failing, for every solve it sends while the game says it
is paused or its clock is at 0 (`WARNING: N solve(s) sent while the game was
paused`), because a paused game holds every event. A pause alone is not
reported: going to the title pauses the game on its own and `boot` undoes
it. The game logs of the arrow session and the main run are kept in
`testserver/logs/` as `e2e-<stamp>-arrow.log` and `e2e-<stamp>.log`.

On each level it forces only what the mod has not greyed AND the seed's logic
has reached (`not forcing X - the seed's logic has not reached it`), the same
rule the paper plan uses. A group greyed although the table calls it free is
an understated requirement, the kind that softlocks a seed, and shows up as
the run falling behind its plan (Fruit Stickers, 2026-09-24). Radial Dance
Party registers nothing until a player starts it, so seeds holding it are
passed over; it is played by hand.

A Skip works on every level. With no run up, the game's own SkipLevel
completes all 173 levels inside the call and raises LevelSkipped
(`probe-forceable.py --skip-all`, 2026-09-25). In a run it will not skip a
generator level: Pencils reads `Skippable` False there and nothing happens.
So where the game has not skipped by the time SkipLevel returns and the level
says it is not skippable, the mod releases every location on the card,
spends the Skip and goes back to the run's track. The harness may buy a Skip
on any level, and stops the run if one does nothing.

**A short reproducer must do REAL solves.** Three were written during that
session and all three used DevTools' `complete` instead of solving the
controllers. `complete` does not produce the post-level state a real solve
does, so `replayselect` did not land where it lands in a real run, and all
three came back clean while the bug reproduced every time in the full e2e.
Three false negatives in a row is what drove the twelve full runs.

`--quick` answers that question instead: the same fifteen puzzles, no
throwaway arrow session, ability locks and cat traps off, every correctness
check kept - the error census, the
mod-versus-harness reconciliation, the campaign-save isolation. What it gives
up is coverage of the arrow, the pause-menu Exit and the launch count, which
are the parts that need a second session. Use it for "did this edit break
anything"; use the full gate to sign off a release.

```
PYTHONUNBUFFERED=1 py -3.13 -u tools/release_e2e.py --quick 2>/dev/null
```

### The gate runs a self-test first

`self_test()` runs before anything is installed and exits on the first
disagreement. It costs milliseconds and it exists because every expensive
failure this harness has had was its own parsing, not the mod: a listing read
before it finished printing, a name left behind by a splice, a hard-coded pack
count no seed could satisfy, and two string literals that were supposed to
match and did not. Add a case here whenever a run fails for a reason that was
knowable without launching the game.

### Proving the skip path without a full run

`tools/probe-skip-path.py` answers one question in about two minutes:
can the harness get past a level it cannot force-solve?

`solve:` sets a controller's solved flag. That finishes most levels and does
not finish a PHASED one - PawPrints registers five controllers, all five
solve, all five checks fire, and `PawPrintsPhaseLevel` still never raises
`LevelComplete`, because its phase machine wants the real solve path. The mod
is right to bank no Beaten token. Phased campaign levels became drawable in
0.3.1, so a run that draws one used to stall for nineteen rounds and then fail
six assertions that had nothing wrong with them. The gate spends a Skip
instead, which since 0.3.1 finishes the slot and counts toward the credits.

It loads `release_e2e.py` and calls the real `solve_level`, deliberately: the
bug it guards against WAS a copy that had drifted out of step. Expect

    solve_level: done=False exhausted=True
    skip spent=True  mod banked a Beaten token=True

It needs a seed in `testserver/out-e2e`, which the gate leaves behind, and the
release installed. It cheat-sends the Skip rather than hoping the seed placed
one early, so it measures the path and not the placement.

This is the exception to "a short reproducer must do REAL solves" above, and
only because a forced solve failing to complete the level IS the condition
under test.

### What a Skip does NOT prove, and the one manual check

A Skip banks the slot's Beaten token, so a skipped level is indistinguishable
from a solved one in every count the gate prints. A run can go green having
never actually solved a quarter of its puzzles.

The gate now says so rather than leaving it in the transcript. It keeps a
ledger of every Skip it spent and asserts two things about it:

- **a Skip was spent only where one is known to be needed.** The allowlist is
  `KNOWN_UNFORCEABLE` in `release_e2e.py`. A new name failing this check is
  far more likely to be a regression in solve routing or in the controller
  table than a genuinely unforceable level - measure before allowlisting.
- **no Skip covered for a level the mod was still gating.** This is the one
  with teeth. An ability-gated level reaches the skip path looking exactly
  like an unfinishable one: every controller the player can reach is solved.
  Without this check, a mod that wrongly withheld an ability would be papered
  over by the Skip and the run would pass. The harness must never buy its way
  past a gating bug.

Neither check proves a HUMAN can finish those levels, and no harness here can.
A real solve needs the drag path - `DragObject` has no `OnDrag`, so synthetic
pointer input never starts the settle tween that `Snap()` does. So this stays
manual, once per release:

> Generate a seed containing each level in `KNOWN_UNFORCEABLE`, grant the
> abilities that level's table DECLARES and no others, and finish it by hand.
> Confirm the level completes, the mod banks a Beaten token, and the checks
> reach the server.

`tools/handtest-level.py <index> <ability ...>` does the setup: it serves a
seed holding exactly the abilities named and boots the level.

**Grant the declared set, not every ability**, which is the one thing worth
being strict about. Granting everything answers "does the level work", which
is not the risk. The risk is a table that UNDERSTATES what a level needs -
then objects stay dimmed for a player holding exactly what it asks for, and
the puzzle cannot be finished at all. A solve performed while holding extra
abilities cannot see that. It is why an earlier TupperwareTower playthrough
did not settle the question: it was done when that level's abilities were
overstated.

**Result, 2026-09-15.** Both levels beaten by hand, both with `0 locked`:

| Level | Held | Outcome |
|---|---|---|
| TupperwareTower | Grids, Stacking | Solution 1 + Beaten, nothing dimmed |
| Desktop Computer | Containers, Gadgets, Rotating, Swapping | 5 parts + Solution 1 + Beaten |

So both tables are sufficient and both levels are sound. The Skips the gate
spends on them are a harness limitation and nothing more - for Desktop
Computer, specifically, `Computer Errors` is a sequence the player starts and
finishes rather than an arrangement, so forcing its flag sets a bit for a
sequence that never ran.

Re-run this when a level's declared abilities change, or when solve routing
does. Skipping it is a reasonable call for a patch release that touched
neither. Skipping it silently is not - say so in the release notes, because
the gate's green does not cover it.

### The level table against the game, which nothing runs for you

`tools/check-game-facts.py` compares `apworld/alttl/data/levels.json` with a
dump taken from the running game. It fails loudly when it disagrees - and
**nothing invokes it**: not CI, not `package-release.py`, not the gate. The
table's agreement with the game has been a convention, not a gate, which is
the same shape as every other finding in this file.

The dump has to be taken with the mod **moved out of `BepInEx/plugins`
entirely**, because the daily guard answers `IsDailyTidy` false while a run is
active - a dump taken with the mod loaded reports zero daily levels and looks
like proof there are none. That has already nearly been written into
`levels.json` once. Renaming the folder does not disable it; BepInEx scans
every subdirectory.

**`tools/levelsweep.py` does the parking now**, which is the half that used to
be left to memory. It moves the folder out, launches with DevTools only, runs
the command, closes, and then **reads the log back to prove the mod never
logged a line** before restoring it. A person following the same steps cannot
easily check that last part, and it is the part that failed.

    py -3.13 tools/levelsweep.py --dump      # for check-game-facts.py
    py -3.13 tools/levelsweep.py --survey    # the prefab walk
    py -3.13 tools/levelsweep.py             # the runtime sweep, all levels
    py -3.13 tools/levelsweep.py 1235 1236   # just those indices

So it is a release step, and it belongs here rather than in someone's memory:

> Before a release that touched `levels.json`, `names.json` or
> `abilities.json`: `py -3.13 tools/levelsweep.py --dump`, then
> `py -3.13 tools/check-game-facts.py <the file it names>`.

Skip it for a release that touched none of those three. Say so in the notes if
you skip it, for the same reason as the hand-solve above.

**Never copy a fresh sweep over `levels.json`.** A full sweep is a worse copy
of that file than the one in the tree: it drops the phased controllers the
2026-09-08 audit restored by hand and puts back the prefab ghosts it removed -
measured 2026-09-17, it disagreed on five levels and the shipped side was right
every time. Merge new rows in with `tools/merge-levels.py`, which appends only
levels the table does not already have and refuses to write if an existing row
would change.

### DLC

`tools/probe-dlc.py` covers the run-time half of DLC support in about two
minutes, and nothing else does: it generates a Seeing Stars seed, connects,
and launches a **star-gated** puzzle from the run's own track, checking that it
registers controllers and sends checks.

Worth running before a release that touches the level table, the track or the
connect guard. The three things it settles were all open questions:

- a DLC level plays without the game being put into DLC mode,
- its checks reach the mod,
- the mod clears the star gate the game puts on five Seeing Stars puzzles.

That last one has a failure mode worth knowing: a locked level does **not**
throw or return false. `LevelManager.SetActiveLevel` redirects to the DLC level
select and returns, so a broken gate looks like a puzzle that simply never
opens. The probe is sized so a gated level is always drawn - twenty slots from
Seeing Stars alone, measured 20 of 20 - rather than rolling seeds and hoping,
which an earlier version did and missed twelve times running.

### The other in-game checks

The gate cannot do these, so they are named here to be remembered. All of them
are in [testing.md](testing.md):

- **Hint Pages** - the erasing needs hands on a mouse.
- **Cat traps** - worth re-reading for any release that touched `Traps.cs`:
  the trap has been wrong three times, and each time it passed a test first.
- **Ability locks** - run `tools/probe-lock-roundtrip.py` on any release that
  touched `AbilityLocks.cs` or `ObjectLock.cs`.
- **Background Reset Token, navigation after a puzzle** - the checks listed
  there.

A harness cannot tell "no player can earn this" from "I cannot pull a drawer
open", so a force-solve probe must never be read as evidence that a location
is dead ([history/manual-container-test.md](../history/manual-container-test.md)
has the case that taught it).

## The automatic route

```
PYTHONUNBUFFERED=1 py -3.13 -u tools/release_e2e.py 2>/dev/null
```

Seven phases: clean the install to vanilla, install the mod from its zip,
install the world from its `.apworld`, generate a 15-puzzle seed,
launch and connect, play the run to the credits, and check the campaign save
was never written.

15 puzzles: three blocks of 5 (the option's floor is 10, two full packs).
The yaml asks for `pack_size: 5`, the option's floor; the generator decides
how many packs that buys (the cap is 3 at 15 puzzles): 5 open free and two
packs of 5 (boundaries `[5, 10, 15]`).

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

### Thunderstore

`package-release.py` also writes `<out>/thunderstore/A_Little_to_the_Left_Archipelago-<version>.zip`:
the same five DLLs at the zip root with a generated `manifest.json`
(dependency `BepInEx-BepInExPack_IL2CPP-6.0.755`), `thunderstore/icon.png`,
`thunderstore/README.md` and this version's CHANGELOG section with a link to
the whole file (Thunderstore refuses a text file over 100000 characters; the
whole changelog is over 200000). It is not a GitHub asset.

- **Upload the zip from the same run as the GitHub assets.** A rebuild from
  the same source does not give byte-identical DLLs (1.0.0, 2026-10-02:
  three of five differed), so a later rebuild would not be what GitHub
  players run.
- Upload by hand at thunderstore.io (Upload), under the **drohack** team, to
  the "A Little to the Left" community, categories **Mods** and
  **AI Generated** (Thunderstore's Global Rules). The package:
  [drohack/A_Little_to_the_Left_Archipelago](https://thunderstore.io/c/a-little-to-the-left/p/drohack/A_Little_to_the_Left_Archipelago/);
  1.0.0 was the first upload (2026-10-02). The package name cannot change.
- r2modman lists a new version once Thunderstore rebuilds the community's
  listing index (`/c/a-little-to-the-left/api/v1/package-listing-index/`;
  about 30 minutes for 1.0.0) and the manager's own copy is refreshed
  (Settings, Refresh online mod list).
- Then install and launch it once through r2modman. The game starts with
  r2modman's doorstop arguments (checked 2026-09-30: title screen, no
  dialog), unlike the Unity arguments in testing.md. An r2modman install
  runs from its profile, not the game folder: its log is
  `%APPDATA%/r2modmanPlus-local/ALittleToTheLeft/profiles/<profile>/BepInEx/LogOutput.log`,
  its config beside it, and it has no DevTools.

## The manual route

Same thing, by hand. Takes about ten minutes.

**1. Clean the install.**

```
py -3.13 tools/release_e2e.py --clean-only
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

**4. Generate.** Copy `apworld/alttl/player.yaml`, set `puzzle_count: 15`,
`levels_to_beat: 15`, `pack_size: 5` for a short run (the floor is 10), and:

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
| the mod loaded | `features live: save redirect, connection pane, track, skips, hints, navigation, daily guard, title screen, ability locks, card stars, section stars, overview strip, success stars, retry panel, steam achievements, cursor guard` in `BepInEx/LogOutput.log`, and no `PATCH FAILED` |
| the seed came through | `connected. 15 puzzles, 2 packs of 5, beat 15 to unlock the credits` |
| the track is gated | `track: 15 puzzles, 5 open, 2 packs` - five of fifteen, not all fifteen |
| checks reach the server | `checks: sent 1, 0 still owed` |
| packs open more | `track: 1/2 packs, 10 puzzles open (+5)` |
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

`tools/release_e2e.py` drives the game through DevTools' command file, and
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
- **A patched class does not mean a patch was installed.** `Plugin` patches
  class by class from a hand-written list, and a class that is never added to
  that list has NONE of its Harmony attributes applied - silently, with a
  perfectly normal startup. `DlcGuard` shipped that way through the whole DLC
  feature: two prefixes, neither ever installed, while the file accumulated
  comments explaining why one of them "never fires". The game announced it at
  every launch and nothing read the line:

      features live: save redirect, connection pane, track, skips, hints,
                     navigation, daily guard, title screen

  Eight names, and `dlc guard` was not one of them. Two checks now cover it,
  and BOTH are needed because each sees a failure the other cannot:
  `tools/check-patches.py` reads the source and fails when a Harmony class is
  never registered or a `Tick` is never called (it cannot see runtime), and
  `release_e2e.patch_problem()` reads the launch and fails on `PATCH FAILED` /
  `FEATURES DISABLED` or a missing feature (a class can be registered, carry
  attributes, and still be refused at runtime - registering `DlcGuard`
  produced `PATCH FAILED, dlc guard IS DISABLED: IL Compile Error`). Every
  probe calls the second one before it measures anything.
- **A green build does not mean the game is running your code.** The deploy
  step cannot overwrite a loaded DLL, so building while the game is open
  leaves a fresh `bin/Release/ALTTLArchipelago.dll` and a STALE one in
  `BepInEx/plugins/`, and the build still prints "Build succeeded". Any test
  launched afterwards measures the old mod and reports a clean, plausible,
  wrong answer. This cost a false negative on 2026-09-18: a check for the
  thirteenth ability icon read `badges: loaded 12 ability icon(s)`, which was
  a correct reading of a DLL that predated the icon. When a measurement
  disagrees with a change you just made, compare the mtime and size of the
  deployed DLL against `bin/Release/` BEFORE debugging the change.
- **A log line you care about can be consumed by an unrelated wait.** The pack
  announcement arrives mid-puzzle, so the harness re-derives progress from the
  whole transcript rather than the newest chunk.
- **Menu navigation after finishing a puzzle is the hard part.** The Menu
  button opens the pause menu, `menu:title` does not instantiate a title
  screen, and `clicktrack` starts nothing until the track is the live one
  on screen. The gate reads the open menus, not the state, to decide its
  next press (`route_step`). DevTools `pause` enters `Menu_GameState` and
  pauses the game, as a player's Esc does (measured against droha's Esc,
  2026-10-01). Its old form only raised the menu-open event, which left the
  state at `Gameplay_GameState`; a Reset or Skip from that menu then
  switched to gameplay "already active" and nothing took the menu down,
  a pause no player can reach. The gate no longer boots; the
  notes below on `boot:` and the unwind apply to the probes that still do.
- **The next-level arrow once poisoned the session it was pressed in.**
  Measured as a pair, everything else identical: with a single press, 2 of 8
  beaten and 103 exceptions; without one, 8 of 8 and none.

  That was measured while the arrow launched through the mod's own
  `GoToNext` (since deleted), which started a level without releasing the
  previous one, and while the gate opened levels with `boot:`, whose teardown
  only sees ACTIVE levels. The left-over level's `CheckWinCondition` stayed
  subscribed to the event bus and the next level's solves died inside it.

  Both causes are gone. The arrow is now the game's own `RetryMenu.NextLevel`
  (Navigation lets the game advance), and since 2026-10-01 every run puzzle
  with nothing left to find moves on through that same call, a dozen times a
  gate, and the gate never boots. `check_arrow()` still verifies the arrow in
  a session of its own, which also checks a fresh launch's pause-menu Exit;
  merging the two would need a gate run to show it is safe.

- **A probe that boots unwinds to the TITLE after every puzzle, not just out
  of the level.** `replayselect` -> `menu:levels` -> `menu:title`, and only
  after a FINISHED level (the post-level button exists only then). Shorter
  exits before a `boot:` do not work:
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
