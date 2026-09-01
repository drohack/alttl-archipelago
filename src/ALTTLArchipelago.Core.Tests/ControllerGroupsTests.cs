using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class ControllerGroupsTests
{
    private static ControllerInfo C(string name, string type, params string[] deps)
        => new() { Name = name, Type = type, DependsOn = deps.ToList() };

    private static LevelInfo Level(string id, int solutions, params ControllerInfo[] cs)
        => new() { LevelId = id, SolutionCount = solutions, Controllers = cs.ToList() };

    [Fact]
    public void IndependentControllersStayApart()
    {
        var level = Level("MedicineCabinet", 1,
            C("Swabs Containables", "Containables"),
            C("Green Bottles Draggables", "DraggablesOrdered"),
            C("Cup Draggables", "Draggables"));

        var groups = ControllerGroups.For(level);

        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Members));
    }

    [Fact]
    public void MutuallyDependentControllersBecomeOneGroup()
    {
        // Chess Shadows: the pieces and their shadows must agree, so they are
        // one puzzle and must not mint two separately collectable checks.
        var level = Level("Chess Shadows", 1,
            C("Shuffleables Pieces", "ShuffleablesRelative", "Shuffleables Shadows"),
            C("Shuffleables Shadows", "ShuffleablesRelative", "Shuffleables Pieces"));

        var groups = ControllerGroups.For(level);

        var group = Assert.Single(groups);
        Assert.Equal(new[] { "Shuffleables Pieces", "Shuffleables Shadows" }, group.Members);
        Assert.Equal("Shuffleables Pieces", group.Name);
    }

    [Fact]
    public void AOneWayDependencyDoesNotMerge()
    {
        // Desktop Computer: the errors wait on the desktop, but they are
        // solved one after the other, so they stay two checks.
        var level = Level("Desktop Computer", 1,
            C("Computer Desktop", "Draggables"),
            C("Computer Errors", "ComputerErrorsController", "Computer Desktop"));

        var groups = ControllerGroups.For(level);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void AOneWayDependencyInheritsTheAbilitiesItWaitsOn()
    {
        var level = Level("Waiting", 1,
            C("First", "Shuffleables"),
            C("Second", "GridPuzzle", "First"));

        var groups = ControllerGroups.For(level);
        var second = groups.Single(g => g.Name == "Second");

        Assert.Contains(Abilities.Grids, second.Abilities);
        Assert.Contains(Abilities.Swapping, second.Abilities);

        // The dependency does not inherit in the other direction.
        var first = groups.Single(g => g.Name == "First");
        Assert.DoesNotContain(Abilities.Grids, first.Abilities);
    }

    [Fact]
    public void TransitiveDependenciesAreFollowed()
    {
        var level = Level("Chain", 1,
            C("A", "Shuffleables"),
            C("B", "GridPuzzle", "A"),
            C("C", "Rotateables", "B"));

        var c = ControllerGroups.For(level).Single(g => g.Name == "C");

        Assert.Equal(
            new[] { Abilities.Grids, Abilities.Rotating, Abilities.Swapping }.OrderBy(x => x),
            c.Abilities.OrderBy(x => x));
    }

    [Fact]
    public void CamerePanControllersAreNotPuzzles()
    {
        var level = Level("Fruit Stickers", 2,
            C("Objects", "Pannables"),
            C("Remove Stickers", "Pluckables"),
            C("Match Stickers", "Stickables"));

        var groups = ControllerGroups.For(level);

        Assert.Equal(2, groups.Count);
        Assert.DoesNotContain(groups, g => g.Name == "Objects");
    }

    [Fact]
    public void ControllersSharingAGameObjectNameCollapse()
    {
        // Radial Dance Party lists "Radial Cat Toys 1" twice, as a RadialDance
        // and as a Draggables on the same visual group.
        var level = Level("Radial Dance Party", 1,
            C("Radial Cat Toys 1", "RadialDance"),
            C("Radial Cat Toys 1", "Draggables"),
            C("Radial Pencils 0", "RadialDance"));

        var groups = ControllerGroups.For(level);

        Assert.Equal(2, groups.Count);
        Assert.Contains(Abilities.Rotating, groups.Single(g => g.Name == "Radial Cat Toys 1").Abilities);
    }

    [Fact]
    public void BaselineDraggablesNeedsNoAbility()
    {
        var level = Level("Plain", 1, C("Only", "Draggables"));

        var group = Assert.Single(ControllerGroups.For(level));

        Assert.Empty(group.Abilities);
    }

    [Fact]
    public void AnUnknownControllerClassStaysPlayable()
    {
        // A game update adding a class we have never seen must not lock the
        // level behind an item that does not exist.
        var level = Level("Future", 1, C("Mystery", "SomethingNewController"));

        Assert.Empty(Assert.Single(ControllerGroups.For(level)).Abilities);
        Assert.Null(Abilities.ForClass("SomethingNewController"));
    }

    [Fact]
    public void LevelAbilitiesAreTheUnionOverGroups()
    {
        var level = Level("Mixed", 1,
            C("A", "Shuffleables"),
            C("B", "GridPuzzle"),
            C("C", "Draggables"));

        var all = ControllerGroups.AbilitiesForLevel(level);

        Assert.Equal(
            new[] { Abilities.Grids, Abilities.Swapping }.OrderBy(x => x),
            all.OrderBy(x => x));
    }
}
