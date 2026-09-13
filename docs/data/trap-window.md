# What the trap guard and the background poll actually see

Measured by `tools/probe-trap-window.py` on 2026-09-13, against the mod built
from this working tree. droha asked "does the cat trap hold actually work at
the end of the level when it fires again? did you test these on an actual
game?" - it had not been, and this is that test.

## What it settles

**The hold releases, and the trap still goes off.** Test B sends a Cat Trap
and relaunches the level 150ms later, so the trap ticks while the load is in
flight. The flags show the window opening and closing - loaded=True, then
loaded=False with the Level gone, then loaded=True again - and the trap lands
on the far side of it as `1 cat(s) reset the puzzle`. Held, not spent.

**A trap on the level select cannot be stranded.** `ActiveLevelInterface` is
NULL there, so the guard does not fire at all and the trap falls through to
the existing miss. Confirmed separately: `1 cat(s) found nothing to knock
over`.

**The background poll is not repainting every tick.** Ten seconds settled in a
puzzle holding a Background Change Trap: ZERO writes. Through a level load:
zero. Across the whole session: one. The per-frame work is a Color comparison;
the write happens about once per level, which is what it was always for.

## What it does NOT settle

**The original freeze was not reproduced.** In this run the load window shows
`level=null`, which the released 0.3.1 code already treated as a miss - so
this is not the state droha's game died in. The guard is shown to be safe and
to preserve the trap; it is NOT shown to fix the reported hang.

**Test C did not exercise a real completion.** DevTools `complete` does not
produce a real post-level state (`docs/release-testing.md:35`), and the flags
confirm it: the level never left `loaded=True`, and the trap simply sprang.
The completion grace never ran.

## Raw measurements

```
== level select ==
watch: gameState=Levels_GameState interface=null loaded=- transitioning=- level=null

== settled in a puzzle ==
watch: gameState=Gameplay_GameState interface=Stamps (Randomized) loaded=True transitioning=False level=present

== camera while settled ==
0.953,0.871,0.541

== repaints while settled ==
(nothing recorded)

== A: trap in a settled puzzle ==
trap: using 'badge8-cat8' for the cat paw
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle

== B: trap into a load ==
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle

== B: flags through the load ==
watch: gameState=Gameplay_GameState interface=Stamps (Randomized) loaded=True transitioning=False level=present
watch: gameState=Gameplay_GameState interface=Stamps (Randomized) loaded=False transitioning=False level=null
watch: gameState=Gameplay_GameState interface=Stamps (Randomized) loaded=True transitioning=False level=present

== B: repaints through the load ==
(nothing recorded)

== C: trap onto a completion ==
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle

== C: flags through the completion ==
watch: gameState=Gameplay_GameState interface=Stamps (Randomized) loaded=True transitioning=False level=present

== C: repaints through the completion ==
backgrounds: repainted the camera 1 time(s)

== alive after ==
ALIVE

== every trap line in the run ==
trap: using 'badge8-cat8' for the cat paw
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle
trap: stopped animations on 9 object(s), 0 of them detached, before the reset
trap: 1 cat(s) reset the puzzle

```
