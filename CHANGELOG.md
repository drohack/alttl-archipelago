# Changelog

Every release ships three files that carry the same version number and are
meant to be used together: the mod zip, `alttl.apworld`, and the player yaml.
A mod and an apworld that disagree about the version disagree about the item
table, and nothing detects that at runtime - so the version is checked by
`tools/check-version.py`, in CI, and again before a release will build.

The format is loosely [Keep a Changelog](https://keepachangelog.com/).

## Unreleased

Nothing yet.

## 0.3.0

The first release. Everything below is what "it works" currently means.

### The run

- **The seed replaces the campaign.** The level select shows the run's puzzles
  in the order the generator planned, and the campaign save is never opened
  while a session is active - the game is pointed at `save_ap_<slot>_<seed>`
  instead. Isolation by construction, so a crash or an alt-F4 cannot leak a
  randomized run into the player's own progress.
- **Progressive Puzzle Packs** reveal puzzles in widening groups. The ramp is
  decided at generation and sent in slot data rather than recomputed in the
  game, so the two cannot disagree about what is reachable.
- **Ability locks.** Objects for a mechanic you have not unlocked are dimmed
  and immovable, so a puzzle can be partly solved and returned to.
- **Checks** are the distinct solutions of each puzzle, plus a controller-group
  check where a level has separable parts.
- **The goal** is beating a configurable number of puzzles, which unlocks the
  credits card.

### Items beyond progression

- **Skips** clear a puzzle you are stuck on. A skipped puzzle does not count
  towards the goal.
- **Hint Pages** unlock a page of the in-game notepad. Without one the notepad
  still opens and the hint is visible as a scribble - you just cannot erase it.
  How many exist is a percentage of the pages the seed actually drew.
- **Cat Traps** knock your work over. They cost time, never progress, and the
  number already sprung is persisted so a reconnect does not fire them again.
- **Level and Menu Backgrounds** recolour the game, one shade per item.

### Connection

- **Offline play.** If no server answers at launch, the run resumes from a
  cache of the slot data and the received items rather than dropping to the
  vanilla game. Checks earned offline are queued and sent on the next
  connection. A seed regenerated under the same slot name gets its own save
  and replaces the cache, so a stale cache cannot write into the wrong run.
- **A mid-session drop keeps playing.** Progress is kept and pushed, never
  reverted.
- **Retries are unlimited** by default, backing off 5, 10, 20, 40 then 60
  seconds, and stop only on Cancel or Disconnect - matching Archipelago's own
  reference client. A refused login (wrong slot name or password) is never
  retried, because retrying cannot fix it.
- **CONNECT / CANCEL / DISCONNECT** in the in-game pane, three states for three
  situations. A press during an attempt supersedes it rather than being
  ignored.

### Known gaps

- A level that once loaded empty and has never reproduced. A watchdog logs
  `LEVEL LOADED EMPTY` if it happens again.
- The Play button's behaviour partway through a run - see the verification log.
- Leaving an offline run for the vanilla campaign means turning `AutoConnect`
  off in the Archipelago dialog and relaunching. There is no in-session route.
- DLC2 *Seeing Stars* is not supported; it was not owned when the content was
  surveyed.
