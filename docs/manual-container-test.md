# Manual test: do container locations actually pay a check?

## ANSWERED, 2026-09-17: yes. The data was right and the harness was wrong.

droha played all three representative levels by hand, on game version 3.6.1
with the 0.4.0 mod, and **every flagged location fired**:

| Level | Group | Result |
|---|---|---|
| Game Pieces (Cupboards and Drawers) | `Drawers` | **fires** - all 8 of its locations paid out |
| Tea Cabinet (Cupboards and Drawers) | `Cupboard Doors` | **fires** - all 8 paid out |
| Robots (Seeing Stars) | `Spring` | **fires** |
| Robots (Seeing Stars) | `Spring Containable` | **fires** |

droha: "i was able to do all 3 just fine."

Those three cover all fourteen flagged DLC rows between them - `Game Pieces`
is a plain `DrawerController`, which is twelve of the fourteen; `Tea Cabinet`
is the `AnimScrubbables` cupboard-door pair; `Robots` is the two odd
single-object groups. **Nothing was removed from `levels.json`, and nothing
should be.**

The base-game rows need no test either. droha had already answered two from
real play, and the third - `Paper Plane Supplies / Drawer` - is the same plain
`DrawerController` that Game Pieces just proved.

**What this leaves is a GATE bug, not a data bug.** See "What is actually at
stake" below: the harness cannot pull a drawer open, so a level can be beaten
with one of its locations unearned, and an item placed there strands the run.
That is what to fix.

The rest of this file is kept as the record of how the question was asked.

---

Twenty-two group locations were reported as never paying a check when a
harness force-solved every controller on the level. **That report was not
trustworthy**, and this file exists to replace it with something that is.

## Why a person has to do it

The harness solves a level by setting each registered controller's solved flag
and dispatching its event. That works for a puzzle you complete by arranging
objects. It does not obviously work for a container - a drawer you pull open,
a cupboard door you swing, a lid you lift off - because the thing being
"solved" is an interaction, not an arrangement.

So a group that never fires under force-solve has two possible explanations
and the harness cannot tell them apart:

1. **Dead.** It registers, mints a location, and no player can ever earn it.
   That is a run-breaking bug: the generator will happily place a Progressive
   Puzzle Pack there and the run stops.
2. **Unreachable by the harness.** A person opening the drawer by hand earns
   it perfectly well.

**THIS EXACT CLASS IS ALREADY DOCUMENTED**, which is the strongest reason to
distrust the report. `docs/release-testing.md` records Desktop Computer's
`Computer Errors` as a sequence "you START and FINISH, rather than an
arrangement you tidy. Forcing its solved flag sets a bit for a sequence that
never ran." That level sits in `KNOWN_UNFORCEABLE` in `release_e2e.py` for
precisely this reason, alongside TupperwareTower, and both were settled by
hand on 2026-09-15 with the answer that the levels are fine and the harness is
the limited one.

A drawer you pull open and a cupboard door you swing are the same shape of
thing as a sequence you start and finish. So the prior here is that these are
harness limitations too, and the burden is on proving otherwise.

droha has already disproved the harness on two of them from real play:

- **Desktop Computer / Computer Errors** - "I've played desktop computer and
  done the computer errors just fine. It has an animation to it and is a mini
  solution, it triggered its location correctly."
- **MedicineCabinet / Jar Lid** - the q-tip jar. The lid has to come off
  before the q-tips can go in, and it has been seen greyed out, which is a
  dependency the harness does not satisfy.

Two disproofs out of the three base-game entries is enough to distrust the
whole list, so **nothing has been removed from `levels.json`**. This test is
how each entry gets an answer.

## What is actually at stake

If an entry is reason 2, the data is right and the RELEASE GATE is what needs
fixing. It stalled a DLC run at 5 of 8 puzzles because the only Progressive
Puzzle Pack sat on `Game Pieces - Drawers`, and the harness cannot open a
drawer.

Note what the gate did NOT do there: it never spent a Skip on Game Pieces,
because it spends one only for a level it cannot BEAT, and Game Pieces was
beaten - its Beaten token banked while one of its seven locations stayed
unearned. That is the gate's gap, and it is a different gap from the
`KNOWN_UNFORCEABLE` list: "beaten but not fully checked" is a state the
harness has no answer for.

**A Skip would have rescued it, and the reason it was thought otherwise is
worth keeping.** A comment in `release_e2e.py` claimed a beaten level "never
fires `LevelComplete` again", so a Skip spent there would grant nothing.
`tools/probe-skip-beaten.py` measured it on 2026-09-18 against a pre-fix
build: it fires. Re-entering a beaten puzzle RELOADS it, and a reloaded level
completes and skips like any other - beating DLC1 Filing Cabinet, re-entering
it and skipping sent Solutions 2 and 3. The premise had to be wrong, because
the only way to press Skip is to be standing in a loaded level.

What that same measurement DID find is a real waste: a Skip was consumed on a
slot with nothing left to grant, logging `skip: spent one, 0 left` and then
`sent 0 remaining location(s)`. `Skips.BeforeSkipLevel` now refuses that
instead of taking the item, and `tools/probe-skip-beaten.py` holds it there -
that assertion fails on the pre-fix build and passes on this one.

If an entry is reason 1, that location has to come out of `levels.json`, the
way TupperwareTower's two `StackableGrid`s and SomethingEggstra Fridge's spare
groups already did.

## Setting it up

Two seeds, already generated. The DLC one draws all 62 DLC puzzles into 79
slots, so every level below is certainly in it; the base one was checked to
contain its four.

Everything that could interfere is off: no ability locks (so nothing is
greyed out for the wrong reason), no cat traps (so nothing resets mid-arrange),
no skips (so a level cannot be cleared without being played), and every hint
page granted so you can look up a solution rather than fight it.

    # 1. nothing already on the port - a stale server serves the OLD seed
    powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort 38281"

    # 2. serve the DLC half, from a real terminal so the console works
    cd Archipelago
    python MultiServer.py --port 38281 ../testserver/out-container-dlc/AP_*.zip

    # 3. launch the game by running the exe directly, with no arguments
    "G:/Games/Steam/steamapps/common/A Little To The Left/A Little To The Left.exe"

Connect as `droha`. Swap in `../testserver/out-container-base/AP_*.zip` for the
base-game half.

To regenerate either seed:

    cd Archipelago
    python Generate.py --player_files_path ../testserver/yaml-container \
                       --seed 5151 --outputpath ../testserver/out-container-dlc

## What to do on each level

For each row below:

1. Open the level from the run's track.
2. **Play the container itself** - pull the drawer fully open, swing the
   cupboard doors, lift the lid off. Do the thing the group is named after,
   deliberately, not just the arranging inside it.
3. Finish the puzzle normally.
4. Watch for the check. Either is fine as evidence:
   - the toast in-game naming the location, or
   - a `check: <level> - <group>` line in
     `<game>/BepInEx/LogOutput.log`.
5. Write **fires** or **never fires** in the table.

If a group never fires even when you work the container by hand and the level
completes, that is the real thing - note anything you noticed about why.

## The rows

Name in the table is the location as a player sees it, which is what the log
prints.

### DLC - the ones that matter for this release

| Level | Group | What to work | Result |
|---|---|---|---|
| Clock Cupboard (Cupboards and Drawers) | Cupboard Doors | the cupboard doors | |
| Craft Supplies (Cupboards and Drawers) | Drawers | the drawers | |
| Daggers (Cupboards and Drawers) | Drawers | the drawers (this one expands) | |
| Game Pieces (Cupboards and Drawers) | Drawers | the drawers | |
| Jewelry Box (Cupboards and Drawers) | Drawers | the drawers | |
| Nested Drawers (Cupboards and Drawers) | Drawers | the drawers | |
| Sewing Box (Cupboards and Drawers) | Drawers | the drawers | |
| Tea Cabinet (Cupboards and Drawers) | Cupboard Doors | the cupboard doors | |
| Combs (Seeing Stars) | Drawer | the drawer | |
| Junk Drawer Transforming (Seeing Stars) | Drawer | the drawer | |
| Material Drawers (Seeing Stars) | Drawer Controller | the drawers | |
| Sticky Drawer (Seeing Stars) | Drawer | the drawer (it sticks) | |
| Robots (Seeing Stars) | Spring | the single spring | |
| Robots (Seeing Stars) | Spring Containable | the spring's socket | |

**Game Pieces is the important one.** It is the level whose `Drawers` location
held the Puzzle Pack that deadlocked the gate run, so its answer decides
whether the data or the harness is wrong.

### Base game - already half answered

These shipped in 0.3.0, so if any is genuinely dead, removing it renumbers
every location after it and breaks seeds in flight. Do not change anything on
the strength of a harness report.

| Level | Group | Expected | Result |
|---|---|---|---|
| Desktop Computer | Computer Errors | **fires** - droha has played it | |
| Medicine Cabinet | Jar Lid | **fires**, after the lid is taken off | |
| Paper Plane Supplies (Drawer Chores) | Drawer | unknown - the same drawer shape as the DLC rows | |

### Not part of this test

`TupperwareNesting` also appeared in the report, for `(Large Square)`, `Food`,
`Stack 2`, `Stack 3` and `Tray`. Those are its documented PHASED groups: the
level reveals them only as the earlier ones are solved, and a single
force-solve pass cannot reach them. They are known-good and listed here only so
nobody re-investigates them.

## Recording the answer

Fill the Result column in, and note the date and the game version. Then:

- every row that **fires** means the data is right and the gate's solver needs
  to handle containers;
- every row that **never fires** goes to `SurveyCrossCheckTests` with its
  evidence, and its controller comes out of `levels.json` - DLC rows freely,
  base rows only as a deliberate id-breaking change.
