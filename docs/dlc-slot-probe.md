# Every DLC slot, probed one at a time

`tools/probe-slots.py --dlc`, 2026-09-21, against the gate's own seed
(`--seed 20260906`, `testserver/out-dlc`). A fresh game session per slot,
no items collected beyond the seed's `start_inventory`, so each verdict
is about that level alone.

The mod was rebuilt from source first. This matters: `release-test/`
holds the 0.4.0 zip, and that build contains neither the `Earned()`
reachability gate nor the ability-lock register hook from `b29b34d`.

## Result: 8 slots, 0 needing attention

The prediction column is computed from the seed alone - which locations
are reachable holding only what the run starts with (`Stacking`). It is
the DATA layer's answer; the verdict is the game's.

| Slot | Level | Reachable at start | Verdict | Agree |
|---|---|---|---|---|
| 0 | DLC2 Pizza | 0/5, needs Distributing | gated (expected) | yes |
| 1 | DLC2 Books Stacked | 6/6 | beaten | yes |
| 2 | DLC1 Clock Cupboard | 1/4, needs Drawer + Gadgets | **beaten** | no |
| 3 | DLC2 Bells | 0/4, needs Drawer + Ordering | gated (expected) | yes |
| 4 | DLC1 Game Pieces | 5/9, needs Containers + Drawer | partial | yes |
| 5 | DLC1 Bathroom Cupboard | 1/5, needs Containers + Drawer | partial | yes |
| 6 | DLC1 Filing Cabinet | 0/4, needs Swapping | gated (expected) | yes |
| 7 | DLC1 Daggers | 2/5, needs Drawer | **beaten** | no |

A cat trap fired on slots 4 and 5 and was refunded correctly both times.

## The two disagreements, and why neither is a bug

Clock Cupboard and Daggers COMPLETED while a gated controller was never
solved - the harness logged `not forcing AnimScrubbables` and `not
forcing DrawerExpandableController` respectively, so the dimmer really
had locked them, and the level finished anyway.

The `- Beaten` location's requirement is the level's whole ability union
(`rules.requirements`), so logic says those two Beaten checks were not
reachable. The mod filed them regardless, because the Beaten path is
deliberately not gated by `Earned()`: it is an addressless event
location with no recovery path.

**This is the safe direction and needs no change.** Beaten carries the
`Level Beaten` event that counts toward the goal; filing it early makes
the goal reachable sooner than logic expects, never later. Nothing is
stranded, and with `accessibility: full` every location is reachable
anyway. The logic is simply more conservative than the game.

The unsafe direction would be the opposite - logic believing a level
needs LESS than it does - and that is why `bypassedAbilities` entries
are only added after droha plays the level by hand.

What it does tell us: **Clock Cupboard does not need Gadgets, and
Daggers does not need Drawer, in order to be completed.** Both are
candidates for `bypassedAbilities`, and neither should be entered on the
strength of this probe alone. They are recorded here for a hand test.

Distinct from `docs/toothless-gates.md`, which asks whether a gate dims
anything. These gates DID dim - the harness refused to touch them. The
level just does not need them to finish.

## What this rules out

Combined with `tools/test_harness_data.py` (DATA) and
`tools/test_run_model.py` (CHOICE, which clears this same seed on paper
in 14 rounds, 8/8, spending 3 Skips), all three testable layers are
clean on the DLC content. Whatever produced 23/25 on 2026-09-20 was not
any of them - and the mod under test on those runs was the stale 0.4.0
build, which contains neither the reachability gate nor the ability-lock
register hook.

The probe also corrected the paper model. It first reported 11 rounds
and zero Skips because it collected the three container locations in
this seed for free; the harness cannot pull a drawer, so each of Clock
Cupboard, Daggers and Game Pieces strands its card until a Skip releases
it. Three Skips of the six available. The model reads the excluded list
from the same yaml the gate generates from now, so they cannot drift.
