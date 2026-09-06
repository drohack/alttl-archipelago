using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void ItStopsAfterTheAllowedAttempts()
    {
        var policy = new RetryPolicy(maxAttempts: 5);

        for (int i = 1; i <= 4; i++)
        {
            Assert.NotNull(policy.Failed("nope"));
            Assert.False(policy.GaveUp);
        }

        // The fifth failure is the last one: no delay comes back, because
        // there is nothing left to wait for.
        Assert.Null(policy.Failed("nope"));
        Assert.True(policy.GaveUp);
    }

    [Fact]
    public void TheWaitGrowsAndThenLevelsOff()
    {
        var policy = new RetryPolicy(firstDelaySeconds: 1, maxDelaySeconds: 8);

        Assert.Equal(1, policy.DelayForAttempt(1));
        Assert.Equal(2, policy.DelayForAttempt(2));
        Assert.Equal(4, policy.DelayForAttempt(3));
        Assert.Equal(8, policy.DelayForAttempt(4));
        // Capped - an ever-growing wait stops reading as "reconnecting".
        Assert.Equal(8, policy.DelayForAttempt(5));
        Assert.Equal(8, policy.DelayForAttempt(50));
    }

    [Fact]
    public void ResetGivesAFullSetOfTriesAgain()
    {
        var policy = new RetryPolicy(maxAttempts: 2);
        policy.Failed("a");
        policy.Failed("b");
        Assert.True(policy.GaveUp);

        // Pressing Connect is the player saying something has changed.
        policy.Reset();
        Assert.False(policy.GaveUp);
        Assert.Equal(0, policy.Attempts);
        Assert.Equal("", policy.LastError);
    }

    [Fact]
    public void NonsenseSettingsDegradeRatherThanThrow()
    {
        // These come from a config file a player can edit. Zero attempts is no
        // longer nonsense - it is how "keep trying" is spelled - so the nonsense
        // here is the negative, and the delays.
        var junk = new RetryPolicy(maxAttempts: -4, firstDelaySeconds: 0,
                                   maxDelaySeconds: -5);
        Assert.True(junk.DelayForAttempt(1) > 0);
        Assert.True(junk.IsUnlimited);
        Assert.NotNull(junk.Failed("still going"));
    }

    [Fact]
    public void TheDefaultPolicyRetriesForever()
    {
        // Archipelago's own client is unbounded, and a bounded policy is worse
        // than it sounds: it gives up quietly, and the button to start again
        // was itself broken while an attempt was in flight.
        var policy = new RetryPolicy();

        Assert.True(policy.IsUnlimited);
        for (int i = 0; i < 50; i++)
        {
            Assert.NotNull(policy.Failed("server is down"));
            Assert.False(policy.GaveUp);
        }
    }

    [Fact]
    public void TheDefaultScheduleIsFiveTenTwentyFortySixty()
    {
        // Doubling to a one-minute ceiling. Without the cap, pure doubling
        // reaches an hour by the twelfth attempt, which a player cannot tell
        // apart from the mod having given up.
        var policy = new RetryPolicy();

        Assert.Equal(5, policy.DelayForAttempt(1));
        Assert.Equal(10, policy.DelayForAttempt(2));
        Assert.Equal(20, policy.DelayForAttempt(3));
        Assert.Equal(40, policy.DelayForAttempt(4));
        Assert.Equal(60, policy.DelayForAttempt(5));
        Assert.Equal(60, policy.DelayForAttempt(20));
    }

    [Fact]
    public void ABoundedPolicyStillGivesUp()
    {
        // Kept available: the tests below use it, and a player may set
        // MaxRetries to a number if they would rather it stopped.
        var policy = new RetryPolicy(maxAttempts: 2);

        Assert.False(policy.IsUnlimited);
        Assert.NotNull(policy.Failed("one"));
        Assert.Null(policy.Failed("two"));
        Assert.True(policy.GaveUp);
    }

    [Fact]
    public void TheStatusLineSaysSomethingUsefulInEveryState()
    {
        var policy = new RetryPolicy(maxAttempts: 3);

        Assert.Equal("Not connected", policy.Describe(false, "droha"));
        Assert.Equal("Connected as droha", policy.Describe(true, "droha"));

        policy.Failed("connection refused");
        Assert.Contains("attempt 2 of 3", policy.Describe(false, "droha"));

        policy.Failed("connection refused");
        policy.Failed("connection refused");
        var final = policy.Describe(false, "droha");
        Assert.Contains("Gave up", final);
        // The reason matters most - "it failed" is not actionable.
        Assert.Contains("connection refused", final);
    }

    [Fact]
    public void ConnectingSuccessfullyIsReportedEvenAfterFailures()
    {
        var policy = new RetryPolicy();
        policy.Failed("timeout");
        Assert.Equal("Connected as droha", policy.Describe(true, "droha"));
    }
}
