using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// The game's skip, gated on holding a Skip item.
///
/// Skipping is normally free once a puzzle has sat unsolved for a while, which
/// in a randomizer would let anyone walk past every gate in the seed. Here it
/// costs an item, and each one is spent when used.
///
/// Note what a skipped puzzle does NOT do: it does not count toward the credits
/// goal. The generator's logic already assumes that - the "beat N levels" gate
/// counts Level Beaten tokens, which come from completing a puzzle, not from
/// leaving it. So no extra bookkeeping is needed here to keep the two in step.
/// </summary>
internal static class Skips
{
    /// <summary>How many are held and unspent.</summary>
    internal static int Available
        => Math.Max(0, Inventory.SkipsHeld - RunState.SkipsUsed);

    /// <summary>Counts refusals, so a test can assert the patch FIRED.</summary>
    internal static int Refusals { get; private set; }
    internal static int Spent { get; private set; }

    /// <summary>
    /// Refuse a skip nobody has paid for.
    ///
    /// A prefix on the game's own SkipLevel, so every route to it is covered
    /// rather than just the button - the tooltip's hold-to-skip and the pause
    /// menu both end up here.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.SkipLevel))]
    [HarmonyPrefix]
    private static bool BeforeSkipLevel()
    {
        try
        {
            if (!Track.Active) return true;      // not a run; vanilla rules

            if (Available <= 0)
            {
                Refusals++;
                Plugin.Logger.LogInfo("skip: refused, none held");
                Toasts.Show("No Skip available - find one to skip a puzzle",
                            Toasts.Notice);
                return false;
            }

            RunState.SpendSkip();
            Spent++;
            Plugin.Logger.LogInfo($"skip: spent one, {Available} left");
            Toasts.Show($"Skip used - {Available} left", Toasts.Notice);
            return true;
        }
        catch (Exception e)
        {
            // Fail open. A player stuck on a puzzle with no way past is worse
            // than one who got a skip they had not earned.
            Plugin.Logger.LogWarning($"skip: gate failed, allowing: {e.Message}");
            return true;
        }
    }
}
