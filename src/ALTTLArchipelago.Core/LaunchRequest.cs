namespace ALTTLArchipelago.Core;

/// <summary>
/// Which of the game's LevelManager.StartLevel calls Track's prefix must leave
/// exactly as asked. Pure, so it is tested here.
///
/// THE GAME ASKS FOR THE LEVEL ALREADY RUNNING, AND MEANS NOTHING BY IT. With
/// no index (0) and no reload, StartLevel on the level already loaded does
/// nothing in the game. It asks that twice around a finished generator: just
/// after the finish, and when it enters Gameplay_GameState again as the level
/// select closes over the finished level (the quick gate of 2026-10-01: the
/// caller il2cpp itself, "asked index=0 forceReload=False seed=-1"). The
/// prefix read index 0 as a click, took the running level for the target and
/// upgraded the call to that slot's seeded reload, so the finished generator
/// relaunched about a second after "navigation: next" - 3 to 5 times in every
/// gate since 2026-09-27, and on the DLC's own route after Trophy Cabinet and
/// Water Glasses.
///
/// A reload the game means says so. A Retry, a Cat Trap's reset and the
/// harness's boot pass forceReload; the arrow passes another index; a card
/// click, even on a level still loaded under the track, comes with its arm.
/// </summary>
public static class LaunchRequest
{
    /// <param name="askedIndex">startLevelIndex as the caller passed it (0: none).</param>
    /// <param name="askedReload">forceReload as the caller passed it.</param>
    /// <param name="clickArmed">A card click armed this launch in the last frames.</param>
    /// <param name="runningIndex">The active level interface's index, or -1 for none.</param>
    /// <param name="runningLoaded">That level is loaded and its Level exists.</param>
    public static bool AsksForTheRunningLevel(
        int askedIndex, bool askedReload, bool clickArmed, int runningIndex, bool runningLoaded)
        => !askedReload && !clickArmed && runningLoaded && runningIndex > 0
           && (askedIndex <= 0 || askedIndex == runningIndex);

    /// <summary>
    /// The seed a generator gets when it is launched outside the run's slots,
    /// so it is built rather than empty: stable per level, and always above 0.
    ///
    /// A negative seed reads as "no seed" to the game, and the generator comes
    /// up empty - the soft lock the fallback exists to prevent. The old
    /// formula, the index times 2654435761 cast to an int, wrapped negative
    /// for 6 of the 20 generators (Books, Batteries, Post-It Notes, Trim
    /// Plant, Microscope, DLC2 Figurines); Post-It Notes booted outside the
    /// run came up empty 9 of 9 (2026-10-01). Masked to 31 bits, the seeds
    /// that were already positive stay the same.
    /// </summary>
    public static int FallbackSeed(int levelIndex)
    {
        var seed = (int)(unchecked((uint)levelIndex * 2654435761u) & 0x7FFFFFFFu);
        return seed == 0 ? 1 : seed;
    }
}
