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
| `boot:<index>[:<seed>]` | Launch any level with an optional forced procedural seed |
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
| `iconinfo:<index>` | Dump one level-select icon's child tree, with components, sizes and sibling order |
| `marker:states` / `marker:refresh` / `marker:off` | Cycle the four tracker-badge states across the cards: green, green/red split corner to corner, red, star. See S5 in the verification log |
| `unlockto:<n>` | Give the first n levels a completion entry, so the level select renders them unlocked |

Gameplay events land in `BepInEx/alttl-watch.log`, and only when the
`WatchEvents` config setting is on - `ObjectPlaced` alone fires hundreds of
times per level load, so it is off by default.

Curated copies of the probe output are in [data/](data/).

## Commands added since

These came out of 0.3.2's debugging and are documented here rather than in the
table above, which is organised by what the research needed.

| Command | Effect |
|---|---|
| `watch:<seconds>` | Report the active level's load flags and the camera colour every frame, printing only changes. Written to see what a trap sees during a level load |
| `livelevels` | Count the `Level` objects alive in the scene. Exactly one is correct; two means a reset landed inside a navigation |
| `clickat[:X,Y]` | Dispatch a real pointer click wherever the player would click, defaulting to the middle of the window |
| `creditscard` | The credits card's unlock state and the names of its own locked/unlocked sprites |
| `mute` / `unmute` | Hold `AudioListener.volume` at zero, or let it go. Also a config setting, `MuteAudio`, which the harnesses set |
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
| `members:<Type>[:<filter>]` | List a game type's members by reflection, e.g. `members:HintManager`. Built after guessing member names one compile at a time; the interop assemblies rename things unpredictably |
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
| `focus:<name>` | Scroll the level select to a named icon |
| `menu:<name>` | Go to a named menu, e.g. `menu:title`. **`play` presses Play on the TITLE menu, so it needs `menu:title` first** |
| `jiggle[:<n>]` | Pick pieces up and drop them for real, one after another. Exists because a bug needed quarter-second timing to reproduce, which is not something to ask a human for |

### Screen and sprites

| Command | Effect |
|---|---|
| `setres[:<w>x<h>]` | Set the window resolution. Resolution INDEXES are not stable - do not use them |
| `spriteexport:<filter>` | Write matching loaded sprites out as PNGs. How the twelve ability icons were found; see [data/ability-icons.md](data/ability-icons.md) |
| `spritegrid:<filter>` / `spritegrid:off` | Draw matching sprites on screen in a labelled grid, so a name can be matched to a picture |
| `bgset:<index>` | Force the level background, for checking the background trap's catalogue |

## A warning

`iconinfo` once hung the game with no output and no exception, and it took a
process kill to recover. It now logs before it touches anything and rejects an
implausible index before searching the scene, so a repeat is at least
diagnosable. Treat the commands here as debugging aids on a session you are
willing to lose, not as something to run against a real playthrough.
