namespace ALTTLArchipelago.Core;

/// <summary>
/// Which puzzles the player can see, given how many packs they hold.
///
/// The one answer to "is slot N revealed". The level select, the card lock and
/// the tracker badge all ask this rather than each working it out, because
/// three implementations of one rule is three chances to disagree about which
/// puzzle a player is allowed to open.
///
/// The boundaries are NOT recomputed here. They arrive in slot_data from the
/// generator, which chose them when it decided what each location would
/// require. Re-deriving the ramp in C# would be a second implementation of a
/// rule that already exists in Python, and the moment the two disagreed the
/// game would unlock a different set of puzzles than the logic believed
/// reachable - a seed that looks fine and cannot be finished.
/// </summary>
public sealed class TrackState
{
    private readonly IReadOnlyList<int> _boundaries;

    public TrackState(SlotData slot)
    {
        Slots = slot.Slots;
        _boundaries = slot.PackBoundaries.Count > 0
            ? slot.PackBoundaries
            // A payload with no boundaries is a generator too old to send
            // them. Opening everything is wrong, opening nothing is worse -
            // the free opening is the honest fallback and the mod logs it.
            : new List<int> { Math.Min(slot.PackSize, slot.Slots.Count) };
    }

    public IReadOnlyList<SlotEntry> Slots { get; }

    /// <summary>Progressive Puzzle Packs received so far.</summary>
    public int PacksHeld { get; private set; }

    /// <summary>Packs the seed contains, so the UI can say "3 of 14".</summary>
    public int PackTotal => Math.Max(0, _boundaries.Count - 1);

    /// <summary>
    /// Where each pack stops, cumulative. The level select uses these to
    /// break the track into sections, so the breaks a player sees are the
    /// same ones the generator planned.
    /// </summary>
    public IReadOnlyList<int> Boundaries => _boundaries;

    /// <summary>
    /// How many slots are revealed right now.
    ///
    /// Clamped both ends: more packs than the seed has is not an error worth
    /// refusing (a resend, or a generator change) and must not index past the
    /// table.
    /// </summary>
    public int OpenSlots
    {
        get
        {
            var index = Math.Clamp(PacksHeld, 0, _boundaries.Count - 1);
            return Math.Min(_boundaries[index], Slots.Count);
        }
    }

    public bool IsOpen(int slotIndex)
        => slotIndex >= 0 && slotIndex < OpenSlots;

    /// <summary>
    /// Set the pack count. Absolute rather than incremental, because
    /// Archipelago resends the whole item list on reconnect - counting
    /// arrivals would double everything the second time.
    /// </summary>
    public void SetPacksHeld(int packs)
        => PacksHeld = packs < 0 ? 0 : packs;

    /// <summary>
    /// Slots revealed by going from one pack count to another, for the "new
    /// puzzles appeared" message. Empty when nothing changed or when packs
    /// went backwards.
    /// </summary>
    public IReadOnlyList<int> SlotsRevealedBy(int previousPacks)
    {
        var from = BoundaryFor(previousPacks);
        var to = OpenSlots;

        var revealed = new List<int>();
        for (int i = from; i < to; i++) revealed.Add(i);
        return revealed;
    }

    private int BoundaryFor(int packs)
    {
        var index = Math.Clamp(packs, 0, _boundaries.Count - 1);
        return Math.Min(_boundaries[index], Slots.Count);
    }
}
