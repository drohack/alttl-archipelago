# ALTTLDevTools: the research and survey plugin

`src/ALTTLDevTools/` is a BepInEx plugin that measures what the game does and
drives it from a script. It is never shipped with the randomizer and is
installed separately, by hand. Almost every fact this project relies on was
measured with it rather than assumed - the level table, the controller survey,
what a level select does when a card is locked.

Build it with the mod: `bash tools/deploy.sh` closes the game, builds both
plugins and deploys them; `bash tools/deploy.sh --no-kill` only compiles, as
does `dotnet build src/ALTTLDevTools -p:SkipDeploy=true`. A plain
`dotnet build src/ALTTLDevTools` deploys into the game whenever it is closed.
Curated copies of probe output are in [docs/reference/](../reference/).

## It changes the game

It is not read-only, and a session that matters should know how:

- **Runs in the background.** It sets `Application.runInBackground`, so the
  game keeps ticking while another window has focus.
- **Ignores input while unfocused**, by default (`IgnoreInputWhenUnfocused`):
  Rewired stops reading the keyboard and mouse, and `ZoomFocusGuard` skips the
  camera zoom, while the window is in the background.
- **Can skip the game's focus pause** (`KeepRunningWhenUnfocused`, off by
  default) and **hold the audio at zero** (`MuteAudio` or `mute`).
- **Some commands write the save**: `setres` (the resolution choice) and the
  three under [Save file](#save-file). Never run those against a save that
  matters.
- A few commands change the level on screen for a test (`solve:`, `freeze:`,
  `drawers:open:`, `scrub:`, `bgset:`, `timescale:`); they last until the
  level is reloaded, or say how to undo them.

## Sending a command

Write ONE command into `<game>/BepInEx/alttl-devtools-commands.txt`. It is
taken within half a second, the file is emptied, `command: <text>` is logged,
and the answer lands in `BepInEx/LogOutput.log`. `tools/release_e2e.py`'s
`dev()` does exactly this. A command still in the file when the game froze
runs at the next launch, so empty it after a freeze. While a sweep runs, only
`stop` is taken; any other command waits in the file until the sweep ends.

- The keyword is the text before the first `:` (a space works too, and is the
  usual form for `sprites`); the argument is everything after it. Keywords
  ignore case. `help` lists every command from the same table that runs them,
  and an unknown command logs the nearest keywords.
- A switch turns off with `<name>:off` (`trace:off`, `freeze:off`, `watch:off`,
  `flip:off`, `mute:off`, `spritegrid:off`, `traps:off`,
  `registrations:off`).
- **A level INDEX** is the game's `LevelInterface.LevelIndex`: 1 to 79 for the
  campaign, 81 to 84 for its specials and the credits, 995 to 1000 for the
  randomized-only generators, 1001 and up for the event packs, 1100 and up for
  the DLCs. **A TRACK POSITION** is a card's place on the level select counted
  from 0, with pack dividers and the credits card counted. `boot:`,
  `loadlevel:`, `clickcard:` and `marksolved:` take an index; `clicktrack:`,
  `focus:`, `scrolltrack:`, `iconinfo:` and `why:` take a position.
- `tools/check-devtools.py` holds this page to the command table in
  `Plugin.cs`: every spelling in a `Command` column below is one the table
  accepts, under the heading of its area, and the other way round.

## Settings, patches and what is always logged

`BepInEx/config/droha.alttl.devtools.cfg`:

| Setting | Default | Effect |
|---|---|---|
| `[Window] TargetVirtualDesktop` | 0 | Move the game and the BepInEx console to this Windows virtual desktop at startup, 1-based; 0 leaves them alone |
| `[Window] IgnoreInputWhenUnfocused` | true | Stop the game reading keyboard and mouse while unfocused (it keeps running) |
| `[Window] KeepRunningWhenUnfocused` | false | Skip the game's own pause when its window loses focus; a paused game holds every solve. The harnesses set it and put it back |
| `[Window] RaiseWindowAtStartup` | false | Bring the game to the foreground at startup. Off, because it steals focus from whatever is being worked on |
| `[Debug] MuteAudio` | false | Hold `AudioListener.volume` at zero. The automated harnesses set it for their run and put it back; the hand-test tools write it false, so a game droha plays has sound |
| `[Debug] WatchEvents` | false | Write every ObjectControllerSolved, SolutionChanged, LevelComplete and ObjectPlaced event to `alttl-watch.log`. ObjectPlaced fires hundreds of times per load |

Patches applied at every launch:

| Patch | What it does |
|---|---|
| `PauseTrace` | Logs `game: Pause(...)` and `game: OnApplicationFocus(...)` after each call to the game's own pause and focus handlers, and skips the focus-loss pause when `KeepRunningWhenUnfocused` is on |
| `ZoomFocusGuard` | Skips `ZoomManager.ProcessZoom` while the window is unfocused, when `IgnoreInputWhenUnfocused` is on |
| `StarTrace` | Counts calls to `LevelIcon.SetCompletionStars` and `InitIconSolutionStars`, for `starcalls` |
| `RegistrationLog` | Records object-controller registrations, only between `registrations:on` and `registrations:off` |

Logged with no command:

- `PartSolved  id=<level>  part=<controller>` once per part per level load -
  a hand test reads these to see which part checks can fire.
- `<time>  <event>  id=...  index=...  solutionCount=...  found=...  seed=...`
  (and `solutionId=` when the game gives one) for LevelSelected,
  LevelComplete, LevelCompleteEarly, LevelSkipped, LevelRandomized,
  LevelHintTaken, LevelExited, CampaignFinished and DLCFinished. Grep
  `'LevelComplete +id='`: there are two spaces.
- `time: timeScale changed X -> Y` whenever the clock moves, and the
  `PauseTrace` lines above. Going to the title pauses the game on its own;
  `boot:` undoes a pause it finds.

Files it writes, all in `<game>/BepInEx/`:

| File | Written by | Read by |
|---|---|---|
| `alttl-dump.json` | `dump` | `tools/check-game-facts.py` (refuses one taken with the mod loaded), `tools/levelsweep.py --dump` |
| `alttl-levels.json` | `levelsweep` | `tools/levelsweep.py`, then `tools/merge-levels.py` into `apworld/alttl/data/levels.json` |
| `alttl-solutions.tsv` | `solutions` | `tools/levelsweep.py`; curated as `fixtures/controller-survey.tsv` |
| `alttl-generators.tsv` | `gensweep:` | by hand; curated as `docs/reference/generator-sweep.tsv` |
| `alttl-unlocks.tsv`, `alttl-chapters.txt` | `unlocks` | `tools/probe-star.py` |
| `alttl-registrations.tsv` | `registrations:off` | `tools/levelsweep.py` |
| `alttl-bounds-<tag>.tsv` | `bounds:` | `tools/probe-occlusion.py`, `tools/blocking.py` |
| `alttl-sharing.tsv` | `sharing:` | `tools/probe-object-sharing.py` |
| `alttl-layout-<tag>.tsv` | `layout:` | by hand, to diff |
| `<file>` (any path) | `objects:` | `tools/probe-lock-roundtrip.py` |
| `alttl-watch.log` | `WatchEvents` | by hand |
| `alttl-why.txt` | `why:` | the mod, only with `[Diagnostics] BadgeWhyProbe = true` |

## Help

| Command | Effect |
|---|---|
| `help` / `help:<command>` | Every command grouped by area, or one command's spellings and area. Read from the same table the dispatcher uses, so it cannot list a command that does not run |

## Launch and levels

| Command | Effect |
|---|---|
| `boot:<index>[:<seed>]` | Launch any level by index with an optional forced procedural seed. Tears down every live level first, including a `Level` left in the scene after an exit to the title, and refuses to start if any survive. For three seconds after the start it also destroys any live level that is not the active one: booting a level again after leaving it for the level select brought the old copy back on top. Lifts a Seeing Stars star gate in memory (a locked level otherwise loads a chapter header), undoes the game's own pause (`GameManager.Pause`, which holds every gameplay event) and resets a stopped `Time.timeScale` |
| `loadlevel:<index>` | `StartLevel` with a forced reload and a fixed seed, 12345, for putting a level load in flight deliberately. **Run it with no run active**: with a run, the mod's `Track` rewrites the index to the slot the run intends |
| `complete` | The game's own `CompleteLevel` on the active level |
| `skip` | Press the game's own `SkipLevel` - the path the randomizer's skip gate hooks. Logs the loaded level's `skippable`, `allowPause` and `randomizable` first, then `skip: calling MainMenu.SkipLevel` and `skip: SkipLevel returned`: what the game logs between those two it did inside the call |
| `solve:<index or name>` | Force one controller of the RUNNING level to solved, by its place in `controllers` or by name, and raise the game's own `ObjectControllerSolved`. Proves the event-to-check path, not that the puzzle can be solved |
| `livelevels` | Count the `Level` objects alive in the scene. Exactly one is correct; two means a reset landed inside a navigation |
| `dedupe` | Keep the level `ActiveLevelInterface` owns and destroy every other `Level` clone in the scene, inactive ones included. `boot:` does this by itself now |
| `state` / `state:<file>` | `state`: one line - the game state, the active level and its index, seed, solution counts, the load flags, and the active menu (`menu=`). `state:<file>` is the old spelling of `objects:<file>`, kept for the lock probe |
| `contextual` | Ask the running gameplay state where finishing would return to |
| `watch[:<seconds>]` / `watch:off` | For that many seconds (default 10), every frame, report the game state and the active level's load flags, and the camera's background colour beside the level's `BackgroundColor`, `ActiveBackgroundColor` (the one the game paints from) and its `Level`'s - printing only changes. `watch:off` stops it early |
| `time` | Report `Time.timeScale`, the scaled and unscaled clocks, and the game's own pause (`Paused`) |
| `timescale:<n>` | Set `Time.timeScale`, e.g. `timescale:0` to stop the clock. Written to test whether a pause holds the game's events; `boot:` resets it to 1 |

## Menus and input

| Command | Effect |
|---|---|
| `menu:<name>` | Go to a menu: `title`, `levels`, `archive` or `daily`. Does nothing if the game is already there (forcing a state it is in leaves no menu open) |
| `play` | Press Play on the TITLE menu. There is no live TitleMenu anywhere else, so it needs `menu:title` first |
| `pause` | Open the in-level pause menu by raising the game's own `MenuOpen` event. `PostOpenMenuEvent` is accepted and opens nothing, and calling `ShowHideMenuItems` directly throws, because the game dereferences a GameEventData a caller cannot construct |
| `pausebuttons` | The pause menu's buttons and which are shown. Found with `FindObjectsOfTypeAll`, because the pause menu is inactive while closed |
| `leave` | Press the pause menu's own Level Select button - the route a player takes out of a puzzle |
| `next` | The post-level Continue arrow: a pointer click on the retry panel's Continue Button when that panel is on screen, else the ReplayMenu's arrow |
| `replayselect` | The post-level ReplayMenu's Level Select button |
| `menus` | Every menu the game knows about, and its state |
| `buttons` | Every clickable control in the loaded scene, by GameObject name and on-screen label. The two differ: the tutorial modal's confirm reads "Okay" and is not named Okay |
| `titlebuttons` | The title menu's entries and which are shown |
| `titletree` | The title screen's object tree, three levels deep |
| `uitree:<object name>[:<depth>]` | One UI object's subtree, found by exact name (an active one first): each child's components by type name and its RectTransform - size, anchored position, pivot, anchors, scale, screen x - then its parents up to the canvas. At most six children per parent; depth 3 by default. Built to see which object holds the overview strip's Scrollbar |
| `press:<name>` | Click a control the way a POINTER would, through the EventSystem, by its name or its parent's. Not the same as `clickbutton:`, which invokes `onClick` - the level-select tutorial's confirm is a Button with nothing on `onClick`, so `clickbutton:` reported four successful clicks that did nothing |
| `clickbutton:<name>` | Invoke the `onClick` of the first active Button named so (or whose parent is), else the game's long-press control of that name. No synthetic input, so another plugin's UI can be driven from a script |
| `clickat[:<x>,<y>]` | A real pointer click wherever the player would click, in screen pixels from the BOTTOM-left, defaulting to the middle of the window; logs what the raycast hit |

## Level select

| Command | Effect |
|---|---|
| `clicktrack:<position>` | Click the card at that track position through its `OnPointerClick`, as a real click enters. Refuses while the track is not on screen rather than clicking a leftover one |
| `clickcard:<index>` | Invoke `LevelIcon.DoStartLevel` on the card for that level INDEX. Answers "open menu:levels first" while the track is not on screen, which `tools/release_e2e.py` retries on |
| `focus:<position>` | Hover the card at that track position (`OnFocus` + `IconFocus`), and log its level and the menu title |
| `scrolltrack:<position>` | Scroll the level select to that track position and log how its icon is drawn: the game's unlocked flag and which art (default or unlocked) is showing |
| `sections` | Log the level-select sections (title, start, colour) and every track icon's lock state, and whether the track read is the one on screen |
| `iconinfo:<position>` | One card's child tree, with components, sizes and sibling order, and `isOn` for a Toggle - a card's solution stars are Toggles |
| `why:<position>` | Ask the mod to explain the badge on the card at that track position - which locations it counts and which it thinks are blocked. **The mod only answers with `[Diagnostics] BadgeWhyProbe = true`** in its config; otherwise the request sits unread in `alttl-why.txt` |
| `creditscard` | The credits card's unlock state and the names of its own locked and unlocked sprites. The card on the track - the seed's finale, a DLC's credits included - or, with no track up, the base game's |
| `starcalls` | How many times the game's own card-star methods (`LevelIcon.SetCompletionStars`, `InitIconSolutionStars`) ran since the last `starcalls`; then zero the counts |
| `skiptip` | Force the level-select skip prompt on screen and report what it reads |

## Locks and objects

| Command | Effect |
|---|---|
| `controllers` | The active level's object controllers, with type and solved flag. This is what the release harness reads to decide what is left to solve |
| `indexables` | Every Indexables controller of the running level: each solution's target index per object (or its order under `matchOrder`), and each object's current index, how many it has and `LockIndex`. What to tell a player to set (Nanopets: each pet to its last state) |
| `revoke:<Ability>[,<Ability>]` / `revoke:none` | Make the Archipelago mod treat those abilities as NOT held, whatever the server sent - its real lock, applied at once - so one seed holding every ability covers any hand test. `revoke:none` gives them back. Replaces the set each time; lasts until the session reconnects |
| `traps:off` / `traps:on` | Make the Archipelago mod count no Background Change Traps for the rest of this game session (the hand-test seed's filler is all background traps, and they hide pieces), or count them again. A level already on screen keeps its colour until it is loaded again |
| `locks` | Per controller, how many of its objects the ability locks have dimmed and frozen, how many two controllers share, and how many have no sprite to dim. Walks each controller's FULL object set, not just `ManagedObjects` - Dirtyables, Containables, Stickables and StackablesY keep their own lists |
| `reachable` | Per controller, how many of its objects a POINTER could actually hit right now: active in the hierarchy AND carrying an enabled collider. Run it at boot, solve whatever opens the container, run it again - anything that becomes touchable in between was gated by that thing, which is the `dependsOn` edge the table is missing |
| `freeze:<controller>` / `freeze:off` | Disable the colliders of one controller's objects, the way an ability lock does, on demand; `freeze:off` puts every collider back |
| `colliders:<controller>` / `colliders:all` | One line per object of that controller (or every controller): dimmed or not, the state of `obj.collider` and `obj.rigidbody`, how many Collider2D sit under the object, how many are live (enabled, active, on a simulated body or none), and the live ones that are not `obj.collider`. The lock reaches only `obj.collider`, so an extra live collider on a locked object is one that can still be hit or push things |
| `drawers` / `drawers:open:<name>` / `drawers:close:<name>` | Every Drawer in the running level: its class (`Drawer`, or the one subclass `DrawerExpandable`), its state, and for each piece it holds the interactable state the drawer SAVED (`recorded=`, filled as it closes and used up as it opens) beside the piece's flags, body, collider and dim now. `open:`/`close:` run that drawer's own `OpenDrawer`/`CloseDrawer`, the call the game itself makes, so a drawer can be moved without hands; `<name>` may be an `objects:` key where names repeat |
| `objects:<file>` | One JSON line per object of the running level, written to `<file>`: the controllers holding it, its flags (`Interactable`/`PreventSelection`, the fields under them, and the game's own `Selectable`), collider, body, whether the Archipelago mod has it locked, and the colour of every sprite the lock paints and of every other sprite it shows; per drawer its state, travel and saved contents; per scrub object how far it is scrubbed. Also the handles no controller lists, a sticker's peel handle, a rag (`ClearingObject`) and a match, with `handleOf`: the keys of what each acts on. A key is each node's name and its place among same-named siblings, from the level down - not the sibling index, which a drawer reshuffles as it opens and closes. What `tools/probe-lock-roundtrip.py` compares |
| `scrub:<name>[:<fraction>]` | Drive one scrub object (a cupboard door, a wilting flower) as a drag does: its handle moved `fraction` of the way (default 0.8) and the game's `ScrubPickedUp`/`Scrubbing`/`ScrubReleased` called with it - the handlers the mod refuses while the object is locked, so a locked one logs its refusal. `<name>` may be an `objects:` key. Called in one frame, it does not move a free door or flower either |
| `flip:<name>[:<seconds>]` / `flip:off` | For that many seconds (default 20), every frame: each level object with that name whose collider, `Interactable`, selection or lock changed, with the frame number; and the game events that fire meanwhile (intro start and end, transition in, phase, state and grid changes, and more), also by frame. Listens and reads only, nothing patched: a `trace:` of `DragObjectBase.SetInteractable` crashed the game |
| `tree:<name>` | The child tree of every object in the running level whose name contains `<name>`: each node's components, and on a SpriteRenderer its sprite, colour, whether it draws and its sorting order. On an AnimScrubObject it follows `animationToScrub` (the animated picture) and `objectToReference` (the handle that is grabbed) too, which can sit elsewhere in the scene |
| `layout:<tag>` | Write the whole level's layout to `alttl-layout-<tag>.tsv`, for diffing: parent, world and local position, rotation, placed flag and active flag per object. Position alone made two rounds of cat-trap testing lie |
| `shove` / `shove:<x>,<y>` | Move every active piece of the running level by a world offset (default 1.5, -1): a stand-in for a player's work before a Cat Trap, so a `layout:` diff after the reset has something to undo. Not a drag: nothing is snapped or placed |
| `bounds:<tag>` | Every object's world bounds, grouped by controller, to `alttl-bounds-<tag>.tsv`. Written to find ability-locked objects sitting physically on top of free ones |
| `sharing:new` / `sharing:append` | Which controllers claim which objects on the active level, to `alttl-sharing.tsv` - `new` starts the file, `append` adds this level. Written to find gates that are bypassable because their objects are shared with a group that is not locked; see [gate-sharing.md](../history/gate-sharing.md) |
| `jiggle[:<n>]` | Call `DragObject.Snap()` by reflection on every piece (or the first n) in one frame - the settle a drop runs - so a cat trap sent meanwhile lands mid-animation. Not a pointer drag: `DragObject` has no drag handler for a synthetic drag to reach |
| `cats` | Every cat-event object in the loaded scene, by class. Scanning a real scene names the class that performs a cat event, which no static probe ever found |
| `catevent[:info\|grab\|trigger\|try\|climb]` | The running level's own `CatGrab` / `CatClimb`: its Interlude config (trigger, completes or deactivates the level) and state. With an argument, starts it out of sequence (`DoGrab()`, `OnTrigger()`, `TryDoInterlude()`, `StartCatClimb`) and logs each change of its state for 15 s as `catevent: +1.23s ...` |

## Hints

| Command | Effect |
|---|---|
| `hints` | Whether the running level actually has a hint, and which picture each notepad page draws (`shows=`) beside the level's own `HintImages` and its randomizer's hint sprites. Open the notepad first: the pages refresh when it opens. Open the level by its CARD (`clicktrack:`), not `boot:` - HintMenu.SetHints runs on the card click, so a booted level keeps the previous level's pages. Reading a page's `CanBeWiped` can spend a Hint Page, on purpose |
| `hinttaken` | Call `LevelInterface.HintTaken` directly, the path the mod charges a Hint Page from. It does not raise the game's `LevelHintTaken` event, so it does not trigger the hint achievements |
| `erase` | Drag the eraser across the current page through the UI drag handlers. Reading `CanBeWiped` from a probe answers a different question - it exercises the getter with no wipe in progress |

## Screen, audio and art

| Command | Effect |
|---|---|
| `shot:<abs path>[\|<n>]` | Screenshot to that file; a pipe and a number renders it at that many times the window size (at most 8) without changing the window |
| `setres:<w>x<h>` | Set the window to that size, windowed, through the game's own settings menu, and WRITE the choice to the save as the game does. Resolution INDEXES are not stable - the list changes with the display - so it takes a size |
| `resolutions` | The game's current resolution list and the saved choice. Never use the indexes as information |
| `mute` / `mute:off` / `unmute` | Hold `AudioListener.volume` at zero, or let it go. The same switch as the `MuteAudio` setting |
| `bgset:<colour>` | Force the running level's background to an HTML colour (`bgset:#8899AA`), for checking the background trap. Take a screenshot after it: the fields always accept the write, whether or not the screen changes |
| `bgcatalogue` | The game's own palette of level background colours |
| `menubg` | What actually draws the pause screen's background. Do not assume it is one Image |
| `sprites <filter>` | Every loaded sprite name containing the filter (every name without one), deduplicated |
| `spriteexport:<names>\|<folder>` | Write the named sprites (comma-separated) out as PNGs into the folder. How the ability icons were found (thirteen with Distributing); see [ability-icons.md](../reference/ability-icons.md) |
| `spritegrid:<names>` / `spritegrid:off` | Draw the named sprites (exact names, comma-separated) on screen in a labelled grid, large and at icon size, so a name can be matched to a picture |
| `newsprites` | Sprite names that have appeared since the last time this was run - load a level in between and the difference is that level's art |

## Save file

These three write the campaign save. `unlocks` only reads it.

| Command | Effect |
|---|---|
| `unlocks` | Write every level's unlock and completion state, its store and saved solution count to `alttl-unlocks.tsv`, the chapters to `alttl-chapters.txt`, and log what the game thinks comes next |
| `unlockto:<n>` | Give levels 0 to n-1 a completion entry, so the level select renders them unlocked. Writes the save |
| `marksolved:<index>[:<solutionId>]` | Write a completion entry for that level into the save, the way the game does, and log `marksolved: ... found=... solved=...`. Writes the save |
| `resetlevels` | Reset level completion data to a fresh save. Writes the save |

## Sweeps and reports

| Command | Effect |
|---|---|
| `dump` | Write the full level / daily / archive / DLC table to `alttl-dump.json`, stamped with whether the mod was loaded. Take it with the mod moved OUT of `BepInEx/plugins` (see `tools/check-game-facts.py`) |
| `solutions` | Load every level prefab in turn and record its object controllers and solution ids to `alttl-solutions.tsv` |
| `levelsweep` / `levelsweep:<i1,i2,...>` | Boot every level (or just those indices) in turn and record its RUNTIME controllers to `alttl-levels.json`. MERGE that into `apworld/alttl/data/levels.json` with `tools/merge-levels.py` rather than copying it over - a fresh sweep regresses the hand-audited phased levels. See `docs/dev/level-data.md` |
| `gensweep:<seeds>[:<index>]` | Regenerate every randomizable level (or just that one) under that many seeds (default 6, at most 100) and record how its layout varies, to `alttl-generators.tsv`: controllers, the rules chosen, and each controller's solution lists after generation (an ending's id is the controller's name and the entry it matched). `gensweep:6:28` sweeps Breadtags six times |
| `rules` | gensweep's row for the level on screen, on whatever seed it has - gensweep only regenerates its own fixed seeds. `boot:995:<seed>` then `rules` says which rules a Books (Randomized) seed chose, and so which one is symmetric |
| `stop` | End the running sweep - `solutions`, `levelsweep` or `gensweep` - and write the rows it has so far |
| `endings` | What screen every level finishes on, for all of them at once, as TSV in the log (the retry panel's authored and computed flags, silent completion) |
| `achievements` | Every achievement the game has loaded (id, name, whether it reads as already met on this Steam profile, description), then every achievement checker in the scene - they live under `Game Manager/SteamDataTracker`, not in the level - with the achievement it awards and, one line each, the level it watches and its other settings. Read only |
| `cursor` | The game's own cursor (GameCursor): state, mode, whether it is active, and whether its sprite is drawn. A scripted test has no pointer over the window, so a screenshot cannot show it |
| `registrations:on` / `registrations:off` / `regstart` / `regstop` | Start and stop recording object-controller registrations - what a level registers, and on which frame - to `alttl-registrations.tsv`. Registration order is why the ability locks needed a register-time hook rather than a single pass |

## Reflection and tracing

| Command | Effect |
|---|---|
| `members:<Type>[:<filter>]` | List a game type's properties, fields and methods by reflection, e.g. `members:HintManager`. Built after guessing member names one compile at a time; the interop assemblies rename things unpredictably |
| `xrefs:<Type>.<Method>` | What a game method calls, from Il2CppInterop's cross-reference scan of its native code, e.g. `xrefs:ReplayMenu.LevelSelect`. The interop assemblies have no method bodies, so this is how to learn which routine a button runs instead of patching a guess. **It can freeze the game**: resolving ReplayMenu.LevelSelect's eighth call never returned; each call logs `resolving 0x...` first, so the last such line names the one that hung |
| `xrefs:<Type>.<Method>\|<Type>.<Candidate>,...` | The same scan with nothing resolved: each call's target is compared with the named candidates' native entry points and marked `= Type.Method` on a match |
| `trace:<Type>.<Method>[,...]` / `trace:off` | Patch the named game methods at runtime and log every call: the frame, the object it ran on and its arguments (every overload of a name; at most 300 lines per start). For "which routine does this", where `xrefs:` can kill the game and a guess costs a rebuild |
| `findtext:<text>` | Every text label whose content matches, anywhere in the loaded scene, inactive ones included, and whether it is localised |

## A warning

`iconinfo` once hung the game with no output and no exception, and it took a
process kill to recover. It now logs before it touches anything and rejects an
implausible position before searching the scene, so a repeat is at least
diagnosable. Treat the commands here as debugging aids on a session you are
willing to lose, not as something to run against a real playthrough.
