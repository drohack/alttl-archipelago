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
            ItemNames.HintPage, ItemNames.HintPage,
            ItemNames.BackgroundTrap, ItemNames.BackgroundTrap,
            ItemNames.BackgroundTrap,
            "Swapping",
        }, IsAbility);

        Assert.Equal(2, counts.Packs);
        Assert.Equal(1, counts.Skips);
        Assert.Equal(3, counts.Traps);
        Assert.Equal(1, counts.Beaten);
        Assert.Equal(2, counts.HintPages);
        Assert.Equal(3, counts.BackgroundTraps);
        Assert.True(counts.HasCredits);
        Assert.Equal(new[] { "Swapping" }, counts.Abilities);
    }

    [Fact]
    public void AHintPageIsNotMistakenForAnAbility()
    {
        // The failure this guards is quiet rather than loud. Anything the
        // counter does not recognise falls through to isAbility, and the mod
        // then hands the player a mechanic they were never sent - so a new
        // item name that is missing from ItemNames.IsSpecial does not error,
        // it silently unlocks something. "Hint Page" is a plausible ability
        // name to a matcher that works by elimination.
        var counts = InventoryCounts.From(
            new[] { ItemNames.HintPage }, _ => true);

        Assert.Equal(1, counts.HintPages);
        Assert.Empty(counts.Abilities);
    }

    [Fact]
    public void HintPagesAreCountedNotLatched()
    {
        // A level can hold up to five separately erasable pages, so holding
        // three Hint Pages has to mean three, not "has hints". A boolean here
        // would have been the natural shape and would silently cap every
        // multi-page notepad at one.
        var counts = InventoryCounts.From(
            new[] { ItemNames.HintPage, ItemNames.HintPage, ItemNames.HintPage },
            IsAbility);

        Assert.Equal(3, counts.HintPages);
    }

    [Fact]
    public void AnUnknownNameIsIgnoredRatherThanMistakenForAnAbility()
    {
        // Anything the counter does not recognise falls through to isAbility,
        // and the mod then hands the player a mechanic they were never sent.
        // A name from an older seed - these three were the filler pool before
        // backgrounds replaced them - must do nothing rather than something.
        var counts = InventoryCounts.From(
            new[] { "Colour Scheme", "Title Theme", "Daily Badge" }, IsAbility);

        Assert.Empty(counts.Abilities);
        Assert.Equal(0, counts.Packs);
        Assert.False(counts.HasCredits);
    }

    [Fact]
    public void BackgroundsAreCountedSoAReplayLandsOnTheSameColour()
    {
        // The invariant the whole background design rests on. The colour shown
        // is palette[count % length], so it is a pure function of the count -
        // which is what makes Archipelago's replay of the entire item list on
        // every connect a no-op. A handler that stepped the palette forward on
        // each arrival would put the player on a different colour every login.
        var list = new[]
        {
            ItemNames.BackgroundTrap, ItemNames.BackgroundTrap,
            ItemNames.BackgroundTrap,
        };

        var first = InventoryCounts.From(list, IsAbility);
        var second = InventoryCounts.From(list, IsAbility);

        Assert.Equal(3, first.BackgroundTraps);
        Assert.Equal(first.BackgroundTraps, second.BackgroundTraps);
    }

    [Fact]
    public void EveryBackdropReadsTheOneBackgroundCounter()
    {
        // There used to be two items and two counters, and the pause screen
        // could sit several colours behind the puzzle. One item, one counter:
        // the puzzle, the pause screen and the level select all step together.
        var counts = InventoryCounts.From(
            new[] { ItemNames.BackgroundTrap, ItemNames.BackgroundTrap,
                    ItemNames.BackgroundTrap }, IsAbility);

        Assert.Equal(3, counts.BackgroundTraps);
    }

    [Fact]
    public void ABackgroundChangeTrapIsNotMistakenForAnAbility()
    {
        // The rename is exactly the kind of change that drops an item out of
        // IsSpecial and leaves it landing in the held-ability set instead,
        // where it would silently unlock nothing and gate nothing.
        var counts = InventoryCounts.From(
            new[] { ItemNames.BackgroundTrap }, IsAbility);

        Assert.Equal(1, counts.BackgroundTraps);
        Assert.Empty(counts.Abilities);
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
