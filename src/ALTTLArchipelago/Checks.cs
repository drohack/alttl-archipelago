using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;

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

    /// <summary>Distinct solution ids seen per slot, in the order found.</summary>
    private static readonly Dictionary<int, List<string>> _solutionsFound = new();

    /// <summary>
    /// The IL2CPP side holds listeners through a weak wrapper, so a delegate
    /// that is not rooted on the managed side stops firing at the first GC.
    /// This list is the entire reason the subscriptions keep working.
    /// </summary>
    private static readonly List<Il2CppSystem.Action<GameEventManager.GameEventData>> KeepAlive = new();

    private static bool _attached;

    /// <summary>
    /// Counts events actually handled, so a battery can assert the listeners
    /// FIRED rather than merely that they were attached. A subscription that
    /// silently failed looks identical to a quiet game.
    /// </summary>
    internal static int ControllerEvents { get; private set; }
    internal static int LevelCompleteEvents { get; private set; }

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
    internal static bool Active => _router != null;

    internal static void Begin(SlotData slot)
    {
        _router = new CheckRouter(slot);
        _progress = new SlotProgress(slot, _router);
        _slot = slot;
        _ledger = new CheckLedger();
        _solutionsFound.Clear();
        _currentSlot = -1;
        Attach();
    }

    internal static void End()
    {
        _router = null;
        _progress = null;
        _slot = null;
        _currentSlot = -1;
        _solutionsFound.Clear();
    }

    /// <summary>What the server says we have already checked.</summary>
    internal static void AdoptServerChecks(IEnumerable<string> names)
        => _ledger.AdoptServerChecks(names);

    /// <summary>The player just launched this slot's card.</summary>
    internal static void EnterSlot(int slotIndex)
    {
        _currentSlot = slotIndex;
        _audited = false;

        // Any skip in flight belongs to the level we just left. If it never
        // produced a completion, the flag would otherwise sit set and swallow
        // the Beaten token for THIS level, which the player would then have to
        // earn twice with no way to know why.
        Skips.Skipping = false;

        Plugin.Logger.LogInfo($"checks: now playing slot {slotIndex}");
    }

    internal static void LeaveSlot()
    {
        _currentSlot = -1;
        _audited = false;
    }

    /// <summary>Whether the running level has been compared against the table.</summary>
    private static bool _audited;

    private static float _sinceAudit;

    /// <summary>Levels whose mismatch has already been reported, so it is said once.</summary>
    private static readonly HashSet<string> _mismatchesReported = new(StringComparer.Ordinal);

    internal static int Mismatches { get; private set; }

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
        if (_slot == null || _audited) return;

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

            _audited = true;

            var levelId = _slot.Slots[_currentSlot].LevelId;
            if (_mismatchesReported.Contains(levelId)) return;
            if (!_slot.ControllerGroups.TryGetValue(levelId, out var known)) return;

            var unknown = new List<string>();
            for (int i = 0; i < registered.Count; i++)
            {
                var oc = registered[i];
                if (oc == null) continue;
                var name = oc.gameObject?.name ?? "";
                if (name.Length > 0 && !known.ContainsKey(name)) unknown.Add(name);
            }

            if (unknown.Count == 0) return;

            // Not every registered controller is a puzzle - the table only
            // holds the ones that can be checked - so this is a report, not an
            // error. It is loud because a genuine new controller means the
            // logic and the game disagree about what is solvable.
            Mismatches++;
            _mismatchesReported.Add(levelId);
            Plugin.Logger.LogWarning(
                $"CONTROLLER MISMATCH on {levelId}: {registered.Count} registered, "
                + $"{known.Count} in the table, not recognised: "
                + string.Join(", ", unknown));
        }
        catch (Exception e)
        {
            _audited = true;
            Plugin.Logger.LogWarning($"checks: controller audit failed: {e.Message}");
        }
    }

    private static float _sinceLevelWatch;
    private static string _watchedLevel = "";
    private static float _emptyFor;

    /// <summary>Times a level came up with nothing in it.</summary>
    internal static int EmptyLevels { get; private set; }

    /// <summary>
    /// Shout if a level is on screen with nothing in it.
    ///
    /// A level that loads empty looks identical to a level that is still
    /// loading, except that it never recovers and the game stops accepting
    /// input - and there is nothing in the log to say so. That happened in play
    /// and cost a restart to diagnose. This turns a silent hang into one line
    /// naming the level, so the next report is actionable immediately.
    /// </summary>
    /// <summary>How often the empty-level watch looks.</summary>
    private const float WatchInterval = 1f;

    /// <summary>
    /// How long a level must stay empty before it is called broken. Long
    /// enough that a slow load is not accused, short enough to still be on
    /// screen while the player is looking at it.
    /// </summary>
    private const float EmptyAfter = 6f;

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
            var level = li?.Level;
            if (li == null || level == null)
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
            var empty = (controllers == null || controllers.Count == 0)
                        && (objects == null || objects.Count == 0);

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

            EmptyLevels++;
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
            Listen<GameEventManager.GameEvent_LevelComplete>(OnLevelComplete);
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
    /// version of being wrong that loses progress.
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

            if (fallback < 0) return;

            _currentSlot = fallback;
            _audited = false;
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
        ControllerEvents++;
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
            Plugin.Logger.LogInfo(
                $"checks: '{name}' solved on slot {_currentSlot}, no location for it");
            return;
        }

        Report(location);
    }

    private static void OnLevelComplete(GameEventManager.GameEventData data)
    {
        LevelCompleteEvents++;
        EnsureSlot();
        if (_router == null || _currentSlot < 0) return;

        var solutionId = data?.SolutionId ?? "";

        if (!_solutionsFound.TryGetValue(_currentSlot, out var found))
        {
            found = new List<string>();
            _solutionsFound[_currentSlot] = found;
        }

        // Distinct, and ordinal. Re-finding a solution already found is not a
        // new check - a player can complete the same arrangement repeatedly.
        if (!found.Contains(solutionId)) found.Add(solutionId);

        var solution = _router.ForSolution(_currentSlot, found.Count);
        if (solution != null) Report(solution);

        // Beating the level is its own location, granting the token the credits
        // gate counts. Sent on any completion, not only the first solution -
        // but NOT for a skip.
        //
        // The game reports a skipped level as complete, so without this a Skip
        // item granted the credits token, and enough Skips reached the goal
        // with nothing solved. The solution check above still fires: getting
        // past the puzzle is what a Skip is for. Only the goal is protected.
        if (Skips.Skipping)
        {
            Skips.Skipping = false;
            Plugin.Logger.LogInfo("checks: skipped, so no Beaten token");
            return;
        }

        var beaten = _router.ForBeaten(_currentSlot);
        if (beaten != null) Report(beaten);
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

            // Beating a puzzle is the thing the credits gate counts, so it is
            // worth saying out loud - it was silent before, which made
            // finishing a level feel like nothing had happened.
            var goal = Plugin.Seed?.LevelsToBeat ?? 0;
            Toasts.Show(goal > 0
                ? $"Puzzle beaten ({LevelsBeaten}/{goal})"
                : "Puzzle beaten", Toasts.Notice);
            return;
        }

        if (!_ledger.Check(location)) return;      // already ours

        Plugin.Logger.LogInfo($"check: {location}");
        Toasts.Show("Found " + ALTTLArchipelago.Core.ApPalette.Paint(
                location, ALTTLArchipelago.Core.ApPalette.ForLocation()),
            Toasts.Plain);
        Plugin.QueueCheckFlush();
    }
}
