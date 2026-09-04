namespace ALTTLArchipelago.Core;

/// <summary>
/// Which mechanics the player has, and therefore which objects they may move.
///
/// The mod dims objects by their controller's CLASS, so this answers the
/// question the dimming loop actually asks - "is this class locked" - rather
/// than making every caller map class to ability first.
///
/// Fails OPEN. An unrecognised controller class is treated as unlocked, never
/// locked. Getting that backwards would make a puzzle unsolvable because of a
/// class we failed to recognise, which is far worse than the alternative of
/// letting the player move something a stricter reading would have gated. The
/// generator's logic and this class are both built from abilities.json, so a
/// class arriving here unrecognised means the game has content the table does
/// not know about - and in that case the honest answer is to get out of the
/// way.
/// </summary>
public sealed class AbilityState
{
    private readonly Dictionary<string, string> _classToAbility =
        new(StringComparer.Ordinal);
    /// <summary>
    /// Granted by the seed itself, and listed in slot_data.
    ///
    /// The generator push_precollects these, so they DO also arrive as items -
    /// but they arrive on their own schedule, and in testing one landed a step
    /// ahead of slot_data being parsed. Holding them from slot_data means the
    /// state is right from the instant the seed is known, and no ordering
    /// accident between the item stream and the handshake can leave the player
    /// unable to move something they were given at generation.
    ///
    /// Kept apart from the received set so that SetHeld, which is absolute over
    /// items, cannot clear them.
    /// </summary>
    private readonly HashSet<string> _starting = new(StringComparer.Ordinal);

    /// <summary>Abilities that arrived as items.</summary>
    private readonly HashSet<string> _received = new(StringComparer.Ordinal);

    /// <summary>
    /// Every ability name this seed uses, from slot_data's own catalogue.
    ///
    /// This is what makes "is this item an ability?" answerable without a
    /// second list to keep in step. The server also sends filler (Title Theme,
    /// Colour Scheme, Daily Badge), traps and the beaten token; treating an
    /// unrecognised name as an ability put junk in the held set.
    /// </summary>
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);

    private bool _locksEnabled = true;

    /// <summary>
    /// Build from slot_data's ability catalogue: ability -> classes it unlocks.
    /// </summary>
    public AbilityState(SlotData slot)
    {
        _locksEnabled = slot.AbilityLocks;
        foreach (var (ability, classes) in slot.Abilities)
        {
            _known.Add(ability);
            foreach (var cls in classes)
            {
                if (!string.IsNullOrEmpty(cls)) _classToAbility[cls] = ability;
            }
        }
        foreach (var ability in slot.StartingAbilities)
        {
            if (!string.IsNullOrEmpty(ability)) _starting.Add(ability);
        }
    }

    /// <summary>Is this item name one of the seed's abilities?</summary>
    public bool IsAbility(string name)
        => !string.IsNullOrEmpty(name) && _known.Contains(name);

    /// <summary>Everything the player has, from either source.</summary>
    public IReadOnlyCollection<string> Held
    {
        get
        {
            var all = new HashSet<string>(_starting, StringComparer.Ordinal);
            all.UnionWith(_received);
            return all;
        }
    }

    public bool LocksEnabled => _locksEnabled;

    /// <summary>
    /// Replace the set of abilities that ARRIVED AS ITEMS.
    ///
    /// Absolute rather than additive, because Archipelago resends every item
    /// on reconnect: counting arrivals would be right the first time and wrong
    /// every time after. Starting abilities are untouched - they are not items
    /// and are never resent, so clearing them here would lose them for good.
    /// </summary>
    public void SetHeld(IEnumerable<string> abilities)
    {
        _received.Clear();
        foreach (var ability in abilities)
        {
            if (!string.IsNullOrEmpty(ability)) _received.Add(ability);
        }
    }

    public void Grant(string ability)
    {
        if (!string.IsNullOrEmpty(ability)) _received.Add(ability);
    }

    public bool Has(string ability)
        => _starting.Contains(ability) || _received.Contains(ability);

    /// <summary>
    /// Whether a controller of this class should be dimmed and unmovable.
    ///
    /// False for a class nobody gates - the baseline verbs like Draggables,
    /// and anything the catalogue has never heard of.
    /// </summary>
    public bool IsClassLocked(string controllerClass)
    {
        if (!_locksEnabled) return false;
        if (string.IsNullOrEmpty(controllerClass)) return false;
        if (!_classToAbility.TryGetValue(controllerClass, out var ability)) return false;
        return !Has(ability);
    }

    /// <summary>The ability a class needs, or null when it needs none.</summary>
    public string? AbilityFor(string controllerClass)
        => _classToAbility.TryGetValue(controllerClass, out var ability) ? ability : null;

    /// <summary>Whether every ability in this set is held.</summary>
    public bool HasAll(IEnumerable<string> abilities)
    {
        foreach (var ability in abilities)
        {
            if (!Has(ability)) return false;
        }
        return true;
    }
}
