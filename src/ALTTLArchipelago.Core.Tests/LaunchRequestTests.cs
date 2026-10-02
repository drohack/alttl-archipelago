using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class LaunchRequestTests
{
    // Every generator's level index (levels.json, source generator).
    private static readonly int[] Generators =
        { 995, 996, 997, 998, 999, 1000, 12, 26, 28, 46, 58, 62, 69, 70, 73, 75, 1125, 1209, 1223, 1235 };

    [Fact]
    public void AGeneratorOutsideTheRunIsGivenASeedTheGameBuildsFrom()
    {
        // A negative seed reads as "no seed" and the generator comes up empty:
        // Post-It Notes (998) booted outside the run, 9 of 9 empty
        // (2026-10-01). The old fallback gave 6 of the 20 a negative seed.
        foreach (var index in Generators)
            Assert.True(LaunchRequest.FallbackSeed(index) > 0, $"level {index}");
    }

    [Fact]
    public void TheFallbackSeedIsStableAndKeepsThePositiveOnes()
    {
        Assert.Equal(LaunchRequest.FallbackSeed(998), LaunchRequest.FallbackSeed(998));
        Assert.Equal(772599381, LaunchRequest.FallbackSeed(997));   // was positive already
        Assert.Equal(295853050, LaunchRequest.FallbackSeed(26));
    }

    [Fact]
    public void TheGamesOwnCallForTheFinishedGeneratorIsLeftAlone()
    {
        // The quick gate of 2026-10-01, Stamps (Randomized) at frames 3815
        // and 5005: "asked index=0 forceReload=False seed=-1", active=997.
        Assert.True(LaunchRequest.AsksForTheRunningLevel(0, false, false, 997, true));
        Assert.True(LaunchRequest.AsksForTheRunningLevel(997, false, false, 997, true));
    }

    [Fact]
    public void AReloadTheGameMeansIsStillSeeded()
    {
        // Retry, a Cat Trap's reset, DevTools boot: forceReload (slot 4 Fruit
        // Stickers in the same gate, "asked index=29 forceReload=True").
        Assert.False(LaunchRequest.AsksForTheRunningLevel(29, true, false, 29, true));
        Assert.False(LaunchRequest.AsksForTheRunningLevel(0, true, false, 997, true));
    }

    [Fact]
    public void AClickTheArrowAndALaunchFromTheTrackAreNotTheRunningLevel()
    {
        // A card click on the level still loaded under the track.
        Assert.False(LaunchRequest.AsksForTheRunningLevel(0, false, true, 997, true));
        // The arrow passes the next level's index.
        Assert.False(LaunchRequest.AsksForTheRunningLevel(996, false, false, 997, true));
        // The level select tore the old level down: nothing is running.
        Assert.False(LaunchRequest.AsksForTheRunningLevel(0, false, false, -1, false));
        Assert.False(LaunchRequest.AsksForTheRunningLevel(0, false, false, 997, false));
    }
}
