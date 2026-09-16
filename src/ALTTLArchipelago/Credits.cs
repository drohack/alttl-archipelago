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
    ///
    /// IT CARRIES THE CREDITS-PLAYED FLAG BACK IN, and that is not optional.
    /// Reporting the goal requires the credits to have been played, and this
    /// method replaces the latch wholesale - so before RunState persisted the
    /// flag, finishing a run offline, playing the credits and reconnecting
    /// wiped the one fact the report depends on, and the goal was never sent.
    /// The multiworld then waited forever on a slot that had genuinely
    /// finished. Same for a relaunch between the credits and the report.
    ///
    /// Reset still clears "already reported", deliberately: the client
    /// library gives no acknowledgement, so re-sending on the next connection
    /// is how a goal that never left eventually lands. The server takes a
    /// repeat as idempotent.
    ///
    /// Called AFTER RunState.Begin on both paths (Plugin.cs, the offline start
    /// and OnReady), so the flag is loaded by the time this reads it.
    /// </summary>
    internal static void Reset()
    {
        _latch = new GoalLatch(RunState.CreditsPlayed);
        _sinceCheck = 0f;
        if (RunState.CreditsPlayed)
        {
            Plugin.Logger.LogInfo(
                "credits: this run already played them, so the goal is still "
                + "owed to the server until a send lands");
        }
    }

    /// <summary>
    /// How many more puzzles the goal still wants before the credits open.
    ///
    /// Beaten or starred, depending on the seed - Checks.GoalProgress owns
    /// that choice so this does not have to. GoalLatch below takes the
    /// number and nothing else, so the two goals need no code of their own
    /// past this line.
    /// </summary>
    internal static int Remaining(SlotData? slot)
    {
        var (done, needed, _) = Checks.GoalProgress(slot);
        return Math.Max(0, needed - done);
    }

    /// <summary>
    /// The credits card was played through to the end.
    ///
    /// Told by Checks, which sees the completion event. The credits are not a
    /// slot and hold no checks, so everything else in that handler ignores
    /// them - this is the one thing that cares.
    /// </summary>
    internal static void NotePlayed()
    {
        if (_latch.Played) return;
        _latch.CreditsPlayed();
        // Persisted immediately rather than at the next flush: the report may
        // not be sendable for a long time (an offline finish), and the process
        // can end before it is.
        RunState.NoteCreditsPlayed();
        Plugin.Logger.LogInfo("credits: played to the end");
    }

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
                Toasts.Show("The credits are unlocked - play them to finish "
                            + "the run", Toasts.Notice);
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
