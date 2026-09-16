# What happens when the credits unlock

**Measured 2026-09-14, and the design it questioned has since changed. This is
a historical record, not a description of current behaviour.** The probe that
produced it, `tools/probe-credits-goal.py`, was deleted once the question was
settled; the run below is what it found.

The seed used `levels_to_beat: 3`.

## What it showed

Unlocking the credits card took two things, and still does: the beaten count
being met AND the Credits item arriving. Neither alone is enough.

```
with the count met but no Credits item yet:
nothing said - correct, the run is not won without the item

then, in order:
received item: Credits
credits: unlocked after 1 puzzles
toast: The credits are unlocked - play them to finish the run

the credits card:
creditscard: id=Credits index=84 isUnlocked=True hasCompletionData=True unlockedOnLevelSelect=True

did the server get the goal:
no
```

## What that "no" meant, and what happened to it

At the time the goal was reported when the unlock CONDITION held, so a run
whose card had appeared but had not been played was ambiguous - and the probe
recorded the server not being told.

That is no longer the rule. The credits became an ending you play rather than
a card that merely appears, so the goal is reported when the credits have
actually been PLAYED and only then. `GoalLatch` states it in its own docstring.
0.3.3 then fixed the gap that change opened: the played flag lives in the run's
sidecar file, so finishing offline, playing the credits and reconnecting later
still reports the goal.

So the "no" above is a record of the old design being measured, not an open
bug. See the 0.3.2 and 0.3.3 entries in the CHANGELOG, and
`src/ALTTLArchipelago.Core/GoalLatch.cs`.
