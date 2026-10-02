namespace ALTTLArchipelago.Core;

/// <summary>
/// The game's achievements that can be a check in a run, with the puzzle each
/// belongs to. Only used when the yaml turns `achievements` on.
///
/// WHERE THE TABLE COMES FROM. DevTools `achievements`, 2026-09-28: every
/// achievement is awarded by a checker under Game Manager/SteamDataTracker,
/// and each level-specific checker holds the LevelInterface it watches (a
/// serialized reference, readable at the title). An achievement is here only
/// if it is tied to ONE puzzle a run can draw and is not the same event as a
/// check the run already has. Left out, and why:
///
///   campaign, chapter and DLC completion (15) - a run has no chapters and
///       never plays a campaign through;
///   Helpful Hints, One Clean Page, Let It Be, Seeing In A New Light, Triple
///       Threat - earned on any puzzle, so they name no place;
///   Be The Chaos - the credits, which open only once the goal is met;
///   In No Rush - a Cupboards and Drawers cat interlude, not a run puzzle;
///   Nine Lives - finishing Ghost Cat, which is its Beaten check;
///   Dead End Boss Gems - one of the Boss's endings, which is a Solution;
///   Whatcha Lookin' At? - Figurines' "Groupables (Achievement)" controller,
///       never seen solved in play, so notALocation (2026-10-02); the
///       achievement has not been seen awarded in a run either;
///   Sweep Them On The Floor - Sharp Pencils' Shavings Removed and Breadtags'
///       Crumbs, both part checks, and awarded only once BOTH puzzles are
///       cleared (its checker holds the two levels; droha cleared Breadtags'
///       crumbs alone in the 0.4.2 playtest and nothing fired);
///   Path of Destruction - its checker watches Free Leaf 01 to 05 and Coffee
///       Spill, the objects of Paw Prints' Free Leaves and Coffee Spill parts;
///   Keep Away - never awarded in a run: droha met it on Place Setting four
///       times on 2026-09-28 (the cat tracker's pullCount 1, its one grab),
///       the last with the achieved flag cleared, and nothing fired. Cause
///       not found; droha: "just disable it".
///
/// An achievement the player's Steam profile already holds reads as met in
/// the game, and a checker may skip it. So the mod clears every achievement's
/// in-memory "achieved" flag at each slot entry
/// (SteamAchievements.ForgetUnlocks); Steam itself is never written.
///
/// Display names are ASCII: the game's own use a typographic apostrophe, and
/// these are location names the apworld and the mod both spell.
/// </summary>
public static class AchievementChecks
{
    public sealed class Entry
    {
        public Entry(string levelId, string achievementId, string display)
        {
            LevelId = levelId;
            AchievementId = achievementId;
            Display = display;
        }

        /// <summary>The puzzle whose checker awards it (levels.json levelId).</summary>
        public string LevelId { get; }

        /// <summary>AchievementManager.Achievement's name, as SetAchievementMet carries it.</summary>
        public string AchievementId { get; }

        /// <summary>The achievement's name, ASCII.</summary>
        public string Display { get; }
    }

    /// <summary>In level-index order, which is the order the location ids take.</summary>
    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry("Cat Toys", "UNIQUE_FUN_FOR_HUMANS", "Fun for Humans Too"),
        new Entry("Eggs", "UNIQUE_EXACTING_EGGS", "Exacting Eggs"),
        new Entry("Place Setting", "UNIQUE_BAD_KITTY", "Bad Kitty"),
        new Entry("Junk Drawer", "UNIQUE_DRAW_RAINBOW", "Draw Me A Rainbow"),
        new Entry("Radial Dance Party", "UNIQUE_PERFECT_PET_CAT", "Harmonized Purr"),
        new Entry("TupperwareTower", "UNIQUE_TUPPERWARE_RAINBOW", "Rainbow To The Moon"),
        new Entry("TupperwareTower", "UNIQUE_STABLE_STACKER", "Unstable Stacker"),
        new Entry("DLC1 Fountain Pens", "DLC1_UNIQUE_PEN_CAPS", "Where Is My Cap?"),
        new Entry("DLC1 Lunch Tray", "DLC1_UNIQUE_BALANCED_MEAL", "A Balanced Meal"),
        new Entry("DLC1 Pantry", "DLC1_UNIQUE_CAN_DO_ALTITUDE", "Can Do Altitude"),
        new Entry("DLC1 Media Cabinet", "DLC1_UNIQUE_PLAYING_WITH_POWER", "Now You're Playing With Power"),
        new Entry("DLC1 Trophy Cabinet", "DLC1_UNIQUE_SHOW_OFF", "Show Off"),
        new Entry("DLC2 Water Glasses", "DLC2_UNIQUE_WATER", "I'll Take My Water Neat"),
        new Entry("DLC2 Pizza", "DLC2_UNIQUE_TOP_HEAVY", "Top Heavy Slice"),
        new Entry("DLC2 Sticky Drawer", "DLC2_UNIQUE_STICKY", "Sticky Wand"),
        new Entry("DLC2 Junk Drawer Transforming", "DLC2_UNIQUE_WRONG_END", "Grabbed the Wrong End"),
        new Entry("DLC2 Music Box", "DLC2_UNIQUE_KEEPING_COUNT", "Keeping Count"),
    };

    /// <summary>The achievements one puzzle can award, in table order.</summary>
    public static IReadOnlyList<Entry> For(string? levelId)
    {
        var found = new List<Entry>();
        if (string.IsNullOrEmpty(levelId)) return found;
        foreach (var entry in All)
        {
            if (entry.LevelId == levelId) found.Add(entry);
        }
        return found;
    }

    /// <summary>This puzzle's entry for an achievement id, or null.</summary>
    public static Entry? Find(string? levelId, string? achievementId)
    {
        if (string.IsNullOrEmpty(levelId) || string.IsNullOrEmpty(achievementId)) return null;
        foreach (var entry in All)
        {
            if (entry.LevelId == levelId && entry.AchievementId == achievementId) return entry;
        }
        return null;
    }
}
