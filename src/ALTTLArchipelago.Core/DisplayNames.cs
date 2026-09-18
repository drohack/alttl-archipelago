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
    /// Seasonal event packs and the two DLCs. The id prefixes the source onto
    /// the level; a reader wants the level first and the source as context, so
    /// it moves to the end. "NeatStreak" is the internal name for the pack
    /// shown as Drawer Chores.
    ///
    /// The DLCs go through the same path for the same reason. Their ids read
    /// "DLC1 Trophy Cabinet", which is a developer's label, not a name a
    /// player has for a puzzle - and these strings are the ONLY words a player
    /// ever gets for one, because the game itself never shows a level name.
    /// Routed here they come out as "Trophy Cabinet (Cupboards and Drawers)",
    /// which matches every event level already shipped.
    /// </summary>
    private static readonly (string Prefix, string Pack)[] Packs =
    {
        ("GoodTidings_", "Good Tidings"),
        ("TrickOrTidy_", "Trick or Tidy"),
        ("MerryMess_", "Merry Mess"),
        ("NeatStreak_", "Drawer Chores"),
        ("SomethingEggstra ", "Something Eggstra"),
        ("SnackPack ", "Snack Pack"),
        ("DLC1 ", "Cupboards and Drawers"),
        ("DLC2 ", "Seeing Stars"),
    };

    /// <summary>Ids the camel-case splitter gets wrong.</summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        // Splitting on lower-to-upper gives "Jack OLanterns".
        ["JackOLanterns"] = "Jack O'Lanterns",
    };

    private static readonly Regex CamelBoundary = new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>
    /// Answers already worked out. The result is a pure function of the level
    /// id and there are fewer than two hundred of those in the whole game.
    ///
    /// Worth caching because of who calls it: the tracker badge rebuilt every
    /// card's status once a second, and each card cost several of these, so a
    /// regex replace, a split and a join ran a few hundred times a second to
    /// produce strings that had not changed since the seed was generated.
    ///
    /// Concurrent because slot_data is parsed off the main thread while the
    /// game keeps rendering.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>
        Cache = new(StringComparer.Ordinal);

    public static string For(string levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId)) return "";
        if (Cache.TryGetValue(levelId, out var cached)) return cached;

        var built = Build(levelId);
        Cache[levelId] = built;
        return built;
    }

    private static string Build(string levelId)
    {

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
