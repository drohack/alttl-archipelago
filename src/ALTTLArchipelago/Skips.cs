using System;
using ALTTLArchipelago.Core;
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
/// A SKIP FINISHES THE PUZZLE, and that changed in 0.3.1: it sends the whole
/// slot and banks the Beaten token (Checks.ReleaseSlot), bounded by
/// skip_count rather than forbidden.
///
/// ON EVERY LEVEL, droha 2026-09-25: the game's own skip where it skips, and
/// where it will not (its Skippable flag), the mod releases the slot itself,
/// spends the Skip and goes back to the level select. Which of those
/// happens, and the single charge, is Core's SkipFlow.
/// </summary>
internal static class Skips
{
    /// <summary>How many are held and unspent.</summary>
    internal static int Available
        => Math.Max(0, Inventory.SkipsHeld - RunState.SkipsUsed);

    /// <summary>The pending Skip, if any.</summary>
    internal static readonly SkipFlow Flow = new();

    /// <summary>A slot to release on the next frame (the fallback), or -1.</summary>
    private static int _fallbackSlot = -1;

    /// <summary>
    /// Refuse a skip nobody has paid for, and arm the one that is.
    ///
    /// A prefix on the game's own SkipLevel, so every route to it is covered
    /// rather than just the button - the tooltip's hold-to-skip, the pause
    /// menu and DevTools' `skip` all end up here.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.SkipLevel))]
    [HarmonyPrefix]
    private static bool BeforeSkipLevel()
    {
        try
        {
            if (!Track.Active) return true;      // not a run; vanilla rules

            var slot = Checks.CurrentSlot;
            var router = Checks.Router;
            var workLeft = router == null || slot < 0
                           || router.HasWorkLeft(slot, Checks.Ledger.IsCollected);
            switch (Flow.Request(slot, Available, workLeft))
            {
                case SkipRefusal.NotARunSlot:
                    // Not one of the run's puzzles: nothing to grant or charge.
                    return true;
                case SkipRefusal.NoneHeld:
                    Plugin.Logger.LogInfo("skip: refused, none held");
                    Toasts.Show("No Skip available - find one to skip a puzzle",
                                Toasts.Notice);
                    return false;
                case SkipRefusal.NothingLeft:
                    // Spending one here would be pure loss, written to disk
                    // with no refund path.
                    Plugin.Logger.LogInfo(
                        $"skip: refused, slot {slot} has nothing left to find");
                    Toasts.Show("Nothing left to find here - the Skip was not used",
                                Toasts.Notice);
                    return false;
            }

            // Armed, not charged: the charge comes when the game's
            // LevelSkipped arrives (OnLevelSkipped) or, on a level the game
            // will not skip, from the fallback (AfterSkipLevel).
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

    /// <summary>
    /// The game's SkipLevel has returned. A skip it completed inside the call
    /// is already charged; a level it will not skip gets the fallback on the
    /// next frame; a level that may still skip late stays armed.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.SkipLevel))]
    [HarmonyPostfix]
    private static void AfterSkipLevel()
    {
        try
        {
            if (!Track.Active) return;
            var slot = Checks.CurrentSlot;
            switch (Flow.Returned(slot, GameWillSkip()))
            {
                case SkipReturn.Fallback:
                    // Next frame, not from inside the game's own call - the same
                    // reason DlcGuard arms rather than switching state here.
                    _fallbackSlot = slot;
                    Plugin.Logger.LogInfo(
                        $"skip: the game does not skip this level; releasing slot {slot} instead");
                    break;
                case SkipReturn.StayArmed:
                    Plugin.Logger.LogInfo("skip: waiting for the game to finish the skip");
                    break;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"skip: after-skip check failed: {e.Message}");
        }
    }

    /// <summary>The game's LevelSkipped: release the rest of the slot, charge once.</summary>
    internal static void OnLevelSkipped(int slot)
    {
        if (!Flow.Skipped(slot)) return;
        Checks.ReleaseSlot(slot);
        Charge();
    }

    /// <summary>
    /// The fallback, one frame after SkipLevel returned on a level the game
    /// will not skip: release the slot, charge, and leave by the pause menu's
    /// route to the run's track. The mod forces no controllers.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (_fallbackSlot < 0) return;
        var slot = _fallbackSlot;
        _fallbackSlot = -1;
        if (!Track.Active || slot != Checks.CurrentSlot) return;

        Checks.ReleaseSlot(slot);
        Charge();
        Navigation.GoToTrack("skip");
    }

    /// <summary>Whether the game skips the running level (its own flag).</summary>
    private static bool GameWillSkip()
    {
        var level = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        return level != null && level.Skippable;
    }

    private static void Charge()
    {
        RunState.SpendSkip();
        Plugin.Logger.LogInfo($"skip: spent one, {Available} left");
        Toasts.Show($"Skip used - {Available} left", Toasts.Notice);
    }
}
