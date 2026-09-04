namespace ALTTLArchipelago.Core;

/// <summary>
/// What has been checked, and separately what the server has not been told.
///
/// TWO SETS, NOT ONE, and the distinction is the whole reason this class
/// exists. After logging in, the server's list of checked locations is
/// authoritative for what the player has already collected - it knows about
/// sessions on other machines and things collected by other players. But
/// anything checked while offline is ALSO already collected as far as the
/// player is concerned, and still has to be sent.
///
/// Merge the two and one of them wins wrongly: trust the server alone and
/// offline checks are silently dropped; trust ours alone and we re-send
/// everything on every login. So "collected" and "owed" are tracked apart.
///
/// The game fires its solved event repeatedly - measured, sixteen events
/// across thirteen controllers in one level - so deduping is not an
/// optimisation, it is what stops the same location being sent over and over.
/// </summary>
public sealed class CheckLedger
{
    private readonly HashSet<string> _collected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _owed = new(StringComparer.Ordinal);

    /// <summary>
    /// The subset of <see cref="_collected"/> that came from us and can never
    /// come from anywhere else.
    ///
    /// Event locations have no address, so the server never lists them back at
    /// login, and they are deliberately never owed - so neither of the two
    /// things that rebuild a ledger knows about them. Tracked apart purely so
    /// they can be written to the save and restored; without that the beaten
    /// count reset to zero on every login and the credits goal was reachable
    /// only inside one unbroken session.
    /// </summary>
    private readonly HashSet<string> _local = new(StringComparer.Ordinal);

    /// <summary>Everything known checked, from any source.</summary>
    public IReadOnlyCollection<string> Collected => _collected;

    /// <summary>Checked here but not yet acknowledged by the server.</summary>
    public IReadOnlyCollection<string> Owed => _owed;

    public bool IsCollected(string name) => _collected.Contains(name);

    /// <summary>
    /// Record a check the player just earned. Returns true only the first
    /// time, so a caller can send it exactly once and log it exactly once.
    /// </summary>
    public bool Check(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!_collected.Add(name)) return false;
        _owed.Add(name);
        return true;
    }

    /// <summary>
    /// Record a check that is real but has nowhere to be sent.
    ///
    /// The Beaten locations are Archipelago event locations: the generator uses
    /// them to spread progression, and the server has no address for them.
    /// They still have to be COLLECTED, because that is how the credits goal is
    /// counted - but owing one would mean retrying a send that can only ever be
    /// rejected, forever.
    /// </summary>
    public bool RecordLocal(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        _local.Add(name);
        return _collected.Add(name);
    }

    /// <summary>
    /// The locally-recorded event checks, in a stable order, for the save file.
    /// Sorted so a save that changed nothing produces an identical file.
    /// </summary>
    public IReadOnlyList<string> LocalForSaving()
    {
        var list = new List<string>(_local);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>
    /// Restore event checks persisted across a restart.
    ///
    /// Collected, never owed - the same rule as when they were first earned.
    /// They also stay in the local set, so the next save writes them out again
    /// rather than quietly dropping what this restore just recovered.
    /// </summary>
    public void RestoreLocal(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            _local.Add(name);
            _collected.Add(name);
        }
    }

    /// <summary>
    /// Take on the server's view at login.
    ///
    /// These count as collected but are NOT owed - the server already has
    /// them. Anything we owed before stays owed even if the server also lists
    /// it, because a location can be in both: collected offline, sent, and the
    /// acknowledgement lost to the same disconnect.
    /// </summary>
    public void AdoptServerChecks(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrEmpty(name)) _collected.Add(name);
        }
    }

    /// <summary>
    /// Called once the server has confirmed these. Only clears what was
    /// actually sent, so a check earned mid-flush is not lost.
    /// </summary>
    public void Acknowledge(IEnumerable<string> names)
    {
        foreach (var name in names) _owed.Remove(name);
    }

    /// <summary>Restore a queue persisted across a restart.</summary>
    public void RestoreOwed(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            _collected.Add(name);
            _owed.Add(name);
        }
    }

    /// <summary>
    /// The owed queue in a stable order, for writing to the save file.
    /// Sorted so the file does not churn between saves that changed nothing.
    /// </summary>
    public IReadOnlyList<string> OwedForSaving()
    {
        var list = new List<string>(_owed);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public void Clear()
    {
        _collected.Clear();
        _owed.Clear();
        _local.Clear();
    }
}
