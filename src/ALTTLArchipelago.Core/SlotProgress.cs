namespace ALTTLArchipelago.Core;

/// <summary>What the badge on a card should say.</summary>
public enum SlotStatus
{
    /// <summary>Everything still to do here can be done now.</summary>
    Doable,

    /// <summary>Some of what is left can be done, some cannot.</summary>
    Mixed,

    /// <summary>Nothing still to do here can be done yet.</summary>
    Locked,

    /// <summary>Nothing left to do.</summary>
    Complete,
}

/// <summary>
/// Whether the checks left on a card can actually be reached right now.
///
/// Answered from the generator's OWN requirements table - the same one that
/// built the access rules - rather than by re-deriving what an item unlocks.
/// The badge is a promise to the player about what is worth their time, and a
/// second implementation of reachability would eventually make that promise
/// while logic disagreed.
///
/// A location is reachable when the player holds enough packs for it and every
/// ability it lists. Both conditions come straight from the payload.
/// </summary>
public sealed class SlotProgress
{
    private readonly SlotData _slot;
    private readonly CheckRouter _router;

    public SlotProgress(SlotData slot, CheckRouter router)
    {
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>
    /// The state of one card, given what has been collected and what is held.
    /// </summary>
    public SlotStatus StatusOf(
        int slotIndex,
        Func<string, bool> isCollected,
        int packsHeld,
        AbilityState abilities)
    {
        var locations = _router.ForSlot(slotIndex);

        int reachable = 0, blocked = 0;
        foreach (var name in locations)
        {
            if (isCollected(name)) continue;

            if (IsReachable(name, packsHeld, abilities)) reachable++;
            else blocked++;
        }

        // Nothing outstanding. Note this is also the answer for a card with no
        // locations at all, which is correct: there is nothing here to come
        // back for.
        if (reachable == 0 && blocked == 0) return SlotStatus.Complete;

        if (blocked == 0) return SlotStatus.Doable;
        if (reachable == 0) return SlotStatus.Locked;
        return SlotStatus.Mixed;
    }

    /// <summary>Can this location be checked with what the player has now?</summary>
    public bool IsReachable(string name, int packsHeld, AbilityState abilities)
    {
        if (!_slot.Requirements.TryGetValue(name, out var requirement))
        {
            // Not a location in this seed. Calling it unreachable would paint a
            // card red forever over something that does not exist.
            return true;
        }

        if (packsHeld < requirement.Packs) return false;

        // Locks off means the abilities in the table are not enforced, so the
        // badge must not claim otherwise.
        if (!abilities.LocksEnabled) return true;

        return abilities.HasAll(requirement.Abilities);
    }
}
