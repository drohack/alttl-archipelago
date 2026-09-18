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

namespace ALTTLDevTools;


/// <summary>
/// Research probe for A Little To The Left. It answers one question - what
/// does the game expose that a randomizer could drive - by dumping the level
/// tables to disk and logging the gameplay events as they fire.
///
/// Nothing here modifies the game. It reads and it writes files.
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
    /// Raise the game window above the BepInEx console at startup.
    ///
    /// OFF by default, and it must stay that way: SetForegroundWindow takes
    /// focus from whatever the person at the keyboard is doing on whichever
    /// desktop they are on, which makes a scripted launch genuinely disruptive
    /// to someone working alongside it. Turn it on only for a session where
    /// the game is meant to be the foreground window.
    /// </summary>
    internal static bool IgnoreInputUnfocused;
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
        harmony.PatchAll(typeof(LevelSelectOverride));
        harmony.PatchAll(typeof(CardLock));
        // PatchAll(Type) registers ONE class. A new [HarmonyPatch] class that
        // nobody adds here is silently never applied - which is not a
        // hypothetical: RegistrationLog was written, shipped, and reported
        // "zero registrations across 111 levels", and that was read as
        // evidence about the game when it was really evidence that the patch
        // did not exist at runtime.
        harmony.PatchAll(typeof(RegistrationLog));
        harmony.PatchAll(typeof(LaunchTrace));
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

    private bool _dumped;

    private bool _desktopMoved;

    private readonly DataTable _dataTable = new();

    private bool _listenersAttached;

    private float _nextCommandPoll;

    // Solution survey state. Driven from Update rather than a coroutine so no
    // IEnumerator has to be marshalled into the IL2CPP domain.
    private bool _surveying;

    private int _surveyIndex;

    private int _surveyWaitFrames;

    private Il2CppSystem.Threading.Tasks.Task<Level>? _surveyTask;

    private LevelInterface? _surveyLevel;

    private List<LevelInterface> _surveyQueue = new();

    private readonly StringBuilder _surveyOut = new();

    private static string GameDir => Path.GetDirectoryName(Application.dataPath)!;

    private static string CommandFile => Path.Combine(GameDir, "BepInEx", "alttl-devtools-commands.txt");

    private static string DumpFile => Path.Combine(GameDir, "BepInEx", "alttl-dump.json");

    private static string EventLog => Path.Combine(GameDir, "BepInEx", "alttl-events.log");

    private static string SolutionFile => Path.Combine(GameDir, "BepInEx", "alttl-solutions.tsv");

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

        if (!_dumped && _frames > SettleFrames && HasLevelTable(gm))
        {
            _dumped = true;
            SafeRun("auto dump", Dump);
        }

        ApplyFocusRule();

        if (_surveying)
        {
            SafeRun("survey step", SurveyStep);
            return;
        }

        if (_dataTable.Running)
        {
            SafeRun("levelsweep step", _dataTable.Tick);
            return;
        }

        if (_sweeping)
        {
            SafeRun("gensweep step", GenSweepStep);
            return;
        }

        if (Time.unscaledTime >= _nextCommandPoll)
        {
            _nextCommandPoll = Time.unscaledTime + 0.5f;
            PollCommands();
        }
    }

    // -------------------------------------------------- generator seed sweep

    private bool _sweeping;

    private int _sweepPos;

    private int _sweepWait;

    private Il2CppSystem.Threading.Tasks.Task? _sweepTask;

    private List<(int index, int seed)> _sweepJobs = new();

    private readonly StringBuilder _sweepOut = new();

    private static string SweepFile => Path.Combine(GameDir, "BepInEx", "alttl-generators.tsv");

    /// <summary>
    /// "gensweep:SEEDCOUNT" - regenerates every randomizable level under a
    /// series of seeds and records what came out. This is the data the
    /// randomizer's logic needs: whether a generator's solution count is
    /// fixed or varies, which sorting rule it picked, and how big the puzzle
    /// got (the only difficulty signal the game offers, since it ships no
    /// difficulty rating of any kind).
    /// </summary>
    private void StartGenSweep(string arg)
    {
        var seedCount = int.TryParse(arg, out var n) ? Math.Max(1, n) : 6;
        var lm = GameManager.Instance.levelManager;
        var all = lm.AllLevelInterfaces(false);
        var installed = DataTable.InstalledDlc();

        _sweepJobs = new List<(int, int)>();
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            bool randomizable;
            int idx;
            try { randomizable = li.IsRandomizable; idx = li.LevelIndex; }
            catch { continue; }
            if (!randomizable) continue;
            // An uninstalled DLC throws on load, so its levels are skipped -
            // but an owned one is swept. Four DLC levels are randomizable and
            // belong in this sweep exactly like the base game's.
            var dlc = DataTable.DlcKeyOf(li);
            if (dlc != null && !installed.Contains(dlc)) continue;
            for (int s = 0; s < seedCount; s++)
            {
                _sweepJobs.Add((idx, 1000 + s * 7919));
            }
        }

        _sweepOut.Clear();
        _sweepOut.AppendLine("levelIndex\tlevelId\tseed\tdeclaredSolutions\tlevelNumSolutions"
            + "\tobjectCount\tcontrollerCount\tcontrollers\trandomizer\tchosenSolutions");
        _sweepPos = 0;
        _sweepTask = null;
        _sweeping = true;
        DevToolsPlugin.Log.LogInfo($"-- generator sweep: {_sweepJobs.Count} regenerations --");
    }

    private void GenSweepStep()
    {
        if (_sweepPos >= _sweepJobs.Count)
        {
            File.WriteAllText(SweepFile, _sweepOut.ToString());
            _sweeping = false;
            DevToolsPlugin.Log.LogInfo($"gensweep complete: {SweepFile}");
            return;
        }

        var lm = GameManager.Instance.levelManager;
        var (index, seed) = _sweepJobs[_sweepPos];

        if (_sweepTask == null)
        {
            _sweepWait = 0;
            _sweepTask = lm.SetActiveLevel(index, false, true, seed);
            if ((_sweepPos % 6) == 0)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"[{_sweepPos + 1}/{_sweepJobs.Count}] regenerating level {index} seed {seed}");
            }
            return;
        }

        _sweepWait++;
        bool done;
        try { done = _sweepTask.IsCompleted; } catch { done = true; }
        // Give the randomizer a few frames after the load to finish building.
        if (!done && _sweepWait < 600) return;
        if (done && _sweepWait < 20) return;

        RecordGeneration(index, seed);
        _sweepTask = null;
        _sweepPos++;
    }

    private void RecordGeneration(int index, int seed)
    {
        var lm = GameManager.Instance.levelManager;
        var li = lm.ActiveLevelInterface;
        var level = li == null ? null : li.Level;

        var id = Str(() => li == null ? "?" : li.LevelId);
        var declared = Str(() => li == null ? "?" : li.SolutionCount.ToString());
        var actual = Str(() => level == null ? "?" : level.numSolutions.ToString());
        var objects = Str(() => level == null || level.allLevelObjects == null
            ? "?" : level.allLevelObjects.Count.ToString());

        var controllers = new StringBuilder();
        var ctrlCount = 0;
        try
        {
            var list = level!.objectControllers;
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                var oc = list![i];
                if (oc == null) continue;
                if (ctrlCount > 0) controllers.Append('|');
                controllers.Append(Str(() => oc.GetIl2CppType().Name));
                ctrlCount++;
            }
        }
        catch { /* recorded as whatever was gathered */ }

        var randomizerName = "-";
        var chosen = "-";
        try
        {
            var r = level!.m_randomizer;
            if (r != null)
            {
                randomizerName = Str(() => r.GetIl2CppType().Name);
                var books = r.TryCast<Books_LevelRandomizer>();
                if (books != null)
                {
                    chosen = books.firstSolution.ToString() + "+" + books.secondSolution.ToString();
                }
                var pencils = r.TryCast<Pencils_LevelRandomizer>();
                if (pencils != null)
                {
                    chosen = pencils.firstSolution.ToString() + "+" + pencils.secondSolution.ToString();
                }
            }
        }
        catch { /* leave the markers */ }

        _sweepOut.AppendLine($"{index}\t{id}\t{seed}\t{declared}\t{actual}\t{objects}"
            + $"\t{ctrlCount}\t{controllers}\t{randomizerName}\t{chosen}");
    }

    // ------------------------------------------------------- solution survey

    /// <summary>
    /// Loads every level prefab in turn and records the solution ids it
    /// exposes. This is the question the whole "unlocks are alternate
    /// solutions" design rests on: the game only publishes SolutionCount as a
    /// number, and a location needs a stable name.
    /// </summary>
    private void StartSurvey()
    {
        var gm = GameManager.Instance;
        var all = gm.levelManager.AllLevelInterfaces(false);
        _surveyQueue = new List<LevelInterface>();
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            if (all![i] != null) _surveyQueue.Add(all[i]);
        }
        _surveyIndex = 0;
        _surveyTask = null;
        _surveyLevel = null;
        _surveyOut.Clear();
        _surveyOut.AppendLine(
            "levelIndex\tlevelId\tsolutionCount\tcontroller\tcontrollerType"
            + "\tsetCount\tsolutionIds\tdependsOn\tnote");
        _surveying = true;
        DevToolsPlugin.Log.LogInfo($"-- solution survey: {_surveyQueue.Count} levels --");
    }

    private void SurveyStep()
    {
        if (_surveyIndex >= _surveyQueue.Count)
        {
            File.WriteAllText(SolutionFile, _surveyOut.ToString());
            _surveying = false;
            DevToolsPlugin.Log.LogInfo($"survey complete: {SolutionFile}");
            return;
        }

        var li = _surveyQueue[_surveyIndex];
        var total = _surveyQueue.Count;
        var id = Str(() => li.LevelId);
        var idx = Str(() => li.LevelIndex.ToString());

        if (_surveyTask == null)
        {
            try
            {
                _surveyLevel = li;
                _surveyTask = li.LoadLevel(false);
                _surveyWaitFrames = 0;
                DevToolsPlugin.Log.LogInfo(
                    $"[{_surveyIndex + 1}/{total} {id}] step 1/2: loading level prefab");
            }
            catch (Exception e)
            {
                Record(idx, id, li, null, "load-threw:" + e.GetType().Name);
                Advance();
            }
            return;
        }

        _surveyWaitFrames++;
        bool done;
        try { done = _surveyTask.IsCompleted; }
        catch { done = true; }

        if (!done)
        {
            // 10 seconds is generous for a local addressable; past that the
            // level is almost certainly missing (uninstalled DLC).
            if (_surveyWaitFrames > 600)
            {
                Record(idx, id, li, null, "load-timeout");
                Advance();
            }
            return;
        }

        Level? level = null;
        string note = "";
        try
        {
            level = _surveyTask.Result;
            if (level == null) note = "null-level";
        }
        catch (Exception e)
        {
            note = "result-threw:" + e.GetType().Name;
        }

        DevToolsPlugin.Log.LogInfo(
            $"[{_surveyIndex + 1}/{total} {id}] step 2/2: reading controllers ({note})");
        Record(idx, id, li, level, note);
        Advance();
    }

    private void Advance()
    {
        try { _surveyLevel?.ReleaseAssetsAndDestroyLevel(); }
        catch (Exception e) { DevToolsPlugin.Log.LogWarning($"release failed: {e.Message}"); }
        _surveyLevel = null;
        _surveyTask = null;
        _surveyIndex++;
    }

    private void Record(string idx, string id, LevelInterface li, Level? level, string note)
    {
        var solCount = Str(() => li.SolutionCount.ToString());
        if (level == null)
        {
            _surveyOut.AppendLine($"{idx}\t{id}\t{solCount}\t\t\t\t\t\t{note}");
            return;
        }

        int rows = 0;
        try
        {
            // level.objectControllers is only populated once a level is
            // actually started - controllers self-register in their own Start.
            // Walking the loaded prefab finds them without running the level.
            var controllers = level.gameObject.GetComponentsInChildren<ObjectController>(true);
            for (int i = 0; i < (controllers == null ? 0 : controllers.Length); i++)
            {
                var oc = controllers![i];
                if (oc == null) continue;
                var name = Str(() => oc.gameObject.name);
                // The component's own class - Draggables, Shuffleables,
                // GridPuzzle and so on - is the game's real "kind of puzzle"
                // taxonomy. The GameObject name is just a level-author label.
                var type = Str(() => oc.GetIl2CppType().Name);
                int sets = 0;
                try
                {
                    var ss = oc.GetComponent<SolutionSets>()
                             ?? oc.GetComponentInChildren<SolutionSets>(true);
                    if (ss != null && ss.sets != null) sets = ss.sets.Count;
                }
                catch { /* controller type carries no SolutionSets */ }

                var ids = new StringBuilder();
                for (int s = 0; s < sets; s++)
                {
                    if (s > 0) ids.Append('|');
                    ids.Append(name).Append('_').Append(s);
                }

                // Phase 0 S4. A controller that depends on another cannot be
                // solved until that one is, so an ability lock on the
                // dependency transitively blocks this controller too - which
                // widens its access rule in the apworld.
                var deps = new StringBuilder();
                try
                {
                    var dl = oc.dependencies;
                    for (int dnum = 0; dnum < (dl == null ? 0 : dl.Count); dnum++)
                    {
                        var dep = dl![dnum];
                        if (dep == null) continue;
                        if (deps.Length > 0) deps.Append('|');
                        deps.Append(Str(() => dep.gameObject.name));
                    }
                }
                catch (Exception e) { deps.Append("<err:").Append(e.GetType().Name).Append('>'); }

                _surveyOut.AppendLine(
                    $"{idx}\t{id}\t{solCount}\t{name}\t{type}\t{sets}\t{ids}\t{deps}\t{note}");
                rows++;
            }
        }
        catch (Exception e)
        {
            note = (note.Length > 0 ? note + ";" : "") + "controllers-threw:" + e.GetType().Name;
        }

        if (rows == 0)
        {
            _surveyOut.AppendLine($"{idx}\t{id}\t{solCount}\t\t\t\t\t\t{note}|no-controllers");
        }
    }

    private static bool HasLevelTable(GameManager gm)
    {
        try
        {
            var lm = gm.levelManager;
            return lm != null && lm.LevelInterfaces != null && lm.LevelInterfaces.Length > 0;
        }
        catch
        {
            return false;
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

    /// <summary>Phase 0 transcript listener - writes to alttl-watch.log.</summary>
    private static void Watch<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(data => Phase0.LogWatch(label, data));
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    private static void Listen<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(data => LogEvent(label, data));
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
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
            File.AppendAllText(EventLog, line.ToString() + Environment.NewLine);
            DevToolsPlugin.Log.LogInfo(line.ToString());
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"event log failed for {label}: {e.Message}");
        }
    }

    // -------------------------------------------------------------- commands

    private void PollCommands()
    {
        try
        {
            if (!File.Exists(CommandFile)) return;
            var cmd = File.ReadAllText(CommandFile).Trim();
            if (cmd.Length == 0) return;
            File.WriteAllText(CommandFile, string.Empty);
            DevToolsPlugin.Log.LogInfo($"command: {cmd}");

            if (cmd.Equals("dump", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("dump", Dump);
            }
            else if (cmd.Equals("solutions", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("survey", StartSurvey);
            }
            else if (cmd.StartsWith("boot:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("boot", () => Boot(cmd.Substring(5)));
            }
            else if (cmd.Equals("complete", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("complete", () =>
                {
                    var li = GameManager.Instance.levelManager.ActiveLevelInterface;
                    DevToolsPlugin.Log.LogInfo($"complete: CompleteLevel() on {li.LevelId}");
                    li.CompleteLevel();
                });
            }
            else if (cmd.Equals("play", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("play", () =>
                {
                    var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
                    if (title == null)
                    {
                        DevToolsPlugin.Log.LogWarning("play: no live TitleMenu");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("play: pressing Play");
                    title.PlayGame();
                });
            }
            else if (cmd.Equals("resettest", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("resettest", ResetTest);
            }
            else if (cmd.Equals("showpause", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("showpause", () =>
                {
                    MainMenu? menu = null;
                    foreach (var candidate in Resources.FindObjectsOfTypeAll(
                                 Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
                    {
                        var found = candidate?.TryCast<MainMenu>();
                        if (found == null || !found.gameObject.scene.IsValid()) continue;
                        menu = found;
                        break;
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning("showpause: no live MainMenu");
                        return;
                    }

                    // The method the game runs when the pause menu opens. The
                    // buttons are only hidden at that moment, so dumping a
                    // closed menu says nothing about what a player sees.
                    DevToolsPlugin.Log.LogInfo("showpause: running ShowHideMenuItems");
                    menu.ShowHideMenuItems(null);
                });
            }
            else if (cmd.Equals("pausebuttons", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("pausebuttons", () =>
                {
                    MainMenu? menu = null;
                    foreach (var candidate in Resources.FindObjectsOfTypeAll(
                                 Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
                    {
                        var found = candidate?.TryCast<MainMenu>();
                        if (found == null || !found.gameObject.scene.IsValid()) continue;
                        menu = found;
                        break;
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning("pausebuttons: no live MainMenu");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo(
                        "pausebuttons: active=" + Str(() => menu.gameObject.activeInHierarchy.ToString()));
                    var container = menu.ButtonsContainer;
                    if (container == null) { DevToolsPlugin.Log.LogWarning("  no container"); return; }
                    for (int i = 0; i < container.childCount; i++)
                    {
                        var child = container.GetChild(i);
                        DevToolsPlugin.Log.LogInfo(
                            $"  {Str(() => child.name)} active="
                            + Str(() => child.gameObject.activeSelf.ToString()));
                    }
                });
            }
            else if (cmd.Equals("titletree", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("titletree", () =>
                {
                    var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
                    if (title == null)
                    {
                        DevToolsPlugin.Log.LogWarning("titletree: no title screen");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("titletree:");
                    DumpTitle(title.transform, 0);
                });
            }
            else if (cmd.Equals("titlebuttons", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("titlebuttons", () =>
                {
                    var title = UnityEngine.Object.FindObjectOfType<TitleMenu>();
                    var container = title == null ? null : title.MainMenuContainer;
                    if (container == null)
                    {
                        DevToolsPlugin.Log.LogWarning("titlebuttons: no title screen");
                        return;
                    }
                    for (int i = 0; i < container.childCount; i++)
                    {
                        var child = container.GetChild(i);
                        DevToolsPlugin.Log.LogInfo(
                            $"  {Str(() => child.name)} active="
                            + Str(() => child.gameObject.activeSelf.ToString()));
                    }
                });
            }
            else if (cmd.Equals("menus", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("menus", DumpMenus);
            }
            else if (cmd.Equals("replayselect", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("replayselect", () =>
                {
                    ReplayMenu? menu = null;
                    foreach (var candidate in Resources.FindObjectsOfTypeAll(
                                 Il2CppInterop.Runtime.Il2CppType.Of<ReplayMenu>()))
                    {
                        var found = candidate?.TryCast<ReplayMenu>();
                        if (found == null || !found.gameObject.scene.IsValid()) continue;
                        menu = found;
                        break;
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning("replayselect: no live ReplayMenu");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("replayselect: post-level Level Select");
                    menu.LevelSelect();
                });
            }
            else if (cmd.Equals("next", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("next", () =>
                {
                    ReplayMenu? menu = null;
                    foreach (var candidate in Resources.FindObjectsOfTypeAll(
                                 Il2CppInterop.Runtime.Il2CppType.Of<ReplayMenu>()))
                    {
                        var found = candidate?.TryCast<ReplayMenu>();
                        if (found == null || !found.gameObject.scene.IsValid()) continue;
                        menu = found;
                        break;
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning("next: no live ReplayMenu");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("next: pressing the arrow");
                    menu.NextLevel();
                });
            }
            else if (cmd.Equals("leave", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("leave", () =>
                {
                    // The pause menu's own Level Select button - the thing a
                    // player actually presses to leave a puzzle. Asking the
                    // game where it WOULD go proved nothing; this takes the
                    // route.
                    // FindObjectsOfTypeAll, because the pause menu is INACTIVE
                    // while a puzzle is being played - a plain search finds
                    // nothing and the button can never be pressed.
                    // A LIVE instance, not a prefab. FindObjectsOfTypeAll
                    // returns prefabs too, and pressing a button on one does
                    // nothing useful - it produced a blank screen that looked
                    // like a bug in the thing being tested.
                    MainMenu? menu = null;
                    foreach (var candidate in Resources.FindObjectsOfTypeAll(
                                 Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
                    {
                        var found = candidate?.TryCast<MainMenu>();
                        if (found == null) continue;
                        if (!found.gameObject.scene.IsValid()) continue;   // prefab
                        menu = found;
                        break;
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning("leave: no MainMenu in the scene");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("leave: pressing Level Select");
                    menu.LevelSelect();
                });
            }
            else if (cmd.Equals("contextual", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("contextual", ReportContextualState);
            }
            else if (cmd.StartsWith("why:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("why", () => WhyBadge(cmd.Substring("why:".Length)));
            }
            else if (cmd.StartsWith("clicktrack:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("clicktrack", () => ClickTrack(cmd.Substring("clicktrack:".Length)));
            }
            else if (cmd.StartsWith("focus:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("focus", () => FocusIcon(cmd.Substring("focus:".Length)));
            }
            else if (cmd.Equals("controllers", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("controllers", ListControllers);
            }
            else if (cmd.Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                // The game's own SkipLevel, which is where our gate lives.
                //
                // Not the pause-menu button: the button is only reachable with
                // the menu open, and SkipLevel is where every route - button,
                // pause menu, and the tooltip's hold-to-skip - ends up. Unlike
                // ShowHideMenuItems it takes no GameEventData, so calling it
                // needs nothing invented.
                SafeRun("skip", () =>
                {
                    // FindObjectOfType only sees ACTIVE objects, and the pause
                    // menu is inactive while closed - which is exactly the
                    // state we want to skip from.
                    MainMenu? menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                    if (menu == null)
                    {
                        foreach (var obj in Resources.FindObjectsOfTypeAll(
                                     Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
                        {
                            menu = obj == null ? null : obj.TryCast<MainMenu>();
                            if (menu != null) break;
                        }
                    }
                    if (menu == null)
                    {
                        DevToolsPlugin.Log.LogWarning(
                            "skip: no MainMenu - it exists only while a level is running");
                        return;
                    }
                    DevToolsPlugin.Log.LogInfo("skip: calling MainMenu.SkipLevel");
                    menu.SkipLevel();
                });
            }
            else if (cmd.StartsWith("press:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("press", () => PressControl(cmd.Substring("press:".Length)));
            }
            else if (cmd.Equals("mute", StringComparison.OrdinalIgnoreCase)
                     || cmd.Equals("unmute", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("mute", () =>
                {
                    DevToolsPlugin.MuteAudio =
                        cmd.Equals("mute", StringComparison.OrdinalIgnoreCase);
                    UnityEngine.AudioListener.volume =
                        DevToolsPlugin.MuteAudio ? 0f : 1f;
                    DevToolsPlugin.Log.LogInfo(
                        $"audio: {(DevToolsPlugin.MuteAudio ? "muted" : "unmuted")}"
                        + $" (AudioListener.volume="
                        + $"{UnityEngine.AudioListener.volume:0.##})");
                });
            }
            else if (cmd.Equals("creditscard", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("creditscard", ReportCreditsCard);
            }
            else if (cmd.Equals("livelevels", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("livelevels", CountLiveLevels);
            }
            else if (cmd.StartsWith("clickat", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Length > 8 ? cmd.Substring(8) : "";
                SafeRun("clickat", () => ClickAt(arg));
            }
            else if (cmd.Equals("pause", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("pause", OpenPauseMenu);
            }
            else if (cmd.Equals("skiptip", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("skiptip", ShowSkipTooltip);
            }
            else if (cmd.StartsWith("findtext:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("findtext", () => FindText(cmd.Substring(9)));
            }
            else if (cmd.Equals("bgcatalogue", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("bgcatalogue", ReportBackgroundCatalogue);
            }
            else if (cmd.StartsWith("bgset:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("bgset", () => SetLevelBackground(cmd.Substring(6)));
            }
            else if (cmd.Equals("menubg", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("menubg", ReportMenuBackground);
            }
            else if (cmd.Equals("hinttaken", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("hinttaken", RaiseHintTaken);
            }
            else if (cmd.Equals("erase", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("erase", DragTheEraser);
            }
            else if (cmd.Equals("hints", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("hints", ReportHints);
            }
            else if (cmd.StartsWith("members:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("members", () => ListMembers(cmd.Substring("members:".Length)));
            }
            else if (cmd.Equals("buttons", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("buttons", ListButtons);
            }
            else if (cmd.StartsWith("bounds:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("bounds", () => DumpBounds(cmd.Substring("bounds:".Length)));
            }
            else if (cmd.Equals("cats", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("cats", ListCats);
            }
            else if (cmd.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("layout", () => DumpLayout(cmd.Substring("layout:".Length)));
            }
            else if (cmd.StartsWith("solve:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("solve", () => SolveController(cmd.Substring("solve:".Length)));
            }
            else if (cmd.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("state", ReportState);
            }
            else if (cmd.StartsWith("watch:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("watch", () => StartWatch(cmd.Substring("watch:".Length)));
            }
            else if (cmd.StartsWith("menu:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("menu", () => GoToMenu(cmd.Substring(5)));
            }
            else if (cmd.StartsWith("shot:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("shot", () =>
                {
                    // shot:C:/path.png            the window as it is
                    // shot:C:/path.png|3          rendered at three times
                    //
                    // SUPERSIZE RATHER THAN A BIGGER WINDOW. Unity renders
                    // the frame at a multiple of the current resolution, so
                    // a detailed capture costs nothing but time - no
                    // resolution change, no window rebuild, and nothing of
                    // the player's display touched. Changing the resolution
                    // to take a picture would move the size the mod
                    // remembers, which is the one thing this project has
                    // been asked repeatedly not to do.
                    var arg = cmd.Substring(5);
                    var size = 1;
                    var bar = arg.LastIndexOf('|');
                    if (bar > 0
                        && int.TryParse(arg.Substring(bar + 1), NumberStyles.Integer,
                                        CultureInfo.InvariantCulture, out var parsed)
                        && parsed >= 1)
                    {
                        size = Math.Min(parsed, 8);
                        arg = arg.Substring(0, bar);
                    }

                    ScreenCapture.CaptureScreenshot(arg, size);
                    DevToolsPlugin.Log.LogInfo(
                        $"screenshot requested: {arg} at {size}x");
                });
            }
            else if (cmd.Equals("unlocks", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("unlocks", DumpUnlocks);
            }
            else if (cmd.Equals("resolutions", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("resolutions", DumpResolutions);
            }
            else if (cmd.StartsWith("loadlevel:", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Substring("loadlevel:".Length);
                SafeRun("loadlevel", () => LoadLevel(arg));
            }
            else if (cmd.StartsWith("spriteexport:", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Substring("spriteexport:".Length);
                SafeRun("spriteexport", () => ExportSprites(arg));
            }
            else if (cmd.StartsWith("spritegrid:", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Substring("spritegrid:".Length);
                SafeRun("spritegrid", () => ShowSpriteGrid(arg));
            }
            else if (cmd.Equals("newsprites", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("newsprites", DumpNewSprites);
            }
            else if (cmd.StartsWith("sprites", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Length > 8 ? cmd.Substring(8) : "";
                SafeRun("sprites", () => DumpSprites(arg));
            }
            else if (cmd.Equals("endings", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("endings", DumpEndings);
            }
            else if (cmd.StartsWith("setres", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Length > 7 ? cmd.Substring(7) : "";
                SafeRun("setres", () => SetResolution(arg));
            }
            else if (cmd.StartsWith("jiggle", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Length > 7 ? cmd.Substring(7) : "";
                SafeRun("jiggle", () => JigglePieces(arg));
            }
            else if (cmd.Equals("sections", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("sections", DumpSections);
            }
            else if (cmd.StartsWith("reorder:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("reorder", () => ReorderTrack(cmd.Substring(8)));
            }
            else if (cmd.StartsWith("gensweep:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("gensweep", () => StartGenSweep(cmd.Substring(9)));
            }
            else if (cmd.StartsWith("marksolved:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("marksolved",
                        () => MarkSolved(cmd.Substring("marksolved:".Length)));
            }
            else if (cmd.StartsWith("inert:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("inert", () => Phase0.Inert(cmd.Substring(6)));
            }
            else if (cmd.StartsWith("lockcard:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("lockcard", () => Phase0.LockCard(cmd.Substring(9)));
            }
            else if (cmd.StartsWith("clickcard:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("clickcard", () => Phase0.ClickCard(cmd.Substring(10)));
            }
            else if (cmd.Equals("cardlabels", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("cardlabels", () => CardLabels.Apply(true));
            }
            else if (cmd.Equals("cardlabels:off", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("cardlabels", () => CardLabels.Apply(false));
            }
            // Before the bare form: "levelsweep:1235" must not be eaten by
            // an Equals that can never match it, and putting the specific
            // branch first is how this ladder states that.
            else if (cmd.StartsWith("levelsweep:", StringComparison.OrdinalIgnoreCase))
            {
                var only = cmd.Substring("levelsweep:".Length);
                SafeRun("levelsweep", () => _dataTable.Start(only));
            }
            else if (cmd.Equals("levelsweep", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("levelsweep", _dataTable.Start);
            }
            else if (cmd.Equals("tint", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("tint", () => Phase0.TintCards(false));
            }
            else if (cmd.Equals("tint:refresh", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("tint", () => Phase0.TintCards(true));
            }
            else if (cmd.StartsWith("clickbutton:", StringComparison.OrdinalIgnoreCase))
            {
                var name = cmd.Substring("clickbutton:".Length);
                SafeRun("clickbutton", () => ClickButton(name));
            }
            else if (cmd.StartsWith("unlockto:", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Substring("unlockto:".Length);
                SafeRun("unlockto", () => UnlockTo(arg));
            }
            else if (cmd.StartsWith("marker:", StringComparison.OrdinalIgnoreCase))
            {
                var mode = cmd.Substring("marker:".Length);
                SafeRun("marker", () => Markers.Apply(mode));
            }
            else if (cmd.StartsWith("iconinfo:", StringComparison.OrdinalIgnoreCase))
            {
                var arg = cmd.Substring("iconinfo:".Length);
                SafeRun("iconinfo", () =>
                {
                    if (int.TryParse(arg, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var n))
                    {
                        Markers.IconInfo(n);
                    }
                    else
                    {
                        DevToolsPlugin.Log.LogWarning($"iconinfo: not an index: {arg}");
                    }
                });
            }
            else if (cmd.Equals("launchtrace", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("launchtrace", LaunchTrace.Toggle);
            }
            else if (cmd.Equals("regstart", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("regstart", RegistrationLog.Start);
            }
            else if (cmd.Equals("regstop", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("regstop", RegistrationLog.Stop);
            }
            else if (cmd.Equals("resetlevels", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("resetlevels", () =>
                {
                    SaveSystem.data.ResetLevelData();
                    SaveSystem.data.AddFirstLevelToCompletionData();
                    SaveSystem.SaveGame();
                    DevToolsPlugin.Log.LogInfo("level completion data reset to a fresh save");
                });
            }
            else
            {
                DevToolsPlugin.Log.LogWarning($"unknown command: {cmd}");
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"command poll failed: {e.Message}");
        }
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

    /// <summary>
    /// Reads one value, turning any IL2CPP-side failure into a marker string
    /// rather than aborting the whole dump. A partial dump beats no dump.
    /// </summary>
    private static string Str(Func<string> f)
    {
        try
        {
            return f() ?? "";
        }
        catch (Exception e)
        {
            return "<err:" + e.GetType().Name + ">";
        }
    }
}
