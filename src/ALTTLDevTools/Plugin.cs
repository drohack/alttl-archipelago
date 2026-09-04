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
    internal static bool RaiseWindow;

    /// <summary>
    /// Write a per-event transcript to alttl-watch.log. Off by default because
    /// ObjectPlaced fires hundreds of times per level load.
    /// </summary>
    internal static bool WatchEvents;

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

        RaiseWindow = Config.Bind(
            "Window",
            "RaiseWindowAtStartup",
            false,
            "Bring the game window to the foreground at startup. Off by default "
            + "because it steals focus from whatever else is being worked on, on "
            + "whichever virtual desktop that happens to be.").Value;

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
            else if (cmd.StartsWith("menu:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("menu", () => GoToMenu(cmd.Substring(5)));
            }
            else if (cmd.StartsWith("shot:", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("shot", () =>
                {
                    var path = cmd.Substring(5);
                    ScreenCapture.CaptureScreenshot(path);
                    DevToolsPlugin.Log.LogInfo($"screenshot requested: {path}");
                });
            }
            else if (cmd.Equals("unlocks", StringComparison.OrdinalIgnoreCase))
            {
                SafeRun("unlocks", DumpUnlocks);
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

    private static void GoToMenu(string arg)
    {
        var gm = GameManager.Instance;
        var parts = arg.Split(':');
        switch (parts[0].ToLowerInvariant())
        {
            case "title":
                gm.SetGameState<Title_GameState>(null, false);
                break;
            case "levels":
                // Levels_GameState builds its own LevelsTrack_MenuData; the
                // state data slot only carries a transition delay.
                gm.SetGameState<Levels_GameState>(null, false);
                break;
            case "archive":
                gm.SetGameState<Archive_GameState>(null, false);
                break;
            case "daily":
                gm.SetGameState<DailyTidy_GameState>(null, false);
                break;
            default:
                DevToolsPlugin.Log.LogWarning($"unknown menu: {arg}");
                return;
        }
        DevToolsPlugin.Log.LogInfo($"menu: {arg}");
    }

    /// <summary>
    /// Marks a level solved in the save exactly the way the game does, so the
    /// unlock rule can be observed rather than guessed. "solve:INDEX" or
    /// "solve:INDEX:solutionId".
    /// </summary>
    /// <summary>
    /// "unlockto:N" gives the first N levels a LevelCompletionData entry, which
    /// IS the unlock condition, so the level select renders them in full colour.
    /// Needed to compare tracker markers: a fresh save shows three unlocked
    /// cards, and the markers only matter on unlocked ones.
    /// </summary>
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

        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        var items = track == null ? null : track.trackItems;
        if (items == null || position < 0 || position >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning(
                $"clicktrack: position {position} is not on the track");
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
    /// The controllers the RUNNING level has registered.
    ///
    /// Registered, not walked from the prefab: only the registered set raises
    /// GameEvent_ObjectControllerSolved, and the two differ - MedicineCabinet
    /// shows 14 on the prefab and 13 at runtime. A location built from the
    /// prefab set would include one that can never be checked.
    /// </summary>
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
        GameEventManager.AddGameEvent<GameEventManager.GameEvent_ObjectControllerSolved>(data);
        DevToolsPlugin.Log.LogInfo($"solve: dispatched ObjectControllerSolved for {name}");
    }

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
                + $" completion={Str(() => s.CompletionInfo == null ? "-" : s.CompletionInfo.CompletionPercentageString)}");
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
            + " unlocked=" + Str(() => li == null ? "-" : li.IsUnlocked.ToString()));
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
