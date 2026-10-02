using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class AfterPuzzleTests
{
    [Fact]
    public void SolutionsLeftShowThePanel()
    {
        Assert.Equal(AfterPuzzle.Panel, AfterPuzzleRoute.For(solutionsLeft: true));
    }

    [Fact]
    public void EveryFinishedSlotMovesOnTheSameWayGeneratorsToo()
    {
        // A generator used to keep the game's straight-on route, which ended
        // on the Daily Tidy page for the daily guard to rescue: every
        // generator finish in the second player's 0.4.2 playtest (105 times). One ending
        // for every level now (droha, 2026-10-01: "a set ending sequence for
        // all levels so we are in control").
        Assert.Equal(AfterPuzzle.MoveOn, AfterPuzzleRoute.For(solutionsLeft: false));
    }

    [Fact]
    public void ASkipOnAStraightOnLevelKeepsTheGamesRoute()
    {
        // The 1.0.0 DLC gate (2026-10-02): a Skip on Cupcakes, which the game
        // sends straight on, moved the level out by itself while the panel the
        // mod asked for came up, and the next puzzle opened under the panel.
        Assert.Equal(AfterPuzzle.TheGames,
            AfterPuzzleRoute.For(solutionsLeft: true, skipping: true, gameShowsPanel: false));
        Assert.Equal(AfterPuzzle.TheGames,
            AfterPuzzleRoute.For(solutionsLeft: false, skipping: true, gameShowsPanel: false));
        // A level built for the panel stops on it after a Skip, and the panel
        // route moves it on (Fruit Stickers in the quick gate).
        Assert.Equal(AfterPuzzle.MoveOn,
            AfterPuzzleRoute.For(solutionsLeft: false, skipping: true, gameShowsPanel: true));
        // Not a Skip: one ending for every level, whatever the game's own.
        Assert.Equal(AfterPuzzle.Panel,
            AfterPuzzleRoute.For(solutionsLeft: true, skipping: false, gameShowsPanel: false));
    }
}
