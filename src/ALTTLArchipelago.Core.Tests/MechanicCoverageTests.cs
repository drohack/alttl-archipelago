using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class MechanicCoverageTests
{
    private static LevelTable Table()
        => LevelTable.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")));

    [Fact]
    public void FourAbilitiesHaveNoGeneratorAtAll()
    {
        var gaps = MechanicCoverage.WithoutGeneratorCoverage(Table());

        Assert.Equal(
            new[] { Abilities.Containers, Abilities.Furniture, Abilities.Jigsaw, Abilities.Stacking }
                .OrderBy(x => x, StringComparer.Ordinal),
            gaps.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheGapAbilitiesAreTheFragileOnes()
    {
        var t = Table();

        // Jigsaw is the worst case: four levels, every one of them archive.
        Assert.Equal(4, MechanicCoverage.CountFor(t, Abilities.Jigsaw));
        Assert.All(
            t.Levels.Where(l => ControllerGroups.AbilitiesForLevel(l).Contains(Abilities.Jigsaw)),
            l => Assert.Equal("archive", l.Source));

        // Furniture is next: four levels, only Workbench outside the archive.
        Assert.Equal(4, MechanicCoverage.CountFor(t, Abilities.Furniture));
    }

    [Fact]
    public void ReservingOneOfEachIsCheapBecauseTheyOverlap()
    {
        var t = Table();
        var gaps = MechanicCoverage.WithoutGeneratorCoverage(t);

        var reserved = MechanicCoverage.Reserve(t, gaps, 1);

        // NeatStreak_Paper Plane Supplies alone covers three of the four.
        Assert.InRange(reserved.Count, 2, 4);
        var covered = reserved.SelectMany(ControllerGroups.AbilitiesForLevel).ToHashSet();
        Assert.All(gaps, g => Assert.Contains(g, covered));
    }

    [Fact]
    public void ReservingThreeOfEachStaysAffordable()
    {
        var t = Table();
        var gaps = MechanicCoverage.WithoutGeneratorCoverage(t);

        var reserved = MechanicCoverage.Reserve(t, gaps, 3);

        // Roughly a tenth of a 79-slot run; it must not eat the whole draw.
        Assert.InRange(reserved.Count, 5, 12);
        Assert.Equal(reserved.Count, reserved.Select(l => l.LevelId).Distinct().Count());

        foreach (var g in gaps)
        {
            var got = reserved.Count(l => ControllerGroups.AbilitiesForLevel(l).Contains(g));
            Assert.True(got >= 3, $"{g} only covered {got} times");
        }
    }

    [Fact]
    public void AnUnmeetableDemandReturnsWhatItCanRatherThanFailing()
    {
        var t = Table();

        // Furniture exists on exactly four levels, so ten is impossible.
        var reserved = MechanicCoverage.Reserve(t, new[] { Abilities.Furniture }, 10);

        Assert.Equal(4, reserved.Count);
        Assert.All(reserved,
            l => Assert.Contains(Abilities.Furniture, ControllerGroups.AbilitiesForLevel(l)));
    }

    [Fact]
    public void AbilitiesGeneratorsDoCoverAreNotReserved()
    {
        var gaps = MechanicCoverage.WithoutGeneratorCoverage(Table());

        Assert.DoesNotContain(Abilities.Swapping, gaps);
        Assert.DoesNotContain(Abilities.Ordering, gaps);
        Assert.DoesNotContain(Abilities.Grids, gaps);
        Assert.DoesNotContain(Abilities.Tidying, gaps);
    }
}
