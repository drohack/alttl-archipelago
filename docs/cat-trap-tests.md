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

Test 9 closes the gap left open under "A. Reset correctness under real placement"
below for the surface-sticking mechanism specifically. It was confirmed by hand
rather than by the harness, and it is the strongest single result here: a
grabbed object is live state held by the input layer, not just a transform, and
it is precisely what a hand-rolled restore would have left dangling.

## Still to prove

### A. Reset correctness under real placement

Everything above disturbed the puzzle by shoving transforms, which does NOT
create attachments. The attachment case is argued structurally (the level is
rebuilt) rather than demonstrated. To demonstrate it, the harness needs a
disturbance that goes through the game's own drop path.

`LevelObject` has no drag API (`OnPointerDown`, `OnDrag`, `Grab`, `Drop` all
absent), so the entry point is in the input or selection layer and still has to
be found. Until then this is one manual test: post a stamp onto an envelope,
fire a trap, then drag the envelope and confirm the stamp stays behind.

### B. One test per puzzle mechanism

For each of: surfaces, drawers, grid cells, nesting, stacks, buoyancy,
containables, jigsaw, ordered, telescoping, scrubbing/cleaning.

Disturb, trap, then check all four:

1. the layout matches the opening layout exactly;
2. nothing is still attached to something it was placed into (drag test);
3. the puzzle can still be solved to completion;
4. no check is sent twice and no earned check is revoked.

### C. Run integrity

- A trap must not send, revoke or duplicate checks already earned.
- A trap on an ability-locked level: locked groups must still be dimmed and
  non-interactive one second after the reset. **This is the one real risk the
  rebuild introduces** - fresh `LevelObject`s mean our dimming has to re-arm.
- The tracker badge must be correct after a reset.
- A trap on the credits card.
- A trap arriving during a level transition, and with the pause menu open.

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
