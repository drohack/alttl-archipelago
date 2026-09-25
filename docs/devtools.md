# ALTTLDevTools: the research and survey plugin

`src/ALTTLDevTools/` is a BepInEx plugin that reads and writes files and does
not change the game. It is never shipped with the randomizer and is installed
separately, by hand.

It exists because almost every fact this project relies on was measured rather
than assumed - the level table, the controller survey, what a level select
does when a card is locked. Most of the commands below were written to settle
one argument each.

Build (game must be closed):

```
cp src/GameDir.props.example src/GameDir.props   # point it at your install
dotnet build src/ALTTLDevTools
```

Drive it by writing a command into `<game>/BepInEx/alttl-devtools-commands.txt`:

| Command | Effect |
|---|---|
| `dump` | Write the full level / daily / archive / DLC table to `BepInEx/alttl-dump.json` (also runs automatically at the main menu) |
| `solutions` | Load all 186 level prefabs and record their object controllers to `BepInEx/alttl-solutions.tsv` |
| `state` | Log the current game state and active level |
| `boot:<index>[:<seed>]` | Launch any level with an optional forced procedural seed. Tears down every live level first, including a `Level` left in the scene after an exit to the title, and refuses to start if any survive. Lifts a Seeing Stars star gate in memory (a locked level otherwise loads a chapter header), undoes the game's own pause (`GameManager.Pause`, which holds every gameplay event) and resets a stopped `Time.timeScale` |
| `complete` | Force-complete the active level |
| `menu:title` / `menu:levels` / `menu:archive` / `menu:daily` | Jump to a menu |
| `unlocks` | Write the campaign unlock/completion state and chapter membership |
| `sections` | Log the level-select sections and every track icon's lock state |
| `levelsweep` | Boot every level in turn and record its RUNTIME controllers to `BepInEx/alttl-levels.json`. MERGE that into `apworld/alttl/data/levels.json` with `tools/merge-levels.py` rather than copying it over - a fresh sweep regresses the hand-audited phased levels. See `apworld/alttl/data/README.md` |
| `levelsweep:<i1,i2,...>` | The same, for just those level indices. Re-measuring a handful costs two minutes instead of twenty |
| `gensweep:<index>[:<n>]` | Regenerate one procedural puzzle n times and record how its layout varies |
| `cardlabels` / `cardlabels:off` | Put the level name under each level-select card |
| `solve:<controller>` | Force a controller in the RUNNING level to its solved state |
| `marksolved:<index>[:<solutionId>]` | Write a completion entry into the save, the way the game does. Was documented as `solve:` and could never run under that name - `solve:` matches first |
| `resetlevels` | Reset level completion data to a fresh save |
| `reorder:<i1,i2,...>` or `reorder:off` | Replace the level-select track with an arbitrary level list |
| `shot:<abs path>` | Screenshot |
| `inert:list` / `inert:<controller>` / `inert:off` | Dim and disable one controller's objects on the active level |
| `lockcard:<index>` / `lockcard:off` | Refuse launches of a level |
| `clickcard:<index>` | Invoke `LevelIcon.DoStartLevel` the way a real click does |
| `tint` / `tint:refresh` | Recolour every level-select card border, optionally forcing a repaint |
| `iconinfo:<index>` | Dump one level-select icon's child tree, with components, sizes and sibling order, and `isOn` for a Toggle - a card's solution stars are Toggles. The index is the card's track position, not a level index |
| `marker:states` / `marker:refresh` / `marker:off` | Cycle the four tracker-badge states across the cards: green, green/red split corner to corner, red, star. See S5 in the verification log |
| `unlockto:<n>` | Give the first n levels a completion entry, so the level select renders them unlocked |

Gameplay events land in `BepInEx/alttl-watch.log`, and only when the
`WatchEvents` config setting is on - `ObjectPlaced` alone fires hundreds of
times per level load, so it is off by default.

Always on: `PartSolved  id=<level>  part=<controller>` in `LogOutput.log`, once
per part per level load, whenever the game marks a part solved. A hand test
reads it to see which part checks can fire, with no seed needed.

Curated copies of the probe output are in [data/](data/).

## Commands added since

These came out of 0.3.2's debugging and are documented here rather than in the
table above, which is organised by what the research needed.

| Command | Effect |
|---|---|
| `watch:<seconds>` | Report the active level's load flags and the camera colour every frame, printing only changes. Written to see what a trap sees during a level load |
| `livelevels` | Count the `Level` objects alive in the scene. Exactly one is correct; two means a reset landed inside a navigation |
| `time` | Report `Time.timeScale`, the scaled and unscaled clocks, and the game's own pause (`Paused`). `boot` also logs the scale it found and resets it to 1 |
| `timescale:<n>` | Set `Time.timeScale`, e.g. `timescale:0` to pause the clock. Written to test whether a pause holds the game's events; `boot` resets it to 1 |
| (no command) | Always logged: `game: Pause(...)` and `game: OnApplicationFocus(...)` for every call to the game's own pause and focus handlers, and `time: timeScale changed X -> Y` whenever the clock moves. Going to the title pauses the game on its own; `boot` undoes a pause it finds |
| `starcalls` | How many times the game's own card-star methods (`LevelIcon.SetCompletionStars`, `InitIconSolutionStars`) ran since the last `starcalls`; then zero the counts |
| `dedupe` | Keep the level `ActiveLevelInterface` owns and destroy every other `Level` clone in the scene, inactive ones included. Run after a boot that `livelevels` counts as more than one |
| `clickat[:X,Y]` | Dispatch a real pointer click wherever the player would click, defaulting to the middle of the window |
| `creditscard` | The credits card's unlock state and the names of its own locked/unlocked sprites |
| `mute` / `unmute` | Hold `AudioListener.volume` at zero, or let it go. Also a config setting, `MuteAudio`, which the harnesses set. They also set `[Window] KeepRunningWhenUnfocused`, which skips the game's own pause when its window loses focus (a paused game holds every solve) |
| `endings` | What screen every level finishes on, for all of them at once |
| `shot:<path>[|<n>]` | Screenshot, optionally rendered at n times the window size |
| `sprites <filter>` | Every loaded sprite name matching a substring |
| `loadlevel:<index>` | `StartLevel` with a forced reload, for putting a level load in flight deliberately |

## Everything else

The rest of the surface, listed because it was not listed anywhere - several of
these were documented only inside one other doc, and the table above is meant to
be the index.

### Inspecting the running game

| Command | Effect |
|---|---|
| `controllers` | The active level's object controllers, with type and solved flag. This is what the release harness reads to decide what is left to solve |
| `reachable` | Per controller, how many of its objects a POINTER could actually hit right now: active in the hierarchy AND carrying an enabled collider. Run it at boot, solve whatever opens the container, run it again - anything that becomes touchable in between was gated by that thing, which is the `dependsOn` edge the table is missing. Written because "can the player reach this" was being asked of a human for something the engine already knows |
| `locks` | Per controller, how many of its objects the ability locks have dimmed and frozen. Walks the controller's FULL object set, not just `ManagedObjects` - Dirtyables, Containables, Stickables and StackablesY keep their own lists, and reading only the one missed them |
| `sharing:<tag>` | Object-to-controller membership for the active level. Written to find gates that are bypassable because their objects are shared with a group that is not locked; see [gate-sharing.md](gate-sharing.md) |
| `freeze:<controller>` | Dim, disable and physically freeze one controller's objects the way an ability lock does, on demand. Stops the rigidbody before removing the collider, or unsupported objects fall |
| `cats` | Every cat-event object in the loaded scene. Scanning a real scene names the class that performs a cat event, which no static probe ever found |
| `hints` | Whether the running level actually has a hint |
| `contextual` | Ask the running gameplay state where it would return to |
| `resolutions` | The game's current resolution list. Resolution INDEXES are not stable and must never be used as information - the list changes with the display |
| `members:<Type>[:<filter>]` | List a game type's members by reflection, e.g. `members:HintManager`. Built after guessing member names one compile at a time; the interop assemblies rename things unpredictably |
| `xrefs:<Type>.<Method>` | What a game method calls, from Il2CppInterop's cross-reference scan of its native code, e.g. `xrefs:ReplayMenu.LevelSelect`. The interop assemblies have no method bodies, so this is how to learn which routine a button runs instead of patching a guess. **It can freeze the game**: resolving ReplayMenu.LevelSelect's eighth call never returned; each call logs `resolving 0x...` first, so the last such line names the one that hung |
| `xrefs:<Type>.<Method>\|<Type>.<Candidate>,...` | The same scan with nothing resolved: each call's target is compared with the named candidates' native entry points and marked `= Type.Method` on a match. On 2026-09-25 it showed ReplayMenu.LevelSelect calling `LevelInterface.get_IsDLCLevel`, then the scan itself killed the game at the eighth reference - so what comes after that is still unread |
| `inert:<index>` | Report a level's controllers and which of them are inert |
| `bounds:<index>` | Every managed object's world bounds, grouped by controller. Written to find ability-locked objects sitting physically on top of free ones |
| `layout:<tag>` | Write the whole level's layout to a file, for diffing. Records the parent and the placed flag beside the position, because position alone made two rounds of cat-trap testing lie |
| `findtext:<text>` | Every text label whose content matches, anywhere in the loaded scene |
| `sections` (see above) and `why:<slot>` | `why` explains what a card's badge is reading - which locations it counts and which it thinks are blocked |

### Driving the UI

| Command | Effect |
|---|---|
| `press:<name>` | Click a control the way a POINTER would, through the EventSystem. Not the same as `clickbutton`, which invokes `onClick` - the level-select tutorial's confirm is a Button with nothing on `onClick`, so `clickbutton` reported four successful clicks that did nothing |
| `clickbutton:<name>` | Invoke the `onClick` of the first Button with that GameObject name. No synthetic input, so another plugin's UI can be driven from a script |
| `clicktrack:<n>` | Click the nth card on the level-select track |
| `focus:<position>` | Hover the level-select card at that track position (`OnFocus` + `IconFocus`), and log its level and the menu title |
| `menu:<name>` | Go to a named menu, e.g. `menu:title`. **`play` presses Play on the TITLE menu, so it needs `menu:title` first** |
| `jiggle[:<n>]` | Pick pieces up and drop them for real, one after another. Exists because a bug needed quarter-second timing to reproduce, which is not something to ask a human for |
| `play` | Press Play on the TITLE menu. Needs `menu:title` first - there is no live TitleMenu anywhere else |
| `pause` | Open the pause menu through `PostOpenMenuEvent`. Calling `ShowHideMenuItems` directly throws, because the game dereferences a GameEventData a caller cannot construct |
| `showpause` / `pausebuttons` | Report the pause MainMenu and list its buttons. Both use `FindObjectsOfTypeAll`, because the pause menu is inactive while closed and an ordinary find cannot see it |
| `leave` | Press the pause menu's own Level Select button - the route a player takes out of a puzzle. Asking the game where it WOULD go proved nothing |
| `next` / `replayselect` | The post-level ReplayMenu's Continue arrow, and its Level Select button |
| `buttons` | Every clickable control in the loaded scene, by GameObject name and on-screen label. The two differ: the tutorial modal's confirm reads "Okay" and is not named Okay |
| `titlebuttons` / `titletree` | The title menu's buttons, and its whole object tree |
| `menus` | Every menu the game knows about, and its state |
| `skip` | Press the game's own `SkipLevel` - the path the randomizer's skip gate hooks. Logs the loaded level's `skippable`, `allowPause` and `randomizable` first, then `skip: calling MainMenu.SkipLevel` and `skip: SkipLevel returned`: what the game logs between those two it did inside the call |
| `skiptip` | Force the level-select skip prompt on screen and report what it reads |
| `hinttaken` | Raise `LevelInterface.HintTaken` directly. The postfix has never been seen to fire from a synthetic drag, which is what this exists to work around |
| `erase` | Drag the eraser for real. Reading `CanBeWiped` from a probe answers a different question - it exercises the getter with no wipe in progress |

### Screen and sprites

| Command | Effect |
|---|---|
| `setres[:<w>x<h>]` | Set the window resolution. Resolution INDEXES are not stable - do not use them |
| `spriteexport:<filter>` | Write matching loaded sprites out as PNGs. How the twelve ability icons were found; see [data/ability-icons.md](data/ability-icons.md) |
| `spritegrid:<filter>` / `spritegrid:off` | Draw matching sprites on screen in a labelled grid, so a name can be matched to a picture |
| `bgset:<index>` | Force the level background, for checking the background trap's catalogue |
| `bgcatalogue` | The game's own palette of level background colours |
| `menubg` | What actually draws the pause screen's background. Do not assume it is one Image |
| `newsprites` | Sprite names that have appeared since the last time this was run |

### Tracing and one-off probes

| Command | Effect |
|---|---|
| `launchtrace` | Toggle a trace of the launch sequence |
| `regstart` / `regstop` | Start and stop recording object-controller registrations, to see what a level registers and when. Registration order is why the ability locks needed a register-time hook rather than a single pass |
| `resettest` | Does `ObjectController.Reset` actually move objects back? Written to settle exactly that |

## A warning

`iconinfo` once hung the game with no output and no exception, and it took a
process kill to recover. It now logs before it touches anything and rejects an
implausible index before searching the scene, so a repeat is at least
diagnosable. Treat the commands here as debugging aids on a session you are
willing to lose, not as something to run against a real playthrough.
