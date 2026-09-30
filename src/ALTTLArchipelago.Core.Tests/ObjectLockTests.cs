using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Paper Plane Supplies' 14 holder pieces are held by Containables and the
/// Drawer Controller and nothing else (DevTools sharing:, 2026-09-23), so while
/// the drawer counted as a group, holding Drawer freed them and the Containers
/// lock did nothing.
/// </summary>
public class ObjectLockTests
{
    private static bool Unlocked(params (string Class, bool Locked)[] owners)
    {
        var vote = new ObjectLock();
        foreach (var (cls, locked) in owners) vote.Add(cls, locked);
        return vote.Unlocked;
    }

    [Theory]
    [InlineData("DrawerController")]
    [InlineData("DrawerExpandableController")]
    [InlineData("Cupboard")]
    public void AnOpenEnclosureDoesNotFreeAnotherGroupsObject(string enclosure)
    {
        Assert.False(Unlocked(("Containables", true), (enclosure, false)));
        Assert.False(Unlocked((enclosure, false), ("Containables", true)));
    }

    [Fact]
    public void HoldingTheGroupsOwnAbilityFreesIt()
    {
        Assert.True(Unlocked(("Containables", false), ("DrawerController", false)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheDrawerItselfFollowsTheDrawer(bool drawerLocked, bool expected)
    {
        Assert.Equal(expected, Unlocked(("DrawerController", drawerLocked)));
    }

    /// <summary>
    /// AnimScrubbables is a door on the three cupboard levels and a PUZZLE on
    /// Wilting Flowers, whose "Upright" flowers are the thing to solve. Treated
    /// as a cover there, they stopped freezing and could be stood up without
    /// the ability (code survey, 2026-09-25).
    /// </summary>
    [Theory]
    [InlineData("DLC1 Tea Cabinet", true)]
    [InlineData("DLC1 Clock Cupboard", true)]
    [InlineData("DLC1 Trophy Cabinet", true)]
    [InlineData("Wilting Flowers", false)]
    [InlineData("", false)]
    public void AnimScrubbablesIsADoorOnlyOnTheCupboardLevels(string levelId, bool cover)
    {
        var obj = new ObjectLock(levelId);
        obj.Add("AnimScrubbables", true);
        Assert.Equal(cover, obj.IsCover);
    }

    /// <summary>
    /// Every AnimScrubbables group in the shipped table has been sorted into
    /// door or puzzle. A new one fails here until someone decides which it is.
    /// </summary>
    [Fact]
    public void EveryAnimScrubbablesLevelIsClassified()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        var table = LevelTable.FromJson(File.ReadAllText(path));
        var seen = table.Levels
            .Where(l => l.Controllers.Any(c => c.Type == "AnimScrubbables"))
            .Select(l => l.LevelId)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var known = ObjectLock.DoorLevels.Concat(ObjectLock.ScrubPuzzleLevels)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(known, seen);
    }

    [Fact]
    public void ADrawerSetPartIsACoverHeldByADrawerController()
    {
        var box = new ObjectLock();
        box.Add("DrawerController", true);
        Assert.True(box.IsDrawerSetPart);

        var cupboardDoor = new ObjectLock("DLC1 Tea Cabinet");
        cupboardDoor.Add("AnimScrubbables", true);
        Assert.False(cupboardDoor.IsDrawerSetPart);

        // Contents held by their own group as well are not the set.
        var spool = new ObjectLock();
        spool.Add("Draggables", true);
        spool.Add("DrawerController", true);
        Assert.False(spool.IsDrawerSetPart);
    }

    [Fact]
    public void ADrawerHeldByAnOpenGroupStillKnowsItsDrawerLock()
    {
        // Daggers: the Draggables group frees the four drawers, but their
        // drawer controller is locked, and AbilityLocks locks a drawer that
        // slides on that.
        var drawer = new ObjectLock();
        drawer.Add("DrawerExpandableController", true);
        drawer.Add("Draggables", false);
        Assert.True(drawer.Unlocked);
        Assert.True(drawer.DrawerSetLocked);

        var open = new ObjectLock();
        open.Add("DrawerController", false);
        Assert.False(open.DrawerSetLocked);
        Assert.True(open.HeldByDrawerSet);

        var loose = new ObjectLock();
        loose.Add("DraggablesOrdered", true);
        Assert.False(loose.HeldByDrawerSet);
    }

    [Fact]
    public void OnlyTheCoverItselfIsACover()
    {
        var door = new ObjectLock("DLC1 Tea Cabinet");
        door.Add("AnimScrubbables", true);
        Assert.True(door.IsCover);

        var drawer = new ObjectLock();
        drawer.Add("DrawerController", true);
        Assert.True(drawer.IsCover);

        // Jewelry Box: a watch is held by its group AND the drawer controller.
        var watch = new ObjectLock();
        watch.Add("Draggables", true);
        watch.Add("DrawerController", true);
        Assert.False(watch.IsCover);
    }

    [Fact]
    public void AnyActingGroupStillFreesASharedObject()
    {
        // Coins 1 (Shape): six coins in an Ordered group and a Stacked group.
        Assert.True(Unlocked(("DraggablesOrdered", false), ("StackablesZ", true)));
        // HangingToolsController maps to Drawer but moves what it holds.
        Assert.True(Unlocked(("HangingToolsController", false), ("Containables", true)));
    }

    [Theory]
    [InlineData("Cat Food Cans")]
    [InlineData("Boxes (Stacked)")]
    [InlineData("GoodTidings_Presents (Stacked)")]
    public void AStackedLevelReloadsWhenItsPiecesUnlock(string level)
    {
        // droha, Cat Food Cans, 2026-09-27: unlocked in place, the covered
        // cans could be picked up and not put down.
        Assert.True(ObjectLock.ResetOnUnlock(level, 6, 0));
        Assert.True(ObjectLock.ResetOnUnlock(level, 6, 2));
    }

    [Fact]
    public void OnlyAnUnlockReloadsAndOnlyOnTheThreeLevels()
    {
        Assert.False(ObjectLock.ResetOnUnlock("Cat Food Cans", 0, 0));   // nothing was locked
        Assert.False(ObjectLock.ResetOnUnlock("Cat Food Cans", 6, 6));   // nothing unlocked
        Assert.False(ObjectLock.ResetOnUnlock("Cat Food Cans", 0, 6));   // it locked instead
        Assert.False(ObjectLock.ResetOnUnlock("Wilting Flowers", 5, 0)); // the Dirt: harmless
        Assert.False(ObjectLock.ResetOnUnlock("DLC1 Sewing Box", 36, 0));
        Assert.False(ObjectLock.ResetOnUnlock(null!, 6, 0));
        Assert.Equal(3, ObjectLock.ResetOnUnlockLevels.Count);
    }

    /// <summary>
    /// Books (Randomized): a seed with a *_SYMMETRIC rule adds a second
    /// controller, Draggables, over the same 11 books Shuffle holds. By its
    /// class it needs nothing, so it freed every book and a player without
    /// Swapping could finish the puzzle (the 0.4.3 run, "abilities: 1 locked,
    /// 1 open, 11 objects, waiting on Swapping").
    /// </summary>
    [Fact]
    public void BooksSymmetricDraggablesLocksAsShuffle()
    {
        var draggables = ObjectLock.LockClass("Books (Randomized)", "Draggables", "Draggables");
        Assert.Equal("Shuffleables", draggables);

        var state = new AbilityState(new SlotData
        {
            AbilityLocks = true,
            Abilities = new Dictionary<string, List<string>>
            {
                ["Swapping"] = new() { "Shuffleables" },
            },
        });
        Assert.True(state.IsClassLocked(draggables));

        // A book is held by both, and neither frees it without Swapping.
        Assert.False(Unlocked(("Shuffleables", state.IsClassLocked("Shuffleables")),
                              (draggables, state.IsClassLocked(draggables))));

        state.Grant("Swapping");
        Assert.True(Unlocked(("Shuffleables", state.IsClassLocked("Shuffleables")),
                             (draggables, state.IsClassLocked(draggables))));
    }

    [Theory]
    [InlineData("Books (Randomized)", "Shuffle", "Shuffleables")]
    [InlineData("Books 3", "Height (Draggables)", "Draggables")]
    [InlineData("TrickOrTidy_ChocolateBars", "Height (Draggables)", "Draggables")]
    [InlineData("SomethingEggstra Fridge", "StandardObjects", "Draggables")]
    [InlineData("", "Draggables", "Draggables")]
    public void EveryOtherControllerLocksByItsOwnClass(string level, string controller, string cls)
    {
        // Books 3 and Chocolate Bars have the same shape, and their table
        // bypasses Swapping instead: there the drag rule is a real way out.
        Assert.Equal(cls, ObjectLock.LockClass(level, controller, cls));
    }

    /// <summary>
    /// Each override names a controller its level's table does not list and a
    /// class the table does hold, so the two cannot drift apart unnoticed.
    /// </summary>
    [Fact]
    public void EveryOverrideLocksAsAClassOfItsOwnLevel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        var table = LevelTable.FromJson(File.ReadAllText(path));
        Assert.NotEmpty(ObjectLock.LockedAs);
        foreach (var ((level, controller), cls) in ObjectLock.LockedAs)
        {
            var row = table.Levels.Single(l => l.LevelId == level);
            Assert.Contains(row.Controllers, c => c.Type == cls);
            Assert.DoesNotContain(row.Controllers, c => c.Name == controller);
        }
    }
}
