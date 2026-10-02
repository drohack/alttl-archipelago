using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ALTTLModKit;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;


/// <summary>
/// Research probe for A Little To The Left: it measures what the game does,
/// drives it from a command file, and logs gameplay events as they fire.
/// docs/dev/devtools.md lists every command.
///
/// IT DOES CHANGE THE GAME, and says so rather than claiming otherwise: it
/// keeps the game running while unfocused (Application.runInBackground), by
/// default stops it reading input while unfocused (IgnoreInputWhenUnfocused,
/// ZoomFocusGuard), can skip the game's focus pause (KeepRunningWhenUnfocused,
/// PauseTrace) and hold the audio at zero (MuteAudio), and a handful of
/// commands write the save (setres, unlockto, marksolved, resetlevels).
/// Never shipped with the randomizer.
/// </summary>
[BepInPlugin(Guid, "ALTTL Dev Tools", "0.1.0")]
public class DevToolsPlugin : BasePlugin
{
    public const string Guid = "droha.alttl.devtools";

    internal static new ManualLogSource Log = null!;

    /// <summary>
    /// Which Windows virtual desktop to move the game's windows to at startup,
    /// 1-based in Task View order. 0 leaves them wherever Windows put them,
    /// which is whichever desktop happened to be active at launch.
    /// </summary>
    internal static int TargetDesktop;

    /// <summary>
    /// Stop the game acting on keyboard and mouse input while its window is
    /// not focused (Rewired's ignoreInputWhenAppNotInFocus, plus
    /// ZoomFocusGuard for the camera zoom). ON by default: typing into
    /// another window during a scripted run otherwise lands in the game.
    /// </summary>
    internal static bool IgnoreInputUnfocused;

    /// <summary>
    /// Raise the game window above the BepInEx console at startup.
    ///
    /// OFF by default, and it must stay that way: SetForegroundWindow takes
    /// focus from whatever the person at the keyboard is doing on whichever
    /// desktop they are on, which makes a scripted launch genuinely disruptive
    /// to someone working alongside it. Turn it on only for a session where
    /// the game is meant to be the foreground window.
    /// </summary>
    internal static bool RaiseWindow;

    /// <summary>
    /// Write a per-event transcript to alttl-watch.log. Off by default because
    /// ObjectPlaced fires hundreds of times per level load.
    /// </summary>
    internal static bool WatchEvents;

    /// <summary>
    /// Silence the game.
    ///
    /// A harness run plays real sessions for fifteen minutes with the music
    /// and the effects going, which is exactly as pleasant as it sounds when
    /// it happens on the machine someone is working at. droha: "can you set
    /// the tests to have the music and sf muted?"
    ///
    /// AudioListener.volume, NOT the player's own volume settings. Those live
    /// inside the encoded save alongside actual progress, so muting by editing
    /// them would mean decoding and rewriting a save file to change a
    /// preference - a great deal of risk for a quiet room, and it would leave
    /// the player's sliders moved afterwards. The listener is the engine's own
    /// master tap, it is not persisted anywhere, and it is forgotten the
    /// moment the process ends.
    ///
    /// Off by default: a player running DevTools by hand should hear the game.
    /// The harnesses turn it on, and harness_env puts the setting back.
    /// </summary>
    internal static bool MuteAudio;

    /// <summary>
    /// Skip the game's own pause when its window loses focus. The game pauses
    /// itself on focus loss and a paused game holds every gameplay event, so
    /// a scripted run stalled the moment someone clicked another window: the
    /// 0.4.1 DLC gate rerun lost its arrow session that way (2026-09-25).
    /// Off by default; the harnesses turn it on and harness_env puts it back.
    /// </summary>
    internal static bool KeepRunningUnfocused;

    public override void Load()
    {
        Log = base.Log;

        TargetDesktop = Config.Bind(
            "Window",
            "TargetVirtualDesktop",
            0,
            "Move the game window and the BepInEx console to this Windows virtual "
            + "desktop at startup, 1-based in Task View order. 0 leaves them alone. "
            + "Windows otherwise puts a new window on whichever desktop was active "
            + "when it was created, which for a scripted launch is arbitrary.").Value;

        IgnoreInputUnfocused = Config.Bind(
            "Window",
            "IgnoreInputWhenUnfocused",
            true,
            "Stop the game acting on the keyboard and mouse while its window "
            + "does not have focus. The game keeps RUNNING - a scripted run "
            + "must not stall just because the window is in the background - "
            + "it simply stops treating a keystroke meant for another window "
            + "as gameplay input. Without this, typing elsewhere during a test "
            + "run lands in the game: menus open, levels get reset, and the "
            + "run fails for a reason that is nowhere in the log.").Value;

        KeepRunningUnfocused = Config.Bind(
            "Window",
            "KeepRunningWhenUnfocused",
            false,
            "Skip the game's own pause when its window loses focus. A paused "
            + "game holds every gameplay event, so a scripted run stalls the "
            + "moment another window is clicked. For scripted runs; off for "
            + "play.").Value;

        RaiseWindow = Config.Bind(
            "Window",
            "RaiseWindowAtStartup",
            false,
            "Bring the game window to the foreground at startup. Off by default "
            + "because it steals focus from whatever else is being worked on, on "
            + "whichever virtual desktop that happens to be.").Value;

        MuteAudio = Config.Bind(
            "Debug",
            "MuteAudio",
            false,
            "Silence the game by holding AudioListener.volume at zero. For "
            + "scripted runs, which otherwise play music and effects for the "
            + "length of the session. Does not touch the player's own volume "
            + "settings, which live in the save file.").Value;

        WatchEvents = Config.Bind(
            "Debug",
            "WatchEvents",
            false,
            "Write every gameplay event to BepInEx/alttl-watch.log. Useful for "
            + "diagnosing what the game signals and when, but ObjectPlaced alone "
            + "fires hundreds of times per level load, so leave it off for sweeps.").Value;
        ClassInjector.RegisterTypeInIl2Cpp<DevToolsBehaviour>();
        var harmony = new Harmony(Guid);
        // PatchAll(Type) registers ONE class. A new [HarmonyPatch] class that
        // nobody adds here is silently never applied - which is not a
        // hypothetical: RegistrationLog was written, shipped, and reported
        // "zero registrations across 111 levels", and that was read as
        // evidence about the game when it was really evidence that the patch
        // did not exist at runtime.
        harmony.PatchAll(typeof(RegistrationLog));
        harmony.PatchAll(typeof(ZoomFocusGuard));
        harmony.PatchAll(typeof(PauseTrace));
        harmony.PatchAll(typeof(StarTrace));
        foreach (var m in harmony.GetPatchedMethods())
        {
            Log.LogInfo($"patched {m.DeclaringType?.Name}.{m.Name}");
        }

        var go = new GameObject("ALTTLDevTools");
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<DevToolsBehaviour>();

        Log.LogInfo("ALTTL dev tools loaded");
    }
}

public partial class DevToolsBehaviour : MonoBehaviour
{
    // Required for a MonoBehaviour injected into the IL2CPP domain.
    public DevToolsBehaviour(IntPtr ptr) : base(ptr) { }

    private const int SettleFrames = 240;

    private int _frames;

    private bool _desktopMoved;

    private readonly DataTable _dataTable = new();

    private bool _listenersAttached;

    private float _nextCommandPoll;

    private float _lastTimeScale = -1f;

    /// <summary>
    /// Log `time: timeScale changed X -> Y` whenever the clock moves. The
    /// setter is an engine call, so a patch would only see the mods' own
    /// writes, never the game's; one comparison a frame sees them all.
    /// </summary>
    private void WatchTimeScale()
    {
        var scale = UnityEngine.Time.timeScale;
        if (scale == _lastTimeScale) return;
        if (_lastTimeScale >= 0f)
        {
            DevToolsPlugin.Log.LogInfo(
                $"time: timeScale changed {_lastTimeScale} -> {scale} | {PauseTrace.State()}");
        }
        _lastTimeScale = scale;
    }

    private static string GameDir => Path.GetDirectoryName(Application.dataPath)!;

    private static string CommandFile => Path.Combine(GameDir, "BepInEx", "alttl-devtools-commands.txt");

    private static string DumpFile => Path.Combine(GameDir, "BepInEx", "alttl-dump.json");

    private void Update()
    {
        _frames++;

        // Both the Unity window and the BepInEx console exist by now. Done
        // from here rather than Load() because at load time the window may
        // not have been created yet.
        // Keep ticking while another window has focus.
        //
        // Unity stops calling Update on a backgrounded player, which for a
        // scripted test means the command file is never polled and the game
        // looks hung. That only showed up once the startup focus grab was
        // turned off: with the window sitting unfocused on another virtual
        // desktop, nothing ran at all.
        if (!Application.runInBackground) Application.runInBackground = true;

        if (!_desktopMoved && _frames > 60)
        {
            _desktopMoved = true;
            VirtualDesktop.MoveGameTo(DevToolsPlugin.TargetDesktop, m => DevToolsPlugin.Log.LogInfo(m));
            if (DevToolsPlugin.RaiseWindow)
            {
                // After any desktop move, so the raise is not undone by it.
                VirtualDesktop.FocusGameWindow(m => DevToolsPlugin.Log.LogInfo(m));
            }
        }

        TickWatch();
        TickFlip();
        TickCatEvent();
        WatchTimeScale();

        // Re-asserted rather than set once. The game raises the listener back
        // to 1 on its own at least at startup, and a mute that loses a race
        // with that is worse than none - it sounds like the setting does not
        // work. One float comparison per frame is nothing.
        if (DevToolsPlugin.MuteAudio
            && UnityEngine.AudioListener.volume != 0f)
        {
            UnityEngine.AudioListener.volume = 0f;
        }

        var gm = GameManager.Instance;
        if (gm == null) return;

        if (!_listenersAttached)
        {
            TryAttachListeners();
        }

        ApplyFocusRule();

        // A sweep owns the game while it runs; the command file is still
        // read, but only for `stop` (see PollCommands).
        var busy = _surveying || _dataTable.Running || _sweeping;
        if (_surveying) SafeRun("survey step", SurveyStep);
        else if (_dataTable.Running) SafeRun("levelsweep step", _dataTable.Tick);
        else if (_sweeping) SafeRun("gensweep step", GenSweepStep);
        else SafeRun("boot check", TickBootCheck);

        if (Time.unscaledTime >= _nextCommandPoll)
        {
            _nextCommandPoll = Time.unscaledTime + 0.5f;
            PollCommands(busy);
        }
    }

    // ---------------------------------------------------------------- events

    private void TryAttachListeners()
    {
        try
        {
            // The per-controller and per-solution transcript. Off by default:
            // ObjectPlaced alone fires hundreds of times per level load, and
            // writing every one to disk slowed a full level sweep to a crawl.
            // Turn WatchEvents on in the config when diagnosing.
            if (DevToolsPlugin.WatchEvents)
            {
                Watch<GameEventManager.GameEvent_ObjectControllerSolved>("ObjectControllerSolved");
                Watch<GameEventManager.GameEvent_SolutionChanged>("SolutionChanged");
                Watch<GameEventManager.GameEvent_LevelComplete>("LevelComplete");
                Watch<GameEventManager.GameEvent_ObjectPlaced>("ObjectPlaced");
            }

            // Always on, unlike WatchEvents: it fires only when a part becomes
            // solved, and it is how a hand test sees which part checks can
            // fire without needing the level to be a slot in a seed.
            Il2CppSystem.Action<GameEventManager.GameEventData> onSolved =
                (Action<GameEventManager.GameEventData>)LogPartSolved;
            KeepAlive.Add(onSolved);
            GameEventManager.AddEventListener<GameEventManager.GameEvent_ObjectControllerSolved>(onSolved);

            Listen<GameEventManager.GameEvent_LevelSelected>("LevelSelected");
            Listen<GameEventManager.GameEvent_LevelComplete>("LevelComplete");
            Listen<GameEventManager.GameEvent_LevelCompleteEarly>("LevelCompleteEarly");
            Listen<GameEventManager.GameEvent_LevelSkipped>("LevelSkipped");
            Listen<GameEventManager.GameEvent_LevelRandomized>("LevelRandomized");
            Listen<GameEventManager.GameEvent_LevelHintTaken>("LevelHintTaken");
            Listen<GameEventManager.GameEvent_LevelExited>("LevelExited");
            Listen<GameEventManager.GameEvent_CampaignFinished>("CampaignFinished");
            Listen<GameEventManager.GameEvent_DLCFinished>("DLCFinished");
            _listenersAttached = true;
            DevToolsPlugin.Log.LogInfo("event listeners attached");
        }
        catch (Exception e)
        {
            // GameEventManager may not be initialised on the very first frames.
            if (_frames > SettleFrames)
            {
                _listenersAttached = true;
                DevToolsPlugin.Log.LogWarning($"could not attach listeners: {e.Message}");
            }
        }
    }

    // The IL2CPP side only holds the delegate through a weak wrapper, so a
    // listener that is not rooted on the managed side stops firing as soon as
    // the GC runs. Keeping them here is what makes the subscription stick.
    private static readonly List<Il2CppSystem.Action<GameEventManager.GameEventData>> KeepAlive = new();

    /// <summary>The WatchEvents transcript listener - writes to alttl-watch.log.</summary>
    private static void Watch<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(data => LogWatch(label, data));
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    private static string WatchLog => Path.Combine(GameDir, "BepInEx", "alttl-watch.log");

    /// <summary>
    /// Appends one line per gameplay event with whatever identifying detail it
    /// carries. Written to its own file so a hand-play session produces a
    /// readable transcript instead of being buried in the BepInEx log.
    /// </summary>
    private static void LogWatch(string label, GameEventManager.GameEventData data)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append("  ").Append(label.PadRight(26));

            var oc = data?.ObjectController;
            if (oc != null)
            {
                sb.Append(" controller=\"").Append(Str(() => oc.gameObject.name)).Append('"');
                sb.Append(" type=").Append(Str(() => oc.GetIl2CppType().Name));
                sb.Append(" solutionIndex=").Append(Str(() => oc.SolutionId.ToString()));
                sb.Append(" isSolved=").Append(Str(() => oc.IsSolved.ToString()));
            }

            var lo = data?.LevelObject;
            if (lo != null) sb.Append(" object=\"").Append(Str(() => lo.gameObject.name)).Append('"');

            var li = data?.LevelInterface;
            if (li != null)
            {
                sb.Append(" level=\"").Append(Str(() => li.LevelId)).Append('"');
                sb.Append(" found=").Append(Str(() => li.NumSolutionsFound.ToString()));
                sb.Append('/').Append(Str(() => li.SolutionCount.ToString()));
            }

            if (!string.IsNullOrEmpty(data?.SolutionId))
                sb.Append(" SolutionId=\"").Append(data.SolutionId).Append('"');

            File.AppendAllText(WatchLog, sb.ToString() + Environment.NewLine);
            DevToolsPlugin.Log.LogInfo(sb.ToString());
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"watch log failed for {label}: {e.Message}");
        }
    }

    private static void Listen<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(data => LogEvent(label, data));
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    /// <summary>Parts already reported for the level on screen; the game
    /// re-raises the event for a solved part, so each is logged once.</summary>
    private static readonly HashSet<string> _partsSolved = new(StringComparer.Ordinal);
    private static string _partsLevel = "";

    private static void LogPartSolved(GameEventManager.GameEventData data)
    {
        try
        {
            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            var level = Str(() => li!.LevelId);
            var name = Str(() => data.ObjectController.gameObject.name);
            // Keyed on the level clone, new on every load, so a replay logs
            // again. Not the LevelInterface: it can outlive a load.
            var instance = Str(() => li!.Level.Pointer.ToString());
            if (instance != _partsLevel) { _partsLevel = instance; _partsSolved.Clear(); }
            if (!_partsSolved.Add(name)) return;
            DevToolsPlugin.Log.LogInfo(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                + $"  PartSolved  id={level}  part={name}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"PartSolved log failed: {e.Message}");
        }
    }

    private static void LogEvent(string label, GameEventManager.GameEventData data)
    {
        try
        {
            var li = data?.LevelInterface;
            var line = new StringBuilder();
            line.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            line.Append("  ").Append(label);
            if (li != null)
            {
                line.Append("  id=").Append(Str(() => li.LevelId));
                line.Append("  index=").Append(Str(() => li.LevelIndex.ToString()));
                line.Append("  solutionCount=").Append(Str(() => li.SolutionCount.ToString()));
                line.Append("  found=").Append(Str(() => li.NumSolutionsFound.ToString()));
                line.Append("  seed=").Append(Str(() => li.RandomSeed.ToString()));
            }
            if (!string.IsNullOrEmpty(data?.SolutionId))
            {
                line.Append("  solutionId=").Append(data.SolutionId);
            }
            DevToolsPlugin.Log.LogInfo(line.ToString());
            if (label == "LevelComplete") _partsSolved.Clear();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"event log failed for {label}: {e.Message}");
        }
    }

    // -------------------------------------------------------------- commands

    private void PollCommands(bool busy)
    {
        try
        {
            if (!File.Exists(CommandFile)) return;
            var cmd = File.ReadAllText(CommandFile).Trim();
            if (cmd.Length == 0) return;
            // Mid-sweep only `stop` is taken. Anything else stays in the file
            // and runs once the sweep is over.
            if (busy && !cmd.Equals("stop", StringComparison.OrdinalIgnoreCase)) return;
            File.WriteAllText(CommandFile, string.Empty);
            DevToolsPlugin.Log.LogInfo($"command: {cmd}");
            Dispatch(cmd);
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"command poll failed: {e.Message}");
        }
    }

    /// <summary>
    /// The doc section each command is listed under in docs/dev/devtools.md;
    /// `help` groups by the same names.
    /// </summary>
    private static class Areas
    {
        internal const string Help = "Help";
        internal const string Levels = "Launch and levels";
        internal const string Menus = "Menus and input";
        internal const string Track = "Level select";
        internal const string Objects = "Locks and objects";
        internal const string Hints = "Hints";
        internal const string Screen = "Screen, audio and art";
        internal const string Save = "Save file";
        internal const string Sweeps = "Sweeps and reports";
        internal const string Reflection = "Reflection and tracing";
    }

    /// <summary>
    /// One command: the keyword it is sent as, the doc section it is listed
    /// under, every spelling docs/dev/devtools.md documents for it (the
    /// first column, exactly - tools/check-devtools.py holds the two to each
    /// other, both ways), and what runs. The keyword is the text before the
    /// first ':' or space; the handler gets everything after that separator.
    /// </summary>
    private sealed class Command
    {
        internal readonly string Keyword;
        internal readonly string Area;
        internal readonly string[] Usages;
        internal readonly Action<string> Run;

        internal Command(string keyword, string area, string[] usages, Action<string> run)
        {
            Keyword = keyword;
            Area = area;
            Usages = usages;
            Run = run;
        }
    }

    private static Command Cmd(string keyword, string area, string[] usages, Action<string> run)
        => new(keyword, area, usages, run);

    private static Command Cmd(string keyword, string area, string[] usages, Action run)
        => new(keyword, area, usages, _ => run());

    /// <summary>`on`/`off` for a switch: anything but "off" means on.</summary>
    private static bool IsOff(string arg) => arg.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);

    private List<Command>? _commandList;
    private Dictionary<string, Command>? _commandMap;

    /// <summary>
    /// Every command, in the order `help` and docs/dev/devtools.md list them.
    ///
    /// A TABLE, NOT A LADDER. The if/else chain this replaced matched in
    /// order, which is how `marksolved:` was once documented as `solve:` and
    /// could never run - `solve:` matched first. A keyword is now looked up
    /// whole, so two commands cannot shadow each other, and the usages here
    /// are what check-devtools compares with the doc.
    /// </summary>
    private List<Command> Commands()
    {
        if (_commandList != null) return _commandList;
        var list = new List<Command>
        {
            Cmd("help", Areas.Help, new[] { "help", "help:<command>" }, Help),

            Cmd("boot", Areas.Levels, new[] { "boot:<index>[:<seed>]" }, Boot),
            Cmd("loadlevel", Areas.Levels, new[] { "loadlevel:<index>" }, LoadLevel),
            Cmd("complete", Areas.Levels, new[] { "complete" }, CompleteActiveLevel),
            Cmd("skip", Areas.Levels, new[] { "skip" }, PressSkip),
            Cmd("solve", Areas.Levels, new[] { "solve:<index or name>" }, SolveController),
            Cmd("livelevels", Areas.Levels, new[] { "livelevels" }, CountLiveLevels),
            Cmd("dedupe", Areas.Levels, new[] { "dedupe" }, Dedupe),
            Cmd("state", Areas.Levels, new[] { "state", "state:<file>" }, StateCommand),
            Cmd("contextual", Areas.Levels, new[] { "contextual" }, ReportContextualState),
            Cmd("watch", Areas.Levels, new[] { "watch[:<seconds>]", "watch:off" }, StartWatch),
            Cmd("time", Areas.Levels, new[] { "time" }, ReportTime),
            Cmd("timescale", Areas.Levels, new[] { "timescale:<n>" }, SetTimeScale),

            Cmd("menu", Areas.Menus, new[] { "menu:<name>" }, GoToMenu),
            Cmd("play", Areas.Menus, new[] { "play" }, PressPlay),
            Cmd("pause", Areas.Menus, new[] { "pause", "pause:event" }, OpenPauseMenu),
            Cmd("pausebuttons", Areas.Menus, new[] { "pausebuttons" }, ListPauseButtons),
            Cmd("leave", Areas.Menus, new[] { "leave" }, LeavePuzzle),
            Cmd("next", Areas.Menus, new[] { "next" }, PressNext),
            Cmd("replayselect", Areas.Menus, new[] { "replayselect" }, ReplayLevelSelect),
            Cmd("menus", Areas.Menus, new[] { "menus" }, DumpMenus),
            Cmd("buttons", Areas.Menus, new[] { "buttons" }, ListButtons),
            Cmd("titlebuttons", Areas.Menus, new[] { "titlebuttons" }, ListTitleButtons),
            Cmd("titletree", Areas.Menus, new[] { "titletree" }, DumpTitleTree),
            Cmd("uitree", Areas.Menus, new[] { "uitree:<object name>[:<depth>]" }, DumpUiTree),
            Cmd("press", Areas.Menus, new[] { "press:<name>" }, PressControl),
            Cmd("clickbutton", Areas.Menus, new[] { "clickbutton:<name>" }, ClickButton),
            Cmd("clickat", Areas.Menus, new[] { "clickat[:<x>,<y>]" }, ClickAt),

            Cmd("clicktrack", Areas.Track, new[] { "clicktrack:<position>" }, ClickTrack),
            Cmd("clickcard", Areas.Track, new[] { "clickcard:<index>" }, ClickCard),
            Cmd("focus", Areas.Track, new[] { "focus:<position>" }, FocusIcon),
            Cmd("scrolltrack", Areas.Track, new[] { "scrolltrack:<position>" }, ScrollTrack),
            Cmd("sections", Areas.Track, new[] { "sections" }, DumpSections),
            Cmd("iconinfo", Areas.Track, new[] { "iconinfo:<position>" }, IconInfo),
            Cmd("why", Areas.Track, new[] { "why:<position>" }, WhyBadge),
            Cmd("creditscard", Areas.Track, new[] { "creditscard" }, ReportCreditsCard),
            Cmd("starcalls", Areas.Track, new[] { "starcalls" }, ReportStarCalls),
            Cmd("skiptip", Areas.Track, new[] { "skiptip" }, ShowSkipTooltip),

            Cmd("controllers", Areas.Objects, new[] { "controllers" }, ListControllers),
            Cmd("indexables", Areas.Objects, new[] { "indexables" }, ListIndexables),
            Cmd("revoke", Areas.Objects, new[] { "revoke:<Ability>[,<Ability>]", "revoke:none" }, Revoke),
            Cmd("traps", Areas.Objects, new[] { "traps:off", "traps:on" }, Traps),
            Cmd("locks", Areas.Objects, new[] { "locks" }, ReportLocks),
            Cmd("reachable", Areas.Objects, new[] { "reachable" }, Reachable),
            Cmd("freeze", Areas.Objects, new[] { "freeze:<controller>", "freeze:off" }, Freeze),
            Cmd("colliders", Areas.Objects, new[] { "colliders:<controller>", "colliders:all" }, Colliders),
            Cmd("drawers", Areas.Objects, new[] { "drawers", "drawers:open:<name>", "drawers:close:<name>" }, Drawers),
            Cmd("objects", Areas.Objects, new[] { "objects:<file>" }, a => State(a.Trim(), "objects")),
            Cmd("scrub", Areas.Objects, new[] { "scrub:<name>[:<fraction>]" }, Scrub),
            Cmd("flip", Areas.Objects, new[] { "flip:<name>[:<seconds>]", "flip:off" }, StartFlip),
            Cmd("tree", Areas.Objects, new[] { "tree:<name>" }, Tree),
            Cmd("layout", Areas.Objects, new[] { "layout:<tag>" }, DumpLayout),
            Cmd("shove", Areas.Objects, new[] { "shove", "shove:<x>,<y>" }, Shove),
            Cmd("bounds", Areas.Objects, new[] { "bounds:<tag>" }, DumpBounds),
            Cmd("sharing", Areas.Objects, new[] { "sharing:new", "sharing:append" }, DumpSharing),
            Cmd("jiggle", Areas.Objects, new[] { "jiggle[:<n>]" }, JigglePieces),
            Cmd("cats", Areas.Objects, new[] { "cats" }, ListCats),
            Cmd("catevent", Areas.Objects, new[] { "catevent[:info|grab|trigger|try|climb]" }, CatEvent),

            Cmd("hints", Areas.Hints, new[] { "hints" }, ReportHints),
            Cmd("hinttaken", Areas.Hints, new[] { "hinttaken" }, RaiseHintTaken),
            Cmd("erase", Areas.Hints, new[] { "erase" }, DragTheEraser),

            Cmd("shot", Areas.Screen, new[] { "shot:<abs path>[|<n>]" }, Screenshot),
            Cmd("setres", Areas.Screen, new[] { "setres:<w>x<h>" }, SetResolution),
            Cmd("resolutions", Areas.Screen, new[] { "resolutions" }, DumpResolutions),
            Cmd("cameras", Areas.Screen, new[] { "cameras", "cameras:all" }, ReportCameras),
            Cmd("mute", Areas.Screen, new[] { "mute", "mute:off" }, a => SetMute(!IsOff(a))),
            Cmd("unmute", Areas.Screen, new[] { "unmute" }, () => SetMute(false)),
            Cmd("bgset", Areas.Screen, new[] { "bgset:<colour>" }, SetLevelBackground),
            Cmd("bgcatalogue", Areas.Screen, new[] { "bgcatalogue" }, ReportBackgroundCatalogue),
            Cmd("menubg", Areas.Screen, new[] { "menubg" }, ReportMenuBackground),
            Cmd("sprites", Areas.Screen, new[] { "sprites <filter>" }, DumpSprites),
            Cmd("spriteexport", Areas.Screen, new[] { "spriteexport:<names>|<folder>" }, ExportSprites),
            Cmd("spritegrid", Areas.Screen, new[] { "spritegrid:<names>", "spritegrid:off" }, ShowSpriteGrid),
            Cmd("newsprites", Areas.Screen, new[] { "newsprites" }, DumpNewSprites),

            Cmd("unlocks", Areas.Save, new[] { "unlocks" }, DumpUnlocks),
            Cmd("unlockto", Areas.Save, new[] { "unlockto:<n>" }, UnlockTo),
            Cmd("marksolved", Areas.Save, new[] { "marksolved:<index>[:<solutionId>]" }, MarkSolved),
            Cmd("resetlevels", Areas.Save, new[] { "resetlevels" }, ResetLevels),

            Cmd("dump", Areas.Sweeps, new[] { "dump" }, Dump),
            Cmd("solutions", Areas.Sweeps, new[] { "solutions" }, StartSurvey),
            Cmd("levelsweep", Areas.Sweeps, new[] { "levelsweep", "levelsweep:<i1,i2,...>" }, a => _dataTable.Start(a)),
            Cmd("gensweep", Areas.Sweeps, new[] { "gensweep:<seeds>[:<index>]" }, StartGenSweep),
            Cmd("rules", Areas.Sweeps, new[] { "rules" }, ReportRules),
            Cmd("stop", Areas.Sweeps, new[] { "stop" }, StopSweeps),
            Cmd("endings", Areas.Sweeps, new[] { "endings" }, DumpEndings),
            Cmd("achievements", Areas.Sweeps, new[] { "achievements" }, DumpAchievements),
            Cmd("cursor", Areas.Sweeps, new[] { "cursor" }, ReportCursor),
            Cmd("registrations", Areas.Sweeps, new[] { "registrations:on", "registrations:off" },
                a => { if (IsOff(a)) RegistrationLog.Stop(); else RegistrationLog.Start(); }),
            Cmd("regstart", Areas.Sweeps, new[] { "regstart" }, RegistrationLog.Start),
            Cmd("regstop", Areas.Sweeps, new[] { "regstop" }, RegistrationLog.Stop),

            Cmd("members", Areas.Reflection, new[] { "members:<Type>[:<filter>]" }, ListMembers),
            Cmd("values", Areas.Reflection, new[] { "values:<Type>[:<filter>]" }, ListValues),
            Cmd("xrefs", Areas.Reflection, new[] { "xrefs:<Type>.<Method>", "xrefs:<Type>.<Method>|<Type>.<Candidate>,..." }, ListXrefs),
            Cmd("trace", Areas.Reflection, new[] { "trace:<Type>.<Method>[,...]", "trace:off" }, MethodTrace.Start),
            Cmd("findtext", Areas.Reflection, new[] { "findtext:<text>" }, FindText),
        };

        var map = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in list)
        {
            if (map.ContainsKey(command.Keyword))
                throw new InvalidOperationException($"two commands named {command.Keyword}");
            map[command.Keyword] = command;
        }
        _commandMap = map;
        _commandList = list;
        return list;
    }

    private void Dispatch(string cmd)
    {
        Commands();
        var cut = cmd.IndexOfAny(new[] { ':', ' ' });
        var keyword = (cut < 0 ? cmd : cmd.Substring(0, cut)).Trim();
        var arg = cut < 0 ? "" : cmd.Substring(cut + 1);
        if (!_commandMap!.TryGetValue(keyword, out var command))
        {
            var near = NearMatches(keyword);
            DevToolsPlugin.Log.LogWarning($"unknown command: {cmd}"
                + (near.Count == 0 ? " (help lists every command)" : $" - did you mean {string.Join(", ", near)}?"));
            return;
        }
        SafeRun(command.Keyword, () => command.Run(arg));
    }

    /// <summary>
    /// "help" lists every command by area; "help:boot" shows one. The same
    /// table the dispatcher reads, so it cannot list a command that does not
    /// run.
    /// </summary>
    private void Help(string arg)
    {
        var list = Commands();
        var wanted = arg.Trim();
        if (wanted.Length > 0)
        {
            if (_commandMap!.TryGetValue(wanted, out var one))
            {
                DevToolsPlugin.Log.LogInfo($"help: {string.Join("  /  ", one.Usages)}   ({one.Area})");
                return;
            }
            var near = NearMatches(wanted);
            DevToolsPlugin.Log.LogWarning($"help: no command named {wanted}"
                + (near.Count == 0 ? "" : $" - did you mean {string.Join(", ", near)}?"));
            return;
        }

        DevToolsPlugin.Log.LogInfo(
            $"help: {list.Count} commands, each written into BepInEx/alttl-devtools-commands.txt; "
            + "the argument follows the first ':' (a space for sprites and setres). "
            + "docs/dev/devtools.md explains each one.");
        string? area = null;
        foreach (var command in list)
        {
            if (command.Area != area)
            {
                area = command.Area;
                DevToolsPlugin.Log.LogInfo($"help: -- {area} --");
            }
            DevToolsPlugin.Log.LogInfo($"help:   {string.Join("  /  ", command.Usages)}");
        }
    }

    /// <summary>Keywords within two edits of a mistyped one, or sharing its start.</summary>
    private List<string> NearMatches(string typed)
    {
        typed = typed.ToLowerInvariant();
        var near = new List<string>();
        foreach (var command in Commands())
        {
            var k = command.Keyword;
            if (Distance(typed, k) <= 2
                || (typed.Length >= 3 && (k.StartsWith(typed, StringComparison.Ordinal)
                                          || typed.StartsWith(k, StringComparison.Ordinal))))
            {
                near.Add(k);
            }
            if (near.Count == 5) break;
        }
        return near;
    }

    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) row[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1),
                                  diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    private static bool _focusRuleApplied;

    /// <summary>
    /// Tell Rewired to ignore input while the window is not focused.
    ///
    /// Applied from the update loop rather than at startup because Rewired is
    /// not ready when the plugin loads, and setting it early is silently lost.
    /// One-shot, and it deliberately does NOT touch Application.runInBackground:
    /// the game must keep ticking while unfocused or a scripted run stalls the
    /// moment attention moves elsewhere. Running and listening are separate
    /// things, and only the second is unwanted here.
    ///
    /// The harness is unaffected: DevTools drives the game through its command
    /// file and by invoking handlers directly - clickbutton: calls the Button's
    /// onClick - so nothing it does arrives as Rewired input.
    /// </summary>
    private static void ApplyFocusRule()
    {
        if (_focusRuleApplied || !DevToolsPlugin.IgnoreInputUnfocused) return;

        try
        {
            if (!Rewired.ReInput.isReady) return;      // not up yet; try again
            Rewired.ReInput.configuration.ignoreInputWhenAppNotInFocus = true;
            _focusRuleApplied = true;
            DevToolsPlugin.Log.LogInfo(
                "input: the game will ignore the keyboard and mouse while unfocused");
        }
        catch (Exception e)
        {
            _focusRuleApplied = true;                  // do not retry every frame
            DevToolsPlugin.Log.LogWarning(
                $"input: could not set the focus rule: {e.Message}");
        }
    }

    /// <summary>
    /// A transform's full path, for telling three "Background"s apart.
    ///
    /// PathOf, not Path: this file uses System.IO.Path, and a static method of
    /// that name shadows the type for the whole class.
    /// </summary>
    private static string PathOf(Transform t)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var at = t; at != null; at = at.parent) parts.Add(at.gameObject.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void SafeRun(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"{what} failed: {e}");
        }
    }

}
