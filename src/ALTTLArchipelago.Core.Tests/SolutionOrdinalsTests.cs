using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Mapping the game's internal solution ids onto the generator's numbered
/// solution locations. Getting this wrong either hands out checks nobody
/// earned or withholds ones they did.
/// </summary>
public class SolutionOrdinalsTests
{
    [Fact]
    public void DistinctSolutionsAreNumberedInTheOrderTheyAreFound()
    {
        var ordinals = new SolutionOrdinals();

        Assert.Equal(1, ordinals.Record(3, "Stacked_0"));
        Assert.Equal(2, ordinals.Record(3, "ByColour_0"));
        Assert.Equal(3, ordinals.Record(3, "BySize_0"));
    }

    [Fact]
    public void SolvingTheSameArrangementAgainIsNotANewCheck()
    {
        // A player can leave a level and come back and solve it the same way.
        // Counting completions instead of distinct ids would hand out the
        // second and third solution locations for solving the first one three
        // times.
        var ordinals = new SolutionOrdinals();

        Assert.Equal(1, ordinals.Record(3, "Stacked_0"));
        Assert.Equal(0, ordinals.Record(3, "Stacked_0"));
        Assert.Equal(0, ordinals.Record(3, "Stacked_0"));
        Assert.Equal(1, ordinals.CountFor(3));
    }

    [Fact]
    public void SlotsAreCountedSeparately()
    {
        // Two slots can be the same LEVEL - a repeated puzzle - and would then
        // report identical solution ids. They are different cards with
        // different locations.
        var ordinals = new SolutionOrdinals();

        Assert.Equal(1, ordinals.Record(1, "Stacked_0"));
        Assert.Equal(1, ordinals.Record(2, "Stacked_0"));
        Assert.Equal(1, ordinals.CountFor(1));
        Assert.Equal(1, ordinals.CountFor(2));
    }

    [Fact]
    public void ALevelThatReportsNoIdStillHasOneSolution()
    {
        // Some completions arrive with an empty SolutionId. Refusing those
        // would mean the level could never be checked at all.
        var ordinals = new SolutionOrdinals();

        Assert.Equal(1, ordinals.Record(0, ""));
        Assert.Equal(0, ordinals.Record(0, ""));
    }

    [Fact]
    public void AnUnknownSlotIsRefusedRatherThanRecordedAgainstNothing()
    {
        var ordinals = new SolutionOrdinals();

        Assert.Equal(0, ordinals.Record(-1, "Stacked_0"));
        Assert.Equal(0, ordinals.CountFor(-1));
    }

    /// <summary>
    /// THE BUG THIS CLASS SHIPPED WITH, PINNED.
    ///
    /// The counter lived only in memory, so relaunching the game restarted it
    /// at 1 and the next arrangement re-filed "Solution 1" - a location already
    /// collected. droha found all three arrangements of Snow Globes, the game
    /// recorded 3 of 3, and the server had exactly one check. The other two
    /// were filed against a collected location and went nowhere, leaving a card
    /// that looked unfinished with nothing obvious left to do.
    /// </summary>
    [Fact]
    public void SeedingRestoresOrdinalsSoARelaunchDoesNotRefileSolutionOne()
    {
        var before = new SolutionOrdinals();
        Assert.Equal(1, before.Record(0, "Pan_0"));
        Assert.Equal(2, before.Record(0, "Pan_1"));

        // The game closes. A fresh session, and the save says two arrangements
        // were already found.
        var after = new SolutionOrdinals();
        Assert.Equal(2, after.Seed(0, new[] { "Pan_0", "Pan_1" }));

        // The third arrangement must file Solution 3, not Solution 1.
        Assert.Equal(3, after.Record(0, "Pan_2"));

        // And a repeat of an old one is still not a new check.
        Assert.Equal(0, after.Record(0, "Pan_0"));
    }

    [Fact]
    public void SeedingTwiceIsHarmless()
    {
        // EnterSlot runs every time the card is opened, so this happens.
        var ordinals = new SolutionOrdinals();

        Assert.Equal(2, ordinals.Seed(0, new[] { "a", "b" }));
        Assert.Equal(0, ordinals.Seed(0, new[] { "a", "b" }));
        Assert.Equal(1, ordinals.Seed(0, new[] { "a", "b", "c" }));
        Assert.Equal(3, ordinals.CountFor(0));
    }

    [Fact]
    public void SeedingDoesNotLeakBetweenSlots()
    {
        var ordinals = new SolutionOrdinals();

        ordinals.Seed(0, new[] { "a", "b" });

        // A different slot of the same generator level shares solution ids, and
        // must still start from the beginning.
        Assert.Equal(1, ordinals.Record(1, "a"));
        Assert.Equal(2, ordinals.CountFor(0));
    }

    [Fact]
    public void SeedingIgnoresNonsenseRatherThanThrowing()
    {
        var ordinals = new SolutionOrdinals();

        Assert.Equal(0, ordinals.Seed(-1, new[] { "a" }));
        Assert.Equal(0, ordinals.Seed(0, null!));
        Assert.Equal(0, ordinals.CountFor(0));
    }
}
