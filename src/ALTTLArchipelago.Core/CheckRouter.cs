namespace ALTTLArchipelago.Core;

/// <summary>
/// Turns something that happened in a level into the Archipelago location it
/// is, or into nothing at all.
///
/// "Or into nothing at all" is most of the job. The game raises far more
/// events than a seed has locations:
///
/// - a single-group level mints no part location, because its group check and
///   its first solution check are the same event and minting both would double
///   count;
/// - a level can hold controllers the seed never turned into a check;
/// - the same level can occupy several slots, and only the slot the player is
///   actually in decides which instance's location was earned.
///
/// So every answer is validated against the seed's own requirements table,
/// which lists every location this seed contains. A name that is not in there
/// is not a location, and inventing one would send the server a check it has
/// never heard of.
/// </summary>
public sealed class CheckRouter
{
    private readonly SlotData _slot;

    /// <summary>
    /// Locations that exist only in the generator's logic, never on the server.
    ///
    /// The "Beaten" locations are Archipelago EVENT locations - address None -
    /// carrying a Level Beaten token that the fill uses to spread progression.
    /// The server has never heard of them, so sending one is rejected, and the
    /// token is never delivered to the client either. Both facts matter: the
    /// mod must not try to send them, and it must count beaten puzzles from
    /// what it has collected rather than from items it will never receive.
    /// </summary>
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);

    public CheckRouter(SlotData slot)
    {
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));

        foreach (var entry in _slot.Slots)
        {
            var beaten = LocationNames.Beaten(entry.LevelId, entry.Instance);
            if (_slot.Requirements.ContainsKey(beaten)) _events.Add(beaten);
        }
    }

    /// <summary>
    /// True for a location that lives only in logic. It still counts as
    /// collected - it is how "beat N puzzles" is measured - but it is never
    /// sent anywhere.
    /// </summary>
    public bool IsLocalEvent(string? name)
        => !string.IsNullOrEmpty(name) && _events.Contains(name!);

    /// <summary>How many of this run's Beaten events have been collected.</summary>
    public int BeatenCount(Func<string, bool> isCollected)
    {
        var beaten = 0;
        foreach (var name in _events)
        {
            if (isCollected(name)) beaten++;
        }
        return beaten;
    }

    /// <summary>Is this a location the seed actually contains?</summary>
    public bool Exists(string? name)
        => !string.IsNullOrEmpty(name) && _slot.Requirements.ContainsKey(name!);

    /// <summary>
    /// The location for a solved controller, or null when that controller is
    /// not a check on its own.
    /// </summary>
    public string? ForController(int slotIndex, string? controllerName)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null || string.IsNullOrEmpty(controllerName)) return null;

        if (!_slot.ControllerGroups.TryGetValue(entry.LevelId, out var groups)) return null;
        if (!groups.TryGetValue(controllerName!, out var group)) return null;

        var name = LocationNames.Part(entry.LevelId, entry.Instance, group);
        return Exists(name) ? name : null;
    }

    /// <summary>
    /// The location for the Nth distinct solution found on this slot, 1-based.
    ///
    /// Ordinal rather than keyed by the game's solution id: the ids are
    /// internal strings the generator never sees, so the Nth solution found is
    /// the only thing both sides can agree on. It does mean two players can
    /// check "Solution 1" by finding different solutions, which is the correct
    /// behaviour - the location is "you solved it once", not "you found this
    /// particular arrangement".
    /// </summary>
    public string? ForSolution(int slotIndex, int solutionNumber)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null || solutionNumber < 1) return null;

        var name = LocationNames.Solution(entry.LevelId, entry.Instance, solutionNumber);
        return Exists(name) ? name : null;
    }

    /// <summary>
    /// The event location that grants one "Level Beaten" token, or null if
    /// this slot has none.
    /// </summary>
    public string? ForBeaten(int slotIndex)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null) return null;

        var name = LocationNames.Beaten(entry.LevelId, entry.Instance);
        return Exists(name) ? name : null;
    }

    /// <summary>
    /// Every location this slot can ever produce. Used to decide whether a
    /// puzzle still has anything left in it, which is what the tracker badge
    /// on the card reports.
    /// </summary>
    public IReadOnlyList<string> ForSlot(int slotIndex)
    {
        // Built once per slot. Which locations a slot CAN produce is fixed by
        // the seed; only whether they are collected changes, and that is the
        // caller's question, not this one's.
        //
        // The badge refresh asked this for every card once a second, and each
        // answer allocated a list, ran a LINQ Distinct and built several names
        // through a regex - a few hundred allocations a second to reproduce an
        // identical answer.
        if (_forSlot.TryGetValue(slotIndex, out var known)) return known;

        var built = BuildForSlot(slotIndex);
        _forSlot[slotIndex] = built;
        return built;
    }

    private readonly Dictionary<int, IReadOnlyList<string>> _forSlot = new();

    private IReadOnlyList<string> BuildForSlot(int slotIndex)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null) return Array.Empty<string>();

        var names = new List<string>();

        for (int n = 1; ; n++)
        {
            var solution = LocationNames.Solution(entry.LevelId, entry.Instance, n);
            if (!Exists(solution)) break;
            names.Add(solution);
        }

        if (_slot.ControllerGroups.TryGetValue(entry.LevelId, out var groups))
        {
            // Distinct: a merged group has several controllers pointing at one
            // name, and listing it twice would make the card look unfinishable.
            foreach (var group in groups.Values.Distinct(StringComparer.Ordinal))
            {
                var part = LocationNames.Part(entry.LevelId, entry.Instance, group);
                if (Exists(part)) names.Add(part);
            }
        }

        var beaten = ForBeaten(slotIndex);
        if (beaten != null) names.Add(beaten);

        return names;
    }

    private SlotEntry? SlotAt(int slotIndex)
        => slotIndex >= 0 && slotIndex < _slot.Slots.Count ? _slot.Slots[slotIndex] : null;
}
