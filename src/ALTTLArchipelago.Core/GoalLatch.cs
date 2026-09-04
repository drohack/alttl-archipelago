namespace ALTTLArchipelago.Core;

/// <summary>
/// Whether the run is won, and whether that still needs saying.
///
/// Two separate latches over one condition, which is why this is worth its own
/// type rather than a pair of bools next to the poll:
///
/// - ANNOUNCE, for the player: said once, when the credits become available.
/// - REPORT, for the server: sent once, and only counted as sent when the send
///   actually succeeded. A goal reached while offline must be re-attempted, so
///   this latch closes on the acknowledgement, not on the attempt.
///
/// The condition is "nothing left to beat AND the Credits item is held". It is
/// reported as soon as it holds, NOT when the credits card is played: a player
/// who has met the condition has finished the seed, and holding their
/// completion hostage to watching an animation would strand a multiworld
/// waiting on them.
/// </summary>
public sealed class GoalLatch
{
    private bool _announced;
    private bool _reported;

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
    /// Should the goal be sent to the server right now?
    ///
    /// Ask every poll while it returns true. It keeps returning true until
    /// <see cref="Sent"/> confirms one landed, because the run can be won while
    /// disconnected and a send that never left is not a report.
    /// </summary>
    public bool ShouldReport(int remaining, bool hasCreditsItem)
        => !_reported && IsWon(remaining, hasCreditsItem);

    /// <summary>The send succeeded. Stop asking.</summary>
    public void Sent() => _reported = true;

    /// <summary>
    /// A reconnect re-arms the announcement but NOT the report.
    ///
    /// Re-announcing is harmless and the player may have missed it; re-sending
    /// a goal is not harmful either - the server takes it as idempotent - but
    /// there is no reason to, and keeping the latch closed makes the log honest
    /// about how many times the run was actually won.
    /// </summary>
    public void Reconnected() => _announced = false;

    private static bool IsWon(int remaining, bool hasCreditsItem)
        => remaining <= 0 && hasCreditsItem;
}
