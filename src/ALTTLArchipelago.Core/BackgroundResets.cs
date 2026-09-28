namespace ALTTLArchipelago.Core;

/// <summary>Why a Background Reset Token was not spent.</summary>
public enum BackgroundResetRefusal
{
    None,
    NoneHeld,
    NothingToReset,
}

/// <summary>
/// The arithmetic of the Background Reset Token. Pure, so it is tested here;
/// the plugin owns the button and the colours.
///
/// A token does not remove traps - the server replays them on every connect.
/// It marks the trap count (RunStateData.BackgroundResetAt), and every
/// backdrop is drawn from the traps received since that mark.
/// </summary>
public static class BackgroundResets
{
    /// <summary>Tokens the player can still spend.</summary>
    public static int Available(int received, int used) => Math.Max(0, received - used);

    /// <summary>The trap count the backdrops are drawn from: 0 means the game's own.</summary>
    public static int EffectiveTraps(int traps, int resetAt) => Math.Max(0, traps - resetAt);

    /// <summary>Whether a press may spend a token now.</summary>
    public static BackgroundResetRefusal Check(int available, int traps, int resetAt)
    {
        if (available <= 0) return BackgroundResetRefusal.NoneHeld;
        if (EffectiveTraps(traps, resetAt) <= 0) return BackgroundResetRefusal.NothingToReset;
        return BackgroundResetRefusal.None;
    }
}
