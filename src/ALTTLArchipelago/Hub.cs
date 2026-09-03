using System;
using System.Collections.Concurrent;

namespace ALTTLArchipelago;

/// <summary>
/// The boundary between the Archipelago client's threads and Unity's.
///
/// The client raises its callbacks on a socket thread. Touching a Unity object
/// from there is undefined behaviour that usually looks like a crash somewhere
/// unrelated, so nothing does: callbacks push a plain delegate onto this queue
/// and the main thread drains it. The client never learns what Unity is, which
/// is also what makes the connection logic testable without a game.
///
/// A plain static class rather than anything injected into IL2CPP: statics on
/// injected types correlated with stack-overflow crashes during level load in
/// the sibling cw4 project, so all state lives here and the MonoBehaviour that
/// ticks it holds none.
/// </summary>
internal static class Hub
{
    private static readonly ConcurrentQueue<Action> Pending = new();

    /// <summary>Queue work to run on the next Unity frame.</summary>
    internal static void OnMainThread(Action action)
    {
        if (action != null) Pending.Enqueue(action);
    }

    /// <summary>
    /// Drain the queue. Called once per frame.
    ///
    /// Each item is isolated: one throwing action must not eat the rest of the
    /// queue, because the ones behind it may be the checks that unstick a run.
    /// The count is snapshotted so an action that queues more work cannot spin
    /// this loop forever within a single frame.
    /// </summary>
    internal static void Tick()
    {
        int budget = Pending.Count;
        while (budget-- > 0 && Pending.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"queued action threw: {e}");
            }
        }
    }

    internal static int PendingCount => Pending.Count;
}
