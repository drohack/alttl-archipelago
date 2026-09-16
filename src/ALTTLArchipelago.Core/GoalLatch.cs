namespace ALTTLArchipelago.Core;

/// <summary>
/// Whether the run is won, and whether that still needs saying.
///
/// Two separate latches over one condition, which is why this is worth its own
/// type rather than a pair of bools next to the poll:
///
/// - ANNOUNCE, for the player: said once, when the credits become available.
/// - REPORT, for the server: sent once per session, and re-attempted on the
///   next one until it lands.
///
/// THIS LATCH CANNOT CLOSE ON AN ACKNOWLEDGEMENT, and it used to claim it did.
/// The client library exposes SetGoalAchieved with no async or callback form,
/// so "the send left" is the strongest signal available and nothing can wait
/// for more. What makes that safe is the other half: the credits-played flag
/// is persisted, so a reconnect re-arms this latch and sends again, and a
/// repeated goal is idempotent to the server. Re-attempting until it sticks is
/// the compensation for having no ack - not a comment claiming one exists.
///
/// The condition is "nothing left to beat AND the Credits item is held". That
/// opens the credits card and is said to the player at once.
///
/// REPORTING WAITS FOR THE CREDITS TO BE PLAYED, AND ONLY FOR THAT. This used
/// to fire at the same instant, on the reasoning that a finished seed is
/// finished and a multiworld should not wait on someone watching an animation.
/// In play it read as the run ending without an ending: droha beat the puzzle
/// that granted the item and "that instant it said i completed the game. I
/// didn't have to go out and play the credits at all", with the credits card
/// left on the track as something already made pointless.
///
/// So the ending is something you do: reach the card, click it, watch it.
///
/// NO TIMEOUT. A five-minute fallback was built first and then removed on
/// droha's explicit call - "there should be no fallback. the user needs to
/// click on the credits level to finish". A run that reports itself after a
/// wait is the same run ending on its own that this change exists to stop; it
/// would just do it more quietly. The cost is real and accepted: a player who
/// wins and never plays the credits never reports, and a multiworld waiting on
/// that slot waits.
/// </summary>
public sealed class GoalLatch
{
    private bool _announced;
    private bool _reported;
    private bool _played;

    /// <param name="creditsAlreadyPlayed">
    /// True when the run's saved state says the credits were played in an
    /// earlier session. Without this a reconnect or relaunch lost the fact and
    /// the goal could never be reported - see RunState.CreditsPlayed.
    /// </param>
    public GoalLatch(bool creditsAlreadyPlayed = false)
    {
        _played = creditsAlreadyPlayed;
    }

    /// <summary>Have the credits been played?</summary>
    public bool Played => _played;

    /// <summary>Has the server been told, successfully?</summary>
    public bool Reported => _reported;

    /// <summary>Has the player been told?</summary>
    public bool Announced => _announced;

    /// <summary>
    /// Should the player be told the credits are open, right now? True at most
    /// once per run.
    /// </summary>
    public bool ShouldAnnounce(int remaining, bool hasCreditsItem)
    {
        if (_announced) return false;
        if (!IsWon(remaining, hasCreditsItem)) return false;

        _announced = true;
        return true;
    }

    /// <summary>
    /// The player reached the credits card and played it. The run is over in
    /// the way the player experiences it, so stop waiting.
    /// </summary>
    public void CreditsPlayed() => _played = true;

    /// <summary>
    /// Should the goal be sent to the server right now?
    ///
    /// Ask every poll while it returns true. It keeps returning true until
    /// <see cref="Sent"/> confirms one landed, because the run can be won while
    /// disconnected and a send that never left is not a report.
    ///
    /// Won is not enough on its own: the credits have to have been played.
    /// There is no clock here and no wall-clock read anywhere in this type,
    /// so the only thing that can end a run is the player finishing it.
    /// </summary>
    public bool ShouldReport(int remaining, bool hasCreditsItem)
        => !_reported && IsWon(remaining, hasCreditsItem) && _played;

    /// <summary>The send succeeded. Stop asking.</summary>
    public void Sent() => _reported = true;

    private static bool IsWon(int remaining, bool hasCreditsItem)
        => remaining <= 0 && hasCreditsItem;
}
