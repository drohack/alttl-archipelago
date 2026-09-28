using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Achievements as checks: the table names real puzzles, the router files an
/// achievement only on the puzzle that awards it and only when the seed has
/// the location, and they stay outside the star and the Skip.
/// </summary>
public class AchievementChecksTests
{
    private static LevelTable Table()
        => LevelTable.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")));

    private static SlotData Seed(bool achievements)
    {
        var data = new SlotData();
        data.Slots.Add(new SlotEntry { LevelId = "Eggs", LevelIndex = 27, Instance = 1 });
        data.Slots.Add(new SlotEntry { LevelId = "Place Setting", LevelIndex = 32, Instance = 1 });
        data.Slots.Add(new SlotEntry { LevelId = "Breadtags", LevelIndex = 28, Instance = 2 });
        var names = new List<string>
        {
            "Eggs - Solution 1", "Eggs - Beaten",
            "Place Setting - Solution 1", "Place Setting - Beaten",
            "Breadtags #2 - Solution 1", "Breadtags #2 - Beaten",
        };
        if (achievements)
        {
            names.Add("Eggs - Achievement: Exacting Eggs");
            names.Add("Place Setting - Achievement: Bad Kitty");
        }
        foreach (var name in names) data.Requirements[name] = new Requirement();
        return data;
    }

    [Fact]
    public void EveryPuzzleInTheTableIsARealLevel()
    {
        var ids = Table().Levels.Select(l => l.LevelId).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in AchievementChecks.All)
        {
            Assert.True(ids.Contains(entry.LevelId), $"{entry.LevelId} is not in levels.json");
        }
    }

    [Fact]
    public void TheTableIsInLevelIndexOrderWithOneEntryPerPuzzleAndAchievement()
    {
        var index = Table().Levels.ToDictionary(l => l.LevelId, l => l.LevelIndex, StringComparer.Ordinal);
        var order = AchievementChecks.All.Select(e => index[e.LevelId]).ToList();
        Assert.Equal(order.OrderBy(i => i).ToList(), order);

        var keys = AchievementChecks.All.Select(e => $"{e.LevelId}/{e.AchievementId}").ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AchievementsThatCannotBeTheirOwnCheckAreLeftOut()
    {
        // Sweep is Sharp Pencils' Shavings Removed and Breadtags' Crumbs, and
        // comes only once both puzzles are cleared; Path of Destruction's
        // checker watches exactly Paw Prints' Free Leaves and Coffee Spill;
        // Keep Away never fired in a run with its condition met (2026-09-28).
        foreach (var id in new[] { "UNIQUE_SWEEP", "UNIQUE_PATH_OF_DESTRUCTION", "UNIQUE_KEEP_AWAY" })
        {
            Assert.DoesNotContain(AchievementChecks.All, e => e.AchievementId == id);
        }
    }

    [Fact]
    public void TheNamesAreAsciiLocationNames()
    {
        foreach (var entry in AchievementChecks.All)
        {
            Assert.All(entry.Display, ch => Assert.InRange(ch, ' ', '~'));
            Assert.False(string.IsNullOrWhiteSpace(entry.AchievementId));
        }
        Assert.Equal("Eggs - Achievement: Exacting Eggs",
            LocationNames.Achievement("Eggs", 1, "Exacting Eggs"));
        Assert.Equal("Place Setting #2 - Achievement: Bad Kitty",
            LocationNames.Achievement("Place Setting", 2, "Bad Kitty"));
    }

    [Fact]
    public void AnAchievementIsFiledOnThePuzzleThatAwardsIt()
    {
        var router = new CheckRouter(Seed(achievements: true));
        Assert.Equal("Eggs - Achievement: Exacting Eggs",
            router.ForAchievement(0, "UNIQUE_EXACTING_EGGS"));
        Assert.Equal("Place Setting - Achievement: Bad Kitty",
            router.ForAchievement(1, "UNIQUE_BAD_KITTY"));
    }

    [Fact]
    public void AnAchievementFromAnotherPuzzleOrAHintIsNotACheck()
    {
        var router = new CheckRouter(Seed(achievements: true));
        Assert.Null(router.ForAchievement(0, "UNIQUE_BAD_KITTY"));
        Assert.Null(router.ForAchievement(0, "HINTS_USE_HINT"));
        Assert.Null(router.ForAchievement(0, null));
        Assert.Null(router.ForAchievement(9, "UNIQUE_EXACTING_EGGS"));
        // Breadtags' own, left out of the table.
        Assert.Null(router.ForAchievement(2, "UNIQUE_SWEEP"));
    }

    [Fact]
    public void ASeedWithoutTheOptionHasNone()
    {
        var router = new CheckRouter(Seed(achievements: false));
        Assert.Null(router.ForAchievement(0, "UNIQUE_EXACTING_EGGS"));
        Assert.Empty(router.ForAchievements(1));
    }

    [Fact]
    public void TheyAreOutsideTheStarAndTheSkip()
    {
        var router = new CheckRouter(Seed(achievements: true));
        Assert.Equal(new[] { "Place Setting - Achievement: Bad Kitty" },
            router.ForAchievements(1));
        Assert.DoesNotContain(router.ForSlot(1), n => n.Contains("Achievement:"));

        // Every star-counted check collected and the achievement not: starred.
        var collected = new HashSet<string>(router.ForSlot(0), StringComparer.Ordinal);
        Assert.False(router.HasWorkLeft(0, collected.Contains));
    }

    [Fact]
    public void TheirIdsComeAfterEveryOtherLocation()
    {
        var all = LocationNames.AllPossible(Table());
        var first = all.ToList().FindIndex(n => n.Contains(" - Achievement: "));
        Assert.True(first > all.ToList().IndexOf(LocationNames.Credits));
        Assert.All(all.Skip(first), n => Assert.Contains(" - Achievement: ", n));
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        // 7 base, 5 Cupboards and Drawers, 5 Seeing Stars; none of their
        // puzzles repeats, so each is minted once.
        Assert.Equal(17, all.Count(n => n.Contains(" - Achievement: ")));
    }
}
