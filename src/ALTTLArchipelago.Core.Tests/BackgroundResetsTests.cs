using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The Background Reset Token's arithmetic. droha, 2026-09-25: a button that
/// spends one to put the backdrops back, never automatic.
/// </summary>
public class BackgroundResetsTests
{
    [Fact]
    public void APressIsRefusedWithNoTokenOrNothingToReset()
    {
        Assert.Equal(BackgroundResetRefusal.NoneHeld,
                     BackgroundResets.Check(available: 0, traps: 4, resetAt: 0));
        Assert.Equal(BackgroundResetRefusal.NothingToReset,
                     BackgroundResets.Check(available: 1, traps: 4, resetAt: 4));
        Assert.Equal(BackgroundResetRefusal.None,
                     BackgroundResets.Check(available: 1, traps: 5, resetAt: 4));
    }

    [Fact]
    public void AReplayedItemListLandsOnTheSameBackdrop()
    {
        // The server resends every trap on connect. The mark is absolute, so
        // counting the doubled list again changes nothing a reset decided.
        var list = new[] { ItemNames.BackgroundTrap, ItemNames.BackgroundTrap,
                           ItemNames.BackgroundTrap };
        var first = InventoryCounts.From(list).BackgroundTraps;
        var again = InventoryCounts.From(list).BackgroundTraps;

        Assert.Equal(0, BackgroundResets.EffectiveTraps(first, resetAt: 3));
        Assert.Equal(0, BackgroundResets.EffectiveTraps(again, resetAt: 3));
        Assert.Equal(1, BackgroundResets.EffectiveTraps(first + 1, resetAt: 3));
        Assert.Equal(2, BackgroundResets.Available(received: 3, used: 1));
        Assert.Equal(0, BackgroundResets.Available(received: 1, used: 2));
    }
}
