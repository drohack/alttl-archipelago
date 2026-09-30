using System;
using System.Collections.Generic;
using System.Linq;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The Collect Stars goal. A star is a solution found, as the level select
/// counts them (droha, 2026-09-28: "it's number of solutions"), and the
/// goal's count is the cards' hover stars summed: one count, in one place,
/// so a card can never light a star the goal does not count or the reverse.
/// </summary>
public class StarGoalTests
{
    private static SlotData Seed() => ExampleSeed.Load();

    [Fact]
    public void NothingCollectedMeansNoStars()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        Assert.Equal((0, data.StarsTotal), router.RunStars(_ => false));
    }

    [Fact]
    public void EverythingCollectedLightsEveryStar()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        Assert.True(data.StarsTotal > data.Slots.Count, "some puzzles hold more than one star");
        Assert.Equal((data.StarsTotal, data.StarsTotal), router.RunStars(_ => true));
    }

    [Fact]
    public void BeatingAPuzzleAndItsPartsLightNoStar()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        var slot = Enumerable.Range(0, data.Slots.Count)
            .First(i => router.ForSlot(i).Count > router.SolutionsOf(i).Count + 1);
        var done = new HashSet<string>(router.ForSlot(slot).Except(router.SolutionsOf(slot)),
                                       StringComparer.Ordinal);
        Assert.Equal(1, router.BeatenCount(done.Contains));
        Assert.Equal(0, router.RunStars(done.Contains).Lit);
    }

    [Fact]
    public void EachSolutionIsOneStar()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        var slot = Enumerable.Range(0, data.Slots.Count)
            .First(i => router.SolutionsOf(i).Count > 1);
        var done = new HashSet<string>(router.SolutionsOf(slot), StringComparer.Ordinal);
        Assert.Equal(router.SolutionsOf(slot).Count, router.RunStars(done.Contains).Lit);
    }

    [Fact]
    public void ASectionCountsItsSlotsStarsAsTheCardsDo()
    {
        // The pack header's count, which the game took from the save: a
        // section of finished generators read "0/15 (0%)" (2026-09-29).
        var data = Seed();
        var router = new CheckRouter(data);
        var slots = Enumerable.Range(0, Math.Min(5, data.Slots.Count)).ToList();
        var done = new HashSet<string>(slots.SelectMany(router.SolutionsOf), StringComparer.Ordinal);
        var total = slots.Sum(s => router.SolutionsOf(s).Count);

        Assert.Equal((total, total), router.StarsOf(slots, done.Contains));
        Assert.Equal((0, total), router.StarsOf(slots, _ => false));
        Assert.Equal((0, 0), router.StarsOf(Array.Empty<int>(), _ => true));
    }

    [Theory]
    [InlineData(1, 15, "1/15 (7%)")]
    [InlineData(1, 17, "1/17 (6%)")]
    [InlineData(0, 5, "0/5 (0%)")]
    [InlineData(15, 15, "15/15 (100%)")]
    [InlineData(0, 0, "0/0 (0%)")]
    public void TheCountReadsAsTheGameWritesIt(int lit, int total, string text)
        => Assert.Equal(text, CompletionText.Of(lit, total));

    [Fact]
    public void TheGoalCountsTheStarsTheCardsLight()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        // Every other solution in the run, so the sum is neither 0 nor all.
        var every = Enumerable.Range(0, data.Slots.Count).SelectMany(router.SolutionsOf).ToList();
        var done = new HashSet<string>(every.Where((_, i) => i % 2 == 0), StringComparer.Ordinal);
        var byCard = Enumerable.Range(0, data.Slots.Count)
            .Sum(i => router.SolutionStars(i, done.Contains).Lit);
        Assert.Equal(done.Count, byCard);
        Assert.Equal(byCard, router.RunStars(done.Contains).Lit);
    }

    [Fact]
    public void TheRealSeedDefaultsToBeating()
    {
        // An older server sends no "goal" key at all, and that payload has to
        // keep meaning what it meant before the option existed.
        var data = Seed();
        Assert.False(data.GoalIsStars);
        Assert.Equal(data.LevelsToBeat, data.GoalTarget);
    }

    [Fact]
    public void AStarSeedTargetsTheStarCount()
    {
        var data = Seed();
        data.Goal = "collect_stars";
        data.StarsToCollect = 12;

        Assert.True(data.GoalIsStars);
        Assert.Equal(12, data.GoalTarget);
    }

    [Fact]
    public void AGoalThisBuildDoesNotKnowIsReported()
    {
        var data = Seed();
        data.Goal = "collect_every_sock";

        Assert.Contains(data.Problems(),
                        p => p.Contains("collect_every_sock", StringComparison.Ordinal));
    }

    [Fact]
    public void AStarCountBeyondTheRunIsReported()
    {
        var data = Seed();
        data.StarsToCollect = data.StarsTotal + 1;

        Assert.Contains(data.Problems(),
                        p => p.Contains("stars_to_collect", StringComparison.Ordinal));
    }
}
