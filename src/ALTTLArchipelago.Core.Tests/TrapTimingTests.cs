using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class TrapTimingTests
{
    private const float Grace = 3f;

    private static TrapMoment At(bool inALevel = true, bool settling = false, bool hasObjects = true,
        bool isCredits = false, bool finished = false, float? since = null)
        => TrapTiming.For(inALevel, settling, hasObjects, isCredits, finished, since, Grace);

    [Fact]
    public void APuzzleBeingPlayedIsKnockedOver()
    {
        Assert.Equal(TrapMoment.Spring, At());

        // Relaunched by the retry panel's restart long after the last finish:
        // a new launch, so a live puzzle again.
        Assert.Equal(TrapMoment.Spring, At(since: 40f));
    }

    [Fact]
    public void NoPuzzleMisses()
    {
        Assert.Equal(TrapMoment.NothingToHit, At(inALevel: false));
        Assert.Equal(TrapMoment.NothingToHit, At(hasObjects: false));
    }

    [Fact]
    public void ALoadNotAfterAFinishIsHeld()
    {
        // tools/probe-trap-window.py test B: a relaunch 150 ms after the trap
        // was sent; it springs on the far side of the load.
        Assert.Equal(TrapMoment.Hold, At(settling: true));
        Assert.Equal(TrapMoment.Hold, At(settling: true, since: 40f));
    }

    [Fact]
    public void AFinishedPuzzleGoingStraightOnMissesInsteadOfBeingHeld()
    {
        // The generator's exit transitions for longer than the grace; held,
        // the trap came out after it and reset the finished puzzle (the
        // second player's 0.4.2 playtest: Buttons, Trim Plant #2, Stamps #3, Clock).
        Assert.Equal(TrapMoment.AlreadyFinished, At(settling: true, finished: true, since: 0.2f));
        Assert.Equal(TrapMoment.AlreadyFinished, At(settling: false, finished: true, since: 5f));
    }

    [Fact]
    public void AFinishedPuzzleOnItsPanelMissesAfterTheGraceToo()
    {
        // Sitting on the retry panel: nothing to undo until a restart
        // launches it again.
        Assert.Equal(TrapMoment.AlreadyFinished, At(finished: true, since: 60f));
    }

    [Fact]
    public void TheNextPuzzleLoadingInsideTheGraceMisses()
    {
        // Nothing has been done on it yet.
        Assert.Equal(TrapMoment.AlreadyFinished, At(settling: true, since: 1f));
    }

    [Fact]
    public void TheResetWaitsForTheLevelsOwnCatToLeave()
    {
        // The paw is in for 1.6 to 2.3 s; the level is rebuilt only after,
        // so the player sees the game's cat take a piece, then the reset.
        Assert.Equal(CatReset.Wait, After(busy: true, seen: true, waited: 1.2f));
        Assert.Equal(CatReset.Reset, After(busy: false, seen: true, waited: 2.3f));

        // A paw that never leaves does not hold the trap forever.
        Assert.Equal(CatReset.Reset, After(busy: true, seen: true, waited: TrapTiming.CatCap));
    }

    [Fact]
    public void ACatThatNeverComesSendsOurs()
    {
        // Not decided in the frame the cat was started: a paw a frame late
        // is waited for, not reset under.
        Assert.Equal(CatReset.Wait, After(busy: false, seen: false, waited: 0f));
        Assert.Equal(CatReset.Wait, After(busy: true, seen: false, waited: 0.1f));

        // Seeded Shells on its leaf layout: the cat never moved.
        Assert.Equal(CatReset.NoCat, After(busy: false, seen: false, waited: TrapTiming.CatStartWindow));
    }

    [Fact]
    public void APuzzleThatEndedUnderTheCatIsNotReset()
    {
        // Finished, left, or relaunched while the paw was in: resetting then
        // would be For's finished-puzzle bug again.
        Assert.Equal(CatReset.Drop, After(finished: true, busy: false, seen: true, waited: 2f));
        Assert.Equal(CatReset.Drop, After(sameLaunch: false, busy: false, seen: true, waited: 2f));
        Assert.Equal(CatReset.Drop, After(settling: true, busy: true, seen: true, waited: 1f));
        Assert.Equal(CatReset.Drop, After(finished: true, busy: false, seen: false, waited: 1f));
    }

    private static CatReset After(bool busy, bool seen, float waited,
        bool sameLaunch = true, bool settling = false, bool finished = false)
        => TrapTiming.AfterCat(sameLaunch, settling, finished, busy, seen, waited);

    [Fact]
    public void TheCreditsAreNeverKnockedOver()
    {
        // The goal's release delivers the run's own traps while they play.
        Assert.Equal(TrapMoment.Credits, At(isCredits: true));
        Assert.Equal(TrapMoment.Credits, At(isCredits: true, settling: true));
        Assert.Equal(TrapMoment.Credits, At(isCredits: true, since: 0.5f));
    }
}
