namespace ALTTLArchipelago.Core;

/// <summary>Which way a finished run puzzle leaves (AfterPuzzleRoute).</summary>
public enum AfterPuzzle
{
    /// <summary>The retry panel: the slot still has solutions to find.</summary>
    Panel,

    /// <summary>The panel's route with the panel never shown: on at once.</summary>
    MoveOn,
}

/// <summary>
/// Which way a finished run puzzle leaves. Pure, so it is tested here; the
/// plugin answers LevelInterface.ShowRetryMenu with it (RetryPanel).
///
/// ONE ENDING FOR EVERY RUN LEVEL (droha, 2026-10-01: "a set ending sequence
/// for all levels so we are in control"). The panel while the slot has
/// solutions left to find (droha, 2026-09-25); otherwise the panel's route
/// with the panel never shown, which goes on to the next slot, or to the
/// level select when nothing else is playable (Navigation; droha,
/// 2026-09-28: "it should hopefully go directly to the level select").
///
/// Generators too. They used to keep the game's own straight-on route,
/// which ended on the Daily Tidy page for DailyGuard to turn into the next
/// slot. The game decides that page in
/// LevelManager.OnLevelCompleteTweenOutComplete, which every way out of a
/// finished level passes through, so DailyGuard now answers that decision
/// itself and every route goes on to the next slot.
/// </summary>
public static class AfterPuzzleRoute
{
    /// <param name="solutionsLeft">The slot has more than one Solution and not all are in.</param>
    public static AfterPuzzle For(bool solutionsLeft)
        => solutionsLeft ? AfterPuzzle.Panel : AfterPuzzle.MoveOn;
}
