namespace ALTTLArchipelago.Core;

/// <summary>What an arrived Cat Trap does this frame (TrapTiming.For).</summary>
public enum TrapMoment
{
    /// <summary>A puzzle is being played: knock it over.</summary>
    Spring,

    /// <summary>A puzzle is loading or changing: ask again next frame.</summary>
    Hold,

    /// <summary>No puzzle to knock over: spent, and said so.</summary>
    NothingToHit,

    /// <summary>The puzzle on screen is finished: spent, too late.</summary>
    AlreadyFinished,

    /// <summary>The credits are playing: spent, they are not a puzzle.</summary>
    Credits,
}

/// <summary>What a reset waiting for a level's own cat does this frame (TrapTiming.AfterCat).</summary>
public enum CatReset
{
    /// <summary>The paw is still in: ask again next frame.</summary>
    Wait,

    /// <summary>The paw has gone (or the cap ran out): reset the puzzle now.</summary>
    Reset,

    /// <summary>The puzzle it was for has ended or changed: nothing to reset.</summary>
    Drop,

    /// <summary>The level's cat never reached in: ours goes, then the reset.</summary>
    NoCat,
}

/// <summary>
/// When a Cat Trap goes off. Pure, so it is tested here; Traps.Tick asks it
/// every frame a trap is owed.
///
/// A FINISHED PUZZLE MISSES BEFORE ANYTHING ELSE IS ASKED, the hold included.
/// The hold used to come first: a trap landing as a generator went straight
/// on found the level transitioning, was held through the whole exit, and
/// came out the far side after the 3 s grace had run - so it reset the
/// finished puzzle and threw the queued next slot away (the second player's
/// 0.4.2 playtest log: Buttons, Trim Plant #2, Stamps (Randomized) #3, Clock).
///
/// THE CREDITS ARE NOT A PUZZLE. The goal is reported as they start, and the
/// server's release then delivers every item left in the world, the run's own
/// Cat Traps included; each one reset the credits (the second player's 0.4.3
/// ending, which then threw on the way out).
/// </summary>
public static class TrapTiming
{
    /// <param name="inALevel">A level interface is active (none on the level select).</param>
    /// <param name="settling">That level is loading or transitioning.</param>
    /// <param name="hasObjects">Its Level and object list exist.</param>
    /// <param name="isCredits">It is a credits level.</param>
    /// <param name="finished">This launch of it has completed and nothing has launched since.</param>
    /// <param name="secondsSinceCompletion">Since the last completion, or null when none.</param>
    /// <param name="grace">How long after a completion a trap still counts as too late.</param>
    /// <summary>
    /// How long a reset waits for a level's own cat (CatGrab) at most. Its
    /// grab is gone in 1.6 to 2.3 s on all four levels (DevTools `catevent`,
    /// 2026-09-30).
    /// </summary>
    public const float CatCap = 4f;

    /// <summary>
    /// How long a started cat may take to show its paw before ours goes
    /// instead. Not the same frame: whether the paw is out is only asked on
    /// the next ones. On a seeded Shells that drew its leaf layout the cat
    /// never came at all (tools/probe-trap-window.py, 2026-09-30).
    /// </summary>
    public const float CatStartWindow = 0.5f;

    /// <summary>
    /// The reset waiting behind a level's own cat: wait while the paw is
    /// still in, reset once it has gone (or at the cap), send ours when the
    /// level's never came, and drop it when the puzzle it was for is no
    /// longer the one being played.
    /// </summary>
    /// <param name="sameLaunch">The launch the cat reached into is still the one on screen.</param>
    /// <param name="settling">That level is loading or transitioning.</param>
    /// <param name="finished">It has completed since the cat reached in.</param>
    /// <param name="catBusy">The paw is reaching or grabbing now.</param>
    /// <param name="catSeen">The paw has been seen out at all since the cat was started.</param>
    /// <param name="waited">Seconds since the cat was started.</param>
    public static CatReset AfterCat(bool sameLaunch, bool settling, bool finished, bool catBusy,
        bool catSeen, float waited)
    {
        // A finished puzzle is not reset, for the reason For misses one.
        if (!sameLaunch || settling || finished) return CatReset.Drop;
        if (!catSeen && !catBusy) return waited < CatStartWindow ? CatReset.Wait : CatReset.NoCat;
        if (catBusy && waited < CatCap) return CatReset.Wait;
        return CatReset.Reset;
    }

    public static TrapMoment For(bool inALevel, bool settling, bool hasObjects, bool isCredits,
        bool finished, float? secondsSinceCompletion, float grace)
    {
        if (!inALevel) return TrapMoment.NothingToHit;
        if (isCredits) return TrapMoment.Credits;

        // Before the hold: see the class summary. The grace also covers the
        // next puzzle's load, where nothing has been done yet.
        if (finished) return TrapMoment.AlreadyFinished;
        if (secondsSinceCompletion is float since && since < grace) return TrapMoment.AlreadyFinished;

        // A level still loading is not one to knock over, but it has a
        // target: held, not spent (Traps.Tick says why).
        if (settling) return TrapMoment.Hold;
        if (!hasObjects) return TrapMoment.NothingToHit;
        return TrapMoment.Spring;
    }
}
