using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class LocationNamesTests
{
    private static ControllerInfo C(string name, string type, params string[] deps)
        => new() { Name = name, Type = type, DependsOn = deps.ToList() };

    private static LevelInfo Level(string id, int solutions, params ControllerInfo[] cs)
        => new() { LevelId = id, SolutionCount = solutions, Controllers = cs.ToList() };

    [Fact]
    public void SlotNumbersAreOneBasedAndPadded()
    {
        Assert.Equal("Slot 01 - Books (Randomized) - Solution 1",
            LocationNames.Solution(0, "Books (Randomized)", 1));
        Assert.Equal("Slot 79 - Jars - Solution 3",
            LocationNames.Solution(78, "Jars", 3));
    }

    [Fact]
    public void ASingleGroupLevelGetsOnlySolutionLocations()
    {
        // One controller, one solution: the controller check and the solution
        // check would be the same event, so only the solution is minted.
        var level = Level("SpiderWeb", 1, C("Draggables", "Draggables"));

        var names = LocationNames.ForSlot(0, level);

        Assert.Equal(new[] { "Slot 01 - SpiderWeb - Solution 1" }, names);
    }

    [Fact]
    public void ASingleGroupLevelWithSeveralSolutionsGetsOnePerSolution()
    {
        var level = Level("Books (Randomized)", 2, C("Shuffleables", "Shuffleables"));

        var names = LocationNames.ForSlot(4, level);

        Assert.Equal(new[]
        {
            "Slot 05 - Books (Randomized) - Solution 1",
            "Slot 05 - Books (Randomized) - Solution 2",
        }, names);
    }

    [Fact]
    public void AMultiGroupLevelGetsSolutionsAndControllers()
    {
        var level = Level("Breadtags", 1,
            C("Interlocking", "Draggables"),
            C("Crumbs", "Removables"));

        var names = LocationNames.ForSlot(0, level);

        Assert.Equal(new[]
        {
            "Slot 01 - Breadtags - Solution 1",
            "Slot 01 - Breadtags - Crumbs",
            "Slot 01 - Breadtags - Interlocking",
        }, names);
    }

    [Fact]
    public void AMutualPairDoesNotProduceTwoControllerLocations()
    {
        // Spice Jars is a generator whose two Shuffleables are mutually
        // dependent. Collapsing them leaves ONE group, which means no
        // controller locations at all - only its two solutions.
        var level = Level("Spice Jars", 2,
            C("SpiceJarShuffleables", "Shuffleables", "SpiceJarShuffleables_BottomShelf"),
            C("SpiceJarShuffleables_BottomShelf", "Shuffleables", "SpiceJarShuffleables"));

        var names = LocationNames.ForSlot(2, level);

        Assert.Equal(new[]
        {
            "Slot 03 - Spice Jars - Solution 1",
            "Slot 03 - Spice Jars - Solution 2",
        }, names);
    }

    [Fact]
    public void TheSameGeneratorInDifferentSlotsGetsDistinctNames()
    {
        var level = Level("Books (Randomized)", 2, C("Shuffleables", "Shuffleables"));

        var a = LocationNames.ForSlot(3, level);
        var b = LocationNames.ForSlot(40, level);

        Assert.Empty(a.Intersect(b));
    }

    [Fact]
    public void BeatenEventIsNamedPerSlot()
    {
        Assert.Equal("Slot 07 - Jars - Beaten", LocationNames.Beaten(6, "Jars"));
    }

    [Fact]
    public void EveryNameIsUniqueWithinASlot()
    {
        var level = Level("MedicineCabinet", 1,
            C("Swabs Containables", "Containables"),
            C("Green Bottles Draggables", "DraggablesOrdered"),
            C("Cup Draggables", "Draggables"));

        var names = LocationNames.ForSlot(0, level);

        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
