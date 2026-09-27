namespace ALTTLArchipelago.Core;

/// <summary>
/// Holds a session's events until its login has been ACCEPTED, then lets
/// them through in the one order the run can handle: the held items, then
/// Ready, then anything else.
///
/// WHY. The server starts sending the moment the socket is up, before the
/// mod has checked the seed. Two things went wrong because of that:
///
/// - A REFUSED seed still delivered. droha's 0.4.1 log: "refusing the seed:
///   this seed was built with the Cupboards and Drawers DLC ..." and, around
///   it, "received item: Jigsaw", a "Received Jigsaw from Server" toast and
///   "items: 1 restored on connect", while an offline run on another seed was
///   the one being played.
/// - Every reconnect replayed the run's old cat traps as new. The replay was
///   handled BEFORE Ready loaded the run state that counts the traps already
///   sprung: "trap: 26 cat(s) found nothing to knock over", then "run state:
///   ... 25 trap(s) already sprung" (droha, 2026-09-25; Kat's log shows 40).
///   On the title screen they hit nothing; in a level they would reset it.
///
/// Items and Ready are queued as ONE action. Queued separately, a frame could
/// fall between them, and the trap tick would run with the new items and the
/// old count.
///
/// Checked-location updates that arrive before acceptance are dropped, not
/// held: they are the login's own list, and the Ready handler reads the whole
/// list itself.
/// </summary>
public sealed class SessionGate<TItem>
{
    private readonly Action<Action> _dispatch;
    private readonly object _lock = new();
    private List<TItem> _items = new();
    private List<string> _lines = new();
    private bool _open;

    /// <param name="dispatch">Queues work for the main thread.</param>
    public SessionGate(Action<Action> dispatch)
    {
        _dispatch = dispatch;
    }

    /// <summary>True once the login was accepted and Ready has been queued.</summary>
    public bool IsOpen
    {
        get { lock (_lock) return _open; }
    }

    /// <summary>Items from the server: held until open, then delivered.</summary>
    public void Items(IReadOnlyList<TItem> items, Action<TItem> deliver)
    {
        if (items.Count == 0) return;
        lock (_lock)
        {
            if (!_open)
            {
                _items.AddRange(items);
                return;
            }
            var batch = items.ToList();
            _dispatch(() =>
            {
                foreach (var item in batch) deliver(item);
            });
        }
    }

    /// <summary>A line to show (an item send): held until open, then delivered.</summary>
    public void Line(string line, Action<string> deliver)
    {
        lock (_lock)
        {
            if (!_open)
            {
                _lines.Add(line);
                return;
            }
            _dispatch(() => deliver(line));
        }
    }

    /// <summary>Locations the server marked checked: dropped until open.</summary>
    public void Locations(IReadOnlyList<string> names, Action<IReadOnlyList<string>> deliver)
    {
        if (names.Count == 0) return;
        lock (_lock)
        {
            if (!_open) return;
            var batch = names.ToList();
            _dispatch(() => deliver(batch));
        }
    }

    /// <summary>
    /// The login was accepted: queue the held items, Ready and the held
    /// lines as one action. A second call does nothing.
    /// </summary>
    public void Open(Action<TItem> deliverItem, Action ready, Action<string> deliverLine)
    {
        lock (_lock)
        {
            if (_open) return;
            _open = true;
            var items = _items;
            var lines = _lines;
            _items = new List<TItem>();
            _lines = new List<string>();
            _dispatch(() =>
            {
                foreach (var item in items) deliverItem(item);
                ready();
                foreach (var line in lines) deliverLine(line);
            });
        }
    }
}
