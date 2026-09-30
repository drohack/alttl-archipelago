using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class AfterPuzzleTests
{
    private static bool NotAsked() => throw new InvalidOperationException("asked when it cannot matter");

    [Fact]
    public void AGeneratorGoesStraightOnOnlyWhileSomethingElseIsPlayable()
    {
        // Solutions left: the panel, whatever the level.
        Assert.Equal(AfterPuzzle.Panel, AfterPuzzleRoute.For(true, true, NotAsked));

        // A generator keeps the game's straight-on route while another slot
        // is playable; with none it moves on through the panel's route, which
        // Navigation takes to the level select (droha, 2026-09-28).
        Assert.Equal(AfterPuzzle.StraightOn, AfterPuzzleRoute.For(false, true, () => true));
        Assert.Equal(AfterPuzzle.MoveOn, AfterPuzzleRoute.For(false, true, () => false));

        // Every other level moves on through the panel's route.
        Assert.Equal(AfterPuzzle.MoveOn, AfterPuzzleRoute.For(false, false, NotAsked));
    }
}
