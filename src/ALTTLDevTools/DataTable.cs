using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Generates the shared level data table that both the apworld and the mod
/// consume, so the two cannot disagree about how many checks a level has.
///
/// It is a RUNTIME sweep, not a prefab walk, and that distinction is load
/// bearing: MedicineCabinet exposes 14 ObjectControllers when you walk the
/// loaded prefab but registers only 13 in Level.objectControllers once the
/// level is running, and only the registered set raises
/// GameEvent_ObjectControllerSolved. A location built from the prefab set
/// would include one that can never be checked.
///
/// Output: BepInEx/alttl-levels.json, copied into apworld/alttl/data/.
/// </summary>
internal sealed class DataTable
{
    // A fixed seed so re-running the sweep produces a comparable table. The
    // generators' controller sets are stable across seeds (verified over 8
    // seeds each) with one exception, noted per level below.
    private const int SweepSeed = 20260901;

    private bool _running;
    private int _pos;
    private int _wait;
    private Il2CppSystem.Threading.Tasks.Task? _task;
    private List<int> _queue = new();
    private readonly StringBuilder _out = new();
    private bool _first = true;
    private int _lastCount = -1;
    private int _stable;

    /// <summary>Frames the controller count must hold steady before reading.</summary>
    private const int StableFrames = 45;

    private static int ControllerCount()
    {
        try
        {
            var li = GameManager.Instance.levelManager.ActiveLevelInterface;
            var level = li == null ? null : li.Level;
            var list = level == null ? null : level.objectControllers;
            return list == null ? 0 : list.Count;
        }
        catch { return -1; }
    }

    internal bool Running => _running;

    private static string GameDir => Path.GetDirectoryName(Application.dataPath)!;
    private static string OutFile => Path.Combine(GameDir, "BepInEx", "alttl-levels.json");

    internal void Start()
    {
        var lm = GameManager.Instance.levelManager;
        var all = lm.AllLevelInterfaces(false);
        _queue = new List<int>();

        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            int idx;
            int solutions;
            bool credits;
            try { idx = li.LevelIndex; solutions = li.SolutionCount; credits = li.IsCredits; }
            catch { continue; }

            // Base game only for v1. DLC levels are defined in the build even
            // when not installed, and loading an uninstalled one throws.
            if (idx >= 1100) continue;
            // Chapter markers and credits carry no checks.
            if (solutions <= 0 || credits) continue;
            _queue.Add(idx);
        }

        _out.Clear();
        _out.AppendLine("{");
        _out.AppendLine("  \"generatedBy\": \"ALTTLDevTools levelsweep\",");
        _out.AppendLine($"  \"gameVersion\": {Json(Application.version)},");
        _out.AppendLine($"  \"sweepSeed\": {SweepSeed},");
        _out.AppendLine("  \"levels\": [");
        _first = true;
        _pos = 0;
        _task = null;
        _running = true;
        DevToolsPlugin.Log.LogInfo($"-- level data sweep: {_queue.Count} levels --");
    }

    internal void Tick()
    {
        if (!_running) return;

        if (_pos >= _queue.Count)
        {
            _out.AppendLine();
            _out.AppendLine("  ]");
            _out.AppendLine("}");
            File.WriteAllText(OutFile, _out.ToString());
            _running = false;
            DevToolsPlugin.Log.LogInfo($"levelsweep complete: {OutFile}");
            return;
        }

        var lm = GameManager.Instance.levelManager;
        var index = _queue[_pos];

        if (_task == null)
        {
            _wait = 0;
            _lastCount = -1;
            _stable = 0;
            // doTransitionIn MUST be true. Radial Dance Party and
            // TupperwareNesting build their controllers during the intro
            // animation (RadialCatIntro, TupperwareTower_Intro), so skipping
            // the transition left them reporting 0 and 2 controllers instead
            // of 13 and 9. Slower, but the alternative is silently missing
            // locations.
            _task = lm.SetActiveLevel(index, true, true, SweepSeed);
            // Every level, not every tenth: when this hung, the last line
            // named a level 10 before the real culprit.
            DevToolsPlugin.Log.LogInfo($"[{_pos + 1}/{_queue.Count}] loading level {index}");
            return;
        }

        _wait++;
        bool done;
        try { done = _task.IsCompleted; } catch { done = true; }
        if (!done && _wait < 600) return;

        // Controllers register themselves in their own Start, and the
        // animation-heavy levels register late while their rings and stacks
        // tween in. So wait for the count to STOP CHANGING rather than
        // guessing a delay.
        //
        // This comment used to claim Radial Dance Party has 13 controllers and
        // TupperwareNesting 9, and that a flat 20-frame settle caught only 0
        // and 2 of them. Re-measured 2026-09-04 by booting both with the same
        // flags the sweep uses (showTransition and forceReload both true) and
        // waiting 25 SECONDS, far longer than any settle: Radial Dance Party
        // registers 0 and TupperwareNesting registers 2 (Lids/TupperwareLids
        // and Stack 1/StackablesZ). Those are the true counts, the table
        // records them, and the mod's runtime-vs-table guard stays quiet on
        // both. The 13 and the 9 were wrong.
        //
        // The wait stays anyway: it costs a few frames, it is the right shape
        // for a value that is built rather than read, and the alternative -
        // guessing a delay - is how a wrong number gets written down.
        int count = ControllerCount();
        // Never record an empty level early: zero is "stable" too, so a level
        // read before its controllers register looks settled and empty. Radial
        // Dance Party really is empty, so this guard costs it 900 frames and
        // changes nothing - that is the right trade, because a level that is
        // genuinely late would otherwise be recorded as having no checks.
        if (count <= 0 && _wait < 900) return;
        if (count != _lastCount)
        {
            _lastCount = count;
            _stable = 0;
            return;
        }
        _stable++;
        if (_stable < StableFrames && _wait < 900) return;

        Record(index);
        Teardown();
        _task = null;
        _pos++;
    }

    /// <summary>
    /// Destroy the level we just read before loading the next one.
    ///
    /// Not optional. Loading level after level on forceReload alone leaves the
    /// previous one alive, and DraggablesOrdered.SetupElasticTargets then does
    /// a Dictionary.Add keyed by GameObject name against a dictionary that
    /// already has the entry:
    ///
    ///   ArgumentException: An item with the same key has already been added.
    ///   Key: Targets (UnityEngine.GameObject)
    ///
    /// which wedges the sweep and leaves the game spinning at 100% CPU inside
    /// LeanTween. The prefab survey got away without this only because it
    /// released every level explicitly.
    /// </summary>
    private void Teardown()
    {
        try
        {
            var li = GameManager.Instance.levelManager.ActiveLevelInterface;
            if (li != null) li.ReleaseAssetsAndDestroyLevel();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"levelsweep: teardown threw: {e.Message}");
        }
    }

    /// <summary>
    /// The level's own cat, if it has one.
    ///
    /// Recorded per level and keyed by level id, so the answer is the same for
    /// every seed: which puzzles ship with a real cat event is a fact about the
    /// game, not about a run. The trap reads it for whatever level is open.
    ///
    /// Scanned under the LEVEL's transform rather than the whole scene on
    /// purpose. A scene-wide scan picks up CatAchievementTracker, which sits on
    /// a global object and is present in every level - it would report a cat
    /// everywhere and mean nothing.
    ///
    /// Inactive children are included: a cat that has not been triggered yet is
    /// exactly the case this is looking for, and a swipe object waiting its
    /// turn may well be switched off.
    /// </summary>
    private static string CatsIn(Level? level)
    {
        var row = new StringBuilder("[");
        if (level == null) return row.Append(']').ToString();

        try
        {
            var found = level.gameObject.GetComponentsInChildren<Component>(true);
            int n = 0;
            for (int i = 0; i < (found == null ? 0 : found.Length); i++)
            {
                var c = found![i];
                if (c == null) continue;

                string type;
                try { type = c.GetIl2CppType().Name; }
                catch { continue; }

                if (type.IndexOf("Cat", StringComparison.Ordinal) < 0
                    && type.IndexOf("Paw", StringComparison.Ordinal) < 0
                    && type.IndexOf("Swipe", StringComparison.Ordinal) < 0) continue;

                if (n > 0) row.Append(", ");
                row.Append('{');
                row.Append($"\"type\": {Json(type)}");
                row.Append($", \"name\": {Json(Str(() => c.gameObject.name))}");
                row.Append($", \"active\": {Bool(() => c.gameObject.activeInHierarchy)}");
                row.Append('}');
                n++;
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"levelsweep: cats threw: {e.Message}");
        }

        return row.Append(']').ToString();
    }

    private void Record(int index)
    {
        var lm = GameManager.Instance.levelManager;
        var li = lm.ActiveLevelInterface;
        var level = li == null ? null : li.Level;

        var row = new StringBuilder();
        row.Append("    {");
        row.Append($"\"levelIndex\": {index}");
        row.Append($", \"levelId\": {Json(Str(() => li == null ? "?" : li.LevelId))}");
        row.Append($", \"source\": {Json(SourceOf(index))}");
        row.Append($", \"solutionCount\": {Str(() => li == null ? "0" : li.SolutionCount.ToString())}");
        row.Append($", \"isRandomizable\": {Bool(() => li != null && li.IsRandomizable)}");
        row.Append($", \"isArchived\": {Bool(() => li != null && li.IsArchived)}");
        row.Append($", \"isDailyTidy\": {Bool(() => li != null && li.IsDailyTidy)}");
        row.Append(", \"controllers\": [");

        int n = 0;
        try
        {
            var list = level!.objectControllers;
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                var oc = list![i];
                if (oc == null) continue;
                if (n > 0) row.Append(", ");
                row.Append('{');
                row.Append($"\"name\": {Json(Str(() => oc.gameObject.name))}");
                row.Append($", \"type\": {Json(Str(() => oc.GetIl2CppType().Name))}");
                row.Append($", \"objects\": {Str(() => oc.ManagedObjects == null ? "0" : oc.ManagedObjects.Count.ToString())}");
                row.Append(", \"dependsOn\": [");
                try
                {
                    var deps = oc.dependencies;
                    int d = 0;
                    for (int k = 0; k < (deps == null ? 0 : deps.Count); k++)
                    {
                        var dep = deps![k];
                        if (dep == null) continue;
                        if (d > 0) row.Append(", ");
                        row.Append(Json(Str(() => dep.gameObject.name)));
                        d++;
                    }
                }
                catch { /* leave the list empty */ }
                row.Append(']');
                row.Append('}');
                n++;
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"levelsweep: controllers for {index} threw: {e.Message}");
        }

        row.Append(']');

        // Hints are authored per level as images, so whether a level HAS one is
        // a fact about the level and belongs in the table beside its
        // controllers. Recorded after a claim was made off a sample of three.
        row.Append($", \"hintAvailable\": {Bool(() => li!.HintAvailable)}");
        row.Append($", \"hintImages\": {Str(() => (li!.HintImages == null ? 0 : li.HintImages.Count).ToString())}");
        row.Append(", \"cats\": ");
        row.Append(CatsIn(level));
        row.Append('}');

        if (!_first) _out.AppendLine(",");
        _out.Append(row);
        _first = false;
    }

    /// <summary>
    /// Which pool a level is drawn from. Indices are the game's own: the base
    /// campaign is below 100, the daily-exclusive generators are 995-1000, and
    /// the seasonal event packs are flagged IsArchived.
    /// </summary>
    private static string SourceOf(int index)
    {
        if (index >= 995 && index <= 1000) return "generator";
        var li = GameManager.Instance.levelManager.GetLevelInterface(index);
        try
        {
            if (li != null && li.IsArchived) return "archive";
            // A campaign level that carries a randomizer is a generator too -
            // the daily reuses it with a fresh seed.
            if (li != null && li.IsRandomizable) return "generator";
        }
        catch { }
        return index < 100 ? "base" : "other";
    }

    private static string Json(string s)
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

    private static string Bool(Func<bool> f)
    {
        try { return f() ? "true" : "false"; } catch { return "false"; }
    }

    private static string Str(Func<string> f)
    {
        try { return f() ?? ""; } catch (Exception e) { return "<err:" + e.GetType().Name + ">"; }
    }
}
