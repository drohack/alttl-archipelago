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

    /// <summary>
    /// The run's stars: (Solution locations collected, Solution locations
    /// the run has). A star is a solution found, as the level select counts
    /// them (droha, 2026-09-28: "it's number of solutions"), and this is what
    /// the Collect Stars goal counts. It is the cards' hover stars summed, so
    /// a card can never light a star the goal does not count.
    /// </summary>
    public (int Lit, int Total) RunStars(Func<string, bool> isCollected)
    {
        int lit = 0, total = 0;
        for (int slot = 0; slot < _slot.Slots.Count; slot++)
        {
            var (l, t) = SolutionStars(slot, isCollected);
            lit += l;
            total += t;
        }
        return (lit, total);
    }

    /// <summary>
    /// The stars of some slots, summed: the run's count for one section of the
    /// level select, whose header the game would otherwise count from the
    /// save (the plugin's SectionStars). The same sum as the cards' hover
    /// stars and the goal, so the three cannot disagree.
    /// </summary>
    public (int Lit, int Total) StarsOf(IEnumerable<int> slots, Func<string, bool> isCollected)
    {
        int lit = 0, total = 0;
        foreach (var slot in slots)
        {
            var (l, t) = SolutionStars(slot, isCollected);
            lit += l;
            total += t;
        }
        return (lit, total);
    }

    /// <summary>
    /// Is anything on this slot still uncollected?
    ///
    /// Lifted out of Track, which had it as a private helper, so the level
    /// select and the Skip cannot drift apart about what "nothing left to do"
    /// means - the yellow star on a card is this predicate.
    /// </summary>
    public bool HasWorkLeft(int slotIndex, Func<string, bool> isCollected)
    {
        foreach (var name in ForSlot(slotIndex))
        {
            if (!isCollected(name)) return true;
        }
        return false;
    }

    /// <summary>
    /// The hover stars on a run's card: (Solution locations of this slot
    /// collected, Solution locations it has). Counted, not a prefix, so a
    /// Solution 2 collected first still lights one star; a Skip sends every
    /// location, so it lights them all.
    /// </summary>
    public (int Lit, int Total) SolutionStars(int slotIndex, Func<string, bool> isCollected)
    {
        int lit = 0, total = 0;
        foreach (var name in SolutionsOf(slotIndex))
        {
            total++;
            if (isCollected(name)) lit++;
        }
        return (lit, total);
    }

    /// <summary>
    /// This slot's Solution locations, in order: one per ending, or numbered
    /// on a generated puzzle. Only names the seed contains.
    /// </summary>
    public IReadOnlyList<string> SolutionsOf(int slotIndex)
    {
        var names = new List<string>();
        var entry = SlotAt(slotIndex);
        if (entry == null) return names;

        if (_slot.Endings.TryGetValue(entry.LevelId, out var endings) && endings.Count > 0)
        {
            foreach (var e in endings)
            {
                var name = LocationNames.Ending(entry.LevelId, entry.Instance, e.Location);
                if (Exists(name)) names.Add(name);
            }
            return names;
        }

        for (int n = 1; ; n++)
        {
            var name = LocationNames.Solution(entry.LevelId, entry.Instance, n);
            if (!Exists(name)) break;
            names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// The location a completion files: the ending its solution id names.
    ///
    /// A FIXED ENDING, NOT THE NTH FOUND (droha, 2026-09-28). The id picks the
    /// location. An id the table does not know - an ending nobody had seen
    /// when it was measured, or a forced or skipped completion ("_-1") -
    /// files the first unseen ending ("Solution: Other k") still free, else
    /// the first ending still free. A generated puzzle has no table: its
    /// endings change with the layout, and the Nth distinct id found files
    /// "Solution N" as before.
    ///
    /// EVERY DISTINCT ID ITS OWN LOCATION. The ids found before this one each
    /// took an ending, and this one takes the ending it names only if that is
    /// still free. A table that guessed an ending wrong then costs a name, not
    /// a check: Figurines' table had Groupables-(Achievement)_0 for its third
    /// ending, the game's was SortingItemsDraggables_1, and that filed the
    /// Draggables check already in (droha, 2026-10-02: 2 stars of 3 for 3
    /// done). Past the last free ending, the first ending (nothing new).
    ///
    /// BY THE ORDER FOUND, NEVER BY WHAT IS COLLECTED: the answer for an id
    /// must be the same at every launch, or the pass that files what an
    /// earlier session earned would send a different location each time. It
    /// depends only on the ids found before it. `foundInOrder` is the slot's
    /// distinct ids in the order found (SolutionOrdinals), including this one.
    /// </summary>
    public string? ForEnding(int slotIndex, string? solutionId, IReadOnlyList<string> foundInOrder)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null) return null;
        var id = solutionId ?? "";
        var position = -1;
        for (int i = 0; i < foundInOrder.Count; i++)
        {
            if (foundInOrder[i] == id) { position = i; break; }
        }
        if (position < 0) return null;

        if (!_slot.Endings.TryGetValue(entry.LevelId, out var endings) || endings.Count == 0)
            return ForSolution(slotIndex, position + 1);

        var taken = new bool[endings.Count];
        var pick = -1;
        for (int i = 0; i <= position; i++)
        {
            pick = Claim(endings, foundInOrder[i], taken);
            if (pick >= 0) taken[pick] = true;
        }
        if (pick < 0) pick = 0;
        return Present(LocationNames.Ending(entry.LevelId, entry.Instance, endings[pick].Location));
    }

    /// <summary>
    /// The free ending `id` takes: the one it names (an entry may answer to
    /// several ids, "Shuffle_1|Draggables_0", Endings.Alternatives: Books'
    /// second solution), else the first free unseen one, else the first free
    /// one; -1 when every ending is taken.
    /// </summary>
    private static int Claim(IReadOnlyList<EndingEntry> endings, string id, bool[] taken)
    {
        for (int j = 0; j < endings.Count; j++)
        {
            if (!taken[j] && Endings.Alternatives(endings[j].Id).Contains(id)) return j;
        }
        for (int j = 0; j < endings.Count; j++)
        {
            if (!taken[j] && endings[j].Id == null) return j;
        }
        for (int j = 0; j < endings.Count; j++)
        {
            if (!taken[j]) return j;
        }
        return -1;
    }

    private string? Present(string name) => Exists(name) ? name : null;

    /// <summary>
    /// The Beaten events still uncollected on slots that already have a
    /// Solution in. droha, 2026-09-25: "I wouldn't say 'beaten' is different
    /// from solving at least 1 solution for the puzzle ... i would expect them
    /// to be intertwined." A Solution sent for the slot by anyone - an admin's
    /// /send_location, a collect - counts the puzzle as beaten, which a Beaten
    /// event (no server address) could otherwise never be told.
    /// </summary>
    public IReadOnlyList<string> BeatenBySolutions(Func<string, bool> isCollected)
    {
        var owed = new List<string>();
        for (int slot = 0; slot < _slot.Slots.Count; slot++)
        {
            var beaten = ForBeaten(slot);
            if (beaten == null || isCollected(beaten)) continue;
            if (SolutionStars(slot, isCollected).Lit > 0) owed.Add(beaten);
        }
        return owed;
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
    /// Every controller of the group this one belongs to on the slot's level,
    /// itself included; empty when it is in no group. A part is its whole
    /// group, so the mod files it only once all of these are solved
    /// (GroupSolved).
    /// </summary>
    public IReadOnlyList<string> GroupMembers(int slotIndex, string? controllerName)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null || string.IsNullOrEmpty(controllerName)) return Array.Empty<string>();
        if (!_slot.ControllerGroups.TryGetValue(entry.LevelId, out var groups)) return Array.Empty<string>();
        if (!groups.TryGetValue(controllerName!, out var group)) return Array.Empty<string>();
        return groups.Where(kv => kv.Value == group).Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// Is this group finished: every member the level has registered solved,
    /// and at least one registered? A member not registered is not waited
    /// for, so a controller that never turns up cannot strand the check.
    /// `solved` maps each registered controller's name to IsSolved. Medicine
    /// Cabinet's "Red Items" is seven controllers, and its check waits for the
    /// last (droha, 2026-09-28: red waits for the toothbrush and paste).
    /// </summary>
    public static bool GroupSolved(IEnumerable<string> members, IReadOnlyDictionary<string, bool> solved)
    {
        var any = false;
        foreach (var member in members)
        {
            if (!solved.TryGetValue(member, out var done)) continue;
            if (!done) return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The part locations of the controllers a completion's solution id names
    /// (SolutionParts): what that ending actually needed. Empty when the id
    /// names no controller that is a check of its own - a single-group level,
    /// an id like MedicineCabinet's "Draggables_0" - and the caller then
    /// judges the solution by its own requirement, the level's whole set.
    /// </summary>
    public IReadOnlyList<string> PartsForSolution(int slotIndex, string? solutionId)
    {
        var parts = new List<string>();
        var entry = SlotAt(slotIndex);
        if (entry == null) return parts;
        if (!_slot.ControllerGroups.TryGetValue(entry.LevelId, out var groups)) return parts;

        foreach (var controller in SolutionParts.ControllersFor(solutionId, groups.Keys))
        {
            var part = ForController(slotIndex, controller);
            if (part != null && !parts.Contains(part)) parts.Add(part);
        }
        return parts;
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
    /// The location for an achievement the game just awarded, or null when it
    /// is not one of this slot's checks: the seed was generated without
    /// `achievements`, the achievement belongs to another puzzle, or it is one
    /// AchievementChecks leaves out (a hint or chapter achievement).
    /// </summary>
    public string? ForAchievement(int slotIndex, string? achievementId)
    {
        var entry = SlotAt(slotIndex);
        if (entry == null) return null;
        var found = AchievementChecks.Find(entry.LevelId, achievementId);
        if (found == null) return null;

        var name = LocationNames.Achievement(entry.LevelId, entry.Instance, found.Display);
        return Exists(name) ? name : null;
    }

    /// <summary>
    /// This slot's achievement locations, empty unless the seed has
    /// `achievements`. Part of ForSlot: they can hold progression, so the
    /// card's star and badge count them and a Skip sends them (droha,
    /// 2026-09-29). Their requirement is the puzzle's whole ability set before
    /// bypasses, which can be more than the Beaten event asks; the star goal
    /// counts solutions (a Star event per solution), so a card's star is not
    /// in its logic.
    /// </summary>
    public IReadOnlyList<string> ForAchievements(int slotIndex)
    {
        var names = new List<string>();
        var entry = SlotAt(slotIndex);
        if (entry == null) return names;
        foreach (var found in AchievementChecks.For(entry.LevelId))
        {
            var name = LocationNames.Achievement(entry.LevelId, entry.Instance, found.Display);
            if (Exists(name)) names.Add(name);
        }
        return names;
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

        var names = new List<string>(SolutionsOf(slotIndex));

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

        // Achievements are checks like any other: the star waits for them
        // and a Skip sends them (droha, 2026-09-29).
        names.AddRange(ForAchievements(slotIndex));

        return names;
    }

    private SlotEntry? SlotAt(int slotIndex)
        => slotIndex >= 0 && slotIndex < _slot.Slots.Count ? _slot.Slots[slotIndex] : null;
}
