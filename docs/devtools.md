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
| `levelsweep` | Boot every level in turn and record its RUNTIME controllers to `apworld/alttl/data/levels.json`. This is the source of truth for the level table |
| `gensweep:<index>[:<n>]` | Regenerate one procedural puzzle n times and record how its layout varies |
| `cardlabels` / `cardlabels:off` | Put the level name under each level-select card |
| `solve:<index>[:<solutionId>]` | Write a completion entry into the save |
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

Curated copies of the probe output are in [docs/data/](docs/data/).

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

## A warning

`iconinfo` once hung the game with no output and no exception, and it took a
process kill to recover. It now logs before it touches anything and rejects an
implausible index before searching the scene, so a repeat is at least
diagnosable. Treat the commands here as debugging aids on a session you are
willing to lose, not as something to run against a real playthrough.
