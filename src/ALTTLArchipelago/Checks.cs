using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;

namespace ALTTLArchipelago;

/// <summary>
/// Turns solving things into Archipelago checks.
///
/// Three signals matter, and each has a trap in it:
///
/// - GameEvent_ObjectControllerSolved fires when one controller is finished.
///   It fires REPEATEDLY - sixteen events across thirteen controllers, measured
///   - so it must be deduped or the same location goes to the server over and
///   over.
/// - GameEvent_LevelComplete carries a SolutionId. The ids are internal strings
///   the generator never sees, so solutions are counted ordinally: the Nth
///   DISTINCT id found on a slot is that slot's Nth solution location.
/// - Which slot the player is in cannot be read off the level, because a level
///   can occupy several slots. It is remembered from the card they launched.
///
/// Nothing here decides what a location IS - CheckRouter does, from the
/// generator's own tables. This file is the Unity half only.
/// </summary>
internal static class Checks
{
    private static CheckRouter? _router;
    private static SlotProgress? _progress;
    private static SlotData? _slot;
    private static CheckLedger _ledger = new();

    /// <summary>Which slot the player is currently inside, or -1 in a menu.</summary>
    private static int _currentSlot = -1;

    /// <summary>
    /// Which solution number each completion is. The mapping lives in Core,
    /// where it is tested; this file only feeds it the game's events.
    /// </summary>
    private static readonly SolutionOrdinals _solutions = new();

    /// <summary>
    /// How many times each (slot, controller) has solved with no location
    /// behind it, so the line is said once instead of once per frame.
    ///
    /// In the 0.3.0 playtest log this accounted for 723 of 736 such lines:
    /// 'Constellations' on slot 28 raised its solved event 362 times and on
    /// slot 11 361 times. Some controllers re-raise every frame while they sit
    /// in their solved state. That is harmless - the location is already
    /// collected - but it buried the eleven lines that were real, and those
    /// are the ones that say a controller map is wrong.
    /// </summary>
    private static readonly Dictionary<string, int> _unrouted = new(StringComparer.Ordinal);

    /// <summary>(slot, controller) already said to be waiting on its group, so a re-raise stays quiet.</summary>
    private static readonly HashSet<string> _waitingOnGroup = new(StringComparer.Ordinal);

    /// <summary>
    /// The IL2CPP side holds listeners through a weak wrapper, so a delegate
    /// that is not rooted on the managed side stops firing at the first GC.
    /// This list is the entire reason the subscriptions keep working.
    /// </summary>
    private static readonly List<Il2CppSystem.Action<GameEventManager.GameEventData>> KeepAlive = new();

    private static bool _attached;


    internal static CheckLedger Ledger => _ledger;
    internal static SlotProgress? Progress => _progress;
    internal static CheckRouter? Router => _router;

    /// <summary>The slot being played, or -1 in a menu.</summary>
    internal static int CurrentSlot => _currentSlot;

    /// <summary>
    /// How many puzzles have been beaten, counted from what we have collected.
    ///
    /// NOT from received items: the Level Beaten token rides on an event
    /// location, so the server never delivers it and counting arrivals gave
    /// zero forever - the credits could never unlock.
    /// </summary>
    internal static int LevelsBeaten
        => _router?.BeatenCount(_ledger.IsCollected) ?? 0;

    /// <summary>
    /// How many stars the run has lit: solutions found, the stars the level
    /// select counts (droha, 2026-09-28: "it's number of solutions"). The
    /// cards' hover stars summed (CheckRouter.RunStars), so the counter and
    /// the cards cannot disagree. From the ledger, like LevelsBeaten.
    /// </summary>
    internal static int StarsCollected
        => _router?.RunStars(_ledger.IsCollected).Lit ?? 0;

    /// <summary>
    /// Progress toward whichever goal this seed set, as done and needed.
    ///
    /// ONE PLACE, because there were three. The credits gate, the goal
    /// counter on the level select and the offline summary each read
    /// LevelsBeaten against LevelsToBeat directly; adding a second goal to
    /// three call sites is how two of them end up telling the player a
    /// different number. (The "Puzzle beaten x/n" toast is gone: droha,
    /// 2026-09-25, the count is on the level select already.)
    /// </summary>
    internal static (int Done, int Needed, string Unit) GoalProgress(SlotData? slot)
    {
        if (slot == null) return (0, 0, "beaten");
        return slot.GoalIsStars
            ? (StarsCollected, slot.StarsToCollect, "stars")
            : (LevelsBeaten, slot.LevelsToBeat, "beaten");
    }
    internal static bool Active => _router != null;

    /// <summary>
    /// Was this the slot Checks was begun with? The goal count read before
    /// then is the last run's, or nobody's (Track.CreditsPlayable).
    /// </summary>
    internal static bool IsFor(SlotData slot) => _router != null && ReferenceEquals(_slot, slot);

    internal static void Begin(SlotData slot)
    {
        _router = new CheckRouter(slot);
        _progress = new SlotProgress(slot, _router);
        _slot = slot;
        _ledger = new CheckLedger();
        _solutions.Clear();
        _unrouted.Clear();
        _waitingOnGroup.Clear();
        _achievementsNotChecks.Clear();
        _currentSlot = -1;
        // A new run gets a fresh chance to complain about its own table.
        _groupsWarnedFor = "";
        Attach();
    }

    internal static void End()
    {
        _router = null;
        _progress = null;
        _slot = null;
        _currentSlot = -1;
        _solutions.Clear();
    }

    /// <summary>
    /// What the server says we have already checked - at login, offline from
    /// the cache, or mid-session. A slot with a Solution in counts as beaten
    /// (CheckRouter.BeatenBySolutions), so a Solution sent by anyone files the
    /// slot's Beaten event too.
    /// </summary>
    internal static void AdoptServerChecks(IEnumerable<string> names)
    {
        _ledger.AdoptServerChecks(names);
        if (_router == null) return;
        foreach (var beaten in _router.BeatenBySolutions(_ledger.IsCollected)) Report(beaten);
    }

    /// <summary>The player just launched this slot's card.</summary>
    internal static void EnterSlot(int slotIndex)
    {
        _currentSlot = slotIndex;
        _auditedCount = 0;
        _pendingSlot = -1;

        // Any skip in flight belongs to the level we just left. One that never
        // landed is dropped here, uncharged.
        Skips.Flow.Entered(slotIndex);

        // Same reasoning for the hint refusal toast: it is latched so that
        // dragging the eraser does not produce a wall of them, and the latch
        // belongs to the notepad we just left.
        Hints.LevelStarted();

        // Recolour here rather than in a StartLevel postfix, which is where it
        // was and did not work. Measured: after that postfix the level's own
        // setup still runs and writes Camera.main.backgroundColor from the
        // level's colour, so our value was overwritten a moment later. The
        // giveaway was that LevelInterface.BackgroundColor read back as the
        // colour we asked for while the camera - the thing that actually
        // renders - still held the level's own.
        Backgrounds.ApplyToLevel();

        SeedSolutionsFromSave(slotIndex);
        FileWithheld();

        // Before any checker sees the level: one the Steam profile holds must
        // still be earnable.
        SteamAchievements.ForgetUnlocks();

        Plugin.Logger.LogInfo($"checks: now playing slot {slotIndex}");
    }

    /// <summary>
    /// The running level is no open slot of this run, so nothing earned on it
    /// is filed. Kept, the slot played before it took its checks: finishing
    /// Wilting Flowers, which the seed does not contain, straight after Fruit
    /// Stickers filed "Fruit Stickers - Solution 2" (DevTools boot, 2026-09-26).
    /// </summary>
    internal static void LeaveSlot(string what)
    {
        if (_currentSlot < 0) return;
        Plugin.Logger.LogInfo(
            $"checks: {what} is not an open slot of this run - left slot {_currentSlot}, nothing on it is filed");
        _currentSlot = -1;
        _auditedCount = 0;
        _pendingSlot = -1;
    }

    /// <summary>
    /// The arrangements a save list records for a level, or null if the list
    /// does not mention it at all.
    ///
    /// Null rather than an empty list on purpose: "this level is not in this
    /// list" and "this level is in this list with nothing found" are different
    /// answers, and only the first should send the caller on to the other list.
    /// </summary>
    private static List<string>? SolutionIdsFor(
        Il2CppSystem.Collections.Generic.List<SaveData.LevelCompletionData>? all,
        string levelId)
    {
        if (all == null) return null;

        for (int i = 0; i < all.Count; i++)
        {
            var entry = all[i];
            if (entry == null || entry.levelId != levelId) continue;

            var ids = new List<string>();
            var solutions = entry.solutions;
            for (int k = 0; solutions != null && k < solutions.Count; k++)
            {
                var one = solutions[k];
                if (one != null) ids.Add(one.solutionId ?? "");
            }
            return ids;
        }
        return null;
    }

    /// <summary>
    /// Hand over solution checks the player earned but never received.
    ///
    /// SEEDING ALONE WOULD HAVE MADE THE BUG PERMANENT. Restoring the ordinals
    /// stops a relaunch re-filing Solution 1, but on a run that already lost
    /// checks it also means every arrangement is now "seen", so Record returns
    /// 0 forever and the missing locations can never be filed. droha's Snow
    /// Globes had all three arrangements found and one check banked; the fix
    /// for future sessions would have frozen the other two out for good.
    ///
    /// Finding N distinct arrangements earns Solutions 1 to N. That is the
    /// whole rule, and it is true regardless of which session each was found
    /// in, so anything short is simply owed.
    ///
    /// Reachability is still respected, exactly as SweepAlreadySolved does it:
    /// a solution the run cannot currently reach stays uncollected rather than
    /// handing out an item the logic says is not earned. It will be filed on a
    /// later visit once the ability arrives.
    /// </summary>
    private static void FileSolutionsAlreadyEarned(int slotIndex, string when = "in an earlier session")
    {
        if (_router == null || _progress == null) return;

        var abilities = Inventory.Abilities;
        if (abilities == null) return;
        var packs = Track.State?.PacksHeld ?? 0;

        var ids = _solutions.IdsFor(slotIndex);
        foreach (var id in ids)
        {
            // The ending each id names - the same answer at every launch.
            var location = _router.ForEnding(slotIndex, id, ids);
            if (location == null || _ledger.IsCollected(location)) continue;
            var needs = _router.PartsForSolution(slotIndex, id);
            if (needs.Count > 0 ? !AllWouldEarn(needs)
                                : !_progress.IsReachable(location, packs, abilities)) continue;

            Plugin.Logger.LogInfo(
                $"checks: {location} was earned {when} but never "
                + "filed - sending it now");
            Report(location);
        }
    }

    /// <summary>
    /// Tell the ordinal counter which arrangements this slot already found.
    ///
    /// The counter is what turns a completion into a location: first new
    /// arrangement files "Solution 1", second files "Solution 2". It lived only
    /// in memory, so every relaunch restarted it at 1 and re-filed a location
    /// that was already collected. droha found all three arrangements of Snow
    /// Globes across two sessions; the game recorded 3 of 3 and the server had
    /// one check, with the other two quietly dropped.
    ///
    /// Read from the game's save rather than from the ledger on purpose. The
    /// ledger knows HOW MANY solution locations are collected, but not WHICH
    /// arrangements produced them, so it cannot tell a repeat of an old
    /// arrangement from a genuinely new one - and treating a repeat as new
    /// would hand out a check the player has not earned. The save stores the
    /// solutionIds themselves, which is exactly the question being asked.
    ///
    /// Called on every slot entry. Seed skips ids it already holds, so the
    /// repeat is free.
    ///
    /// AND AT EVERY COMPLETION (OnLevelCompleteEarly), with `completing` the
    /// id being finished, which is left to OnLevelComplete's own filing. The
    /// Seeing Stars Boss's endings are its phases: DLC2Boss_Lock and
    /// DLC2Boss_Compass reach the save as those phases are solved and only
    /// DLC2Boss_Knife comes with the completion, so one playthrough found all
    /// three and filed one until the next visit (droha's hand test,
    /// 2026-09-28). Read here, they are filed before the retry panel is
    /// decided. AND AFTER EVERY SAVE WRITE during play (TickSaveWritten), so
    /// each phase is filed as it is solved.
    /// </summary>
    private static void SeedSolutionsFromSave(int slotIndex, string? completing = null)
    {
        if (_slot == null || slotIndex < 0 || slotIndex >= _slot.Slots.Count) return;

        try
        {
            var levelId = _slot.Slots[slotIndex].LevelId;
            var data = SaveSystem.data;
            if (data == null) return;

            // BOTH LISTS. The save keeps campaign and archive progress apart,
            // and 26 of the 111 levels a seed can draw are archive levels - a
            // quarter of the pool. Reading only levelCompletionData found
            // nothing for Snow Globes and quietly did nothing, which looked
            // exactly like the fix working.
            var ids = SolutionIdsFor(data.levelCompletionData, levelId)
                      ?? SolutionIdsFor(data.archiveCompletionData, levelId);
            if (ids == null) return;
            if (completing != null) ids.RemoveAll(id => id == completing);

            var during = completing != null;
            var added = _solutions.Seed(slotIndex, ids);
            if (added > 0)
            {
                // "will file Solution 4" on a 3-solution level read like an
                // off-by-one (the second player's log, Figurines); say when none are left.
                var have = _solutions.CountFor(slotIndex);
                var total = _router == null ? 0 : _router.SolutionStars(slotIndex, _ => false).Item2;
                var next = total > 0 && have >= total
                    ? $"all {total} are found"
                    : $"the next new arrangement will file Solution {have + 1}";
                Plugin.Logger.LogInfo(during
                    ? $"checks: slot {slotIndex}'s save recorded {added} more solution(s) "
                      + $"during this play; {next}"
                    : $"checks: slot {slotIndex} already had {added} solution(s) "
                      + $"found in an earlier session; {next}");
            }

            FileSolutionsAlreadyEarned(slotIndex, during ? "earlier in this play" : "in an earlier session");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning(
                $"checks: could not read earlier solutions for slot {slotIndex}: {e.Message}");
        }
    }

    /// <summary>
    /// How many controllers the last audit compared against.
    ///
    /// NOT a bool any more, and that is the fix. It used to be `_audited`, set
    /// true after the first successful audit, so the comparison ran ONCE about
    /// a second and a half after a level opened - and a level that reveals its
    /// controllers PHASE BY PHASE has revealed almost nothing by then.
    ///
    /// TupperwareNesting registers 2 controllers at open and 7 by the time the
    /// player has worked through it. The one-shot audit saw the 2, agreed with
    /// the table, and said nothing; the five that appeared later were never
    /// compared, so five groups the player could solve had no location behind
    /// them and no warning was raised until the mismatch happened to be caught
    /// by hand.
    ///
    /// Re-auditing whenever the count GROWS turns ordinary play into the
    /// survey. It also distinguishes a phased controller from a prefab-only
    /// ghost for free: a ghost never registers, so it never appears here.
    /// </summary>
    private static int _auditedCount;

    /// <summary>
    /// The level whose missing controller_groups entry has already been
    /// reported, so a phased level does not repeat it on every reveal.
    /// </summary>
    private static string _groupsWarnedFor = "";

    private static float _sinceAudit;

    /// <summary>Levels whose mismatch has already been reported, so it is said once.</summary>
    private static readonly HashSet<string> _mismatchesReported = new(StringComparer.Ordinal);


    /// <summary>
    /// Collect the groups that were ALREADY solved when the level opened.
    ///
    /// The game raises GameEvent_ObjectControllerSolved when a group BECOMES
    /// solved. A group that is already in its finished arrangement when the
    /// level loads never raises it, so nothing ever filed its check - and no
    /// amount of playing could, because there was nothing left to do to it.
    ///
    /// That is what "half green and half red when there's nothing to do" looks
    /// like from the level select. SlotProgress calls a card Mixed when some
    /// remaining locations are reachable and some are not; an already-solved
    /// group counts as reachable-and-uncollected forever, so the card kept
    /// advertising work that did not exist. The badge was telling the truth
    /// about a ledger that was wrong.
    ///
    /// GUARDED ON REACHABILITY, deliberately. A group can read as solved while
    /// the ability that governs it is still locked - its objects are dimmed,
    /// not rearranged - and filing that check would hand out an item the logic
    /// says has not been earned. Anything blocked stays uncollected, which
    /// makes the card honestly Locked rather than falsely Mixed.
    /// </summary>
    private static void SweepAlreadySolved(
        Il2CppSystem.Collections.Generic.List<ObjectController> registered)
    {
        if (_router == null || _progress == null || _currentSlot < 0) return;

        var abilities = Inventory.Abilities;
        var packs = Track.State?.PacksHeld ?? 0;
        if (abilities == null) return;

        var filed = 0;
        var blocked = 0;
        for (int i = 0; i < registered.Count; i++)
        {
            var oc = registered[i];
            if (oc == null) continue;

            bool solved;
            try { solved = oc.IsSolved; }
            catch { continue; }
            if (!solved) continue;

            var name = oc.gameObject?.name ?? "";
            if (name.Length == 0) continue;

            var location = _router.ForController(_currentSlot, name);
            if (location == null || _ledger.IsCollected(location)) continue;
            if (!GroupDone(name, registered, null)) continue;

            if (!_progress.IsReachable(location, packs, abilities))
            {
                blocked++;
                continue;
            }

            Report(location);
            filed++;
        }

        if (filed > 0 || blocked > 0)
        {
            Plugin.Logger.LogInfo(
                $"checks: {filed} group(s) were already solved on load and have "
                + $"been collected, {blocked} left for when they unlock");
        }
    }

    /// <summary>
    /// Compare the controllers the running level REGISTERED against the table
    /// the generator built its logic from.
    ///
    /// This is the one assumption seed generation cannot test. levels.json came
    /// from a runtime sweep, and if the running game registers a controller the
    /// sweep never saw, our logic is looser than reality: the generator may
    /// have assumed a check is reachable when it is not, and the seed can be
    /// unwinnable in a way nothing else will report.
    ///
    /// A poll rather than an event because controllers self-register in their
    /// own Start, so there is no single moment that is reliably "after all of
    /// them". Running late is fine; the answer does not change.
    /// </summary>
    internal static void TickAudit(float dt)
    {
        if (_slot == null) return;

        _sinceAudit += dt;
        if (_sinceAudit < 1.5f) return;
        _sinceAudit = 0f;

        EnsureSlot();
        if (_currentSlot < 0) return;

        try
        {
            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            var level = li?.Level;
            var registered = level?.objectControllers;
            if (registered == null || registered.Count == 0) return;   // not up yet

            // Only when the level has revealed MORE than last time. A level
            // that is not phased settles on its first count and this costs one
            // comparison; a phased one is re-checked at every reveal.
            if (registered.Count <= _auditedCount) return;
            _auditedCount = registered.Count;

            SweepAlreadySolved(registered);

            var levelId = _slot.Slots[_currentSlot].LevelId;
            if (!_slot.ControllerGroups.TryGetValue(levelId, out var known))
            {
                // THE WORST CASE, AND IT USED TO BE THE SILENT ONE.
                //
                // A level with no controller_groups entry cannot match any
                // group the player tidies, so every part location on it is
                // unearnable and its star is unreachable. This audit exists to
                // shout about exactly that kind of table gap - and it returned
                // without a word, so the one case where nothing can ever be
                // collected was the one case that produced no log line at all.
                // A level merely MISSING a few groups got the loud warning.
                //
                // SlotData.Problems() names it at connect, which is now a
                // refusal, so reaching here at all means something got past
                // that - a level swapped in mid-run, or a payload the refusal
                // did not see. Said once per level, because the audit re-runs
                // on every reveal of a phased level.
                if (_groupsWarnedFor != levelId)
                {
                    _groupsWarnedFor = levelId;
                    Plugin.Logger.LogWarning(
                        $"UNEARNABLE LOCATIONS on {levelId}: the table has no "
                        + $"controller_groups entry for it at all, so none of "
                        + $"its group checks can ever be sent");
                }
                return;
            }

            var unknown = new List<string>();
            var live = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < registered.Count; i++)
            {
                var oc = registered[i];
                if (oc == null) continue;
                var name = oc.gameObject?.name ?? "";
                if (name.Length == 0) continue;
                live.Add(name);
                if (known.ContainsKey(name)) continue;

                // No location on purpose (solved at load, or never solved).
                if (_slot.NotLocations.TryGetValue(levelId, out var skip)
                    && skip.Contains(name)) continue;

                // A controller that can never carry a location is not a
                // mismatch, it is scenery. Pannables is the only one - see
                // ControllerTypes, where the count behind that is recorded -
                // and reporting it made the loudest warning in the mod
                // permanent on every level that has one.
                var runtimeType = "";
                try { runtimeType = oc.GetIl2CppType().Name; } catch { }
                if (!ALTTLArchipelago.Core.ControllerTypes.Scores(runtimeType))
                {
                    continue;
                }

                unknown.Add(name);
            }

            // The OTHER direction, which nothing checked before: a controller
            // the table knows about that the running level does not have.
            //
            // This one is worse than an unrecognised controller, because it is
            // silent. Every part location is minted from the table, so a group
            // with no controller behind it can never raise its solved event and
            // can never be collected - the card sits on Mixed or green for the
            // whole run with nothing the player can do about it, and the star,
            // which needs EVERY location on the slot, can never be reached.
            // "Some levels show half green and half red when there's nothing to
            // do" is what that looks like from the level select.
            var missing = new List<string>();
            foreach (var name in known.Keys)
            {
                if (!live.Contains(name)) missing.Add(name);
            }

            // Reported only while the level is still short of what the table
            // expects. On a phased level the early phases legitimately lack
            // most groups, so this would cry wolf at every reveal; it is only
            // interesting once the level has stopped growing, which in practice
            // means it is reported and then withdrawn as later phases arrive.
            // Kept as a warning rather than suppressed, because a genuinely
            // unearnable location looks identical until the level ends.
            if (missing.Count > 0)
            {
                Plugin.Logger.LogWarning(
                    $"UNEARNABLE LOCATIONS on {levelId} (at {registered.Count} "
                    + $"controller(s) so far): the table expects "
                    + $"{string.Join(", ", missing)}, which this level has not "
                    + "registered - if the level is phased they may still appear");
            }

            // NEW ones only. Keyed by controller rather than by level, because
            // a phased level reveals unknown controllers a few at a time and a
            // per-level latch would report the first batch and hide the rest -
            // which is the same mistake as the one-shot audit, one level up.
            unknown.RemoveAll(n => !_mismatchesReported.Add($"{levelId}|{n}"));
            if (unknown.Count == 0) return;

            // Not every registered controller is a puzzle - the table only
            // holds the ones that can be checked - so this is a report, not an
            // error. It is loud because a genuine new controller means the
            // logic and the game disagree about what is solvable.
            Plugin.Logger.LogWarning(
                $"CONTROLLER MISMATCH on {levelId}: {registered.Count} registered, "
                + $"{known.Count} in the table, not recognised: "
                + string.Join(", ", unknown));
        }
        catch (Exception e)
        {
            // Do not re-arm on a throw: mark the current count as audited so a
            // persistent fault cannot log once every 1.5 seconds for the rest
            // of the level.
            _auditedCount = int.MaxValue;
            Plugin.Logger.LogWarning($"checks: controller audit failed: {e.Message}");
        }
    }

    private static float _sinceLevelWatch;
    private static string _watchedLevel = "";
    private static float _emptyFor;


    /// <summary>How often the empty-level watch looks.</summary>
    private const float WatchInterval = 1f;

    /// <summary>
    /// How long a level must stay empty before it is called broken. Long
    /// enough that a slow load is not accused, short enough to still be on
    /// screen while the player is looking at it.
    /// </summary>
    private const float EmptyAfter = 6f;

    /// <summary>
    /// Shout if a level is on screen with nothing in it.
    ///
    /// A level that loads empty looks identical to a level that is still
    /// loading, except that it never recovers and the game stops accepting
    /// input - and there is nothing in the log to say so. That happened in play
    /// and cost a restart to diagnose. This turns a silent hang into one line
    /// naming the level, so the next report is actionable immediately.
    /// </summary>
    internal static void TickEmptyLevelWatch(float dt)
    {
        // Only while a run is on. This watches for a bug in OUR level loading,
        // and it was polling the game every second for the whole process
        // lifetime - through the menus, and through plain vanilla play with the
        // mod idle - reaching three interop properties deep each time.
        if (!Active) return;

        _sinceLevelWatch += dt;
        if (_sinceLevelWatch < WatchInterval) return;

        // Subtract rather than zero, so the period is the interval and not the
        // interval plus however long the frame took.
        _sinceLevelWatch -= WatchInterval;

        try
        {
            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            if (li == null)
            {
                _watchedLevel = "";
                _emptyFor = 0f;
                return;
            }

            // THE CREDITS ARE CHECKED BEFORE Level IS, and the order is the
            // point. This used to read `if (li == null || level == null)
            // return;` above the IsCredits branch, which made reporting the
            // goal depend on an animation having a Level at all - and the
            // comment three lines down already says the credits have no
            // controllers and no level objects. When Level came back null the
            // watch returned before noticing the run had been won, leaving
            // OnLevelComplete as the only path: that fires at the END of
            // several minutes of animation, so the release gate's 60-second
            // wait for the goal timed out with "the mod reported the goal"
            // and "the server agrees the goal is met" both failing while
            // every other check passed.
            if (li.IsCredits)
            {
                _watchedLevel = "";
                _emptyFor = 0f;
                Credits.NotePlayed();
                return;
            }

            var level = li.Level;
            if (level == null)
            {
                _watchedLevel = "";
                _emptyFor = 0f;
                return;
            }

            var id = li.LevelId ?? "";
            if (id != _watchedLevel)
            {
                _watchedLevel = id;
                _emptyFor = 0f;
            }

            var controllers = level.objectControllers;
            var objects = level.allLevelObjects;
            // A chapter card is never an empty puzzle: the title can hold one
            // as the active level after the track was left (base gate,
            // 2026-09-25). See EmptyLevelWatch.
            var empty = EmptyLevelWatch.LoadedEmpty(
                li.LevelType == LevelType.Chapter, li.IsCredits,
                controllers == null ? 0 : controllers.Count,
                objects == null ? 0 : objects.Count);

            if (!empty)
            {
                _emptyFor = 0f;
                return;
            }

            _emptyFor += WatchInterval;

            // Six seconds. Long enough that a slow load is not accused, short
            // enough to be on screen while the player is still looking at it.
            // Once, at the moment it crosses the threshold. The upper bound used
        // to be 6.5, which was unreachable: _emptyFor moves in whole interval
        // steps, so the window was only ever hit exactly.
        if (_emptyFor < EmptyAfter) return;
        if (_emptyFor - WatchInterval >= EmptyAfter) return;

            Plugin.Logger.LogError(
                $"LEVEL LOADED EMPTY: {id} (index {li.LevelIndex}) has no objects "
                + "and no controllers. The game will not accept input here.");
            Toasts.Show($"{id} failed to load - please report this",
                        Toasts.Notice);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"checks: empty-level watch failed: {e.Message}");
        }
    }

    private static void Attach()
    {
        if (_attached) return;
        try
        {
            Listen<GameEventManager.GameEvent_ObjectControllerSolved>(OnControllerSolved);
            Listen<GameEventManager.GameEvent_LevelCompleteEarly>(OnLevelCompleteEarly);
            Listen<GameEventManager.GameEvent_LevelComplete>(OnLevelComplete);
            Listen<GameEventManager.GameEvent_LevelSkipped>(OnLevelSkipped);
            _attached = true;
            Plugin.Logger.LogInfo("checks: listening for solves");
        }
        catch (Exception e)
        {
            // GameEventManager may not exist on the first frames after load.
            Plugin.Logger.LogWarning($"checks: could not attach listeners: {e.Message}");
        }
    }

    private static void Listen<T>(Action<GameEventManager.GameEventData> handler)
        where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(data =>
            {
                // A throw crossing back into IL2CPP is not survivable, so every
                // handler is wrapped here rather than trusted to be careful.
                try { handler(data); }
                catch (Exception e) { Plugin.Logger.LogError($"checks: handler failed: {e}"); }
            });
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    /// <summary>
    /// Make sure _currentSlot names the level that is actually running.
    ///
    /// The card click is the primary signal and the only one that can tell two
    /// instances of the same level apart. But it is not the only way a level
    /// starts - the track can re-centre and select a different card after a
    /// launch, and a level can be started directly - so a check earned in a
    /// level we were never told about would otherwise be silently dropped.
    ///
    /// The fallback picks the earliest OPEN slot for the running level that
    /// still has something left to check. Earliest-with-work-left is the best
    /// guess available: it cannot distinguish two instances, but it never
    /// routes a check to an instance that is already finished, which is the
    /// version of being wrong that loses progress. A level with no open slot
    /// leaves the slot (LeaveSlot), so its checks go nowhere rather than to
    /// the level played before it.
    /// </summary>
    private static void EnsureSlot()
    {
        if (_slot == null || _router == null) return;

        try
        {
            var levelId = GameManager.Instance?.levelManager?.ActiveLevelInterface?.LevelId;
            if (string.IsNullOrEmpty(levelId)) return;

            if (_currentSlot >= 0 && _currentSlot < _slot.Slots.Count
                && _slot.Slots[_currentSlot].LevelId == levelId)
            {
                return;                       // the click already told us
            }

            var track = Track.State;
            var fallback = -1;
            for (int i = 0; i < _slot.Slots.Count; i++)
            {
                if (_slot.Slots[i].LevelId != levelId) continue;
                if (track != null && !track.IsOpen(i)) continue;

                if (fallback < 0) fallback = i;

                var remaining = false;
                foreach (var name in _router.ForSlot(i))
                {
                    if (!_ledger.IsCollected(name)) { remaining = true; break; }
                }
                if (remaining) { fallback = i; break; }
            }

            if (fallback < 0)
            {
                LeaveSlot(levelId);
                return;
            }

            _currentSlot = fallback;
            _auditedCount = 0;
            Plugin.Logger.LogInfo(
                $"checks: playing {levelId} as slot {fallback} (resolved from the running level)");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"checks: could not resolve the slot: {e.Message}");
        }
    }

    private static void OnControllerSolved(GameEventManager.GameEventData data)
    {
        EnsureSlot();
        if (_router == null || _currentSlot < 0) return;

        var controller = data?.ObjectController;
        if (controller == null) return;

        var name = controller.gameObject?.name;
        var location = _router.ForController(_currentSlot, name);

        // Logged either way. A solve that routes nowhere is usually correct -
        // a single-group level has no part location - but it is also exactly
        // what a broken controller map looks like, and the two are impossible
        // to tell apart from silence.
        if (location == null)
        {
            // Said once per (slot, controller), then only when a repeat count
            // crosses a round number - a re-firing controller stays visible
            // without drowning out the ones that fire once. See _unrouted.
            var key = $"{_currentSlot}/{name}";
            _unrouted.TryGetValue(key, out var seen);
            _unrouted[key] = seen + 1;

            if (seen == 0)
            {
                Plugin.Logger.LogInfo(
                    $"checks: '{name}' solved on slot {_currentSlot}, no location for it");
            }
            else if ((seen + 1) % 250 == 0)
            {
                Plugin.Logger.LogInfo(
                    $"checks: '{name}' on slot {_currentSlot} has re-raised "
                    + $"{seen + 1} times with no location - it is re-firing, not re-solving");
            }
            return;
        }

        // A PART IS ITS WHOLE GROUP. Filed on the group's last controller, not
        // its first: Medicine Cabinet's "Red Items" is seven, and placing the
        // cup used to file it (droha, 2026-09-28: red waits for the toothbrush).
        if (!GroupDone(name!, null, name))
        {
            if (_waitingOnGroup.Add($"{_currentSlot}/{name}"))
            {
                Plugin.Logger.LogInfo(
                    $"checks: '{name}' solved; {location} waits for the rest of its group");
            }
            return;
        }

        if (!Earned(location))
        {
            // Remembered, so it can be filed once the run can reach it. The
            // game rebuilds the level unsolved on every load, so without this
            // a later visit has nothing to sweep (see RunStateData.Withheld).
            RunState.AddWithheld(location);
            return;
        }
        Report(location);
    }

    /// <summary>
    /// Is the group this controller belongs to finished (CheckRouter.GroupSolved)?
    /// True for a group of one, and when the level's controllers cannot be read,
    /// so a check is never lost to a read that failed. `justSolved` counts as
    /// solved whatever IsSolved says in the same frame.
    /// </summary>
    private static bool GroupDone(string name,
        Il2CppSystem.Collections.Generic.List<ObjectController>? registered, string? justSolved)
    {
        var members = _router!.GroupMembers(_currentSlot, name);
        if (members.Count <= 1) return true;
        registered ??= GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level?.objectControllers;
        if (registered == null) return true;

        var wanted = new HashSet<string>(members, StringComparer.Ordinal);
        var solved = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (int i = 0; i < registered.Count; i++)
        {
            var oc = registered[i];
            if (oc == null) continue;
            var member = oc.gameObject?.name;
            if (member == null || !wanted.Contains(member)) continue;
            bool done;
            try { done = oc.IsSolved; }
            catch { continue; }
            solved[member] = solved.TryGetValue(member, out var prior) ? prior && done : done;
        }
        if (justSolved != null) solved[justSolved] = true;
        return CheckRouter.GroupSolved(members, solved);
    }

    /// <summary>Achievements already said to be no check here, once each per slot.</summary>
    private static readonly HashSet<string> _achievementsNotChecks = new(StringComparer.Ordinal);

    /// <summary>
    /// The game awarded an achievement (SteamAchievements saw it reach
    /// AchievementManager.SetAchievementMet). When the seed has it as a check
    /// on the puzzle being played, file it as a part would be: withheld until
    /// the run holds the puzzle's abilities, then filed. Anything else - a
    /// seed without `achievements`, a hint or chapter achievement, one from
    /// another puzzle - is said once and dropped.
    /// </summary>
    internal static void OnAchievement(string achievementId)
    {
        EnsureSlot();
        if (_router == null || _currentSlot < 0) return;

        var location = _router.ForAchievement(_currentSlot, achievementId);
        if (location == null)
        {
            if (_achievementsNotChecks.Add($"{_currentSlot}/{achievementId}"))
            {
                Plugin.Logger.LogInfo(
                    $"checks: achievement {achievementId} on slot {_currentSlot} is not a check of this run");
            }
            return;
        }

        if (!Earned(location))
        {
            RunState.AddWithheld(location);
            return;
        }
        Report(location);
    }

    /// <summary>The arrangement being completed, noted before it is filed.</summary>
    private static int _pendingSlot = -1;
    private static string _pendingSolution = "";

    /// <summary>
    /// LevelCompleteEarly always comes before LevelComplete. Noting the
    /// arrangement here lets the level-complete screen count this completion
    /// even when it asks before OnLevelComplete has filed it (OfferRetry).
    /// </summary>
    private static void OnLevelCompleteEarly(GameEventManager.GameEventData data)
    {
        // A finished puzzle is not a puzzle to knock over - see Traps. Here
        // too, since the endings filed below can bring a trap back first.
        Traps.NoteCompletion();

        EnsureSlot();
        _pendingSlot = _currentSlot;
        _pendingSolution = CanonicalEnding(data?.SolutionId ?? "");

        // Endings the save recorded during play without a completion of their
        // own - the Seeing Stars Boss's phases - filed now, before the retry
        // panel counts the slot's stars. This completion is left to
        // OnLevelComplete, so its filing and withholding are as they were.
        SeedSolutionsFromSave(_currentSlot, data?.SolutionId ?? "");
        _saveWrittenFrame = -1;
    }

    /// <summary>
    /// The game recorded a solution in the save (SaveRedirect's postfixes on
    /// SaveData.SaveLevelData and SaveSystem.SaveGame). The Seeing Stars
    /// Boss's phases arrive this way, each as it is solved, with no completion
    /// of their own.
    ///
    /// Read on the next frame, not here: a completion records its own ending
    /// too, and that one is OnLevelComplete's to file or withhold. Its
    /// LevelCompleteEarly clears the note, and once a completion has run on
    /// the slot (_pendingSlot) the save is not read again until the next entry.
    /// </summary>
    internal static void NoteSaveWritten() => _saveWrittenFrame = UnityEngine.Time.frameCount;

    private static int _saveWrittenFrame = -1;

    /// <summary>Called every check tick; files what the save recorded during play.</summary>
    internal static void TickSaveWritten()
    {
        if (_saveWrittenFrame < 0 || UnityEngine.Time.frameCount <= _saveWrittenFrame) return;
        _saveWrittenFrame = -1;
        if (_currentSlot < 0 || _pendingSlot == _currentSlot) return;
        SeedSolutionsFromSave(_currentSlot, "");
    }

    /// <summary>
    /// The id a completion files under: Books (Randomized)'s Draggables_0 read
    /// against the two rules this seed chose, from the running level's own
    /// randomizer (Core Endings.Canonical). The level is still loaded at both
    /// completion events. Anything else, or a randomizer that cannot be read,
    /// comes back as it was and the table's alternatives answer.
    /// </summary>
    private static string CanonicalEnding(string id)
    {
        if (_slot == null || _currentSlot < 0 || id != "Draggables_0") return id;
        string? first = null, second = null;
        try
        {
            var books = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level?
                .m_randomizer?.TryCast<Books_LevelRandomizer>();
            if (books != null)
            {
                first = books.firstSolution.ToString();
                second = books.secondSolution.ToString();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"checks: could not read the seed's rules: {e.Message}");
        }
        return Endings.Canonical(_slot.Slots[_currentSlot].LevelId, id, first, second);
    }

    /// <summary>
    /// Whether the level-complete screen offers the retry panel: the slot has
    /// more than one Solution location and they will not all be in once this
    /// completion is filed. Null when no run slot is up, so the game decides.
    /// droha, 2026-09-25: show it while a level with several solutions still
    /// has some to find, otherwise go on to the next.
    /// </summary>
    internal static bool? OfferRetry(out int lit, out int total)
    {
        lit = total = 0;
        var slot = _currentSlot;
        if (_router == null || slot < 0) return null;

        (lit, total) = _router.SolutionStars(slot, _ledger.IsCollected);

        // The completion in progress, when the screen asks before it is filed
        // (measured: it asks at LevelCompleteEarly). After OnLevelComplete has
        // recorded it, Peek answers 0: no double count.
        if (_pendingSlot == slot)
        {
            var nth = _solutions.Peek(slot, _pendingSolution);
            var solution = nth > 0
                ? _router.ForEnding(slot, _pendingSolution,
                    new List<string>(_solutions.IdsFor(slot)) { _pendingSolution ?? "" })
                : null;
            if (solution != null && !_ledger.IsCollected(solution)
                && WouldEarnSolution(solution, _router.PartsForSolution(slot, _pendingSolution))) lit++;
        }
        return total > 1 && lit < total;
    }

    private static void OnLevelComplete(GameEventManager.GameEventData data)
    {
        // A finished puzzle is not a puzzle to knock over - see Traps.
        Traps.NoteCompletion();

        // THE CREDITS, FIRST, because everything below this line is about
        // slots and the credits card is not one - EnsureSlot finds nothing and
        // the handler returns, which is why noticing them anywhere later would
        // never have run.
        //
        // The goal is normally already reported by then: the level watch sees
        // the card load and calls NotePlayed there, so the server hears about
        // it as the animation starts rather than when it ends. This is the
        // belt to that braces, for a credits run that somehow completes
        // without the watch having ticked.
        var finale = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        if (finale != null && finale.IsCredits)
        {
            Credits.NotePlayed();
            return;
        }

        EnsureSlot();
        if (_router == null || _currentSlot < 0) return;

        // A NEW ending only: a repeat of one already found files nothing.
        var reported = data?.SolutionId ?? "";
        var ending = CanonicalEnding(reported);
        var nth = _solutions.Record(_currentSlot, ending);
        string? solution = null;
        if (nth > 0)
        {
            // Fixed endings (2026-09-28): the id names the location; a
            // generated puzzle files by the order found (CheckRouter.ForEnding).
            solution = _router.ForEnding(_currentSlot, ending, _solutions.IdsFor(_currentSlot));
            if (solution != null)
            {
                var needs = _router.PartsForSolution(_currentSlot, ending);
                if (EarnedSolution(solution, needs)) Report(solution);
                else RunState.AddWithheld(solution, needs);
            }
        }

        // Which ending the game named and where it went. Without this a
        // player's log could not say why a second solution filed nothing:
        // Books (Randomized), 2026-09-28, both arrangements done and one of
        // its two Solution checks in (Endings.Canonical).
        var named = reported == ending ? $"'{ending}'" : $"'{reported}' (as '{ending}')";
        Plugin.Logger.LogInfo(nth > 0
            ? $"checks: slot {_currentSlot} ended on {named} -> {solution ?? "no location"}"
            : $"checks: slot {_currentSlot} ended on {named} again - nothing new");

        // A skip's completion is paid like any other here; the game's
        // LevelSkipped, which follows it, releases the rest of the slot (see
        // OnLevelSkipped and Skips).
        var beaten = _router.ForBeaten(_currentSlot);
        if (beaten != null) Report(beaten);

        // The completion stars popped before this ran; light what it earned.
        SuccessStars.Refresh(_currentSlot);
    }

    /// <summary>
    /// The game skipped the level. If it was the run's paid-for Skip, the rest
    /// of the slot is released and the Skip charged (Skips.OnLevelSkipped).
    /// </summary>
    private static void OnLevelSkipped(GameEventManager.GameEventData data)
    {
        EnsureSlot();
        Skips.OnLevelSkipped(_currentSlot);
    }

    /// <summary>
    /// Send every location on the slot not yet collected, Beaten included: a
    /// Skip finishes the puzzle outright - every solution, every controller
    /// group, and the Beaten token.
    ///
    /// This REVERSES the earlier behaviour, deliberately and on droha's call.
    /// Beaten used to be withheld on a skip, because the credits gate counts
    /// Beaten tokens and enough Skips could therefore reach the goal with
    /// nothing solved. The cost was a card that could never be finished: the
    /// star badge needs every location on the slot. The exploit is bounded by
    /// supply instead - skip_count caps at 20.
    ///
    /// Sent explicitly rather than left to the solved-controller events: a
    /// skip does not necessarily raise one per group, and the part locations
    /// are exactly the ones that would otherwise be stranded. Not gated by
    /// Earned: a Skip grants the whole card by design.
    /// </summary>
    internal static int ReleaseSlot(int slot)
    {
        if (_router == null || slot < 0) return 0;
        var sent = 0;
        foreach (var name in _router.ForSlot(slot))
        {
            if (_ledger.IsCollected(name)) continue;
            Report(name);
            sent++;
        }
        Plugin.Logger.LogInfo($"checks: skipped slot {slot}, sent {sent} remaining location(s)");
        SuccessStars.Refresh(slot);
        return sent;
    }

    /// <summary>
    /// Has the run actually earned this location, or is it being handed a
    /// check for work its abilities say it could not have done?
    ///
    /// MEASURED, NOT SUPPOSED. Books 3 declares a Shuffleables group of 17
    /// books behind Swapping and a plain Draggables group over the SAME 17
    /// books. Draggables is baseline, and the dimmer merges per object with
    /// unlocked winning, so the baseline group frees every object the gated
    /// one was meant to hold: on 2026-09-19, holding ZERO abilities, nothing
    /// on that level was dimmed and completing it filed
    /// `Books 3 - Design (Shuffle)`. A player could do that by hand - nothing
    /// was locked - so this is a logic leak rather than a harness artifact.
    ///
    /// WITHHOLDING IS SAFE FOR THE LOCATIONS THIS GUARDS, and that is the
    /// whole reason it guards only those. A withheld location is kept in the
    /// run file and filed by FileWithheld once its ability arrives; a withheld
    /// solution is also re-found by FileSolutionsAlreadyEarned from the save.
    /// SweepAlreadySolved alone was never enough for a part: the game rebuilds
    /// the level unsolved, so a later visit found nothing to sweep. A check nobody
    /// re-files is worse than a check sent early, so anything without a
    /// recovery path is deliberately NOT gated:
    ///
    ///   Beaten - a local event location the server has no address for, with
    ///            no recovery path at all, and it is what the credits gate
    ///            counts. Finishing a puzzle is self-evidently earned.
    ///   Skips  - a deliberate bypass that grants the whole card by design,
    ///            bounded by supply rather than by rule. See OnLevelComplete.
    ///
    /// Fails OPEN on every uncertainty, like the dimmer it backs up: no
    /// progress table, no inventory, a location this seed does not contain,
    /// or ability locks turned off in the yaml all report earned. The cost of
    /// a wrong "no" is a check that never arrives; the cost of a wrong "yes"
    /// is a check that arrives early.
    /// </summary>
    private static bool Earned(string location)
    {
        if (WouldEarn(location)) return true;

        Plugin.Logger.LogInfo(
            $"checks: withheld {location} - the run cannot reach it yet; "
            + "kept, and filed as soon as the item arrives");
        return false;
    }

    /// <summary>
    /// Earned, for a SOLUTION: judged by the part locations its ending named
    /// (CheckRouter.PartsForSolution) when there are any, so an ending is
    /// withheld only for abilities it actually uses. Every solution location
    /// carries the level's whole ability set, and in the 0.4.2 playtest that
    /// withheld Spoons' Size (Elastic) ending for Stacking, Figurines' first
    /// ending for Gadgets and Coins 1 for Stacking - none of which those
    /// endings touch. With no named part it is Earned as before.
    /// </summary>
    private static bool EarnedSolution(string solution, IReadOnlyList<string> needs)
    {
        if (needs.Count == 0) return Earned(solution);
        if (WouldEarnSolution(solution, needs)) return true;

        Plugin.Logger.LogInfo(
            $"checks: withheld {solution} - its ending ({string.Join(", ", needs)}) "
            + "needs what the run does not hold yet; kept, and filed as soon as it does");
        return false;
    }

    /// <summary>EarnedSolution's answer without its log line.</summary>
    private static bool WouldEarnSolution(string solution, IReadOnlyList<string> needs)
        => needs.Count == 0 ? WouldEarn(solution) : AllWouldEarn(needs);

    private static bool AllWouldEarn(IReadOnlyList<string> locations)
    {
        foreach (var location in locations)
        {
            if (!WouldEarn(location)) return false;
        }
        return true;
    }

    /// <summary>
    /// An ability arrived: file what was withheld on the next check tick,
    /// once the session is surely up (see FileWithheld).
    /// </summary>
    internal static void NoteAbilitiesChanged() => _withheldDirty = true;

    private static bool _withheldDirty;

    /// <summary>Called every check tick; files withheld checks after an ability arrived.</summary>
    internal static void TickWithheld()
    {
        if (!_withheldDirty) return;
        _withheldDirty = false;
        FileWithheld();
    }

    /// <summary>
    /// File every withheld check the run can now reach.
    ///
    /// A check solved out of reach is kept in the run file (Earned's caller
    /// adds it) and filed here once its ability is held. Called at login,
    /// offline start and slot entry, and after an ability arrives - never
    /// directly from the item handler, which runs during a reconnect replay
    /// before the new session's router exists.
    /// </summary>
    internal static void FileWithheld()
    {
        if (_router == null || _progress == null) return;
        var waiting = RunState.Withheld();
        if (waiting.Count == 0) return;

        foreach (var location in new List<string>(waiting))
        {
            if (_ledger.IsCollected(location))
            {
                RunState.RemoveWithheld(location);
                continue;
            }
            var needs = RunState.NeedsFor(location);
            if (needs.Count > 0 ? !AllWouldEarn(needs) : !WouldEarn(location)) continue;

            Plugin.Logger.LogInfo(
                $"checks: {location} was withheld; the run can reach it now - filing it");
            RunState.RemoveWithheld(location);
            Report(location);
        }
    }

    /// <summary>Earned's answer without its log line, for a question asked often.</summary>
    private static bool WouldEarn(string location)
    {
        if (_progress == null) return true;

        var abilities = Inventory.Abilities;
        if (abilities == null) return true;

        // ABILITIES ONLY. IsReachable also gates on PACKS, and this guard
        // must not - twice over.
        //
        // It is not what this exists for. C backs up the ability locks; packs
        // gate which CARDS open, and a player standing in a level necessarily
        // has the pack that opened it. Gating on packs here adds a way to
        // fail with nothing to gain.
        //
        // And the reading is not trustworthy. Track.State is null before the
        // track is built, so `PacksHeld ?? 0` fails CLOSED - every pack-gated
        // location is withheld on a zero that means "not known yet" rather
        // than "none held". A gate run lost `Paper Plane Supplies (Drawer
        // Chores) - Draggables` that way; it needs packs 1 and Drawer, the
        // run held Drawer, and the Grids sitting on that location never
        // shipped. TupperwareTower needed Grids, so the run stalled at 7 of 8
        // while every other check passed.
        //
        // int.MaxValue satisfies the pack half unconditionally and leaves the
        // ability half exactly as it was.
        return _progress.IsReachable(location, int.MaxValue, abilities);
    }

    private static void Report(string location)
    {
        // An event location is collected but never sent - the server has no
        // address for it. Owing one would retry a send that can only ever be
        // rejected, which is exactly what filled the log with "server has no
        // location named ... - Beaten".
        if (_router != null && _router.IsLocalEvent(location))
        {
            if (!_ledger.RecordLocal(location)) return;
            Plugin.Logger.LogInfo($"beaten: {location}");

            // Straight to disk, not via the check flush.
            //
            // The flush exists to send owed checks and returns early when
            // nothing is owed, which for an event location is always - so
            // routing this through it would persist nothing. Nothing else can
            // recover these either: the server has no address for them, so it
            // never lists them back at login.
            RunState.SetBeaten(_ledger.LocalForSaving());

            // No toast. droha, 2026-09-25: the beaten count is on the level
            // select already ("i don't need to see beaten x/n").
            return;
        }

        if (!_ledger.Check(location)) return;      // already ours

        Plugin.Logger.LogInfo($"check: {location}");
        // Online, the server's own line for the send says what was found and
        // who got it (Connection.ItemMessage). Offline there is no server to
        // say anything, so the location alone is better than silence.
        if (!Plugin.IsConnected)
        {
            Toasts.Show("Found " + ALTTLArchipelago.Core.ApPalette.Paint(
                    location, ALTTLArchipelago.Core.ApPalette.ForLocation()),
                Toasts.Plain);
        }
        Plugin.QueueCheckFlush();
    }
}
