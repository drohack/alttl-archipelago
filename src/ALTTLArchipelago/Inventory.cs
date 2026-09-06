using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago;

/// <summary>
/// What the player has been given, and what that changes.
///
/// Items are applied by COUNTING, not by reacting. Archipelago replays the
/// whole item list on every reconnect, so "a pack arrived, open four more
/// puzzles" would double the track the second time you log in. Instead every
/// arrival re-counts the inventory from scratch and re-asserts the resulting
/// state, which makes a replay a no-op rather than a corruption.
///
/// That is also why the appliers take absolute values - TrackState.SetPacksHeld
/// and AbilityState.SetHeld both replace rather than accumulate.
/// </summary>
internal static class Inventory
{
    /// <summary>Every item name received this session, including duplicates.</summary>
    private static readonly List<string> _received = new();

    private static AbilityState? _abilities;


    internal static AbilityState? Abilities => _abilities;
    internal static int PacksHeld { get; private set; }
    internal static bool HasCredits { get; private set; }
    internal static int SkipsHeld { get; private set; }
    internal static int TrapsReceived { get; private set; }
    internal static int LevelsBeaten { get; private set; }
    internal static int HintPagesHeld { get; private set; }
    internal static int LevelBackgrounds { get; private set; }
    internal static int MenuBackgrounds { get; private set; }


    /// <summary>
    /// A new connection is being ATTEMPTED. The last one's items are forgotten
    /// the moment the new one produces anything.
    ///
    /// This is the session boundary. It cannot be in <see cref="Begin"/>,
    /// because items start arriving before slot_data has been parsed - a
    /// precollected starting ability landed a full step ahead of Ready in
    /// testing - so Begin alone would throw those away.
    ///
    /// Without a boundary the list was cumulative across an in-session
    /// reconnect: the server replays every item on connect, Receive appended
    /// them a second time, and the counts doubled. Packs and Skips doubled
    /// silently; traps announced themselves by firing a burst, since owed is
    /// TrapsReceived minus the number already sprung. It never showed up in
    /// testing because every reconnect test restarted the process, and a fresh
    /// process starts with an empty list.
    ///
    /// DEFERRED rather than immediate, because the list is now PERSISTED.
    /// SlotCache reads Received() to write the offline cache, so an empty list
    /// while a live run holds items is a list that can be written to disk, and
    /// the next offline start would come up on a locked track. An ATTEMPT is
    /// not a session; only its success is.
    ///
    /// So the clear is armed here and fires at the first thing the NEW session
    /// produces, which is either an item or Begin. An attempt that fails
    /// produces neither, and the run in progress is untouched.
    ///
    /// HONESTLY MEASURED, because the first version of this comment claimed a
    /// dramatic failure that does not happen. The immediate clear was run
    /// against tools/offline-reconnect-test.py deliberately: pressing Connect
    /// during an offline run against a dead server did NOT collapse the track
    /// or lose the abilities. Nothing recounts, because the plain clear never
    /// called Apply, so the displayed state stays stale-correct until the next
    /// arrival. The exposure is narrower and quieter than that: a cache write
    /// already pending from a recent item would serialise the emptied list.
    ///
    /// That is worth closing anyway - a value written to disk should never be
    /// briefly wrong - but it is a precaution taken when items started being
    /// cached, not a fix for a bug anybody saw.
    /// </summary>
    internal static void NewSession() => _clearPending = true;

    /// <summary>Armed by NewSession, fired by the next Receive or Begin.</summary>
    private static bool _clearPending;

    /// <summary>Fire a pending clear exactly once. Both entry points call it.</summary>
    private static void TakeOverIfPending()
    {
        if (!_clearPending) return;
        _clearPending = false;
        _received.Clear();
    }

    /// <summary>
    /// Everything received, for the offline cache. A copy: the live list keeps
    /// changing, and what is written must be what was true when asked.
    /// </summary>
    internal static IReadOnlyList<string> Received() => new List<string>(_received);

    /// <summary>
    /// Adopt a cached item list for an offline start.
    ///
    /// The counterpart of Received(). Safe to be replaced wholesale later: the
    /// next real connection arms NewSession and the server's replay takes over
    /// this list rather than adding to it, which is the same property that
    /// stops a reconnect doubling every count.
    /// </summary>
    internal static void RestoreReceived(IReadOnlyList<string> items)
    {
        _clearPending = false;
        _received.Clear();
        _received.AddRange(items);
    }

    internal static void Begin(SlotData slot)
    {
        // Takes over from the previous session if nothing has yet - see
        // NewSession. Anything that arrived since IS this session's and is
        // kept, because the first arrival already did the clearing.
        TakeOverIfPending();

        _abilities = new AbilityState(slot);
        PacksHeld = 0;
        HasCredits = false;
        SkipsHeld = 0;
        TrapsReceived = 0;
        LevelsBeaten = 0;
        HintPagesHeld = 0;
        LevelBackgrounds = 0;
        MenuBackgrounds = 0;
        Apply();
    }

    internal static void End()
    {
        _clearPending = false;
        _received.Clear();
        _abilities = null;
    }

    /// <summary>One item arrived. Order does not matter; the count does.</summary>
    internal static void Receive(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        TakeOverIfPending();
        _received.Add(name);
        Apply();
    }

    /// <summary>
    /// Re-derive everything from the full list and push it into the game.
    ///
    /// Idempotent on purpose: calling it twice with the same list must leave
    /// the game in the same place, because that is exactly what a reconnect
    /// does.
    /// </summary>
    private static void Apply()
    {
        // The counting lives in Core, where it is tested. This method is the
        // Unity half only: take the numbers and push them at the game.
        var counts = InventoryCounts.From(
            _received, _abilities == null ? null : _abilities.IsAbility);

        PacksHeld = counts.Packs;
        SkipsHeld = counts.Skips;
        TrapsReceived = counts.Traps;
        LevelsBeaten = counts.Beaten;
        HintPagesHeld = counts.HintPages;
        LevelBackgrounds = counts.LevelBackgrounds;
        MenuBackgrounds = counts.MenuBackgrounds;
        HasCredits = counts.HasCredits;

        // Only the abilities that arrived as ITEMS. AbilityState holds the
        // seed's starting abilities separately, because the server never
        // resends those and clearing them here would lose them for good.
        _abilities?.SetHeld(counts.Abilities);

        Track.SetPacksHeld(counts.Packs);

        // Repaint now, not at the next level load. A Level Background is meant
        // to be a visible reward, and a player who receives one mid-puzzle
        // should see it land rather than find out later. Safe to call on every
        // recount, replay included: the colour is derived from the count, so
        // re-applying it writes the same value.
        Backgrounds.ApplyToLevel();

        // The offline cache is now out of date. Only marked, not written: a
        // reconnect replays hundreds of items one at a time, and writing per
        // item would serialise the whole draw hundreds of times to arrive back
        // where it started.
        SlotCache.MarkDirty();
    }
}
