using System;
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
        harmony.PatchAll(typeof(SaveRedirect));
        harmony.PatchAll(typeof(ConnectionPane));
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

        var connection = new Connection(Hub.OnMainThread);
        connection.Ready += OnReady;
        connection.ItemReceived += name => Logger.LogInfo($"received item: {name}");
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
                    if (_automatic)
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
    /// What the seed contains, on the main thread.
    ///
    /// The save is redirected HERE rather than at connect, because only now is
    /// the room's seed known - and the seed is what stops two multiworlds
    /// played under one slot name sharing a save.
    /// </summary>
    private static void OnReady(SlotData slot)
    {
        // The server's seed string if it gave one, otherwise a fingerprint of
        // the draw itself. RoomState.Seed came back empty on every connection
        // tested here, and without a fallback every multiworld played under
        // one slot name would share a single save file.
        var seed = _session?.Seed ?? "";
        if (string.IsNullOrEmpty(seed))
        {
            seed = slot.Fingerprint();
            Logger.LogInfo($"room reported no seed; using the draw's fingerprint {seed}");
        }
        SaveRedirect.Begin(_slotName.Value, seed);

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
        TypingGuard.Tick(ConnectionPane.FocusedField, ConnectionPane.FocusNext);
    }
}
