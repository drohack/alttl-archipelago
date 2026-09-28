using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Fixed endings (droha, 2026-09-28): each ending its own named check, the
/// group that IS an ending no part check of its own, per-colour merges, and
/// the router filing a completion by its id.
/// </summary>
public class EndingsTests
{
    private static LevelTable Table()
        => LevelTable.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")));

    private static LevelInfo Level(string id) => Table().ById(id)!;

    private static List<string> Suffixes(string id) => Endings.For(Level(id)).Select(e => e.Suffix).ToList();

    [Fact]
    public void AnEndingMadeOfOneGroupIsNamedForItAndThatGroupIsNoPart()
    {
        Assert.Equal(new[] { "Solution: Size (Elastic)", "Solution: Stacked" }, Suffixes("Spoons"));
        var names = LocationNames.ForInstance(Level("Spoons"), 1);
        Assert.Equal(new[] { "Spoons - Solution: Size (Elastic)", "Spoons - Solution: Stacked" }, names);
    }

    [Fact]
    public void SeveralEndingsOfOneControllerAreNumberedFromTheirIds()
    {
        Assert.Equal(new[] { "Solution: Ordered 1", "Solution: Ordered 2", "Solution: Ordered 3" },
            Suffixes("Keys"));
        Assert.Equal(new[] { "Solution: Design (Shuffle)", "Solution: Height" }, Suffixes("Books 3"));
    }

    [Fact]
    public void ASingleEndingIsJustSolutionAndKeepsEveryPart()
    {
        // The id names whichever controller the game passed; it is no group's
        // ending, so none is dropped and the requirement stays the level's.
        var radial = Level("Radial Dance Party");
        Assert.Equal(new[] { "Solution" }, Suffixes("Radial Dance Party"));
        Assert.Null(Endings.For(radial)[0].Group);
        Assert.Empty(Endings.EndingGroups(radial));
        Assert.Equal(11, LocationNames.ForInstance(radial, 1).Count);
    }

    [Fact]
    public void AGeneratedPuzzleHasFixedEndingsByPosition()
    {
        // droha: "don't generated puzzles still have fixed solutions?" The seed
        // picks which sorting rules count; the ids stay _0 and _1.
        Assert.Equal(new[] { "Solution: Ordered 1", "Solution: Ordered 2" }, Suffixes("Pencils (Randomized)"));
        Assert.Equal(new[] { "Ordered_0", "Ordered_1" }, Endings.For(Level("Pencils (Randomized)")).Select(e => e.Id));
        Assert.Equal(new[] { "Solution" }, Suffixes("Stamps (Randomized)"));
    }

    [Fact]
    public void BooksSecondEndingAnswersToEitherController()
    {
        // gensweep:40:995: a symmetric seed checks the second solution with a
        // Draggables controller (Draggables_0), the rest with Shuffle
        // (Shuffle_1). droha: "can we not see what solutions it has after the
        // seed is generated? so we can set the solution names/ids correctly?"
        Assert.Equal(new[] { "Solution: Shuffle 1", "Solution: Shuffle 2" }, Suffixes("Books (Randomized)"));
        Assert.Equal(new[] { "Shuffle_1", "Draggables_0" },
            Endings.Alternatives(Endings.For(Level("Books (Randomized)"))[1].Id));
    }

    [Fact]
    public void AnEndingWithAlternativesFilesTheSameCheckForEither()
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "Books (Randomized)", LevelIndex = 995, Instance = 1, Seed = 1 });
        data.Endings["Books (Randomized)"] = new List<EndingEntry>
        {
            new() { Id = "Shuffle_0", Location = "Solution: Shuffle 1" },
            new() { Id = "Shuffle_1|Draggables_0", Location = "Solution: Shuffle 2" },
        };
        foreach (var name in new[] { "Books (Randomized) - Solution: Shuffle 1", "Books (Randomized) - Solution: Shuffle 2" })
            data.Requirements[name] = new Requirement();
        var router = new CheckRouter(data);
        // A symmetric seed: the drag rule first, then the swap rule.
        Assert.Equal("Books (Randomized) - Solution: Shuffle 2",
            router.ForEnding(0, "Draggables_0", new[] { "Draggables_0" }));
        Assert.Equal("Books (Randomized) - Solution: Shuffle 1",
            router.ForEnding(0, "Shuffle_0", new[] { "Draggables_0", "Shuffle_0" }));
        // Any other seed.
        Assert.Equal("Books (Randomized) - Solution: Shuffle 2",
            router.ForEnding(0, "Shuffle_1", new[] { "Shuffle_1" }));
    }

    [Fact]
    public void AnEndingNobodyHasSeenIsOther()
    {
        // Every ending has been seen since droha's hand tests (2026-09-28);
        // a table with a gap is still named this way.
        var level = Level("DLC2 Bookshelf");
        level.Endings = new List<string?> { "Shuffle---Top-Left_0", null, null };
        Assert.Equal(new[] { "Solution: Shuffle - Top Left", "Solution: Other 1", "Solution: Other 2" },
            Endings.For(level).Select(e => e.Suffix));
    }

    [Fact]
    public void EveryEndingHasBeenSeen()
    {
        Assert.DoesNotContain(Table().Levels, l => l.Endings != null && l.Endings.Contains(null));
        Assert.Equal(new[] { "Solution: Knife", "Solution: Lock", "Solution: Compass" }, Suffixes("DLC2 Boss"));
    }

    [Fact]
    public void MedicineCabinetIsCheckedPerColour()
    {
        var parts = LocationNames.ForInstance(Level("MedicineCabinet"), 1).Skip(1).ToList();
        Assert.Contains("Medicine Cabinet - Red Items", parts);
        Assert.Equal(6, parts.Count);
        var red = ControllerGroups.For(Level("MedicineCabinet")).Single(g => g.DisplayName == "Red Items");
        Assert.Equal(7, red.Members.Count);
        Assert.Contains("Oral Containables", red.Members);
    }

    [Fact]
    public void AMergedPartWaitsForItsLastController()
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "MedicineCabinet", LevelIndex = 49, Instance = 1 });
        data.ControllerGroups["MedicineCabinet"] = new Dictionary<string, string>
        {
            ["Cup Draggables"] = "Red Items",
            ["Brush Draggables"] = "Red Items",
            ["Swabs Draggables"] = "Swabs",
        };
        var router = new CheckRouter(data);
        var red = router.GroupMembers(0, "Cup Draggables");
        Assert.Equal(new[] { "Brush Draggables", "Cup Draggables" }, red.OrderBy(m => m));
        Assert.Single(router.GroupMembers(0, "Swabs Draggables"));
        Assert.Empty(router.GroupMembers(0, "Not A Controller"));

        Assert.False(CheckRouter.GroupSolved(red, new Dictionary<string, bool>
            { ["Cup Draggables"] = true, ["Brush Draggables"] = false }));
        Assert.True(CheckRouter.GroupSolved(red, new Dictionary<string, bool>
            { ["Cup Draggables"] = true, ["Brush Draggables"] = true }));
        // A member that never registered is not waited for; none at all is not done.
        Assert.True(CheckRouter.GroupSolved(red, new Dictionary<string, bool> { ["Cup Draggables"] = true }));
        Assert.False(CheckRouter.GroupSolved(red, new Dictionary<string, bool>()));
    }

    [Fact]
    public void MirrorIsTheBigItemsAndTheSolution()
    {
        // droha: the jug, candle, dish, bottle and the books-and-box stack in
        // place are a check; the little things - the lemon wedge, what goes in
        // the containers, the candle put out - are folded into the Solution.
        var groups = ControllerGroups.For(Level("Mirror"));
        Assert.Equal(new[] { "Little Things", "Still Life" }, groups.Select(g => g.DisplayName).OrderBy(n => n));
        var big = groups.Single(g => g.DisplayName == "Still Life");
        Assert.Equal(5, big.Members.Count);
        Assert.Contains("Books + Box StackablesY ", big.Members);
        Assert.Equal(3, groups.Single(g => g.DisplayName == "Little Things").Members.Count);
        Assert.Equal(new[] { "Mirror - Solution", "Mirror - Still Life" }, LocationNames.ForInstance(Level("Mirror"), 1));
    }

    [Fact]
    public void EveryEndingNameIsUniqueWithinItsLevel()
    {
        foreach (var level in Table().Levels)
        {
            var names = LocationNames.ForInstance(level, 1);
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(level.SolutionCount, Endings.For(level).Count);
        }
    }

    private static CheckRouter Router() => Seed().Router;

    private static (CheckRouter Router, HashSet<string> Collected) Seed()
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "Spoons", LevelIndex = 24, Instance = 1 });
        data.Slots.Add(new SlotEntry { LevelId = "DLC2 Bookshelf", LevelIndex = 1230, Instance = 1 });
        data.Slots.Add(new SlotEntry { LevelId = "Books (Randomized)", LevelIndex = 995, Instance = 1, Seed = 7 });
        data.Slots.Add(new SlotEntry { LevelId = "Pencils (Randomized)", LevelIndex = 999, Instance = 2, Seed = 9 });
        data.Endings["Pencils (Randomized)"] = new List<EndingEntry>
        {
            new() { Id = "Ordered_0", Location = "Solution: Ordered 1" },
            new() { Id = "Ordered_1", Location = "Solution: Ordered 2" },
        };
        data.Endings["Spoons"] = new List<EndingEntry>
        {
            new() { Id = "Size-(Elastic)_0", Location = "Solution: Size (Elastic)" },
            new() { Id = "Stacked_0", Location = "Solution: Stacked" },
        };
        data.Endings["DLC2 Bookshelf"] = new List<EndingEntry>
        {
            new() { Id = "Shuffle---Top-Left_0", Location = "Solution: Shuffle - Top Left" },
            new() { Id = null, Location = "Solution: Other 1" },
            new() { Id = null, Location = "Solution: Other 2" },
        };
        foreach (var name in new[]
                 {
                     "Spoons - Solution: Size (Elastic)", "Spoons - Solution: Stacked", "Spoons - Beaten",
                     "Bookshelf (Seeing Stars) - Solution: Shuffle - Top Left",
                     "Bookshelf (Seeing Stars) - Solution: Other 1",
                     "Bookshelf (Seeing Stars) - Solution: Other 2",
                     "Books (Randomized) - Solution 1", "Books (Randomized) - Solution 2",
                     "Pencils (Randomized) #2 - Solution: Ordered 1",
                     "Pencils (Randomized) #2 - Solution: Ordered 2",
                 })
        {
            data.Requirements[name] = new Requirement();
        }
        return (new CheckRouter(data), new HashSet<string>(StringComparer.Ordinal));
    }

    [Fact]
    public void AnEndingFilesTheLocationItsIdNames()
    {
        var router = Router();
        // Found second, still its own location: fixed, not "the 2nd found".
        Assert.Equal("Spoons - Solution: Stacked", router.ForEnding(0, "Stacked_0", new[] { "Stacked_0" }));
        Assert.Equal("Spoons - Solution: Size (Elastic)",
            router.ForEnding(0, "Size-(Elastic)_0", new[] { "Stacked_0", "Size-(Elastic)_0" }));
    }

    [Fact]
    public void AnUnknownIdFillsTheUnseenEndingsInTheOrderFound()
    {
        var router = Router();
        var found = new[] { "Shuffle---Top-Left_0", "Shuffle---Top-Left_3", "Shuffle---Mid-Right_1", "Odd_9" };
        Assert.Equal("Bookshelf (Seeing Stars) - Solution: Shuffle - Top Left", router.ForEnding(1, found[0], found));
        Assert.Equal("Bookshelf (Seeing Stars) - Solution: Other 1", router.ForEnding(1, found[1], found));
        Assert.Equal("Bookshelf (Seeing Stars) - Solution: Other 2", router.ForEnding(1, found[2], found));
        // More unknown ids than unseen endings: the next ending in order.
        Assert.Equal("Bookshelf (Seeing Stars) - Solution: Shuffle - Top Left", router.ForEnding(1, found[3], found));
    }

    [Fact]
    public void AForcedOrSkippedCompletionStillFilesSomethingAndAlwaysTheSame()
    {
        var router = Router();
        // Spoons' endings are all known: "_-1" is the first unknown, so the
        // first ending in order - at this launch and every later one.
        var found = new[] { "Stacked_-1" };
        Assert.Equal("Spoons - Solution: Size (Elastic)", router.ForEnding(0, "Stacked_-1", found));
        Assert.Equal("Spoons - Solution: Size (Elastic)", router.ForEnding(0, "Stacked_-1", found));
        Assert.Null(router.ForEnding(0, "not-found-yet", found));
    }

    [Fact]
    public void ALevelWithoutAnEndingsTableFilesByTheOrderFound()
    {
        var router = Router();
        Assert.Equal("Books (Randomized) - Solution 2",
            router.ForEnding(2, "Shuffle_1", new[] { "Draggables_0", "Shuffle_1" }));
    }

    [Fact]
    public void AGeneratedPuzzlesSecondEndingFilesItsOwnCheckWhenFoundFirst()
    {
        var router = Router();
        Assert.Equal("Pencils (Randomized) #2 - Solution: Ordered 2",
            router.ForEnding(3, "Ordered_1", new[] { "Ordered_1" }));
    }

    [Fact]
    public void TheStarsCountTheEndings()
    {
        var (router, collected) = Seed();
        collected.Add("Spoons - Solution: Stacked");
        Assert.Equal((1, 2), router.SolutionStars(0, collected.Contains));
        Assert.Equal(3, router.SolutionsOf(1).Count);
        Assert.Contains("Spoons - Solution: Stacked", router.ForSlot(0));
    }
}
