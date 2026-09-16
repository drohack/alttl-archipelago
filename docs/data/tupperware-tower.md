# TupperwareTower: are Foundation and Falling Blocks checks?

**No.** They are the tower's mechanism. They never raise a solved event, so as
locations they could never be collected, and they were deliberately deleted -
see `tools/probe-dead-controllers.py`, which measured it, and
`test_fill_stress.py`'s split comment recording the day they went: "Now
(24, 94). TupperwareTower lost three groups and gained one back".

The level still requires Grids through `extraAbilities`, because the falling
blocks are dimmed without it. The mismatch the release gate used to report is
a deliberate gap and is allowlisted in `KNOWN_TABLE_GAPS`.

**The measurement below concluded the opposite, and it was wrong.** It is kept
because the listings are real and because the trap is worth leaving written
down: the probe solves only the Tower, sees the level fail to complete, and
reads that as "the other two are work the player must do". But forcing
`SetSolved` on the Tower does not actually finish the puzzle, so the level was
never going to complete, and its not completing says nothing at all about the
other two controllers. The question it needed to ask was whether a solved
event ever fires for them - which is a different probe.

Measured by `tools/probe-tupperware-tower.py`, since deleted - it reached
the wrong conclusion (see above) and the right one is now pinned by
`KNOWN_TABLE_GAPS` in `tools/release-e2e.py` and by `test_fill_stress.py`. It
booted the level with no run and no server,
solves only the Tower, and looks at what the other two do.

## as it loads

```
[0] Foundation type=StackableGrid solved=False
[1] Tower type=TupperwareTower solved=False
[2] Falling Blocks type=StackableGrid solved=False
```

## after solving only the Tower

```
[0] Foundation type=StackableGrid solved=False
[1] Tower type=TupperwareTower solved=True
[2] Falling Blocks type=StackableGrid solved=False
```

## did the level complete

```
no
```

## verdict, as this probe reported it

```
WORK. They stayed unsolved and the level would not finish, so a player has to
do them - they are real checks and the table owes this level two locations.
```

Wrong, for the reason above. The script now reports this same evidence as
INCONCLUSIVE.
