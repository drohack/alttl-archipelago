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
}
