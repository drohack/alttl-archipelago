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
    public void AReconnectReArmsTheAnnouncementButNotTheReport()
    {
        var latch = Finished();
        latch.ShouldAnnounce(0, true);
        Assert.True(latch.ShouldReport(0, true));
        latch.Sent();

        latch.Reconnected();

        Assert.True(latch.ShouldAnnounce(0, true));
        Assert.False(latch.ShouldReport(0, true));
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
