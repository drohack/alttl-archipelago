namespace ALTTLArchipelago.Core;

/// <summary>
/// Which slot Play, the next arrow and the level select's opening scroll
/// point at - and whether there is one at all.
///
/// PLAYABLE means an open slot with an uncollected location the run can earn
/// now, by packs and abilities. The next arrow used to fall back to any
/// unfinished open slot when nothing was playable, which opened a puzzle the
/// player could not advance; droha, 2026-09-26: bring the player to the level
/// select instead, "it would be more visually better to know when you are
/// blocked". So every pick here is playable-only, and -1 means "show the
/// level select".
///
/// Fails OPEN while the run is still being wired up (no progress table or no
/// abilities yet): every open slot counts as playable, the fallback the
/// callers always had.
/// </summary>
public sealed class SlotPicker
{
    private readonly TrackState _track;
    private readonly CheckRouter _router;
    private readonly Func<string, bool> _isCollected;
    private readonly SlotProgress? _progress;
    private readonly AbilityState? _abilities;

    public SlotPicker(TrackState track, CheckRouter router, Func<string, bool> isCollected,
                      SlotProgress? progress, AbilityState? abilities)
    {
        _track = track ?? throw new ArgumentNullException(nameof(track));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _isCollected = isCollected ?? throw new ArgumentNullException(nameof(isCollected));
        _progress = progress;
        _abilities = abilities;
    }

    /// <summary>
    /// Any uncollected location on this slot the run can earn now, open or
    /// not. The same predicate the card badge colours by: true exactly when
    /// the card is green or green over red.
    /// </summary>
    public bool HasPlayableWork(int slot)
    {
        if (_progress == null || _abilities == null) return true;
        foreach (var name in _router.ForSlot(slot))
        {
            if (_isCollected(name)) continue;
            if (_progress.IsReachable(name, _track.PacksHeld, _abilities)) return true;
        }
        return false;
    }

    /// <summary>Open, and holding work the run can do now.</summary>
    public bool IsPlayable(int slot) => _track.IsOpen(slot) && HasPlayableWork(slot);

    /// <summary>
    /// The next playable slot after <paramref name="current"/>, in track
    /// order, wrapping once - so the current slot itself comes last. -1 when
    /// nothing is playable.
    /// </summary>
    public int Next(int current)
    {
        var count = _track.Slots.Count;
        var start = current >= 0 ? current + 1 : 0;
        for (int step = 0; step < count; step++)
        {
            var slot = (start + step) % count;
            if (IsPlayable(slot)) return slot;
        }
        return -1;
    }

    /// <summary>The first playable slot: what Play opens. -1 when none.</summary>
    public int First()
    {
        for (int slot = 0; slot < _track.Slots.Count; slot++)
        {
            if (IsPlayable(slot)) return slot;
        }
        return -1;
    }

    /// <summary>The last playable slot: where the level select opens. -1 when none.</summary>
    public int Farthest()
    {
        for (int slot = _track.Slots.Count - 1; slot >= 0; slot--)
        {
            if (IsPlayable(slot)) return slot;
        }
        return -1;
    }

    /// <summary>
    /// Whether any slot other than <paramref name="except"/> is playable: the
    /// question at a completion, while the finished slot's own check may not
    /// be filed yet.
    /// </summary>
    public bool AnyPlayableBesides(int except)
    {
        for (int slot = 0; slot < _track.Slots.Count; slot++)
        {
            if (slot != except && IsPlayable(slot)) return true;
        }
        return false;
    }
}
