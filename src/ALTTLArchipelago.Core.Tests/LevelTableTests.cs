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

    [Fact]
    public void TheTableCoversEveryInScopeLevel()
    {
        Assert.Equal(111, Table().Levels.Count);
    }

    [Fact]
    public void SourcesSplitAsTheContentReportSays()
    {
        var bySource = Table().Levels
            .GroupBy(l => l.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(69, bySource["base"]);
        Assert.Equal(16, bySource["generator"]);
        Assert.Equal(26, bySource["archive"]);
    }

    [Fact]
    public void TotalSolutionsMatchTheContentReport()
    {
        Assert.Equal(162, Table().Levels.Sum(l => l.SolutionCount));
    }

    /// <summary>
    /// Levels built as bespoke Level subclasses, whose puzzle pieces are not
    /// ordinary registered ObjectControllers. Radial Dance Party
    /// (RadialDanceParty) registers none of its ten rings; TupperwareNesting
    /// registers 2 of the 9 groups the prefab shows.
    ///
    /// This is not a sweep bug - it reproduces through the normal gameplay
    /// path, with transitions, from Gameplay_GameState. It is simply how those
    /// levels are built.
    ///
    /// The consequence is benign and self-correcting: they contribute solution
    /// checks and few or no controller checks, and because nothing there is
    /// ability-gated they stay fully playable. Listed explicitly so a NEW
    /// zero-controller level still fails the test below.
    /// </summary>
    private static readonly HashSet<string> BespokeLevels = new() { "Radial Dance Party" };

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
    public void ABespokeLevelStillContributesItsSolutionChecks()
    {
        var radial = Table().ById("Radial Dance Party")!;

        Assert.Empty(radial.Controllers);
        // No controller groups means no controller locations, but the solution
        // location must still exist or the slot would be unreachable.
        Assert.Empty(ControllerGroups.For(radial));
        Assert.Single(LocationNames.ForSlot(0, radial));
        // And nothing is gated, so it is always playable.
        Assert.Empty(ControllerGroups.AbilitiesForLevel(radial));
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
        var gated = Abilities.All.ToDictionary(a => a, _ => 0);

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
