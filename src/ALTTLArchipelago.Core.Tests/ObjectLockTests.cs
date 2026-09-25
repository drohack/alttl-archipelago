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

    [Fact]
    public void AnyActingGroupStillFreesASharedObject()
    {
        // Coins 1 (Shape): six coins in an Ordered group and a Stacked group.
        Assert.True(Unlocked(("DraggablesOrdered", false), ("StackablesZ", true)));
        // HangingToolsController maps to Drawer but moves what it holds.
        Assert.True(Unlocked(("HangingToolsController", false), ("Containables", true)));
    }
}
