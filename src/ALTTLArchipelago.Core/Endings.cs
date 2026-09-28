using System.Globalization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// One ending of a level as a check: the game's solution id it answers to
/// (null while nobody has seen it), the location's suffix after the level's
/// name, and the controller group the ending is made of, if exactly one.
/// </summary>
public sealed class EndingInfo
{
    public EndingInfo(string? id, string suffix, string? group)
    {
        Id = id;
        Suffix = suffix;
        Group = group;
    }

    /// <summary>LevelComplete's solutionId for this ending, or null (unseen).</summary>
    public string? Id { get; }

    /// <summary>"Solution", "Solution: Stacked", "Solution 2", "Solution: Other 1".</summary>
    public string Suffix { get; }

    /// <summary>The ControllerGroup.Name the ending is that group solved, or null.</summary>
    public string? Group { get; }
}

/// <summary>
/// Fixed endings (droha, 2026-09-28: "yes we want fixed ending names"). Every
/// ending of a level is its own check, named for what it is, and the same
/// ending always files the same location - no longer "the Nth solution you
/// happened to find".
///
/// levels.json "endings" lists a level's solution ids, measured: the ids seen
/// in real play, extended by each controller's arrangement count (DevTools
/// `solutions`), null for an ending nobody has seen yet. An id is the
/// controller that finished the level and the arrangement it matched
/// ("Ordered_1"), so an ending made of one controller group is named by that
/// group, and that group's part check is dropped: one ending, one location.
///
/// GENERATED PUZZLES TOO (droha: "don't generated puzzles still have fixed
/// solutions?"). Their ids are the same on every seed, by position: Pencils
/// (Randomized) reports Ordered_0 and Ordered_1 whichever two sorting rules
/// the seed picked, Stamps (Randomized) Stamps-Pattern_0 on seven seeds. The
/// Draggables_0 once seen on Books (Randomized) was a stray controller that
/// solved itself at load (docs/dev/backlog.md), not an ending. A level with
/// no table falls back to numbered solutions in the order found.
/// </summary>
public static class Endings
{
    public static IReadOnlyList<EndingInfo> For(LevelInfo level)
    {
        var endings = new List<EndingInfo>();
        if (level.Endings == null || level.Endings.Count == 0)
        {
            for (int n = 1; n <= level.SolutionCount; n++)
                endings.Add(new EndingInfo(null, Numbered(n), null));
            return endings;
        }

        var groups = ControllerGroups.For(level);
        var memberOf = new Dictionary<string, ControllerGroup>(StringComparer.Ordinal);
        foreach (var g in groups)
            foreach (var m in g.Members) memberOf[m] = g;

        string? GroupOf(string? id)
        {
            if (id == null) return null;
            var named = SolutionParts.ControllersFor(Primary(id), memberOf.Keys);
            var owners = named.Select(n => memberOf[n].Name).Distinct(StringComparer.Ordinal).ToList();
            return owners.Count == 1 ? owners[0] : null;
        }

        // ONE ENDING: finishing the level needs everything, and the id merely
        // names whichever controller the game passed (Radial Dance Party's
        // "Radial-Pencils-0_0" names its first dance). No group is the ending.
        if (level.Endings.Count == 1)
        {
            endings.Add(new EndingInfo(level.Endings[0], "Solution", null));
            return endings;
        }

        // A name per ending: its group's display when it is one group solved,
        // else the controller named in the id, tidied. Numbered where one name
        // covers several endings ("Ordered 1", "Ordered 2").
        var bases = level.Endings.Select(id =>
        {
            if (id == null) return null;
            var group = GroupOf(id);
            if (group != null)
            {
                var g = groups.First(x => x.Name == group);
                if (g.Members.Count == 1) return g.DisplayName;
            }
            return Readable(Prefix(Primary(id)));
        }).ToList();

        var others = 0;
        for (int i = 0; i < level.Endings.Count; i++)
        {
            var id = level.Endings[i];
            if (id == null)
            {
                others++;
                endings.Add(new EndingInfo(null, $"Solution: Other {others.ToString(CultureInfo.InvariantCulture)}", null));
                continue;
            }
            var name = bases[i]!;
            if (bases.Count(b => b == name) > 1)
                name = $"{name} {IndexOf(Primary(id), i).ToString(CultureInfo.InvariantCulture)}";
            endings.Add(new EndingInfo(id, $"Solution: {name}", GroupOf(id)));
        }
        return endings;
    }

    /// <summary>
    /// The ControllerGroup names that are an ending of their own: no part
    /// check. Only on a level with several endings, where one group solved is
    /// one way to finish it (Spoons: Stacked or Size).
    /// </summary>
    public static IReadOnlySet<string> EndingGroups(LevelInfo level)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in For(level))
            if (e.Group != null) set.Add(e.Group);
        return set;
    }

    public static string Numbered(int n)
        => $"Solution {n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The ids one table entry answers to: "Shuffle_1|Draggables_0" is either.
    /// Books (Randomized) checks its second solution with a second controller
    /// on symmetric seeds (Draggables, SolutionId 1) and with Shuffle on the
    /// rest - measured over 40 seeds with DevTools `gensweep:40:995`.
    /// </summary>
    public static IReadOnlyList<string> Alternatives(string? id)
        => id == null ? Array.Empty<string>() : id.Split('|');

    /// <summary>The id an entry is named by: its first alternative.</summary>
    private static string Primary(string id) => Alternatives(id)[0];

    /// <summary>"Ordered_1" -> 2, a 1-based number for a name; a custom id counts by position.</summary>
    private static int IndexOf(string id, int position)
    {
        var cut = id.LastIndexOf('_');
        return cut > 0 && int.TryParse(id.Substring(cut + 1), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n + 1
            : position + 1;
    }

    /// <summary>The controller part of an id: "Size-(Elastic)_0" -> "Size-(Elastic)".</summary>
    private static string Prefix(string id)
    {
        var cut = id.LastIndexOf('_');
        if (cut <= 0) return id;
        var tail = id.Substring(cut + 1);
        // A custom id with no number ("DLC2Boss_Knife") is named by its tail.
        return int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? id.Substring(0, cut)
            : tail;
    }

    /// <summary>An id's controller part as words: dashes were spaces, then PartNames' tidy.</summary>
    private static string Readable(string prefix)
    {
        // "Shuffle---Top-Left" was "Shuffle - Top Left": the spaced dash first.
        var words = string.Join(" - ", prefix.Split(new[] { "---" }, StringSplitOptions.None)
            .Select(part => part.Replace('-', ' ')));
        var tidy = PartNames.Tidy(words);
        return tidy == words ? SpaceCamel(words) : tidy;
    }

    private static string SpaceCamel(string text)
    {
        var chars = new List<char>();
        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) chars.Add(' ');
            chars.Add(text[i]);
        }
        return new string(chars.ToArray());
    }
}
