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
        // These come from a config file a player can edit.
        var zero = new RetryPolicy(maxAttempts: 0, firstDelaySeconds: 0,
                                   maxDelaySeconds: -5);
        Assert.True(zero.MaxAttempts >= 1);
        Assert.True(zero.DelayForAttempt(1) > 0);
        Assert.Null(zero.Failed("only attempt"));
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
