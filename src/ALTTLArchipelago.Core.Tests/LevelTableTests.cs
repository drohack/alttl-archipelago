using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Assertions against the real shipped data table, not fixtures.
///
/// The table is regenerated from the game by the DevTools levelsweep command,
/// and a game update can silently change it. These tests are the tripwire:
/// a changed controller name is a changed location name, which breaks seeds
/// already in flight, so it should fail here loudly rather than quietly there.
///
/// The expected numbers come from docs/content-report.md.
/// </summary>
public class LevelTableTests
{
    private static LevelTable Table()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        Assert.True(File.Exists(path),
            $"levels.json missing at {path}. Regenerate with the DevTools levelsweep command.");
        return LevelTable.FromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// 173 = the base game's 111 plus 62 from the two DLCs, measured
    /// 2026-09-17 by a sweep taken with both installed.
    ///
    /// The 62 are 25 from Cupboards and Drawers and 37 from Seeing Stars.
    /// Neither DLC's cat interludes, boss credits or Star Finale are here:
    /// they carry no solutions, and the sweep drops anything with none.
    /// </summary>
    [Fact]
    public void TheTableCoversEveryInScopeLevel()
    {
        Assert.Equal(173, Table().Levels.Count);
    }

    [Fact]
    public void SourcesSplitAsTheContentReportSays()
    {
        var bySource = Table().Levels
            .GroupBy(l => l.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(69, bySource["base"]);
        Assert.Equal(26, bySource["archive"]);

        // 20, not 16: four DLC levels carry the game's own randomizer flag
        // (DLC1 Trophy Cabinet, DLC2 Water Glasses, Figurines and Bread
        // Crusts), so they are generators like any other and repeat with a
        // fresh seed. That is why generator is not simply the base sixteen.
        Assert.Equal(20, bySource["generator"]);

        // The DLC sources, which exist so a yaml can weight each DLC
        // separately. A randomizable DLC level is NOT here - its source says
        // generator - which is exactly why levels also carry a `dlc` field:
        // source answers "which pool", dlc answers "which DLC is required",
        // and for those four the answers differ.
        Assert.Equal(24, bySource["dlc1"]);
        Assert.Equal(34, bySource["dlc2"]);
    }

    /// <summary>
    /// 294 = the base game's 162, plus 32 from Cupboards and Drawers and 100
    /// from Seeing Stars. Seeing Stars alone carries more solutions than the
    /// whole base campaign, which is what makes it the DLC that matters most
    /// to a star goal.
    /// </summary>
    [Fact]
    public void TotalSolutionsMatchTheContentReport()
    {
        Assert.Equal(294, Table().Levels.Sum(l => l.SolutionCount));
    }

    /// <summary>
    /// Every level that needs a DLC says so, and no other level does.
    ///
    /// The one that would slip through without this is a randomizable DLC
    /// level: its source is "generator", so anything keying eligibility off
    /// source alone would put DLC2 Bread Crusts in a run belonging to a player
    /// who does not own Seeing Stars, and the level would not load.
    /// </summary>
    [Fact]
    public void EveryDlcLevelDeclaresWhichDlcItNeeds()
    {
        var levels = Table().Levels;

        Assert.All(levels.Where(l => l.LevelIndex >= 1100),
                   l => Assert.False(string.IsNullOrEmpty(l.Dlc),
                                     $"{l.LevelId} is DLC content with no dlc field"));
        Assert.All(levels.Where(l => l.LevelIndex < 1100),
                   l => Assert.True(string.IsNullOrEmpty(l.Dlc),
                                    $"{l.LevelId} is base content claiming dlc {l.Dlc}"));

        Assert.Equal(25, levels.Count(l => l.Dlc == "DLC1"));
        Assert.Equal(37, levels.Count(l => l.Dlc == "DLC2"));
    }

    /// <summary>
    /// Levels whose puzzle pieces are not ordinary registered ObjectControllers
    /// at boot.
    ///
    /// EMPTY NOW, AND THAT IS THE POINT. This set used to hold Radial Dance
    /// Party, which reported zero controllers and was therefore excused from
    /// the "every level registers something" rule. Two separate investigations
    /// concluded its ten rings simply were not controllers.
    ///
    /// They are. RadialDanceParty declares ten phases - Radial Pencils 0
    /// through Radial Chess 9 - and they are in the table. The level was never
    /// bespoke in the way this list assumed; the sweep just could not see past
    /// phase one, and an exception list let it not have to.
    ///
    /// Kept as an empty set rather than deleted so the test below still reads
    /// as a rule with a carve-out, and so a future level that genuinely needs
    /// one has somewhere to go - with the warning above about what happened
    /// last time attached.
    /// </summary>
    private static readonly HashSet<string> BespokeLevels = new();

    [Fact]
    public void EveryLevelHasSolutionsAndControllersExceptTheKnownBespokeOnes()
    {
        foreach (var l in Table().Levels)
        {
            Assert.True(l.SolutionCount >= 1, $"{l.LevelId} has no solutions");
            if (BespokeLevels.Contains(l.LevelId)) continue;
            // A level with no registered controller can never raise a solved
            // event, so a controller location there could never be checked.
            Assert.True(l.Controllers.Count >= 1, $"{l.LevelId} registered no controllers");
        }
    }

    [Fact]
    public void RadialDancePartyContributesAllTenOfItsDances()
    {
        var radial = Table().ById("Radial Dance Party")!;

        // Ten dances, two of which carry a Draggables alongside their
        // RadialDance, so twelve controller entries collapse to ten groups.
        Assert.Equal(10, ControllerGroups.For(radial).Count);
        Assert.Equal(1, radial.SolutionCount);
        Assert.Equal(11, LocationNames.ForInstance(radial, 1).Count);

        // The order the game declares, which is also the dependency chain.
        Assert.Equal(10, radial.Phases.Count);
        Assert.Equal("Radial Pencils 0", radial.Phases[0]);
        Assert.Equal("Radial Chess 9", radial.Phases[9]);

        // It teaches Rotating through the dances themselves now, rather than
        // through an extraAbilities override.
        Assert.Contains("Rotating", ControllerGroups.AbilitiesForLevel(radial));
        Assert.Empty(radial.ExtraAbilities);
    }

    [Fact]
    public void LevelIdsAndIndicesAreUnique()
    {
        var t = Table();
        Assert.Equal(t.Levels.Count, t.Levels.Select(l => l.LevelId).Distinct().Count());
        Assert.Equal(t.Levels.Count, t.Levels.Select(l => l.LevelIndex).Distinct().Count());
    }

    [Fact]
    public void EveryControllerTypeIsKnownOrHarmless()
    {
        // An unknown type is not a failure - it stays playable by design - but
        // it does mean an ability grouping was missed, so surface it.
        var unknown = Table().Levels
            .SelectMany(l => l.Controllers)
            .Select(c => c.Type)
            .Distinct()
            .Where(t => Abilities.ForClass(t) == null
                        && !Abilities.Baseline.Contains(t)
                        && !Abilities.NotPuzzles.Contains(t))
            .OrderBy(t => t)
            .ToList();

        Assert.True(unknown.Count == 0,
            "Controller types with no ability grouping: " + string.Join(", ", unknown));
    }

    [Fact]
    public void EveryAbilityGatesAtLeastOneLevel()
    {
        var t = Table();
        // AllWithDlc, not All: a DLC level reports the DLC's own abilities, so
        // keying on the base twelve alone threw KeyNotFoundException the
        // moment Distributing appeared. An ability that gates nothing is a
        // dead item whichever list it came from, so the check widens rather
        // than filters.
        var gated = Abilities.AllWithDlc.ToDictionary(a => a, _ => 0);

        foreach (var l in t.Levels)
        {
            foreach (var a in ControllerGroups.AbilitiesForLevel(l)) gated[a]++;
        }

        var dead = gated.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        Assert.True(dead.Count == 0, "Abilities gating nothing: " + string.Join(", ", dead));
    }

    [Fact]
    public void EveryDependencyNamesAControllerOnTheSameLevel()
    {
        foreach (var l in Table().Levels)
        {
            var names = l.Controllers.Select(c => c.Name).ToHashSet();
            foreach (var c in l.Controllers)
            {
                foreach (var d in c.DependsOn)
                {
                    Assert.True(names.Contains(d),
                        $"{l.LevelId}: {c.Name} depends on {d}, which is not on this level");
                }
            }
        }
    }

    [Fact]
    public void KnownLevelsHaveTheirKnownShape()
    {
        var t = Table();

        // Verified by hand-solving during the Phase 0 gate.
        Assert.Equal(13, t.ById("MedicineCabinet")!.Controllers.Count);

        // A mutual pair collapses to one group, so a generator with two
        // Shuffleables must not mint two independent controller checks.
        var spice = t.ById("Spice Jars")!;
        Assert.Single(ControllerGroups.For(spice));
        Assert.Equal(2, spice.SolutionCount);

        // The richest partial-progress level in the base game.
        Assert.True(ControllerGroups.For(t.ById("MedicineCabinet")!).Count > 5);
    }
}
