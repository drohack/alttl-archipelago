namespace ALTTLArchipelago.Core;

/// <summary>
/// The names Archipelago items arrive under.
///
/// These are matched as literal strings against what the server sends, so a
/// typo does not fail - it silently does nothing. The mod spent a build
/// matching "Puzzle Pack" against an item actually called "Progressive Puzzle
/// Pack": packs arrived, the log said so, and not one puzzle ever unlocked.
///
/// They are exported to names.json and pinned by a test on the Python side, so
/// the two languages cannot drift apart the way that build did.
/// </summary>
public static class ItemNames
{
    /// <summary>Reveals the next block of puzzles. Progressive: the Nth is the Nth.</summary>
    public const string Pack = "Progressive Puzzle Pack";

    /// <summary>Puts the credits card on the track.</summary>
    public const string Credits = "Credits";

    /// <summary>One use of the game's skip.</summary>
    public const string Skip = "Skip";

    /// <summary>Scatters the puzzle in progress.</summary>
    public const string CatTrap = "Cat Trap";

    /// <summary>
    /// Granted by an event location for beating a puzzle. It is an item the
    /// client receives, not an ability - counting it as one would put a
    /// nonsense entry in the held set on every level completed.
    /// </summary>
    public const string BeatenToken = "Level Beaten";

    /// <summary>
    /// True for the items that are not abilities. Anything else the server
    /// sends is treated as an ability name.
    /// </summary>
    public static bool IsSpecial(string name)
        => name == Pack || name == Credits || name == Skip
           || name == CatTrap || name == BeatenToken;
}
