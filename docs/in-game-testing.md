# In-game testing

The mod cannot be tested without the game, and the game is installed once, for
one person, with their saves and their settings in it. Every harness here runs
against that install. This is the discipline that keeps a test run from being
something the player has to clean up after.

## Put the player's environment back when the harness exits

A harness needs settings the player did not choose - a local server, a fixed
slot name, auto-connect on - and it writes them into the same BepInEx config
the player uses. It drives the game through real saves, so it also leaves run
files and completion data behind.

**What that costs when it is not restored.** CW4 hit the sharp version on
2026-09-05: a battery left `Host = localhost` and a dead port behind, the
player launched a real session, and the mod auto-connected to nothing while the
message box filled with timeouts. The mod was working perfectly; the rig had
repointed it. Here it was milder only by luck - `TargetVirtualDesktop` was
changed in the DevTools config and the player's `.run.json` was rewritten
several times to reset hint pages, and both were *announced* rather than
restored.

Announcing is not restoring. It puts the work on the person who was not doing
the testing, and it only happens at all when the change is remembered.

**Use `tools/harness_env.py`.** Wrap the harness body:

```python
from harness_env import Environment

with Environment("my-harness") as env:
    env.configure(Host="localhost", Port=38281, SlotName="droha",
                  AutoConnect="true")
    ...
```

It snapshots both plugin configs and every save-folder file on entry, and on
exit puts them back and deletes anything the run created. Restore runs on a
normal exit, on an exception and on Ctrl-C, because all three unwind the
`with`.

`env.configure()` takes effect at the **next launch** - the plugin reads its
config once at startup - so configure before launching, not after.

`tools/playthrough.py` and `tools/emptysoak.py` both use it.
`tools/deploy.sh` does not and does not need to: it builds and copies DLLs and
changes no player state.

### After a run that ended badly

A killed process does not unwind anything, so nothing is restored. Every
snapshot stays on disk for that case:

```
py -3.13 tools/harness_env.py --list
py -3.13 tools/harness_env.py --restore-latest
```

`--restore-latest` restores the newest snapshot, which is the right one
directly after a hard kill and the wrong one if you have played since. Check
`--list` first.

### Why restore closes the game first

BepInEx writes its config files on shutdown, from the values held in memory.
Restore a config under a running game and the game overwrites it seconds later
while the harness prints "restored" having achieved nothing. `close_game()`
runs before every restore for that reason, and the harness therefore leaves
the game closed.

### Finding the save folder, not spelling it out

Unity's save path is `<LocalLow>/<company>/<product>`, and both halves come
from project settings rather than from anything a person reads. The first
version of `harness_env` hardcoded `Max Inferno/A Little to the Left`, which is
how the studio's name is written everywhere visible. The real folder is
`maxinferno/A Little To The Left`.

**Nothing failed.** The glob matched no files, the snapshot contained the two
config files, and the harness printed that it had protected the environment.
It is now found by glob, and `_require_save_dir()` refuses to run at all if it
cannot be found - because the restore sweep deletes save-folder files not in
its manifest, and an empty `SAVE_DIR` makes that glob relative to the working
directory, which is the repo.

## Launching the game

Run the executable directly, with no arguments:

```
"G:/Games/Steam/steamapps/common/A Little To The Left/A Little To The Left.exe"
```

Not `steam://rungameid/...` - this copy is family-shared and Steam refuses it
with a "no license" dialog. Not with Unity command-line arguments either: the
game pops a dialog for unrecognised ones. Both failures look like a hang.

### Why the game used to open, close and open again

Running the exe directly makes Steam's DRM stub call
`SteamAPI_RestartAppIfNecessary`, which relaunches the game through Steam and
exits the process you started. On screen that is the window appearing,
vanishing and coming back, which reads as a crash on startup. For a harness it
is worse: the process it launched is gone and the log belongs to a different
one.

Measured: one launch produced PID 70840, replaced four seconds later by PID
76964.

**Fixed with `steam_appid.txt` in the game folder**, containing `1629520` -
the App ID from `steamapps/appmanifest_1629520.acf`, not from the store URL.
That is Valve's documented way to say "already running as the right app", and
with it there is one PID and no relaunch. Nothing else changes: the Steam API
still initialises.

`harness_env.ensure_no_steam_relaunch()` writes it before any launch, and the
file is deliberately left in place rather than restored - it is one line and
it fixes a real annoyance for whoever plays this install next. Delete it if
you ever want the Steam-relaunch behaviour back.

## Deploying a build

`tools/deploy.sh` closes the game before copying, always. Windows will not
overwrite a DLL a running process has loaded, so building with the game open
compiles cleanly, fails only in the copy, and leaves the OLD plugin in place.
That once cost a full round of testing against a build that did not contain
the fix being tested, with a green "0 Error(s)" on screen throughout.

`tools/deploy.sh --no-kill` compiles without closing a running game and reports
the copy failure rather than hiding it. Use it to check that a change builds
while someone is playing.

## Harnesses measure nothing until you check that they can

Two of the harnesses here have reported a clean result while being incapable of
producing a dirty one:

- `emptysoak.py` moved on from each level after about a second. The watchdog it
  depends on only reports a level empty after a **continuous six seconds**, so
  the counter never reached its threshold. It ran 40 loads, reported "0 empty",
  and had measured nothing. `DWELL` is now 7.5 seconds.
- The compliance suite was almost swapped for `unittest -k "A Little to the
  Left"`, which matches the three tests that generate a class per world and
  silently skips `test_fill`, `test_ids` and `test_reachability` - those loop
  over worlds inside the test body. It looks like a 100x speedup and is mostly
  a 100x reduction in coverage.

Before trusting a green run, make the harness fail on purpose. This is not
advice held in reserve - it caught a third case on 2026-09-06.
`tools/offline-reconnect-test.py` passed on its first green run and was
measuring nothing: it read only the log AFTER `connecting to localhost`, and
the wipe it looks for is logged just BEFORE it. Rebuilding the mod with the
bug deliberately put back is what exposed that, and the same control then
revealed that the ORIGINAL code never produced the failure the change had been
written up as fixing. Two findings, both from one control run that took four
minutes.

## Two traps that make a harness look broken when it is not

- **`SKIP_REQUIREMENTS_UPDATE=1` is needed locally, not only in CI.**
  `MultiServer.py` and `Generate.py` both call `ModuleUpdate.update()`, which
  does not fail on a drifted requirement - it PROMPTS. With no console the read
  is an EOFError and the process dies before binding, and the only symptom the
  harness sees is `No response from localhost:38281`. Every subprocess that
  runs Archipelago code needs the variable in its environment.
- **BepInEx truncates `LogOutput.log` on every launch.** A harness that records
  the log size before launching and seeks to it afterwards is seeking past the
  end of a new, shorter file, and reads nothing. `Log.new()` in these harnesses
  resets to 0 when the file shrinks for this reason; a quick script written
  without that will silently report no output at all.
