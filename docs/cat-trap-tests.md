# The cat trap: what it does, and the battery that proves it

The cat trap is the run's only filler item, so it fires more often than
anything else in the game. It has also been wrong three times, and each time it
passed a test first. This document records what it does now, what has actually
been proven, and the battery that has to stay green.

## What it does

One thing: play our cat-paw animation, then call the game's own
`LevelManager.ResetLevel()` - the same call behind the pause menu's Reset
button.

### Why not put the pieces back ourselves

Three versions tried to, and all three broke puzzles:

1. **Random displacement.** Took pieces off the line, shelf or grid they belong
   to. In a puzzle whose only move is reordering along an axis, there was then
   no way to put them back.
2. **Swapping positions between pieces.** Looks safe, is not: Popcorn arranges
   its pieces on three separate lines, so a swap across lines leaves an
   arrangement the puzzle can never accept.
3. **Restoring each object's opening transform and parent.** The one that looks
   obviously correct, and the most misleading of the three.

The third failed because a piece is not merely somewhere - it is IN something.
Probing `LevelObject` found no attachment API at all:

| Looked for | On `LevelObject`? |
|---|---|
| `RemoveFromSurface`, `SetStickingToSurface`, `ObjectIsOnSurface` | no |
| `attachedToObject`, `IsAttachedToObject` | no |
| `m_originalParent` | no |
| `Reset`, `OriginalPosition` | no |
| `placed` (bool) | yes - and that is all |

The links live in the mechanisms instead, each with its own unwinding rules:
surfaces (`RemoveFromSurface`), drawers (`RemoveObjectFromDrawer`), grid cells
(`RemoveObjectFromCell`), nesting (`NestInside`, `TupperwareNesting`), stacks
(`RemoveObjectFromStack`), buoyancy zones, containables. Restoring transforms
put pieces back at the right coordinates while leaving them attached, so
dragging the envelope a stamp had been posted into dragged the stamp with it.

### Why the reset is safe on every puzzle type

Measured, not assumed: `ResetLevel()` **rebuilds the level**. The `Level`
instance id changes across the call.

    Telescope   start instance=-36788    after instance=-70234
    Bats        start instance=-111150   after instance=-111642
    Stamps      start instance=-128160   after instance=-129018

Nothing survives a rebuild, so every attachment mechanism above is handled for
free, on every puzzle type, including ones nobody has tested and ones added by
a future update. That is the whole argument for this design: it is not a list
of cases we got right, it is a construction in which the cases cannot arise.

The rebuild uses the SAME seed, so a generator puzzle stays the same puzzle
rather than being re-rolled into a different one.

## Proven in play (2026-09-04)

| # | Test | Result |
|---|---|---|
| 1 | Trap in a puzzle resets it | PASS - layout after is byte-identical to the opening layout, on Telescope, Bats and Stamps |
| 2 | Reset restores parents and placed flags, not just positions | PASS - the layout dump includes both columns and both matched |
| 3 | Generator seed survives the reset | PASS - Telescope 141671695, Stamps 1216270523, unchanged across the trap |
| 4 | The paw animation plays | PASS - captured mid-swipe over the Stamps puzzle |
| 5 | Trap outside a puzzle is spent, not queued | PASS - 2 traps at menus logged as missed, no crash |
| 6 | Traps do not re-fire on reconnect | PASS - 4 historical traps replayed on two separate logins, neither fired |
| 7 | Trap accounting balances | PASS - 8 received = 2 reset + 2 missed + 4 absorbed; `trapsSprung: 8` |
| 8 | Ability dimming re-arms after the rebuild | PARTIAL - the pass runs every second from live state and was seen re-reporting object counts after a reset, but not yet on a level with a LOCKED group |
| 9 | Trap fired while the player is HOLDING a piece | PASS - confirmed in play on Stamps: the held piece reset with everything else, nothing was left attached or stuck to the cursor |
| 10 | Trap on pieces POSTED INTO something | PASS - confirmed in play on the envelopes: stamps come free of the envelope they were posted into, so the parents really are reset |
| 11 | Trap with the pause menu open | PASS - layout byte-identical, seed kept, menu still up with all seven entries |

Test 9 closes the gap left open under "A. Reset correctness under real placement"
below for the surface-sticking mechanism specifically. It was confirmed by hand
rather than by the harness, and it is the strongest single result here: a
grabbed object is live state held by the input layer, not just a transform, and
it is precisely what a hand-rolled restore would have left dangling.

## What was still to prove, and where it landed

### A. Reset correctness under real placement - CONFIRMED IN PLAY

This was the last thing standing on an argument rather than an observation, and
it is now settled: droha played it. Stamps posted onto envelopes, a cat trap,
and the parents came back correct - the pieces are loose again rather than
riding the envelope they were posted into. An earlier session confirmed the
other half, a trap fired while HOLDING a piece.

That matters more than the rest of this document, because it is the exact
failure the previous design produced: pieces restored to the right coordinates
while still attached, so dragging the envelope dragged them along. The rebuild
argument said that could not happen any more. Now it has been looked at.

Worth being clear about why it took a person. Everything the harness can do
disturbs a puzzle by shoving transforms, which does NOT create attachments, so
no automated test here could have exercised it. `LevelObject` has no drag API -
`OnPointerDown`, `OnDrag`, `Grab` and `Drop` are all absent - so the entry point
is somewhere in the input or selection layer and was never found. `press:` now
dispatches real pointer clicks, which is most of what a drag harness would need
if one is ever wanted, but the question it would answer has been answered.

### B. One test per puzzle mechanism - NOT DONE, AND PROBABLY NOT NEEDED

The original plan was a test each for surfaces, drawers, grid cells, nesting,
stacks, buoyancy, containables, jigsaw, ordered, telescoping and cleaning.

That list was written when the trap put pieces back itself, and every mechanism
was a separate way to get it wrong. It is not that any more. `ResetLevel`
rebuilds the level, so no mechanism can survive it - which is the point of
choosing a construction over a list of cases. Surface-sticking has been
confirmed by hand anyway (A above), and it is the mechanism the old design
actually broke.

Kept here rather than deleted because if the trap ever stops rebuilding and goes
back to restoring pieces, this list becomes required reading again.

### C. Run integrity - DONE

All of these have since been run and are recorded in
`docs/verification-log.md`:

- **Ability-locked level.** This was called out as the one real risk the rebuild
  introduces, because fresh `LevelObject`s mean the dimming has to re-arm.
  Tested on MedicineCabinet with 5 locked groups and 8 open: dimming re-armed
  identically, confirmed by comparing screenshots either side of the trap.
- **The tracker badge.** Read every open slot's badge, fired a trap, read them
  again: byte-identical. A reset undoes the arrangement and nothing about what
  has been collected.
- **The credits card.** Appears when the Credits item arrives, refuses a click
  while under the goal. One gap found and recorded: the refusal is silent,
  because the game's own card lock fires before our "N puzzles to go" message.
- **With the pause menu open.** Needed a way to open the menu from a script,
  which took three attempts; now the DevTools `pause` command. Layout
  byte-identical, seed kept, menu still up with all seven entries afterwards.
- **Checks are not double-sent.** Re-entering and re-solving a rebuilt level
  sends nothing new.

Only a trap arriving mid-transition is untested, and it is the least
interesting: the trap already handles arriving with no level open by counting
itself spent.

### D. Built-in cats

Wanted behaviour: on a level that HAS a real cat event, fire a random valid one
of that level's own; otherwise fall back to the reset.

#### The sweep (done)

`levelsweep` now records each level's own cats into `alttl-levels.json`, the
table both the apworld and the mod already consume. It is keyed by level id and
scanned from the level's own transform, so it is a fact about the game and the
same for every seed. The sweep otherwise reproduced the previous table
byte-for-byte, and the 91 apworld tests pass against the new one.

**13 of 111 levels carry a cat**, and every one of them carries exactly one -
except PawPrints, whose whole level is cat-themed (70 components). So "pick a
random valid cat for this level" turns out to be almost moot: on a level with a
cat there is one cat to pick.

| Level | Index | Class | Trigger found |
|---|---|---|---|
| Place Setting | 32 | `CatGrab` | `DoGrab()`, no args |
| Shells (generator) | 62 | `CatGrab` | `DoGrab()`, no args |
| Stamps | 9 | `CatGrab` | `DoGrab()`, no args |
| MerryMess_Crackers (archive) | 1016 | `CatGrab` | `DoGrab()`, no args |
| TupperwareTower | 83 | `CatClimb` | `DoClimb()`, `StartCatClimb()` |
| Clover | 68 | `CatSwipe` | none |
| Pasta | 30 | `CatSwipe` | none |
| Wrong Aspect Papers | 17 | `CatSwipe` | none |
| Sharp Pencils | 19 | `CatSwipe_RecordPlayer` | none |
| Trim Plant (Vines) | 63 | `CatSwipe_DropObjects` | none |
| Frame Maze | 72 | `CatchNet` | none |
| Radial Dance Party | 81 | `RadialCatIntro` | an intro, not a trap |
| PawPrints | 57 | 15 paw-print classes | the level itself |

#### What that means for the feature

There is **no uniform "perform this level's cat event" API**. `CatGrab` and
`CatClimb` expose a clean trigger; `CatSwipe` and its two subclasses are config
helpers only (`SetupSwipe`, `AddSwipeables`, `DoSwipeAudio` - nothing that
performs a swipe), and `CatchNet` exposes nothing either. `hasSwatCatInLevel`,
the flag that would answer the question directly, is not on `LevelInterface`,
`Level` or `LevelManager`.

So built-in cats would be five bespoke integrations, each with unknown effects
on puzzle state, covering at most 12 of 111 levels - and only 5 of those have a
trigger at all. **Recommendation: keep the reset as the universal behaviour.**
If we want the flourish later, the 4 `CatGrab` levels are the cheap win: one
class, one no-arg call, and the trap can still reset afterwards so the outcome
is identical either way.

## Later: use a level's own cat (NOT BUILT - upgrade feature)

Decided 2026-09-04: the reset is the behaviour for every level for now. This
section is the design sketch if we want the flourish later, and the reason it
was not worth doing yet.

**What it would do.** On a level that ships with a real cat, play THAT cat -
Stamps' paw reaching in and grabbing a stamp, Tupperware's cat climbing the
tower - instead of our overlay paw, then reset as usual. The reset still does
the actual work, so the outcome is identical and the feature is purely
cosmetic. That is what makes it safe to add later and safe to skip now.

**What it would cost.** Five bespoke integrations, because there is no shared
trigger. Two of the five classes have no trigger at all, so those levels would
need the cat driven some other way or left on the overlay paw. At best it
changes 12 of 111 levels; realistically 5, the ones with a callable trigger.

**Where to start.** The four `CatGrab` levels - Place Setting, Shells, Stamps
and MerryMess_Crackers. One class, one no-arg `DoGrab()`, and the data to find
them is already in `alttl-levels.json` under each level's `cats` array. Sketch:

    // in Traps.Spring, before the reset
    var grab = FindInLevel<CatGrab>();     // level's own transform, not the scene
    if (grab != null) grab.DoGrab();       // the game's cat
    else Toasts.SweepPaw();                // ours

**What has to be checked if we build it.** Whether `DoGrab()` is safe to call
out of sequence - these cats are scripted parts of a puzzle's own choreography,
not general-purpose effects, so one may assume a state the puzzle is not in.
And whether the animation survives the reset that follows it: our paw does
because it lives on our overlay canvas, but the game's cat is IN the level and
the reset rebuilds the level, so it would likely need to finish first.

## The harness

DevTools commands, driven through `BepInEx/alttl-devtools-commands.txt`:

- `layout:<tag>` writes `BepInEx/alttl-layout-<tag>.tsv` with index, name,
  **parent**, world position, local position, rotation, **placed** and active
  for every object. Snapshot, disturb, trap, snapshot, diff.
- `cats` lists every cat-ish component in the scene, with the level id.
- `resettest` shoves every object, as a stand-in for a player having moved
  pieces.
- `shot:<name>` screenshots; the file lands in `A Little To The Left_Data`.

### Two harness rules, both learned the hard way

**Position alone is not evidence.** A piece at the right coordinates can still
be attached to the wrong thing. That is why `layout:` records the parent and
the placed flag, and why the diff compares whole rows.

**Test the path, not the piece.** Every wrong result in this feature's history
came from a harness that exercised something the player never touches:
`DoStartLevel` instead of `OnPointerClick`, a closed pause menu instead of an
open one, `boot:` always passing `forceReload`, and cat traps fired at puzzles
that had no progress to undo. `solve:0` joined the list today - it sets solved
flags without moving anything, so it is useless as a disturbance and it
completes the level out from under the test.
