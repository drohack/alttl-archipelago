using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
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
/// SCOPE RIGHT NOW: connect from the main menu, keep the connection up, and
/// keep the run out of the player's campaign save. Nothing yet touches the
/// level select or the puzzles - that is the next piece of work, and it comes
/// after this one because it is the part that writes progress, and progress
/// must have somewhere safe to go first.
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
[BepInPlugin(Guid, "A Little To The Left Archipelago", "0.1.0")]
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
            "How many times to retry a lost or refused connection before "
            + "giving up and waiting for you to press Connect.");

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
                     ("navigation", typeof(Navigation)),
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

        // Quietly, and only once - see AutoConnect below.
        if (_autoConnect.Value) AutoConnect();
    }

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

        // Ignore a repeat press while an attempt is already running. Spamming
        // Connect otherwise started a second retry chain alongside the first,
        // and the log showed two independent "attempt 2 of 5" sequences
        // interleaving against the same dead server.
        if (_connecting)
        {
            Logger.LogInfo("already connecting; ignoring");
            return;
        }

        _retry.Reset();
        _retryIn = 0f;
        Attempt();
    }

    /// <summary>True while an attempt is in flight, for the status line.</summary>
    private static bool _connecting;

    internal static bool IsConnecting => _connecting;

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
        if (IsConnected || _connecting) return;

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
            + $"(attempt {_retry.Attempts + 1} of {_retry.MaxAttempts})");
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
    internal static void DisconnectNow()
    {
        _retryIn = 0f;
        _connecting = false;
        _retry.Reset();

        _session?.Disconnect();
        _session = null;

        _slot = null;
        Toasts.Destroy();
        Track.End();
        TitleScreen.Refresh();
        Checks.End();
        Inventory.End();
        RunState.End();
        SaveRedirect.End();
        Logger.LogInfo("disconnected");
        ConnectionPane.RefreshStatus();
    }

    /// <summary>One line describing the connection, for the pane.</summary>
    internal static string StatusLine()
    {
        if (_connecting)
        {
            return _retry.Attempts == 0
                ? "Connecting..."
                : $"Connecting... (attempt {_retry.Attempts + 1} of {_retry.MaxAttempts})";
        }
        if (_retryIn > 0f && !IsConnected)
        {
            return $"Retrying in {Math.Ceiling(_retryIn):0}s "
                + $"(attempt {_retry.Attempts + 1} of {_retry.MaxAttempts})";
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
        DisconnectNow();
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

    private void Update()
    {
        Hub.Tick();
        Plugin.TickRetry(Time.unscaledDeltaTime);
        Plugin.TickChecks(Time.unscaledDeltaTime);
        Checks.TickAudit(Time.unscaledDeltaTime);
        Checks.TickEmptyLevelWatch(Time.unscaledDeltaTime);
        Abilities.Tick(Time.unscaledDeltaTime);
        Toasts.Tick(Time.unscaledDeltaTime);
        Track.TickTrackIntegrity(Time.unscaledDeltaTime);
        Badges.Tick(Time.unscaledDeltaTime);
        Badges.TickWhy(Time.unscaledDeltaTime);
        Plugin.TickCredits(Time.unscaledDeltaTime);
        Traps.Tick();
        Track.TickCreditsCard();
        TypingGuard.Tick(ConnectionPane.FocusedField, ConnectionPane.FocusNext);
    }
}
