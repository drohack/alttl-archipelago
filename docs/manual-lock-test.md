# Ability locks: what was fixed, how it is tested, what still needs hands

The ability lock (`src/ALTTLArchipelago/AbilityLocks.cs`, with the vote in
`src/ALTTLArchipelago.Core/ObjectLock.cs`) greys and freezes the pieces of a
group whose ability the run does not hold, and gives them back when the
ability arrives. This file lists every lock fix made on 2026-09-25 to 27, how
each one is tested, and the checks that still need a person at the mouse.

## How the locks are tested now

`tools/probe-lock-roundtrip.py` does it without hands, on every level with a
group an ability locks (132 levels, ~2.5 h for all of them):

1. Load the level with every ability held: the control, what the level looks
   like when nothing is locked.
2. Load it again with abilities withheld (`all`; on levels with drawers or
   doors also only Drawer / Gadgets withheld, and all but Drawer / Gadgets).
   Check each locked piece: frozen or, for a drawer or door, solid; every
   visible sprite grey; nothing clear greyed into view; locked drawers refuse
   to move; locked doors and flowers refuse a drag.
3. Run a lock pass, move the drawers as a player would, give every ability
   back, put the drawers as they started and then open them all.
4. Compare with the control piece by piece: can it be picked up (the game's
   own `Selectable`), collider, physics, every sprite's colour, drawer state,
   what each drawer saved.

Run it on the levels a change can touch, or `--all`:

    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py 1121 1124
    PYTHONUNBUFFERED=1 py -3.13 -u tools/probe-lock-roundtrip.py --all [--resume]

It needs the hand-test server up with a seed that holds every ability
(`tools/handtest-level.py` sets one up), and starts the game itself.
Results: `testserver/logs/lock-roundtrip/results.jsonl`, with the snapshots
of anything that did not pass.

**Every fix below was seen to fail the probe when broken.** Each was undone
in turn in a scratch build and the probe run on the level it was found on:
14 of 14 caught (2026-09-27), then fixes 17 to 19, 3 of 3. Two lines no
longer trip it: the drawer physics fallback (fix 9), covered by fix 15, and
the cover thaw (fix 16), whose only case - Sticky Drawer's gum counted as a
cover - went away with fix 17 (re-run 2026-09-27: missed). Both stay as
fallbacks.

## The fixes

| # | Fix | Found on | Probe check | Also |
|---|---|---|---|---|
| 1 | A locked drawer, cupboard or door keeps its collider and physics, so nothing behind it can be grabbed through it | Tea Cabinet (droha) | "a locked cover lost its collider/body"; hand-tested answers for the three door levels | hand-tested |
| 2 | Scrubbed objects are doors only on Clock Cupboard, Tea Cabinet and Trophy Cabinet; on Wilting Flowers they lock like any piece | Wilting Flowers | hand-tested answer "should be frozen" | hand-tested |
| 3 | A locked door or flower refuses a drag | Tea Cabinet, Wilting Flowers (droha) | DevTools `scrub:` on every locked scrub object must log the refusal (4 levels) | hand-tested on both |
| 4 | Wilting Flowers' flowers are greyed through the sprite that draws them | Wilting Flowers | "locked, not grey" | screenshot |
| 5 | A drawer piece that cannot move is not locked or greyed | Sewing Box's box, Jewelry Box's Main Box (droha) | hand-tested answers "should not be locked" | hand-tested |
| 6 | A locked drawer does not move when the game moves it; the move is made once it unlocks | Jewelry Box, Kitchen Utensils (droha) | "a locked drawer moved", "a refused drawer move was not made on unlock" (where the game's own move moves it) | hand-tested |
| 7 | A sliding drawer locks with its drawer set even when another group frees it | Daggers (droha) | hand-tested answers "should be locked" | hand-tested |
| 8 | A locked drawer's art is greyed at any depth | Bathroom Drawer (droha) | "locked, not grey" | now part of fix 13 |
| 9 | Drawer-set pieces get their physics back on unlock | Sewing Box (droha) | covered by fix 15 | fallback only |
| 10 | A piece a drawer saved while locked comes out free when the drawer opens | Sewing Box's pins (droha), Paper Plane's chalk | "the drawer saved a piece as not interactable", "cannot be picked up after everything came back" | hand-tested 2026-09-27 |
| 11 | Flags are written only on locked pieces, the pass cannot re-enter itself, no pass on ControllerChanged | Fruit Stickers crash (droha) | every level's log is checked for a re-entered pass (none) | droha dragged stickers a minute, no crash |
| 12 | Unlock gives back the game's own "interactable", not "yes" | Sewing Box's Curved Needles Container | "can be picked up where the game did not allow it" | |
| 13 | Every sprite a locked piece shows is greyed (child sprites, its own sprite; not shadows) | Candles, Spice Jars, Pencils, Lamp, Eggs, Microscope, Frame Maze, Record Player, Books (11 levels) | "locked, not grey" | screenshot of Candles |
| 14 | Clear sprites stay clear; one that fades in is greyed then and comes back opaque; a colour is put back only while the grey is still on | Daggers' drawer masks, Books (Randomized) | "a clear sprite greyed into view", "colour not back" | screenshots of Daggers, Books |
| 15 | A collider or physics the game turns on after the lock took the piece comes back on | PawPrints (19 dead pieces) | "collider not back", "physics not back" | |
| 16 | A drawer or door frozen by an earlier pass, before it counted as one, gets its collider back | Sticky Drawer | "a locked cover lost its collider" | no level shows it since fix 17 (the gum is no longer a cover); kept as a fallback |
| 17 | A sticker's peel handle (`StickerObject.pluckObj`) locks with its sticker | Sticky Drawer (droha: Stickables solved with Sticking missing, through the gum) | hand-tested answer "should be locked" on every handle (`handles` in EXPECT) | hand-tested 2026-09-27: grey and fixed without Sticking, peeled and finished with it |
| 18 | A rag (`ClearingObject`) locks with the level's clearable pieces | PawPrints (droha: the cloth cleaned the grey paw prints and coffee spill) | as fix 17 | with fix 19 |
| 19 | A lock pass when the intro ends (`InterludeEnd`), on a level with a rag only | PawPrints: the game switches the cloth and 19 paw prints back on the frame after it (DevTools `flip:`), and picking the cloth up ignores the lock's flags. Only there because nothing else needs it: on every level it passes too, once the probe's keys stopped misreading reshuffled drawer contents | "should be frozen" on the handles; "locked, collider still on" | hand-tested 2026-09-27: grey and fixed without Tidying, cleaned with it |
| 20 | A match (`Match`) locks with the level's candles (`Candle`) | Candles (droha: the lit match, in colour and in no group, grew and shrank the grey candles) | hand-tested answer "should be locked" on the handles (`handles` in EXPECT) | hand-tested 2026-09-27: grey and fixed without Gadgets; with it, droha solved the candles |
| 21 | Cat Food Cans, Boxes (Stacked) and Presents (Stacked) reload when their pieces unlock mid-level (`ObjectLock.ResetOnUnlockLevels`), instead of unlocking in place | section 4 below (droha: a covered can "can't drag and drop") | Core `ObjectLockTests` (seen to fail with the rule off); the probe logs the reload as INFO | hand-tested 2026-09-27: "worked beautifully", "works correctly"; after the reload the game's own covered pieces are fixed again (DevTools `state:`) |

Fixes 12 to 16 were found by the probe; before them, the first 72 levels
had 6 failing and 23 to-review configs. After them all 22 levels with drawers
or doors pass every config (64), and the only failures left in the whole
sweep are the known cases under "Seen, not fixed". Re-run after fixes 17 to
19 (2026-09-27, build 460a795f): 21 of the 22 pass every config, and
Wilting Flowers keeps the Dirt review it had before (section 4); the handle
levels (Calendar, Fruit Stickers, PawPrints) pass; the only failures are the
collider cases in "Still needs hands" 2.

Fixes 17 to 19 were found by hand (the drawer and door matrix,
2026-09-27). A sticker is peeled by its handle and a mess is wiped by a
cloth, and no group lists either, so the probe never saw them: DevTools
`state:` now lists them with `handleOf`, the pieces each acts on. Calendar,
Fruit Stickers, Sticky Drawer and PawPrints are the levels with either.
Locking PawPrints' cloth was not enough on its own: the game switched it
back on when the intro ended, which is fix 19.

## Still needs hands

Set-up for each (I do this part; you only open the game): the hand-test
server with a seed holding every ability, then DevTools `traps:off`,
`revoke:<abilities>`, `boot:<index>`. Nothing needs a new seed.

### 1. Sewing Box: pieces that start in a drawer (fix 10) - ANSWERED

**2026-09-27: yes.** With the broken case staged (both drawers shut over the
locked pins after a lock pass, their saves reading "not interactable"),
giving Ordering back freed all three saves, and droha: "both can be picked
up". The zippers and safety pins were then solved. Kept as the recipe for a
re-test.

Why: the fix is proven by the probe, but droha found the bug by hand.

1. Sewing Box (1121) with Ordering and Sticking withheld; the Top Drawer is
   shut over its locked pins with a lock pass in between (the case that
   broke).
2. Check the safety pins and zippers are grey. Then Ordering is given back.
3. Open the Top Drawer and the Bottom Drawer.

Question: can you pick up the zipper and the safety pins that start inside
the drawers?

### 2. The game turns a locked piece's collider back on

Why: on these levels the game switches colliders back on after the lock
froze them, so until the next lock pass the piece is grey with its flags off
but can be hit. Whether a player can use it then depends on whether the
game's own interaction checks the flags.

| Level | Index | Pieces | Try |
|---|---|---|---|
| PawPrints | 57 | paw prints, coffee spill (19) | wipe or clear one |
| Trim Plant | 58 | leaves (16) | pluck one |
| TupperwareTower | 83 | tupperware (23) | pick one up |
| Bells | 1222 | Bell 1.2, 2.2, 3.3 | pick one up |
| Robots | 1230 | KeyInRobot2 Container, NeckContainer | put something in one |

Each with every ability withheld. Question: can you do anything with a grey
piece? If you can, the fix is a lock pass shortly after the level starts.

**2026-09-27, two answered.** Trim Plant: all 16 leaves locked with their
colliders back on (DevTools `state:`), and droha could not pluck one - the
flags hold on their own. PawPrints: droha cleaned the first screen with
Tidying missing, through the cloth (fixes 18 and 19); the leaves on the
second screen held. PawPrints' 19 colliders came back on at the end of the
intro, and fix 19 keeps them off.

**2026-09-27, the other three answered, each with every ability withheld
and the colliders read back on first (DevTools `state:`):** TupperwareTower
(12 visible pieces with colliders on) - "greyed out and not moveable,
nothing able to be done"; Bells (Bell 1.2, 2.2 and 3.3 with collider and
physics on) - "greyed out, not moveable"; Robots (KeyInRobot2 Container and
NeckContainer with colliders on) - nothing doable, no part solved (watched).
So on all four levels the flags hold on their own; section 2 is closed.

### 3. Locked pieces with a second collider

Why: the lock turns off a piece's own collider only. These pieces have
another live one under them, which might still take a click or push other
pieces.

| Level | Index | Pieces |
|---|---|---|
| Spoons | 24 | Spoon 1 (Wood) |
| Coins 1 (Shape) | 41 | Coin 1 (Large Silver) |
| Microscope | 69 | ScrollField 01_1 to 01_3 |
| Frame Maze | 72 | Frame-01, -03, -07, -08 rotateable |
| Candles | 78 | all six candles |
| Mirror | 79 | Box, Candle |
| TupperwareNesting | 82 | Square 1 to 3 |
| First Aid Kit | 1229 | Wipe Container |
| Cupcakes | 1236 | the ten icing layers |

Each with every ability withheld. Question: can you grab a grey piece, or does
a free piece bump into it where nothing is drawn? Spoons and Candles first:
they are the simplest.

**2026-09-27:** Spoons - "greyed out, not moveable, no solution available"
(Spoon 1's second collider live; nothing in the level is free to bump it).
Candles - the candles held, but the match, in no group, lit them: fix 20.
Coins 1 (Shape), Frame Maze - grey and fixed. Microscope - the zoom works, but
no shard can be dragged. Mirror - the free pieces move, the rest are grey and
fixed; the box still opens through its live second colliders, but the skull
inside is locked, and the candle's lit state (Gadgets) cannot be changed.
TupperwareNesting - grey and fixed, and the level cannot move to its next
stage. First Aid Kit - the grey pieces do not move and are not in the way of
the free ones. Cupcakes - only the candles (also in a free group) move; the
grey icing and wrappers do not, alone or dragged along by a candle, so it
cannot be solved. Section 3 is closed.

### 4. Pieces the game keeps fixed become movable after unlock (older than these fixes)

Why: the game makes some pieces not interactable itself after the lock has
taken them (a can under another can), and the lock cannot tell that write
from its own, so it gives back "interactable".

| Level | Index | Pieces |
|---|---|---|
| Cat Food Cans | 34 | Can Chicken 2, Can Fish 6, Can Pig 9 |
| Boxes (Stacked) | 53 | seven boxes |
| Presents (Stacked) | 1004 | eight presents |
| Wilting Flowers | 54 | the Dirt (cannot be dragged, so probably harmless) |

Load with every ability withheld, then give them back. Question: can you pull
a can or box out from under one on top of it, which normal play does not let
you do?

The fix, if wanted: intercept the game's own `SetInteractable` while a piece
is locked and remember what it asked for. That patches a method every
movable piece uses, so it needs its own crash check first.

**FIXED 2026-09-27 by a reload (fix 21).** On droha's call the three levels
restart when their ability arrives mid-level; every piece in them is
locked without it, so nothing is lost. The Dirt is left as it is.

**2026-09-27, confirmed, and worse than a free pick.** The probe still shows
it on all four (Cat Food Cans' Chicken 2, Fish 6 and Pig 9; seven boxes;
eight presents; the Dirt): the game had them not interactable and
prevented, and after the round trip they are neither. droha, Cat Food Cans
locked then given everything back: "i can't drag and drop things from the
middle, they don't drop, not all of them move" - a covered can can be
picked up but not put down. In a run that is Stacking arriving while the
level is open. Two ways to fix it: the patch above, which covers every
class; or asking each controller to re-mark its covered pieces after an
unlock - Cat Food Cans' StackablesY has DeactivateStackObjects(), Boxes and
Presents are StackableGrid and need their own, and the Dirt is a
CleanablesController (it cannot be dragged, so it is harmless).

### 5. Not covered by the probe

- **Radial Dance Party** (81): its pieces appear when the dance starts, after
  the probe's snapshot. The Cat Toys fade in (why fix 14 greys a fading
  sprite once it shows). Question: with Rotating withheld, do the toys go grey
  once they appear, and come back in colour when Rotating arrives?
  **2026-09-27:** no controllers register at load; petting the cat brings
  out the pencils, which arrive grey and cannot be spun (droha). The toys
  are the next phase and cannot appear without Rotating, and they are also
  in a free Draggables group, so the question cannot arise in play.
- **Fruit Stickers** (29): the crash needed dragging; fix 11 was hand-tested
  once. Worth a minute of sticking and peeling on each release.
  **2026-09-27:** a minute on build 69bddbe3 (peel handles voting), no crash
  (droha; watched).

## Seen, not fixed (visual only)

- **Seed Pods (65), Clover (68):** the game tints its pieces to 0.9 once,
  after the level's intro, and only the ones not locked then. A piece locked
  through the intro comes back at full white, 10% brighter than its
  neighbours.
- **Mirror (79)'s candle, Ghost Cat (1239)'s cats:** their animations repaint
  them every frame, so they show in colour while locked. They are left as the
  animation has them on unlock.
- **Robots (1230)'s little robot on a line:** grey with every ability
  withheld, but it moves, and its two antennas toggle up and down when
  clicked (droha, 2026-09-27). Its antennas have no collider of their own
  (DevTools `state:` col none), and nothing in the level can be solved like
  that, so no check comes early. Likewise the wind-up robot's button can
  still be pressed, but with its key locked in place the robot neither moves
  nor changes its head size.
