using System.Text.RegularExpressions;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Shortens a controller's GameObject name into something worth reading in a
/// hint. "Blue Bottles Draggables" becomes "Blue Bottles"; "Containables -
/// Keys" becomes "Keys"; "ChalkMint Jigsaw" becomes "Chalk Mint".
///
/// The level authors suffixed most controllers with their component type,
/// which is noise to a player: they already know they are dragging things.
/// Stripping it turns a hint from
///
///     Medicine Cabinet - Blue Bottles Draggables
/// into
///     Medicine Cabinet - Blue Bottles
///
/// These strings end up in Archipelago location names, so like DisplayNames
/// they are FROZEN once shipped. Changing one breaks seeds in flight.
///
/// Tidying can collide - Record Player has two controllers that both reduce to
/// "Needle & Knobs" - so the whole level is tidied at once and any name that
/// would collide keeps its raw form. Correctness beats prettiness: two
/// locations sharing a name would merge two different checks.
/// </summary>
public static class PartNames
{
    // Longest first, so "DraggablesOrdered" is not half-eaten by "Draggables".
    private static readonly string[] TypeWords =
    {
        "DraggablesOrdered", "DraggablesStacked", "DraggablesJigsaw",
        "SymmetricalPlaceables", "ShuffleablesRelative",
        "Draggables", "Draggable", "Containables", "Shuffleables",
        "Stackables", "Removables", "Pluckables", "Rotateables",
        "Placeables", "Jigsaw", "Controller",
    };

    private static readonly Regex CamelBoundary =
        new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>
    /// Display names for every controller on a level, keyed by raw name.
    /// Computed per level because collision handling needs the whole set.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForLevel(LevelInfo level)
    {
        var raw = level.Controllers.Select(c => c.Name).Distinct(StringComparer.Ordinal).ToList();
        var tidied = raw.ToDictionary(n => n, Tidy, StringComparer.Ordinal);

        // Any tidied form claimed by more than one raw name is unusable; those
        // raw names keep their original text.
        var clashes = tidied.GroupBy(kv => kv.Value, StringComparer.Ordinal)
                            .Where(g => g.Count() > 1)
                            .SelectMany(g => g.Select(kv => kv.Key))
                            .ToHashSet(StringComparer.Ordinal);

        foreach (var n in clashes) tidied[n] = n;
        return tidied;
    }

    /// <summary>The tidy applied to one name, ignoring collisions.</summary>
    public static string Tidy(string name)
    {
        var s = name;
        foreach (var w in TypeWords)
        {
            // No leading \b on purpose: the type word is often glued straight
            // onto the name in camelCase ("CandleStateController"), where a
            // word boundary would never match. Longest-first ordering above is
            // what stops "Draggables" being half-eaten by "Draggable".
            s = Regex.Replace(s, @"[\s_-]*" + w + @"\b", "", RegexOptions.IgnoreCase);
        }
        s = CamelBoundary.Replace(s, " ");
        s = string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '_');
        // Never reduce a name to nothing: a controller called exactly
        // "Draggables" is still the thing the player has to solve.
        return s.Length == 0 ? name : s;
    }
}
