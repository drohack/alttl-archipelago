using System;
using System.Collections.Generic;
using System.Linq;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Counting STARRED puzzles, and the goal that can be set to want them.
///
/// A star is every check on a puzzle collected, which the level select
/// already draws on the card. The point of these tests is that the goal and
/// the card agree: one predicate, in one place, so a card can never show a
/// star the goal does not count or the reverse.
/// </summary>
public class StarGoalTests
{
    private static SlotData Seed() => ExampleSeed.Load();

    [Fact]
    public void NothingCollectedMeansNothingStarred()
    {
        var router = new CheckRouter(Seed());
        Assert.Equal(0, router.StarredCount(_ => false));
    }

    [Fact]
    public void EverythingCollectedStarsEverySlot()
    {
        var data = Seed();
        var router = new CheckRouter(data);
        Assert.Equal(data.Slots.Count, router.StarredCount(_ => true));
    }

    [Fact]
    public void BeatingAPuzzleDoesNotStarIt()
    {
        // The distinction the whole option rests on. A slot with more than
        // its Beaten event is not starred by the Beaten event alone.
        var data = Seed();
        var router = new CheckRouter(data);

        var slot = Enumerable.Range(0, data.Slots.Count)
            .First(i => router.ForSlot(i).Count > 1);
        var beaten = router.ForBeaten(slot);
        Assert.NotNull(beaten);

        var done = new HashSet<string>(StringComparer.Ordinal) { beaten! };
        Assert.Equal(1, router.BeatenCount(done.Contains));
        Assert.Equal(0, router.StarredCount(done.Contains));
    }

    [Fact]
    public void CollectingEveryLocationOnOneSlotStarsExactlyThatSlot()
    {
        var data = Seed();
        var router = new CheckRouter(data);

        var slot = Enumerable.Range(0, data.Slots.Count)
            .First(i => router.ForSlot(i).Count > 1);
        var done = new HashSet<string>(router.ForSlot(slot), StringComparer.Ordinal);

        Assert.Equal(1, router.StarredCount(done.Contains));
    }

    [Fact]
    public void StarringAlwaysImpliesBeating()
    {
        // ForSlot includes the Beaten event, so the two counts are nested
        // rather than independent. If that ever stops being true, a star
        // goal could be met by a player the server does not think has beaten
        // anything.
        var data = Seed();
        var router = new CheckRouter(data);

        for (int i = 0; i < data.Slots.Count; i++)
        {
            var beaten = router.ForBeaten(i);
            if (beaten == null) continue;
            Assert.Contains(beaten, router.ForSlot(i));
        }
    }

    [Fact]
    public void HasWorkLeftIsTheSamePredicateTheCountUses()
    {
        var data = Seed();
        var router = new CheckRouter(data);

        var byHand = 0;
        for (int i = 0; i < data.Slots.Count; i++)
        {
            if (!router.HasWorkLeft(i, _ => true)) byHand++;
        }
        Assert.Equal(router.StarredCount(_ => true), byHand);
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
        data.Goal = "star_levels";
        data.LevelsToStar = 12;

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
        data.LevelsToStar = data.Slots.Count + 1;

        Assert.Contains(data.Problems(),
                        p => p.Contains("levels_to_star", StringComparison.Ordinal));
    }
}
