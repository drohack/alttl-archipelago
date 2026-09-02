using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class LocationNamesTests
{
    private static ControllerInfo C(string name, string type, params string[] deps)
        => new() { Name = name, Type = type, DependsOn = deps.ToList() };

    private static LevelInfo Level(string id, int solutions, params ControllerInfo[] cs)
        => new() { LevelId = id, SolutionCount = solutions, Controllers = cs.ToList() };

    private static LevelTable Table()
        => LevelTable.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")));

    [Fact]
    public void ANameSaysWhatTheCheckActuallyIs()
    {
        // The point of content naming: a hint is meaningful on its own.
        Assert.Equal("Cookies Jigsaw (Good Tidings) - Match Reindeer",
            LocationNames.Part("GoodTidings_Cookies (Jigsaw)", 1, "Match Reindeer"));
        Assert.Equal("Medicine Cabinet - Solution 1",
            LocationNames.Solution("MedicineCabinet", 1, 1));
    }

    [Fact]
    public void RepeatedGeneratorsAreNumberedAfterTheFirst()
    {
        Assert.Equal("Books (Randomized) - Solution 2",
            LocationNames.Solution("Books (Randomized)", 1, 2));
        Assert.Equal("Books (Randomized) #3 - Solution 2",
            LocationNames.Solution("Books (Randomized)", 3, 2));
    }

    [Fact]
    public void NoNameCarriesASlotOrChapter()
    {
        // Anything positional would differ between seeds and break the
        // datapackage, since slot 7 holds a different puzzle every time.
        foreach (var n in LocationNames.AllPossible(Table()))
        {
            Assert.DoesNotContain("Slot ", n, StringComparison.Ordinal);
            Assert.DoesNotContain("Chapter ", n, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheStaticTableIsUniqueAndCoversTheCredits()
    {
        var all = LocationNames.AllPossible(Table());

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Contains(LocationNames.Credits, all);
    }

    [Fact]
    public void EveryLocationAnySeedCanProduceIsInTheStaticTable()
    {
        var table = Table();
        var all = LocationNames.AllPossible(table).ToHashSet();

        foreach (var level in table.Levels)
        {
            int instances = level.Source == "generator"
                ? LocationNames.MaxGeneratorInstances : 1;
            for (int i = 1; i <= instances; i++)
            {
                foreach (var n in LocationNames.ForInstance(level, i)) Assert.Contains(n, all);
                Assert.Contains(LocationNames.Beaten(level.LevelId, i), all);
            }
        }
    }

    [Fact]
    public void ASingleGroupLevelGetsOnlySolutionLocations()
    {
        var level = Level("SpiderWeb", 1, C("Draggables", "Draggables"));

        Assert.Equal(new[] { "Spider Web - Solution 1" },
            LocationNames.ForInstance(level, 1));
    }

    [Fact]
    public void AMultiGroupLevelGetsSolutionsAndNamedParts()
    {
        var level = Level("Breadtags", 1,
            C("Interlocking", "Draggables"),
            C("Crumbs", "Removables"));

        Assert.Equal(new[]
        {
            "Breadtags - Solution 1",
            "Breadtags - Crumbs",
            "Breadtags - Interlocking",
        }, LocationNames.ForInstance(level, 1));
    }

    [Fact]
    public void AMutualPairDoesNotProduceTwoParts()
    {
        // Spice Jars is a generator whose two Shuffleables are mutually
        // dependent, so it collapses to one group and gets no Part locations.
        var level = Level("Spice Jars", 2,
            C("SpiceJarShuffleables", "Shuffleables", "SpiceJarShuffleables_BottomShelf"),
            C("SpiceJarShuffleables_BottomShelf", "Shuffleables", "SpiceJarShuffleables"));

        Assert.Equal(new[]
        {
            "Spice Jars - Solution 1",
            "Spice Jars - Solution 2",
        }, LocationNames.ForInstance(level, 1));
    }

    [Fact]
    public void TheTableIsBigEnoughToBeInterestingAndSmallEnoughToShip()
    {
        var count = LocationNames.AllPossible(Table()).Count;

        // Sanity bounds rather than an exact pin: the exact number moves with
        // MaxGeneratorInstances, which is a tuning decision.
        Assert.InRange(count, 300, 1200);
    }

    [Fact]
    public void ChapterBoundariesStillFollowVanillaForTheTrackLayout()
    {
        // Chapters no longer appear in location names, but they still lay the
        // level-select track out and title its sections.
        Assert.Equal(79, Chapters.TotalSlots);
        Assert.Equal(1, Chapters.ChapterOf(19));
        Assert.Equal(2, Chapters.ChapterOf(20));
        Assert.Equal(5, Chapters.ChapterOf(78));
        Assert.Equal(12, Chapters.PositionInChapter(78));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chapters.ChapterOf(79));
    }
}
