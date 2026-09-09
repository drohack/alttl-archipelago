using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// How many of the pool's levels are dailies, pinned as data.
///
/// THIS TEST EXISTS BECAUSE A NUMBER IN A COMMENT WAS WRONG FOR MONTHS.
///
/// DailyGuard was written against "six of the levels a seed can draw" - the
/// "(Randomized)" ones. The real figure is 36, a third of the pool. The six is
/// the count of levels that exist NOWHERE ELSE, which is a different question
/// and was never the one the guard needed answered. A guard sized for six was
/// shipped, and droha was routed out of the run onto the Daily Tidy page three
/// separate times before anybody counted.
///
/// What makes it worth a test rather than a better comment: the correct number
/// was ALREADY WRITTEN DOWN. docs/content-report.md says, in bold, "The daily
/// pool is not 6 levels - it is 36", with the three sources broken out. The
/// project knew. Nothing connected knowing to the code, so the code kept the
/// wrong number and no build ever objected. Prose cannot fail; a test can.
///
/// The same shape of error has bitten this project repeatedly - how many
/// levels are randomizable, how many have hints, which abilities gate a
/// partial completion. The response is the same each time: get the fact from
/// the game, put it in levels.json, and pin it here.
/// </summary>
public class DailyPoolTests
{
    private static LevelTable Table()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        return LevelTable.FromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// The three counts, from docs/content-report.md and confirmed against the
    /// game on 2026-09-08 by three independent measurements that agreed:
    ///
    /// - the levelsweep's per-level IsDailyTidy flag: 16
    /// - DailyTidyManager.GetDailyTidyLevels(true): the same 16
    /// - dailyDateCount > 0, which marks a level pinned to calendar dates: 20
    ///
    /// The third is the one to reach for when re-measuring. GetDailyTidyLevels
    /// reports what is in rotation TODAY, so it answers a different question
    /// depending on the date and on which save is loaded - it returned 36 once
    /// and 16 an hour later, which is exactly the kind of unstable reading that
    /// produces a confidently wrong constant. dailyDateCount does not move.
    /// </summary>
    [Fact]
    public void TheDailyPoolIsThirtySixLevelsInTwoKinds()
    {
        var levels = Table().Levels;

        var everyday = levels.Count(l => l.IsDailyTidy);
        var holiday = levels.Count(l => l.IsHolidayDaily);
        var both = levels.Count(l => l.IsDailyTidy && l.IsHolidayDaily);
        var pool = levels.Count(l => l.InDailyPool);

        Assert.Equal(16, everyday);
        Assert.Equal(20, holiday);
        Assert.Equal(0, both);      // the game keeps the two kinds disjoint
        Assert.Equal(36, pool);

        // A third of the run, which is the fact that makes this a common case
        // rather than an edge one.
        Assert.Equal(111, levels.Count);
    }

    /// <summary>
    /// The four seasonal packs account for every holiday daily.
    ///
    /// Named rather than counted, because "20" alone would still pass if the
    /// twenty were the wrong twenty. This is the check that a regenerated table
    /// put the flag on the same levels and not merely on the same number.
    /// </summary>
    [Fact]
    public void EveryHolidayDailyBelongsToASeasonalPack()
    {
        string[] packs = { "MerryMess_", "TrickOrTidy_", "GoodTidings_", "SomethingEggstra" };

        foreach (var level in Table().Levels.Where(l => l.IsHolidayDaily))
        {
            Assert.True(packs.Any(p => level.LevelId.StartsWith(p, StringComparison.Ordinal)),
                $"{level.LevelId} is flagged a holiday daily but is not in a seasonal pack; "
                + "either the flag is wrong or there is a fifth pack to account for");
        }
    }

    /// <summary>
    /// SpiderWeb specifically, because it is the one that caught us out.
    ///
    /// It reads as an ordinary campaign puzzle and it is in the everyday daily
    /// rotation, so finishing it in a run routed the player to the Daily page.
    /// It is not a "(Randomized)" level and would never have been caught by the
    /// reasoning that produced the six.
    /// </summary>
    [Fact]
    public void SpiderWebIsADailyDespiteLookingLikeAnOrdinaryPuzzle()
    {
        var spider = Table().ById("SpiderWeb");
        Assert.NotNull(spider);
        Assert.True(spider!.IsDailyTidy);
        Assert.False(spider.IsHolidayDaily);
        Assert.True(spider.InDailyPool);
    }

    /// <summary>
    /// Ten of the dailies are ordinary campaign puzzles, not generators.
    ///
    /// content-report.md breaks the 16 down as 10 campaign puzzles carrying a
    /// randomizer plus 6 daily-exclusive generators. Pinned because that split
    /// is the reason the "six" was believable: six IS a real count, of the
    /// daily-exclusive ones, and it is simply not the count that matters here.
    /// </summary>
    [Fact]
    public void SixteenEverydayDailiesSplitTenCampaignAndSixExclusive()
    {
        var everyday = Table().Levels.Where(l => l.IsDailyTidy).ToList();

        // The daily-exclusive generators are the ones that exist nowhere else,
        // which in the table means they carry a randomizer AND are the only
        // appearance of that puzzle: "(Randomized)" plus Procedural Grid Puzzle.
        var exclusive = everyday.Count(l =>
            l.LevelId.EndsWith("(Randomized)", StringComparison.Ordinal)
            || l.LevelId == "Procedural Grid Puzzle");

        Assert.Equal(6, exclusive);
        Assert.Equal(10, everyday.Count - exclusive);
    }
}
