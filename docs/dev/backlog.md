# Backlog

What has been asked for and not yet done, each with what was asked and what is
already known, so picking one up does not begin with rediscovery. New items go
here rather than in a message.

## From the 0.4.3 run (droha and a second player, 2026-09-28), in 0.4.4

Reported live. Seed `AP_76116543768317964936`, generated locally from two
yamls that differ only in the name and the two DLC switches (off for droha,
on for the second player). Both players reached the goal. After the run:
the second player's `LogOutput.log` and `ErrorLog.log` (the error log is
only Steam's minidump line) and droha's own log. Neither
log has an error or an exception of the mod's; the warnings are the known
phased-level "UNEARNABLE LOCATIONS" (Tupperware Nesting, Radial Dance
Party), the three known controller mismatches (Tupperware Tower, Something
Eggstra Fridge, Books' Draggables), focus "suppression has flipped" and one
login timeout. No server log (archipelago.gg shows it to the room owner
only).

All done and checked in game, the last of them (1, 10, 11, 12, and 14, a
question droha asked) on 2026-09-29, droha's hand tests included. Each
is in the CHANGELOG's 0.4.4 section, with how it was checked.

1. **Filled squares on the overview strip in packs not opened** (droha: "we
   fixed their big counterpart in the level select screen, but not the
   scroll bar"; a screenshot of Pack 3). Measured on that screenshot against
   the seed: of the 53 squares in shut packs, the six filled ones were
   exactly the copies of a level open in the first 15 slots (Batteries and
   Stamps (Randomized), Pencils (Randomized) twice, Trim Plant, Calendar).
   The game draws a square from its level's save row, the cause the cards'
   fix in 0.4.3 already undoes for cards. Fixed in source
   (`Badges.DrawShutSquaresLocked`, a postfix on each of the two methods that
   set a square's sprite, per the interop's xref cache). **Checked in game**
   after the run: the run's seed served fresh (only the opening open) over
   droha's finished save, so every shut level had a save row - 65 shut
   squares, 0 filled. That pass also showed 35 of them in the game's cream
   highlightColor, matching no save field; shut squares now get
   regularColor too, and the second pass had all 65 white.
   **Checked mid-session too** (2026-09-29, the same seed and save): a pack
   sent while on the track painted pack 2 and left the 60 still shut as
   white outlines; one sent inside a puzzle, then the pause menu's Level
   Select, painted pack 3 and left 55. None filled. The pack's rebuild runs
   the game's `LayoutOverview` then `UpdateOverviewAppearance` (DevTools
   `trace:`), so the postfix redraws after the game; `OverviewItemUnlock`
   never ran (the rows already exist). Screenshots at `shot:...|2` leave the
   game's UI out; a plain `shot:` has it.
2. **The second player's Fossils: the solution's hover star, and still a green card**
   (relayed by droha). The second player played part of it without Drawer, got Drawer, went
   back and finished it. Fossils is their slot 10, Pack 2. Its checks: Fern and
   Snake Fossil need Jigsaw; Dragonfly, Fish, Leaf and Shell Fossil and the
   Solution need Drawer and Jigsaw; Drawers needs Drawer. The one left is
   **Fossils - Drawers**, the drawer controller's part (the room's public
   tracker at 19:13 had the other seven checked), holding the second player's own
   Background Reset Token, filler. Not an achievement: Fossils has none
   (`AchievementChecks` lists none, nor does the spoiler). Why: item 8.
3. **A finished puzzle with nothing playable shows the main menu before the
   level select** (droha: "it first goes to the main menu? it should
   hopefully go directly to the level select"). Working as 0.4.3 built it:
   droha's log, TrickOrTidy_ChocolateBars (slot 22) with 4 of 4 solutions,
   "nothing playable after post-level Continue - the track, by way of the
   title", then "title is up - pressing its Levels". The title is there
   because the direct routes tried so far broke the level select: from the
   Daily page (after a generator) the track had no Close button and its
   cards never launched (2026-09-25, droha's first session ended there);
   `GoToLevelSelectForLevel` from the post-level screen builds the same
   half-made menu; the pause menu's Level Select does nothing after a beaten
   level (verification log). Not tried: the game's own post-level Level
   Select (`ReplayMenu.LevelSelect`, which the mod already lets the game run
   and which lands on the run's track), called from the refused retry panel
   in place of its NextLevel. To check in game after the run, on a panel
   level and on a generator: the Close button is there, a card launches, no
   title under the track.
   **Done** (droha: "go ahead and figure out the straight to level
   select"): with nothing playable the move on presses
   `ReplayMenu.LevelSelect` a frame later, once nothing is transitioning
   (`Navigation.ShowTrackFromThePostLevel`); it makes the same
   `SetGameState<Levels_GameState>` call as the title's Levels (the
   interop's xref cache). A generator takes the panel's route when nothing
   else is playable (Core `AfterPuzzleRoute`), so it no longer reaches the
   Daily page. The title route stays for the Daily page and as the fallback
   if the state has not changed 4 s after the press. Checked in game on two
   seeds: hand-made puzzles (Front of Fridge 1; NES Games then Cat Frame),
   generators (Calendar, Trim Plant) and DLC1 puzzles (Craft Supplies,
   Daggers) all landed on the track with its Close button
   and only the level select live, and each next card launched from there
   with one live level. A DLC puzzle passes through that DLC's own level
   select, which `DlcGuard` turns into the run's track (its Close then goes
   to the title, as before). Seen once and not since: in the first run,
   4 s after Calendar's route opened the track, the game launched the
   puzzle played before it (Front of Fridge 1) - the log shape of a click
   or Enter on its card (the mod's `DoStartLevel` prefix, then
   `LevelSelected`). The same seed and order replayed with DevTools
   `trace:` on the launch methods stayed on the track for the full 20 s. If
   it recurs, `trace:LevelIcon.IconSelected,LevelIcon.DoStartLevel,Menu.ButtonSelected` says what launched it.
   For a DLC puzzle the title route never built the DLC's own menu; this
   route does, for a moment, before `DlcGuard` leaves it - the route on
   which two level selects were once seen on screen together ("Two level
   selects on screen after a DLC level", below). Clean in both test runs;
   if it shows up again, DLC puzzles can keep the title route.
4. **Reset Background with no trap on must not spend a token** (droha: "if
   you use a background change token and youre not currently trapped it
   should not use the token"). Built that way in 0.4.3: `BackgroundResets.Check`
   refuses with the toast "The background is already the game's own" when no
   trap arrived since the last reset, and `RunStateData.SpendBackgroundReset`
   spends nothing then either (both pinned in Core tests). droha's run spent
   one while trapped ("reset by a token at 3 trap(s); 1 token(s) left"); the
   **Checked in game** after the run (a fresh run file, a token sent): with no
   trap on, "The background is already the game's own" and nothing spent;
   with one trap, "reset by a token at 1 trap(s); 0 token(s) left".
5. **Breadtags' Interlocking and Solution: combine?** (droha's question;
   droha's call). Not the Spoons' Stacked case: neither group finishes the
   puzzle alone, and the game files its one ending under the Interlocking
   controller's id (`Interlocking_0`), hence the likeness. Whichever group is
   done last lands with the Solution: droha's 0.4.2 playtest log has
   Crumbs 20:34:42 then Interlocking and the finish at 20:35:02, and
   Interlocking 21:08:57 then Crumbs and the finish at 21:09:03. The same
   holds for every puzzle with one ending and two or more part checks, 43 of
   them (Fossils too). Removing the double would mean a rule for all 43, such
   as no Solution check where one ending and several parts, with the star
   goal's Star events kept; location ids move (new seeds only).
   **Done for Breadtags** (droha: "combine/remove the breadtags order, and
   just have the solution"): Crumbs and Interlocking are
   `solutionOnlyParts`, so its one check is the Solution, on all eight
   copies (base ids 401 -> 385, new seeds only). The other 42 stay as
   they are.
6. **The draw against the yaml** (droha: "double check the distribution of
   levels vs yaml"). Checked: both slots were generated with the yamls'
   options (spoiler) by an apworld identical to the 0.4.3 commit in
   `slots.py`, `data.py`, `levels.json` and `names.json`. The real
   `generate_early` on 300 seeds per slot (redraws included; none happened at
   70 puzzles): the second player's mix is ordinary on every source; droha's 34 generators
   against a mean of 42.6 (5% to 95%: 37 to 49) is a 1-in-100 roll, with the
   campaign (19) and events (17) high to match. Chance, not the draw. The
   yaml's shares are per rolled puzzle: at 70/15/15 a run averages 61%
   generators, since the mechanic reserve places about 10 hand-made puzzles
   first (9.8 measured without a DLC, 10.3 with both, at coverage 3).
7. **`player.yaml` shortened and reordered** (droha, same message): done in
   the working tree for droha's review, with the options page's groups in
   the same order; the spoiler still lists options in `ALTTLOptions` order.
   droha's review (2026-09-29) applied: repeats said once for every
   source, `mechanic_coverage` and `generator_repeat_limit` (now 8 by
   default) plainer, a Skip's every check, and achievements able to hold
   progression. **Done**: droha's second look, 2026-09-29, "the rest looks
   good", with the achievements comment cut to what they are.
8. **Drawer-controller parts that never fire in play** (the second player's Lunch Tray:
   "[they] did the achivement, but it's still a green square"; and Fossils,
   item 2). On both only the `DrawerController`'s own part is unchecked
   (tracker: Lunch Tray - Trays, Fossils - Drawers), with Drawer held (the second player's
   16th item). The evidence that they never solve in play:
   droha's locks-off recordings (`testserver/logs/unlocks/1106.json`,
   `1115.json`, `1126.json`, 2026-09-23) have Trays, Drawers and Nesting
   Boxes' Boxes at `solved: None` while every other group solved; the second player
   finished Lunch Tray and Fossils in the 0.4.2 playtest without them either
   (Fossils - Drawers there came in the goal's release); Nesting Boxes' Boxes
   filed only when DevTools forced it (0.4.2, droha), and the second player's 0.4.2 run
   filed its Cat Organizer but not Boxes. The second player's log for this run: no
   `'Drawers' solved` or `'Trays' solved` line on any visit - Fossils with
   its drawers locked (Fern, Snake filed), then with Drawer held (the other
   four, the Solution); Lunch Tray finished twice (DrawerChanged 55 and 77).
   The controller never raises a solve, the TupperwareTower case of 0.3.x
   again (`Foundation` and `Falling Blocks`, removed for that reason). The
   other 13 drawer controllers are already `notALocation`. **They can hold progression**: their levels
   are proven, so `unproven_locations` does not guard them, and an ability
   there would never arrive. This run is safe - the second player's two
   held filler, and droha's run has no DLC. Recommended: `notALocation` on those three (droha's call,
   levels.json). The parts the same audit could not settle are item 13.
   **Done** (droha: "yes remove the ones that never fire"): Lunch Tray's
   Trays, Fossils' Drawers and Nesting Boxes' Boxes are `notALocation`
   (DLC1 ids 114 -> 111, new seeds only). Every other requirement is
   unchanged (`rules.requirements` before and after, on those levels):
   the fossils keep Drawer and Jigsaw, and Drawer stays in the Solutions.
9. **The second player's Books (Randomized) #1: both solutions done, one star** (relayed
   by droha: "my guess is that it's given solution 1, and stuck on that").
   Tracker: Solution: Shuffle 2 in, Shuffle 1 not (the second player's slot 49, seed
   891593314). Endings: `Shuffle_0` files Shuffle 1; `Shuffle_1` or
   `Draggables_0` files Shuffle 2 (the second controller symmetric seeds
   add). Both finishes answering to Shuffle 2 would give exactly this; not
   proven, since the mod never logged the id - it now does, one line per
   finish ("checks: slot N ended on '<id>' -> <location>"). Not from the run
   save: it keeps no found ids for a generated puzzle (droha's decoded save,
   `solutions: []` on Books, Pencils and Batteries (Randomized), while Sharp
   Pencils lists its two) - so no id leaks between two copies of one
   generator, and nothing is re-filed from the save for them either.
   droha hit it too ("i think it has the same id or something for both
   solutions"), with DevTools on, so the ids are in droha's log: slot 28
   (seed 104727143, one controller) ended on `Shuffle_1` then `Shuffle_0`,
   both filed; **slot 62** (seed 945554386, symmetric: "CONTROLLER MISMATCH
   ... Draggables") ended on `Draggables_0` (20:57:43, filed Shuffle 2) then
   `Shuffle_1` (20:59:07, the same check, nothing new). On every symmetric
   seed of the 40 swept, symmetric-first ones included
   (`HEIGHT_SYMMETRIC+IMAGE`), the game gives Draggables `SolutionId:1` and
   Shuffle `SolutionId:0` (`alttl-generators.tsv`), so by the game's own
   numbering both ids are solution 2 - droha's two arrangements would then
   be the symmetric rule twice, and `Shuffle_0` still undone. If they were
   two different rules, `Draggables_0` means "the symmetric rule" in
   whichever position the seed puts it, and 0.4.3 files it wrong on
   symmetric-first seeds. droha's two arrangements decide it.
   The second player's log, slot 49 (symmetric: the same mismatch warning): the first
   finish was the `Shuffle` controller and filed Shuffle 2, so `Shuffle_1`;
   the second was `Draggables` ("'Draggables' solved on slot 49"), nothing
   new. The same pair as droha's slot 62, in the other order. Two players,
   each "did both solutions", and on a symmetric seed neither produced
   `Shuffle_0` - which reads as two rules colliding rather than one rule
   done twice by both. **Settled, 2026-09-28** (DevTools `boot:995:<seed>`
   then `rules`, no run active): 945554386 chose `HEIGHT_SYMMETRIC+IMAGE`,
   891593314 `WIDTH_SYMMETRIC+HEIGHT` - the symmetric rule FIRST on both -
   and the control 104727143 (droha's slot 28, which filed both) chose
   `WIDTH+HEIGHT`. `Draggables_0` is the symmetric rule, wherever the seed
   puts it: on these two it is Solution 1, and `Shuffle_1` the other rule.
   Both players did both rules; 0.4.3 files `Draggables_0` as Shuffle 2 on
   every seed, which is wrong whenever the symmetric rule comes first. The
   game's own `SolutionId:1` on Draggables is the same on both orders, so it
   is no guide. **Fixed in source**: `Endings.Canonical` (Core) turns
   `Draggables_0` into the symmetric rule's own Shuffle entry, and
   `Checks.CanonicalEnding` reads the two rules from the running level's
   `Books_LevelRandomizer` at both completion events. Core test
   `BooksSymmetricRuleFilesWhereTheSeedPutIt` fails without it. **Checked
   in game** (droha's hand test on slot 62, seed 945554386): the symmetric
   arrangement "ended on 'Draggables_0' (as 'Shuffle_0') -> Books
   (Randomized) #2 - Solution: Shuffle 1", the picture one "ended on
   'Shuffle_1' -> ... Solution: Shuffle 2" - both withheld only because the
   test server had sent no Swapping.
10. **Small things from the logs.** Both players launched offline: the mod
   tried the last session's `archipelago.gg:58324`, the room was on 61849.
   The second player's offline start resumed their finished 0.4.2 run until the new port was
   entered, and toasted "The credits are unlocked - play them to finish the
   run" for that old run; nothing was filed on it, and "the offline run is
   taken over by seed 76116543768317964936" worked. On the takeover the log
   also says "the Credits item arrived; redrawing the credits card" - the
   credits state changed, nothing arrived (the real Credits came at 56
   beaten).
   **Done** (2026-09-29). The toast: `GoalLatch.ShouldAnnounce` ignored
   whether the credits had been played, so every session start of a
   finished run said "play them to finish the run", online too ("this run
   already played them" then "unlocked after 69" in their 0.4.2 log); it now
   refuses once they are played (Core test). The log line came from
   `Track.TickCreditsCard`, whose flag `Begin` never reset; it is gone - its
   redraw never wrote the card's row either (item 11) - and "received item:
   Credits" already says when the item comes. Checked in game: a finished
   run's offline start logs "this run already played them" and no unlock
   toast; the takeover by another seed (Connect in the pane) logs no credits
   line and leaves that run's card locked. The old port is the room's: it
   changes when the room restarts, and only the player can know the new
   one. A cached run from an older mod version still resumes offline; the
   version check guards the live connect, which is where a check could go
   out wrong.
11. **With a DLC on, a random credits** (droha: "i think the DLCs have their
   own endings. if they are enabled it should randomize which credits is
   played at the end"). They do: the game's level dump (DevTools, `isCredits`)
   has three - `Credits` (84), `DLC1 Credits` (1129) and `DLC2 Credits`
   (1233, `Assets/Levels/DLC/DLC2 Credits.prefab`). `Track.CreditsLevel`
   takes the first `IsCredits` level, so every run ends on the base one. A
   design: the apworld picks one per seed among the base and the enabled
   DLCs' (world.random, named in the spoiler) and sends it in slot_data;
   the mod launches that level as the finale, the base one when the key is
   absent (older seeds). The rest of the credits handling goes by
   `IsCredits`, not by index. **Built in source**: `pool.pick_finale` (its
   own RNG, seeded per world, so no draw moves; tests in test_dlc),
   slot_data `credits`, `SlotData.Credits`, `Track.CreditsLevel`, and the
   gate clicks the seed's finale (`FINALE_INDEX`). The first in-game try (a
   10-puzzle both-DLC seed picking DLC2 Credits) ended on the base credits:
   `Track.Begin` rebuilds the track before Plugin sets its seed, so reading
   `Plugin.Seed` there found none and cached 84. Begin now takes the finale
   from the slot it is handed; and a DLC's credits are not in
   `LevelManager.LevelInterfaces`, so the finale is found by id
   (`GetLevelInterface`). **Checked in game** (Steam on, a 10-puzzle
   both-DLC seed picking DLC2 Credits): the last card is DLC2 Credits, it
   plays and the goal is reported; the pause menu's Levels during it -
   the exit both players took in the 0.4.3 run - ends it through
   Credits.CreditsComplete and lands on the run's track, Close button and
   all. Its natural end was not seen: after 8 minutes it was still rolling
   ("GAME DESIGN"), and the game's credits have scattered objects
   (`CreditsScatterObject`), so they may wait on the player.
   **The natural end, seen** (2026-09-29, nothing touched, the window
   unfocused but `KeepRunningWhenUnfocused` on): DLC1 Credits rolled about
   three minutes and ended by themselves - `Credits.CreditsComplete`, "the
   credits have no next level - back to the track", "the credits -> the
   run's track" - and DLC2 Credits after 217 s, onto the run's track with
   its Close button and no title under it. `timescale:` does not hurry
   them: `Credits` keeps its start on the audio clock (`StartDSPTime`),
   which no time scale moves. The 8 minutes above were most likely the
   game's own pause while unfocused.
   Seen on the way, and not DLC's: the finale's completion row - what makes
   its card playable - is written only when the track rebuilds. Credits that
   become playable while the player sits on the level select (the puzzle
   count met by a location the server sent, after the Credits item had come)
   leave the card selecting but not starting until the menu is reopened. A
   player's last puzzle or last item comes with a rebuild; a server-sent
   location or a collect does not.
   **The card: done** (2026-09-29). The row is written by
   `Track.ApplyUnlocks` alone - the run's start, a pack, a menu opening -
   and a rebuild does not call it, so the Credits item's own redraw missed
   it too. `Credits.Tick`, which polls the goal every 2 s, now calls
   `Track.OpenFinale`, which writes the row and redraws once when the goal
   is met and the item held. And `ApplyUnlocks` read the goal from
   `Plugin.Seed`, which `Track.Begin` runs ahead of - no seed, so
   `Remaining` was 0 and the card drew open while puzzles were still owed -
   so it now asks only once Checks holds this run (`CreditsPlayable`).
   In game, a 10-puzzle both-DLC seed ending on DLC1 Credits: the old build
   left the card locked after the server met the count and two real clicks
   did nothing; the new one logged "track: the credits are playable - their
   card is open" before the unlock toast and the next real click started
   them. Holding Credits with the count unmet, the card stayed locked, and
   stayed so across a relaunch.
12. **On a symmetric Books seed the Swapping lock does not hold** (the same
   hand test). At load "abilities: 1 locked, 0 open, 11 objects, waiting on
   Swapping", then "1 locked, 1 open": the second controller, Draggables,
   needs no ability and holds the same 11 books, so they are freed and a
   player without Swapping finishes the puzzle. Nothing is sent early - both
   endings are withheld until Swapping arrives - but the lock means nothing
   on 21 of 40 seeds. A fix would dim Books' Draggables with Shuffle
   (AbilityLocks), tested on every level with a second controller over the
   same objects.
   **Done** (2026-09-29): the lock goes by class, so Core
   `ObjectLock.LockedAs` names the one controller that locks as another
   class - Books (Randomized)'s Draggables as Shuffleables - and
   `AbilityLocks.ApplyOnce` asks `LockClass`. Keyed to that level only:
   Books 3 and Chocolate Bars, the same pair of groups, bypass Swapping in
   the table instead (their drag rule is a real way out), and Core tests pin
   both, plus that each override names a class its level's table holds.
   In game, the run's slot 62 (seed 945554386, only Gadgets held): the old
   build "1 locked, 1 open", 0 of 22 book entries dimmed; the new one "2
   locked, 0 open, 11 objects, waiting on Swapping", 22 of 22 dimmed and not
   interactive; Swapping sent mid-level, "0 locked, 2 open" and every colour
   back. `tools/probe-lock-roundtrip.py` restates the override in its rules;
   it cannot boot a chosen generator seed (under a run the mod gives a
   generator its slot's seed, or a stable one), so the check was the real
   slot. droha's hand test the same day, on that slot: "books greyed out and
   not moveable, and the block is red"; then, Swapping sent, the symmetric
   rule filed Solution: Shuffle 1 ("ended on 'Draggables_0' (as
   'Shuffle_0')") with the retry panel up, and the picture rule filed
   Shuffle 2 and went on to the next puzzle.
13. **Parts never seen to solve, which can hold progression** (item 8's
   audit of 51 recordings). Three parts never solved there and were never
   played in a log: DLC1 Fountain Pens' Containables, DLC1 Junk Drawer 2's
   Shuffleables (in the second player's run, a Hint Page) and DLC2 Material
   Drawers' Drawer Draggables; DLC1 Boss's Drawer part has no play data at
   all. All four are checks on proven levels, so `unproven_locations` does
   not guard them (2026-09-29: `unproven_parts` is empty on all four
   levels). If one never fires it is item 8 again: whatever the seed placed
   there comes only with the goal's release, and a progression item there
   softlocks the run. A hand test each, every ability held: finish the
   level and watch for the part's `PartSolved` line; one that never comes
   is `notALocation`.
   **Done** (droha's hand tests, 2026-09-29, no run, DevTools `boot:`):
   Fountain Pens' Containables (the caps, 12:14:50), Junk Drawer 2's
   Shuffleables (12:16:51) and Material Drawers' Drawer Draggables (with
   the completion, 12:26:56) all fire, and stay checks. None of the three
   is needed for its level's finish: the 2026-09-23 recordings finished
   without doing them, hence "never solved". DLC1 Boss, played whole in
   a fresh launch (Dining Room, Parking Lot, Landscape, Keys, then the
   completion at 12:22:06), never raised a solve for its
   `DrawerController`: its Drawer part is `notALocation` (DLC1 ids
   111 -> 110, new seeds only; every other Boss requirement unchanged,
   `rules.requirements` before and after). The verdicts are in
   `proven-requirements.json`.
14. **The puzzles that lead into credits** (droha, 2026-09-29: "double
   check that the levels that typically auto go to their credits don't
   actually do that, and they follow the normal next level path"). Three
   puzzles end straight into their campaign's credits in the game
   (`preventRetryMenu`, docs/reference/level-endings.tsv): Tupperware
   Tower (83 -> Credits 84), DLC1 Boss (1128 -> DLC1 Credits 1129) and
   DLC2 Boss (1231 -> DLC2 Credits 1233). The run answers GetNextLevelIndex
   with its own next slot, and with nothing playable leaves the game's
   answer - a credits level - alone, so a finale that reached the credits
   some other way would have played them. Checked 2026-09-29, each on a
   seed with it in the opening pack, finished in a run: DLC1 Boss (forced,
   `solve:`) and DLC2 Boss (`complete`) went to the next run puzzle with
   others playable, and with every location in the run sent from the
   server - the credits unlocked meanwhile - to the level select by the
   post-level Level Select, the DLC's own select turned into the run's
   track by `DlcGuard`. No credits level started on any of the four.
   Tupperware Tower cannot be finished by forcing (every controller solved,
   no completion; `complete` does nothing either), so it was droha's hand
   test: "it loads to the next level/level select correctly" (2026-09-29;
   on to the next puzzle with others playable, as in droha's hand test of
   2026-09-28, item 7 of the 0.4.2 playtest).

## Built 2026-09-27 and 2026-09-28, in 0.4.3

Each is in the CHANGELOG's 0.4.3 section, with how it was checked.

- **Achievements as checks** (droha, 2026-09-25; built 2026-09-28): the
  `achievements` option, off by default, 17 puzzle achievements
  (`AchievementChecks` in Core says which and why the rest are left out;
  Sweep and Path of Destruction went the same day, being part checks the
  run already has, and Keep Away, which never fired in a run with its
  condition met). Ones the Steam profile holds are earnable too: the mod
  clears the game's in-memory "achieved" flags at each slot entry.
  Checked in game on Eggs (withheld without Ordering, filed when it arrived).
  All 17 since seen firing in a run (hand test, 2026-09-28): droha's
  achievements seed (130 puzzles holding all of them) filed 14 as real
  checks, among them I'll Take My Water Neat and Harmonized Purr, held on
  droha's profile, once the held flags were cleared; Exacting Eggs,
  Unstable Stacker and Now You're Playing With Power fired on their puzzle
  in earlier runs. A missed one is retried by opening the finished card
  (Place Setting reopened as the same slot). Since 2026-09-29 they can
  hold progression (droha: "then what's the point of enabling them?"):
  a card's star counts them and a Skip sends them.
- **Option ranges and defaults** (droha, 2026-09-28): every range measured at
  and past its ends; `puzzle_count` 10 to 130 (130 puzzles checked in game:
  146 cards, the overview strip at 0.59 of its 0.55 floor and now centred),
  `pack_size` 5 to 20, `guaranteed_open_slots` 4 to 20 and 5 by
  default, `starting_abilities` 0 to 13, `mechanic_coverage` 0 to 10,
  `skip_count` 0 to 50; Cat Traps 15%, Background Change Traps their own 15%,
  the rest of the filler split between Hint Pages and tokens; the "79" repeats and
  "five chapters" gone from `player.yaml` and the tooltips. Hints name the
  track section ("Pack 2, puzzle 4").
- **The release gate brought up to date**: features read from `Plugin.cs`,
  pack size 5, `predict_gate.py` on the gate's own pre-flight.

- **Nothing playable: the level select, never a blocked puzzle** (playtest item
  1; droha, 2026-09-26). Core `SlotPicker`; the arrow, the daily guard and a
  finished puzzle with nothing playable all go to the track by way of the title.
  After a puzzle it now goes straight there (item 3 of the 0.4.3 run).
- **Solutions judged by the part their ending names** (playtest item 2): Spoons,
  Coins 1, Figurines file without the abilities their ending does not use.
- **The cursor after Tupperware Tower** (playtest item 7): droha's hand
  test (2026-09-28) finished the Tower in a run; the mod moved on without the
  panel and the cursor was there on Tacks (DevTools `cursor`: active,
  alpha 1). `CursorGuard` never had to act.
- **Media Cabinet without Drawer** (playtest item 9): droha's hand test
  (2026-09-28) finished it holding only Ordering and Stacking, as the second player did;
  Drawer is bypassed (proven-requirements.json has both).
- **Fixed ending names, one check per ending, Medicine Cabinet per colour**
  (playtest items 3-5, droha 2026-09-28), reviewed by droha with every
  ending seen (the fourteen unseen ones played by hand): Medicine
  Cabinet's "Red Items" confirmed; Mirror's big items in place are "Still
  Life" and its little things are folded into the Solution ("for mirror
  just fold the little things into the solution"); generated puzzles got
  fixed endings too. Every ending as built:
  `docs/reference/ending-names-review.md`. The gate's paper plan knows
  which ending forcing files (`fixtures/forced-endings.tsv`).
- **Title menu drawn under the level select** (playtest item 6): reproduced;
  the Levels press now waits for the title to be the active menu, with a
  backstop that takes a leftover title down.
- **Filled icons on cards in packs not opened** (playtest item 8): reproduced
  on Pack 5 and fixed; the per-card rule could not place a card while the track
  was being built.
- **The overview strip's drag stopped short of both ends** (droha,
  2026-09-28, the 130-puzzle achievements seed): the Unity `Scrollbar` and
  its handle live on 'Levels Overview Scrollbar' itself, 3037 wide on the
  1920 screen, while the fit scaled only its dots (DevTools `uitree`).
  `FitStrip` now narrows the bar's width by the same factor; droha dragged
  it end to end: "that's way better".
- **The retry panel's pop-up when a finished puzzle moves on** (droha,
  2026-09-27): not shown any more; the panel's own arrow is used unseen.
- **Background Reset Token** (droha, 2026-09-25), **YAML weights** (the
  fixed-layout DLC levels roll under their DLC's weight; player.yaml quotes
  the shares), **Steam Cloud** (the mod's files moved into
  `<save folder>/Archipelago/`; Steam now finds only `save1.json`).

## Known, not built

- **The Seeing Stars Boss files two of its endings late.** Its endings are
  its phases: `DLC2Boss_Lock` and `DLC2Boss_Compass` are recorded in the
  save as those phases are solved, and only `DLC2Boss_Knife` comes with the
  level's completion (droha's hand test, 2026-09-28: 3 of 3 found in one
  playthrough, one check filed). The other two are filed when the mod next
  reads the save for that slot. Filing them as each phase is solved would
  need the game's phase event hooked; not built.
  **Filed at the finish now** (2026-09-29): `Checks.OnLevelCompleteEarly`
  reads the slot's save again (`SeedSolutionsFromSave`, leaving the id
  being completed to its own filing), so what the save recorded during
  play is filed before the retry panel counts the stars. Checked in a run
  with the two phase endings written into the save the game's way (DevTools
  `marksolved:1231:DLC2Boss_Lock`, then `..._Compass`) and the Boss
  finished with `complete`: "Solution: Lock was earned earlier in this play
  but never filed - sending it now", the same for Compass, Knife from the
  completion, and "3 of 3 solution(s) in ... -> next, without showing the
  panel". Still per phase only at the finish, not as each phase is solved.

## Open, waiting for a recurrence or the game

- **A boot's teardown line does not prove the old level is gone.** In
  the item 13 hand tests (2026-09-29) DevTools `boot:1128` replaced an
  unfinished Junk Drawer 2 and printed its teardown ("tearing down 'DLC1
  Junk Drawer 2 Interface(Clone)'", "destroying orphan level"); the
  Boss's first solve then threw `NullReferenceException` in
  `LevelInterface.CheckWinCondition` (Unity's Player.log) and the Boss
  stopped there - its drawer never closed for the second key (droha:
  "in drawer with keys it's frozen?"). release-testing.md has the case
  where no teardown line prints; this one had it. The same level in a
  fresh launch played clean, which is why a hand test gets a fresh launch
  per level (CLAUDE.md, section 5). Harness only: a player never boots.

- **Books (Randomized) finishing on its Draggables rule** (0.4.2 playtest;
  investigated 2026-09-28). Not a stray controller: on symmetric seeds the
  generator adds a second controller, `Draggables`, over the same books, and
  it checks the puzzle's second solution (`SolutionId` 1; `gensweep:40:995`,
  21 of 40 seeds) - the table lists only `Shuffle`, hence `CONTROLLER
  MISMATCH`. Its ending `Draggables_0` is real: the symmetric rule, first
  or second as the seed puts it, and now files as that rule (item 9 of the
  0.4.3 run, `Endings.Canonical`). Whether any
  finish came without the player moving a book is unproven: the logs carry
  no timestamp between the level opening and the finish (the second player's none at all),
  and 100 directly generated layouts (52 with the rule), the daily-guard
  route, a card click, locks on, and a level left alive underneath never
  finished on their own. If it recurs, note the time the level opened.

- **A forced finish relaunches a generator (harness only).** In every gate
  since at least 0.4.2's (release gate 2026-09-27 17:25 and both of
  2026-09-28), a generator the harness finishes with `solve:` goes straight
  on and, about a second later, the mod relaunches the SAME slot with its
  seed ("checks: now playing slot 1" after "navigation: next -> slot 2").
  Players do not see it: across both 0.4.2 playtest logs, 90 of 93
  straight-on finishes went to the Daily page and the daily guard opened the
  next slot. The harness boots each next level itself, so only the arrow
  check could notice, and it no longer starts on a generator. It also hits
  mid-solve on a seeded level with two controllers (DLC1 Trophy Cabinet, DLC
  gate 2026-09-28), which the harness now refunds. Unexplained: what calls
  StartLevel with the level's own index. Not the item alone, not `boot:`,
  not KeepRunningWhenUnfocused: the same seed served by hand, Stamps opened
  from the track or booted, forced, a Hint Page arriving - it went on to the
  next slot through the daily guard every time (2026-09-28).

- **The arrow session once solved Post-It Notes and got no completion.**
  Base gate, 2026-09-24 13:34: "the level moved on (1 controller(s), 1
  solved)", then "waiting for the completion", so both arrow checks failed.
  The same seed and slot passed in the runs before and after it, and in
  `--only-arrow`. That passing log shows the mod launching the slot twice
  with forceReload (13:36); a solve that lands on the first copy would be
  lost with it. Not supported so far: in the 103 kept logs, 215 of 1521
  boots launched twice and none had a solve between the two launches. The
  failing session's log was overwritten by the next launch; the gate now
  keeps it (`e2e-<stamp>-arrow.log`), so the next failure can be read.
  Possibly the same cause as DLC2 Broken Vases the same day, whose
  completion came 13 s after the solve against a 6 s wait; the wait
  (`COMPLETION_WAIT`) is now 30 s. If it recurs, it was not that.
- **A gate run can stop receiving game events for good.** Three runs on
  2026-09-24: base 10:24 (mid TupperwareNesting), DLC 14:43 (DLC1 Boss)
  and DLC 22:31 (DLC1 Sewing Box, stopping the gate at visit 18 of 25).
  From one moment on, solves set their flags but no PartSolved, check or
  completion arrived, and the next boot found timeScale 0 in the middle of
  a level. 4 of the 364 boots in that day's kept logs. What caused it is
  NOT known: the logs did not record it. Measured on one level: the game's
  own `GameManager.Pause(true)` holds every event, a clock reset does not
  release them and `Pause(false)` does. So `boot` now undoes that pause,
  DevTools logs every `Pause` call and focus change (`game:`) and every
  clock change (`time:`), and the gate warns for a solve sent while paused.
  The trace cannot name the caller (IL2CPP's stack walk returns 0 frames).
  Measured 2026-09-25: the title pauses the game on its own (pause-menu
  Exit, `replayselect` then `menu:title`) and `boot` finds and undoes that.
  Tried 8 times (two arrow sessions, three replays each of 22:31 and 14:43),
  seen 0 times. Then caught by the logs in the DLC gate of 2026-09-25
  (`e2e-20260925-101921.log`, visit 23): a Cat Trap reset DLC1 Craft
  Supplies, the game called `Pause(true)` itself (clock 0), the harness's
  four solves raised nothing, and about 8 s later the game's own
  `Pause(false)` released them all at once - the level completed. A reset
  that never unpaused would look exactly like the stops above; not seen.
- **Two level selects on screen after a DLC level.** droha, watching the
  DLC gate on 2026-09-24: "there was 2 level selects open at the same time
  there for a minute". The log: after the post-level Level Select, DlcGuard
  logs "opening the run's track"; the harness presses the level select's
  Close Button, the game heads back to the DLC's own select, and DlcGuard's
  Tick opens the run's track a second time. Not new: every kept DLC gate log
  since 2026-09-17 shows two guard openings per Close. DlcGuard.cs's own
  comment records the same double render on 2026-09-18, fixed then for the
  post-level route only. A player pressing Close there likely sees it too.
  **The Close half is fixed** (2026-09-24): Close from the run's track now
  goes to the title, checked in game twice. **Still open:** the post-level
  route still builds the DLC menu for a moment before the guard leaves it.
  Known: `ReplayMenu.LevelSelect` reads `IsDLCLevel` itself (DevTools
  `xrefs:ReplayMenu.LevelSelect`; the scan froze the game on its eighth
  call, so the rest of that list is unread). Not the route: a
  `GoToLevelSelectForLevel` prefix and a `ContextualState` postfix both
  installed and never ran on it. About 20 screenshots of that route and of
  Close, taken before the fix at up to two a second, never caught the two
  menus on screen together.
  Measured 2026-09-25 in a hand-test run: 18 post-level Level Selects on DLC
  puzzles all entered the DLC menu first (one guard opening each), 0
  exceptions; once, after DLC1 Boss, the track was built twice and BOTH
  level selects stayed on screen with clicks going nowhere - the first
  screenshot of it. Tried and reverted: blanking the finished level's
  `DLCDetails` for the length of `ReplayMenu.LevelSelect` (restored in a
  finalizer) - the DLC menu still opened. With the IsDLCLevel postfix that
  also changed nothing, the route is decided outside that call or by
  something the pointer scan (`xrefs:...|...`) cannot reach: it kills the
  game at the method's eighth reference. Not known what decides it.
  NOT this item: the DLC gates of 2026-09-27 opened the track twice after
  12 of 13 puzzles and then threw in `menu:title`, but that was the gate's
  own timing - `replayselect` sent while the retry panel was still coming
  in - fixed in the harness (`settle_post_level`).

## Seen, not fixed (visual only)

- **Seed Pods (65), Clover (68):** the game tints its pieces to 0.9 once,
  after the level's intro, and only the ones not locked then. A piece locked
  through the intro comes back at full white, 10% brighter than its
  neighbours.
- **Mirror (79)'s candle, Ghost Cat (1239)'s cats:** their animations repaint
  them every frame, so they show in colour while locked. They are left as the
  animation has them on unlock.
- **Robots (1230)'s little robot on a line:** grey with every ability
  withheld, but it moves, and its antennas toggle when clicked; nothing in the
  level can be solved that way, so no check comes early.

## Ideas, not asked for

- **A level's own cat for the Cat Trap.** On the four `CatGrab` levels (Place
  Setting, Shells, Stamps, MerryMess_Crackers), call the game's `DoGrab()`
  before the reset instead of the overlay paw - cosmetic, since the reset does
  the work. Unchecked: whether `DoGrab()` is safe out of sequence, and whether
  the animation survives the rebuild. `history/cat-trap-tests.md` has the
  survey.
- **Steam Cloud while testing:** harness runs that change `save1.json` outside
  a Steam launch still make Steam report a conflict; unticking "Keep game saves
  in the Steam Cloud" for the game while testing stops it.
