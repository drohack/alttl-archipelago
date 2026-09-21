# Manual test: do these levels really need the abilities they declare?

---

## THE DRAG BENCH, 2026-09-19: the locks were cosmetic on three counts

**Answered by hand, all twelve controller classes, nothing granted.** The
question was never "do objects go grey" - it was whether a grey object can
still be moved. It could, in more ways than anyone had looked for.

Set up with a zero-ability run and `boot:`; the seed does NOT need to contain
the levels, because the gate is per controller CLASS against run-wide
inventory. `tools/setup-ability-handtest.py --drag` hunts for a seed holding
all twelve and does not need to - that hunt is what droha had already told me
to stop doing.

| Level | Class | Result |
|---|---|---|
| Calendar | Stickables | locked (after fix 1) |
| Trim Plant | Pluckables | locked |
| Books | Shuffleables | locked |
| Eggs | DraggablesOrdered | locked |
| Stacked Papers | StackablesZ | locked |
| Cat Food Cans | StackablesY | locked |
| Angled Image Frame | Rotateables | locked |
| Rock Collection | GridPuzzle | locked |
| Clover | SymmetricalPlaceables | locked |
| Breadtags | Removables + Draggables | **split, correctly** |
| Coins 2 (Dirtyness) | Dirtyables | locked (after fix 2) |
| SomethingEggstra Fridge | Containables + StackablesY + Draggables | split, correctly |
| Fruit Stickers | Pluckables + Stickables + Pannables | split, correctly |

Fruit Stickers is the level the bug was first seen on, and was re-checked
on its own rather than assumed from Calendar: it has THREE controllers to
Calendar's one, and its ungated `Pannables` group is the pan frame, which
must stay live or the level cannot be navigated.

Breadtags and the Fridge are the informative ones: plain `Draggables` maps to
no ability, so those groups stay open while the gated ones lock. A lock that
froze them too would be over-locking, the direction that can make a puzzle
impossible.

### Three defects, all the same shape

Each was the dimmer believing something about the base class that a subclass
had quietly changed.

1. **Flags alone never stopped a sticker.** `StickerObject` redeclares
   `PreventSelection` as a GET-ONLY property, and its real drag lives on a
   separate `PluckObject`. Nothing the dimmer could write reached either.
   Fixed by removing the collider - a pointer cannot hit what it cannot hit.
2. **`ManagedObjects` is not a controller's full set.** `Dirtyables` holds its
   coins in `dirtyObjects`/`cleanerObjects` and registers ONE object;
   `Containables` holds three more lists, `StackablesY` one, `Stickables` one.
   Coins 2 was fully playable while looking fully locked - the worst case,
   because the tint walks subrenderers and so everything went grey anyway.
   Fixed by reflecting over the CONCRETE controller class for LevelObject
   collections. `GetType()` returns the base wrapper, so the class has to be
   resolved from `GetIl2CppType().Name` and cast to.
3. **Locks landed up to a second late.** They were applied only by the 1 Hz
   poll, so every level opened with everything live. Fixed with a postfix on
   `ObjectController.RegisterObjectController()`.

### Two things that were tried and are deliberately NOT in the code

- **Collider removal alone.** It stops every interaction, and it drops any
  object the collider was holding up. droha: "i saw the eggs in the background
  be dimmed and fall off of the screen." The collider still goes, but the
  rigidbody is stopped in the same call.
- **Following a LevelObject's own object references.** Added to reach a
  sticker's `pluckObj`, then measured to be unnecessary - the collider freeze
  already covers it - and found to be harmful: an egg's `Container` on the
  Fridge is the strawberry basket, which belongs to the UNLOCKED group. The
  reference graph does not respect the group boundaries the gate is defined
  in terms of.

---

**STAGE 1 ANSWERED, 2026-09-18. Three of the four were the harness's fault;
one is a real table error.** Stages 2 and 3 still open. Set up with
`tools/setup-ability-handtest.py`.

droha played all four holding Containers, Jigsaw, Ordering and Stacking, with
**Drawer withheld**:

| Level | Finished without Drawer? | Verdict |
|---|---|---|
| **MedicineCabinet** | **YES - and every one of its 14 locations fired** | **table is WRONG** |
| NeatStreak_Tool Drawer | no | table is right |
| NeatStreak_Bathroom Drawer | no | table is right |
| NeatStreak_Paper Plane Supplies | no | table is right |

droha: "i was able to complete medicine cabinet, but all 3 drawer levels i was
not able to complete. i need containers/drawer to do those."

**The three NeatStreak levels confirm the harness limitation.** They cannot be
finished without Drawer, exactly as predicted - the drawer will not open, so
the contents are unreachable. `probe-level-requirements.py` "completed" them
only because forcing a controller's solved flag reaches objects a player
cannot. Nothing changes for these three, and the prediction in this file was
right.

**MedicineCabinet is a genuine table error, and a different shape from what
was expected.** Every location fired - all 13 parts, Solution 1 and Beaten -
while holding no Drawer at all. It has **no `DrawerController`**; its
requirement comes entirely from a hand-added `extraAbilities: ['Drawer']`,
presumably on the theory that a medicine cabinet has a door to open. The
measurement says nothing on the level needs it.

Removing that entry is a logic change - it feeds the requirement for every
Medicine Cabinet location, and `test_regression.py` pins the draw - so it is
droha's call, not an automatic edit.

## Why a person has to do it

`tools/probe-level-requirements.py` swept all 36 non-DLC levels with more than
one controller. It forces controllers one at a time and stops the moment
`LevelComplete` fires, so anything not yet forced is **provably not needed to
complete the level**. Twenty-eight of the 36 completed; seven of those
declared an ability the completion never used.

That measurement is sound in one direction only. It proves an ability was
unnecessary; it cannot prove the others are necessary, because forcing in
index order finds one sufficient set rather than the smallest one. And it is
blind to **physical access**: forcing a controller's flag reaches objects a
player cannot, which is why the drawers below are almost certainly the
harness's limitation rather than a table error.

A part check can also still need an ability the completion did not - the
requirement for a part is its own group's abilities, while the solution and
Beaten locations take the level's whole union.

## What a result means

- **Overstated** is the safe direction. The level asks for more than it needs,
  so it gates progress harder than the game does. Worth fixing, not urgent.
- **Understated** is the dangerous one, and stage 3 is where it could show:
  hold exactly the declared set and if objects stay dimmed, the puzzle cannot
  be finished at all.

Nothing in `levels.json` moves on the strength of this file alone. The
requirement feeds the logic, and `test_regression.py` pins the draw.

---

## Stage 1 - the four drawers. Expected to FAIL, and that is the useful answer

All four completed under a forced solve **without their `DrawerController`**,
because the harness rearranged the contents without ever opening the drawer.
The project already knows this shape:
`test_a_drawer_cannot_be_emptied_before_it_opens` exists because 0.3.0 logic
said Tool Drawer's 47 draggables needed nothing while the game kept the drawer
shut.

One seed, `Drawer` withheld from all four - the only coherent group here,
since nothing in it re-grants Drawer.

    py -3.13 tools/setup-ability-handtest.py

| Level | Holding | Can you finish it? | Notes |
|---|---|---|---|
| MedicineCabinet | Containers, Ordering, Stacking | | |
| NeatStreak_Paper Plane Supplies | Containers, Jigsaw, Ordering | | |
| NeatStreak_Tool Drawer | Containers | | |
| NeatStreak_Bathroom Drawer | Containers, Ordering | | |

**If you CANNOT finish these:** the table is right, the harness is limited,
and nothing changes. **If you CAN:** that is a real finding - the drawer is
not actually required.

## Stage 2 - the three real candidates. Expected to SUCCEED if the tables overstate

None is access-gated; all three are arrangement mechanics. **One level per
run**, because the three conflict: granting Coins 1 its `Ordering` would hand
Spoons the very ability it is meant to be missing.

    py -3.13 tools/setup-ability-handtest.py --stage2 "Spoons"
    py -3.13 tools/setup-ability-handtest.py --stage2 "Fruit Stickers"
    py -3.13 tools/setup-ability-handtest.py --stage2 "Coins 1 (Shape)"

| Level | Declares | Withheld | Holding | Can you finish it? |
|---|---|---|---|---|
| Spoons | Ordering, Stacking | **Ordering** | Stacking | **YES - every location fired** |
| Fruit Stickers | Sticking, Tidying | **Sticking** | Tidying | |
| Coins 1 (Shape) | Ordering, Stacking | **Stacking** | Ordering | |

### Spoons, answered 2026-09-18: the gate is TOOTHLESS

droha: "spoons fully completed". All four locations fired without Ordering -
both parts, both solutions and Beaten - INCLUDING `Size (Elastic)`, which the
table says needs Ordering.

The mod did gate it: `abilities: 1 locked, 1 open, 7 objects, waiting on
Ordering`. Asking the running game what that actually did to the objects
explains why it made no difference:

    Stacked         type=StackablesZ       objects=7 dimmed=0 shared=7
    Size (Elastic)  type=DraggablesOrdered objects=7 dimmed=0 shared=7

**Both controllers manage the same seven spoons.** The dimmer merges per
object and lets UNLOCKED WIN, so holding Stacking keeps all seven
interactive, and arranging them by size satisfies the Ordering controller as
a side effect. The Ordering gate cannot dim anything while Stacking is held.

This is the "toothless requirement" shape `probe-ability-locks.py` was written
to detect, confirmed end to end by a person. Note what it means for logic:
`Spoons - Size (Elastic)` is gated behind Ordering but earnable without it -
a check available EARLIER than the logic expects. That is the safe direction,
never unwinnable, but it is a real inaccuracy.

**Finishing it means the requirement is overstated.**

## Stage 3 - the eight the harness cannot finish at all

These never complete under a forced solve, so nothing is known about their
requirements either way. Each is granted **exactly its declared set** - no
more, because holding extra cannot rule out a table that demands too few.
One level per run, for that reason.

    py -3.13 tools/setup-ability-handtest.py --stage3 "Radial Dance Party"
    ... and so on for each name below.

| Level | Declares | Why the harness cannot do it | Can you finish it? |
|---|---|---|---|
| Radial Dance Party | Rotating | registers **zero** controllers at boot | |
| PawPrints | Tidying | phased; `PawPrintsPhaseLevel` never raises `LevelComplete` | |
| Sharp Pencils | Ordering, Tidying | `Removables` - a sequence, not an arrangement | |
| Record Player | Gadgets, Rotating | `Rotateables` + `GenericLevelObjects` | |
| Wilting Flowers | Gadgets, Tidying | `AnimScrubbables` - scrub an animation | |
| Lamp | Gadgets, Rotating | `Toggleables` - switches | |
| Desktop Computer | Containers, Gadgets, Rotating, Swapping | already on `KNOWN_UNFORCEABLE` | |
| TupperwareNesting | Containers, Grids, Stacking | phased | |

**If a level cannot be finished on its declared set, the table is UNDERSTATED**
- the dangerous direction, and worth stopping for.

Radial Dance Party is the one worth extra attention: zero controllers at boot
means the randomizer mints no part locations for it either, so it is worth
checking what the card offers.

---

## Running it

Each stage prints the exact commands. In short:

    py -3.13 tools/setup-ability-handtest.py        # stage 1, builds the seed

then, in a real terminal, the two lines it prints - a `tail -f`-fed
MultiServer and the game exe - and connect as `droha`. Stages 2 and 3 append
to the same running server, so nothing needs restarting between levels.

The seed has every slot open, no packs to earn, `skip_count: 0` so a Skip
cannot quietly finish the level under test, and full hint coverage so you can
look a solution up rather than fight it.

**Cleaning up** - nothing does it for you, and until it runs the mod points at
localhost instead of your real server:

    py -3.13 tools/harness_env.py --restore-latest
