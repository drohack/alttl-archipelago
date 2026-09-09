namespace ALTTLArchipelago.Core;

/// <summary>
/// What a list of received item names adds up to.
///
/// Items are applied by COUNTING, not by reacting. Archipelago replays the
/// whole item list on every connect, so "a pack arrived, open four more
/// puzzles" would double the track the second time you log in. Counting from
/// scratch makes a replay a no-op instead of a corruption.
///
/// The counting is here rather than in the plugin because it is pure: a list of
/// strings and an ability catalogue in, six numbers out. It used to live beside
/// the Unity code and had no tests, and that is where the reconnect doubling
/// hid - the counting was always right, and the LIST it was given was wrong.
/// A test can now state both halves.
/// </summary>
public sealed class InventoryCounts
{
    public int Packs { get; }
    public int Skips { get; }
    public int Traps { get; }
    public int Beaten { get; }

    /// <summary>
    /// Hint Pages received. One opens one page of one puzzle's notepad, and a
    /// level can hold up to five pages, so this is a running total the gate
    /// spends against - not a per-level flag.
    /// </summary>
    public int HintPages { get; }

    /// <summary>
    /// Background recolour items received, counted rather than reacted to.
    ///
    /// The count IS the state: the colour shown is
    /// catalogue[count % catalogue.Count], so it is a pure function of how
    /// many have arrived. That is what makes a reconnect harmless - the server
    /// replays the whole list, and a handler that stepped the palette forward
    /// on each arrival would walk it forward again on every login, landing the
    /// player on a different colour every time they started the game.
    /// </summary>
    /// <summary>Background Change Traps received. One counter, one item.</summary>
    public int BackgroundTraps { get; }

    public bool HasCredits { get; }

    /// <summary>
    /// Ability items received, in arrival order. NOT the seed's starting
    /// abilities - those are held separately, because the server never resends
    /// them and folding them in here would lose them on the next recount.
    /// </summary>
    public IReadOnlyList<string> Abilities { get; }

    private InventoryCounts(
        int packs, int skips, int traps, int beaten, int hintPages,
        int backgrounds,
        bool credits, IReadOnlyList<string> abilities)
    {
        BackgroundTraps = backgrounds;
        Packs = packs;
        Skips = skips;
        Traps = traps;
        Beaten = beaten;
        HintPages = hintPages;
        HasCredits = credits;
        Abilities = abilities;
    }

    /// <summary>
    /// Count a received-item list.
    ///
    /// <paramref name="isAbility"/> is asked rather than a second name table
    /// being kept here, so there is nothing to fall out of step with the
    /// generator. Anything matching neither a known item nor an ability is
    /// unrecognised and changes nothing - which used to be where the whole
    /// filler pool landed, back when filler was three names with no code.
    /// </summary>
    public static InventoryCounts From(
        IEnumerable<string> received, Func<string, bool>? isAbility = null)
    {
        int packs = 0, skips = 0, traps = 0, beaten = 0, hintPages = 0;
        int backgrounds = 0;
        var credits = false;
        var abilities = new List<string>();

        foreach (var name in received)
        {
            if (string.IsNullOrEmpty(name)) continue;

            // Exact matches against ItemNames, which is pinned against the
            // generator's own table. Prefix matching is what let "Puzzle Pack"
            // look plausible while never matching "Progressive Puzzle Pack".
            if (name == ItemNames.Pack) packs++;
            else if (name == ItemNames.Credits) credits = true;
            else if (name == ItemNames.Skip) skips++;
            else if (name == ItemNames.CatTrap) traps++;
            else if (name == ItemNames.BeatenToken) beaten++;
            else if (name == ItemNames.HintPage) hintPages++;
            else if (name == ItemNames.BackgroundTrap) backgrounds++;
            else if (isAbility != null && isAbility(name)) abilities.Add(name);
        }

        return new InventoryCounts(
            packs, skips, traps, beaten, hintPages,
            backgrounds, credits, abilities);
    }
}
