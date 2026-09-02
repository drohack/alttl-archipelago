using System.Text.RegularExpressions;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Turns the game's internal level ids into names a person can read.
///
/// This matters more than cosmetics. The game never shows a level name
/// anywhere - the level select is artwork and a chapter header, and
/// LevelInterface exposes no title - so these strings are the only words a
/// player will ever have for a puzzle. They appear in Archipelago location
/// names, in hints, and on the card label the mod adds.
///
/// WHICH MEANS THEY MUST NOT CHANGE. A location name is part of the
/// datapackage checksum, so editing a display name after release breaks every
/// seed in flight. DisplayNameTests pins all 111. Treat a failure there as
/// "you are about to break saves", not "update the expectation".
/// </summary>
public static class DisplayNames
{
    /// <summary>
    /// Seasonal event packs. The id prefixes the pack onto the level; a reader
    /// wants the level first and the pack as context, so it moves to the end.
    /// "NeatStreak" is the internal name for the pack shown as Drawer Chores.
    /// </summary>
    private static readonly (string Prefix, string Pack)[] Packs =
    {
        ("GoodTidings_", "Good Tidings"),
        ("TrickOrTidy_", "Trick or Tidy"),
        ("MerryMess_", "Merry Mess"),
        ("NeatStreak_", "Drawer Chores"),
        ("SomethingEggstra ", "Something Eggstra"),
        ("SnackPack ", "Snack Pack"),
    };

    /// <summary>Ids the camel-case splitter gets wrong.</summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        // Splitting on lower-to-upper gives "Jack OLanterns".
        ["JackOLanterns"] = "Jack O'Lanterns",
    };

    private static readonly Regex CamelBoundary = new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    public static string For(string levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId)) return "";

        foreach (var (prefix, pack) in Packs)
        {
            if (!levelId.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var stem = levelId.Substring(prefix.Length);
            stem = Overrides.TryGetValue(stem, out var o) ? o : Split(stem);
            // A pack level whose own name already has parentheses would end up
            // with two bracketed groups ("Cookies (Jigsaw) (Good Tidings)"), so
            // flatten the inner one.
            stem = stem.Replace("(", "").Replace(")", "");
            return $"{Collapse(stem)} ({pack})";
        }

        var name = Overrides.TryGetValue(levelId, out var ov) ? ov : Split(levelId);
        return Collapse(name);
    }

    private static string Split(string s) => CamelBoundary.Replace(s, " ");

    private static string Collapse(string s)
        => string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
