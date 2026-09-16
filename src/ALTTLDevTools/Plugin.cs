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
/// Replaces the campaign level-select contents with an arbitrary level list.
///
/// This is the extension point the game itself uses: ArchiveMenu derives from
/// LevelSelect and overrides exactly these two methods to show a different set
/// of levels in the same track UI. A mod hooking them is doing what the game
/// already does, not fighting it.
/// </summary>
[HarmonyPatch]
public static class LevelSelectOverride
{
    /// <summary>Level indices to show, in order. Null leaves the game alone.</summary>
    public static List<int>? Order;

    public static string SectionTitle = "Randomized";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelSelect), nameof(LevelSelect.SetLevels))]
    public static void AfterSetLevels(LevelSelect __instance)
    {
        if (Order == null) return;
        var lm = GameManager.Instance.levelManager;
        var replacement = new Il2CppSystem.Collections.Generic.List<LevelInterface>();
        foreach (var n in Order)
        {
            var li = lm.GetLevelInterface(n);
            if (li != null) replacement.Add(li);
        }
        __instance.Levels = replacement;
        DevToolsPlugin.Log.LogInfo($"SetLevels override: {replacement.Count} levels");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelSelect), nameof(LevelSelect.SetupSections))]
    public static void AfterSetupSections(LevelSelect __instance)
    {
        if (Order == null) return;
        // One section spanning the whole list, otherwise the chapter
        // partitioning puts our levels back into their original buckets.
        var section = new LevelSelect.Section
        {
            SectionIndex = 1,
            TrackStartIndex = 0,
            SectionTitle = SectionTitle,
            SectionLevels = __instance.Levels,
        };
        var sections = new Il2CppSystem.Collections.Generic.List<LevelSelect.Section>();
        sections.Add(section);
        __instance.Sections = sections;
        DevToolsPlugin.Log.LogInfo($"SetupSections override: 1 section \"{SectionTitle}\"");
    }
}

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

public class DevToolsBehaviour : MonoBehaviour
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
            // Skip DLC levels; an uninstalled DLC throws on load.
            if (idx >= 1100) continue;
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
            else if (cmd.Equals("spritegrid:off", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("spritegrid", () => ShowSpriteGrid(""));
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
            else if (cmd.StartsWith("solve:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("solve", () => MarkSolved(cmd.Substring(6)));
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

    /// <summary>
    /// Whether the game is already in this state, so the transition is a no-op.
    ///
    /// Forcing a state the game is already in is not harmless. SetGameState
    /// closes the active menu on the way, so "menu:levels" while already in
    /// Levels_GameState leaves NOTHING open - and the next menu: command then
    /// calls CloseActiveMenu with no active menu, where the game's own
    /// TransitionMenuOut dereferences null. Measured: 7 failures in 7 attempts,
    /// every one preceded by the game logging
    /// "SetGameState: Levels_GameState already active".
    /// </summary>
    private static bool AlreadyIn(GameManager gm, string stateTypeName)
    {
        try
        {
            var current = gm.GameState == null
                ? null : gm.GameState.GetIl2CppType().Name;
            if (current != stateTypeName) return false;

            DevToolsPlugin.Log.LogInfo($"menu: already in {stateTypeName}, nothing to do");
            return true;
        }
        catch
        {
            return false;      // on doubt, do what was asked
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

    private static void GoToMenu(string arg)
    {
        var gm = GameManager.Instance;
        var parts = arg.Split(':');
        switch (parts[0].ToLowerInvariant())
        {
            case "title":
                if (AlreadyIn(gm, "Title_GameState")) return;
                gm.SetGameState<Title_GameState>(null, false);
                break;
            case "levels":
                if (AlreadyIn(gm, "Levels_GameState")) return;
                // Levels_GameState builds its own LevelsTrack_MenuData; the
                // state data slot only carries a transition delay.
                gm.SetGameState<Levels_GameState>(null, false);
                break;
            case "archive":
                if (AlreadyIn(gm, "Archive_GameState")) return;
                gm.SetGameState<Archive_GameState>(null, false);
                break;
            case "daily":
                if (AlreadyIn(gm, "DailyTidy_GameState")) return;
                gm.SetGameState<DailyTidy_GameState>(null, false);
                break;
            default:
                DevToolsPlugin.Log.LogWarning($"unknown menu: {arg}");
                return;
        }
        DevToolsPlugin.Log.LogInfo($"menu: {arg}");
    }

    /// <summary>
    /// "clickbutton:Name" invokes the onClick of the first Button whose
    /// GameObject is called Name. There is no synthetic mouse input here, so
    /// UI added by another plugin can be exercised from a script - which is
    /// the only way to test the randomizer's own connection pane.
    /// </summary>
    private static void ClickButton(string name)
    {
        foreach (var button in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = button == null ? null : button.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            // Own name OR the parent's: a composite control such as the
            // modal's confirm keeps its Button on a child, so matching only
            // the Button's own GameObject missed it.
            var parentName = b.transform.parent?.gameObject.name ?? "";
            if (!string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parentName, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (Button on {b.gameObject.name})");
            b.onClick.Invoke();
            return;
        }

        // Also the game's own long-press control, which is a UIBehaviour and
        // NOT a Button - the modal's confirm is one, so a Button-only search
        // reported "no active button" for a control plainly on screen.
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UILongPressButton>()))
        {
            var lp = obj == null ? null : obj.TryCast<UILongPressButton>();
            if (lp == null || lp.gameObject == null) continue;
            if (!string.Equals(lp.gameObject.name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!lp.gameObject.activeInHierarchy) continue;

            // The inner Button first - that is where a listener is normally
            // attached - and only then the long-press event.
            var inner = lp.Button;
            if (inner != null)
            {
                DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (inner Button)");
                inner.onClick.Invoke();
                return;
            }
            DevToolsPlugin.Log.LogInfo($"clickbutton: invoking {name} (long press)");
            lp.m_btnAction?.Invoke();
            return;
        }

        DevToolsPlugin.Log.LogWarning($"clickbutton: no active control named {name}");
    }

    /// <summary>
    /// "contextual" asks the running gameplay state where it would return to.
    ///
    /// The screen you land on after a puzzle is whatever ContextualState says,
    /// and it cannot be observed by completing a level from a script - a player
    /// clicks through the completion screen to get there. Asking the question
    /// directly is the only way to check the answer without a mouse.
    /// </summary>
    private static void ReportContextualState()
    {
        var state = GameManager.Instance.GameState;
        if (state == null)
        {
            DevToolsPlugin.Log.LogWarning("contextual: no game state");
            return;
        }

        var gameplay = state.TryCast<Gameplay_GameState>();
        if (gameplay == null)
        {
            DevToolsPlugin.Log.LogInfo(
                $"contextual: not in a level (state is {Str(() => state.GetIl2CppType().Name)})");
            return;
        }

        var back = gameplay.ContextualState();
        DevToolsPlugin.Log.LogInfo(
            "contextual: finishing here would return to "
            + Str(() => back == null ? "<null>" : back.GetIl2CppType().Name));
    }

    /// <summary>
    /// "why:N" explains the badge on the card at track position N.
    ///
    /// A badge is four states wide and says nothing about WHY. When one reads
    /// wrong - a card still half red after it looked finished - the only way to
    /// settle it is to list the locations the card holds and say, for each,
    /// whether it is collected and whether it is reachable.
    ///
    /// Read out of the mod through a file it writes, so the dev tools do not
    /// need to reference it.
    /// </summary>
    private static void WhyBadge(string arg)
    {
        var path = Path.Combine(GameDir, "BepInEx", "alttl-why.txt");
        File.WriteAllText(path, arg.Trim());
        DevToolsPlugin.Log.LogInfo($"why: asked the mod about track position {arg.Trim()}");
    }

    /// <summary>
    /// "menus" lists every menu the game knows about and its state.
    ///
    /// The question this answers is whether each level type gets a different
    /// pause menu, or the same one behaving differently - which decides whether
    /// a mod has to take over one menu or several.
    /// </summary>
    private static void DumpMenus()
    {
        var gm = GameManager.Instance;
        var mm = gm == null ? null : gm.menuManager;
        if (mm == null)
        {
            DevToolsPlugin.Log.LogWarning("menus: no MenuManager");
            return;
        }

        var li = gm!.levelManager == null ? null : gm.levelManager.ActiveLevelInterface;
        DevToolsPlugin.Log.LogInfo(
            "menus: in " + Str(() => li == null ? "<no level>" : li.LevelId)
            + " archived=" + Str(() => li == null ? "?" : li.IsArchived.ToString())
            + " type=" + Str(() => li == null ? "?" : li.LevelType.ToString())
            + " state=" + Str(() => gm.GameState == null
                ? "?" : gm.GameState.GetIl2CppType().Name));

        var menus = mm.Menus;
        DevToolsPlugin.Log.LogInfo($"menus: {(menus == null ? 0 : menus.Count)} registered");
        for (int i = 0; i < (menus == null ? 0 : menus.Count); i++)
        {
            var menu = menus![i];
            if (menu == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"  [{i}] {Str(() => menu.GetIl2CppType().Name)}"
                + $" object={Str(() => menu.gameObject.name)}"
                + $" active={Str(() => menu.gameObject.activeInHierarchy.ToString())}"
                + $" alpha={Str(() => menu.canvas == null ? "?" : menu.canvas.alpha.ToString("0.0"))}");
        }
    }

    /// <summary>
    /// The title screen's whole tree, so nothing on it is decided by guesswork.
    /// Depth-limited: the interesting things are entries and their badges, not
    /// the text objects inside them.
    /// </summary>
    private static void DumpTitle(Transform t, int depth)
    {
        if (depth > 3) return;

        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;

            // Component type names rather than typed lookups: the dev tools
            // deliberately reference as little of Unity's UI as possible.
            var kinds = "";
            foreach (var component in child.GetComponents<Component>())
            {
                if (component == null) continue;
                var name = Str(() => component.GetIl2CppType().Name);
                if (name == "Button" || name == "TextMeshProUGUI") kinds += " " + name;
            }

            DevToolsPlugin.Log.LogInfo(
                new string(' ', (depth + 1) * 2)
                + Str(() => child.name)
                + " active=" + Str(() => child.gameObject.activeSelf.ToString())
                + kinds);

            DumpTitle(child, depth + 1);
        }
    }

    /// <summary>
    /// Does ObjectController.Reset actually move objects back?
    ///
    /// The cat trap is built on it, and "the cat undid N groups" was only ever
    /// observed on puzzles with no progress to undo - which proves nothing.
    /// This records positions, displaces everything, then resets, and prints
    /// all three so the answer is not a matter of opinion.
    /// </summary>
    private static void ResetTest()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.allLevelObjects == null)
        {
            DevToolsPlugin.Log.LogWarning("resettest: no level running");
            return;
        }

        var objects = level.allLevelObjects;
        var sample = Math.Min(3, objects.Count);

        var before = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            before.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        // Shove everything, as a stand-in for a player having moved pieces.
        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj == null) continue;
            obj.transform.localPosition += new Vector3(0.75f, 0.35f, 0f);
        }

        var moved = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            moved.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        // Deliberately does NOT reset here any more. The point of this command
        // is now to leave the level displaced so a real cat trap can be fired
        // at it and the restore observed.
        var after = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            after.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        for (int i = 0; i < sample; i++)
        {
            DevToolsPlugin.Log.LogInfo(
                $"resettest[{i}] before={before[i]} displaced={moved[i]} afterReset={after[i]}");
        }
        DevToolsPlugin.Log.LogInfo(
            "resettest: level displaced. Fire a cat trap now and the objects "
            + "should return to the 'before' positions above.");
    }

    /// <summary>
    /// "clicktrack:N" clicks the card at track POSITION N.
    ///
    /// Distinct from clickcard, which takes a level index. Once the track holds
    /// dividers and a credits card, position is the only way to say "the third
    /// thing on screen" - which is what a player actually clicks.
    /// </summary>
    private static void ClickTrack(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var position))
        {
            DevToolsPlugin.Log.LogWarning($"clicktrack: not a number: {arg}");
            return;
        }

        // The POPULATED track, not merely the first one found.
        //
        // FindObjectOfType returns one arbitrary active instance, and the scene
        // holds more than one LevelsTrack. Picking the wrong (empty) one made
        // every position report "not on the track" - including positions that
        // had just been clicked successfully - while the mod's own log happily
        // said it had built 35 items. Inactive ones count too: the menu is
        // cached and rebuilt, so the live track is not always the active one.
        // Prefer a LIVE track, and only then a populated one.
        //
        // Both halves were learned the hard way. FindObjectOfType returns one
        // arbitrary ACTIVE instance and picked an empty track, so every
        // position reported "not on the track". Widening to
        // FindObjectsOfTypeAll and taking the fullest one fixed that and broke
        // something quieter: the scene keeps stale tracks around, so after a
        // few menu transitions the fullest track is a LEFTOVER. Clicking its
        // icons resolves the level name perfectly and then does nothing at all,
        // because the icon is not the one on screen. A silent no-op is far
        // worse than a warning.
        LevelsTrack? track = null;
        int best = -1;
        bool bestLive = false;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var candidate = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (candidate == null) continue;

            int n;
            bool live;
            try
            {
                n = candidate.trackItems == null ? 0 : candidate.trackItems.Count;
                live = candidate.gameObject != null && candidate.gameObject.activeInHierarchy;
            }
            catch { continue; }

            // A live track always beats a dead one, however full the dead one.
            if (live != bestLive ? live : n > best)
            {
                best = n;
                bestLive = live;
                track = candidate;
            }
        }

        // Refuse to click a dead track rather than doing it silently.
        //
        // After a level completes, the level select takes a moment to come up:
        // the game state already says Levels_GameState while every LevelsTrack
        // is still inactive. Clicking then resolved the level name correctly
        // and did absolutely nothing, so a scripted run looked like the game
        // was ignoring it. Saying "not up yet" turns a silent no-op into
        // something a caller can wait on and retry.
        if (track == null || !bestLive)
        {
            DevToolsPlugin.Log.LogWarning(
                $"clicktrack: the track is not up yet (best was a"
                + $" {(track == null ? "missing" : "DEAD")} track of {best} item(s))");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"clicktrack: using a live track of {best} item(s)");

        var items = track == null ? null : track.trackItems;
        if (items == null || position < 0 || position >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning(
                $"clicktrack: position {position} is not on the track"
                + $" (best track found had {best} item(s))");
            return;
        }

        var icon = items[position];
        DevToolsPlugin.Log.LogInfo(
            $"clicktrack: position {position} is {Str(() => icon.level.LevelId)}");

        // OnPointerClick, not DoStartLevel. A real click enters here and does
        // selection and transition work on the way; going straight to
        // DoStartLevel skips all of it, which made a card that breaks the menu
        // on a real click look perfectly fine under test.
        icon.OnPointerClick(null);
    }

    /// <summary>
    /// "focus:N" hovers the Nth card, and reports what the menu header says.
    ///
    /// A mouse cannot be scripted here, and whether hovering shows a level's
    /// name is exactly the kind of thing that has to be observed rather than
    /// reasoned about.
    /// </summary>
    private static void FocusIcon(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            DevToolsPlugin.Log.LogWarning($"focus: not a number: {arg}");
            return;
        }

        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        var items = track == null ? null : track.trackItems;
        if (items == null || index < 0 || index >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning("focus: no such card");
            return;
        }

        var icon = items[index];
        icon.OnFocus();
        icon.IconFocus();

        var select = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        DevToolsPlugin.Log.LogInfo(
            $"focus: card {index} is {Str(() => icon.level.LevelId)}"
            + $"; title=\"{Str(() => select.menuTitle.text)}\""
            + $" subtitle=\"{Str(() => select.menuSubtitle.text)}\"");
    }

    /// <summary>
    /// Write the whole level's layout to a file, for diffing.
    ///
    /// Records the PARENT and the placed flag beside the position, because
    /// position alone is what made the last two rounds of cat-trap testing lie.
    /// A piece posted into an envelope and then "restored" sat at the correct
    /// coordinates while still parented to the envelope, so every position-only
    /// check passed and dragging the envelope still dragged the piece.
    ///
    /// World position as well as local: a piece can be at the right LOCAL
    /// offset under the wrong parent and be in completely the wrong place on
    /// screen, which is precisely the failure a local-only dump hides.
    ///
    /// Usage is snapshot, disturb, trap, snapshot, diff the two files. If the
    /// trap put the level back, they are byte-identical.
    /// </summary>
    private static void DumpLayout(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.allLevelObjects == null)
        {
            DevToolsPlugin.Log.LogWarning($"layout: no level running, nothing written for '{tag}'");
            return;
        }

        var safe = new string(tag.Trim().ToCharArray());
        foreach (var bad in Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
        if (safe.Length == 0) safe = "layout";

        var path = Path.Combine(GameDir, "BepInEx", $"alttl-layout-{safe}.tsv");
        var objects = level.allLevelObjects;
        var rows = new List<string> { "idx	name	parent	world	local	rot	placed	active" };

        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj == null) { rows.Add($"{i}	(null)"); continue; }
            var t = obj.transform;
            rows.Add(string.Join("	", new[]
            {
                i.ToString(),
                Str(() => obj.gameObject.name),
                Str(() => t.parent == null ? "(root)" : t.parent.name),
                // Rounded: physics settles to values that wobble in the last
                // decimal place between frames, and an exact dump would report
                // a difference on every run whether or not anything moved.
                Str(() => Round(t.position)),
                Str(() => Round(t.localPosition)),
                Str(() => t.localEulerAngles.z.ToString("F1")),
                Str(() => obj.placed.ToString()),
                Str(() => obj.gameObject.activeInHierarchy.ToString()),
            }));
        }

        File.WriteAllLines(path, rows);
        DevToolsPlugin.Log.LogInfo(
            $"layout: wrote {objects.Count} object(s) for '{safe}'"
            + $" level={Str(() => li!.LevelId)}"
            + $" instance={Str(() => level.GetInstanceID().ToString())} -> {path}");
    }

    private static string Round(Vector3 v)
        => $"({v.x.ToString("F2")}, {v.y.ToString("F2")}, {v.z.ToString("F2")})";

    /// <summary>
    /// Click a control the way a pointer would, not by invoking onClick.
    ///
    /// clickbutton invokes Button.onClick and returns as soon as it finds a
    /// Button. That is not the same as clicking: the level-select tutorial's
    /// confirm reads "Okay", IS a Button, and has nothing attached to onClick -
    /// invoking it four times left the modal on page 1 of 3 while the log
    /// cheerfully reported four successful clicks. The behaviour lives on a
    /// pointer handler instead.
    ///
    /// So this dispatches a real pointer-click through the EventSystem, which
    /// is what every IPointerClickHandler in the game is actually listening
    /// for, and reports how many handlers received it - zero being the answer
    /// that matters, since that is the case clickbutton reported as success.
    /// </summary>
    private static void PressControl(string name)
    {
        name = name.Trim();
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = obj == null ? null : obj.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            var parent = "";
            try { parent = b.transform.parent == null ? "" : b.transform.parent.gameObject.name; }
            catch { }
            if (!string.Equals(b.gameObject.name, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parent, name, StringComparison.OrdinalIgnoreCase)) continue;

            var data = new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current);
            data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;

            UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            var got = UnityEngine.EventSystems.ExecuteEvents.Execute(
                b.gameObject, data,
                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

            DevToolsPlugin.Log.LogInfo(
                $"press: {name} - pointer click {(got ? "handled" : "NOT handled")}");
            return;
        }
        DevToolsPlugin.Log.LogWarning($"press: no active control named {name}");
    }

    /// <summary>
    /// Open the in-level pause menu, the way the game does.
    ///
    /// Needed because a scripted run cannot press Escape, and every cheaper
    /// route was wrong: the menu has no Show/Open/Toggle, FindObjectOfType
    /// cannot see it because it is inactive while closed, and calling
    /// ShowHideMenuItems directly throws - the game dereferences the
    /// GameEventData a caller has no way to construct. PostOpenMenuEvent is
    /// what the game itself posts.
    ///
    /// This is what unblocks testing anything WITH the pause menu open, which
    /// until now could only be described rather than checked.
    /// </summary>
    private static void OpenPauseMenu()
    {
        var gm = GameManager.Instance;
        var mm = gm == null ? null : gm.menuManager;
        if (mm == null)
        {
            DevToolsPlugin.Log.LogWarning("pause: no menu manager");
            return;
        }

        // Inactive objects included: closed is exactly the state it is in.
        MainMenu? menu = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
        {
            menu = obj == null ? null : obj.TryCast<MainMenu>();
            if (menu != null) break;
        }

        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning(
                "pause: no MainMenu - it exists only while a level is running");
            return;
        }

        // Raise the game's own MenuOpen event, the same way solve: raises
        // ObjectControllerSolved.
        //
        // PostOpenMenuEvent was tried first, with null MenuData and then with a
        // real one. Both were accepted silently and opened nothing - the same
        // "reported success, did nothing" shape as everything else in this
        // harness, which is why the buttons dump is checked afterwards rather
        // than the call's return.
        var data = new GameEventManager.GameEventData
        {
            Menu = menu,
            LevelInterface = GameManager.Instance.levelManager.ActiveLevelInterface,
        };
        DevToolsPlugin.Log.LogInfo("pause: raising MenuOpen");
        GameEventManager.AddGameEvent<GameEventManager.GameEvent_MenuOpen>(data);
    }

    /// <summary>
    /// Does the running level actually have a hint?
    ///
    /// The question is whether a Hint item is worth minting: hints are authored
    /// per level as IMAGES, so a procedurally generated layout may have none,
    /// or may have one drawn for a different arrangement. Reading the values
    /// beats reasoning about it.
    /// </summary>
    private static void ReportHints()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("hints: no level running");
            return;
        }

        var images = -1;
        try { images = li.HintImages == null ? 0 : li.HintImages.Count; } catch { }

        // The OTHER source of hints, and the reason a level can report zero
        // images and still show a scribble: LevelRandomizer carries its own
        // List<Sprite> RandomizerHints plus a virtual GetRandomizerHints(),
        // which Books and Pencils override. LevelInterface.HintImages is what
        // the level sweep reads, so anything living only here is invisible to
        // the generator's page count.
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelRandomizer>()))
        {
            var rnd = obj == null ? null : obj.TryCast<LevelRandomizer>();
            if (rnd == null || rnd.gameObject == null) continue;
            if (!rnd.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo(
                $"hints: randomizer {rnd.GetIl2CppType().Name}"
                + $" RandomizerHints={Str(() => rnd.RandomizerHints == null ? "null" : rnd.RandomizerHints.Count.ToString())}"
                + $" GetRandomizerHints={Str(() => rnd.GetRandomizerHints() == null ? "null" : rnd.GetRandomizerHints().Count.ToString())}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"hints: {Str(() => li.LevelId)}"
            + $" randomizable={Str(() => li.IsRandomizable.ToString())}"
            + $" daily={Str(() => li.IsDailyTidy.ToString())}"
            + $" available={Str(() => li.HintAvailable.ToString())}"
            + $" used={Str(() => li.HintUsed.ToString())}"
            + $" images={images}");

        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<HintMenu>()))
        {
            var menu = obj == null ? null : obj.TryCast<HintMenu>();
            if (menu == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"hints: menu NumActiveHints={Str(() => menu.NumActiveHints.ToString())}"
                + $" maxIndex={Str(() => menu.m_maxHintIndex.ToString())}"
                + $" pages={Str(() => menu.HintPages == null ? "null" : menu.HintPages.Length.ToString())}"
                + $" isDaily={Str(() => menu.m_isDailyTidyHint.ToString())}");

            var mgr = menu.m_activeHintManager;
            DevToolsPlugin.Log.LogInfo(
                $"hints: manager={(mgr == null ? "null" : "yes")}"
                + (mgr == null ? "" :
                   $" usedAt={Str(() => mgr.hintUsedAtNormal.ToString())}"
                   + $" fullAt={Str(() => mgr.hintFullyCleanedAtNormal.ToString())}"
                   + $" taken={Str(() => mgr.m_hintsTakenIndexes.Count.ToString())}"));

            // Read CanBeWiped per page.
            //
            // This is not just reporting - it is the only way to exercise the
            // randomizer's hint gate from a probe, because the gate is a
            // prefix on this exact property getter. Reading it here goes
            // through the same call the eraser makes, so a page that reports
            // true has genuinely been paid for and a page that reports false
            // has genuinely been refused. Reading it can therefore SPEND a
            // Hint Page, which is intended: that is what makes it a test of
            // the gate rather than a description of it.
            ReportHintPages(menu);
            break;
        }
    }

    /// <summary>
    /// Each hint page, and whether the game would currently let it be wiped.
    /// </summary>
    private static void ReportHintPages(HintMenu menu)
    {
        var pages = menu.HintPages;
        if (pages == null)
        {
            DevToolsPlugin.Log.LogInfo("hints: no page array");
            return;
        }

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (page == null)
            {
                DevToolsPlugin.Log.LogInfo($"hints:   page {i}: null");
                continue;
            }

            var surface = page.CleanableSurface;
            DevToolsPlugin.Log.LogInfo(
                $"hints:   page {i}"
                + $" active={Str(() => page.gameObject.activeSelf.ToString())}"
                + $" surface={(surface == null ? "null" : "yes")}"
                + $" canBeWiped={(surface == null ? "-" : Str(() => surface.CanBeWiped.ToString()))}"
                + $" isCleaned={(surface == null ? "-" : Str(() => surface.IsCleaned.ToString()))}");
        }
    }

    /// <summary>
    /// Drag the notepad's eraser across the current hint page, for real.
    ///
    /// Why this exists rather than a cheaper probe: the randomizer's hint gate
    /// hangs off CleanableSurface.CanBeWiped, and the ONE question that cannot
    /// be answered by reading state is whether the game ever consults that
    /// property while a wipe is actually in progress. Reading CanBeWiped from
    /// a probe answers a different question - it exercises the getter with no
    /// wipe underway, which is exactly the case the gate is meant to ignore.
    ///
    /// So this drives the eraser through the UI drag handlers the player's
    /// mouse would, and lets the game do the rest.
    /// </summary>
    private static void DragTheEraser()
    {
        HintMenu? menu = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<HintMenu>()))
        {
            menu = obj == null ? null : obj.TryCast<HintMenu>();
            if (menu != null) break;
        }
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("erase: no HintMenu");
            return;
        }

        var eraser = menu.Eraser;
        if (eraser == null || eraser.gameObject == null)
        {
            DevToolsPlugin.Log.LogWarning("erase: no Eraser on the menu");
            return;
        }

        var index = menu.m_currentHintIndex;
        var pages = menu.HintPages;
        if (pages == null || index < 0 || index >= pages.Length)
        {
            DevToolsPlugin.Log.LogWarning($"erase: no page at index {index}");
            return;
        }

        var page = pages[index];
        if (page == null)
        {
            DevToolsPlugin.Log.LogWarning($"erase: page {index} is null");
            return;
        }

        // Sweep across the page in screen space. The camera is null for an
        // overlay canvas, which WorldToScreenPoint handles.
        var cam = Camera.main;
        var centre = RectTransformUtility.WorldToScreenPoint(
            cam, page.transform.position);

        DevToolsPlugin.Log.LogInfo(
            $"erase: dragging over page {index} at ({centre.x:F0},{centre.y:F0})");

        var data = new UnityEngine.EventSystems.PointerEventData(
            UnityEngine.EventSystems.EventSystem.current);
        data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        data.position = centre;
        data.pressPosition = centre;

        var go = eraser.gameObject;
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);

        for (int step = -6; step <= 6; step++)
        {
            var at = new Vector2(centre.x + step * 40f, centre.y + step * 12f);
            data.delta = new Vector2(40f, 12f);
            data.position = at;
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                go, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
        }

        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            go, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);

        var surface = page.CleanableSurface;
        DevToolsPlugin.Log.LogInfo(
            $"erase: done, page {index}"
            + $" isCleaned={(surface == null ? "-" : Str(() => surface.IsCleaned.ToString()))}"
            + $" beingWiped={(surface == null ? "-" : Str(() => surface.IsBeingWiped().ToString()))}");
    }

    /// <summary>
    /// Start any level by index, ignoring the run entirely.
    ///
    /// For getting at a puzzle's OBJECT sprites, which are the only art in
    /// the game that actually depicts a mechanic. They are loaded with their
    /// level and not before, so the title screen sees none of them - a dump
    /// there finds the level-select card icons and the badge elements and
    /// nothing else.
    ///
    /// RUN THIS WITH NO RUN ACTIVE. Track's StartLevel prefix rewrites the
    /// index to whatever slot the run intends, which is the whole point of
    /// it; it returns early when there is no run, so clearing the slot name
    /// first is what makes this command mean what it says.
    ///
    /// forceReload, because asking for the level that is already loaded
    /// otherwise does nothing and looks like the command failed.
    /// </summary>
    private static void LoadLevel(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out var index))
        {
            DevToolsPlugin.Log.LogWarning("loadlevel: give a level index");
            return;
        }

        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("loadlevel: no LevelManager");
            return;
        }

        manager.StartLevel(index, false, true, 12345);
        DevToolsPlugin.Log.LogInfo($"loadlevel: asked for level {index}");
    }

    /// <summary>Sprite names seen by the last `newsprites` call.</summary>
    private static readonly HashSet<string> _spritesSeen =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Sprite names that have appeared since the last time this was run.
    ///
    /// Loading a level adds its objects to the loaded set, so the DIFFERENCE
    /// is exactly that level's art - no guessing at names, and no wading
    /// through the nine hundred that were already there.
    /// </summary>
    private static void DumpNewSprites()
    {
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        var fresh = new List<string>();

        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0) continue;
            if (_spritesSeen.Add(name)) fresh.Add(name);
        }

        fresh.Sort(StringComparer.Ordinal);
        foreach (var name in fresh) DevToolsPlugin.Log.LogInfo($"newsprites:  {name}");
        DevToolsPlugin.Log.LogInfo(
            $"newsprites: {fresh.Count} new, {_spritesSeen.Count} seen so far");
    }

    /// <summary>
    /// Write named sprites out as PNG files.
    ///
    /// So a human can LOOK at them somewhere other than inside the game.
    /// The grid command puts candidates on screen, which answers "does this
    /// read at icon size", but comparing a dozen options properly means
    /// having the images in hand - droha, reasonably: "is there a way for
    /// me to see these easily? You're just kind of giving the icon name and
    /// a description."
    ///
    /// THROUGH A RENDER TEXTURE, because the game's textures are not
    /// readable. Reading sprite.texture directly throws on an imported
    /// texture without Read/Write enabled, which is all of them; blitting
    /// to a RenderTexture and reading THAT back is the standard way round
    /// it and needs no asset changes.
    ///
    /// Cropped to textureRect, because these are atlased: the whole texture
    /// is a sheet of dozens of sprites, and exporting it would produce the
    /// same sheet a dozen times.
    ///
    ///     spriteexport:Badge1-Books2,badge3-Tape|C:/somewhere
    /// </summary>
    private static void ExportSprites(string arg)
    {
        var parts = arg.Split('|');
        var names = new List<string>();
        foreach (var raw in parts[0].Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0) names.Add(name);
        }
        var dir = parts.Length > 1 ? parts[1].Trim() : "";
        if (dir.Length == 0 || names.Count == 0)
        {
            DevToolsPlugin.Log.LogWarning(
                "spriteexport: give names and a folder, as "
                + "'spriteexport:A,B|C:/folder'");
            return;
        }

        try { System.IO.Directory.CreateDirectory(dir); }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"spriteexport: {e.Message}");
            return;
        }

        var found = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0 || found.ContainsKey(name)) continue;
            if (names.Contains(name)) found[name] = sprite;
        }

        var written = 0;
        foreach (var name in names)
        {
            if (!found.TryGetValue(name, out var sprite)) continue;
            if (WriteSpritePng(sprite, name, dir)) written++;
        }

        DevToolsPlugin.Log.LogInfo(
            $"spriteexport: wrote {written} of {names.Count} to {dir}");
    }

    private static bool WriteSpritePng(Sprite sprite, string name, string dir)
    {
        RenderTexture? rt = null;
        RenderTexture? previous = null;
        Texture2D? readable = null;
        try
        {
            var source = sprite.texture;
            if (source == null) return false;

            var rect = sprite.textureRect;
            var w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var h = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            rt = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);

            previous = RenderTexture.active;
            RenderTexture.active = rt;

            readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // FLIPPED IN Y. Graphics.Blit writes the RenderTexture upside
            // down on D3D relative to the source, while sprite.textureRect
            // is measured from the bottom of the source. Reading at the
            // rect as given returned a neighbouring sprite from the atlas -
            // asking for stacked books and getting a bottle opener.
            var y = source.height - Mathf.RoundToInt(rect.y) - h;
            readable.ReadPixels(new Rect(rect.x, y, w, h), 0, 0, false);
            readable.Apply();

            var bytes = ImageConversion.EncodeToPNG(readable);
            if (bytes == null || bytes.Length == 0) return false;

            // The names carry no path characters today, but a sprite name is
            // the game's to choose and a stray slash would write outside the
            // folder we were given.
            var safe = name;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(bad, '_');
            }

            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(dir, safe + ".png"), bytes);
            return true;
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"spriteexport: {name}: {e.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    private static GameObject? _spriteGrid;

    /// <summary>
    /// Draw named sprites in a grid, so a human can SEE them.
    ///
    /// A sprite name says nothing about silhouette, colour or how it reads
    /// at twenty pixels, and the question this exists for is exactly that:
    /// droha, on picking art for the ability pills, "look for ones easy to
    /// distinguish at a glance". That cannot be answered from a name list,
    /// and it cannot be answered by extracting textures either - the game's
    /// are not readable without a RenderTexture round trip. Putting them on
    /// the screen the game is already drawing sidesteps both problems.
    ///
    /// Each entry is drawn twice - large enough to identify, and at pill
    /// size - because an icon that is obvious at 56 pixels and mud at 20 is
    /// no use for a legend.
    ///
    ///     spritegrid:Badge1-Books1,Badge5-Broom
    ///     spritegrid:off
    /// </summary>
    private static void ShowSpriteGrid(string arg)
    {
        if (_spriteGrid != null)
        {
            UnityEngine.Object.Destroy(_spriteGrid);
            _spriteGrid = null;
        }
        if (string.IsNullOrWhiteSpace(arg) || arg == "off")
        {
            DevToolsPlugin.Log.LogInfo("spritegrid: cleared");
            return;
        }

        var wanted = new List<string>();
        foreach (var raw in arg.Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0) wanted.Add(name);
        }

        var found = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0 || found.ContainsKey(name)) continue;
            if (wanted.Contains(name)) found[name] = sprite;
        }

        var canvasGo = new GameObject("ApSpriteGrid");
        _spriteGrid = canvasGo;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;          // over everything, including toasts
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // An opaque backing, so a pale icon is not judged against whatever
        // happens to be behind it.
        var bg = new GameObject("BG");
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.11f, 0.13f, 0.12f, 1f);

        const int columns = 6;
        const float cell = 300f;
        const float rowHeight = 165f;

        var shown = 0;
        foreach (var name in wanted)
        {
            if (!found.TryGetValue(name, out var sprite)) continue;

            var col = shown % columns;
            var row = shown / columns;
            var x = 40f + col * cell;
            var y = -40f - row * rowHeight;

            AddGridSprite(canvasGo.transform, sprite, x, y, 96f);
            AddGridSprite(canvasGo.transform, sprite, x + 110f, y - 30f, 26f);
            AddGridLabel(canvasGo.transform, name, x, y - 100f, cell - 20f);
            shown++;
        }

        var missing = new List<string>();
        foreach (var n in wanted) if (!found.ContainsKey(n)) missing.Add(n);
        DevToolsPlugin.Log.LogInfo(
            $"spritegrid: showing {shown} of {wanted.Count}"
            + (missing.Count > 0
                ? $"; not loaded: {string.Join(", ", missing)}"
                : ""));
    }

    private static void AddGridSprite(Transform parent, Sprite sprite,
                                      float x, float y, float size)
    {
        var go = new GameObject("s");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(size, size);

        var image = go.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void AddGridLabel(Transform parent, string text,
                                     float x, float y, float width)
    {
        var go = new GameObject("t");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, 40f);

        var label = go.AddComponent<TMPro.TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 16f;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    /// <summary>
    /// Every loaded sprite whose name contains a substring.
    ///
    /// EXISTS SO NOBODY GUESSES A SPRITE NAME AGAIN. The mod borrows the
    /// game's art by name in two places, and the comment history on
    /// Badges.FindStar records two wrong guesses before the right name was
    /// found. Asking the runtime what it has costs one command.
    ///
    /// The immediate use is the ability strip: it draws lettered pills
    /// because the mod ships no art, and the question of whether the game
    /// already has a per-mechanic icon is answerable rather than arguable.
    ///
    ///     sprites star        everything with "star" in the name
    ///     sprites             everything, which is a lot
    /// </summary>
    private static void DumpSprites(string filter)
    {
        filter = (filter ?? "").Trim();

        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        if (all == null)
        {
            DevToolsPlugin.Log.LogWarning("sprites: nothing loaded");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;

            var name = Str(() => sprite.name);
            if (string.IsNullOrEmpty(name)) continue;
            if (filter.Length > 0
                && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            // Deduped: the same sprite is referenced from many objects, and
            // an undeduped dump of this is thousands of identical lines.
            if (!seen.Add(name)) continue;

            DevToolsPlugin.Log.LogInfo($"sprites:  {name}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"sprites: {seen.Count} distinct name(s) of {all.Length} loaded"
            + (filter.Length > 0 ? $" matching '{filter}'" : ""));
    }

    /// <summary>
    /// What screen every level ends on, for all of them at once.
    ///
    /// THE QUESTION THIS ANSWERS. droha: some levels finish on the
    /// three-button panel - restart, pause menu, next arrow - and others drop
    /// you straight into the next puzzle. Nobody knew whether that was the
    /// game's own design or something the mod introduced, and guessing was
    /// how the last three of these went wrong.
    ///
    /// NO LEVEL LOADING. LevelManager.AllLevelInterfaces holds every level's
    /// interface at once, so the whole table can be read from the title
    /// screen in one frame. Loading 111 levels to ask each one a question it
    /// can answer while asleep would take an hour and prove the same thing.
    ///
    /// The flags, and why each is here:
    ///   PreventRetryMenu    authored per level, in the scene data
    ///   DoPreventRetryMenu  the computed answer - authored AND anything else
    ///   ShowRetryMenu       what the game will actually do
    ///   CompleteSilently    finishes with no completion beat at all
    ///   TransitionSilently  moves on with no transition
    ///   IsDailyTidy         the mod forces this false during a run, so a
    ///                       daily-pool level takes a different path than it
    ///                       does in vanilla - the one place the mod is
    ///                       implicated
    ///
    /// Printed as TSV so it can go straight into docs/data/ and be diffed
    /// against apworld/alttl/data/levels.json.
    /// </summary>
    private static void DumpEndings()
    {
        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("endings: no LevelManager yet");
            return;
        }

        // The FIELDS, not AllLevelInterfaces - that one is a method taking a
        // bool whose meaning is not recoverable from a signature-only
        // interop assembly, and guessing an argument is how the last few of
        // these went wrong. m_allLevelInterfaces first because the name says
        // it is the complete set.
        var all = manager.m_allLevelInterfaces;
        var which = "m_allLevelInterfaces";
        if (all == null || all.Length == 0)
        {
            all = manager.LevelInterfaces;
            which = "LevelInterfaces";
        }
        if (all == null || all.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("endings: no level interfaces");
            return;
        }
        DevToolsPlugin.Log.LogInfo($"endings: reading {all.Length} from {which}");

        DevToolsPlugin.Log.LogInfo(
            "endings\tlevelIndex\tlevelId\tpreventRetryMenu\tdoPreventRetryMenu"
            + "\tshowRetryMenu\tcompleteSilently\ttransitionSilently"
            + "\tisDailyTidy\tisHolidayDaily\tisCampaign\tisArchive");

        var panels = 0;
        var silent = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var li = all[i];
            if (li == null) continue;

            // Every read is wrapped: these are computed properties on an
            // interop type, and one of them throwing must not cost the other
            // hundred and ten rows.
            var row = string.Join("\t",
                Num(() => li.LevelIndex),
                Str(() => li.LevelId),
                Flag(() => li.PreventRetryMenu),
                Flag(() => li.DoPreventRetryMenu),
                Flag(() => li.ShowRetryMenu),
                Flag(() => li.CompleteSilently),
                Flag(() => li.TransitionSilently),
                Flag(() => li.IsDailyTidy),
                Flag(() => li.IsHolidayDaily),
                Flag(() => li.IsCampaignLevel),
                Flag(() => li.IsArchiveLevel));

            DevToolsPlugin.Log.LogInfo("endings\t" + row);

            try
            {
                if (li.ShowRetryMenu) panels++;
                if (li.CompleteSilently) silent++;
            }
            catch
            {
                // Counted best-effort; the rows above are the real output.
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"endings: {all.Length} level(s), {panels} showing the retry panel, "
            + $"{silent} completing silently");
    }

    private static string Flag(Func<bool> read)
    {
        try { return read() ? "1" : "0"; }
        catch { return "?"; }
    }

    private static string Num(Func<int> read)
    {
        try { return read().ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }

    /// <summary>
    /// What the game's resolution list actually contains, with indices.
    ///
    /// The save stores the player's choice as an INDEX into this list, and
    /// the list is built from the monitor the game opened on - so the same
    /// number means different things on different displays, and a stale
    /// index silently changes the window size. droha, who worked this out
    /// first: "the game changes the resolution list depending on what
    /// monitor opened it, so a number doesn't help me here."
    ///
    /// Printing the list is the only way to turn "index 0" into something a
    /// person can check.
    /// </summary>
    private static void DumpResolutions()
    {
        var all = Screen.resolutions;
        DevToolsPlugin.Log.LogInfo(
            $"resolutions: {(all == null ? 0 : all.Length)} available, "
            + $"current {Screen.width}x{Screen.height}, "
            + $"fullScreen={Screen.fullScreen} mode={Screen.fullScreenMode}");

        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var r = all![i];
            var here = r.width == Screen.width && r.height == Screen.height
                ? "  <- current size" : "";
            DevToolsPlugin.Log.LogInfo(
                $"resolutions:  [{i}] {r.width}x{r.height}{here}");
        }

        DumpGameList();
        DumpSavedChoice();
    }

    /// <summary>
    /// The GAME's own resolution list, which is not Unity's.
    ///
    /// SettingsMenu builds its own list for the dropdown, and Prefs.resolution
    /// is an index into THAT, not into Screen.resolutions. Unity's list had
    /// 135 entries here - every size repeated once per refresh rate - and no
    /// dropdown shows 135 rows, so the two cannot be the same list, and an
    /// index read against the wrong one is meaningless.
    ///
    /// This is the list that has to be indexed to answer "what is the saved
    /// choice actually asking for".
    /// </summary>
    private static void DumpGameList()
    {
        var menu = FindSettingsMenu();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogInfo(
                "resolutions: no SettingsMenu in the scene, so the game's own "
                + "list cannot be read from here");
            return;
        }

        var list = menu.resolutions;
        if (list == null)
        {
            DevToolsPlugin.Log.LogInfo(
                "resolutions: SettingsMenu found but its list is null - it is "
                + "populated when the settings screen opens");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"resolutions: game list has {list.Count} entry(s)");
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var here = r.width == Screen.width && r.height == Screen.height
                ? "  <- current size" : "";
            DevToolsPlugin.Log.LogInfo(
                $"resolutions:  game[{i}] {r.width}x{r.height}{here}");
        }
    }

    /// <summary>What the save believes the player picked.</summary>
    private static void DumpSavedChoice()
    {
        try
        {
            var data = SaveSystem.data;
            if (data == null || data.playerPrefs == null)
            {
                DevToolsPlugin.Log.LogInfo("resolutions: no save data loaded yet");
                return;
            }

            var prefs = data.playerPrefs;
            DevToolsPlugin.Log.LogInfo(
                $"resolutions: saved choice is index {prefs.resolution}, "
                + $"fullscreen={prefs.fullscreen}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning(
                $"resolutions: could not read the save: {e.Message}");
        }
    }

    /// <summary>
    /// SettingsMenu, whether or not its screen is open.
    ///
    /// FindObjectOfType only sees ACTIVE objects and the settings screen
    /// spends nearly all its life switched off, so this uses
    /// FindObjectsOfTypeAll, which does not.
    /// </summary>
    private static SettingsMenu? FindSettingsMenu()
    {
        try
        {
            // The interop shim only offers the non-generic overload, and it
            // hands back UnityEngine.Object, so the type comes from
            // Il2CppType and the result needs a TryCast.
            var found = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<SettingsMenu>());
            if (found == null) return null;
            for (int i = 0; i < found.Length; i++)
            {
                var menu = found[i]?.TryCast<SettingsMenu>();
                if (menu != null) return menu;
            }
            return null;
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning(
                $"resolutions: could not find the menu: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Set the resolution BY SIZE, through the game's own setter.
    ///
    ///     setres 1280 720
    ///
    /// By size and not by index, because the index is not a stable name for
    /// anything. droha, after a run of tests that each reported a different
    /// number for the same window: "the game changes the resolution list
    /// depending on what monitor opened it, so a number doesn't help me
    /// here. What did we say about indexes - they change. Don't use that as
    /// valid information." So the width and height are the input, and the
    /// index is looked up against the list the game has built right now.
    ///
    /// Through SettingsMenu.SetResolution rather than Screen.SetResolution,
    /// so the game persists the choice the same way it does when a person
    /// picks it from the dropdown. Calling Unity directly would move the
    /// window and leave the game's own idea of the setting untouched, which
    /// is exactly the split that made this bug hard to see.
    /// </summary>
    private static void SetResolution(string arg)
    {
        var parts = (arg ?? "").Replace(":", " ").Replace("x", " ")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var height))
        {
            DevToolsPlugin.Log.LogWarning("setres: give a size, as 'setres 1280 720'");
            return;
        }

        var menu = FindSettingsMenu();
        if (menu == null || menu.resolutions == null)
        {
            DevToolsPlugin.Log.LogWarning(
                "setres: no SettingsMenu with a populated list; open the "
                + "settings screen once so the game builds it");
            return;
        }

        var list = menu.resolutions;
        var index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].width != width || list[i].height != height) continue;
            index = i;
            break;
        }

        if (index < 0)
        {
            DevToolsPlugin.Log.LogWarning(
                $"setres: {width}x{height} is not in the game's list of "
                + $"{list.Count}; run 'resolutions' to see what is");
            return;
        }

        menu.SetFullscreen(false);
        menu.SetResolution(index);

        // AND THE SAVE, which is a separate store that can disagree.
        // SettingsMenu.SetResolution moves the window and writes Unity's
        // registry; it does not appear to touch Prefs. Setting only one of
        // the two is how the save came to hold index 0 - native - while the
        // registry held 1280x720, and the save is the one that wins at boot.
        var persisted = "not saved";
        try
        {
            var data = SaveSystem.data;
            if (data != null && data.playerPrefs != null)
            {
                data.playerPrefs.SetResolution(index);
                data.playerPrefs.SetFullscreen(false);
                SaveSystem.SaveGame();
                persisted = $"save now holds index {data.playerPrefs.resolution}";
            }
        }
        catch (Exception e)
        {
            persisted = $"save write failed: {e.Message}";
        }

        DevToolsPlugin.Log.LogInfo(
            $"setres: asked the game for {width}x{height} (its index {index}), "
            + $"windowed; {persisted}");
    }

    /// <summary>
    /// What state the credits card is actually in on the level select.
    ///
    /// droha: "when i went back to the level select i see a level with a hand
    /// print as the icon. it's greyed out like I can't play it. I think it's
    /// the credits but I can't tell."
    ///
    /// The suspicion to test is that Track.ApplyUnlocks creates completion
    /// data for the chapter dividers and for the run's open slots, and the
    /// credits card is neither - it is appended to the track separately, after
    /// the loop, so nothing ever sets unlockedOnLevelSelect on it. A card with
    /// no completion row draws locked. This reads the three things that would
    /// settle it rather than inferring from a screenshot.
    /// </summary>
    private static void ReportCreditsCard()
    {
        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no LevelManager");
            return;
        }

        LevelInterface? credits = null;
        var all = manager.m_allLevelInterfaces;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var li = all[i];
                if (li == null) continue;
                var isCredits = false;
                try { isCredits = li.IsCredits; } catch { continue; }
                if (isCredits) { credits = li; break; }
            }
        }

        if (credits == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no credits level found");
            return;
        }

        var has = Str(() =>
            SaveSystem.data.LevelHasCompletionData(credits).ToString());
        var flag = "-";
        try
        {
            if (SaveSystem.data.LevelHasCompletionData(credits))
            {
                var entry = SaveSystem.data.GetLevelCompletionData(credits);
                flag = entry == null
                    ? "no entry" : entry.unlockedOnLevelSelect.ToString();
            }
        }
        catch (Exception e) { flag = "threw: " + e.Message; }

        // The card's OWN art, by name. The mod picks no icon for this card -
        // it puts the game's credits level on the track and the LevelIcon
        // draws whatever that level carries - so naming the sprite settles
        // whether the hand print is authored for the credits or something we
        // caused. droha: "is that for the credits, or you just picked it?"
        var locked = Str(() => credits.LockedIcon == null
            ? "none" : credits.LockedIcon.name);
        var unlocked = Str(() => credits.UnlockedIcon == null
            ? "none" : credits.UnlockedIcon.name);

        DevToolsPlugin.Log.LogInfo(
            $"creditscard: lockedIcon={locked} unlockedIcon={unlocked}");

        DevToolsPlugin.Log.LogInfo(
            "creditscard: id=" + Str(() => credits.LevelId)
            + " index=" + Str(() => credits.LevelIndex.ToString())
            + " isUnlocked=" + Str(() => credits.IsUnlocked.ToString())
            + " hasCompletionData=" + has
            + " unlockedOnLevelSelect=" + flag);
    }

    /// <summary>
    /// How many levels are alive at once. Exactly one is correct.
    ///
    /// THE SYMPTOM, MADE COUNTABLE. A cat trap resetting a puzzle inside a
    /// navigation relaunches the level that was on its way out while the
    /// incoming one is still coming up, and neither is torn down - droha,
    /// watching a deliberately un-guarded build: "oh god 2 levels loaded at
    /// once", with a screenshot of one puzzle drawn straight through another.
    ///
    /// This matters more than the hang it may or may not lead to. A freeze is
    /// a race and reproduces perhaps a third of the time; two live levels is
    /// a state, and a state can be counted on every run and compared between
    /// builds. That turns "did the fix work" from a wait-and-see into a
    /// number.
    ///
    /// AllLevelInterfaces rather than the active one, because the whole point
    /// is the level the game has stopped calling active while it is still
    /// loaded and still listening.
    /// </summary>
    private static void CountLiveLevels()
    {
        var lm = GameManager.Instance?.levelManager;
        if (lm == null)
        {
            DevToolsPlugin.Log.LogWarning("livelevels: no LevelManager");
            return;
        }

        // THE LEVEL OBJECTS IN THE SCENE, not the interface table.
        //
        // The first version walked AllLevelInterfaces(false) asking each for
        // LevelIsLoaded, and reported 0 with a puzzle plainly on screen -
        // that table holds the AUTHORED interface per level, not whatever is
        // instantiated. Level is a real Unity object (`controllers` prints
        // its GetInstanceID), so ask the scene instead.
        //
        // FindObjectsOfType, deliberately, NOT FindObjectsOfTypeAll: the
        // former returns only live, active objects, and a level that has been
        // torn down properly should vanish from it. Counting prefabs and
        // dead assets would defeat the whole point.
        var levels = UnityEngine.Object.FindObjectsOfType<Level>();
        var live = new List<string>();
        if (levels != null)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                var lvl = levels[i];
                if (lvl == null) continue;
                live.Add(Str(() => lvl.name + "#" + lvl.GetInstanceID()));
            }
        }

        var active = lm.ActiveLevelInterface;
        DevToolsPlugin.Log.LogInfo(
            $"livelevels: {live.Count} loaded [{string.Join(", ", live)}]"
            + $" active={(active == null ? "none" : Str(() => active.LevelId))}");
    }

    /// <summary>
    /// "clickat" or "clickat:X,Y" - click wherever the player would click.
    ///
    /// THE LAST STEP OF droha's REPORT, and the one nothing here could do.
    /// "It reset ... and when I clicked anywhere the game fully froze." The
    /// harness reproduces the state before that - a trap resetting inside a
    /// navigation leaves two levels alive at once, which droha saw on screen
    /// as "oh god 2 levels loaded at once" - but it had no way to take the
    /// final action, so every run ended with the game wounded and still
    /// ticking.
    ///
    /// Deliberately NOT aimed at a named control, unlike press:. The report
    /// says ANYWHERE, and with two levels stacked the interesting part is
    /// precisely which of the two the raycast finds and what is still
    /// listening on the one that should have been torn down.
    ///
    /// Screen coordinates, origin bottom-left, defaulting to the middle of
    /// the window.
    /// </summary>
    private static void ClickAt(string arg)
    {
        var at = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var parts = arg.Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var y))
        {
            at = new Vector2(x, y);
        }

        var system = UnityEngine.EventSystems.EventSystem.current;
        if (system == null)
        {
            DevToolsPlugin.Log.LogWarning("clickat: no EventSystem");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(system);
        data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        data.position = at;
        data.pressPosition = at;

        var hits = new Il2CppSystem.Collections.Generic.List<
            UnityEngine.EventSystems.RaycastResult>();
        system.RaycastAll(data, hits);

        if (hits.Count == 0)
        {
            DevToolsPlugin.Log.LogInfo(
                $"clickat: ({at.x:F0},{at.y:F0}) hit nothing at all");
            return;
        }

        var target = hits[0].gameObject;
        data.pointerCurrentRaycast = hits[0];
        data.pointerPressRaycast = hits[0];

        DevToolsPlugin.Log.LogInfo(
            $"clickat: ({at.x:F0},{at.y:F0}) over {hits.Count} object(s), "
            + $"topmost '{(target == null ? "null" : target.name)}'");

        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            target, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

        DevToolsPlugin.Log.LogInfo("clickat: dispatched");
    }

    /// <summary>
    /// Pick pieces up and drop them, for real, one after another.
    ///
    /// EXISTS BECAUSE A BUG NEEDED QUARTER-SECOND TIMING TO REPRODUCE.
    /// Dropping a piece starts a LeanTween settle animation, and a cat trap
    /// landing while one is running used to leave a dead callback throwing
    /// every frame. Asking a human to spring a trap inside that window is not
    /// a test; droha, reasonably: "how do I time that? It needs to be timed
    /// to like the quarter second."
    ///
    /// So this drops piece after piece with a short gap, which keeps SOMETHING
    /// settling for as long as it runs. A trap sent any time during that lands
    /// mid-animation without anyone having to aim.
    ///
    /// A REAL POINTER DRAG, not a flag flip. docs/release-testing.md records
    /// that every short reproducer written for this project used DevTools'
    /// `complete` instead of solving, and all of them came back clean while
    /// the bug reproduced in the full run. The settle tween only exists if a
    /// piece is actually dragged and dropped, so this dispatches the same
    /// pointer sequence the game gets from a mouse.
    ///
    ///     jiggle          every piece in the level, once
    ///     jiggle:5        the first five
    /// </summary>
    private static void JigglePieces(string arg)
    {
        var want = int.MaxValue;
        if (!string.IsNullOrEmpty(arg)
            && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var parsed))
        {
            want = parsed;
        }

        var pieces = UnityEngine.Object.FindObjectsOfType<DragObject>();
        if (pieces == null || pieces.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("jiggle: no DragObject in the scene");
            return;
        }

        // ObjectPlaced is what starts the settle animation, and it is reached
        // by REFLECTION rather than a synthetic drag.
        //
        // The first version dispatched pointerDown/beginDrag/drag/endDrag
        // through the EventSystem and started no tween at all - 24 drags,
        // zero detached tweens - because DragObject has no OnDrag at all: the
        // interop shows OnPointerDown, OnBeginDrag and OnEndDrag but no drag
        // handler, so the sequence never amounted to a placement. Calling the
        // method the crash names is both simpler and exactly on target.
        // Snap(), not ObjectPlaced(GameEventData). The crash lives in a
        // closure inside ObjectPlaced, but that overload wants a game event
        // we have no honest way to synthesise - and Snap is what actually
        // runs the settle: the type carries snapMoveTween, snapEase and
        // m_snapTweenID right beside it.
        System.Reflection.MethodInfo? placed = null;
        foreach (var m in typeof(DragObject).GetMethods(
                     System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Instance))
        {
            if (m.Name != "Snap") continue;
            if (m.GetParameters().Length != 0) continue;
            placed = m;
            break;
        }

        if (placed == null)
        {
            // Say what IS there. "No zero-argument ObjectPlaced" is true and
            // useless; the overload list is what picks the next move.
            DevToolsPlugin.Log.LogWarning(
                "jiggle: no zero-argument ObjectPlaced on DragObject - "
                + "candidates follow");
            foreach (var m in typeof(DragObject).GetMethods(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.Instance))
            {
                var n = m.Name;
                if (n.IndexOf("Place", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Snap", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drag", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                var ps = m.GetParameters();
                var sig = new System.Text.StringBuilder(n).Append('(');
                for (int j = 0; j < ps.Length; j++)
                {
                    if (j > 0) sig.Append(", ");
                    sig.Append(ps[j].ParameterType.Name);
                }
                DevToolsPlugin.Log.LogInfo($"jiggle:   {sig.Append(')')}");
            }
            return;
        }

        var moved = 0;
        var failed = 0;
        for (int i = 0; i < pieces.Length && moved < want; i++)
        {
            var piece = pieces[i];
            if (piece == null || piece.gameObject == null) continue;
            if (!piece.gameObject.activeInHierarchy) continue;

            try
            {
                placed.Invoke(piece, null);
                moved++;
            }
            catch (Exception e)
            {
                if (failed++ == 0)
                {
                    DevToolsPlugin.Log.LogWarning(
                        $"jiggle: ObjectPlaced threw: {e.Message}");
                }
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"jiggle: placed {moved} of {pieces.Length} piece(s), {failed} "
            + "threw; anything settling now is what a trap has to survive");
    }

    /// <summary>
    /// Force the level-select skip prompt on screen, and say what it reads.
    ///
    /// Written to settle a question that a hierarchy scan could not: a label
    /// at Menus/Level Select/Levels Track/Skip Tooltip reads "Skipppable" in
    /// the object tree, but it has a localiser and had never been activated,
    /// so that string may be nothing more than the placeholder baked into the
    /// prefab. What a player actually sees is only knowable by showing it.
    /// </summary>
    private static void ShowSkipTooltip()
    {
        var found = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var track = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (track == null || track.gameObject == null) continue;
            if (!track.gameObject.activeInHierarchy) continue;

            var tip = track.skipTooltip;
            if (tip == null)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"skiptip: {PathOf(track.transform)} has no skipTooltip");
                continue;
            }

            found++;
            DevToolsPlugin.Log.LogInfo(
                $"skiptip: {PathOf(tip.transform)}"
                + $" showing={Str(() => tip.Showing.ToString())}"
                + $" expire={Str(() => tip.skipExpireTime.ToString())}"
                + $" before={Str(() => tip.skipText.text)}");

            tip.Show(true);
            tip.StartSkipTooltip();

            DevToolsPlugin.Log.LogInfo(
                $"skiptip: shown, now reads {Str(() => tip.skipText.text)}"
                + $" live={tip.gameObject.activeInHierarchy}");
        }

        if (found == 0)
        {
            DevToolsPlugin.Log.LogWarning(
                "skiptip: no active LevelsTrack - open the level select first");
        }
    }


    /// <summary>
    /// Every text label whose content matches, anywhere in the loaded scene.
    ///
    /// Written to answer a specific question: the randomizer writes a count
    /// into two pause-menu entries by name, and the entry named "Skip Button"
    /// actually reads "Let It Be". If either caption appears on some OTHER
    /// screen - a stuck-puzzle prompt, a settings row, a tutorial - then that
    /// screen may be showing a label we have edited, or may be a place a count
    /// ought to appear and does not.
    ///
    /// Includes inactive objects, because the screen that matters is usually
    /// the one not currently open.
    /// </summary>
    private static void FindText(string needle)
    {
        needle = needle.Trim();
        if (needle.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("findtext: give me something to look for");
            return;
        }

        var hits = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<TMPro.TextMeshProUGUI>()))
        {
            var label = obj == null ? null : obj.TryCast<TMPro.TextMeshProUGUI>();
            if (label == null || label.gameObject == null) continue;

            string text;
            try { text = label.text ?? ""; } catch { continue; }
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

            hits++;
            var localiser = label.gameObject.GetComponent<
                UnityEngine.Localization.Components.LocalizeStringEvent>();

            DevToolsPlugin.Log.LogInfo(
                $"findtext:   {PathOf(label.transform)}"
                + $" live={label.gameObject.activeInHierarchy}"
                + $" localised={(localiser == null ? "NO" : "yes")}"
                + $" text={text.Replace("\n", " ")}");
        }
        DevToolsPlugin.Log.LogInfo($"findtext: {hits} label(s) matching {needle}");
    }


    /// <summary>
    /// The game's own palette of level background colours.
    ///
    /// This is the catalogue a Background Change Trap indexes into, so its SIZE
    /// decides where the modulo wraps. Worth reading rather than assuming.
    /// </summary>
    private static void ReportBackgroundCatalogue()
    {
        var found = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<ColorSchemesData>()))
        {
            var data = obj == null ? null : obj.TryCast<ColorSchemesData>();
            if (data == null) continue;
            found++;

            var schemes = data.levelColorSchemes;
            DevToolsPlugin.Log.LogInfo(
                $"bgcatalogue: {data.name}"
                + $" levelColorSchemes={(schemes == null ? -1 : schemes.Length)}"
                + $" BackgroundColors={Str(() => data.BackgroundColors.Count.ToString())}");

            // Four ways to reach the same ten colours. Both the obvious ones
            // throw "Index was outside the bounds of the array" through
            // interop while Count and Length report 10 perfectly happily, so
            // this tries each and says which survived - guessing a third time
            // would be worse than measuring once.
            Try("schemes[i].backgroundColor", () =>
            {
                for (int i = 0; i < schemes.Length; i++)
                {
                    var scheme = schemes[i];
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   arr[{i}] {Rgb(scheme.backgroundColor)}");
                }
            });

            Try("BackgroundColors[i]", () =>
            {
                var colours = data.BackgroundColors;
                for (int i = 0; i < colours.Count; i++)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   list[{i}] {Rgb(colours[i])}");
                }
            });

            Try("BackgroundColors foreach", () =>
            {
                var n = 0;
                foreach (var c in data.BackgroundColors)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   iter[{n++}] {Rgb(c)}");
                }
            });

            Try("BackgroundColors.ToArray", () =>
            {
                var arr = data.BackgroundColors.ToArray();
                for (int i = 0; i < arr.Length; i++)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   toarr[{i}] {Rgb(arr[i])}");
                }
            });
        }
        if (found == 0) DevToolsPlugin.Log.LogWarning("bgcatalogue: no ColorSchemesData loaded");
    }


    /// <summary>
    /// A colour as plain numbers.
    ///
    /// Not ColorUtility.ToHtmlStringRGB: every one of four different ways to
    /// read the palette failed with the same "Index was outside the bounds of
    /// the array", including on element zero, and the only thing all four had
    /// in common was that call. Formatting the channels by hand removes it
    /// from the experiment.
    /// </summary>
    private static string Rgb(Color c)
        => $"({c.r:F3},{c.g:F3},{c.b:F3})";


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


    /// <summary>Run a probe and report whether it survived.</summary>
    private static void Try(string what, Action body)
    {
        try
        {
            body();
            DevToolsPlugin.Log.LogInfo($"bgcatalogue: OK   {what}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"bgcatalogue: FAIL {what}: {e.Message}");
        }
    }


    /// <summary>
    /// Write a background colour onto the running level and say what changed.
    ///
    /// The point is to find out whether the WRITE IS READ. Both of these are
    /// plain fields, so assigning them always appears to succeed - the failure
    /// mode is that nothing on screen moves, with a perfectly healthy log. Take
    /// a screenshot after this; never trust the line it prints.
    /// </summary>
    private static void SetLevelBackground(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("bgset: no level running");
            return;
        }

        var text = arg.StartsWith("#") ? arg : "#" + arg;
        if (!ColorUtility.TryParseHtmlString(text, out var wanted))
        {
            DevToolsPlugin.Log.LogWarning($"bgset: cannot parse colour {arg}");
            return;
        }

        DevToolsPlugin.Log.LogInfo(
            $"bgset: before BackgroundColor={Str(() => Rgb(li.BackgroundColor))}"
            + $" Active={Str(() => Rgb(li.ActiveBackgroundColor))}");

        try { li.BackgroundColor = wanted; }
        catch (Exception e) { DevToolsPlugin.Log.LogWarning($"bgset: LevelInterface write threw: {e.Message}"); }

        try
        {
            var level = li.Level;
            if (level != null) level.backgroundColor = wanted;
        }
        catch (Exception e) { DevToolsPlugin.Log.LogWarning($"bgset: Level write threw: {e.Message}"); }

        DevToolsPlugin.Log.LogInfo(
            $"bgset: after  BackgroundColor={Str(() => Rgb(li.BackgroundColor))}"
            + $" Active={Str(() => Rgb(li.ActiveBackgroundColor))}");

        // Whatever actually paints the backdrop, name it. The camera clear
        // colour is the most likely and the cheapest to check.
        try
        {
            var cam = Camera.main;
            if (cam != null)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"bgset: Camera.main clearFlags={cam.clearFlags}"
                    + $" background={Rgb(cam.backgroundColor)}");
            }
        }
        catch { }
    }


    /// <summary>
    /// What draws the pause screen's background. Do not assume one Image.
    /// </summary>
    private static void ReportMenuBackground()
    {
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
        {
            var menu = obj == null ? null : obj.TryCast<MainMenu>();
            if (menu == null || menu.gameObject == null) continue;
            if (!menu.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo($"menubg: MainMenu {PathOf(menu.transform)}");
            var buttons = menu.ButtonsContainer;
            DevToolsPlugin.Log.LogInfo(
                $"menubg: ButtonsContainer {(buttons == null ? "null" : PathOf(buttons))}");
            foreach (var img in menu.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null || img.gameObject == null) continue;
                var rt = img.gameObject.GetComponent<RectTransform>();
                var size = rt == null ? "?" : $"{rt.rect.width:F0}x{rt.rect.height:F0}";
                DevToolsPlugin.Log.LogInfo(
                    $"menubg:   Image {PathOf(img.transform)}"
                    + $" active={img.gameObject.activeSelf}"
                    + $" size={size}"
                    + $" color={Rgb(img.color)}"
                    + $" sprite={(img.sprite == null ? "none" : img.sprite.name)}");
            }
            return;
        }
        DevToolsPlugin.Log.LogWarning("menubg: no active MainMenu - open the pause menu first");
    }


    /// <summary>
    /// Fire the game's hint-taken path directly.
    ///
    /// The randomizer charges a Hint Page in a postfix on
    /// LevelInterface.HintTaken, and that postfix has never been observed
    /// firing: a synthetic pointer drag reaches the eraser's drag handlers but
    /// never puts the surface into a wiping state, so the game never gets as
    /// far as raising the event. Calling the method exercises the postfix end
    /// to end, which leaves only "does the game call it when you scrub" - and
    /// HintsTakenCounter.CheckHintTaken subscribes to the same event to keep a
    /// Steam stat, so it demonstrably does.
    /// </summary>
    private static void RaiseHintTaken()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("hinttaken: no level running");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"hinttaken: calling HintTaken on {Str(() => li.LevelId)}");
        li.HintTaken(new GameEventManager.Level_GameEvent.EventData(li, ""));
        DevToolsPlugin.Log.LogInfo("hinttaken: returned");
    }


    /// <summary>
    /// List a game type's members: "members:HintManager" or
    /// "members:HintManager:hint" to filter.
    ///
    /// Built after guessing member names one compile at a time for the third
    /// time in this project. The compile-error oracle works - a wrong name is a
    /// CS1061 - but it answers one guess per build, and the interop assembly
    /// renames things unpredictably, so the guesses are often wrong twice over.
    /// Asking the loaded assembly is instant and exhaustive.
    ///
    /// Ordinary .NET reflection, because the interop assemblies ARE managed
    /// assemblies once the process is up. That is also why this cannot be done
    /// offline: outside the game there is nothing to reflect over.
    /// </summary>
    private static void ListMembers(string arg)
    {
        var parts = arg.Split(':');
        var wanted = parts[0].Trim();
        var filter = parts.Length > 1 ? parts[1].Trim() : "";

        Type? found = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t != null && string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    found = t;
                    break;
                }
            }
            if (found != null) break;
        }

        if (found == null)
        {
            DevToolsPlugin.Log.LogWarning($"members: no type named {wanted}");
            return;
        }

        const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;

        bool Match(string n)
            => filter.Length == 0 || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        DevToolsPlugin.Log.LogInfo($"members: {found.FullName} (base {found.BaseType?.Name})");
        int n = 0;
        foreach (var pr in found.GetProperties(Any))
        {
            if (!Match(pr.Name)) continue;
            DevToolsPlugin.Log.LogInfo($"  P {pr.Name} : {pr.PropertyType.Name}");
            n++;
        }
        foreach (var f in found.GetFields(Any))
        {
            if (!Match(f.Name) || f.Name.StartsWith("NativeFieldInfoPtr_")
                || f.Name.StartsWith("NativeMethodInfoPtr_")) continue;
            DevToolsPlugin.Log.LogInfo($"  F {f.Name} : {f.FieldType.Name}");
            n++;
        }
        foreach (var m in found.GetMethods(Any))
        {
            if (!Match(m.Name) || m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;
            var ps = string.Join(", ", Array.ConvertAll(m.GetParameters(), x => x.ParameterType.Name));
            DevToolsPlugin.Log.LogInfo($"  M {m.Name}({ps}) : {m.ReturnType.Name}");
            n++;
        }
        DevToolsPlugin.Log.LogInfo($"members: {n} shown");
    }

    /// <summary>
    /// Every clickable control currently on screen, with its parent.
    ///
    /// Added after guessing control names twice and being wrong twice - the
    /// tutorial modal's confirm reads "Okay" on screen and is not named Okay,
    /// and the pause menu could not be found at all because it is inactive
    /// while closed. A scripted run has to dismiss whatever a player would
    /// dismiss, and it cannot do that by guessing what the artist called it.
    ///
    /// Parent as well as name because clickbutton matches either, and the
    /// confirm on a modal keeps its Button on a child.
    /// </summary>
    private static void ListButtons()
    {
        int n = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Button>()))
        {
            var b = obj == null ? null : obj.TryCast<UnityEngine.UI.Button>();
            if (b == null || b.gameObject == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            var parent = "";
            try { parent = b.transform.parent == null ? "(root)" : b.transform.parent.gameObject.name; }
            catch { }

            string label = "";
            try
            {
                var t = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (t != null) label = t.text;
            }
            catch { }

            DevToolsPlugin.Log.LogInfo(
                $"  button '{Str(() => b.gameObject.name)}' parent='{parent}' text='{label}'");
            n++;
        }
        DevToolsPlugin.Log.LogInfo($"buttons: {n} active");
    }

    /// <summary>
    /// Every managed object's world bounds, grouped by controller.
    ///
    /// Feeds the blocking question the plan flagged and the generator audit
    /// could not answer: an ability-locked group is dimmed and immovable, so if
    /// one of its objects sits physically on top of a FREE group's objects, a
    /// part check we call reachable may not be. Logic looser than the game is
    /// the dangerous direction, because it makes a seed unwinnable.
    ///
    /// Bounds rather than positions, because overlap is about extent: two
    /// objects can have distant centres and still be stacked. Renderer bounds
    /// are already in world space, so no transform maths is needed here - and
    /// doing it here rather than offline is what keeps this honest, since the
    /// numbers come from the same renderer the player sees.
    ///
    /// This only finds CANDIDATES. Whether an overlap actually prevents solving
    /// the free group depends on where its pieces need to travel, which needs a
    /// person to try. Reported as a list to review, never as a verdict.
    /// </summary>
    private static void DumpBounds(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning($"bounds: no level running for '{tag}'");
            return;
        }

        var safe = tag.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
        if (safe.Length == 0) safe = "bounds";

        var path = Path.Combine(GameDir, "BepInEx", $"alttl-bounds-{safe}.tsv");
        var rows = new List<string> { "controller\ttype\tobject\tcx\tcy\tex\tey" };

        var list = level.objectControllers;
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            var cname = Str(() => oc.gameObject.name);
            var ctype = Str(() => oc.GetIl2CppType().Name);
            var managed = oc.ManagedObjects;
            for (int k = 0; k < (managed == null ? 0 : managed.Count); k++)
            {
                var obj = managed![k];
                if (obj == null) continue;
                var r = obj.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                rows.Add(string.Join("\t", new[]
                {
                    cname, ctype, Str(() => obj.gameObject.name),
                    b.center.x.ToString("F3"), b.center.y.ToString("F3"),
                    b.extents.x.ToString("F3"), b.extents.y.ToString("F3"),
                }));
            }
        }

        File.WriteAllLines(path, rows);
        DevToolsPlugin.Log.LogInfo(
            $"bounds: wrote {rows.Count - 1} object(s) across {list.Count} controller(s)"
            + $" for {Str(() => li!.LevelId)} -> {path}");
    }

    /// <summary>
    /// Every cat-ish component in the running scene.
    ///
    /// The question this answers is "does THIS level have a built-in cat, and
    /// what class is it": CatSwipe turned out to be only a config helper - it
    /// has SetupSwipe, AddSwipeables and the mass and angular settings, but no
    /// trigger - so whatever performs a cat event is a class the static probe
    /// never named. Scanning a real scene names it, once, instead of guessing
    /// class names one compile at a time.
    ///
    /// Deliberately a scene-wide scan rather than a walk of the level's own
    /// object list: a cat that lives outside allLevelObjects is exactly the
    /// case a narrower scan would miss and then report as "no cat here".
    /// </summary>
    private static void ListCats()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var levelId = li == null ? "(none)" : Str(() => li.LevelId);

        var all = UnityEngine.Object.FindObjectsOfType<Component>();
        int hits = 0;
        var seen = new System.Collections.Generic.Dictionary<string, int>();

        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;

            string type;
            try { type = c.GetIl2CppType().Name; }
            catch { continue; }

            if (type.IndexOf("Cat", StringComparison.Ordinal) < 0
                && type.IndexOf("Paw", StringComparison.Ordinal) < 0
                && type.IndexOf("Swipe", StringComparison.Ordinal) < 0) continue;

            hits++;
            seen[type] = seen.TryGetValue(type, out var n) ? n + 1 : 1;
            DevToolsPlugin.Log.LogInfo(
                $"  {type} on '{Str(() => c.gameObject.name)}'"
                + $" active={Str(() => c.gameObject.activeInHierarchy.ToString())}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"cats: level={levelId} components={hits} distinctTypes={seen.Count}");
        foreach (var kv in seen)
        {
            DevToolsPlugin.Log.LogInfo($"cats: type {kv.Key} x{kv.Value}");
        }
    }

    /// <summary>
    /// The controllers the RUNNING level has registered.
    ///
    /// Registered, not walked from the prefab: only the registered set raises
    /// GameEvent_ObjectControllerSolved, and the two differ - MedicineCabinet
    /// shows 14 on the prefab and 13 at runtime. A location built from the
    /// prefab set would include one that can never be checked.
    /// </summary>
    private static void ListControllers()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("controllers: no level running");
            return;
        }

        var list = level.objectControllers;
        // The Level's instance id answers whether re-entering a puzzle reuses
        // the loaded level (progress kept) or rebuilds it (progress lost).
        DevToolsPlugin.Log.LogInfo(
            $"controllers: {list.Count} registered on {Str(() => li!.LevelId)}"
            + $" levelInstance={Str(() => level.GetInstanceID().ToString())}"
            + $" solvedNow={Str(() => level.numSolutions.ToString())}");
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"  [{i}] {Str(() => oc.gameObject.name)}"
                + $" type={Str(() => oc.GetIl2CppType().Name)}"
                + $" solved={Str(() => oc.IsSolved.ToString())}");
        }
    }

    /// <summary>
    /// Force one controller to report itself solved, by index or by name.
    ///
    /// This drives the game's OWN OnSolved, so the event that reaches a mod is
    /// the real one - but it is still a forced solve, not a played one. It
    /// proves the event-to-check path, not that the puzzle is solvable.
    /// </summary>
    private static void SolveController(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("solve: no level running");
            return;
        }

        var list = level.objectControllers;
        ObjectController? target = null;

        if (int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            if (index >= 0 && index < list.Count) target = list[index];
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                var oc = list[i];
                if (oc != null && string.Equals(oc.gameObject.name, arg.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    target = oc;
                    break;
                }
            }
        }

        if (target == null)
        {
            DevToolsPlugin.Log.LogWarning($"solve: no controller matching '{arg}'");
            return;
        }

        var name = Str(() => target.gameObject.name);
        DevToolsPlugin.Log.LogInfo($"solve: forcing {name} solved");

        target.SetSolved(true);

        // Raise the event through the game's own dispatcher rather than calling
        // OnSolved.
        //
        // OnSolved is virtual, and every real controller is a subclass -
        // DraggablesJigsaw here. Calling it on an ObjectController-typed
        // reference through the interop shim invokes the BASE method, which
        // raises nothing: the forced solve looked like it worked, the log said
        // "forcing ... solved", and no event was ever dispatched.
        //
        // Note what this does and does not prove. It exercises a listener's
        // handling of the event exactly as the game would deliver it. It does
        // NOT prove the game raises the event when a puzzle is really solved -
        // only playing one does that.
        var data = new GameEventManager.GameEventData
        {
            ObjectController = target,
            LevelInterface = li,
        };

        try
        {
            GameEventManager.AddGameEvent<GameEventManager.GameEvent_ObjectControllerSolved>(data);
            DevToolsPlugin.Log.LogInfo($"solve: dispatched ObjectControllerSolved for {name}");
        }
        catch (Exception e)
        {
            // NOT AN ERROR, and it used to be logged as one.
            //
            // The throw comes from the GAME, in its own win check:
            //
            //   System.NullReferenceException
            //     at LevelInterface.CheckWinCondition (GameEventData details)
            //     at GameEventManager.TryDispatchEvent
            //     at GameEventManager.AddGameEvent[T]
            //     at DevToolsBehaviour.SolveController
            //
            // No mod code is on that stack. It happens when a solved event is
            // pushed at a level the game already considers finished - which is
            // what re-entering a beaten puzzle and forcing a controller does,
            // and is a state only this command can manufacture.
            //
            // It matters because the release gate counts logged ERRORS, so
            // twelve of these from one revisit pass failed "no solve threw
            // inside the game" on a run where nothing had gone wrong. The
            // first fix was to stop the gate looking at revisit passes at all,
            // which hid real errors along with this one. Logging it honestly
            // is better than teaching the gate to look away.
            //
            // The mod's own behaviour here is already correct and tested: a
            // location it has collected is never sent twice
            // (CheckLedger.Check returns false, CheckLedgerTests pins it).
            DevToolsPlugin.Log.LogInfo(
                $"solve: {name} was already solved as far as the level is "
                + $"concerned, so its win check had nothing to do ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// "unlockto:N" gives the first N levels a LevelCompletionData entry, which
    /// IS the unlock condition, so the level select renders them in full colour.
    /// Needed to compare tracker markers: a fresh save shows three unlocked
    /// cards, and the markers only matter on unlocked ones.
    /// </summary>
    private static void UnlockTo(string arg)
    {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var count))
        {
            DevToolsPlugin.Log.LogWarning($"unlockto: not a number: {arg}");
            return;
        }

        var manager = GameManager.Instance.levelManager;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var li = manager.GetLevelInterface(i);
                if (li == null || SaveSystem.data.LevelHasCompletionData(li)) continue;
                SaveSystem.data.CreateLevelCompletionData(li, null);
                made++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"unlockto: index {i}: {e.Message}");
            }
        }
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"unlockto: created {made} completion entries up to index {count}."
            + " Reopen the level select to see them.");
    }

    /// <summary>
    /// Marks a level solved in the save exactly the way the game does, so the
    /// unlock rule can be observed rather than guessed. "solve:INDEX" or
    /// "solve:INDEX:solutionId".
    /// </summary>
    private static void MarkSolved(string arg)
    {
        var parts = arg.Split(':');
        var index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var solutionId = parts.Length > 1 ? parts[1] : "probe_0";
        var li = GameManager.Instance.levelManager.GetLevelInterface(index);
        SaveSystem.data.SaveLevelData(li, solutionId, true);
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"solve: {li.LevelId} solutionId={solutionId} -> found={li.NumSolutionsFound} solved={li.Solved}");
    }

    /// <summary>
    /// The unlock picture for the base campaign: what is unlocked now, what
    /// the game says it would unlock next, and how the chapters partition the
    /// level list.
    /// </summary>
    private static void DumpUnlocks()
    {
        var gm = GameManager.Instance;
        var lm = gm.levelManager;
        var sb = new StringBuilder();

        sb.AppendLine("levelIndex\tlevelId\ttype\tchapter\tsolutionCount\tfound\tsolved\tcompleted"
                      + "\tisUnlocked\tunlockedOnLevelSelect\thasSaveEntry\tskipped\thintUsed");

        var all = lm.AllLevelInterfaces(false);
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            var idx = Str(() => li.LevelIndex.ToString());
            // Base campaign only: everything else has a synthetic index >= 100.
            if (!int.TryParse(idx, out var n) || n >= 100) continue;
            sb.Append(idx).Append('\t')
              .Append(Str(() => li.LevelId)).Append('\t')
              .Append(Str(() => li.LevelType.ToString())).Append('\t')
              .Append(Str(() => li.ChapterDetails == null ? "" : li.ChapterDetails.chapterNumber.ToString())).Append('\t')
              .Append(Str(() => li.SolutionCount.ToString())).Append('\t')
              .Append(Str(() => li.NumSolutionsFound.ToString())).Append('\t')
              .Append(Str(() => li.Solved.ToString())).Append('\t')
              .Append(Str(() => li.Completed.ToString())).Append('\t')
              .Append(Str(() => li.IsUnlocked.ToString())).Append('\t')
              .Append(Str(() => li.IsUnlockedOnLevelSelect().ToString())).Append('\t')
              .Append(Str(() => SaveSystem.data.LevelHasCompletionData(li).ToString())).Append('\t')
              .Append(Str(() => li.Skipped.ToString())).Append('\t')
              .Append(Str(() => li.HintUsed.ToString()))
              .AppendLine();
        }

        File.WriteAllText(Path.Combine(GameDir, "BepInEx", "alttl-unlocks.tsv"), sb.ToString());

        // Chapter membership, straight from the authored ChapterDetails.
        var chapters = new StringBuilder();
        var chapterInterfaces = lm.GetAllChapterInterfaces();
        for (int i = 0; i < (chapterInterfaces == null ? 0 : chapterInterfaces.Length); i++)
        {
            var ch = chapterInterfaces![i];
            if (ch == null) continue;
            var cd = ch.ChapterDetails;
            var members = new StringBuilder();
            try
            {
                var list = cd.levelIndicesInChapter;
                for (int k = 0; k < (list == null ? 0 : list.Count); k++)
                {
                    if (k > 0) members.Append(',');
                    members.Append(list![k]);
                }
            }
            catch (Exception e) { members.Append("<err:").Append(e.GetType().Name).Append('>'); }

            chapters.AppendLine($"chapter {Str(() => cd.chapterNumber.ToString())}"
                + $"\t{Str(() => cd.chapterTitle)}"
                + $"\tinterfaceIndex={Str(() => ch.LevelIndex.ToString())}"
                + $"\tcompletion={Str(() => ch.ChapterCompletionPercentage.ToString())}%"
                + $"\tmembers=[{members}]");
        }
        File.WriteAllText(Path.Combine(GameDir, "BepInEx", "alttl-chapters.txt"), chapters.ToString());

        // What the game itself thinks comes next. Scoped to the base campaign
        // (indices below 100) so DLC and event levels do not skew the totals.
        var campaign = new Il2CppSystem.Collections.Generic.List<LevelInterface>();
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            try { if (li.LevelIndex < 100) campaign.Add(li); } catch { }
        }
        var info = new SaveData.LevelsCompletionInfo(campaign);
        DevToolsPlugin.Log.LogInfo(
            "campaign: levels=" + Str(() => info.LevelsCount.ToString())
            + " unlocked=" + Str(() => info.UnlockedCount.ToString())
            + " fullyUnlocked=" + Str(() => info.IsFullyUnlocked.ToString())
            + " unsolved=" + Str(() => info.UnsolvedCount.ToString())
            + " allSolved=" + Str(() => info.AllLevelsSolved.ToString())
            + " solutions=" + Str(() => info.SolutionsCount.ToString())
            + " solutionsFound=" + Str(() => info.SolutionsFoundCount.ToString())
            + " completion=" + Str(() => info.CompletionPercentageString)
            + " allSolutionsFound=" + Str(() => info.AllSolutionsFound.ToString())
            + " lastUnlocked=" + Str(() => info.LastUnlockedLevel == null ? "-" : info.LastUnlockedLevel.LevelId)
            + " firstUnsolved=" + Str(() => info.FirstUnsolvedLevel == null ? "-" : info.FirstUnsolvedLevel.LevelId)
            + " toUnlockOnSelect=" + Str(() => info.LevelsToUnlockOnSelect == null
                  ? "-" : info.LevelsToUnlockOnSelect.Count.ToString())
            + " | nextLevelIndex=" + Str(() => lm.GetNextLevelIndex().ToString())
            + " gameCompleteCheck=" + Str(() => lm.GameCompleteCheck().ToString()));

        DevToolsPlugin.Log.LogInfo("unlocks written to alttl-unlocks.tsv / alttl-chapters.txt");
    }

    /// <summary>
    /// Rewrites the level-select track to an arbitrary list of level indices
    /// and rebuilds it. "reorder:1,1013,1017,40,1008,..." - the question being
    /// answered is whether a randomizer can put any puzzle in any slot of the
    /// campaign's own UI, including puzzles the campaign never contains.
    /// </summary>
    private static void ReorderTrack(string csv)
    {
        if (csv.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            LevelSelectOverride.Order = null;
            DevToolsPlugin.Log.LogInfo("reorder: override cleared");
            return;
        }

        var order = new List<int>();
        foreach (var part in csv.Split(','))
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                order.Add(n);
        }
        LevelSelectOverride.Order = order;
        DevToolsPlugin.Log.LogInfo($"reorder: {order.Count} levels queued");

        // The menu object is built once and reused, so reopening it does not
        // re-run Setup. Drive the two rebuild steps directly - the patches
        // hang off them either way.
        var menu = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogInfo("reorder: no live LevelSelect, will apply when one is built");
            return;
        }
        menu.SetLevels();
        menu.SetupSections();
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track != null)
        {
            track.Init();
            track.SetInitialScrollPosition();
        }
        DumpSections();
    }

    /// <summary>
    /// A colour as rrggbb, without ColorUtility - see DumpSections.
    /// </summary>
    private static string Hex(UnityEngine.Color c)
    {
        int r = UnityEngine.Mathf.Clamp((int)(c.r * 255f + 0.5f), 0, 255);
        int g = UnityEngine.Mathf.Clamp((int)(c.g * 255f + 0.5f), 0, 255);
        int b = UnityEngine.Mathf.Clamp((int)(c.b * 255f + 0.5f), 0, 255);
        return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
    }

    /// <summary>Level-select sections, readable only while that menu is open.</summary>
    private static void DumpSections()
    {
        var menu = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("sections: no LevelSelect in the scene - open menu:levels first");
            return;
        }
        var sections = menu.Sections;
        DevToolsPlugin.Log.LogInfo($"LevelSelect: {Str(() => sections == null ? "0" : sections.Count.ToString())} sections,"
            + $" activeSection={Str(() => menu.ActiveSection == null ? "-" : menu.ActiveSection.SectionTitle)},"
            + $" levels in track={Str(() => menu.Levels == null ? "0" : menu.Levels.Count.ToString())}");
        for (int i = 0; i < (sections == null ? 0 : sections.Count); i++)
        {
            var s = sections![i];
            DevToolsPlugin.Log.LogInfo(
                $"  section {Str(() => s.SectionIndex.ToString())} \"{Str(() => s.SectionTitle)}\""
                + $" trackStart={Str(() => s.TrackStartIndex.ToString())}"
                + $" levels={Str(() => s.SectionLevels == null ? "0" : s.SectionLevels.Count.ToString())}"
                + $" completion={Str(() => s.CompletionInfo == null ? "-" : s.CompletionInfo.CompletionPercentageString)}"
                // The colour is what a Background Change Trap moves, and it is
                // the only way to check that without eyeballing a screenshot.
                //
                // Formatted BY HAND. ColorUtility.ToHtmlStringRGB throws
                // IndexOutOfRangeException through interop - the same trap
                // already written up in Backgrounds.Palette, walked into again
                // here, and it reads as the SECTION lookup failing rather than
                // as the formatter failing.
                + $" bg={Str(() => Hex(s.BackgroundColor))}");
        }

        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track != null)
        {
            DevToolsPlugin.Log.LogInfo(
                $"LevelsTrack: items={Str(() => track.trackItems == null ? "0" : track.trackItems.Count.ToString())}"
                + $" levelCount={Str(() => track.LevelCount.ToString())}"
                + $" unlockAll={Str(() => track.UnlockAllLevels.ToString())}"
                + $" scrollable={Str(() => track.Scrollable.ToString())}");
            var items = track.trackItems;
            for (int i = 0; i < (items == null ? 0 : items.Count); i++)
            {
                var it = items![i];
                if (it == null) continue;
                DevToolsPlugin.Log.LogInfo(
                    $"  track[{i}] {Str(() => it.level == null ? "-" : it.level.LevelId)}"
                    + $" unlocked={Str(() => it.isUnlocked.ToString())}"
                    + $" unlockable={Str(() => it.isUnlockable.ToString())}");
            }
        }
    }

    /// <summary>How many more frames the watcher has to run, and its state.</summary>
    private static int _watchFrames;
    private static string _watchLast = "";
    private static Color _watchCam;
    private static bool _watchCamSeen;

    /// <summary>
    /// "watch:SECONDS" - report the level's load flags and the camera's
    /// background colour EVERY FRAME, printing only when something changes.
    ///
    /// Two questions this exists to answer, both of which were being decided
    /// by argument rather than measurement.
    ///
    /// ONE: what do LevelIsLoaded and IsTransitioning actually read outside a
    /// puzzle? The cat trap now HOLDS itself while a level is mid-load, and a
    /// hold that never releases is worse than the freeze it replaced - so the
    /// level select and the post-level screen have to be watched, not assumed.
    ///
    /// TWO: does the game keep repainting Camera.main.backgroundColor after a
    /// level has settled, or only during setup? Backgrounds.Tick writes it on
    /// every differing frame because two one-shot attempts lost to a later
    /// paint. If the paint is a one-time thing at setup, the per-frame poll is
    /// doing nothing for the rest of the puzzle and can stop.
    ///
    /// Change-only output on purpose: a frame-by-frame dump of a ten-second
    /// window is 600 identical lines, and the thing worth seeing is the edges.
    /// </summary>
    private static void StartWatch(string arg)
    {
        var seconds = 10f;
        float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        if (seconds <= 0f) seconds = 10f;

        _watchFrames = Mathf.RoundToInt(seconds * 60f);
        _watchLast = "";
        _watchCamSeen = false;
        DevToolsPlugin.Log.LogInfo(
            $"watch: reporting changes for {seconds:0.#}s ({_watchFrames} frames)");
    }

    /// <summary>One frame of the watcher. Called from Update, cheap when off.</summary>
    private static void TickWatch()
    {
        if (_watchFrames <= 0) return;
        _watchFrames--;

        try
        {
            var gm = GameManager.Instance;
            var lm = gm == null ? null : gm.levelManager;
            var li = lm == null ? null : lm.ActiveLevelInterface;

            var line =
                "gameState=" + (gm == null || gm.GameState == null
                    ? "null" : gm.GameState.GetIl2CppType().Name)
                + " interface=" + (li == null ? "null" : li.LevelId)
                + " loaded=" + (li == null ? "-" : li.LevelIsLoaded.ToString())
                + " transitioning=" + (li == null ? "-" : li.IsTransitioning.ToString())
                + " level=" + (li == null || li.Level == null ? "null" : "present");

            if (line != _watchLast)
            {
                _watchLast = line;
                DevToolsPlugin.Log.LogInfo($"watch: {line}");
            }

            var cam = Camera.main;
            if (cam != null)
            {
                var c = cam.backgroundColor;
                if (!_watchCamSeen || c != _watchCam)
                {
                    _watchCamSeen = true;
                    _watchCam = c;
                    DevToolsPlugin.Log.LogInfo(
                        $"watch: camera={c.r:0.000},{c.g:0.000},{c.b:0.000}");
                }
            }

            if (_watchFrames == 0)
            {
                DevToolsPlugin.Log.LogInfo("watch: finished");
            }
        }
        catch (Exception e)
        {
            _watchFrames = 0;
            DevToolsPlugin.Log.LogWarning($"watch: stopped, {e.Message}");
        }
    }

    private static void ReportState()
    {
        var gm = GameManager.Instance;
        var lm = gm.levelManager;
        var li = lm.ActiveLevelInterface;
        DevToolsPlugin.Log.LogInfo(
            "state: gameState=" + Str(() => gm.GameState == null ? "null" : gm.GameState.GetIl2CppType().Name)
            + " activeLevel=" + Str(() => li == null ? "none" : li.LevelId)
            + " index=" + Str(() => li == null ? "-" : li.LevelIndex.ToString())
            + " seed=" + Str(() => li == null ? "-" : li.RandomSeed.ToString())
            + " solutionCount=" + Str(() => li == null ? "-" : li.SolutionCount.ToString())
            + " found=" + Str(() => li == null ? "-" : li.NumSolutionsFound.ToString())
            + " solved=" + Str(() => li == null ? "-" : li.Solved.ToString())
            + " unlocked=" + Str(() => li == null ? "-" : li.IsUnlocked.ToString())
            + " loaded=" + Str(() => li == null ? "-" : li.LevelIsLoaded.ToString())
            + " transitioning=" + Str(() => li == null ? "-" : li.IsTransitioning.ToString())
            + " level=" + Str(() => li == null || li.Level == null ? "null" : "present"));
    }

    /// <summary>
    /// "boot:INDEX" or "boot:INDEX:SEED" - launch an arbitrary level straight
    /// from wherever we are. If this works for an archive or daily-only level,
    /// a randomizer can place any puzzle anywhere in the run.
    /// </summary>
    private static void Boot(string arg)
    {
        var parts = arg.Split(':');
        var index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var seed = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : -1;

        var gm = GameManager.Instance;
        DevToolsPlugin.Log.LogInfo($"boot: StartLevel(index={index}, seed={seed})");

        // Destroy whatever is loaded first, exactly as the level sweep does.
        //
        // forceReload alone leaves the previous level ALIVE, and its listeners
        // stay subscribed to the global event bus. Booting Radial Dance Party
        // and then hopping to another level left RadialDanceParty.
        // CheckWinCondition attached, so the next solve anywhere threw a
        // NullReferenceException inside it and the solve was lost. The failure
        // surfaced two levels and several minutes away from the boot that
        // caused it, which is what makes it worth doing unconditionally.
        // ActiveLevelInterface WAS NOT ENOUGH, and the gap is a level you have
        // FINISHED. Completing a puzzle moves the game to RetryUI_GameState,
        // at which point the level just beaten is no longer the active one -
        // so this skipped it while its CheckWinCondition stayed subscribed,
        // and every solve in the next level died inside the old level's
        // handler. Measured before the fix: the first level booted after a
        // launch solved cleanly, 21 solves and 0 throws, and every later one
        // threw on all 48.
        //
        // activeInHierarchy is the discriminator and the alternatives are not.
        // scene.IsValid() - the filter used elsewhere in this file - matches
        // all 293 LevelInterface objects, and excluding the 186 prefabs by
        // identity still leaves 107 pooled "<level> Interface(Clone)" objects
        // the level select keeps, every one inactive. Destroying those breaks
        // the levels they belong to: one of them is Bathroom Drawer's, and
        // deleting it on the first boot is exactly why booting that level
        // third came up unwired. Only a level being PLAYED is active.
        var torn = 0;
        foreach (var candidate in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelInterface>()))
        {
            var live = candidate?.TryCast<LevelInterface>();
            if (live == null) continue;
            try
            {
                // activeInHierarchy. Known to be imperfect, and still the
                // best rule found - see the note above and the writeup in
                // docs/verification-log.md.
                //
                // It has one hole: leaving a finished puzzle through the MENUS
                // deactivates it without destroying it, so this skips it and
                // its CheckWinCondition stays subscribed. The log shows it -
                // the boot after a menu exit prints no teardown line, and the
                // solves in the next level throw.
                //
                // Widening to "any LevelInterface with a non-null Level" was
                // tried and is WORSE: chapter headers are LevelInterfaces too,
                // and destroying them left the run loading and "completing"
                // 01__Chapter_HomeSweetHome. Do not reach for that again
                // without a way to tell a chapter from a puzzle.
                if (!live.gameObject.activeInHierarchy) continue;
                DevToolsPlugin.Log.LogInfo(
                    $"boot: tearing down '{Str(() => live.gameObject.name)}'");
                live.ReleaseAssetsAndDestroyLevel();
                torn++;
            }
            catch (Exception e)
            {
                // One level refusing to tear down must not stop the others.
                DevToolsPlugin.Log.LogWarning($"boot: teardown threw: {e.Message}");
            }
        }
        DevToolsPlugin.Log.LogInfo($"boot: tore down {torn} live level(s)");

        gm.SetGameState<Gameplay_GameState>(null, false);
        gm.levelManager.StartLevel(index, true, true, seed);
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

    // ------------------------------------------------------------------ dump

    private static void Dump()
    {
        var gm = GameManager.Instance;
        var j = new Json();

        j.Open();
        j.Prop("gameVersion", Str(() => Application.version));
        j.Prop("dumpedAt", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));

        DumpLevels(j, gm);
        DumpDailyTidy(j, gm);
        DumpArchive(j, gm);
        DumpDlc(j, gm);

        j.Close();

        File.WriteAllText(DumpFile, j.ToString());
        DevToolsPlugin.Log.LogInfo($"dump written to {DumpFile} ({j.ToString().Length} bytes)");
    }

    private static void DumpLevels(Json j, GameManager gm)
    {
        j.Key("levels");
        j.OpenArray();
        try
        {
            var all = gm.levelManager.AllLevelInterfaces(false);
            var count = all == null ? 0 : all.Length;
            DevToolsPlugin.Log.LogInfo($"AllLevelInterfaces: {count}");
            for (int i = 0; i < count; i++)
            {
                var li = all![i];
                if (li == null) continue;
                j.Open();
                j.Prop("levelId", Str(() => li.LevelId));
                j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                j.Prop("levelType", Str(() => li.LevelType.ToString()));
                j.Prop("addressablesKey", Str(() => li.AddressablesKey));
                j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                j.Prop("numSolutionsFound", Str(() => li.NumSolutionsFound.ToString()));
                j.Prop("isUnlocked", Str(() => li.IsUnlocked.ToString()));
                j.Prop("isManualUnlock", Str(() => li.IsManualUnlock.ToString()));
                j.Prop("numStarsReqToUnlock", Str(() => li.NumStarsReqToUnlock.ToString()));
                j.Prop("isCredits", Str(() => li.IsCredits.ToString()));
                j.Prop("isArchived", Str(() => li.IsArchived.ToString()));
                j.Prop("isDailyTidy", Str(() => li.IsDailyTidy.ToString()));
                j.Prop("isRandomizable", Str(() => li.IsRandomizable.ToString()));
                j.Prop("hasColourblindMode", Str(() => li.HasColourblindMode.ToString()));
                j.Prop("keepScore", Str(() => li.KeepScore.ToString()));
                j.Prop("maxScore", Str(() => li.MaxScore.ToString()));
                j.Prop("chapterNumber", Str(() => li.ChapterDetails == null
                    ? "" : li.ChapterDetails.chapterNumber.ToString()));
                j.Prop("chapterTitle", Str(() => li.ChapterDetails == null
                    ? "" : li.ChapterDetails.chapterTitle));
                j.Prop("dlcKey", Str(() => li.DLCDetails == null ? "" : li.DLCDetails.key));
                j.Prop("dailyDateCount", Str(() => li.DailyDates == null
                    ? "0" : li.DailyDates.Length.ToString()));
                j.Close();
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpLevels: {e}");
        }
        j.CloseArray();
    }

    private static void DumpDailyTidy(Json j, GameManager gm)
    {
        j.Key("dailyTidy");
        j.Open();
        try
        {
            var dt = gm.DailyTidyManager;
            j.Prop("initialized", Str(() => dt.DailyTidyInitialized.ToString()));
            j.Prop("dailyDateString", Str(() => dt.DailyDateString));
            j.Prop("completeCount", Str(() => dt.DailyCompleteCount.ToString()));
            j.Prop("currentStreak", Str(() => dt.CurrentStreak.ToString()));
            j.Prop("sequenceRandomSeed", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.randomSeed.ToString()));
            j.Prop("sequenceCount", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.sequenceCount.ToString()));
            j.Prop("levelRepeatMinDays", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.levelRepeatMinDays.ToString()));

            j.Key("levels");
            j.OpenArray();
            var levels = dt.GetDailyTidyLevels(true);
            var n = levels == null ? 0 : levels.Length;
            DevToolsPlugin.Log.LogInfo($"daily tidy levels (incl. holidays): {n}");
            for (int i = 0; i < n; i++)
            {
                var li = levels![i];
                if (li == null) continue;
                j.Open();
                j.Prop("levelId", Str(() => li.LevelId));
                j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                j.Prop("isRandomizable", Str(() => li.IsRandomizable.ToString()));
                j.Prop("isHolidayDaily", Str(() => li.IsHolidayDaily.ToString()));
                j.Prop("dailyDateCount", Str(() => li.DailyDates == null
                    ? "0" : li.DailyDates.Length.ToString()));
                j.Close();
            }
            j.CloseArray();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpDailyTidy: {e}");
        }
        j.Close();
    }

    private static void DumpArchive(Json j, GameManager gm)
    {
        j.Key("archive");
        j.Open();
        try
        {
            var am = gm.ArchiveManager;

            j.Key("groups");
            j.OpenArray();
            var groups = am.ArchiveGroups;
            var gcount = groups == null ? 0 : groups.Count;
            DevToolsPlugin.Log.LogInfo($"archive groups: {gcount}");
            for (int i = 0; i < gcount; i++)
            {
                var g = groups![i];
                if (g == null) continue;
                j.Open();
                j.Prop("groupTitle", Str(() => g.GroupTitle));
                j.Key("levels");
                j.OpenArray();
                var lv = g.Levels;
                for (int k = 0; k < (lv == null ? 0 : lv.Count); k++)
                {
                    var li = lv![k];
                    if (li == null) continue;
                    j.Open();
                    j.Prop("levelId", Str(() => li.LevelId));
                    j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                    j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                    j.Close();
                }
                j.CloseArray();
                j.Close();
            }
            j.CloseArray();

            // Per-level solution ids the archive knows about. This is the one
            // place the game spells out what "a different way to solve it" is
            // actually called.
            j.Key("solutionIds");
            j.Open();
            try
            {
                var dict = am.archiveLevelSolutionsDict;
                if (dict != null)
                {
                    var keys = new List<string>();
                    var ke = dict.Keys.GetEnumerator();
                    while (ke.MoveNext()) keys.Add(ke.Current);
                    DevToolsPlugin.Log.LogInfo($"archive solution dict entries: {keys.Count}");
                    foreach (var key in keys)
                    {
                        var arr = dict[key];
                        var sb = new StringBuilder();
                        for (int i = 0; i < (arr == null ? 0 : arr.Length); i++)
                        {
                            if (i > 0) sb.Append('|');
                            sb.Append(arr![i]);
                        }
                        j.Prop(key, sb.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"solution dict: {e.Message}");
            }
            j.Close();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpArchive: {e}");
        }
        j.Close();
    }

    private static void DumpDlc(Json j, GameManager gm)
    {
        j.Key("dlc");
        j.OpenArray();
        try
        {
            var dm = gm.DLCManager;
            var info = dm.DLCInfo;
            for (int i = 0; i < (info == null ? 0 : info.Count); i++)
            {
                var d = info![i];
                if (d == null) continue;
                j.Open();
                j.Prop("key", Str(() => d.key));
                j.Prop("name", Str(() => d.name));
                j.Prop("appId", Str(() => d.appID.ToString()));
                j.Prop("installed", Str(() => d.Installed.ToString()));
                j.Prop("levelCount", Str(() => d.levelInterfaces == null
                    ? "0" : d.levelInterfaces.Count.ToString()));
                j.Close();
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpDlc: {e}");
        }
        j.CloseArray();
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

    /// <summary>Minimal JSON writer. Avoids a Newtonsoft dependency.</summary>
    private sealed class Json
    {
        private readonly StringBuilder _sb = new();
        private bool _needComma;

        private void Comma()
        {
            if (_needComma) _sb.Append(',');
            _needComma = false;
        }

        public void Open() { Comma(); _sb.Append('{'); }
        public void Close() { _sb.Append('}'); _needComma = true; }
        public void OpenArray() { Comma(); _sb.Append('['); }
        public void CloseArray() { _sb.Append(']'); _needComma = true; }

        public void Key(string k)
        {
            Comma();
            _sb.Append(Quote(k)).Append(':');
        }

        public void Prop(string k, string v)
        {
            Comma();
            _sb.Append(Quote(k)).Append(':').Append(Quote(v));
            _needComma = true;
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public override string ToString() => _sb.ToString();
    }
}
