using System;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago;

/// <summary>
/// The finale: the credits card, and telling the server the run is won.
///
/// Two conditions, deliberately separate:
///
/// - the Credits ITEM has arrived, which is what puts the card on the track at
///   all. Until then the run has no ending in it.
/// - enough puzzles have been BEATEN, which is what unlocks the card.
///
/// Beaten means completed, not skipped. The count comes from Level Beaten
/// tokens, which are granted by an event location for finishing a puzzle - so a
/// skipped one contributes nothing, and the mod does not need its own rule to
/// make that true. The generator's logic already assumes exactly this.
/// </summary>
internal static class Credits
{
    private static bool _reported;
    private static bool _announced;
    private static float _sinceCheck;


    internal static void Reset()
    {
        _reported = false;
        _announced = false;
        _sinceCheck = 0f;
    }

    /// <summary>How many more puzzles must be beaten before the credits open.</summary>
    internal static int Remaining(SlotData? slot)
        => slot == null ? 0 : Math.Max(0, slot.LevelsToBeat - Checks.LevelsBeaten);

    /// <summary>
    /// Watch for the run being won.
    ///
    /// A poll rather than a hook on the beaten token arriving, because the goal
    /// also becomes true on a reconnect that replays the whole item list - and
    /// the version that only reacts to new arrivals would miss it every time.
    /// </summary>
    internal static void Tick(float dt, SlotData? slot, Connection? session)
    {
        if (slot == null || !Track.Active) return;

        _sinceCheck += dt;
        if (_sinceCheck < 2f) return;
        _sinceCheck = 0f;

        try
        {
            var left = Remaining(slot);

            if (left == 0 && Inventory.HasCredits && !_announced)
            {
                _announced = true;
                Plugin.Logger.LogInfo(
                    $"credits: unlocked after {Checks.LevelsBeaten} puzzles");
                Toasts.Show("The credits are unlocked", Toasts.Notice);
            }

            if (_reported) return;

            // The run is won when the goal condition holds. Reported as soon as
            // it does rather than when the credits card is played: a player who
            // has met the condition has finished the seed, and holding their
            // completion hostage to watching an animation would strand a
            // multiworld waiting on them.
            if (left > 0 || !Inventory.HasCredits) return;

            if (session != null && session.ReportGoal())
            {
                _reported = true;
                Plugin.Logger.LogInfo("goal: reported to the server");
                Toasts.Show("Run complete", Toasts.Notice);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"credits: check failed: {e.Message}");
        }
    }
}
