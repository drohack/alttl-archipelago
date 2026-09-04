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
}
