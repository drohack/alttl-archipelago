using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// A refused seed delivered its items, and every reconnect handled the item
/// replay before the run state that counts spent traps (droha, 2026-09-25:
/// "26 cat(s) found nothing to knock over" before "25 trap(s) already
/// sprung"). The gate holds a session's events until the login is accepted.
/// </summary>
public class SessionGateTests
{
    private readonly List<Action> _queue = new();
    private readonly List<string> _seen = new();

    private SessionGate<string> Gate() => new(a => _queue.Add(a));

    private void Drain()
    {
        var run = _queue.ToList();
        _queue.Clear();
        foreach (var a in run) a();
    }

    [Fact]
    public void ARefusedSessionDeliversNothing()
    {
        var gate = Gate();
        gate.Items(new[] { "Jigsaw" }, i => _seen.Add("item " + i));
        gate.Line("Server sent Jigsaw", l => _seen.Add("line " + l));
        gate.Locations(new[] { "Spider Web - Solution 1" }, _ => _seen.Add("locations"));

        Drain();

        Assert.Empty(_seen);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void HeldItemsArriveBeforeReadyInOneAction()
    {
        var gate = Gate();
        gate.Items(new[] { "Cat Trap", "Cat Trap" }, i => _seen.Add("item " + i));
        gate.Line("Kat sent Cat Trap to Grayson", l => _seen.Add("line " + l));

        gate.Open(i => _seen.Add("item " + i), () => _seen.Add("ready"), l => _seen.Add("line " + l));

        // One queued action, so no frame can fall between the replay and
        // Ready (the trap tick would run in between).
        Assert.Single(_queue);
        Drain();
        Assert.Equal(new[] { "item Cat Trap", "item Cat Trap", "ready", "line Kat sent Cat Trap to Grayson" }, _seen);
    }

    [Fact]
    public void TheLoginsOwnCheckListIsDroppedNotHeld()
    {
        var gate = Gate();
        gate.Locations(new[] { "Telescope #2 - Solution 1" }, _ => _seen.Add("locations"));

        gate.Open(_ => { }, () => _seen.Add("ready"), _ => { });
        Drain();

        Assert.Equal(new[] { "ready" }, _seen);
    }

    [Fact]
    public void AfterOpenEverythingPassesStraightThrough()
    {
        var gate = Gate();
        gate.Open(_ => { }, () => _seen.Add("ready"), _ => { });
        gate.Items(new[] { "Drawer" }, i => _seen.Add("item " + i));
        gate.Locations(new[] { "Daggers - Solution 1" }, n => _seen.Add("locations " + n[0]));
        gate.Line("Grayson sent Drawer to Kat", l => _seen.Add("line " + l));

        Drain();

        Assert.Equal(new[] { "ready", "item Drawer", "locations Daggers - Solution 1", "line Grayson sent Drawer to Kat" }, _seen);
    }

    [Fact]
    public void OpeningTwiceQueuesReadyOnce()
    {
        var gate = Gate();
        gate.Open(_ => { }, () => _seen.Add("ready"), _ => { });
        gate.Open(_ => { }, () => _seen.Add("ready"), _ => { });
        Drain();
        Assert.Equal(new[] { "ready" }, _seen);
    }
}
