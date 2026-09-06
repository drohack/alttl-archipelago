using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The offline cache, round-tripped through the real generator payload.
///
/// The test that matters is the ITEM LIST surviving. slot_data round-tripping
/// is the obvious half and the harmless one to get wrong - a run that comes
/// back with the right puzzles and no packs is a locked track, which is worse
/// than not starting at all.
/// </summary>
public class CachedSessionTests
{
    private static SlotData Example() => ExampleSeed.Load();

    private static CachedSession Sample(params string[] items)
        => CachedSession.Of("droha", "abc12345", Example(), items,
                            new DateTime(2026, 9, 6, 1, 2, 3));

    [Fact]
    public void ARoundTripKeepsTheDrawAndTheItems()
    {
        var items = new[]
        {
            ItemNames.Pack, ItemNames.Pack, ItemNames.Skip,
            "Rotating", ItemNames.BeatenToken, ItemNames.Pack,
        };
        var before = Sample(items);

        var after = CachedSession.FromJson(before.ToJson());

        Assert.NotNull(after);
        Assert.Equal("droha", after!.SlotName);
        Assert.Equal("abc12345", after.Seed);
        Assert.Equal("2026-09-06 01:02:03", after.SavedAt);

        // Order and duplicates both, because the counts are derived from them.
        Assert.Equal(items, after.Items);
        Assert.Equal(before.Slot.Slots.Count, after.Slot.Slots.Count);
        Assert.Equal(before.Slot.PackTotal, after.Slot.PackTotal);
        Assert.Equal(before.Slot.PackSize, after.Slot.PackSize);
        Assert.Equal(before.Slot.PackBoundaries, after.Slot.PackBoundaries);
        Assert.Equal(before.Slot.LevelsToBeat, after.Slot.LevelsToBeat);
        Assert.Equal(before.Slot.Fingerprint(), after.Slot.Fingerprint());
    }

    /// <summary>
    /// The point of caching items at all: the same counts come back.
    ///
    /// Asserted through InventoryCounts rather than by comparing the list,
    /// because what the player sees is the counts, and this is the shortest
    /// path from "the file survived" to "the run is the one I left".
    /// </summary>
    [Fact]
    public void TheRestoredListCountsTheSame()
    {
        var items = new[]
        {
            ItemNames.Pack, ItemNames.Pack, ItemNames.Pack,
            ItemNames.Skip, ItemNames.Skip,
            ItemNames.CatTrap, ItemNames.HintPage,
            "Rotating",
        };
        bool IsAbility(string n) => n == "Rotating";

        var restored = CachedSession.FromJson(Sample(items).ToJson())!;

        var before = InventoryCounts.From(items, IsAbility);
        var after = InventoryCounts.From(restored.Items, IsAbility);

        Assert.Equal(before.Packs, after.Packs);
        Assert.Equal(before.Skips, after.Skips);
        Assert.Equal(before.Traps, after.Traps);
        Assert.Equal(before.HintPages, after.HintPages);
        Assert.Equal(before.Abilities, after.Abilities);
        Assert.Equal(3, after.Packs);
    }

    [Fact]
    public void AnEmptyItemListIsFine()
    {
        var after = CachedSession.FromJson(Sample().ToJson());

        Assert.NotNull(after);
        Assert.Empty(after!.Items);
        Assert.Empty(after.Problems());
    }

    [Fact]
    public void AGoodCacheHasNoProblems()
    {
        Assert.Empty(Sample(ItemNames.Pack).Problems());
    }

    [Theory]
    [InlineData("", "abc12345", "no slot name")]
    [InlineData("droha", "", "no seed")]
    public void TheTwoIdentifyingFieldsAreRequired(string slot, string seed,
                                                   string expected)
    {
        var cache = CachedSession.Of(slot, seed, Example(),
                                     Array.Empty<string>(), DateTime.UnixEpoch);

        Assert.Contains(expected, cache.Problems());
    }

    /// <summary>
    /// A cache carrying a draw the mod could not play must be refused here
    /// rather than halfway through starting a run on it.
    /// </summary>
    [Fact]
    public void ABrokenDrawIsAProblemWithTheCache()
    {
        var cache = CachedSession.Of("droha", "abc12345", new SlotData(),
                                     Array.Empty<string>(), DateTime.UnixEpoch);

        Assert.NotEmpty(cache.Problems());
        Assert.Contains("no slots", cache.Problems());
    }

    [Theory]
    [InlineData("droha", true)]
    [InlineData("Droha", false)]      // Archipelago slot names are case-sensitive
    [InlineData("someone-else", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ItOnlyBelongsToTheSlotItWasSavedUnder(string configured, bool expected)
    {
        Assert.Equal(expected, Sample().IsFor(configured));
    }

    /// <summary>
    /// Garbage on disk must read as "no cache", not as an exception on launch.
    /// A cache that cannot be parsed is a bad day; a mod that will not load is
    /// a worse one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"slot_name\": ")]
    [InlineData("[1, 2, 3]")]
    public void UnreadableTextIsNullRatherThanAThrow(string text)
    {
        Assert.Null(CachedSession.FromJson(text));
    }

    /// <summary>
    /// A cache written by an older version has fields this one does not know
    /// and lacks fields it expects. Both must survive, or every mod update
    /// silently ends offline play for everyone mid-run.
    /// </summary>
    [Fact]
    public void AnOlderOrNewerCacheStillLoads()
    {
        var json = "{\"slot_name\":\"droha\",\"seed\":\"abc12345\","
                   + "\"something_from_the_future\":42}";

        var cache = CachedSession.FromJson(json);

        Assert.NotNull(cache);
        Assert.Equal("droha", cache!.SlotName);
        Assert.Empty(cache.Items);
        // No draw, so it is refused - which is the point. It parsed far enough
        // to say why rather than throwing.
        Assert.Contains("no slots", cache.Problems());
    }

    /// <summary>
    /// The list is copied on the way in. It is Inventory's live list at the
    /// call site, and it keeps changing.
    /// </summary>
    [Fact]
    public void TheItemListIsCopiedNotAliased()
    {
        var live = new List<string> { ItemNames.Pack };

        var cache = CachedSession.Of("droha", "abc12345", Example(), live,
                                     DateTime.UnixEpoch);
        live.Add(ItemNames.Pack);
        live.Add(ItemNames.Skip);

        Assert.Single(cache.Items);
    }
}
