namespace ALTTLArchipelago.Core;

/// <summary>Which way a finished run puzzle leaves (AfterPuzzleRoute).</summary>
public enum AfterPuzzle
{
    /// <summary>The retry panel: the slot still has solutions to find.</summary>
    Panel,

    /// <summary>The game's own straight-on route, a generator's by the Daily page.</summary>
    StraightOn,

    /// <summary>The panel's route with the panel never shown: on at once.</summary>
    MoveOn,
}

/// <summary>
/// Which way a finished run puzzle leaves. Pure, so it is tested here; the
/// plugin answers LevelInterface.ShowRetryMenu with it (RetryPanel).
///
/// The panel while the slot has solutions left to find (droha, 2026-09-25),
/// otherwise the panel's route with the panel never shown. A generator the
/// game sends straight on keeps that route while another slot is playable:
/// through the panel's NextLevel the game relaunched the finished generator
/// first (measured 2026-09-27). With nothing else playable it moves on too,
/// so the run reaches the level select from the post-level screen rather than
/// by the Daily page and the title (droha, 2026-09-28: "it should hopefully
/// go directly to the level select").
/// </summary>
public static class AfterPuzzleRoute
{
    /// <param name="solutionsLeft">The slot has more than one Solution and not all are in.</param>
    /// <param name="generatorGoingStraightOn">A generator slot whose level does not show the panel.</param>
    /// <param name="otherPlayable">Whether another slot is playable; asked only for such a generator.</param>
    public static AfterPuzzle For(bool solutionsLeft, bool generatorGoingStraightOn, Func<bool> otherPlayable)
    {
        if (solutionsLeft) return AfterPuzzle.Panel;
        if (generatorGoingStraightOn && otherPlayable()) return AfterPuzzle.StraightOn;
        return AfterPuzzle.MoveOn;
    }
}
