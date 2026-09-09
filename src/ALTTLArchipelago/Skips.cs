using System;
using HarmonyLib;
using ALTTLModKit;

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


    /// <summary>
    /// A skip is in flight, so the next level completion is not a win.
    ///
    /// Consumed by the completion handler rather than cleared on a timer: one
    /// skip produces exactly one completion. It is also cleared whenever a
    /// level starts, so a skip that somehow never completes cannot leave the
    /// flag set and silently swallow the NEXT genuine Beaten token.
    /// </summary>
    internal static bool Skipping { get; set; }


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
                Plugin.Logger.LogInfo("skip: refused, none held");
                Toasts.Show("No Skip available - find one to skip a puzzle",
                            Toasts.Notice);
                return false;
            }

            RunState.SpendSkip();

            // Tell the check side that the completion about to arrive came
            // from a skip.
            //
            // The game fires LevelComplete for a skipped level exactly as for a
            // solved one - measured, the order is LevelComplete then
            // LevelSkipped - so the completion handler cannot tell them apart
            // on its own. This prefix runs before the game's own skip work, so
            // the flag is set before the completion fires.
            //
            // What the flag is FOR changed in 0.3.1. It used to suppress the
            // Beaten token, so that Skips could not reach the credits without
            // solving anything; the cost was a skipped card that could never
            // complete, because the star needs every location on the slot. On
            // droha's call a skip now finishes the puzzle outright and sends
            // the whole slot, and the shortcut is bounded by skip_count rather
            // than forbidden. See Checks.OnLevelComplete.
            Skipping = true;

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
