using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// One Skip, from the press to the charge. droha, 2026-09-25: a Skip must
/// work on every level - the game's own skip, or for a level it will not
/// skip, release the locations, spend the Skip and go back to the level
/// select - and it is spent exactly once, only when it did something.
/// </summary>
public class SkipFlowTests
{
    [Fact]
    public void ASkipTheGameCompletesInsideSkipLevelIsChargedOnce()
    {
        var flow = new SkipFlow();
        Assert.Equal(SkipRefusal.None, flow.Request(3, available: 1, workLeft: true));

        Assert.True(flow.Skipped(3));                        // LevelSkipped: charge
        Assert.Equal(SkipReturn.Done, flow.Returned(3, skippable: true));
        Assert.False(flow.Armed);
    }

    [Fact]
    public void ASkipTheGameCompletesInsideIsDoneWhateverItsFlagSays()
    {
        // Radial Dance Party, 2026-09-25: Skippable False, yet the game
        // completed the skip inside SkipLevel and raised LevelSkipped. No
        // fallback on top of that - it would charge a second time.
        var flow = new SkipFlow();
        flow.Request(3, 1, true);

        Assert.True(flow.Skipped(3));
        Assert.Equal(SkipReturn.Done, flow.Returned(3, skippable: false));
    }

    [Fact]
    public void ASkipTheGameCompletesLateIsChargedWhenItLands()
    {
        var flow = new SkipFlow();
        flow.Request(3, 1, true);

        Assert.Equal(SkipReturn.StayArmed, flow.Returned(3, skippable: true));
        Assert.True(flow.Armed);
        Assert.True(flow.Skipped(3));
        Assert.False(flow.Armed);
    }

    [Fact]
    public void ASkipThatNeverLandsIsDroppedUnchargedWhenALevelStarts()
    {
        var flow = new SkipFlow();
        flow.Request(3, 1, true);
        flow.Returned(3, skippable: true);

        flow.Entered(4);

        Assert.False(flow.Armed);
        Assert.False(flow.Skipped(3));
    }

    [Fact]
    public void ALevelTheGameWillNotSkipFallsBackAndIsChargedOnce()
    {
        var flow = new SkipFlow();
        flow.Request(3, 1, true);

        Assert.Equal(SkipReturn.Fallback, flow.Returned(3, skippable: false));
        Assert.False(flow.Armed);
        Assert.False(flow.Skipped(3));                       // no second charge
    }

    [Fact]
    public void ARealCompletionAfterAnUnlandedSkipChargesNothing()
    {
        // The player solves the level while a late skip is pending: the
        // normal payout runs, no LevelSkipped comes, nothing is spent.
        var flow = new SkipFlow();
        flow.Request(3, 1, true);
        flow.Returned(3, skippable: true);

        flow.Entered(3);                                     // back in, solved for real

        Assert.False(flow.Armed);
        Assert.False(flow.Skipped(3));
    }

    [Fact]
    public void ADoublePressIsChargedOnce()
    {
        var flow = new SkipFlow();
        flow.Request(3, 2, true);
        flow.Request(3, 2, true);

        Assert.True(flow.Skipped(3));
        Assert.False(flow.Skipped(3));
    }

    [Fact]
    public void ALevelSkippedForAnotherSlotChargesNothing()
    {
        var flow = new SkipFlow();
        flow.Request(3, 1, true);

        Assert.False(flow.Skipped(5));
        Assert.Equal(SkipReturn.Idle, flow.Returned(5, skippable: false));
        Assert.True(flow.Armed);
    }

    [Theory]
    [InlineData(-1, 1, true, SkipRefusal.NotARunSlot)]
    [InlineData(3, 0, true, SkipRefusal.NoneHeld)]
    [InlineData(3, 1, false, SkipRefusal.NothingLeft)]
    public void RefusalsArmNothing(int slot, int available, bool workLeft, SkipRefusal expected)
    {
        var flow = new SkipFlow();
        Assert.Equal(expected, flow.Request(slot, available, workLeft));
        Assert.False(flow.Armed);
        Assert.False(flow.Skipped(slot));
    }

    [Fact]
    public void NothingArmedMeansEveryEventIsIgnored()
    {
        var flow = new SkipFlow();
        Assert.False(flow.Skipped(3));
        Assert.Equal(SkipReturn.Idle, flow.Returned(3, skippable: false));
    }
}
