using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Display names are part of Archipelago location names, which are part of the
/// datapackage checksum. Changing one breaks every seed already in flight.
///
/// So these tests pin behaviour, not just check it. A failure here means "you
/// are about to break saves"; the fix is almost never to update the
/// expectation.
/// </summary>
public class DisplayNameTests
{
    [Theory]
    // Event packs move the pack from a prefix to a trailing context.
    [InlineData("GoodTidings_Wreath", "Wreath (Good Tidings)")]
    [InlineData("TrickOrTidy_ChocolateBars", "Chocolate Bars (Trick or Tidy)")]
    [InlineData("MerryMess_SnowGlobes", "Snow Globes (Merry Mess)")]
    [InlineData("SnackPack Pretzels", "Pretzels (Snack Pack)")]
    [InlineData("SomethingEggstra PaintedEggs", "Painted Eggs (Something Eggstra)")]
    // NeatStreak is the internal name for the pack players see as Drawer Chores.
    [InlineData("NeatStreak_Paper Plane Supplies", "Paper Plane Supplies (Drawer Chores)")]
    // A pack level that already has parentheses must not end up with two groups.
    [InlineData("GoodTidings_Cookies (Jigsaw)", "Cookies Jigsaw (Good Tidings)")]
    [InlineData("GoodTidings_Presents (Stacked)", "Presents Stacked (Good Tidings)")]
    // The camel splitter gets this one wrong, hence the override.
    [InlineData("TrickOrTidy_JackOLanterns", "Jack O'Lanterns (Trick or Tidy)")]
    // Plain camel case.
    [InlineData("MedicineCabinet", "Medicine Cabinet")]
    [InlineData("SpiderWeb", "Spider Web")]
    [InlineData("UtensilsDrawer", "Utensils Drawer")]
    [InlineData("TupperwareNesting", "Tupperware Nesting")]
    // Names that are already fine must be left completely alone.
    [InlineData("Books (Randomized)", "Books (Randomized)")]
    [InlineData("Coins 1 (Shape)", "Coins 1 (Shape)")]
    [InlineData("Post-It Notes Scribble #1 (Simple Line)", "Post-It Notes Scribble #1 (Simple Line)")]
    [InlineData("Spice Jars", "Spice Jars")]
    public void NamesAreStable(string levelId, string expected)
    {
        Assert.Equal(expected, DisplayNames.For(levelId));
    }

    [Fact]
    public void EveryLevelInTheTableGetsAUniqueName()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        var table = LevelTable.FromJson(File.ReadAllText(path));

        var names = table.Levels.Select(l => DisplayNames.For(l.LevelId)).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        // Collisions would merge two levels' locations into one.
        var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate display names: " + string.Join(", ", dupes));
        Assert.Equal(111, names.Count);
    }

    [Fact]
    public void NoNameKeepsAnInternalPrefixOrRunOnWord()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        var table = LevelTable.FromJson(File.ReadAllText(path));

        foreach (var l in table.Levels)
        {
            var n = DisplayNames.For(l.LevelId);
            Assert.DoesNotContain("_", n, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", n, StringComparison.Ordinal);
            Assert.DoesNotContain(") (", n, StringComparison.Ordinal);
        }
    }
}
