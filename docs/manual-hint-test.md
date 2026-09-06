# Manual test: the Hint Page gate

Everything about Hint Pages is verified except the one thing that needs hands
on a mouse. This is that check, and why it cannot be automated.

## Why a person has to do it

The randomizer charges a Hint Page in a postfix on `LevelInterface.HintTaken`,
which the game raises once a scribble has been rubbed far enough off. That
postfix IS exercised - a DevTools `hinttaken` command calls the method
directly, and the charge, the toast, the run-file write and the note all
follow. `HintsTakenCounter.CheckHintTaken` subscribes to the same event to keep
a Steam stat, so the game demonstrably raises it in real play.

What no harness has managed is the erasing itself. A synthetic pointer drag
through `ExecuteEvents` reaches the eraser's drag handlers but never puts the
`CleanableSurface` into a wiping state, so the game never gets as far as
raising the event. One human scrub closes the gap.

## Setting it up

The seed is shaped for the test rather than for play: the whole track is open
from the first second, nothing is ability-locked, cat traps are off so nothing
interrupts a scrub, and you start holding five Hint Pages.

    # 1. check nothing is already on the port - a stale server silently
    #    serves the OLD seed and the puzzle count will not match this file
    powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort 38281"

    # 2. regenerate the seed if it is missing (yaml is in testserver/yaml-hint)
    cd Archipelago
    python Generate.py --player_files_path ../testserver/yaml-hint \
                       --seed 5150 --outputpath ../testserver/out-hint

    # 3. serve it, from a real terminal so the console works
    python MultiServer.py --port 38281 ../testserver/out-hint/AP_*.zip

    # 4. launch the game by running the exe directly, with no arguments
    "G:/Games/Steam/steamapps/common/A Little To The Left/A Little To The Left.exe"

The line that proves the server really bound is `server listening on
0.0.0.0:38281`. "Hosting game at ..." prints even when the bind failed.

## The puzzles you need, and why

Seed 5150, 24 puzzles. These are the ones that matter:

| puzzle | pages | what it is for |
|---|---|---|
| Tool Drawer (Drawer Chores) | 1 | the ordinary case |
| Buttons | 2 | page two costs a second page |
| Spice Jars | 2 | same, second opinion |
| Pretzels (Snack Pack) | 3 | |
| Jack O'Lanterns (Trick or Tidy) | 4 | the deepest notepad in the seed |
| Post-It Notes (Randomized) | 1 | generator hints, see below |
| Pencils (Randomized) | 2 | |
| Procedural Grid Puzzle | 1 | |

The three generators are there because they were long believed to have NO
hint - `LevelInterface.HintImages` reports zero for them. They answer
`GetRandomizerHints()` instead, and every puzzle in the game turns out to have
at least one page. Opening a hint on Pencils is what found it.

## The checks

**1. Erasing charges exactly one page.** On Tool Drawer, pause, open Hint. The
note under the page should read `Rubbing this out uses a Hint Page - you have
5`. Rub the scribble off. Expect one toast, `Hint Page used - 4 left`, and the
note to change to `Already uncovered - reading this again is free`.

This is the check the whole document exists for. Watch particularly that it
fires ONCE - not once per stroke of the eraser.

**2. A page you paid for stays paid.** Leave Tool Drawer, come back, open the
hint again. It should still say `Already uncovered`, the count should still be
4, and rubbing at it should cost nothing.

**3. Page two costs its own page.** On Buttons, uncover page one, then turn to
page two. The note should go back to `Rubbing this out uses a Hint Page`, and
uncovering it should take you to 3.

**4. Nothing to spend, nothing to erase.** Spend down to zero, then open a hint
you have not paid for. The note should read `No Hint Pages - find one to
uncover this hint`, the notepad should still OPEN, and the scribble should not
come off however hard you rub.

**5. The generators have hints too.** On Post-It Notes, Pencils and Procedural
Grid, the pause menu should show a COUNT, not `no hint`, and the notepad should
open a real hint. The `no hint` wording still exists as a guard but is now
believed unreachable: no level in the game lacks a hint.

**6. Backgrounds.** You start with one of each. The puzzle backdrop and the
pause screen should both be a colour from the game's own palette rather than
the level's usual one. Restart the game and reconnect: both should come back
the SAME colour, not a new one.

## If something is wrong

`BepInEx/LogOutput.log` in the game folder carries a line per charge:

    hint: page 12:0 read, 4 left        <- slot 12, page 0, four left
    hint: refused, none held

and the run file records what has been paid for, as `"slot:page"` keys:

    %LOCALAPPDATA%Low/maxinferno/A Little To The Left/save_ap_droha_<seed>.run.json
