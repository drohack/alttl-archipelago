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

    /// <summary>Raised on the MAIN thread for each item, oldest first.</summary>
    internal event Action<string>? ItemReceived;

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
                LastError = why;
                Plugin.Logger.LogWarning($"login refused: {why}");
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
            return e.Message;
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
        };
        session.Socket.SocketClosed += reason =>
        {
            var wasConnected = Connected;
            Connected = false;
            LastError = reason?.ToString() ?? "connection closed";
            Plugin.Logger.LogWarning($"disconnected: {reason}");

            // Only an UNEXPECTED drop should trigger a reconnect. A deliberate
            // Disconnect() clears Connected first, so this stays quiet for it.
            if (wasConnected && !_closingDeliberately)
            {
                var why = LastError;
                _dispatch(() => Dropped?.Invoke(why));
            }
        };
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
        var names = new List<string>();
        while (helper.Any())
        {
            var item = helper.DequeueItem();
            names.Add(item.ItemName ?? item.ItemId.ToString());
        }

        if (names.Count == 0) return;
        _dispatch(() =>
        {
            foreach (var name in names) ItemReceived?.Invoke(name);
        });
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
