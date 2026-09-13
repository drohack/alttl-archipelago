# The cat trap, the doubled level, and whether the guard fixes it

Measured 2026-09-13 with `tools/repro-trap-freeze.py`. **The fix works**, on
the symptom that could be measured. The hard freeze itself was never
reproduced; what was reproduced is the corrupted state that precedes it.

## What droha reported

On the released **0.3.1** build: "When finishing a level I got a background
change trap. The background started kind of strobing/shifting between multiple
colors. It reset (maybe a cat trap as well?) and when I clicked anywhere the
game fully froze. Had to alt+F4." The log ends mid-navigation with no exception
in 1,549 lines. Watching an unguarded build reproduce it later: "oh god 2
levels loaded at once", with a screenshot of one puzzle drawn straight through
another.

## The measurement

Not the freeze. The freeze is a race that needs a click and showed up in maybe
a third of sessions; 29 attempts chasing it directly produced nothing. **Two
levels alive at once** is the state that leads there, it is a direct
consequence of a reset landing inside a navigation, and it can be counted on
every single attempt - `livelevels` in DevTools, asked with a puzzle on screen,
where exactly one loaded level is the only correct answer.

Two earlier versions of that measurement were wrong and worth recording:
counting `AllLevelInterfaces` reported 0 with a puzzle plainly visible (that
table holds authored interfaces, not instances), and counting straight after
the trap reported 0 every time (which is simply what "between levels" looks
like). Neither was a defect; both would have been read as one.

## The runs

Ten attempts each. Every attempt finishes a real puzzle with `solve:`, has the
trap delivered by the server the instant the game reports it beaten, and has
`jiggle` holding live settle tweens open across the window.

| Run | Build | Clean | Two levels alive |
|---|---|---|---|
| A | guard OFF, grace OFF, `CancelAnimations` OFF (0.3.1's `Spring`) | 1 | **9** |
| B | the real build, all three guards | 5 | 5 |
| C | the real build, **no trap sent at all** | 5 | 5 |

## Reading it

**Run A is the bug.** The corruption starts at attempt 2 - the first trap -
and never recovers; every later attempt reopens with two levels and the run
sticks on one puzzle it can no longer finish. The trap logs
`1 cat(s) reset the puzzle` every time, landing inside
`launching ... forceReload` exactly as droha's log shows.

**Run B looked like a partial failure and is not one.** Its corruption starts
at attempt 6, immediately after attempt 5, where the harness gave up on
MedicineCabinet's 13 controller groups and navigated away from a half-solved
puzzle.

**Run C is why that matters.** With no trap sent at all, the same build breaks
at the same attempt, in the same way, after the same MedicineCabinet bail. The
doubling in run B is the HARNESS abandoning a level mid-flight through
`menu:title` and `play` - something no player does and this script does
constantly. It is not the trap and not the mod.

So on the real build the trap never once caused a doubled level. It never
resets a level inside a navigation at all: every attempt in run B logged
`found nothing to knock over` instead of `reset the puzzle`.

## What is still not shown

**The hard freeze.** The game's Update loop stayed alive through all of it,
including run A's nine corrupted attempts and four real clicks into the
wreckage via `clickat`. droha's session ended with the game fully hung; that
last step has not been reproduced here. Two levels alive is a state consistent
with it - dead listeners on a level that should have been torn down - but the
link is inference, not measurement.

**A long session.** droha was ~44 puzzles into a 79-puzzle run, playing
offline. Every run here was a fresh room against a local server.

## Reproducing

    py -3.13 tools/repro-trap-freeze.py --attempts 10 <seed.zip>
    py -3.13 tools/repro-trap-freeze.py --no-trap --attempts 10 <seed.zip>

Run the second one before believing the first. It runs the game on virtual
desktop 1 and `harness_env` restores every setting it touches.

## Two harness limitations to fix before the next run

**`--no-trap` is not airtight.** It still saw one
`found nothing to knock over`, so a trap reached the game from somewhere this
script did not send it - most likely a received-items cache surviving in the
restored save rather than the parked room file. It does not affect the
conclusion above, because run C's doubling came after the MedicineCabinet bail
rather than after that trap, but a control that cannot guarantee zero traps is
weaker than it looks.

**MedicineCabinet cannot be finished by this script.** Thirteen controller
groups, and `solve:` in order does not complete it. Skipping that level, or
returning to the title cleanly instead of abandoning a half-solved puzzle,
would remove the only source of corruption the guarded build ever showed.
