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

    /// <summary>Counts applications so a battery can assert this ran at all.</summary>
    internal static int Applications { get; private set; }

    internal static AbilityState? Abilities => _abilities;
    internal static int PacksHeld { get; private set; }
    internal static bool HasCredits { get; private set; }
    internal static int SkipsHeld { get; private set; }
    internal static int TrapsReceived { get; private set; }
    internal static int LevelsBeaten { get; private set; }
    internal static int FillerReceived { get; private set; }

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
        FillerReceived = 0;
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
        Applications++;

        var packs = 0;
        var skips = 0;
        var traps = 0;
        var beaten = 0;

        // Filler - Title Theme, Colour Scheme, Daily Badge - and anything else
        // the server sends that is neither special nor a known ability. Asking
        // the seed's own ability catalogue means there is no second list of
        // names to keep in step with the generator.
        var other = 0;
        var credits = false;
        var abilities = new List<string>();

        foreach (var name in _received)
        {
            // Exact matches against ItemNames, which is pinned against the
            // generator's own table. Prefix matching is what let "Puzzle Pack"
            // look plausible while never matching "Progressive Puzzle Pack".
            if (name == ItemNames.Pack) packs++;
            else if (name == ItemNames.Credits) credits = true;
            else if (name == ItemNames.Skip) skips++;
            else if (name == ItemNames.CatTrap) traps++;
            else if (name == ItemNames.BeatenToken) beaten++;
            else if (_abilities != null && _abilities.IsAbility(name)) abilities.Add(name);
            else other++;
        }

        PacksHeld = packs;
        SkipsHeld = skips;
        TrapsReceived = traps;
        LevelsBeaten = beaten;
        FillerReceived = other;
        HasCredits = credits;

        // Only the abilities that arrived as ITEMS. AbilityState holds the
        // seed's starting abilities separately, because the server never
        // resends those and clearing them here would lose them for good.
        _abilities?.SetHeld(abilities);

        Track.SetPacksHeld(packs);
    }
}
