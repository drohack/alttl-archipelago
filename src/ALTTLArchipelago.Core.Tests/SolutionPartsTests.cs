using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// A solution is judged by the controllers its ending names, not the level's
/// whole ability set. 0.4.2 playtest: Spoons' "Size-(Elastic)_0" was withheld
/// for Stacking, Figurines' Solution 1 for Gadgets, Coins 1 for Stacking.
/// </summary>
public class SolutionPartsTests
{
    [Fact]
    public void TheIdsFromThePlaytestNameTheirControllers()
    {
        Assert.Equal(new[] { "Size (Elastic)" },
            SolutionParts.ControllersFor("Size-(Elastic)_0", new[] { "Stacked", "Size (Elastic)" }));
        Assert.Equal(new[] { "SortingItemsDraggables" },
            SolutionParts.ControllersFor("SortingItemsDraggables_0",
                new[] { "SortingItemsDraggables", "Draggables", "Groupables (Achievement)" }));
        Assert.Equal(new[] { "Ordered" },
            SolutionParts.ControllersFor("Ordered_1", new[] { "Ordered", "Stacked" }));
        // Names no controller: the caller keeps the whole-level rule.
        Assert.Empty(SolutionParts.ControllersFor("Draggables_0",
            new[] { "Trinkets Draggables", "Cup Draggables" }));
        Assert.Empty(SolutionParts.ControllersFor("", new[] { "Stacked" }));
    }

    [Fact]
    public void EveryObservedEndingIdIsCounted()
    {
        // Pinned: a re-harvest of the fixture, a levels.json change or a
        // change to the matching rule moves this number, and it should be
        // looked at when it does. Measured 2026-09-27: 198 of 219.
        // 199 of 220 on 2026-10-02: Figurines' second sort,
        // SortingItemsDraggables_1, from droha's play. 223 of 244 the same
        // day: droha's hand test of ten levels' ending names, every one as
        // the table guessed, and Ghost Cat's Indexables_0.
        var table = LevelTable.FromJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "levels.json")));
        int total = 0, named = 0;
        var unnamed = new List<string>();
        foreach (var line in File.ReadLines(
                     Path.Combine(AppContext.BaseDirectory, "solution-ids-observed.tsv")))
        {
            if (line.StartsWith("#") || line.StartsWith("levelIndex")) continue;
            var cells = line.Split('\t');
            var level = table.ByIndex(int.Parse(cells[0]));
            if (level == null) continue;
            var names = level.Controllers.Select(c => c.Name).ToList();
            foreach (var id in cells[4].Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                total++;
                if (SolutionParts.ControllersFor(id, names).Count > 0) named++;
                else unnamed.Add($"{level.LevelId}:{id}");
            }
        }
        Assert.True(named == 223 && total == 244,
            $"{named} of {total} observed ids name a controller; unnamed: {string.Join(", ", unnamed)}");
    }

    [Fact]
    public void TheRouterMapsAnEndingToItsPartLocations()
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "Books 3", LevelIndex = 13, Instance = 1 });
        data.ControllerGroups["Books 3"] = new Dictionary<string, string>
        {
            ["Design (Shuffle)"] = "Design",
            ["Height (Draggables)"] = "Height",
        };
        data.Requirements["Books 3 - Design"] = new Requirement();
        data.Requirements["Books 3 - Height"] = new Requirement();
        var router = new CheckRouter(data);

        Assert.Equal(new[] { "Books 3 - Design" }, router.PartsForSolution(0, "Design-(Shuffle)_0"));
        Assert.Equal(new[] { "Books 3 - Height" }, router.PartsForSolution(0, "Height-(Draggables)_1"));
        Assert.Empty(router.PartsForSolution(0, "Cupboard_0"));
    }

    [Fact]
    public void AWithheldSolutionKeepsWhatItsEndingNeeded()
    {
        var state = new RunStateData();
        Assert.True(state.AddWithheld("Spoons - Solution 2", new[] { "Spoons - Size (Elastic)" }));
        var reloaded = RunStateData.FromJson(state.ToJson())!;
        Assert.Equal(new[] { "Spoons - Size (Elastic)" }, reloaded.NeedsFor("Spoons - Solution 2"));

        Assert.True(reloaded.RemoveWithheld("Spoons - Solution 2"));
        Assert.Empty(reloaded.NeedsFor("Spoons - Solution 2"));
        // A run file from before this existed: no map, nothing needed.
        Assert.Empty(RunStateData.FromJson("{\"withheld\":[\"X\"]}")!.NeedsFor("X"));
    }
}
