using System;
using ALTTLArchipelago.Core;
using ALTTLModKit;

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
/// The count comes from Level Beaten tokens, granted by an event location for
/// finishing a puzzle. The mod needs no rule of its own for this: it simply
/// counts what arrived.
///
/// A SKIP now finishes a puzzle and grants that token, so a skipped puzzle
/// does count. That reversed in 0.3.1 - the token used to be withheld so Skips
/// could not shortcut the goal, and the price was a skipped card that could
/// never reach its star, because the star needs every location on the slot.
/// The shortcut is now bounded by skip_count rather than forbidden. See
/// Checks.OnLevelComplete and the SkipCount option text.
/// </summary>
internal static class Credits
{
    private static GoalLatch _latch = new();
    private static float _sinceCheck;

    /// <summary>
    /// A fresh run. Called on connect, so a new seed does not inherit the last
    /// one's "already reported".
    /// </summary>
    internal static void Reset()
    {
        _latch = new GoalLatch();
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
            // Both latches live in Core, where they are tested. This is the
            // Unity half: ask, then do the talking and the sending.
            var left = Remaining(slot);
            var held = Inventory.HasCredits;

            if (_latch.ShouldAnnounce(left, held))
            {
                Plugin.Logger.LogInfo(
                    $"credits: unlocked after {Checks.LevelsBeaten} puzzles");
                Toasts.Show("The credits are unlocked", Toasts.Notice);
            }

            if (!_latch.ShouldReport(left, held)) return;

            if (session != null && session.ReportGoal())
            {
                _latch.Sent();
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
