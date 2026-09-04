using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Reporting the run as won. Failing to report strands a multiworld waiting on
/// this player; reporting early claims a seed that is not finished.
/// </summary>
public class GoalLatchTests
{
    [Fact]
    public void NotWonUntilBothHalvesHold()
    {
        var latch = new GoalLatch();

        Assert.False(latch.ShouldReport(remaining: 3, hasCreditsItem: true));
        Assert.False(latch.ShouldReport(remaining: 0, hasCreditsItem: false));
        Assert.True(latch.ShouldReport(remaining: 0, hasCreditsItem: true));
    }

    [Fact]
    public void TheGoalIsSentOnlyOnce()
    {
        var latch = new GoalLatch();

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
        var latch = new GoalLatch();

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
    public void AReconnectReArmsTheAnnouncementButNotTheReport()
    {
        var latch = new GoalLatch();
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
        var latch = new GoalLatch();

        Assert.True(latch.ShouldReport(remaining: -2, hasCreditsItem: true));
    }
}
