using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Packets;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago;

/// <summary>
/// Talks to the Archipelago server. Knows nothing about Unity.
///
/// Everything it wants the game to do goes through a dispatch delegate, which
/// the plugin points at Hub.OnMainThread. That keeps this class runnable from
/// a test and keeps Unity calls off the socket thread.
/// </summary>
internal sealed class Connection
{
    internal const string Game = "A Little to the Left";

    private readonly Action<Action> _dispatch;
    private ArchipelagoSession? _session;

    internal Connection(Action<Action> dispatch) => _dispatch = dispatch;

    internal bool Connected { get; private set; }

    /// <summary>
    /// The last failure was one retrying cannot fix - a bad slot name, wrong
    /// password, wrong game, incompatible version. The caller should stop and
    /// say so rather than back off and try again.
    /// </summary>
    internal bool Terminal { get; private set; }
    internal SlotData? Slot { get; private set; }

    /// <summary>The slot this session logged in as, for display and naming.</summary>
    internal string SlotName { get; private set; } = "";

    /// <summary>
    /// The room's seed string, read LIVE rather than cached at login.
    ///
    /// RoomState is filled from the RoomInfo packet, and capturing it the
    /// instant TryConnectAndLogin returned sometimes got an empty string - the
    /// save was then named save_ap_&lt;slot&gt;_seed, and EVERY multiworld played
    /// under that slot name would have shared one save file. Reading it when
    /// it is needed gives the packet time to land.
    /// </summary>
    internal string Seed
    {
        get
        {
            try { return _session?.RoomState?.Seed ?? ""; }
            catch { return ""; }
        }
    }

    /// <summary>Why the last attempt failed, for the connection pane.</summary>
    internal string LastError { get; private set; } = "";

    /// <summary>Raised on the MAIN thread when the socket drops unexpectedly.</summary>
    internal event Action<string>? Dropped;

    /// <summary>Raised on the MAIN thread once slot_data has been parsed.</summary>
    internal event Action<SlotData>? Ready;

    /// <summary>One item, with what the text client would need to colour it.</summary>
    internal readonly struct ReceivedItem
    {
        internal ReceivedItem(string name, ApPalette.ItemFlags flags, string from, bool fromSelf)
        {
            Name = name;
            Flags = flags;
            From = from;
            FromSelf = fromSelf;
        }

        internal string Name { get; }
        internal ApPalette.ItemFlags Flags { get; }
        internal string From { get; }
        internal bool FromSelf { get; }
    }

    /// <summary>Raised on the MAIN thread for each item, oldest first.</summary>
    internal event Action<ReceivedItem>? ItemReceived;

    /// <summary>
    /// Connect on a BACKGROUND thread, reporting the outcome on the main one.
    ///
    /// TryConnectAndLogin blocks until the server answers or the attempt times
    /// out. Calling it from the main thread froze the whole game solid through
    /// five retries against a dead server - no rendering, no "Connecting...",
    /// nothing but a hung window until the last timeout expired. Reported from
    /// play, and it is the reason this method returns nothing: there is no
    /// answer yet when it returns.
    ///
    /// `onResult` is raised through the dispatcher, so callers stay on the main
    /// thread and never touch Unity from here.
    /// </summary>
    internal void ConnectAsync(string host, int port, string slotName,
                               string? password, Action<string> onResult)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var error = Connect(host, port, slotName, password);
            _dispatch(() => onResult(error));
        });
    }

    /// <summary>
    /// Is anything listening at all? Returns null when it is, or a plain
    /// reason when it is not.
    ///
    /// Worth doing before the client's own connect because the OS already
    /// knows the answer immediately: a port with nothing behind it fails with
    /// "connection refused" in milliseconds, and an unknown host fails almost
    /// as fast. Left to the Archipelago client, both of those instead sit
    /// through its login timeout and then report "Connection timed out",
    /// which is both slow and the wrong reason.
    /// </summary>
    private static string? Unreachable(string host, int port)
    {
        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            var connecting = probe.ConnectAsync(host, port);

            // Short: this only has to catch "nothing there", not measure a
            // slow server. A real one answers a TCP handshake promptly even
            // when it is busy.
            if (!connecting.Wait(TimeSpan.FromSeconds(4)))
            {
                return $"No response from {host}:{port}";
            }
            if (connecting.IsFaulted) throw connecting.Exception!.GetBaseException();
            return null;
        }
        catch (System.Net.Sockets.SocketException e)
        {
            return e.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused
                    => $"Nothing is listening on {host}:{port}",
                System.Net.Sockets.SocketError.HostNotFound
                    => $"Cannot find host {host}",
                System.Net.Sockets.SocketError.TimedOut
                    => $"No response from {host}:{port}",
                _ => $"{host}:{port} unreachable ({e.SocketErrorCode})",
            };
        }
        catch (Exception e)
        {
            return $"{host}:{port} unreachable ({e.GetType().Name})";
        }
    }

    private string Connect(string host, int port, string slotName, string? password)
    {
        var unreachable = Unreachable(host, port);
        if (unreachable != null)
        {
            LastError = unreachable;
            return unreachable;
        }

        try
        {
            _session = ArchipelagoSessionFactory.CreateSession(host, port);
            WireEvents(_session);

            var result = _session.TryConnectAndLogin(
                Game, slotName, ItemsHandlingFlags.AllItems,
                password: string.IsNullOrEmpty(password) ? null : password);

            if (result is LoginFailure failure)
            {
                var why = string.Join("; ", failure.Errors);
                if (why.Length == 0) why = "login refused";

                // A wrong slot name is not a flaky network.
                //
                // The refusal codes were being discarded and every failure fed
                // into the same backoff, so a typo in the slot name was retried
                // three times over nine seconds before the player was told
                // anything - and the thing it was retrying could never succeed.
                Terminal = Refusals.IsTerminal(failure.ErrorCodes);

                LastError = why;
                Plugin.Logger.LogWarning(
                    $"login refused{(Terminal ? " (will not retry)" : "")}: {why}");

                // The server keeps the socket open after a refusal, expecting
                // another Connect on it. We dial a fresh one per attempt
                // instead, so this one has to be closed or it lingers with its
                // handlers still wired to the live Inventory.
                Abandon();
                return why;
            }

            var success = (LoginSuccessful)result;
            Connected = true;
            SlotName = slotName;
            LastError = "";

            // Parse on this thread, hand the RESULT to the main thread. Doing
            // the work here keeps a parse failure out of the frame loop.
            var slot = ParseSlotData(success.SlotData);
            Slot = slot;

            _dispatch(() =>
            {
                foreach (var problem in slot.Problems())
                {
                    Plugin.Logger.LogWarning($"slot_data problem: {problem}");
                }
                Ready?.Invoke(slot);
            });

            return "";
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Plugin.Logger.LogError($"connect failed: {e}");
            Abandon();
            return e.Message;
        }
    }

    /// <summary>
    /// Close a session we are giving up on, and stop listening to it.
    ///
    /// Every attempt builds a new Connection, so without this each failure left
    /// a live socket behind with ItemReceived still pointed at the live
    /// Inventory. A refused login does not close the socket server-side, so
    /// they accumulated one per attempt.
    /// </summary>
    private void Abandon()
    {
        var session = _session;
        _session = null;
        if (session == null) return;

        try
        {
            _closingDeliberately = true;
            session.Items.ItemReceived -= OnItemReceived;
            session.Socket.DisconnectAsync();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not close the refused session: {e.Message}");
        }
        finally
        {
            _closingDeliberately = false;
        }
    }

    /// <summary>
    /// slot_data arrives as a loosely typed dictionary, and it is re-serialized
    /// here so Core's DTO - the one the tests pin against a real generated
    /// payload - can parse it, rather than hand-walking the tree twice.
    ///
    /// SERIALIZED WITH NEWTONSOFT, ON PURPOSE. The Archipelago client is built
    /// on Newtonsoft, so the values in this dictionary are JObject and JArray,
    /// not plain CLR objects. Handing those to System.Text.Json writes out
    /// JToken's own internals instead of the values, and the DTO then fails
    /// with "could not be converted to SlotEntry" on a payload that is
    /// perfectly good. Measured, not guessed - it is what the first in-game
    /// connection did.
    ///
    /// Core stays free of Newtonsoft: it only ever sees the resulting string.
    /// </summary>
    private static SlotData ParseSlotData(Dictionary<string, object> raw)
    {
        try
        {
            return SlotData.FromJson(Newtonsoft.Json.JsonConvert.SerializeObject(raw));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"could not parse slot_data: {e}");
            return new SlotData();
        }
    }

    private void WireEvents(ArchipelagoSession session)
    {
        session.Items.ItemReceived += OnItemReceived;
        session.Socket.ErrorReceived += (e, message) =>
        {
            // The message and the exception TYPE, not the exception. The full
            // trace for an unreachable server runs to several screens of
            // WebSocket and HttpConnectionPool frames and adds nothing.
            var reason = e?.GetBaseException().Message ?? "";
            Plugin.Logger.LogWarning(
                $"socket error: {message}{(reason.Length > 0 ? " - " + reason : "")}");

            // A closed socket reported HERE is still a drop.
            //
            // Measured: kill the server mid-session and this fires, while
            // SocketClosed never does. The reconnect machinery was wired only
            // to SocketClosed, so nothing invoked it - the client sat believing
            // it was still connected, indefinitely, with no retry and nothing
            // on screen to say otherwise. Every check earned after that went to
            // the offline queue and stayed there.
            //
            // Not every error is a drop, so this only reacts to the socket
            // actually being closed; Drop() is idempotent, so if SocketClosed
            // does also fire the second one is ignored.
            if (IsSocketClosed(e)) Drop(reason.Length > 0 ? reason : message);
        };
        session.Socket.SocketClosed += reason => Drop(reason?.ToString());
    }

    /// <summary>
    /// The socket is gone. Say so once, and ask for a reconnect.
    ///
    /// Idempotent through the Connected flag, because the library reports a
    /// drop through either SocketClosed or ErrorReceived depending on how the
    /// connection died, and both are wired.
    /// </summary>
    private void Drop(string? reason)
    {
        if (!Connected) return;                  // already handled, or never up

        Connected = false;
        LastError = string.IsNullOrEmpty(reason) ? "connection closed" : reason!;
        Plugin.Logger.LogWarning($"disconnected: {LastError}");

        // Only an UNEXPECTED drop should trigger a reconnect. A deliberate
        // Disconnect() clears Connected first, so this stays quiet for it.
        if (_closingDeliberately) return;

        var why = LastError;
        _dispatch(() => Dropped?.Invoke(why));
    }

    /// <summary>Is this error the socket having closed, rather than a hiccup?</summary>
    private static bool IsSocketClosed(Exception? e)
    {
        for (var cur = e; cur != null; cur = cur.InnerException)
        {
            if (cur is Archipelago.MultiClient.Net.Exceptions.ArchipelagoSocketClosedException)
            {
                return true;
            }

            // The library does not always wrap it. The websocket layer's own
            // "closed without completing the close handshake" arrives as a
            // plain WebSocketException, and that is exactly the case that went
            // unnoticed.
            if (cur is System.Net.WebSockets.WebSocketException) return true;
        }
        return false;
    }

    /// <summary>
    /// Drain the helper synchronously HERE, on the socket thread, into plain
    /// strings - then hand those to the main thread.
    ///
    /// Deferring the drain instead would let the helper's queue advance before
    /// the main thread reads it, and items would be silently skipped.
    /// </summary>
    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        var items = new List<ReceivedItem>();
        while (helper.Any())
        {
            var item = helper.DequeueItem();

            // Flags and sender come off the item itself. Reading them here
            // rather than passing a bare name is what lets a toast colour an
            // item the way the text client does - and "progression" versus
            // "filler" is meaning, not decoration.
            var flags = ApPalette.ItemFlags.None;
            var from = "";
            var fromSelf = true;
            try
            {
                flags = (ApPalette.ItemFlags)(int)item.Flags;
                fromSelf = _session != null
                    && item.Player.Slot == _session.ConnectionInfo.Slot;
                // Alias first: it is what the player chose to be called.
                from = item.Player.Alias ?? item.Player.Name ?? "";
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"item: could not read its details: {e.Message}");
            }

            items.Add(new ReceivedItem(
                item.ItemName ?? item.ItemId.ToString(), flags, from, fromSelf));
        }

        if (items.Count == 0) return;
        _dispatch(() =>
        {
            foreach (var item in items) ItemReceived?.Invoke(item);
        });
    }

    /// <summary>
    /// Send locations, and report which ones the server accepted.
    ///
    /// Returns the names it actually sent so the ledger clears exactly those
    /// and nothing else - a check earned while this call was in flight must
    /// stay owed. Returns empty on any failure, which leaves everything owed
    /// and lets the next flush try again.
    /// </summary>
    /// <summary>
    /// Send what is owed, and report back only what the server accepted.
    ///
    /// Asynchronous on purpose, twice over. The blocking overload ran on the
    /// Unity main thread, so a slow send stalled a frame. Worse, it told the
    /// caller nothing: the old version returned the names it had looped over,
    /// and the ledger cleared them on that basis. If the socket died after the
    /// Connected check but before the frame went out, the send moved nothing,
    /// threw nothing, and the queue was emptied anyway - the check was gone
    /// from both the server and the owed list, permanently.
    ///
    /// Re-sending on doubt is safe: the protocol says duplicate location checks
    /// do not cause issues, so the only wrong answer here is dropping one.
    ///
    /// The callback arrives on a socket thread and is marshalled before it
    /// reaches the ledger.
    /// </summary>
    internal void SendChecksAsync(
        IReadOnlyList<string> names, Action<IReadOnlyList<string>> accepted)
    {
        if (!Connected || _session == null || names.Count == 0) return;

        try
        {
            var ids = new List<long>();
            var sending = new List<string>();
            var giveUp = new List<string>();

            foreach (var name in names)
            {
                var id = _session.Locations.GetLocationIdFromName(Game, name);
                if (id <= 0)
                {
                    // A location the datapackage does not know. Reporting it
                    // loudly beats retrying it forever on every flush.
                    Plugin.Logger.LogError($"checks: server has no location named '{name}'");
                    giveUp.Add(name);
                    continue;
                }
                ids.Add(id);
                sending.Add(name);
            }

            // Dropped rather than sent, but they must leave the queue or every
            // flush retries a send that can only ever be rejected.
            if (giveUp.Count > 0) _dispatch(() => accepted(giveUp));

            if (ids.Count == 0) return;

            // CompleteLocationChecksAsync(long[]) returns a Task and there is
            // no acknowledgement callback in the library - the task completing
            // means the send left, not that the server recorded it. That is
            // still strictly better than before: a dead socket faults the task,
            // where the blocking call returned normally and the queue was
            // cleared regardless.
            //
            // The remaining gap is closed by the server's own list at the next
            // login, and by duplicates being explicitly harmless: anything it
            // did not record is simply sent again.
            _session.Locations.CompleteLocationChecksAsync(ids.ToArray())
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        var why = task.Exception?.GetBaseException().Message ?? "unknown";
                        Plugin.Logger.LogWarning(
                            $"checks: send of {sending.Count} failed, staying owed: {why}");
                        return;
                    }
                    if (task.IsCanceled) return;
                    _dispatch(() => accepted(sending));
                });
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"checks: send failed, staying owed: {e.Message}");
        }
    }

    /// <summary>
    /// Tell the server the run is finished.
    ///
    /// Safe to call more than once - the server takes the first and ignores the
    /// rest - which matters because the condition is evaluated on a poll rather
    /// than at a single moment.
    /// </summary>
    internal bool ReportGoal()
    {
        try
        {
            if (!Connected || _session == null) return false;
            _session.SetGoalAchieved();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"goal: could not report it: {e.Message}");
            return false;
        }
    }

    /// <summary>Location names the server already has for this slot.</summary>
    internal IReadOnlyList<string> ServerChecks()
    {
        try
        {
            if (_session == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var id in _session.Locations.AllLocationsChecked)
            {
                var name = _session.Locations.GetLocationNameFromId(id, Game);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"checks: could not read the server's list: {e.Message}");
            return Array.Empty<string>();
        }
    }

    private bool _closingDeliberately;

    internal void Disconnect()
    {
        _closingDeliberately = true;
        Connected = false;
        try
        {
            _session?.Socket.DisconnectAsync();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"disconnect: {e.Message}");
        }
        _session = null;
    }
}
