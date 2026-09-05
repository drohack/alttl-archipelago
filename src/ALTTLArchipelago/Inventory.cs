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
    /// A new connection is being made. Forget the last one's items.
    ///
    /// This is the session boundary, and it has to be HERE rather than in
    /// <see cref="Begin"/>, because items start arriving before slot_data has
    /// been parsed - a precollected starting ability landed a full step ahead
    /// of Ready in testing - so Begin cannot clear without throwing those away.
    ///
    /// Without a boundary the list was cumulative across an in-session
    /// reconnect: the server replays every item on connect, Receive appended
    /// them a second time, and the counts doubled. Packs and Skips doubled
    /// silently; traps announced themselves by firing a burst, since owed is
    /// TrapsReceived minus the number already sprung.
    ///
    /// It never showed up in testing because every reconnect test restarted the
    /// process, and a fresh process starts with an empty list.
    /// </summary>
    internal static void NewSession() => _received.Clear();

    internal static void Begin(SlotData slot)
    {
        // Deliberately does NOT clear: see NewSession, which already did, and
        // anything that arrived since is this session's and must be kept.
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
        _received.Clear();
        _abilities = null;
    }

    /// <summary>One item arrived. Order does not matter; the count does.</summary>
    internal static void Receive(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
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
    }
}
