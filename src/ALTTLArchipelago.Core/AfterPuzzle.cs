namespace ALTTLArchipelago.Core;

/// <summary>Which way a finished run puzzle leaves (AfterPuzzleRoute).</summary>
public enum AfterPuzzle
{
    /// <summary>The retry panel: the slot still has solutions to find.</summary>
    Panel,

    /// <summary>The panel's route with the panel never shown: on at once.</summary>
    MoveOn,

    /// <summary>
    /// The game's own straight-on route: a Skip on a level the game sends
    /// straight on, which the game moves out of by itself.
    /// </summary>
    TheGames,
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
///
/// EXCEPT A SKIP ON A LEVEL THE GAME SENDS STRAIGHT ON. The game's skip moves
/// such a level out by itself (TransitionLevelOut from inside SkipLevel), so
/// the panel's route on top of it came up over the next puzzle: Cupcakes and
/// Water Glasses in the 1.0.0 DLC gate, 2026-10-02, and in every DLC gate
/// since 2026-09-30, where the harness happened to recover. The game's own
/// route is then the only one, and it goes on to the next slot like the rest.
/// A level built for the panel stops on it after a Skip, as without one.
/// </summary>
public static class AfterPuzzleRoute
{
    /// <param name="solutionsLeft">The slot has more than one Solution and not all are in.</param>
    /// <param name="skipping">The game's SkipLevel is finishing the level.</param>
    /// <param name="gameShowsPanel">The level's own ShowRetryMenu, before the mod's answer.</param>
    public static AfterPuzzle For(bool solutionsLeft, bool skipping = false, bool gameShowsPanel = true)
        => skipping && !gameShowsPanel ? AfterPuzzle.TheGames
            : solutionsLeft ? AfterPuzzle.Panel : AfterPuzzle.MoveOn;
}
