using System.Globalization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Location naming, shared by the apworld and the mod.
///
/// Names are CONTENT-based - the level and the part, not the slot:
///
///     Cookies Jigsaw (Good Tidings) - Match Reindeer
///     Medicine Cabinet - Blue Bottles Draggables
///     Books (Randomized) #2 - Solution 2
///
/// The hard constraint is that Archipelago's location_name_to_id is a ClassVar
/// folded into the datapackage checksum, so the whole name set must be
/// identical for every seed. That rules out anything positional
/// ("Slot 07 - ...") because slot 7 holds a different puzzle in every seed. It
/// does NOT rule out content, because the set of levels is fixed - only which
/// slot each lands in varies.
///
/// A generator can occupy several slots in one run with different seeds, and
/// those are genuinely different puzzles, so repeats are numbered: the first
/// instance is unsuffixed, later ones get " #2", " #3". That caps how often a
/// generator may repeat - see MaxGeneratorInstances.
///
/// The game shows no level name anywhere, so the mod adds a label under each
/// card using the same DisplayNames strings. Without that these names would be
/// unmatchable to anything on screen.
/// </summary>
public static class LocationNames
{
    /// <summary>
    /// How many times one generator may appear in a run. The static name table
    /// has to cover every instance up front, so this is a hard cap the slot
    /// draw must respect, not a preference.
    /// </summary>
    public const int MaxGeneratorInstances = 8;

    /// <summary>The finale. Always drawn last, whenever its unlock arrives.</summary>
    public const string Credits = "Credits";

    /// <summary>"Books (Randomized)" for the first, "Books (Randomized) #2" after.</summary>
    public static string Instance(string levelId, int instance)
    {
        var name = DisplayNames.For(levelId);
        return instance <= 1
            ? name
            : $"{name} #{instance.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string Solution(string levelId, int instance, int solutionNumber)
        => $"{Instance(levelId, instance)} - Solution {solutionNumber.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// One controller group, named by the group itself. Readable in a hint and
    /// self-evident once the level is open - "Match Reindeer" tells you what to
    /// do in a way that "Part 3" never could.
    /// </summary>
    public static string Part(string levelId, int instance, string groupName)
        => $"{Instance(levelId, instance)} - {groupName}";

    /// <summary>
    /// The event location granting one "Level Beaten" token. Zero-cost event
    /// locations rather than pool items, so the "beat N levels" gate costs no
    /// item slots and still forces the fill to spread progression.
    /// </summary>
    public static string Beaten(string levelId, int instance)
        => $"{Instance(levelId, instance)} - Beaten";

    /// <summary>
    /// One of the game's achievements, as a check on the puzzle that awards it
    /// (AchievementChecks). Only a seed generated with `achievements` on has
    /// these; the name says which kind of check it is, since an achievement's
    /// own name ("Show Off") could pass for a part.
    /// </summary>
    public static string Achievement(string levelId, int instance, string display)
        => $"{Instance(levelId, instance)} - Achievement: {display}";

    /// <summary>
    /// The locations one instance of a level contributes.
    ///
    /// One per distinct solution, always. Plus one per controller group, but
    /// ONLY when the level has more than one - on a single-group level the
    /// group check and the first solution check are the same event, and
    /// minting both would double count.
    /// </summary>
    public static IReadOnlyList<string> ForInstance(LevelInfo level, int instance)
    {
        var names = new List<string>();

        // One per ending (Endings): "Spoons - Solution: Stacked", or numbered
        // on a generated puzzle.
        foreach (var ending in Endings.For(level))
        {
            names.Add(Ending(level.LevelId, instance, ending.Suffix));
        }

        // A group that is an ending of its own is not a part as well: solving
        // it IS that ending (droha, 2026-09-28: "No reason to give an ending 2
        // locations"). Nor is a group done only as part of the Solution
        // (LevelInfo.SolutionOnlyParts) - it still counts as a group here, so
        // Mirror keeps its "Still Life" check.
        var groups = ControllerGroups.For(level);
        if (groups.Count > 1)
        {
            var endingGroups = Endings.EndingGroups(level);
            foreach (var g in groups)
            {
                if (endingGroups.Contains(g.Name) || level.SolutionOnlyParts.Contains(g.DisplayName)) continue;
                names.Add(Part(level.LevelId, instance, g.DisplayName));
            }
        }

        return names;
    }

    /// <summary>An ending's location: the level's instance name and the ending's suffix.</summary>
    public static string Ending(string levelId, int instance, string suffix)
        => $"{Instance(levelId, instance)} - {suffix}";

    /// <summary>
    /// Every location name the game can ever have, in a stable order. This is
    /// the datapackage: it must not depend on options or on the seed. A level
    /// that can only appear once contributes one instance; a repeatable
    /// generator (LevelInfo.Repeatable) contributes MaxGeneratorInstances. Names for instances a given seed
    /// never uses simply become no locations.
    /// </summary>
    public static IReadOnlyList<string> AllPossible(LevelTable table)
    {
        var names = new List<string>();
        foreach (var level in table.Levels.OrderBy(l => l.LevelIndex))
        {
            int instances = level.Repeatable ? MaxGeneratorInstances : 1;
            for (int i = 1; i <= instances; i++)
            {
                names.AddRange(ForInstance(level, i));
                names.Add(Beaten(level.LevelId, i));
            }
        }
        names.Add(Credits);

        // Appended after everything else, so turning them on moved no id.
        foreach (var level in table.Levels.OrderBy(l => l.LevelIndex))
        {
            var awarded = AchievementChecks.For(level.LevelId);
            if (awarded.Count == 0) continue;
            int instances = level.Repeatable ? MaxGeneratorInstances : 1;
            for (int i = 1; i <= instances; i++)
            {
                foreach (var entry in awarded) names.Add(Achievement(level.LevelId, i, entry.Display));
            }
        }
        return names;
    }
}
