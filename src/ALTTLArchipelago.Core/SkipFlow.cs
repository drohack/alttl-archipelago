namespace ALTTLArchipelago.Core;

/// <summary>Why a Skip was not armed.</summary>
public enum SkipRefusal
{
    None,
    NotARunSlot,
    NoneHeld,
    NothingLeft,
}

/// <summary>What to do once the game's SkipLevel has returned.</summary>
public enum SkipReturn
{
    /// <summary>No Skip is pending for this slot.</summary>
    Idle,

    /// <summary>The game skipped inside the call; the Skip is already charged.</summary>
    Done,

    /// <summary>
    /// The game will not skip this level: release every location on the slot,
    /// bank Beaten, charge the Skip and go back to the level select.
    /// </summary>
    Fallback,

    /// <summary>The game may still skip late; LevelSkipped charges it.</summary>
    StayArmed,
}

/// <summary>
/// One Skip, from the press to the charge. Pure: the plugin feeds it the
/// game's events. droha, 2026-09-25: a Skip must work on every level - the
/// game's own skip, or for a level it will not skip, release the locations,
/// use the Skip and go back to the level select - and it is spent exactly
/// once, only when it did something.
///
/// The normal LevelComplete payout always runs; the game's LevelSkipped is
/// what marks a completion as the skip's (Skipped), so the rest of the slot
/// is sent and the Skip charged there. Returned tells a level the game will
/// not skip (by its own Skippable flag) from one that may skip late.
/// </summary>
public sealed class SkipFlow
{
    private int _armedSlot = -1;

    // The slot Skipped charged since the last press, so the Returned that
    // follows a skip completed inside the call says Done rather than Idle.
    private int _chargedSlot = -1;

    /// <summary>A paid-for Skip is waiting for the game.</summary>
    public bool Armed => _armedSlot >= 0;

    /// <summary>The slot the pending Skip was pressed on, or -1.</summary>
    public int ArmedSlot => _armedSlot;

    /// <summary>The player pressed Skip on this slot.</summary>
    public SkipRefusal Request(int slot, int available, bool workLeft)
    {
        _chargedSlot = -1;
        if (slot < 0) return SkipRefusal.NotARunSlot;
        if (available <= 0) return SkipRefusal.NoneHeld;
        if (!workLeft) return SkipRefusal.NothingLeft;
        _armedSlot = slot;
        return SkipRefusal.None;
    }

    /// <summary>
    /// The game's LevelSkipped for this slot. True exactly once per Skip: send
    /// the rest of the slot and charge it.
    /// </summary>
    public bool Skipped(int slot)
    {
        if (!Armed || slot != _armedSlot) return false;
        _armedSlot = -1;
        _chargedSlot = slot;
        return true;
    }

    /// <summary>The game's SkipLevel has returned on this slot.</summary>
    public SkipReturn Returned(int slot, bool skippable)
    {
        if (!Armed && slot >= 0 && slot == _chargedSlot)
        {
            _chargedSlot = -1;
            return SkipReturn.Done;
        }
        if (!Armed || slot != _armedSlot) return SkipReturn.Idle;
        if (skippable) return SkipReturn.StayArmed;
        _armedSlot = -1;
        return SkipReturn.Fallback;
    }

    /// <summary>A level started: a Skip that never landed is dropped, uncharged.</summary>
    public void Entered(int slot)
    {
        _armedSlot = -1;
        _chargedSlot = -1;
    }
}
