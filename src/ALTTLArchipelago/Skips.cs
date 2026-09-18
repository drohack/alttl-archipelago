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
/// A SKIP FINISHES THE PUZZLE, and that changed in 0.3.1. This used to say the
/// opposite - that a skipped puzzle does not count toward the credits, because
/// the goal counts Level Beaten tokens and a skip suppressed the token. The
/// cost was a skipped card that could never complete, since its star needs
/// every location on the slot. On droha's call a skip now sends the whole slot
/// and banks the token, and the shortcut is bounded by skip_count rather than
/// forbidden. See the note on Skipping below and Checks.OnLevelComplete.
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
    ///
    /// A completion DOES arrive even on a puzzle already beaten in an earlier
    /// session - measured 2026-09-18, against the claim in release_e2e.py and
    /// manual-container-test.md that it does not. Re-entering a beaten level
    /// reloads it, and a reloaded level completes and skips like any other.
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

            // NOTHING LEFT TO BUY. A slot whose every location is already ours
            // has nothing a skip can grant, so spending one there is pure loss
            // - and the spend is written to disk with no refund path anywhere.
            // Refused rather than consumed.
            var slot = Checks.CurrentSlot;
            var router = Checks.Router;
            if (router != null && slot >= 0
                && !router.HasWorkLeft(slot, Checks.Ledger.IsCollected))
            {
                Plugin.Logger.LogInfo(
                    $"skip: refused, slot {slot} has nothing left to find");
                Toasts.Show("Nothing left to find here - the Skip was not used",
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

            // THE PAYOUT STAYS IN Checks.OnLevelComplete, and it was worth
            // measuring rather than assuming.
            //
            // This project believed - in a comment in release_e2e.py, and in
            // docs/manual-container-test.md - that a skip on an ALREADY-BEATEN
            // puzzle granted nothing, because "a beaten level never fires
            // LevelComplete again". Measured on 2026-09-18 against a build
            // with this file at its pre-fix state: it does fire. Beating DLC1
            // Filing Cabinet, re-entering it and skipping logged
            // LevelCompleteEarly, LevelComplete, then Solutions 2 and 3, then
            // LevelSkipped. The premise was simply wrong, and it has to be -
            // the only way to press Skip is to be standing in a loaded level,
            // and loading it makes it live again.
            //
            // So paying out here as well would be redundant, and not free: it
            // would bank the Beaten token before the game's own skip has
            // happened. Left alone.
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
