# Changelog

Every release ships three files that carry the same version number and are
meant to be used together: the mod zip, `alttl.apworld`, and the player yaml.
A mod and an apworld that disagree about the version disagree about the item
table, and nothing detects that at runtime - so the version is checked by
`tools/check-version.py`, in CI, and again before a release will build.

The format is loosely [Keep a Changelog](https://keepachangelog.com/).

## Unreleased

An audit droha asked for after the apworld manifest bug: "can you do a full
audit that we're not missing/half implementing other things like this?" The
bug had a shape - half of an external contract, checks that read the source
while the artifact shipped broken, a failure that only logged, and a declared
guarantee that silently did nothing - and the audit looked for all four.

### A run finished offline never reported its goal

The worst thing it found, and a regression from 0.3.2's own credits change.
Reporting the goal requires the credits to have been PLAYED, and that flag
lived only in GoalLatch - which is replaced wholesale on every reconnect and
every offline start. Finish the run offline, play the credits, reconnect: the
flag was gone, the goal was never sent, and the multiworld waited forever on a
slot that had genuinely finished. A relaunch between the two did the same.
Nothing logged it.

The flag is persisted in the run's sidecar file now. The latch also stopped
claiming it "closes on the acknowledgement" - the client library has no async
or callback form of SetGoalAchieved, so a send that left is all anyone knows.
What makes that safe is the persistence: each session re-owes the goal and
re-sends until one lands, and the server takes a repeat as idempotent.

Proved in a real game by `tools/probe-offline-goal.py`, which finishes a run,
kills the server, plays the credits offline, brings the server back and
watches the goal arrive.

### The mod refuses a seed built by a different apworld

`slot_data` carries `world_version` and the mod compares it with its own
assembly version at connect. Location ids move between releases, so a 0.3.1
mod on a 0.3.2 seed sent the wrong checks under the right names and said
nothing. check-version.py enforced the pact inside the repo, between commits,
and never between two installs.

Refused rather than warned: everything past that point builds a run on the
payload, and checks sent into other people's worlds cannot be walked back. A
seed too old to say which version made it is an unknown, not a mismatch, and
is allowed with a warning.

### The packaged apworld carries its container keys

The bug that started the audit. Archipelago's spec defines two manifests with
opposite rules - the source must not declare `version`/`compatible_version`,
the packaged archive must - and the packager copied the source verbatim, so
the artifact had neither. Every release since 0.3.0 shipped it.

It was not only a future problem. The failed parse left `minimum_ap_version`
unpopulated, and the loader's check is `if apworld.minimum_ap_version and ...`
- so the minimum version we declared was never enforced at all.

### Diagnostics that logged and did nothing now act

- the controller audit returned silently on a level with no controller_groups
  entry - the one case where NO group check can ever be collected. A level
  merely missing a few groups got the loud warning.
- `SlotData.Problems()` is "whether the payload is coherent enough to start a
  run on", and the offline start and cache write both refuse on it. The live
  connect path logged a warning and started anyway. It refuses now.
- `pack_size` defaulted to 4 against an option default of 5, and
  `cat_trap_chance` to 10 against 25, contradicting the invariant SlotData
  states about itself. The test that should have caught it was asserting the
  drifted values, so `tools/check-slot-defaults.py` reads both sides in CI.

### The release tooling checks the artifacts

`tools/check-release-assets.py` opens the three files a player downloads:
their four versions must agree, the apworld manifest must be readable, and the
yaml must parse and keep a `{number}` placeholder. `--expect` catches a stale
folder, which "they agree with each other" cannot.

The release gate runs it before installing. It defaulted to `release-test/`,
which holds the PREVIOUS release between releases, and green-lit 0.3.1's
artifacts twice during 0.3.2.

CI now generates from our own shipped yaml rather than Archipelago's template,
using two copies so the name placeholder is exercised, and asserts the
packaged world loads with no errors logged. `.apignore` makes Archipelago's
own packager produce the same archive ours does.

### Docs that contradicted the code

The game page said packs widen as you go (removed in 0.3.2), that the
multiworld holds a Hint Page for every page (the default is 50%, and the same
file said so seven lines earlier), and that the credits card needs only the
count. `docs/release-testing.md` gave a "features live" line missing `daily
guard`, so the string it told you to look for could never match.

## 0.3.2 - 2026-09-14

droha's 79-puzzle multiworld, and the playtest that followed it. **Location
ids move**, so a seed generated before this build will not match a mod built
after it.

### The fridge advertised work the puzzle never wanted

`Fridge (Something Eggstra)` had four locations and droha could only ever earn
two, leaving the card half red for good. It has one now, the Solution.

The level is an egg hunt and it ENDS when the carton is filled. Its other two
controllers - the shelf objects and the tupperware - are arrangements the game
does not ask for, so nothing ever solved them.

Two independent measurements, because the first thing I concluded here was
wrong. `tools/probe-dead-controllers.py` (new) force-solves every controller
and all three fire, so they are not dead in the sense TupperwareTower's
mechanism controllers were. Then `--only EggsContainable` solves the eggs and
NOTHING else, and the game raises `LevelComplete` regardless. droha's room log
agrees from the other side: `Solution 1` and `Eggs Containable` fired together
at 03:31 while holding both Containers and Stacking, and the other two were
never checked in the whole run.

**Stacking is deliberately NOT kept as an `extraAbility`** - contrast
TupperwareTower, where `Grids` is. The tupperware is dimmed without Stacking,
but the level finishes without touching it, so requiring it would hold this
level's only check behind an item the player never needs. The cross-check test
grew an `OptionalAbilities` list for exactly this shape, and its docstring
demands a measurement to join.

435 locations to 432.

### Packs are all the same size

droha: "the packs should all be the same size, the 4 minimum open just means
they have something to do in 4 levels at the start."

The free opening was pinned at `MIN_OPENING` while the packs widened past it,
so a default run opened 4 and then handed out 6 at a time. The floor applies
to the pack SIZE now and the opening is simply the first block, so every block
matches bar the remainder.

- `puzzle_count` 79 to **70**, `pack_size` 4 to **5**, `MIN_OPENING` 4 to 5.
  `levels_to_beat` stays 40. That is 70 + 13 dividers + credits = **84 cards**,
  one under the widest strip the game draws.
- `items.opening_size()` is now the single source of truth. `pool.py` and
  `slots.py` each had their own `max(pack_size, MIN_OPENING)`, which is wrong
  on any run long enough for the cap to widen the packs - they would have
  believed a 79-puzzle run opens 5 when it opens 6.

**Also fixed, and pre-existing:** the ability-granting loop is greedy one at a
time, so it stalled where an opening puzzle needs TWO abilities - neither pays
alone, so both looked worthless. It left openings a check short in 2 of 165
stress configurations. It looks one further now when a single grant stops
helping. Measured 2 before, **0 after**.

### Toasts stopped throwing messages away

droha: "I don't always see it showing my items being released/received."

Beyond five on screen the OLDEST was destroyed, often before it had rendered a
single frame - on screen, indistinguishable from never being raised. Finishing
a level can push six or more at once and a Skip reports every remaining
location on the slot.

Ten now, and overflow WAITS instead of dying; the hold drops from 8s to 2.5s
while a backlog exists so a burst clears rather than trickling. The eviction
loop is now a tripwire that warns if a future caller reaches `AddLine` without
checking for room.

Measured on MedicineCabinet's 13 parts: **5 toasts waited for space, 0 lost.**

Toasts already appeared bottom-left with the newest at the bottom and the
stack growing upward, so nothing moved.

### The level select strip fits the screen

79 puzzles builds 92 cards against a strip the game sizes for about 85, so it
ran off the edge. It is scaled to fit now, from a cached baseline so the
one-second poll cannot shrink it cumulatively.

The measurement that matters: the strip's own parent is content-sized and grew
with the dots - 2025 wide against a span of 2026 - so comparing against it
found no overflow and the first version of this silently did nothing. The room
is the NARROWEST ancestor, which is the real viewport at 1920. Measured: 92
dots span 2026 in 1920, scaled to 0.95, stable across repeated visits.

### The connection pane's mouse no longer sticks

droha: click once and the whole row highlights, then "moving the mouse around
highlights different things, like I'm dragging it", and a second click does
not put the caret where you clicked.

The scene's EventSystem carries the GAME's `RewiredStandaloneInputModule`, and
that module reads mouse buttons from Rewired's own `IMouseInputSource` rather
than `UnityEngine.Input`. `TypingGuard` switched off EVERY Rewired map while a
text box had focus, which blinded the module to the mouse RELEASE: its state
stayed pressed, so mouse movement kept sending drag events to the field and no
fresh press ever arrived.

The comment asserting "the mouse still works - it is Unity's pointer handling
that clicks the buttons, not Rewired's maps" was the load-bearing assumption
and it was false. Only the KEYBOARD's maps are switched off now, which is
where the cursor-drift bindings live.

**The first build of this fix did not work, and the log said it had.** The
lookup asked for `SetMapsEnabled(bool, ControllerType)`, which the map helper
does not have - its two-argument overloads take a category, and the
per-controller-type switch is `SetAllMapsEnabled(bool, ControllerType)`. So
the narrow bind failed, the code fell through to `SetAllMapsEnabled(bool)`,
and it went on disabling everything while printing "1 of them keyboard-only".
A whole round of testing was spent on the wrong conclusion. The log now prints
the bound SIGNATURE, because a label cannot be checked. Confirmed in play:
"the clicking and typing are working".

### The keyboard stopped driving the game from other windows

droha: "why is my keyboard still controlling the mouse in game while I'm not
focused on it? It often opens up the settings page and sometimes changes
settings."

`Application.isFocused` reported true with Notepad plainly in front - the
focus-change log never fired once, which can only happen if the value never
moved. Focus is asked of Windows now, via `GetForegroundWindow` and the owning
process id. It fails OPEN: if the call cannot be made it reports focused,
because suppressing input on a game that IS in front is worse than the bug.

### The cat trap no longer freezes the game

droha hit this three times, and the first report had no log left to read. It
is not a freeze - it is an exception storm. `Player.log` held **6,583**
identical `NullReferenceException`s in
`DragObject+<>c__DisplayClass94_0.<ObjectPlaced>b__1`, thrown from
`LeanTween.update`.

Dropping a piece starts a LeanTween settle tween whose callback closes over
the piece. Resetting the level destroys the piece while the tween is still
registered, so LeanTween calls a dead reference every frame, forever. The game
never hits it because nothing in the game resets a level mid-animation.

**Three fixes missed before this one, and each failed differently.**
`cancelAll()` stopped the exceptions and hung the load instead - the level
load is an async state machine that awaits its own tweens on `Main Camera` and
`Completion Stars`, so killing those meant `SetActiveLevel` never returned.
Cancelling the level's own objects left 4,638 exceptions; adding every
`DragObject` left 2,139. Dumping `LeanTween.tweens` ended the guessing:
`LeanTween.value` has no GameObject, so the tween is parked on an internal
`~LeanTween` holder and none of the three sweeps could ever have reached it.

`CancelDetached` takes those and leaves the load's own tweens alone. Verified
in play rather than by reasoning: three traps caught a live detached tween
with **zero** exceptions, alongside 42 clean resets.

Two smaller defects in the same moment. A trap landing just as a puzzle
finished relaunched the level that was on its way out and threw the queued
navigation away, so the run sat on a reset copy of the puzzle it had just
solved - that counts as a miss now. And the reset restored every object's own
colour, including ones an ability lock had dimmed, so locked pieces sat fully
lit until the once-a-second pass came round; `HoldDim` re-dims every frame for
half a second, which covers the rebuild.

### The cat trap holds itself while a level is loading

A second freeze, same trap, different cause - and this one was reported from
the released 0.3.1 build, which has none of the guards above.

The log ends like this:

    track: slot 53 Stamps (Randomized) launching with seed 307681145, forceReload
    trap: 1 cat(s) reset the puzzle
    <nothing>

`ActiveLevelInterface.Level` is already set partway through an async level
load, so a trap ticking in that window finds what looks like a perfectly good
puzzle and calls `ResetLevel` on it. The load is told to rebuild the level it
is still building. `SetActiveLevel` never returns, and the game stops - no
exception, no error, nothing to read. droha: "when finishing a level I got a
background change trap... it reset and when I clicked anywhere the game fully
froze. Had to alt+F4."

**Not the exception storm above.** That one drowned in 6,583
`NullReferenceException`s; all 1,549 lines of this log hold exactly one
exception, and it is a benign startup timeout. Both needed finding
separately.

A trap now asks the level what it is doing - `LevelIsLoaded` and
`IsTransitioning` - and **holds** rather than spending itself while a load is
in flight, springing on the first tick after the level settles. The completion
grace already in 0.3.2 does not cover this and could not: it measures time
since the last completion, and a level reloading for any other reason is not
one.

**Measured, with the guard deliberately removed to prove it matters.** The
freeze is a race that needs a click and resisted 29 direct attempts, so the
thing measured instead is the state it comes out of: two levels alive at once,
counted on every attempt. droha, watching an unguarded build: "oh god 2 levels
loaded at once", with a screenshot of one puzzle drawn through another.

Ten attempts per build, each finishing a real puzzle, with the trap delivered
by the server the moment the level is reported beaten and live settle tweens
held open across the window:

| Build | Clean | Two levels alive |
|---|---|---|
| guard off, grace off, `CancelAnimations` off - 0.3.1's `Spring` | 1 | **9** |
| the real build | 5 | 5 |
| the real build, no trap sent at all | 5 | 5 |

The unguarded build breaks on its FIRST trap and never recovers. The guarded
build's failures start after the harness abandons a half-solved
MedicineCabinet, and the third row is the control that proves it: with no trap
sent, the same build breaks in the same place the same way. On the real build
the trap never once reset a level inside a navigation - every attempt logged
`found nothing to knock over` rather than `reset the puzzle`.

**The hard freeze is still not reproduced.** The game kept running through all
of it, including four real clicks into the wreckage. Two live levels is a
state consistent with droha's hang, not a demonstration of it.
`docs/data/trap-freeze-repro.md` has the runs, the three wrong versions of the
measurement, and what is still untried.

Also measured: a trap arriving mid-load is now held and springs once the level
settles, where before it hit `level == null` and was silently spent - the
guard turns a lost trap into a fired one - and it cannot strand a trap on the
level select, because `ActiveLevelInterface` is null there.

### The background trap stopped strobing

Same report, same moment. "The background started kind of strobing/shifting
between multiple colors."

`Backgrounds.Tick` polls every frame and writes the camera back to the trap's
colour whenever it differs. That poll exists because the one-shot write kept
losing - the level's own setup runs after `StartLevel` returns and paints the
camera from its own colour - and outlasting the game is the right answer for
a settled level.

It is the wrong answer during a transition, which ANIMATES the backdrop and so
writes a new colour every frame. Two writers, sixty times a second, and the
player sees the flicker.

The poll now stands down while the level is loading or transitioning and picks
up the instant it settles, which is the only moment it was ever needed.

Measured afterwards, because "it writes every frame" deserved a number rather
than an argument: with a Background Change Trap held, ten seconds settled in a
puzzle is **zero** camera writes, a level load is zero, and a whole two-minute
session is **one**. The per-frame cost is a Color comparison; the write
happens about once per level, which is what it was always for.

### The vanilla title menu no longer flashes past

The Archipelago title appeared, then the original menu, then the Archipelago
one again. The menu was following the CONNECTION - hidden at startup, restored
when the first attempt failed, hidden again when the retry succeeded.

droha asked the question that settles it: when would we ever use the normal
menu with this mod installed? Only when you are not playing a multiworld,
which is exactly when there is no slot name. That is the whole condition now.

### Settings are shared between the campaign and a run

A run redirects every save write, `playerPrefs` included, so settings changed
while playing a multiworld landed in `save_ap_<slot>_<seed>.json` and the
campaign never saw them. droha: "the user settings should persist between
save1 and the Archipelago. They should be synced." They sync both ways now,
and only the `playerPrefs` key moves.

### The window opens at the size you chose

droha: "why is my game always opening in full screen mode? I set it to
windowed every time", and later "it's not remembering my selection".

It was never fullscreen. The game stores the display choice as
`Prefs.resolution`, an INDEX into a list `SettingsMenu` rebuilds from whichever
monitor it opened on, sorted largest first - so index 0 is that monitor's
native resolution, on any monitor. That index is applied at startup and
overrides Unity's own stored size: measured, with the registry holding
1280x720 and "use native" off, the game still opened 3840x2160. A windowed
window the size of the monitor is indistinguishable from fullscreen.

droha had already worked out why an index is the wrong thing to store: "the
game changes the resolution list depending on what monitor opened it, so a
number doesn't help me here." A save here held **35 against a list of 27** -
out of range, from a different display - and the game fell back to native.

`DisplayGuard` remembers the SIZE instead, in the mod's own config, and looks
its index up fresh against whatever list the current display produced. It
never picks a size: it re-applies the last size the game was actually running
at, does nothing at all if that size is not offered by this display, and steps
aside if the size has already been changed since startup. droha: "it should
not override to this always, it's whatever the user sets it to - it should
just keep that setting."

Measured end to end: sabotage the save back to index 0, launch, and the window
opens 3840x2160 and is corrected to 1280x720 within nine seconds, with the
save repaired so the next launch needs no correction at all. Change it to
1600x900 and relaunch, and 1600x900 is what comes back.

**One bug in the first version of this, found in play and worth
recording.** The guard sampled the window size on its first tick and
treated any later change as the player choosing one - so it would not
override a size someone had just picked. It could not tell that apart
from the GAME applying its own stored resolution, which happens a few
seconds into the boot. So the game's late apply read as a player choice,
the guard adopted it, and a remembered 1280x720 became 3840x2160 and
stayed there. The log line that gave it away: "the size was changed to
3840x2160 since startup, so that is what gets remembered".

The check is gone. Nothing is recorded until the settle is over and the
game has finished having opinions; a player cannot reach the settings
and pick a size in the first few seconds, so there was nothing being
protected. Verified after: "put the window back to 1280x720 (index 19 on
this display), which the game had opened at 3840x2160".

**And a second bug from the same root, reported as the game opening
behind other windows.** droha: "the game always opens up in the
background for some reason?" Measured on a fresh launch - the game held
the foreground at eight seconds and had lost it by twelve, which is
exactly when the correction lands. Changing the resolution makes Unity
rebuild the window, and Windows hands the foreground to whatever was
behind it.

The correction should not have been running at all. With the registry
and the save BOTH already naming the right size, the game reaches it on
its own; the guard was sampling at its six-second settle, seeing a window
the game had not got round to resizing yet, and racing it. So it now asks
the save first: if the stored index already resolves to the remembered
size, the game is left to apply it and nothing is touched. Verified - no
correction logged at all on a launch that used to log one, and the window
still ends up the right size.

A `SetForegroundWindow` after a real correction covers the case where one
IS needed. That half is unverified: a process started by a background
script is denied the foreground by Windows, so the harness cannot
reproduce the game taking focus in the first place. Only a launch started
by hand can confirm it.

### The harness stopped rewriting the display settings

It used to force the game windowed at 1280x720 before every launch. Those
values are the player's. droha, after it happened again: "why does it re-write
those? It shouldn't. That's the whole thing I've been trying to tell you."

`force_windowed` is gone, replaced by a read-only `describe_display`, and
`self_test` greps its own source for a registry write and fails if one comes
back. Reading is not enough on its own, because the game rewrites those values
every time it exits, so the snapshot now captures them into `SCREEN.json` and
restore puts back the ones that moved.

### The keyboard guard puts maps back as they were, not all on

Found by reviewing what the mod changes against stock rather than by a
report. Suppress called `SetAllMapsEnabled(false, Keyboard)` and Restore
called `SetAllMapsEnabled(true, Keyboard)` - which is not a restore. It
turned every keyboard map ON, including any the game had deliberately off,
and enabling and disabling map CATEGORIES per context is Rewired's normal
idiom.

It barely mattered while this only ran with a text box in our own dialog
focused. Suppression now also covers the game being alt-tabbed away from, so
it runs during ordinary play for as long as the player is in another window,
and a wrong restore stopped being a corner case.

`ControllerMap.enabled` survives Rewired's obfuscation in this build, so each
map's state is now recorded before it is touched and written back afterwards.
The blanket call remains for `Mode.All`, which is a deliberate bisect setting,
and as the fallback when no typed handle is available - which says so in the
log rather than doing it quietly.

Measured: two focus round-trips, "1 keyboard map(s) held individually, 0 of
them already off", zero exceptions. The count line is permanent, so any
context where the game DOES hold a keyboard map off will show up in the log.

### The campaign save is written atomically, and only where it should be

Also from the review, and the worse of the two. The settings mirror wrote
straight over `save1.json` with `File.WriteAllText`, so a crash or a power cut
partway through took the player's real progress with it. The inversion is the
tell: `RunState` and `SlotCache` - the mod's own scratch files - were already
writing temp-then-move, and the one write that touched something
irreplaceable was the one that was not.

It now writes a `.aptmp`, reads it back and checks it decodes to exactly what
went out, and only then moves it into place. A failure discards the temp file
and leaves the original untouched.

The same write also round-tripped the WHOLE document through Newtonsoft to
move one key, which puts every other value through a parse and a re-serialise.
`SettingsSplice` (new, in Core, fourteen tests) finds the span of the settings
object and replaces just that, so every other byte is copied through
unexamined. It refuses rather than guesses - a document where the key is
missing, duplicated, not an object, or unterminated returns null and the
caller falls back to the old rewrite, saying so.

Verified against the real save: only `resolution` changed, all fourteen
progress keys identical, and the 2340 bytes before the settings object byte
for byte the same. To be accurate about the severity, the drift was a latent
risk rather than observed damage - a full re-serialise of this save's current
shape happens to come out identical. The crash window was the real defect.

### The yaml option comments say what the options do

droha read through `player.yaml` during the playtest and four comments
did not survive the reading. None of this changes behaviour - the option
values are untouched - but a comment that misleads is worse than no
comment, and `test_player_yaml.py` skips comment lines, so nothing was
ever going to catch these.

- **`mechanic_coverage`** - "what is this? Should it be defaulted to 6 so
  it's as random as possible?" It is a RESERVE, so higher is *less*
  random. The comment now carries a measured table: 4 pulls in all four
  jigsaw puzzles the game has on every seed, 5 adds all five drawer ones,
  and **6 is identical to 5** because there is nothing left to reserve. 6
  is the least random setting available and buys nothing over 5.
- **`pack_size`** - "why does the comment say 1 to 10 when we want a
  minimum 4?" Because 1 to 10 is the option's range and the floor is
  applied afterwards. The comment now says so, and also admits the second
  adjustment it never mentioned: a run carries at most fourteen packs, so
  at `puzzle_count: 79` every pack is 6 whether you asked for 1 or 5. At
  the default 70 you get the 5 you asked for.
- **`archive_packs`** - "the comments should say the options." All six
  keys are now listed with their in-game names and puzzle counts. The
  jigsaw warning was vague ("turning enough of them off") and is now
  exact: jigsaws exist only in Good Tidings, Trick or Tidy, Merry Mess
  and Drawer Chores.
- **`generator_weight`** - "kind of a bad name, as it's generated random
  levels." Renaming moves an option key and breaks existing yamls, so the
  comment does the work: it says the weights are relative rather than
  percentages, and that "generator" is about where a puzzle comes from.

Left alone deliberately: `archive_packs` stays a block sequence rather
than the flow style droha suggested. `test_player_yaml.py`'s parser only
understands block sequences, so flow style would read as a string and
fail the defaults test - it needs a real YAML parse first, which is a
bigger change than a comment pass.

### Level endings: measured, and not ours

droha: some levels finish on the three-button panel - restart, pause menu,
next arrow - and others drop you straight into the next puzzle. "Why is
that? We might want to make that the same across all levels."

**It is the base game, and the mod is not involved.** Measured rather
than reasoned about, because the three previous guesses at this kind of
question were all wrong. A new DevTools `endings` command reads
`LevelManager.m_allLevelInterfaces` in one frame at the title screen -
every level's authored flags at once, with nothing loaded - and the
result is in `docs/data/level-endings.tsv`.

Of the 111 levels in the run pool, **92 show the panel and 19 do not**,
and the 19 are all sixteen generator levels plus Tupperware Nesting,
Tupperware Tower and Radial Dance Party. `generator_weight` defaults to
80, so most of a default run is generators - which is exactly why it
reads as inconsistent in play, and why the minority that DO show a panel
feel like the odd ones.

Two theories died here. The daily pool is not involved: `isDailyTidy`
reads false for every level in the table, and `DailyGuard`'s rescue -
the one mod path that genuinely skips a panel - leaves a log line that
never appeared. And the fix sketched before the measurement would not
have worked: generators already have `PreventRetryMenu` false, so
forcing that flag changes nothing. Making it uniform would mean patching
the `ShowRetryMenu` getter in either direction.

droha's call, with the numbers in hand: leave it alone. Making 92 levels
stop showing a screen the game wants to show, or making 19 show one they
were never built for, is a bigger change than the inconsistency costs.
The table stays as the record.

### The credits can be gated on STARS instead of completions

A new `goal` option. `beat_levels` is the default and unchanged;
`star_levels` counts a puzzle only when every check on it is done - every
solution and every part - which is the same star the level select already
draws on a card with nothing left to do. It has its own count,
`levels_to_star`, defaulting to 20, because starring is a great deal more
work than beating and the number that makes a good run is a different
number.

**It needed no new logic, and that is the interesting part.** The obvious
implementation is a second event item and a second event location per
level, which would shift every location id again. It is not necessary:
the Beaten event's requirement is already the STRICTEST on the slot - it
asks for the union of the level's abilities, every solution location asks
for the same union, and every part asks for a subset (pinned by
`test_tables.NoPartNeedsMoreThanItsLevel`). All locations on a slot share
one `packs` value. So a state that can reach N Beaten events can reach
every location on those N slots, and "N starred" is provably achievable
exactly when "N beaten" is. The completion condition already in place
proves the star goal too.

The difference between the goals is entirely how much work the PLAYER
does, not what the generator must prove. `test_generation.TestStarGoal`
asserts no Starred location is ever minted, so if a future change decides
it needs one, that is a deliberate decision with ids moving rather than a
surprise.

On the mod side the star predicate already existed as a private helper in
`Track`, doing exactly what the card's star does. It moved into
`CheckRouter` as `StarredCount` / `HasWorkLeft` so the goal and the level
select cannot drift apart about which puzzles are finished. Three places
read `LevelsToBeat` directly - the credits gate, the beaten toast, the
offline summary - and all three now go through one `Checks.GoalProgress`,
because adding a second goal to three call sites is how two of them end
up telling the player a different number. The toast reads "Puzzle beaten
(12/20 starred)" on a star seed.

**A Skip stars the puzzle it clears**, because it fills in every check on
it. That is consistent with skips already counting toward beating, which
was a deliberate 0.3.1 decision, so it is kept rather than special-cased -
but it does mean `skip_count` shortcuts a star goal at full strength. The
fill-stress sweep gained a no-skip star configuration for exactly that
reason, so twenty free stars cannot hide a broken goal.

Verified: 106 apworld tests and 260 C# tests, the star goal in four
fill-stress configurations across the seed span, and a real seed
generated from the player template reporting "Goal: Star Levels, Puzzles
To Star: 20" with the Beaten events unchanged.

### The level select says how far along you are

droha asked for a "levels beaten / needed" counter. The number existed
only in a toast that scrolls away, so the one screen where you decide
what to play next never said how close you were.

It reads the GOAL rather than the beaten count - a counter that always
said "beaten" would be quietly measuring the wrong thing on half the
seeds. Green once the count is met.

**The star goal shows the star, not the word.** droha: "for star goal we
should have 0/50 [star icon]s instead of it saying stars or beaten." It
is the game's own `LTL-LevelSelect-Star-solved`, the same art a card
wears when it has nothing left on it, so the counter and the cards are
plainly talking about the same thing - and it sits directly above the
chapter header's own star, which uses the same shape. The beaten goal
keeps its word: there is no icon in the game for "finished any one way",
and inventing a glyph would be less clear than the word, not more.

Parented to the toast overlay and gated on the run's own track being on
screen, both copied from the connected tag directly above it, and for the
same reasons: the track scrolls and is rebuilt, the overlay is the mod's
own canvas with no layout to lose to. It is in `RepaintSoon` so it does
not arrive a second after the screen has settled - the regression already
written up in that method.

### Which mechanics you hold, at a glance

droha: "show ability locks on the level select - icons for the twelve
mechanics, so you can see at a glance which you hold." Until now the only
way to find out was to open a puzzle and see what was greyed out.

A row of tiles across the top left: a borrowed item picture with a
three-letter label under it - SWP, STK, ORD and so on. Held is full
colour, locked is a dim grey version of the same art, and **all of them
are always shown** so the strip never changes width and you can see what
is still to come rather than only what you have. Pills for mechanics this
seed does not carry are omitted, and nothing is drawn at all when ability
locks are off - in both cases the strip would otherwise describe a
restriction that is not in force.

**The icons are real game art, twelve of them, shipped with the mod.**
There is no per-mechanic art in the game - abilities are the mod's
invention - so the hunt went two ways, and both are in
`docs/data/ability-icons.md`.

First, **badge elements**: the small item pictures that sit on a badge,
rather than an assembled badge. Two new DevTools commands made that
searchable - `sprites <filter>` dumps every loaded sprite name (998 of
them) and `spritegrid:<names>` draws a batch on screen at full size AND
at icon size, because a name says nothing about how something reads at
twenty pixels. Three rounds of that threw out everything thin or low
contrast: a hammer, nails, callipers, keys, dice and scissors all
disappear when small.

Then, better, **objects out of the puzzles themselves**. Puzzle art is
not loaded at the title screen, so `loadlevel:<index>` opens any level
outright, `newsprites` reports what that brought in, and
`spriteexport:<names>|<dir>` writes them out as PNGs - through a
RenderTexture, because the game's textures are not readable, and cropped
to `textureRect` because they are atlased. The first attempt handed back
a bottle opener instead of stacked books: `Graphics.Blit` flips
vertically on D3D and the crop has to invert y.

Objects were taken from **generator puzzles first, then campaign**, and
only from puzzles that use that mechanic and **nothing else** - so the
picture and the lock mean the same thing. droha picked the twelve:

    Swapping    Books          Badge1-Books2
    Stacking    Cartridges     Badge2-NES
    Ordering    Pencils        Badge1-Pencils
    Gadgets     Lightbulb      badge3-lightbulb
    Rotating    Record         Badge1-Record
    Sticking    Stickers       Badge1-Stickers
    Grids       GridTile       1x1-1, from Procedural Grid Puzzle
    Tidying     Breadtag       Breadtag-red, from Breadtags
    Containers  EggCarton      Carton-front copy, from the Fridge
    Furniture   Drawer         Drawer-Top+Bottom, from Tool Drawer
    Symmetry    Wreath         Wreath, from the Good Tidings wreath
    Jigsaw      Gingerbread    GingerbreadMan - the solved cookie, seams
                               and all

**They ship as PNGs inside the DLL**, 129 KB for all twelve. That is not
tidiness, it is the only thing that works: the six puzzle objects are
Addressable assets, loaded when a level opens and released when it
closes, so they are not in memory on the level select. Measured - six of
twelve resolved there and the other six drew as lettered plates. The
alternative was to catch each sprite as it passed and hold a reference,
which meant the strip filled in gradually as a player happened to visit
the right puzzles. droha: "can we just save those as png and use them in
game? That way we don't have to do all this run around."

Three dead ends worth recording so nobody repeats them. The Calendar's
stickers, the shells and the dirty paw prints all export **blank** -
they are white masks the game tints at runtime, so there is no colour in
the sprite to take. The Microscope has no microscope: its pieces are
crystal rings and a transparent lens, because the instrument is scenery.
And `badge7-spider` is not the symmetry puzzle - that is the wreath.

The letters stay under each icon. The mapping is a metaphor, not a fact,
and nobody would guess all twelve cold.

Laid out from droha's read of it in game: the block sits in the gap
between the level select's close button and the chapter heading rather
than at the left edge, where it covered the X; the two rows have air
between them; and the art is fitted to its own proportions and pinned to
a common baseline instead of centred in a square. That last one is why
the egg carton looked wrong - it is five times wider than it is tall, so
a square box with preserveAspect floated it in the middle of its tile
with a gap underneath.

Verified in play at 1280x720: "loaded 12 ability icon(s)", the strip
built twelve tiles with three in full colour for the abilities held and
nine dimmed, the counter read
"0 / 40 beaten" beneath the connection tag and "0 / 50" with the star on
a star-goal run, both survived a track rebuild, both were correctly
absent inside a puzzle and on the pause menu, and the session logged
zero exceptions. The seed used was generated before the `goal` option existed,
so it also demonstrates the payload-without-a-goal default. The
locks-off case is covered by the code path rather than by a run.

### The Furniture ability is now called Drawer

droha: "rename the ability to Drawer instead of Furniture - it's what we
were calling it before I knew the ability name." The item a player
receives should say the thing it opens, and every puzzle behind it is a
drawer or a cupboard.

The name is authored once, in `data/abilities.json`, which BOTH the
apworld and the C# mod read and which `AbilityCatalogTests` pins against
each other - so the rename is that one key and everything else follows.
Ability item ids are positional over that file's key order, so renaming
in place keeps the id and changes only the name.

**A seed generated before this carries the old name.** The mod only draws
a pill for an ability the seed's own catalogue contains, so an old save
shows eleven of twelve with Drawer missing - correct behaviour, not a
bug, and another reason 0.3.2 needs a fresh seed. Location ids had
already moved.

### The level select stopped saying "Chapter N"

The game writes that subtitle from the section index and only has names
for the five chapters it shipped with. A run has as many sections as it
has packs - fifteen at the default - so past the fifth the line had
nothing to say and read differently from every section before it. droha:
"the chapters at 5 don't have a name... just remove the chapter x, and
just have the - for all of them."

Blanked rather than renumbered, because the run's own name for the
section is already on screen directly underneath - "Opening", "Pack 3",
"The End" - and a chapter number above a pack name is two different
countings of the same thing. What is left is the dash and the star
count, identical on every section.

Held blank EVERY FRAME, not on a poll. The first version checked twice a
second, and the game rewrites the subtitle as each section scrolls under
the header - so the old chapter name showed until the next tick. droha:
"I see chapter 1/2/3/4/5 show up when I scroll over the chapter
markers." Only the SEARCH is throttled now; the label is remembered and
looked for again only when the reference has gone.

Only while the run track is up: the archive and daily menus use the same
header, and their chapter names are theirs to keep.

Verified alongside it that the sections line up with the packs, which is
what the headings claim: `Opening` holds the five free puzzles from track
position 0, then each `Pack N` starts at its own divider and holds five -
5 + 13 x 6 = 83 cards for a 70-puzzle seed with thirteen packs.

### Every mechanic is in the run, unless you ask for fewer

droha: "is there a reason why we would have less than the full 12
abilities? We should default try to have them in all runs, and have the
options to have less if we want a simpler run."

Measured first: at defaults, all twelve already appeared in twenty out of
twenty seeds. The gap was **short runs**. The coverage reserve protected
only the four mechanics no generator can make - stacking, containers,
drawers, jigsaws - on the reasoning that the other eight arrive on their
own. True at full length, false when the run is short: at
`puzzle_count: 20`, Rotating was absent from four seeds in eight,
Symmetry three, Gadgets one, all at the default coverage.

The reserve now takes one of EVERY mechanic first, then the extra copies
of those four. Re-measured: `puzzle_count: 20` gets all twelve every
seed, and the default is unchanged because it was already complete.

`mechanic_coverage: 0` is still the simpler run - it turns the whole
reserve off including the new floor. The other ways to lose a mechanic
are all content choices rather than accidents: dropping every event pack
removes jigsaws from the game, generators-only removes all four hand-made
mechanics, and an 8-puzzle run cannot hold twelve mechanics when several
of them exist only on single-mechanic puzzles.

Pinned by `test_every_mechanic_the_content_can_supply_is_in_the_run`,
which asserts against what the ENABLED content could supply rather than
against all twelve - with three exemptions it states outright: coverage
zero, runs under twenty puzzles, and ability locks off.

**A correction to something recorded earlier in this file.** The yaml
notes said dropping `drawer_chores` would gut the Drawer mechanic because
three of its five puzzles live in that pack. Measured, it does not - the
coverage reserve pulls in Workbench and Medicine Cabinet instead, and all
twelve still appear. That claim was reasoning, not measurement.

**And a latent test bug this exposed.**
`test_the_opening_holds_every_solvable_puzzle_it_can` computed the
opening as `max(pack_size, MIN_OPENING)`, the expression
`items.opening_size` exists precisely to replace - the opening is the
WIDENED size when the pack cap forces packs to grow. The test was looking
at five slots while `open_the_start` had filled six. It passed by luck
until a draw put the fourth solvable puzzle at index 5, and then read as
a regression in the reserve rather than as the stale window it was.

### The credits have to be played, not just unlocked

droha, mid-playtest: "i beat the level that had the credits unlock... That
instant it said i completed the game. i didn't have to go out and play the
credits at all."

The goal fired the moment the Credits ITEM arrived, which read as the run
ending without an ending. `GoalLatch.ShouldReport` now also requires the
credits to have been played, and there is deliberately **no fallback** -
droha: "there should be no fallback. the user needs to click on the credits
level to finish". A harness that never opens the card never sees a goal, which
is correct rather than a regression, and cost an e2e run to re-learn.

The card itself was also unreadable: a greyed hand-print with no label, which
droha could not identify as the credits at all. It gets a completion row like
any other card once it is playable, and a divider before it so it is visibly
the end of the track rather than part of the last pack. Not a chapter break -
that was tried and looked like gold-plating - just an ordinary card.

### A mechanic reserve that could eat a short run

The coverage reserve took one level per mechanic before anything else drew,
with nothing stopping it from taking the WHOLE run. At 8 puzzles it did: every
slot went to a different mechanic, so nearly every level needed abilities the
player could not yet hold, and the run deadlocked at 2 of 8.

The 0.3.2 release gate caught it and 0.3.1 passed the same gate 21/21 an hour
later - a real regression this release introduced. Nothing in the unit suites
saw it, because they ask whether a mechanic is PRESENT and every one of them
was. That was the whole problem.

The reserve is capped at half the slots, so the weighted draw always gets the
other half. At any realistic length it changes nothing: twelve abilities need
six to eight levels to cover, and half of a 40-puzzle run is twenty. It binds
only where it has to. `TestTheReserveLeavesRoomOnAShortRun` pins it from the
side that matters - at most half an 8-puzzle seed may need three or more
abilities.

### Scenery stopped being reported as a controller mismatch

`Pannables` carries no locations on any level, so every level holding one
logged the mod's loudest warning forever. It is skipped in the audit now.

Measured across all 39 controller types before hardcoding anything: Pannables
is the only one that appears (12 times) and never carries a location. The
apworld had already reached the same conclusion independently - `abilities.json`
lists it under `notPuzzles` - and `ControllerTypes` in Core is now the single
place that says so, with the count in its comment.

### Tools

`jiggle` settles pieces on demand, because reproducing the cat trap freeze
needed a trap to land inside the settle animation and droha asked the fair
question: "how do I time that? It needs to be timed to like the quarter
second." `resolutions` prints Unity's list, the GAME's list and the saved
index side by side; `setres 1280 720` sets a size by size, never by index.

`AppendLog = false` in `BepInEx.cfg` is why the first freeze report was
uninvestigable. Turn it on before hunting anything intermittent.

**The release gate now says what its Skips covered for.** A Skip banks the
slot's Beaten token, so a skipped level is indistinguishable from a solved one
in every count the gate prints - a run could go green having never solved a
quarter of its puzzles, with the only trace a line in the middle of a
fifteen-minute transcript. It keeps a ledger now and asserts two things: a
Skip was spent only on a level in `KNOWN_UNFORCEABLE`, and no Skip covered for
a level the mod was still ability-gating. The second is the one with teeth -
a gated level reaches the skip path looking exactly like an unfinishable one,
so without it a mod that wrongly withheld an ability would be paid past and
the run would pass. The reading is taken BEFORE the Skip is spent, because
spending it destroys the evidence.

**And it puts the player's environment back.** The gate deletes the mod config
and writes one pointing at localhost; it never restored either, and droha lost
their real server settings to it. Every exit path now goes through a restore,
and the game log is archived per run so an intermittent failure can be diffed
against a passing one instead of guessed at.

`harness_env.set_config` ADDS a missing key instead of warning about it. A key
is missing whenever the plugin has not written its config since the setting
was introduced, and set_config runs before the launch that would write it - so
`MuteAudio` was added, wired into five harnesses, and silently did nothing
every time, while droha listened to the game twice and said so.

Every line the gate prints also lands in `testserver/logs/e2e-progress.txt`,
at a fixed path, so a fifteen-minute run can be watched from an editor pane
that shows neither the background process nor its output.

## 0.3.1 - 2026-09-09

Fixes from droha's first full 79-puzzle playthrough of 0.3.0. The BepInEx log
from that session survived and is the evidence for most of what follows;
several reports that read as separate bugs turned out to share a cause.

### The one that caused three of the reports

The log carried two warnings:

    track: ignoring a pending slot 22 set 9 frames ago
    track: ignoring a pending slot 39 set 8 frames ago

`Navigation.AfterGetNextLevelIndex` is a postfix on `GetNextLevelIndex`, which
the game also calls while building the post-level UI - so finishing a puzzle
armed a slot speculatively. `Track.BeforeStartLevel` then cleared the arm
*before* testing its age, so the next unrelated `StartLevel` consumed it and
fell back to `randomSeed = -1`, which for a generator level means its stock
layout.

- **Two instances of the same generator no longer come out identical.** The
  reported "two of the same envelope level" was both instances losing their
  baked seed and landing on the same stock layout.
- **A Cat Trap no longer swaps the puzzle underneath you.** The trap's restart
  was one of the launches eating the arm, so the reset rebuilt the level from
  the stock layout instead of the one being played. Not reported; the log
  caught it.
- **A generator level can no longer load empty.** The same early return skipped
  `forceReload`, which produces a flat single-colour screen with no way out.

The frame-age heuristic is gone. `Track.ResolveSlotFor` matches on the level
index `StartLevel` was actually handed, which is a fact rather than a guess.

### Two background items became one: Background Change Trap

droha: "I think we can change the name of the menu background item to just
Background Change Trap (as there's sometimes it can hide items which is still
funny)", and then "and it should change the level select menu background as
well".

- **`Level Background` and `Menu Background` are now one item, `Background
  Change Trap`.** They were two items with two counters recolouring two screens
  from the same palette, so the pause screen routinely sat several colours
  behind the puzzle in front of it. One item, one counter, one colour
  everywhere.
- **It now recolours the level select too**, which neither of the old items
  did. The track's section colours are ROTATED by how many traps you hold
  rather than flattened to one - sections exist to be told apart, and a level
  select painted all one colour would cost more than the trap gains.
- **A trap that lands while you are standing in the level select repaints it
  immediately.** `Track.RepaintSections` re-runs `SetupSections` rather than
  the full `Rebuild`, which would re-lay-out the track and throw away your
  scroll position for the sake of a colour.
- Every colour is still a pure function of the received count, never stepped on
  arrival, so Archipelago's replay of the whole item list on each connect lands
  exactly where the player already was.

**This changes item ids: seeds generated before this build will not match a mod
built after it.**

### Level select

- **Clicking a locked card no longer soft-locks the game.** The locked-slot and
  locked-credits refusals sat on `DoStartLevel`, which runs after
  `OnPointerClick` has already selected the card and begun the transition -
  refusing there left the player in a transition to nothing. Both refusals moved
  up to the click, where the divider refusal already lived, and a locked card
  now explains itself with a toast instead of doing nothing.
- **A locked instance of an unlocked level looks locked.** The game's own
  line-art "locked" look is driven by `LevelInterface.IsUnlocked`, which is one
  row per level id with no per-slot scoping - so unlocking one instance turned
  every card for that level to full colour. The save cannot express the
  difference, so it is drawn: locked cards are veiled.
- **Repeated levels are numbered from #1.** They were numbered from #2, so the
  first card of a pair read as an unnumbered duplicate. Cosmetic only - the
  Archipelago location names still leave the first instance unnumbered, because
  location ids are positional and renaming them would invalidate every seed in
  flight.
- **Pack dividers no longer run out.** The game has five chapter cards and a
  default run wants fifteen, so packs 5 to 14 - fifty-eight cards - ran together
  as one unbroken block. The cards now cycle; section headings were already
  titled per pack and stay correct.
- **The badge repaint cache is keyed by slot, not track position.** Positions
  shift when a pack inserts a divider.
- **New: the level select says whether the run is connected**, with the same
  four states as the main menu.
- **New: the overview strip along the bottom is colour-coded** by card state.

### Daily Tidy

- **Finishing a daily-pool puzzle keeps you in the run.** Six of the levels a
  seed can draw are the game's daily generators, and at the default weighting
  they are the most common cards in a run. Vanilla routes their completion
  straight to the Daily page, and the existing redirects patch post-level
  BUTTONS - which are never shown for these, so there was nothing to intercept.
- **A run no longer writes to your real daily progress.** The same routing ran
  the game's return-from-daily sequence, advancing the genuine completion count
  and streak and firing the badge prompt. The save redirect scopes level data
  but not the profile's daily counters, so a run had been quietly crediting
  dailies that were never played. This also removes the badge popups.

### Logic

- **Drawer contents now require the drawer.** `Tool Drawer`, `Bathroom Drawer`,
  `Paper Plane Supplies` and `Workbench` recorded no dependency between their
  contents and the container, so logic said the 47 tools in Tool Drawer needed
  no items at all while the game kept the drawer shut until `Furniture` arrived.
  Eight one-way `dependsOn` edges added. **This changes generated seeds.**
- **Two instances of one generator cannot draw the same seed.** Vanishingly
  unlikely rather than observed, but it is now a guarantee.
- **Arranging a set now requires having assembled it.** `Candy Canes` has five
  jigsaw pairs and an `Ordered` group over the five finished canes, and logic
  believed `Ordered` needed only `Ordering` - so a player holding `Ordering`
  and not `Jigsaw` got a card advertising available work and a level with
  nothing on screen to touch. The canes do not exist until they are matched.
  Same shape on `Paper Plane Supplies`, whose seven chalks are ordered after
  seven jigsaws, and on `GoodTidings_Cookies (Jigsaw)`, which needs `Jigsaw`
  on both sides and so changes no requirement. Sixteen `dependsOn` edges added.
  **This changes generated seeds.**

  Found structurally rather than by luck: the arranging group's object count
  equals the number of assembling groups, because its objects are their
  outputs. That query now has only these three hits across all 111 levels.
- **Four levels no longer hide an ability they need.** `TupperwareNesting`,
  `Record Player`, `Radial Dance Party` and `MedicineCabinet` reveal
  controllers only as the player solves the previous group, so the sweep that
  built the table never saw their later phases - and logic believed the levels
  were finishable without `Grids`, `Gadgets`, `Rotating` and `Furniture`
  respectively. Progression could be placed behind a puzzle the player could
  not finish. Found when droha solved five groups on `TupperwareNesting` and
  received nothing for them. **This changes generated seeds** - it changes
  logic only, so no location moved and no id shifted.

### TupperwareTower had two locations that could never be earned

droha finished it, got the in-game star, and the card stayed green. Only the
`Tower` check had fired; `Foundation` and `Falling Blocks` had not - and both
were unlocked and in active use at the time, since droha holds Grids and was
dragging the falling blocks onto the tower.

They are the tower's MECHANISM, not objectives: the base it sits on, and the
queue of blocks you place. Neither raises a solved event, so both were dead
locations - the card could never go gold, and fill could have put progression
on one. Corroborating: in the only two other levels using `StackableGrid` it is
the SOLE controller, where it plainly is the puzzle. And droha's own read -
"as far as I know there's only 1 solution so i don't think there's really mini
solutions for this level" - matches the table, which records one solution and
no declared phases.

Both removed. **Location ids shift**; 438 to 435.

`Grids` is still required and is now recorded as an `extraAbility`. The
cross-check test caught that removal dropping it, which would have been the
worse bug: the falling blocks are dimmed without Grids, and a tower cannot be
built out of blocks you cannot pick up.

### An ability lock cannot gate a group solved with someone else's objects

Worth writing down as a limit rather than a defect. The lock works by dimming
the objects a controller manages. On TupperwareTower the `Tower` group needs
Stacking, droha does not have Stacking, and the Tower check fired anyway -
because the tower is built by dragging `Falling Blocks` objects, which belong
to a different, unlocked group. Only two objects are shared between the two, so
this is not the shared-object rule letting go; it is that dimming a group's own
objects says nothing about whether its solution can be reached through another
group's.

No fix attempted. It leaks a check in the player's favour rather than stranding
one, which is the safe direction, and any real fix would mean gating on
something other than object interactivity.

### Three more levels where a locked group blocked a free one

The SomethingEggstra Fridge turned out not to be a one-off. droha hit a second
one - "Fridge Inside has half green/half red. some of the items are greyed out
and can't move and i think you need all of them to do the level" - so rather
than wait for the rest to bite, the whole watch list was measured with the same
spatial test: does the gated group's objects sit INSIDE the free group's field?

| Level | Gated among the free ones | Verdict |
|---|---|---|
| Fridge Inside | 2 tupperware in 4 shelf items | **blocked** |
| Breadtags | 9 crumbs over 9 tags | **blocked** |
| MerryMess_Crackers | 5 crackers in the train | **blocked** |
| Cleaning Supplies | none | separated, fine |
| Desktop Computer | none | separated, fine |
| Record Player | no free/gated mix | fine |

Three `dependsOn` edges added. The free group on each now requires what the
locked one needs, so it cannot be handed out as the only reachable check.

Worth noting the shape has now produced **four** real cases out of ten
candidates, so "shares the shape" is a much stronger signal than it looked when
the Fridge was a single data point.

### One-off prompts came back on every new seed

droha: "shouldn't that be gone once i select the option? or since my main save
doesn't have them chosen it still pops those up?" - the second guess, exactly.

The game stores "you have seen the colour assist prompt" in the SAVE, and a run
gets its own save file, so each new seed looked like a fresh install.
`SaveRedirect` already copied the flags forward from the campaign save, but
droha's campaign save has all five set false, so there was nothing to copy and
the prompts returned on every seed - and this project generates a lot of seeds.

New `PromptMemory`: the mod keeps its own note of which prompts have been
answered in ANY run, and unions it into each new run save. Not by writing to
the campaign save - a run must never open that for writing, and "these flags
are harmless" is exactly the argument that would erode the guarantee the save
redirect exists to provide.

### Clicking a card launched the wrong layout

droha: "when I click Play it opens up a different Calendar level than when I go
to level select and click the first level." Play was right; **every card click
was wrong**.

`BeforeStartLevel` applies the slot's baked generator seed, and it needs to know
which level is starting. The arrow route passes a real index. The click route
passes 0, so the prefix fell back to the active level interface - on the
reasoning, written in the comment, that "the card selection already made the
level active". It does not: opening the level select tears the previous level
down, so by the time a card click reaches `StartLevel` the active interface is
NULL. `target` came out -1, the prefix returned before applying anything, and
the level built itself from no seed.

Measured either side. Before: Play gave `StartLevel(index=12, forceReload=True,
seed=715837904)` and a 12-object Calendar; the card gave `StartLevel(index=0,
forceReload=False, seed=-1)` and a 13-object one. After: both give
`seed=715837904, forceReload=True` and byte-identical layouts.

The click prefix already knew exactly which card was clicked, so the target now
comes from that arm - trusted only while it is fresh, the same staleness rule
that governs slot resolution - falling back to the active interface as before.

Why it survived: it is invisible on a hand-made level, which is most of them,
and the release harness drives the next-level arrow rather than card clicks, so
the one route that passes a real index is the one under test.

- **New: `launchtrace` in DevTools.** Logs every call into `StartLevel`,
  `SetActiveLevel` and `RestartLevel` with its arguments. This bug was two
  guesses deep until the trace showed `index=0, seed=-1` in plain text.
- **A Harmony class nobody registers is silently never applied.** DevTools
  calls `PatchAll(Type)` per class. `RegistrationLog` was written, shipped and
  reported "zero registrations across 111 levels" - read at the time as
  evidence about the game, when it was really evidence the patch did not exist
  at runtime. That conclusion is retracted in the verification log. Both
  tracer classes are registered now.

### An egg hunt logic could not see

`SomethingEggstra Fridge` hides six eggs among the 24 items on its shelves, and
the puzzle is to find them and get them into the carton. Logic recorded its
three groups as independent, so `Standard Objects` - a plain Draggables group -
looked free. It is not: the shelf cannot be made tidy while six eggs are
sitting in it, and the eggs need `Containers`.

droha hit this with `Containers` unheld and found the run down to that single
reachable check, with the eggs greyed out and the hint showing a solution made
entirely of moves they could not make. **Effectively a softlock.**

`StandardObjects` now depends on `EggsContainable`. Measured either side: three
eggs sit up among the shelf items and three down by the carton, which is the
hunt the hint describes.

Two things this did NOT come from, and both are worth recording. The game
declares no dependency here - `dependsOn` is empty and `dependenciesPlacedFirst`
is 0 on every object - so the authored data that settled the phase audit is
silent on this one. And the prefab survey shows nothing either. The dependency
is implicit in what "tidy" means for the level, and only playing it reveals
that.

Nine other levels share the shape - a baseline Draggables group beside a gated
one, with no recorded dependency - and four are now cleared: `Books 3` and
`TrickOrTidy_ChocolateBars` by construction (their groups share every object),
`MedicineCabinet` and `Mirror` by play. The remaining six are listed under
Known.

Which levels a human has actually played is now recorded in
`tools/tested-levels.txt` rather than inferred, and the playtest seed generator
reads it to bias new seeds towards untouched puzzle types.

### The main campaign was unreachable

**57 of the game's 69 campaign puzzles could never appear in a seed.** Not a
bug in the usual sense - a design decision with a consequence nobody had
counted. `slots.py` fills slots by source, `source_weights` held only
`generator` and `archive`, so a campaign level could enter only through the
mechanic-coverage reserve, which opens for exactly four abilities. Twelve
levels qualified. The rest were dead content.

It surfaced trying to playtest Radial Dance Party after the phase audit took it
from 1 location to 11: eighteen rolls could not place it, and no amount of
weighting the seed picker could have helped.

- **New `base_weight` option**, and the default split becomes **80 generator /
  10 archive / 10 base**. **Every generated seed changes.** Setting it to 0
  reproduces the old behaviour exactly.
- Measured over 5 seeds at 79 slots: **60.2 generator / 10.0 archive / 8.8
  base**, of which 4.8 are ordinary campaign puzzles that previously could not
  appear at all, and 28 distinct campaign levels across the five seeds against
  7 before. Generator draws fall from 66.6 to 60.2, so a long run leans less on
  repeating the same generator.
- Campaign levels are drawn uniformly. Weighting towards the 45 that teach a
  mechanic was considered and dropped: at a 10% share it is not worth the
  special case, and the naive version of it would have been an exclusion rather
  than a preference.
- The old design is recorded rather than deleted, including which half of the
  original objection to a base weight was a real property and which half was a
  bug that has since been fixed.

Two latent problems that only mattered once campaign levels became common:

- **The pause-menu route home could steal a check.** `Navigation.CampaignLevel`
  cached the first campaign level it found - index 1, Cat Frame - and never
  released it. It is handed to `GoToLevelSelectForLevel` on every pause-menu
  Levels press, so once that level is also one of the run's cards, a later
  seedless `StartLevel` could resolve to its slot and file a check against a
  puzzle nobody opened. It now prefers a level the run does not contain, and
  the cache is cleared when a run ends.
- **The cat sound was decided once per session.** The search latched on the
  first attempt whatever the outcome, so a trap sprung on a level with no cat
  settled it permanently. **11 of the 13 levels carrying a cat are campaign
  levels**, which is exactly why nobody had noticed. It now re-searches while
  the clip is still null.

A third suspected problem turned out not to be one, and is recorded because
the reasoning nearly produced a change. Vanilla creates its own completion row
whenever a level is beaten - "finish N, create N+1" - and `ApplyUnlocks` skips
rows it did not create, so those keep `unlockedOnLevelSelect` false. That
looked like the replayed-unlock-animation bug returning once campaign levels
became common. Measured instead of assumed: reading the save either side of
opening the level select shows the flags unchanged, so nothing clears or
re-sets them, and a replay needs a re-set. A vanilla row also cannot make a
locked card playable - `IsRefused` gates on the run's pack state and never
reads the save. No change made.

`Track.LaunchSlot` is deleted. It had no callers and passed `forceReload:
false`, which a generator slot survives - `BeforeStartLevel` upgrades it - and
a campaign slot does not, because it takes the `Seed < 0` early return first.

### Every phase now pays

The audit proved eleven groups real that mint no location; they are restored,
and the table has nothing left that the game says exists.

- **Radial Dance Party: 1 location -> 11.** All ten declared dances, in the
  order `RadialDanceParty` declares them, each depending on the one before.
  This level had contributed a single check for a ten-ring puzzle since the
  beginning. Its `extraAbilities` override for `Rotating` is gone - the
  `RadialDance` controllers supply it themselves now.
- **TupperwareNesting gains `Food`,** the sixth and final phase.
- **`BespokeLevels` is empty.** Radial Dance Party was excused from "every
  level registers a controller" on the strength of a count the sweep could not
  take correctly. There is no longer any level that needs the exception.

**This changes location ids and invalidates every earlier seed.** 427 locations
to 438. 0.3.1 is unreleased, so it rides with it.

### Books (Randomized) is seed-varying, and the code says why

Recorded here because the first answer came from the wrong place. The absence
of this controller in some seeds was noticed by generating eight of them and
counting - which is how you CHECK a rule, not how you find one.

`Books_LevelRandomizer` holds it in a field called
`DraggablesForSymmetricSolutions`, and the level draws from a seven-value
solution enum of which two, `HEIGHT_SYMMETRIC` and `WIDTH_SYMMETRIC`, are
symmetric. The controller appears exactly when a symmetric solution is rolled.
A location there would be unearnable in any seed that rolls none, so it stays
out - now for a reason rather than a frequency.

Every other generator's controller fields are unconditional, and all of them
are already recorded, so this is the only level of its kind.

### The phase and dependency audit

Three playtests in a row hit the same class of bug - a card offering work the
level would not give - and each was patched by hand from the report. droha
called it: the guessing had to stop. It turns out the game DECLARES all of it,
and nothing here had ever read the declarations.

- **`PhasedLevel.phases`, `TupperwareNesting.GetPhaseControllers()` and
  `RadialDanceParty.dances` are authored, ordered lists** naming exactly which
  controllers a level reveals and in what order. The sweep now records them,
  along with `levelType` (the game's own class for the level) and each
  drawer's `UnlockOnSolvedControllers`. Three levels in the entire game declare
  phases: `PawPrints` (3), `TupperwareNesting` (6) and `Radial Dance Party`
  (10). That is the complete answer to "which puzzles have mini solutions",
  measured rather than inferred.
- **The TupperwareNesting phase order was wrong, in both directions.** It had
  been guessed as "every later group depends on `Lids` and `Stack 1`", the two
  that register at boot. The game declares a CHAIN that does not involve `Lids`
  at all: `Stack 1 -> Stack 2 -> Tray -> Stack 3 -> Layout (Grid) -> Food`. The
  guess invented a `Containers` requirement on three groups and pushed a fourth
  to needing three abilities. Corrected, no group needs three again.
- **`Food` is not a ghost.** It was written off as prefab dead weight because it
  never appeared in play; it is the sixth and final phase. The run never got
  that far.
- **`Radial Dance Party`'s ten rings are real.** The level registers nothing at
  boot, which had been read as "no puzzle content"; it declares ten phases,
  `Radial Pencils 0` through `Radial Chess 9`. It currently mints one location
  for all ten.
- **New: `docs/data/controller-classes.tsv` and `tools/classify-controllers.py`.**
  Every one of the 223 controllers classified as always-on, phase-revealed,
  phase-driver, mutual, gated, seed-varying, ghost or non-puzzle, with the
  evidence for each. Only **three** true ghosts survive the audit.

### Shared objects were being locked by the wrong controller

- **An object owned by two controllers is no longer locked by whichever ran
  last.** The ability pass walked controllers in order and wrote each one's
  objects, so on a level where two groups move the SAME pieces, a locked group
  silently re-locked everything an unlocked one had just released - leaving a
  level with nothing to touch while its card honestly reported work available.
  droha hit this on `Coins 1 (Shape)`, whose `Ordered` and `Stacked` groups
  share all six coins.

  An object is now locked only if EVERY controller that owns it is locked.
  **Five levels are affected**: `Coins 1 (Shape)` (6 shared), `Spoons` (7),
  `Books 3` (17), `TrickOrTidy_ChocolateBars` (9) and `Workbench` (21) - found
  by recording managed-object instance ids in the sweep, not by guessing.
  Verified in game: with Swapping locked, Books 3's seventeen books stay in
  full colour and remain movable for the height ordering that is unlocked.

  The reported object count in the log is now DISTINCT objects rather than
  controller-object pairs, so it reads lower on exactly these levels.

- **A dependency chain no longer breaks at a non-puzzle controller.**
  `ControllerGroups` skipped both the abilities and the onward edges of any
  dependency target it had filtered out, so a chain through a `Pannables` would
  silently lose everything past it - and lose it in the direction that makes a
  seed unfinishable. No level has that shape today; the trap is closed anyway.

### TupperwareNesting pays for every phase, not just the first

The level reveals its groups as you solve them, so the boot-time sweep that
built the table saw two of them and recorded a three-location puzzle. droha
solved five more groups and got nothing for any of them.

- **Five phased groups restored, 3 locations to 7.** `Stack 2`, `Stack 3`,
  `Tray`, and the merged `Layout (Grid)` / `Draggables (Large Square)` pair now
  mint their own checks. **This changes location ids and invalidates older
  seeds.**
- **Evidence, not inference.** These five are exactly the ones the mod's
  runtime audit watched register during the playtest. The prefab lists two
  more - `Food` and `Nested Tupperware` - which never appeared even through
  every phase of real play, so they are treated as prefab ghosts and left out.
  A location behind a controller that never registers can never be checked,
  and fill will happily put progression on it.
- **The phase order is recorded as dependencies.** Each restored group depends
  on `Lids` and `Stack 1`, the two the level offers at the start, so logic
  cannot believe a later phase is reachable before the first one is. That makes
  the grid group the first in the game to need three abilities, which is why
  the narrow-requirement bound moved from two to three and the fill sweep was
  re-measured rather than the check relaxed.
- **`extraAbilities` dropped from this level.** `Grids` was recorded there
  because no controller revealed it; `Layout (Grid)` now does, and two sources
  for one fact is how they drift apart.

The other five under-captured levels are unchanged and still listed under
Known. Only this one has runtime evidence, and the boot-time census run on
2026-09-08 confirmed the rest register nothing extra at load - so for them the
question of phased-versus-ghost is still open.

### Solution checks were lost across sessions

droha reported Snow Globes showing all three stars in game while the card said
there was still something to do. There was: two of its three checks had never
been sent.

- **The solution counter no longer restarts every launch.** Which location a
  completion files is decided by an ordinal - the first new arrangement of a
  slot files Solution 1, the second Solution 2 - and that counter lived only in
  memory. Closing the game reset it, so the next arrangement re-filed Solution
  1, a location already collected, and went nowhere. Any level with more than
  one solution lost checks unless every arrangement was found in a single
  sitting. It is now seeded from the game's own save, which records the
  solutionId of each arrangement found.
- **Checks already earned are handed back.** Seeding alone would have frozen
  the loss in place: with every arrangement now recognised, no further check
  could ever be filed for them. Entering a slot now files any solution the
  player has demonstrably earned and not received, subject to the usual
  reachability rule, so a run that already lost checks repairs itself.
  Confirmed on droha's run: Snow Globes handed back Solutions 2 and 3.
- **Archive levels are read from the right list.** The save keeps campaign and
  archive progress apart and 26 of the 111 levels in the pool are archive
  levels. The first version of this fix read only the campaign list, found
  nothing for Snow Globes, and silently did nothing - which looked exactly like
  the fix working.

### The Daily Tidy page, for the third time

droha finished the Spider Web puzzle, pressed the next arrow, and landed on the
Daily Tidy page. Nothing appeared in the log, because none of the navigation the
mod had patched was involved.

- **A third of the pool are daily levels, not six.** `DailyGuard` was written
  believing six levels could do this - the "(Randomized)" generators. The real
  figure is **36 of 111**: 16 in the everyday rotation, including ordinary
  looking puzzles like Spider Web, Buttons, Shells and Telescope, plus 20
  seasonal holiday levels. Sizing the guard for six is why this took three
  attempts to fix. The correct number was already written in
  `docs/content-report.md`; nothing connected it to the code.
- **No level is a daily while a run owns the game.** `LevelInterface.IsDailyTidy`
  and `IsHolidayDaily` now answer false during a run, which stops the routing
  happening rather than undoing it afterwards. Read-only, and vanilla behaviour
  returns the moment the run ends.
- **New: a watchdog on the state itself.** If the game reaches the Daily page by
  any route at all, being there is the trigger: the run opens its own next
  puzzle instead. A guard that watches the destination cannot be defeated by a
  route nobody found yet, which is what "never go to this page" actually needs.
  It leaves through the game's own state transition and gives up loudly after
  three attempts rather than retrying.
- **New: the daily pool is data.** `isDailyTidy` and `isHolidayDaily` are
  recorded per level in `levels.json` and pinned by `DailyPoolTests`, so the
  count cannot drift from the game again without failing the build.

### Presentation

- **The level select no longer pops its Archipelago furniture in late.** The
  connected tag polls every half second and the overview dots every second, on
  free-running timers that knew nothing about the menu opening - so the badges
  could arrive up to a second after the screen had settled. Building the menu
  now marks those polls due immediately. Polling stays: there is no single
  reliable event for every route into that menu, which is why the track rebuild
  is on a timer too. What was wrong was letting a poll that exists to CATCH
  later changes also decide when the first paint happens.

### Robustness and diagnostics

- **One failing tick step no longer silences the ones below it.** The per-frame
  update ran nineteen bare calls with no `try` anywhere, and `Toasts.Tick` is
  tenth - so a fault above it stopped toasts appearing with nothing in the log.
  Each step is isolated and names itself once if it throws. This is the most
  likely cause of "the toast message doesn't always pop up".
- **The connection pane puts the game's modal back.** It borrows the shared
  singleton modal and replaced its confirm button's whole click event, destroyed
  its localiser and overwrote its caption, restoring none of it - so after one
  visit to the pane, every other dialog in the game had a Confirm button that
  ran an Archipelago connect. Everything is now recorded and undone on close.
- **The pane binds to the live input fields.** A leftover copy from an earlier
  open could be bound instead, which draws but refuses focus - the reported
  "sometimes can't click the text box".
- **A re-firing controller no longer floods the log.** 723 of 736
  "no location for it" lines in the playtest were two controllers re-raising
  their solved event every frame, burying the eleven that were real.
- **New: unearnable locations are reported.** The controller audit only checked
  for controllers missing from the table; a location in the table with no
  controller behind it can never be earned and pins a card for the whole run.
- **New: a probe on the pause menu Exit button.** Two candidate causes, and they
  are separable from one log line - see `Navigation.BeforeExitGame`.
- **The controller audit looks more than once.** It latched after its first
  successful pass, so on a level that reveals its pieces phase by phase it
  looked before anything had been revealed and then never again. It now
  re-audits whenever the level has grown, and reports per controller rather
  than per level so a second batch is not hidden by the first.

### Testing

- **The campaign-save check no longer fails once a day.** It hashed the whole
  file, so it caught the game rolling its daily calendar forward at launch -
  which happens before a session exists and which vanilla does anyway - and
  reported a clean run as a save leak. It now compares campaign progress with
  the timestamp and daily calendar excluded, and separately pins the daily
  completion count, which IS ours to protect. Four negative controls.
- **The narrow-requirement invariant pins a set, not a bound.** Four groups now
  legitimately need two abilities; the test lists them and still fails on a
  fifth, or on one going missing.
- **New: `tools/check-game-facts.py`.** Holds `levels.json` up against a fresh
  DevTools dump and fails on any disagreement. This project has repeatedly
  shipped a confidently wrong count in a comment - how many levels are dailies,
  how many are randomizable, how many carry hints - and prose cannot fail a
  build. The script also records which of the game's own numbers are unstable
  (they answer "what is true today", not "what is true of this level") and
  insists the dump be taken with the mod OFF, because the daily guard changes
  the answers it reports.
- **New: the shipped table is cross-checked against the prefab survey.** The
  two files answer different questions - what registered at boot, versus what
  the level was authored to contain - so this pins the six levels where they
  disagree, controller by controller, rather than demanding they match. A new
  disagreement fails the build and has to be explained as a phased controller
  or a prefab ghost. It also asserts the thing that actually bites: no level
  may need an ability it does not declare.
- **A wrong controller table now fails the e2e.** `CONTROLLER MISMATCH` and
  `UNEARNABLE LOCATIONS` were warnings and the census counts only errors, so a
  run could pass with the table wrong - which is how six under-captured levels
  survived every release gate so far. The six known ones are allowed by name;
  any other fails the run.

### Known

- **Six levels may hide the same trap as the Fridge.** `Breadtags`,
  `Cleaning Supplies`, `Desktop Computer`, `Fridge Inside`, `Record Player` and
  `MerryMess_Crackers` each pair a no-ability Draggables group with a gated one
  and record no dependency between them. That is only a shape, not a defect -
  most such pairs are genuinely independent. Telling them apart needs the
  spatial check that settled the Fridge: boot the level and see whether the
  gated group's objects sit among the free group's.

  Two of the ten that share the shape are cleared by construction: `Books 3`
  and `TrickOrTidy_ChocolateBars` have groups that share every object, so the
  free arrangement is independent. Two more are cleared by play:
  `MedicineCabinet` was hand-solved during Phase 0, and **`Mirror` is the
  useful one** - droha earned all five of its no-ability part checks while its
  Containers, Gadgets and Stacking groups were locked, which is direct proof
  the free groups do not wait on the gated ones.
- **Nothing is missing any more.** The eleven the audit proved real are
  restored above, and `tools/classify-controllers.py` reports zero
  phase-revealed controllers absent from the table.

  The audit closed the question the other way for four controllers.
  `Desktop Computer/Hourglass`, `MedicineCabinet/Cupboard` and
  `Record Player/RecordPlayer` are **ghosts**: in the prefab, named in no
  level's phase declaration, never registering. `Books (Randomized)/Draggables`
  is **seed-varying** - present in 6 of 8 sampled seeds - which is a category
  of its own and is waiting on a wider generator sweep before any policy is
  set. None of the four should become a location.
- **Backgrounds** now step past a palette colour the pieces would vanish into,
  by measuring contrast against the renderers on screen. The threshold is a
  first estimate and may want adjusting.

## 0.3.0

The first release. Everything below is what "it works" currently means.

### The run

- **The seed replaces the campaign.** The level select shows the run's puzzles
  in the order the generator planned, and the campaign save is never opened
  while a session is active - the game is pointed at `save_ap_<slot>_<seed>`
  instead. Isolation by construction, so a crash or an alt-F4 cannot leak a
  randomized run into the player's own progress.
- **Progressive Puzzle Packs** reveal puzzles in widening groups. The ramp is
  decided at generation and sent in slot data rather than recomputed in the
  game, so the two cannot disagree about what is reachable.
- **Ability locks.** Objects for a mechanic you have not unlocked are dimmed
  and immovable, so a puzzle can be partly solved and returned to.
- **Checks** are the distinct solutions of each puzzle, plus a controller-group
  check where a level has separable parts.
- **The goal** is beating a configurable number of puzzles, which unlocks the
  credits card.

### Items beyond progression

- **Skips** clear a puzzle you are stuck on. A skipped puzzle does not count
  towards the goal.
- **Hint Pages** unlock a page of the in-game notepad. Without one the notepad
  still opens and the hint is visible as a scribble - you just cannot erase it.
  How many exist is a percentage of the pages the seed actually drew.
- **Cat Traps** knock your work over. They cost time, never progress, and the
  number already sprung is persisted so a reconnect does not fire them again.
- **Level and Menu Backgrounds** recolour the game, one shade per item.

### Connection

- **Offline play.** If no server answers at launch, the run resumes from a
  cache of the slot data and the received items rather than dropping to the
  vanilla game. Checks earned offline are queued and sent on the next
  connection. A seed regenerated under the same slot name gets its own save
  and replaces the cache, so a stale cache cannot write into the wrong run.
- **A mid-session drop keeps playing.** Progress is kept and pushed, never
  reverted.
- **Retries are unlimited** by default, backing off 5, 10, 20, 40 then 60
  seconds, and stop only on Cancel or Disconnect - matching Archipelago's own
  reference client. A refused login (wrong slot name or password) is never
  retried, because retrying cannot fix it.
- **CONNECT / CANCEL / DISCONNECT** in the in-game pane, three states for three
  situations. A press during an attempt supersedes it rather than being
  ignored.

### Known gaps

- A level that once loaded empty and has never reproduced. A watchdog logs
  `LEVEL LOADED EMPTY` if it happens again.
- The Play button's behaviour partway through a run - see the verification log.
- Leaving an offline run for the vanilla campaign means turning `AutoConnect`
  off in the Archipelago dialog and relaunching. There is no in-session route.
- DLC2 *Seeing Stars* is not supported; it was not owned when the content was
  surveyed.
