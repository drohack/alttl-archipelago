using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Cross-check the shipped table against the prefab survey.
///
/// THE TWO FILES ANSWER DIFFERENT QUESTIONS, so a difference between them is
/// not automatically a bug and this is deliberately not a "they must match"
/// test.
///
/// - levels.json comes from the DevTools levelsweep, which BOOTS a level and
///   reads level.objectControllers: the controllers that actually REGISTERED.
/// - controller-survey.tsv comes from the prefab walk, which reads
///   GetComponentsInChildren&lt;ObjectController&gt;(true) - note the true, so
///   it includes INACTIVE children. That is authored structure, not runtime
///   behaviour.
///
/// The survey is therefore a superset, for two quite different reasons:
///
/// 1. A PHASED controller, which is real but only registers once the player
///    has solved an earlier group. The sweep boots the level and looks once,
///    so it never sees these however long it waits.
/// 2. A PREFAB GHOST, which sits in the prefab and never registers at all.
///    MedicineCabinet/Cupboard is the documented example
///    (apworld/alttl/data/README.md).
///
/// Restoring a ghost would mint a location nobody can ever check; leaving a
/// phased one out loses a location and, worse, can hide an ABILITY the level
/// really needs. So the disagreements are pinned one by one: a new one fails
/// the build and has to be explained as one or the other before it goes in.
/// </summary>
public class SurveyCrossCheckTests
{
    /// <summary>
    /// Controllers the prefab has and the runtime table does not, as
    /// "name/type", by level. Measured 2026-09-08 against the shipped pair.
    ///
    /// If you are here because this test failed: do NOT just paste in the new
    /// value. Find out which of the two kinds it is. Play the level and watch
    /// the mod's CONTROLLER MISMATCH line - a phased controller shows up there
    /// when it registers, a ghost never does.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownGaps = new(StringComparer.Ordinal)
    {
        // GHOSTS. Each is in the prefab, appears in NO level's declared phase
        // list, and does not register at boot. The three levels that reveal
        // controllers progressively all declare what they reveal, and none of
        // these is named, so there is nothing left for them to be. A location
        // behind one could never be checked.
        ["Desktop Computer"] = new[] { "Hourglass/HourglassController" },
        ["MedicineCabinet"] = new[] { "Cupboard/Cupboard" },
        ["Record Player"] = new[] { "Record Player/RecordPlayer" },

        // SEED-VARYING, and the reason is in the game's code rather than in a
        // sample. Books_LevelRandomizer holds this controller in a field named
        // DraggablesForSymmetricSolutions, and the level draws its solutions
        // from a seven-value enum of which two - HEIGHT_SYMMETRIC and
        // WIDTH_SYMMETRIC - are symmetric. So it appears exactly when a
        // symmetric solution is rolled, and a location here would be
        // unearnable in any seed that rolls none.
        //
        // It was first noticed as "present in 6 of 8 sampled seeds", which was
        // the wrong way round: sampling is how you CHECK a rule, not how you
        // find one. Every other generator's controller fields are
        // unconditional and all of them are recorded.
        ["Books (Randomized)"] = new[] { "Draggables/Draggables" },

        // NON-PUZZLE. Radial Dance Party's pan handler. Its ten dances are in
        // the table now; this one is filtered before grouping either way.
        ["Radial Dance Party"] = new[] { "Manager/Pannables" },

        // THE PHASE DRIVER. `Nested Tupperware` is the component that owns
        // GetPhaseControllers() and advances the level from one phase to the
        // next. It is not a puzzle group and must never become a location.
        // Its six phases, `Food` included, are all in the table now.
        ["TupperwareNesting"] = new[] { "Nested Tupperware/TupperwareNesting" },

        // MECHANISM, not objectives. TupperwareTower is one puzzle - build the
        // tower - and these two StackableGrids are how it works: the base the
        // tower sits on, and the queue of blocks that fall for you to place.
        // Neither ever raises a solved event, so both were dead locations: the
        // card could never go gold and fill could have put progression on one.
        //
        // Evidence. droha completed the level and only `Tower` fired a check,
        // while holding Grids and actively dragging the falling blocks - so the
        // two that were unlocked and in use still did not solve. And in the
        // only other two levels that use StackableGrid it is the SOLE
        // controller, where it plainly is the puzzle.
        //
        // Grids is still REQUIRED here and is recorded as an extraAbility: the
        // falling blocks are dimmed without it, and a tower cannot be built
        // out of blocks you cannot pick up.
        ["TupperwareTower"] = new[]
        {
            "Falling Blocks/StackableGrid",
            "Foundation/StackableGrid",
        },
    };

    private sealed record SurveyRow(string LevelId, string Controller, string Type);

    private static List<SurveyRow> Survey()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "controller-survey.tsv");
        Assert.True(File.Exists(path), $"controller-survey.tsv missing at {path}");

        var lines = File.ReadAllLines(path);
        var head = lines[0].Split('\t');
        int level = Array.IndexOf(head, "levelId");
        int ctrl = Array.IndexOf(head, "controller");
        int type = Array.IndexOf(head, "controllerType");

        var rows = new List<SurveyRow>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            if (f.Length <= ctrl || f[ctrl].Length == 0) continue;
            rows.Add(new SurveyRow(f[level], f[ctrl], f[type]));
        }
        return rows;
    }

    private static Dictionary<string, List<SurveyRow>> SurveyByLevel() =>
        Survey().GroupBy(r => r.LevelId)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    private static LevelTable Table()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        return LevelTable.FromJson(File.ReadAllText(path));
    }

    [Fact]
    public void TheTableIsNeverAheadOfTheSurvey()
    {
        // The survey walks the prefab including inactive children, so anything
        // that can ever register is in it. A controller the table has and the
        // survey does not would mean one of the two files is stale, and which
        // one is not answerable from here.
        var survey = SurveyByLevel();

        foreach (var level in Table().Levels)
        {
            if (!survey.TryGetValue(level.LevelId, out var rows)) continue;
            var seen = rows.Select(r => r.Controller).ToHashSet(StringComparer.Ordinal);
            foreach (var c in level.Controllers)
            {
                Assert.True(seen.Contains(c.Name),
                    $"{level.LevelId}/{c.Name} is in the table but not the prefab survey; "
                    + "one of the two files is stale");
            }
        }
    }

    [Fact]
    public void TheTwoTablesDisagreeOnlyWhereWeHaveWrittenItDown()
    {
        var survey = SurveyByLevel();

        var gaps = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var level in Table().Levels)
        {
            if (!survey.TryGetValue(level.LevelId, out var rows)) continue;
            var have = level.Controllers.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var missing = rows
                .Where(r => !have.Contains(r.Controller))
                .Select(r => $"{r.Controller}/{r.Type}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0) gaps[level.LevelId] = missing;
        }

        Assert.Equal(
            KnownGaps.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            gaps.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        foreach (var pair in KnownGaps)
        {
            Assert.Equal(
                pair.Value.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                gaps[pair.Key].OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A lost location is a shame; a lost ABILITY is a
    /// seed that cannot be finished.
    ///
    /// If a level's later phase needs Grids and the table never saw the grid
    /// controller, the generator believes the level is finishable without
    /// Grids and is free to put progression behind it. That is exactly what
    /// happened on TupperwareNesting during the 0.3.1 playtest.
    ///
    /// The prefab sees every phase, so this is answerable statically and needs
    /// no play session: every ability the prefab implies must appear in the
    /// level's requirements, whether it got there from a registered controller
    /// or from an explicit extraAbilities entry.
    ///
    /// Note this asserts REQUIREMENTS (AbilitiesForLevel), not what the level
    /// teaches (AbilitiesTaughtBy). A phase-only controller makes the level
    /// need the ability; it does not make the level a place to learn it,
    /// because the mechanic-coverage guarantee has to be earnable from the
    /// opening state.
    /// </summary>
    [Fact]
    public void NoLevelNeedsAnAbilityItsRequirementsDoNotDeclare()
    {
        var survey = SurveyByLevel();

        foreach (var level in Table().Levels)
        {
            if (!survey.TryGetValue(level.LevelId, out var rows)) continue;

            var implied = rows
                .Where(r => Abilities.IsPuzzleController(new ControllerInfo { Type = r.Type }))
                .Select(r => Abilities.ForClass(r.Type))
                .Where(a => a != null && !Abilities.Baseline.Contains(a!))
                .Select(a => a!)
                .ToHashSet(StringComparer.Ordinal);

            var declared = ControllerGroups.AbilitiesForLevel(level);
            var undeclared = implied.Where(a => !declared.Contains(a))
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();

            Assert.True(undeclared.Length == 0,
                $"{level.LevelId} can need {string.Join(", ", undeclared)} but does not "
                + "require it; add it to extraAbilities in levels.json");
        }
    }
}
