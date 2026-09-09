using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// A Little To The Left, as an Archipelago multiworld game.
///
/// This file is the lifecycle: settings, patching, the connection and its
/// retries, the check flush, and the per-frame tick that drives everything
/// else. What each feature DOES lives in its own file - Track, Checks,
/// Abilities, Badges, Traps, Skips, Credits.
///
/// Architecture, each point a scar from the sibling cw4 project:
///
/// - Everything decidable without Unity lives in ALTTLArchipelago.Core, which
///   has zero references and is unit-tested in CI on a runner with no game.
///   This assembly stays thin.
/// - Client callbacks arrive on a socket thread and are marshalled to the main
///   thread through Hub. Unity is never touched off-thread.
/// - The MonoBehaviour injected into IL2CPP holds no state and no logic.
/// </summary>
[BepInPlugin(Guid, "A Little To The Left Archipelago", "0.3.0")]
public sealed class Plugin : BasePlugin
{
    internal const string Guid = "droha.alttl.archipelago";

    internal static ManualLogSource Logger { get; private set; } = null!;

    private static ConfigEntry<string> _host = null!;
    private static ConfigEntry<int> _port = null!;
    private static ConfigEntry<string> _slotName = null!;
    private static ConfigEntry<string> _password = null!;
    private static ConfigEntry<bool> _autoConnect = null!;
    private static ConfigEntry<int> _maxAttempts = null!;
    private static ConfigEntry<bool> _whyProbe = null!;

    /// <summary>
    /// Whether the badge "why is this card that colour" file probe is running.
    /// Off by default: it costs a filesystem check every second and answers a
    /// question only someone debugging the tracker is asking.
    /// </summary>
    internal static bool WhyProbeEnabled => _whyProbe.Value;

    private static Connection? _session;
    private static RetryPolicy _retry = new();

    /// <summary>Seconds left before the next retry, or 0 when not waiting.</summary>
    private static float _retryIn;

    internal static bool IsConnected => _session is { Connected: true };
    internal static bool AutoConnectEnabled => _autoConnect.Value;

    /// <summary>
    /// Set auto-connect and persist it.
    ///
    /// Set rather than toggle: the dialog offers an explicit OFF and ON, so a
    /// press means "make it this" and pressing the one already active is a
    /// no-op instead of flipping it away.
    /// </summary>
    internal static void SetAutoConnect(bool enabled)
    {
        if (_autoConnect.Value == enabled) return;
        _autoConnect.Value = enabled;
        Logger.LogInfo($"auto-connect {(enabled ? "on" : "off")}");
    }
    internal static string HostSetting => _host.Value;
    internal static int PortSetting => _port.Value;
    internal static string SlotNameSetting => _slotName.Value;
    internal static string PasswordSetting => _password.Value;

    public override void Load()
    {
        Logger = base.Log;

        // The kit takes its logger rather than reaching for ours; this is the
        // one line that replaces the reference it used to hold.
        Hub.OnError = m => Logger.LogError(m);
        Toasts.OnWarning = m => Logger.LogWarning(m);
        Toasts.OnInfo = m => Logger.LogInfo(m);
        Toasts.CanvasName = "ALTTLArchipelagoToasts";
        TypingGuard.OnWarning = m => Logger.LogWarning(m);

        // The kit ships with plain defaults; these are the Archipelago text
        // client's own colours, so a message here reads the same as the same
        // message there.
        Toasts.Plain = Toasts.HexColor(ApPalette.White);
        Toasts.Notice = Toasts.HexColor(ApPalette.Orange);

        _host = Config.Bind("Server", "Host", "archipelago.gg",
            "Server address, without the port. Editable in-game.");
        _port = Config.Bind("Server", "Port", 38281,
            "Server port, from the room page. Editable in-game.");
        _slotName = Config.Bind("Server", "SlotName", "",
            "Your player name in the multiworld, exactly as in your yaml.");
        _password = Config.Bind("Server", "Password", "",
            "Room password, or empty if there is none.");
        _autoConnect = Config.Bind("Server", "AutoConnect", true,
            "Connect automatically when the game launches, using the settings "
            + "above, so quitting and coming back rejoins the multiworld. Does "
            + "nothing until a slot name is set. Toggle it in the Archipelago "
            + "dialog.");
        _maxAttempts = Config.Bind("Server", "MaxRetries",
            RetryPolicy.DefaultMaxAttempts,
            "How many times to retry a lost connection before giving up. 0 "
            + "means keep trying until you press Cancel, which is the default "
            + "and what Archipelago's own client does. A refused login - bad "
            + "slot name or password - is never retried whatever this says.");

        _whyProbe = Config.Bind("Diagnostics", "BadgeWhyProbe", false,
            "Watch BepInEx/alttl-why.txt and explain the tracker badge for the "
            + "track position written into it. For debugging a badge that looks "
            + "wrong; costs a file check every second while on.");

        _retry = new RetryPolicy(_maxAttempts.Value);

        var harmony = new Harmony(Guid);
        // Patched class by class, each in its own try, rather than one
        // PatchAll over the assembly. A game update that renames one method
        // should cost one feature, not the whole plugin - and the failure line
        // is greppable so a battery can assert on its absence.
        var applied = new List<string>();
        var failed = new List<string>();

        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("save redirect", typeof(SaveRedirect)),
                     ("connection pane", typeof(ConnectionPane)),
                     ("track", typeof(Track)),
                     ("skips", typeof(Skips)),
                     ("hints", typeof(Hints)),
                     ("navigation", typeof(Navigation)),
                     ("daily guard", typeof(DailyGuard)),
                     ("title screen", typeof(TitleScreen)),
                 })
        {
            try
            {
                harmony.PatchAll(type);
                applied.Add(name);
            }
            catch (Exception e)
            {
                // A failed patch aborts the WHOLE class, so this is never "one
                // method degraded" - it is the entire feature off. That is why
                // the summary below lists what actually ended up live: a single
                // error line in a long log was easy to skim past while a
                // feature silently did nothing.
                Logger.LogError($"PATCH FAILED, {name} IS DISABLED: {e.Message}");
                failed.Add(name);
            }
        }

        // Resolved by name, so it runs after the attribute patches and reports
        // its own misses rather than aborting the class. See DailyGuard.
        try
        {
            DailyGuard.InstallOptional(harmony);
        }
        catch (Exception e)
        {
            Logger.LogError($"daily guard: optional patches failed: {e.Message}");
        }

        Plugin.Logger.LogInfo($"features live: {string.Join(", ", applied)}");
        if (failed.Count > 0)
        {
            Plugin.Logger.LogError($"FEATURES DISABLED: {string.Join(", ", failed)}");
        }

        foreach (var m in harmony.GetPatchedMethods())
        {
            Logger.LogInfo($"patched {m.DeclaringType?.Name}.{m.Name}");
        }

        ClassInjector.RegisterTypeInIl2Cpp<Ticker>();
        AddComponent<Ticker>();

        Logger.LogInfo("A Little To The Left Archipelago loaded");

        // The cache asks Plugin what to write rather than holding a copy, so
        // there is exactly one place the current session lives.
        SlotCache.Source = () => _slot == null || SaveRedirect.ActiveName == null
            ? null
            : CachedSession.Of(_slotName.Value, _seed, _slot,
                               Inventory.Received(), DateTime.Now);

        // Quietly, and only once - see AutoConnect below.
        //
        // Deliberately the ONLY route to an offline start: it is armed when
        // this attempt fails, and nowhere else. Arming it for auto-connect
        // being OFF as well was written first and taken back out - "do not
        // connect" is not "resume the last run", and with both arming it there
        // was no way left to reach the campaign save at all. Auto-connect off
        // is now the answer to "I want to play the normal game today".
        if (_autoConnect.Value) AutoConnect();
    }

    /// <summary>The seed of the live run, for the cache. Empty when idle.</summary>
    private static string _seed = "";

    /// <summary>Store what the pane collected, so it persists to the config.</summary>
    internal static void ApplySettings(string host, int port, string slot, string password)
    {
        if (!string.IsNullOrWhiteSpace(host)) _host.Value = host;
        if (port > 0) _port.Value = port;
        _slotName.Value = slot;
        _password.Value = password;
    }

    /// <summary>
    /// Connect, resetting the retry budget. Pressing Connect is the player
    /// saying the situation has changed, so it always gets a full set of tries.
    /// </summary>
    internal static void ConnectNow()
    {
        // A click is a deliberate act, so it retries even if the quiet launch
        // attempt already failed.
        _automatic = false;

        // A press during an attempt SUPERSEDES it. This used to return early
        // instead, which stopped a second retry chain running alongside the
        // first - two interleaved "attempt 2 of 5" sequences against the same
        // dead server - but did it by making the button do nothing at all, with
        // only a log line to show for the click. A button that ignores a press
        // is worse than one that fails.
        //
        // Bumping the generation gets the same protection honestly: the older
        // attempt is now stale and cannot schedule a retry or install itself,
        // so there is still only ever one live chain.
        _connectGen++;

        _retry.Reset();
        _retryIn = 0f;
        Attempt();
    }

    /// <summary>
    /// Stop trying, without tearing down a session there is not one of.
    ///
    /// This is what CANCEL does, and until now there was no way to do it: the
    /// pane offered only Connect and Disconnect, so a retry countdown could
    /// only be escaped by quitting the game.
    /// </summary>
    internal static void CancelConnect()
    {
        _connectGen++;              // any in-flight attempt is now stale
        _connecting = false;
        _retryIn = 0f;
        _automatic = false;
        _retry.Reset();
        Logger.LogInfo("connection attempt cancelled");
        ConnectionPane.RefreshStatus();
    }

    /// <summary>
    /// Bumped by every connect, cancel and disconnect. An attempt whose
    /// generation is stale must have no effect at all - it may not set the
    /// session, schedule a retry, or report a status - because by the time it
    /// returns the player has asked for something else.
    ///
    /// Without this a slow attempt that succeeded after CANCEL would quietly
    /// connect anyway, which is the one outcome the player explicitly declined.
    /// </summary>
    private static int _connectGen;

    /// <summary>True while an attempt is in flight, for the status line.</summary>
    private static bool _connecting;

    internal static bool IsConnecting => _connecting;

    /// <summary>
    /// Trying, or about to try: an attempt in flight or a backoff counting
    /// down. The pane offers CANCEL for exactly this state.
    /// </summary>
    internal static bool IsBusy => !IsConnected && (_connecting || _retryIn > 0f);

    /// <summary>
    /// The launch attempt. One try, no retries, and nothing alarming in the
    /// log if it fails.
    ///
    /// Starting the game with no server running is the ordinary case - the
    /// player may not be in a multiworld today at all. Retrying three times
    /// and announcing "gave up" would treat that as a fault. If it does not
    /// connect the pane simply says Not connected, and Connect is there.
    /// </summary>
    private static void AutoConnect()
    {
        _automatic = true;
        Attempt();
    }

    /// <summary>True while the in-flight attempt came from launch, not a click.</summary>
    private static bool _automatic;

    private static void Attempt()
    {
        // Still guarded on IsConnected - there is nothing to do when a session
        // is up - but no longer on _connecting. Superseding is the generation's
        // job now, and the old guard is what made the button look dead.
        if (IsConnected) return;

        var gen = _connectGen;

        if (string.IsNullOrWhiteSpace(_slotName.Value))
        {
            Logger.LogWarning("SlotName is not set");
            _retry.Failed("no slot name set");
            ConnectionPane.RefreshStatus();
            return;
        }

        Logger.LogInfo($"connecting to {_host.Value}:{_port.Value} as {_slotName.Value}");

        // Before anything can arrive on the new socket. The server replays the
        // whole item list on every connect, so without this the second
        // connection's replay lands on top of the first's and every count
        // doubles.
        Inventory.NewSession();

        // Opened here rather than at Ready: items start arriving before
        // slot_data is parsed, so a window that began at Ready would miss the
        // front of the replay.
        _replayQuietFor = ReplayQuiet;
        _replayed = 0;
        _replayedNames.Clear();

        var connection = new Connection(Hub.OnMainThread);
        // The connection is passed in, not read from _session: Ready fires
        // DURING the connect, before the field is assigned. Reading the field
        // here threw a NullReferenceException and, worse, made _session?.Seed
        // silently yield "" - which the log then reported as "room reported
        // no seed", a diagnosis that was never true.
        connection.Ready += slot => OnReady(connection, slot);
        connection.ItemReceived += item =>
        {
            Logger.LogInfo($"received item: {item.Name}");
            Inventory.Receive(item.Name);

            // Not the beaten token: the player gets one every time they finish
            // a puzzle, and announcing it would drown the messages that matter.
            if (item.Name == ALTTLArchipelago.Core.ItemNames.BeatenToken) return;

            // Quiet during the reconnect replay.
            //
            // Archipelago resends the WHOLE item history on every connect, so
            // announcing each one meant launching the game produced a wall of
            // "Received Cat Trap from Server" for cats sent hours ago. Nothing
            // is being re-delivered and nothing re-fires - the counts are
            // rebuilt from the list and the traps are already spent - but the
            // toasts made it look like it.
            //
            // Counted instead, and summarised once when the burst stops. A
            // genuinely new item that lands inside the window is folded into
            // that summary rather than lost.
            if (_replayQuietFor > 0f)
            {
                _replayed++;
                _replayedNames.Add(item.Name ?? "(unnamed)");
                return;
            }

            var painted = ALTTLArchipelago.Core.ApPalette.Paint(
                item.Name, ALTTLArchipelago.Core.ApPalette.ForItem(item.Flags));

            // "from X" only when someone else sent it, which is how the text
            // client reads: your own items are just found.
            var line = item.FromSelf || string.IsNullOrEmpty(item.From)
                ? $"Received {painted}"
                : $"Received {painted} from "
                  + ALTTLArchipelago.Core.ApPalette.Paint(
                      item.From, ALTTLArchipelago.Core.ApPalette.ForPlayer(false));

            Toasts.Show(line, Toasts.Plain);
        };
        connection.Dropped += OnDropped;

        // Off the main thread. The result comes back through the dispatcher,
        // so the game keeps rendering and the pane can say "Connecting...".
        _connecting = true;
        ConnectionPane.RefreshStatus();

        connection.ConnectAsync(_host.Value, _port.Value, _slotName.Value,
            _password.Value, error =>
            {
                if (gen != _connectGen)
                {
                    // A newer connect, a cancel or a disconnect happened while
                    // this was negotiating. Close it rather than installing a
                    // session nobody is asking for any more - and do not touch
                    // _connecting, which now belongs to the newer attempt.
                    Logger.LogInfo("discarding a superseded connection attempt");
                    if (string.IsNullOrEmpty(error)) connection.Disconnect();
                    return;
                }

                _connecting = false;
                if (string.IsNullOrEmpty(error))
                {
                    _session = connection;
                    _retry.Reset();
                }
                else
                {
                    _session = null;

                    // A refusal retrying cannot fix - a bad slot name, the
                    // wrong password, the wrong game, an incompatible version.
                    // Backing off and dialling again just delays telling the
                    // player something only they can put right.
                    if (connection.Terminal)
                    {
                        _automatic = false;
                        _retry.Reset();
                        _retryIn = 0f;
                        Logger.LogWarning($"not retrying: {error}");
                        Toasts.Show($"Archipelago refused the connection - {error}",
                            Toasts.Notice);
                    }
                    else if (_automatic)
                    {
                        _automatic = false;
                        _retry.Reset();
                        _retryIn = 0f;
                        Logger.LogInfo($"no server at launch ({error}); staying offline");
                        ArmOfflineStart("no server at launch");
                    }
                    else
                    {
                        ScheduleRetry(error);
                    }
                }
                ConnectionPane.RefreshStatus();
            });
    }

    private static void OnDropped(string reason)
    {
        // Losing a live session is a fault worth retrying, unlike failing to
        // find a server at launch.
        _automatic = false;
        Logger.LogWarning($"connection lost: {reason}");
        Toasts.Show("Disconnected from Archipelago - checks are kept and sent "
            + "when the server is back", Toasts.Notice);
        // The run's save stays redirected while we try to get back: the player
        // is still in the multiworld, just briefly unable to talk to it.
        ScheduleRetry(reason);
        ConnectionPane.RefreshStatus();
    }

    private static void ScheduleRetry(string error)
    {
        var delay = _retry.Failed(error);
        if (delay == null)
        {
            Logger.LogWarning(
                $"giving up after {_retry.Attempts} attempts: {_retry.LastError}");
            _retryIn = 0f;
            return;
        }
        var seconds = delay.Value;
        Logger.LogInfo($"retrying in {seconds:0.#}s "
            + $"({AttemptLabel()})");
        _retryIn = (float)seconds;
    }

    private static float _statusRefreshIn;

    /// <summary>Counts down to the next retry. Called once per frame.</summary>
    internal static void TickRetry(float deltaSeconds)
    {
        if (_retryIn <= 0f || IsConnected) return;

        _retryIn -= deltaSeconds;

        // Tick the visible countdown about once a second so the pane reads as
        // busy rather than stuck.
        _statusRefreshIn -= deltaSeconds;
        if (_statusRefreshIn <= 0f)
        {
            _statusRefreshIn = 1f;
            ConnectionPane.RefreshStatus();
        }

        if (_retryIn <= 0f)
        {
            _retryIn = 0f;
            Attempt();
        }
    }

    /// <summary>
    /// End the session and go back to the campaign save.
    ///
    /// Order matters: the retry countdown is cleared FIRST, otherwise the
    /// pending timer fires a moment later and reconnects the session the
    /// player just asked to leave.
    /// </summary>
    internal static void DisconnectNow(string why = "unspecified")
    {
        // WHO asked, in the log.
        //
        // A playtester reported being dropped to the DEFAULT title menu with
        // Archipelago reading "disconnected", after finishing a level, without
        // touching the pane - and having to reconnect by hand. The title menu
        // only reverts when Track.End() runs, and Track.End() is reachable
        // only from here, which in turn is reachable only from the pane button
        // and from Unload. A socket drop does NOT come through here: OnDropped
        // keeps the run redirected and retries.
        //
        // So the code says this cannot happen unasked, and the report says it
        // did. Rather than guess, every route now names itself. The next
        // occurrence will say whether a click arrived, the plugin unloaded, or
        // something else entirely is calling this.
        Logger.LogInfo($"disconnecting ({why})");
        _connectGen++;              // any in-flight attempt is now stale
        _retryIn = 0f;
        _connecting = false;
        _retry.Reset();

        _session?.Disconnect();
        _session = null;

        _slot = null;
        _seed = "";
        IsOffline = false;
        _offlineIn = 0f;

        // The cache FILE is deliberately kept - see SlotCache. Only the
        // pending write is dropped, because _slot is null now and there is
        // nothing left to describe.
        SlotCache.End();

        Toasts.Destroy();
        Track.End();
        TitleScreen.Refresh();
        Checks.End();
        Inventory.End();
        RunState.End();
        SaveRedirect.End();
        Logger.LogInfo($"disconnected ({why})");
        ConnectionPane.RefreshStatus();
    }

    /// <summary>
    /// "attempt 3" or "attempt 3 of 5", depending on the policy. An unlimited
    /// policy has no denominator, and printing MaxAttempts regardless would
    /// have read "attempt 3 of 0".
    /// </summary>
    private static string AttemptLabel()
        => _retry.IsUnlimited
            ? $"attempt {_retry.Attempts + 1}"
            : $"attempt {_retry.Attempts + 1} of {_retry.MaxAttempts}";


    /// <summary>One line describing the connection, for the pane.</summary>
    internal static string StatusLine()
    {
        if (_connecting)
        {
            return _retry.Attempts == 0
                ? "Connecting..."
                : $"Connecting... ({AttemptLabel()})";
        }
        if (_retryIn > 0f && !IsConnected)
        {
            return $"Retrying in {Math.Ceiling(_retryIn):0}s "
                + $"({AttemptLabel()})";
        }
        if (IsOffline)
        {
            // Says what is true and what happens next, because the state is
            // new and "Not connected" would read as the run being broken.
            var owed = Checks.Ledger.Owed.Count;
            return owed > 0
                ? $"Playing offline as {_slotName.Value} - {owed} check(s) "
                  + "waiting to be sent"
                : $"Playing offline as {_slotName.Value}";
        }
        return _retry.Describe(IsConnected, _slotName.Value);
    }

    /// <summary>
    /// Set when a check is earned, so the next tick sends it.
    ///
    /// Deferred rather than sent inline because a solve event arrives deep
    /// inside the game's own call stack, and a socket write from there blocks
    /// whatever was mid-animation.
    /// </summary>
    private static bool _checksDirty;

    private static float _sinceFlush;

    /// <summary>
    /// How often the owed queue is retried when a send did not clear it.
    ///
    /// The safety net behind the dirty flag: if a check is ever earned without
    /// setting it, or a send fails, this still gets the queue out within a few
    /// seconds. Checks that arrive late are a nuisance; checks that never
    /// arrive lose someone's progress.
    /// </summary>
    private const float FlushInterval = 5f;

    /// <summary>The seed, kept so the credits poll can read its goal.</summary>
    private static ALTTLArchipelago.Core.SlotData? _slot;

    /// <summary>The connected seed, or null when not in a run.</summary>
    internal static ALTTLArchipelago.Core.SlotData? Seed => _slot;

    internal static void TickCredits(float dt)
        => Credits.Tick(dt, _slot, _session);

    internal static void QueueCheckFlush() => _checksDirty = true;

    internal static void TickChecks(float dt)
    {
        _sinceFlush += dt;
        if (!_checksDirty && _sinceFlush < FlushInterval) return;

        _checksDirty = false;
        _sinceFlush = 0f;
        FlushChecks();
    }

    /// <summary>
    /// Send whatever is owed, and clear only what went.
    ///
    /// Nothing is dropped on failure: the ledger keeps the queue, and it is
    /// persisted with the save, so quitting mid-offline does not lose it.
    /// </summary>
    private static void FlushChecks()
    {
        if (!Checks.Active) return;

        // The cheap test first. OwedForSaving copies the set and sorts it, and
        // this runs on a timer whether or not anything is owed.
        if (Checks.Ledger.Owed.Count == 0) return;

        var owed = Checks.Ledger.OwedForSaving();

        // To disk FIRST, before any attempt to send, because the next thing
        // that happens might be the game closing.
        //
        // This used to be written only on the two paths that KNEW where they
        // stood: an explicit offline branch, and after a successful send. The
        // path between them lost checks. When a server disappears the client
        // goes on reporting Connected for a while, so the offline branch is
        // skipped, the send silently moves nothing, `sent.Count == 0` returns
        // early - and nothing was ever written. Measured 2026-09-04: killed the
        // server mid-puzzle, solved it, quit, restarted the server and
        // reconnected, and "Telescope - Solution 1" was simply gone, with the
        // run state cheerfully reporting 0 checks owed.
        //
        // Writing first costs a file write on a path that already touches the
        // network, and it makes the guarantee unconditional rather than
        // dependent on correctly detecting a disconnection - which is the part
        // that cannot be relied on.
        RunState.SetOwed(owed);

        if (_session == null || !_session.Connected) return;

        // One send in flight at a time. The flush runs on a timer, so without
        // this a slow acknowledgement means the same locations are sent again
        // underneath it. Harmless to the server - duplicates are explicitly
        // fine - but it churns the queue and the log.
        if (_sending) return;
        _sending = true;

        _session.SendChecksAsync(owed, accepted =>
        {
            _sending = false;
            if (accepted.Count == 0) return;

            // Only what the server actually took. Anything else stays owed and
            // goes out on the next flush.
            Checks.Ledger.Acknowledge(accepted);
            RunState.SetOwed(Checks.Ledger.OwedForSaving());
            Logger.LogInfo(
                $"checks: sent {accepted.Count}, {Checks.Ledger.Owed.Count} still owed");
        });
    }

    /// <summary>A send is awaiting the server's acknowledgement.</summary>
    private static bool _sending;

    /// <summary>How long the item replay is given to finish, in seconds.</summary>
    private const float ReplayQuiet = 3f;

    private static float _replayQuietFor;
    private static int _replayed;

    /// <summary>
    /// The names behind the count, because the count on its own reads wrong.
    ///
    /// "Restored 1 item(s) from the server" on a brand new run looks like
    /// leftover state from a previous game - droha asked whether it was a
    /// clean run, and it was: one starting ability, replayed on connect. A
    /// player cannot tell those apart from a number, and should not have to.
    /// </summary>
    private static readonly List<string> _replayedNames = new();

    /// <summary>
    /// Close the quiet window once the replay stops, and say what arrived.
    ///
    /// One line instead of one per item. Silence would be worse: a player
    /// reconnecting deserves to know their things came back.
    /// </summary>
    internal static void TickItemReplay(float dt)
    {
        if (_replayQuietFor <= 0f) return;

        _replayQuietFor -= dt;
        if (_replayQuietFor > 0f) return;

        _replayQuietFor = 0f;
        if (_replayed <= 0) return;

        var n = _replayed;
        _replayed = 0;

        // Aggregated, because a replay is mostly duplicates - "Progressive
        // Puzzle Pack x7" says something, seven identical lines do not.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in _replayedNames)
        {
            counts.TryGetValue(name, out var seen);
            counts[name] = seen + 1;
        }
        _replayedNames.Clear();

        var parts = new List<string>();
        foreach (var pair in counts)
        {
            parts.Add(pair.Value > 1 ? $"{pair.Key} x{pair.Value}" : pair.Key);
        }
        parts.Sort(StringComparer.Ordinal);
        var listed = parts.Count > 0 ? string.Join(", ", parts) : "nothing";

        // "RESTORED" IS ONLY TRUE IF THERE WAS SOMETHING TO RESTORE.
        //
        // The server replays everything the slot holds on every connect, so
        // this same code path runs twice over for two quite different events:
        // handing a brand new run its starting kit, and giving a reconnecting
        // player their progress back. Calling both "restored" made a fresh run
        // read like leftover state from a previous game - which is exactly the
        // doubt it caused, twice, before it was worth fixing.
        //
        // Nothing collected means nothing to restore, so what arrived is what
        // the run STARTS with.
        var fresh = Checks.Ledger.Collected.Count == 0;
        var verb = fresh ? "starting with" : "restored on connect";

        Logger.LogInfo($"items: {n} {verb}: {listed}");

        // The toast gets the first few and a count for the rest. A wall of
        // text on screen is its own kind of unreadable, and the log has the
        // full list for anyone who wants it.
        var shown = parts.Count <= 4
            ? listed
            : string.Join(", ", parts.GetRange(0, 4)) + $", +{parts.Count - 4} more";
        Toasts.Show(fresh
            ? $"Starting with {n} item(s): {shown}"
            : $"Restored {n} item(s): {shown}", Toasts.Notice);
    }

    // ------------------------------------------------------------------
    //  Offline play
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the run on screen came from the cache and no server has been
    /// reached. Checks are still earned; they queue in RunState until one is.
    /// </summary>
    internal static bool IsOffline { get; private set; }

    /// <summary>
    /// Seconds until the offline start runs, or 0 when none is pending.
    ///
    /// Not started on the spot, because both places that arm it can fire
    /// within milliseconds of Load - a refused connection to localhost comes
    /// back almost instantly - and the track has a level select to decorate.
    /// OnReady gets away with running that early only because a real server
    /// handshake takes longer than the game's boot.
    /// </summary>
    private static float _offlineIn;

    private static string _offlineWhy = "";

    private static void ArmOfflineStart(string why)
    {
        if (_slot != null || _offlineIn > 0f) return;
        _offlineWhy = why;
        _offlineIn = 2f;
    }

    /// <summary>Counts down to the offline start. Called once per frame.</summary>
    internal static void TickOfflineStart(float deltaSeconds)
    {
        if (_offlineIn <= 0f) return;

        _offlineIn -= deltaSeconds;
        if (_offlineIn > 0f) return;
        _offlineIn = 0f;

        // A server answered while this was counting down. It wins: its data is
        // current and the cache is only ever a copy of it.
        if (_slot != null || IsConnected || _connecting) return;

        StartOffline(_offlineWhy);
    }

    /// <summary>
    /// Bring the last run back with no server.
    ///
    /// Everything here mirrors OnReady, minus the two steps that need a
    /// socket: the server's own check list is not adopted, and nothing is
    /// sent. Both are picked up on the next connect - the ledger keeps what
    /// was earned, RunState persists it, and FlushChecks pushes it.
    /// </summary>
    private static void StartOffline(string why)
    {
        var cache = SlotCache.Load();
        if (cache == null)
        {
            Logger.LogInfo($"{why}, and no cached run to fall back on");
            return;
        }

        if (!cache.IsFor(_slotName.Value))
        {
            // Not an error. Changing the slot name is how a player says
            // "different multiworld", and starting someone else's run because
            // the file happened to be there would be far worse than doing
            // nothing.
            Logger.LogInfo(
                $"{why}; the cached run is for slot '{cache.SlotName}' but this "
                + $"install is set to '{_slotName.Value}', so not starting it");
            return;
        }

        var problems = cache.Problems();
        if (problems.Count > 0)
        {
            Logger.LogWarning(
                $"{why}, and the cached run is unusable: {string.Join("; ", problems)}");
            return;
        }

        var slot = cache.Slot;
        _seed = cache.Seed;
        SaveRedirect.Begin(cache.SlotName, cache.Seed);

        Track.Begin(slot);
        TitleScreen.Refresh();
        _slot = slot;

        // Before Traps.Reset, which reads how many have already gone off.
        RunState.Begin(SaveRedirect.ActiveName ?? "run");

        // The items BEFORE Begin, which is the mirror image of the online
        // order. Online they arrive on the socket ahead of slot_data and
        // Begin recounts whatever landed; here the whole list is already
        // known, so it is put in place first and Begin's recount finds it.
        Inventory.RestoreReceived(cache.Items);
        Inventory.Begin(slot);

        Abilities.Reset();
        Badges.Reset();
        Credits.Reset();
        Traps.Reset();
        Checks.Begin(slot);

        // The Beaten tokens, then anything earned offline last time. No
        // AdoptServerChecks: there is no server to have a list. That means the
        // ledger knows only what this install has seen, which is exactly right
        // - a check the server already has is re-sent on the next connect, and
        // Archipelago says duplicate sends are fine.
        Checks.Ledger.RestoreLocal(RunState.Beaten());
        Checks.Ledger.RestoreOwed(RunState.Owed());
        FlushChecks();

        IsOffline = true;

        Logger.LogInfo(
            $"offline: resumed slot '{cache.SlotName}' seed {cache.Seed} "
            + $"saved {cache.SavedAt} - {slot.Slots.Count} puzzles, "
            + $"{Inventory.PacksHeld} pack(s) held, {cache.Items.Count} item(s)");
        Toasts.Show(
            $"Playing offline - {Checks.LevelsBeaten} of {slot.LevelsToBeat} beaten. "
            + "Checks are kept and sent when you connect.", Toasts.Notice);

        ConnectionPane.RefreshStatus();
    }

    /// <summary>
    /// What the seed contains, on the main thread.
    ///
    /// The save is redirected HERE rather than at connect, because only now is
    /// the room's seed known - and the seed is what stops two multiworlds
    /// played under one slot name sharing a save.
    /// </summary>
    private static void OnReady(Connection connection, SlotData slot)
    {
        // The server's seed string if it gave one, otherwise a fingerprint of
        // the draw itself. RoomState.Seed came back empty on every connection
        // tested here, and without a fallback every multiworld played under
        // one slot name would share a single save file.
        var seed = connection.Seed ?? "";
        if (string.IsNullOrEmpty(seed))
        {
            seed = slot.Fingerprint();
            Logger.LogInfo($"room reported no seed; using the draw's fingerprint {seed}");
        }
        _seed = seed;

        // Any pending offline start is now moot, and a live offline run is
        // simply taken over: everything below re-derives the whole session
        // from the server, which is authoritative. The cached run and this one
        // are the same run whenever the seeds match, and when they do not, the
        // save is re-pointed at the right file by the line below.
        _offlineIn = 0f;
        if (IsOffline)
        {
            Logger.LogInfo($"a server answered; the offline run is taken over "
                + $"by seed {seed}");
            IsOffline = false;
        }

        SaveRedirect.Begin(_slotName.Value, seed);

        // The track goes up as soon as the seed is known, before any item has
        // arrived, so the player sees their run rather than the campaign.
        Track.Begin(slot);
        TitleScreen.Refresh();

        // Before Checks, so a replayed item list has already opened the track
        // by the time the first flush looks at what is reachable.
        _slot = slot;

        // The run state FIRST: it carries how many traps have already gone off
        // and how many skips were spent, and Traps.Reset reads that. Resetting
        // before it was loaded left the count at zero, so every cat in the
        // run's history fired again on each login.
        RunState.Begin(SaveRedirect.ActiveName ?? "run");

        Inventory.Begin(slot);
        Abilities.Reset();
        Badges.Reset();
        Credits.Reset();
        Traps.Reset();
        Checks.Begin(slot);

        // The Beaten tokens first. They are event locations, so neither the
        // owed queue nor the server's list carries them - this is their only
        // route back, and the credits goal is counted from them.
        Checks.Ledger.RestoreLocal(RunState.Beaten());

        // Anything earned offline last time, before the server's own list is
        // adopted - so a check we owe stays owed even if the server has it.
        Checks.Ledger.RestoreOwed(RunState.Owed());
        // The server's list first, so a check it already has is not re-sent on
        // every login - but it is adopted as COLLECTED, never as acknowledged,
        // so anything earned offline stays owed.
        Checks.AdoptServerChecks(connection.ServerChecks());
        FlushChecks();

        // Written immediately, not left to the debounce. This is the moment
        // the cache is most worth having and least likely to be written
        // otherwise: a player who connects, sees their run and quits within
        // two seconds should still be able to start offline next time.
        SlotCache.Flush();

        Toasts.Show($"Connected to Archipelago - {slot.Slots.Count} puzzles", Toasts.Notice);

        Logger.LogInfo($"connected. {slot.Slots.Count} puzzles, "
            + $"{slot.PackTotal} packs of {slot.PackSize}, "
            + $"beat {slot.LevelsToBeat} to unlock the credits");
        Logger.LogInfo($"ability locks {(slot.AbilityLocks ? "on" : "off")}, "
            + $"{slot.Abilities.Count} abilities, starting with "
            + $"[{string.Join(", ", slot.StartingAbilities)}]");

        ConnectionPane.RefreshStatus();
    }

    public override bool Unload()
    {
        DisconnectNow("the plugin is unloading");
        return true;
    }
}

/// <summary>
/// Drains the main-thread queue and counts down retries. Deliberately empty of
/// everything else - no state, no logic, nothing IL2CPP must marshal beyond
/// the call itself.
/// </summary>
public sealed class Ticker : MonoBehaviour
{
    public Ticker(IntPtr pointer) : base(pointer) { }

    /// <summary>
    /// Failures already reported, so a step that throws every frame says so
    /// once instead of sixty times a second.
    /// </summary>
    private static readonly HashSet<string> _reported = new();

    /// <summary>
    /// Run one tick step, and do not let it take the rest of the frame with it.
    ///
    /// This list used to be nineteen bare calls in a row with no try anywhere.
    /// One throw took out every step BELOW it, silently and for as long as the
    /// condition lasted - and the order matters: Toasts.Tick is tenth, so a
    /// fault in Hub, Checks.TickAudit or Abilities.Tick stopped toasts
    /// appearing while leaving everything above them working. "The toast
    /// message doesn't always pop up" is exactly what that looks like from the
    /// outside, with nothing in the log to say why.
    ///
    /// Isolating the steps does not fix a broken step; it stops one broken step
    /// from presenting as several unrelated bugs, and names it in the log.
    /// </summary>
    private static void Step(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            if (_reported.Add(name))
            {
                Plugin.Logger.LogError(
                    $"tick: '{name}' threw and is being skipped from here on "
                    + $"(reported once): {e}");
            }
        }
    }

    private void Update()
    {
        var dt = Time.unscaledDeltaTime;

        Step("hub", () => Hub.Tick());
        Step("retry", () => Plugin.TickRetry(dt));
        Step("checks", () => Plugin.TickChecks(dt));
        Step("offline start", () => Plugin.TickOfflineStart(dt));
        Step("slot cache", () => SlotCache.Tick(dt));
        Step("item replay", () => Plugin.TickItemReplay(dt));
        Step("check audit", () => Checks.TickAudit(dt));
        Step("empty level watch", () => Checks.TickEmptyLevelWatch(dt));
        Step("abilities", () => Abilities.Tick(dt));
        Step("toasts", () => Toasts.Tick(dt));
        Step("track integrity", () => Track.TickTrackIntegrity(dt));
        Step("badges", () => Badges.Tick(dt));
        Step("badge reasons", () => Badges.TickWhy(dt));
        Step("credits", () => Plugin.TickCredits(dt));
        Step("traps", () => Traps.Tick(dt));
        Step("backgrounds", () => Backgrounds.Tick());
        Step("menu counts", () => Navigation.TickMenuCounts());
        Step("credits card", () => Track.TickCreditsCard());
        Step("track scroll", () => Track.TickScroll(dt));
        Step("daily rescue", () => DailyGuard.TickRescue(dt));
        Step("connected tag", () => Badges.TickConnectedTag());
        Step("overview dots", () => Badges.TickOverviewDots());
        Step("typing guard",
            () => TypingGuard.Tick(ConnectionPane.FocusedField, ConnectionPane.FocusNext));
    }
}
