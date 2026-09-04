using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The ledger decides whether a check the player earned actually reaches the
/// server. Every test here is a way progress could be silently lost.
/// </summary>
public class CheckLedgerTests
{
    [Fact]
    public void TheSameCheckIsOnlyEverReportedOnce()
    {
        // ObjectControllerSolved fires repeatedly for one controller - sixteen
        // events across thirteen controllers, measured. Without this the same
        // location is sent over and over.
        var ledger = new CheckLedger();

        Assert.True(ledger.Check("Spoons - Stacked"));
        Assert.False(ledger.Check("Spoons - Stacked"));
        Assert.False(ledger.Check("Spoons - Stacked"));
        Assert.Single(ledger.Owed);
    }

    [Fact]
    public void AnEarnedCheckIsBothCollectedAndOwed()
    {
        var ledger = new CheckLedger();
        ledger.Check("A");

        Assert.True(ledger.IsCollected("A"));
        Assert.Contains("A", ledger.Owed);
    }

    [Fact]
    public void ServerChecksCountAsCollectedButAreNotOwed()
    {
        // The server already has them. Re-sending its own list back on every
        // login is pointless traffic.
        var ledger = new CheckLedger();
        ledger.AdoptServerChecks(new[] { "A", "B" });

        Assert.True(ledger.IsCollected("A"));
        Assert.Empty(ledger.Owed);
    }

    [Fact]
    public void AnOfflineCheckSurvivesTheServerSayingItDoesNotHaveIt()
    {
        // THE bug this class exists to prevent. Adopting the server's view
        // must not wipe what we earned offline, or the checks are lost the
        // moment we reconnect.
        var ledger = new CheckLedger();
        ledger.Check("earned offline");
        ledger.AdoptServerChecks(new[] { "something else" });

        Assert.Contains("earned offline", ledger.Owed);
        Assert.True(ledger.IsCollected("something else"));
    }

    [Fact]
    public void AnOwedCheckStaysOwedEvenWhenTheServerAlsoListsIt()
    {
        // It can legitimately be in both: sent, then the acknowledgement lost
        // to the same disconnect. Re-sending is harmless; dropping it is not.
        var ledger = new CheckLedger();
        ledger.Check("A");
        ledger.AdoptServerChecks(new[] { "A" });

        Assert.Contains("A", ledger.Owed);
    }

    [Fact]
    public void AcknowledgingClearsOnlyWhatWasSent()
    {
        // A check earned while the flush is in flight must not be cleared by
        // an acknowledgement that never covered it.
        var ledger = new CheckLedger();
        ledger.Check("A");
        var sending = new List<string>(ledger.Owed);

        ledger.Check("B");              // earned mid-flush
        ledger.Acknowledge(sending);

        Assert.DoesNotContain("A", ledger.Owed);
        Assert.Contains("B", ledger.Owed);
    }

    [Fact]
    public void AQueueRestoredFromDiskIsStillOwed()
    {
        var ledger = new CheckLedger();
        ledger.RestoreOwed(new[] { "A", "B" });

        Assert.True(ledger.IsCollected("A"));
        Assert.Equal(2, ledger.Owed.Count);
    }

    [Fact]
    public void TheSavedQueueIsStableSoTheFileDoesNotChurn()
    {
        var one = new CheckLedger();
        one.Check("B"); one.Check("A"); one.Check("C");

        var two = new CheckLedger();
        two.Check("C"); two.Check("A"); two.Check("B");

        Assert.Equal(one.OwedForSaving(), two.OwedForSaving());
    }

    [Fact]
    public void EmptyNamesAreIgnoredRatherThanRecorded()
    {
        // A malformed event should not put a nameless location in the queue,
        // where it would fail to send forever.
        var ledger = new CheckLedger();
        Assert.False(ledger.Check(""));
        Assert.False(ledger.Check(null!));
        Assert.Empty(ledger.Owed);
    }
}
