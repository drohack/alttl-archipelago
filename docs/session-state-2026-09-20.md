# Where this work stands

Written to survive a context compaction. Nothing here is committed except
the batch noted below.

## Committed already

`b29b34d` - the ability locks were cosmetic on three counts. Collider +
rigidbody freeze, the concrete-class reflection for objects held outside
`ManagedObjects`, and the register-time hook. Hand-verified by droha
across twelve controller classes.

## The DLC plan, 2026-09-21

Six phases, gate last. Phases 0-4 are done and green; Phase 5 is running.
See `~/.claude/plans/yeah-i-think-it-s-deep-moler.md`.

### Phase 0 - three harness bugs found by reading, not running

- **`--steady` was dead code.** `STEADY` was set by the flag and read
  nowhere; `cat_trap_chance` consulted `QUICK` only. Every run described
  as "repeatable" still had cat traps at 25%, so every comparison of two
  steady runs was noise. The yaml is now a pure function, `yaml_text`,
  which is what made it testable at all.
- **`play()` returned five values on the never-connected path** where the
  caller unpacks six. The one path written to print
  `connected to the server: FAIL` raised `ValueError` instead.
- **`--quick` was documented as three puzzles** in three places and has
  always been eight; `PUZZLES` has a floor and is never reassigned.

`tools/mutate-scheduler.py`: 22/22 caught, including six new mutations
for the above.

### Phase 1 - the apworld

New `apworld/alttl/test/test_dlc.py`, 27 tests, milliseconds. Covers the
three seams that had nothing:

- `slots._eligible`, the only code keying levels to a DLC, including the
  four DLC levels whose `source` is `generator`.
- `bypassedAbilities` and the `enforced_*` views - all eight levels
  pinned by name, both the solution and the part path.
- slot_data with a DLC on. The existing golden is built with both DLCs
  off, so `classes_for("Distributing")` had never been reached through
  the payload the mod parses.

DLC ids frozen (146 / 267 / 845, `Distributing` = 4050018, DLC block
after Credits). The DRAW is deliberately NOT frozen - `levels.json` is
still being corrected.

New `tools/mutate-apworld.py`: 13/13 caught. It found a real gap - the
part path in `rules.requirements` was untested and a mutation reading
`part_abilities` instead of `enforced_part_abilities` went unnoticed.

Apworld suite: 149 tests, up from 122.

### Phase 2 - the mod

- New `src/ALTTLArchipelago.Core.Tests/EarnedContractTests.cs`, 9 tests.
  `Earned()` is private in an assembly CI cannot build; its contract -
  `IsReachable(name, int.MaxValue, abilities)` - is now pinned.
  Four hand-run mutations of `IsReachable` all caught.
- **The asymmetry is resolved as "leave it".** `Earned()` withholds using
  `int.MaxValue` packs while `SweepAlreadySolved` and
  `FileSolutionsAlreadyEarned` use the real `Track.State?.PacksHeld ?? 0`,
  so the recovery is stricter than the gate. It cannot strand a check:
  `EnterSlot` sets `_auditedCount = 0`, so re-entering the card re-runs
  the sweep, and a player cannot stand in a slot whose pack has not
  opened. One-visit delay, not a lost check. Making the recovery use
  `int.MaxValue` too would file pack-gated checks the logic says are
  unreachable, which is the direction that breaks a multiworld.
- New `tools/check-devtools.py`, wired into CI. DevTools cannot have a
  test project - it references the game. This compares the command
  ladder against `docs/devtools.md` in both directions, which caught 30
  undocumented commands; all 30 are now documented from the source.
  Core suite: 320 tests, up from 312.

### Phase 3 - the DATA layer, on a real DLC seed

New `tools/make-seed.py` generates a base or DLC seed with `Generate.py`
alone - no game, seconds - into `testserver/out-base` and
`testserver/out-dlc`. `tools/test_harness_data.py` now takes
`ALTTL_SEED_DIR` instead of reading whichever seed was lying in
`out-e2e`, which is why it had only ever seen base-game names.

**The leading suspect was wrong.** DLC levels are renamed for display
(`DLC1 Trophy Cabinet` -> `Trophy Cabinet (Cupboards and Drawers)`) and
the slot-to-location mapping matches on display name, so a DLC slot
mapping to nothing would be invisible to the scheduler and look exactly
like the 23/25 stall. Measured against a real DLC seed: it maps
correctly. Kept as a named test so nobody suspects it twice. 11 tests,
green on both seeds.

### Phase 4 - the CHOICE layer, on that same seed

New `tools/test_run_model.py`. The existing whole-run model hands the
scheduler a world where every slot is open from round one and packs do
not exist; the real DLC seed opens 5 of 8 and waits on one Progressive
Puzzle Pack. That difference was invisible to every test written before.

**The DLC seed clears on paper in 14 rounds, 8/8, spending 3 Skips** -
one for each card carrying a container location the harness cannot pull
open. Both negative tests pass (a withheld pack, and a run with no Skips
and unforceable slots), so this is not a vacuous green.

**The model's FIRST answer was wrong and was corrected against reality.**
It said 11 rounds and zero Skips, because it collected the three
container locations in this seed for free - the harness cannot. The real
run was at round 11 with 3 of 8 and a Skip already spent, which is what
exposed it. `container_excludes()` now reads the same yaml the gate
generates from, so the two cannot drift, and three tests keep the model
honest: the unearnable set must be non-empty, the Skip bill must equal
the number of stranded slots, and the assertion is on BEATEN TOKENS
rather than "every location collected" - a card with an unreachable
drawer location is beaten without being cleared, and that is what the
gate actually counts.

DATA and CHOICE are therefore both ruled out as the cause of 23/25.

### Phase 5 - the DRIVE layer (running)

`tools/probe-slots.py --dlc`, one slot at a time, fresh session each.
Two bugs fixed in the probe before trusting it:

- **`--dlc` was in its usage line and parsed nowhere.** It probed
  whichever seed sat in `out-e2e`, which for most of 2026-09-20 was the
  base-game one. Every "the DLC slots are fine" reading through this
  probe was a reading of the wrong seed.
- **It never restored the player's config.** It wrote `Host=localhost`,
  `AutoConnect=true` and a fixed slot name and left them. Now wrapped in
  `harness_env.Environment`.

Also: the game had the **0.4.0 release zip** installed, which the gate
installs by design. That build predates the uncommitted `Earned()` gate
AND commit `b29b34d`, so yesterday's DLC gate runs were not testing
either. Rebuilt from source and verified the deployed binary contains
both.

### Phase 6 - the gate, once: 19/25

Ran with freshly built `dist` assets. Six failures, and **five of them
are one defect**:

| Failure | Cause |
|---|---|
| a Skip was spent only where one is known to be needed | THE defect |
| all 8 puzzles beaten | starved at 5/8 |
| the mod agrees every puzzle was beaten | same |
| the credits unlocked | same |
| the mod reported the goal | same |
| the server agrees the goal is met | same |

Four Skips went to levels not on `KNOWN_UNFORCEABLE`; the supply of five
ran out in round 15 and the run starved. Everything else passed,
including the two that matter most here: **no solve threw inside the
game**, and **no Skip covered for a level the mod was still gating** -
the assertion with teeth. This is not a gating bug and not a mod bug.

**Fixed, with a failing test first:** an unexpected Skip is now REFUSED
rather than spent, announced at the point it happens, and still fails
the verdict. It no longer drains the supply and turns one defect into
six red lines. `spent` and the new `surprises` list are both checked so
the refusal cannot silently delete the assertion that motivated it.
Scheduler suite 51 tests, mutations 26/26.

### The hole in Phase 5, found by the gate

`probe-slots.py` tests each slot holding only the seed's starting
inventory. Any ability-gated level therefore comes back
`gated (expected)` **without ever being attempted** - so its solving is
completely unmeasured. Three of the eight DLC slots (Pizza, Bells,
Filing Cabinet) were reported fine on that basis. DLC2 Pizza was the
first level the gate could not force.

Two fixes: the verdict now reads `gated, SOLVING UNTESTED` and the
summary lists what was never attempted with the command to measure it;
and `make-seed.py --quick --tag dlc-open` produces a locks-off seed
where every level must actually be forced.

### The bisect, one variable at a time

| seed | session | locks | abilities | result |
|---|---|---|---|---|
| `out-dlc-open` | fresh each | off | all | 8/8 beaten |
| `out-dlc-open` | ONE | off | all | 8/8 beaten |
| `out-dlc-held` | fresh each | ON | held from connect | 7 beaten, 1 correctly gated |
| `out-dlc` | fresh each | ON | granted MID-LEVEL | 8/8 beaten |
| the gate | ONE | ON | arrive mid-run | 5 could not be forced |

So it is not the levels, not session reuse, not ability locks being on,
and not the lock-to-unlock transition. Every gated level dimmed
correctly, went to zero dimmed on the grant, and then solved - one of
them through a cat trap that was refunded properly.

**A probe bug nearly produced a false finding here.** `parse_dimmed`
read the number AFTER `" of "` in `4 of 13 object(s) dimmed`, which is
the TOTAL, so every reading was the object count and could never
change. Three levels were about to be reported as failing to unlock on
the strength of "48 -> 48". Caught by checking the format against a
real log line. The parser now has a self-test over verbatim lines that
runs before the probe touches the game, and the single two-second
sample became a 25-second poll, because one read cannot tell a slow
unlock from a broken one.

### One of the five explained: DLC1 Clock Cupboard

The gate runs TWO game launches, and the first - the arrow check -
calls `solve_level`. On this run it chose Clock Cupboard (it skipped
Pizza, logging "needs abilities this session lacks"), so that level was
already solved before the run started, carried in the `.apsave`. The
main run's first visit reported "solved, but the mod banked no Beaten
token" and its second spent a Skip.

Nothing in the mod is broken there. `solve_level` finds every
controller already solved, no completion fires, and it reports
EXHAUSTED - which `play()` reads as "cannot be force-solved". That is
also exactly what the game says about Pizza: `solve: Distributables was
already solved as far as the level is concerned, so its win check had
nothing to do`.

**Every probe wipes the save**, which is precisely why none of them can
see this. `probe-slots.py --no-wipe` now keeps it, so a second run
meets levels the first one solved. Measured: beating a level and then
meeting it again with the save intact still gives `beaten`, so
carried-over progress is NOT what breaks solving.

### The bug that actually cost two puzzles: `restored = set(range(n))`

The mod logs `run state: ... 1 puzzle(s) beaten` at connect - a COUNT,
not a list. `play()` turned that into `set(range(n))`, which means "the
first n slot indices". The arrow session had beaten slot 2 and n was 1,
so the harness marked slot 0 as restored and spent the rest of the run
demanding a second Beaten token from slot 2. The mod will never send
one - `Report` skips a location the ledger already has, which is
correct - so slot 2 was revisited in rounds 2, 6, 10 and 19, parked
twice, and finally bought a Skip. The run reported **5 of 8 having
really done 6**.

Proof, from the gate's own game log: exactly five `beaten:` lines
(Books Stacked, Pizza, Daggers, Game Pieces, Filing Cabinet) and Clock
Cupboard absent, with `state: ... found=1` on its very first boot.

**WHY THIS ONLY EVER BIT THE DLC GATE.** The arrow check picks the
first slot it can actually solve. On the base game that is slot 0, so
`set(range(1))` is `{0}` and the wrong reasoning gives the right answer
every time - 25/25, for as long as the code has existed. Under DLC slot
0 is ability-gated, so the arrow check reports "slot 0 DLC2 Pizza needs
abilities this session lacks" and uses slot 2 instead; `{0}` is then
simply the wrong slot. Measured on both scenarios 2026-09-21:

    base: the arrow opened slot 1, started on slot 0   -> {0} correct
    dlc : arrow check using slot 2, slot 0 gated       -> {0} wrong

A latent bug that a passing test suite had been hiding behind a
coincidence, which is why "base passes and DLC never has" looked like a
DLC problem for two days.

**Fixed.** `play(log, plan, earlier)` now reads WHICH levels the
earlier session beat from that session's own transcript, matching
`beaten: <level> - Beaten` against the slot-to-location map, and warns
when the mod's count disagrees with what the transcript names.
Scheduler suite 56 tests, mutations 30/30.

### Still open: Bells and Bathroom Cupboard

After `Containers` arrived at round 14 (a Skip on Filing Cabinet
granted Solutions 1-3, so the item was NOT stranded), both levels were
attemptable and still reported "cannot be force-solved". Ruled out:

- not cat traps - the run logged **zero**;
- not carried progress - both booted with `found=0 solved=False`;
- not the level - `probe-unlock` beat both, `probe-session` beat Bells,
  and the locks-off probes beat both.

So a level that solves in four separate probes would not solve at round
15 of the gate. That is the remaining thread and it needs its own
single-variable probe, not another gate run. The most likely untested
difference is what a spent SKIP leaves behind: by round 15 five cards
had been skipped, and a skip grants every location without the level
being played.

### Three base gate runs, and what they cost

    23/25  restored fix + surprise-Skip refusal
    18/25  ... and refusing a Skip on `gated` too      -> REVERTED
    20/25  ... and filtering the stall path on gating  -> REVERTED

Both reverted changes read well and both made the run worse, and I
found that out by running the gate rather than by testing. THE UNIT
SUITE WAS GREEN FOR ALL THREE - 61 tests, then 65, mutations 34/34 then
37/37 - because the tests I had written asserted the SHAPE of the
change (`"refuse = surprise" in source`) and not its effect on a run.
A source assertion cannot tell you the run stalls.

Why each was wrong, both visible without a game:

- **Refusing on `gated` at the spend site.** `gated` is
  `any("waiting on ")`, true for a level that is only PARTLY gated.
  By then the level is open, and refusing leaves it unfinishable.
- **Filtering the stall path on gating.** The stall Skip exists FOR
  beaten slots with uncollected locations, and those are uncollected
  precisely because they are ability-gated. Filtering them leaves
  almost no candidate, so the run never finishes - it lost the
  credits, the goal and the server's goal, which had been passing.

**The process fix, which is the part that matters.** Both models -
`simulate()` in test_scheduler and `play_on_paper` in test_run_model -
defined `beaten` as "every location collected". The harness defines it
as the Beaten TOKEN. That difference meant neither model could ever
produce a beaten-but-incomplete slot, which is the only state the
stall Skip exists for, so the stall path was unreachable in both and
neither change could be measured offline.

Both now use the token. Verified by re-applying each regression:

    stall path disabled   -> 4 failures (was: green)
    refuse on gated       -> 2 failures (was: green)

Kept as guards in `TestTwoSkipRefusalsThatWereTriedAndReverted` plus
two mutations, so neither can come back on a green suite.

### The gate's own detection was lying: `gated` was not level-scoped

The last failure standing was "no Skip covered for a level the mod was
still gating", and it was a FALSE POSITIVE.

At the moment of the Skip the game reported

    locks: Stamps (Randomized) 1 controller(s), 0 of 9 object(s) dimmed
    abilities: 0 locked, 1 open, 9 objects

Stamps was not gated. But `gated` was
`any("waiting on " in line for line in (chunk + tail))`, and that
phrase comes from AbilityLocks' once-a-second summary of whatever level
is ACTIVE. Two summaries reading 18 and 24 objects landed in the same
window - Stamps has nine - so a level loading behind the one being
solved tripped the assertion.

A three-agent audit had already checked this and REFUTED it, correctly:
`chunk` and `tail` are rebound every round before `gated` is read, and
`Log.new()` is a destructive incremental read, so nothing carries
across ROUNDS. Neither the audit nor I checked contamination WITHIN a
round from a level loading in the background.

`solve_level` already knew the answer - `locked_controllers` asks the
game about the loaded level and returns indexes - it simply never
published it on the exhausted path. It now emits
`harness: locked controllers: <indexes|none>` and `gated` reads that.

### The last-resort Skip

Round 5 of the base run: one slot beaten, every other slot
ability-gated, and the missing ability on the beaten slot's own
uncollected locations. A closed loop only a Skip opens. `choose_slot`
now names that case (`LAST_RESORT`), prefers an unlocked candidate
whenever one exists, and the verdict reports a last-resort Skip while
still failing any gated Skip the harness could have avoided.

### tools/predict_gate.py - the thing that was missing all day

The run is deterministic: fixed seed, abilities and Skips from the
seed, a pure scheduler, and `--steady` for the cat traps. So the gate's
verdict is computable. This evaluates 13 of the 25 assertions from the
simulated run, and EVERY failure across the day's gate runs (19/25,
23/25, 18/25, 20/25, 24/25) was one of those thirteen. None needed the
game to be predicted; all were found by spending wall clock.

Run it before the gate, every time. A dirty prediction means fix that
first. A clean prediction the gate contradicts is a bug in the model,
which every fast test depends on - that is exactly how the `gated`
scoping bug was finally located.

### Phase 5 done properly: all 25 assertions measured on their own

The gate was run SEVEN times on 2026-09-21 before this table existed,
which is the thing the plan was written to prevent. Nine of the
twenty-five had never been exercised outside a full run; three had
probes sitting unused and five had no tool at all.

| gate assertion | measured by | result |
|---|---|---|
| connected / 8 puzzles / pack count / opening / track opens | `test_harness_data.py`, `predict_gate.py` | pass |
| every DLC level solvable | `probe-slots`, `probe-session`, `probe-unlock` (4 configs) | 8/8 |
| no solve threw; checks reached the server | every probe | pass |
| a Skip pays out on a beaten card | `probe-skip-beaten` | PASS, current build |
| a Skip is refused, not burnt, on a finished card | `probe-skip-beaten` | PASS |
| Skip targeting; the gated-Skip rule | `test_scheduler.py` (87 tests, 48/48 mutations) | pass |
| next-level arrow; pause-menu Exit (+8 nav claims) | `probe-dlc-nav` | 10/10 |
| error census; controller table; campaign untouched; no daily; no DLC in save | `probe-isolation` (NEW) | see below |
| the mod reported the goal | `probe-offline-goal` | pending |
| two game launches, no more | `test_scheduler.py`, structural | pass |
| every feature patched in | `check-patches` | pass |

`tools/probe-isolation.py` is new and exists because five assertions
were being taken on faith: they are all measurements over a SESSION,
not over a run, so a ninety-second probe answers what a fifteen-minute
gate was being used for.

The structural one is worth naming: "two game launches, no more" needs
no game at all. There is exactly ONE `subprocess.Popen([EXE])` in the
harness, so the claim reduces to how many times `launch_and_connect` is
called - twice, by `check_arrow` and by `play` - plus the guard against
Steam relaunching. Three unit tests, no launch.

## Verified

- Base-game gate 25/25, 8 puzzles beaten (2026-09-20).
- Core 320/320. Apworld 149/149. Scheduler 45/45, mutations 22/22.
  Apworld mutations 13/13. DATA 11/11 on both seeds. Run model 6/6.
- check-patches, check-docs, check-version, check-devtools.

## Open

- **DLC gate has never passed.** Best 23/25. DATA and CHOICE are now
  ruled out by fast tests; DRIVE and the mod are what remain.
- **DLC2 Math Set** - the ninth toothless gate, untestable by hand: its
  Indexables group is already solved at load.
- **34 conditional bypasses** in `gate-sharing.md` need an OR-capable
  requirement model; "Ordering or Stacking" cannot be a flat AND-list.
- **The dimmer's invisible gate** - on Bathroom Drawer a locked drawer is
  not greyed out, because its objects are shared with a baseline group
  and unlocked-wins frees them. Combs shows the same gate correctly.
- The 14 container excludes stay, hand-tested by droha. The gate should
  print them so its report never reads as full DLC1 coverage.
- `DlcState.Reset()` is unreachable dead code.
