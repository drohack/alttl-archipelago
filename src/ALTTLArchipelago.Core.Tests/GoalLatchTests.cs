using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Reporting the run as won. Failing to report strands a multiworld waiting on
/// this player; reporting early claims a seed that is not finished.
///
/// The rule changed: winning opens the credits, and PLAYING them reports it.
/// Every test that used to assert an immediate report now spells out which of
/// the two triggers it is exercising, because "won" alone no longer sends.
/// </summary>
public class GoalLatchTests
{
    /// <summary>A latch that has been won and had its credits played.</summary>
    private static GoalLatch Finished()
    {
        var latch = new GoalLatch();
        latch.CreditsPlayed();
        return latch;
    }

    [Fact]
    public void NotWonUntilBothHalvesHold()
    {
        var latch = Finished();

        Assert.False(latch.ShouldReport(remaining: 3, hasCreditsItem: true));
        Assert.False(latch.ShouldReport(remaining: 0, hasCreditsItem: false));
        Assert.True(latch.ShouldReport(remaining: 0, hasCreditsItem: true));
    }

    [Fact]
    public void WinningIsNotEnough_TheCreditsHaveToBePlayed()
    {
        // The report droha made: beating the puzzle that granted the Credits
        // item ended the run on the spot, with the credits card left on the
        // track as something already pointless. Winning now opens the card and
        // says so; it does not finish the run by itself.
        var latch = new GoalLatch();

        Assert.True(latch.ShouldAnnounce(0, true));
        Assert.False(latch.ShouldReport(0, true));

        latch.CreditsPlayed();
        Assert.True(latch.ShouldReport(0, true));
    }

    [Fact]
    public void NothingButPlayingTheCreditsEverReports()
    {
        // There is no timeout, by droha's explicit call: "there should be no
        // fallback. the user needs to click on the credits level to finish".
        // A won run asked a thousand times still does not report, because the
        // only thing that can finish it is the player.
        var latch = new GoalLatch();

        for (int i = 0; i < 1000; i++)
        {
            Assert.False(latch.ShouldReport(0, true));
        }

        latch.CreditsPlayed();
        Assert.True(latch.ShouldReport(0, true));
    }

    [Fact]
    public void TheGoalIsSentOnlyOnce()
    {
        var latch = Finished();

        Assert.True(latch.ShouldReport(0, true));
        latch.Sent();

        Assert.False(latch.ShouldReport(0, true));
        Assert.True(latch.Reported);
    }

    [Fact]
    public void AGoalReachedOfflineKeepsAskingUntilASendLands()
    {
        // The condition holds but the send fails, repeatedly, because there is
        // no server. Sent() is never called, so the poll must keep offering it
        // - otherwise winning while disconnected loses the run's completion.
        var latch = Finished();

        Assert.True(latch.ShouldReport(0, true));
        Assert.True(latch.ShouldReport(0, true));
        Assert.True(latch.ShouldReport(0, true));

        latch.Sent();
        Assert.False(latch.ShouldReport(0, true));
    }

    [Fact]
    public void ThePlayerIsToldOnce()
    {
        var latch = new GoalLatch();

        Assert.True(latch.ShouldAnnounce(0, true));
        Assert.False(latch.ShouldAnnounce(0, true));
    }

    [Fact]
    public void TheAnnouncementDoesNotWaitForTheCredits()
    {
        // Told at once, reported later. The player learns they have won the
        // moment they have, and then goes and watches the ending.
        var latch = new GoalLatch();

        Assert.True(latch.ShouldAnnounce(0, true));
        Assert.False(latch.Played);
    }

    [Fact]
    public void PlayingTheCreditsTwiceChangesNothing()
    {
        var latch = new GoalLatch();
        latch.CreditsPlayed();
        latch.CreditsPlayed();

        Assert.True(latch.ShouldReport(0, true));
        latch.Sent();
        Assert.False(latch.ShouldReport(0, true));
    }

    [Fact]
    public void ARunThatPlayedTheCreditsInAnEarlierSessionStillOwesTheGoal()
    {
        // THE BUG THIS EXISTS FOR. Reporting requires the credits to have been
        // played, and Credits.Reset() builds a new latch on every reconnect
        // and every offline start. While the flag lived only here, a player
        // who finished offline, played the credits and reconnected had it
        // wiped, and the goal was never sent - a multiworld waiting forever on
        // a slot that had genuinely finished. RunState persists it now and
        // hands it back through this constructor.
        var restored = new GoalLatch(creditsAlreadyPlayed: true);

        Assert.True(restored.Played);
        Assert.True(restored.ShouldReport(remaining: 0, hasCreditsItem: true));
    }

    [Fact]
    public void AFreshLatchDoesNotAssumeTheCreditsWerePlayed()
    {
        var fresh = new GoalLatch();

        Assert.False(fresh.Played);
        Assert.False(fresh.ShouldReport(remaining: 0, hasCreditsItem: true));
    }

    [Fact]
    public void ARestoredLatchStillWaitsForTheRestOfTheGoal()
    {
        // Carrying the flag back must not shortcut the other condition: the
        // credits being played says nothing about the count or the item.
        var restored = new GoalLatch(creditsAlreadyPlayed: true);

        Assert.False(restored.ShouldReport(remaining: 3, hasCreditsItem: true));
        Assert.False(restored.ShouldReport(remaining: 0, hasCreditsItem: false));
        Assert.True(restored.ShouldReport(remaining: 0, hasCreditsItem: true));
    }

    [Fact]
    public void AReconnectReSendsTheGoalBecauseNothingAcknowledgesIt()
    {
        // Deliberate, and the opposite of what this file used to assert. The
        // client library has no async or callback form of SetGoalAchieved, so
        // "the send left" is all we ever know. A new session therefore starts
        // owing the goal again and sends until one lands; the server takes a
        // repeat as idempotent. That re-attempt is only reachable because the
        // played flag survives, which is the test above.
        var first = new GoalLatch(creditsAlreadyPlayed: true);
        Assert.True(first.ShouldReport(0, true));
        first.Sent();
        Assert.False(first.ShouldReport(0, true));

        var nextSession = new GoalLatch(creditsAlreadyPlayed: true);

        Assert.True(nextSession.ShouldReport(0, true));
    }

    [Fact]
    public void MoreBeatenThanNeededStillCounts()
    {
        // Remaining is a subtraction and can go negative if the goal shrinks or
        // extra Beaten tokens arrive. That is still won.
        var latch = Finished();

        Assert.True(latch.ShouldReport(remaining: -2, hasCreditsItem: true));
    }
}
