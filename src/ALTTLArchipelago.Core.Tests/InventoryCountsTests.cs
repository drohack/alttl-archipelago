using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Counting received items. Every test here is a way a reconnect could corrupt
/// what the player holds - which is not hypothetical: it happened.
/// </summary>
public class InventoryCountsTests
{
    private static bool IsAbility(string name)
        => name is "Swapping" or "Stacking" or "Ordering";

    [Fact]
    public void CountsEachKindOfItem()
    {
        var counts = InventoryCounts.From(new[]
        {
            ItemNames.Pack, ItemNames.Pack, ItemNames.Skip,
            ItemNames.CatTrap, ItemNames.CatTrap, ItemNames.CatTrap,
            ItemNames.BeatenToken, ItemNames.Credits,
            "Swapping", "Colour Scheme",
        }, IsAbility);

        Assert.Equal(2, counts.Packs);
        Assert.Equal(1, counts.Skips);
        Assert.Equal(3, counts.Traps);
        Assert.Equal(1, counts.Beaten);
        Assert.True(counts.HasCredits);
        Assert.Equal(new[] { "Swapping" }, counts.Abilities);
    }

    [Fact]
    public void FillerIsIgnoredRatherThanMistakenForAnything()
    {
        // Title Theme, Colour Scheme and Daily Badge are real items the server
        // sends. They must not land in Abilities, which would hand the player a
        // mechanic they were never given.
        var counts = InventoryCounts.From(
            new[] { "Colour Scheme", "Title Theme", "Daily Badge" }, IsAbility);

        Assert.Empty(counts.Abilities);
        Assert.Equal(0, counts.Packs);
        Assert.False(counts.HasCredits);
    }

    [Fact]
    public void CountingTheSameListTwiceGivesTheSameAnswer()
    {
        // The invariant the whole design rests on: Archipelago replays the item
        // list on every connect, so applying it again must be a no-op.
        var list = new[] { ItemNames.Pack, ItemNames.Pack, ItemNames.Skip };

        var first = InventoryCounts.From(list, IsAbility);
        var second = InventoryCounts.From(list, IsAbility);

        Assert.Equal(first.Packs, second.Packs);
        Assert.Equal(first.Skips, second.Skips);
    }

    [Fact]
    public void ADoubledListDoublesTheCounts()
    {
        // The counterexample, kept deliberately: counting is not what protects
        // a reconnect - the SESSION BOUNDARY is. Feed the counter a list that
        // has the replay appended to the previous session's items and it will
        // faithfully report twice as many packs, because that is what it was
        // given.
        //
        // This is exactly what happened. Inventory._received was cumulative
        // across an in-session reconnect, so packs and skips silently doubled
        // and traps fired a burst. The fix was Inventory.NewSession(), not
        // anything here. If this test ever starts failing, someone has taught
        // the counter to dedupe, and that would hide the real bug rather than
        // fix it - two packs really are two packs.
        var oneSession = new[] { ItemNames.Pack, ItemNames.Pack };
        var appendedTwice = oneSession.Concat(oneSession);

        Assert.Equal(2, InventoryCounts.From(oneSession, IsAbility).Packs);
        Assert.Equal(4, InventoryCounts.From(appendedTwice, IsAbility).Packs);
    }

    [Fact]
    public void AnEmptyOrNullNameIsSkipped()
    {
        var counts = InventoryCounts.From(
            new[] { ItemNames.Pack, "", ItemNames.Pack }, IsAbility);

        Assert.Equal(2, counts.Packs);
    }

    [Fact]
    public void WithNoAbilityCatalogueNothingIsAnAbility()
    {
        // Items arrive before slot_data has been parsed, so the catalogue can
        // genuinely be absent. Better to count an ability as filler for one
        // pass than to guess.
        var counts = InventoryCounts.From(new[] { "Swapping", ItemNames.Pack });

        Assert.Empty(counts.Abilities);
        Assert.Equal(1, counts.Packs);
    }
}
