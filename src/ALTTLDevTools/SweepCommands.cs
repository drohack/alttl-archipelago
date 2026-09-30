using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// The two sweeps that run across many levels from the Update loop - the
/// prefab solution survey (`solutions`) and the generator seed sweep
/// (`gensweep`) - and `stop`, which ends any sweep early. The runtime level
/// sweep (`levelsweep`) is DataTable.
/// </summary>
public partial class DevToolsBehaviour
{
    // Solution survey state. Driven from Update rather than a coroutine so no
    // IEnumerator has to be marshalled into the IL2CPP domain.
    private bool _surveying;

    private int _surveyIndex;

    private int _surveyWaitFrames;

    private Il2CppSystem.Threading.Tasks.Task<Level>? _surveyTask;

    private LevelInterface? _surveyLevel;

    private List<LevelInterface> _surveyQueue = new();

    private readonly StringBuilder _surveyOut = new();

    private static string SolutionFile => Path.Combine(GameDir, "BepInEx", "alttl-solutions.tsv");

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
            + "\tsetCount\tsolutionIds\tdependsOn\tnote\tarrangements");
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

    /// <summary>
    /// Every list on the controller whose name holds "olution", with its
    /// count - "solutions=2|validSolutions=0". An ending's id is the
    /// controller's name and the index of the arrangement it matched
    /// ("Ordered_1"), so these counts say how many endings a controller can
    /// give, which no level ever played has to have shown.
    /// </summary>
    private static string Arrangements(ObjectController oc, string typeName)
    {
        var parts = new List<string>();
        try
        {
            var type = FindType(typeName);
            if (type == null) return "(no managed type)";
            var typed = Activator.CreateInstance(type, oc.Pointer);
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var property in t.GetProperties(flags))
                {
                    if (property.GetIndexParameters().Length > 0) continue;
                    if (property.Name.IndexOf("olution", StringComparison.Ordinal) < 0) continue;
                    object? value;
                    try { value = property.GetValue(typed); }
                    catch { continue; }
                    if (value == null) { parts.Add($"{property.Name}=null"); continue; }
                    var count = value.GetType().GetProperty("Count")?.GetValue(value)
                                ?? value.GetType().GetProperty("Length")?.GetValue(value);
                    if (count != null) parts.Add($"{property.Name}={count}");
                    else if (value is int number) parts.Add($"{property.Name}:{number}");
                }
            }
        }
        catch (Exception e)
        {
            parts.Add($"<err:{e.GetType().Name}>");
        }
        return string.Join("|", parts);
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
                    $"{idx}\t{id}\t{solCount}\t{name}\t{type}\t{sets}\t{ids}\t{deps}\t{note}"
                    + $"\t{Arrangements(oc, type)}");
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

    // -------------------------------------------------- generator seed sweep

    private bool _sweeping;

    private int _sweepPos;

    private int _sweepWait;

    private Il2CppSystem.Threading.Tasks.Task? _sweepTask;

    private List<(int index, int seed)> _sweepJobs = new();

    private readonly StringBuilder _sweepOut = new();

    private static string SweepFile => Path.Combine(GameDir, "BepInEx", "alttl-generators.tsv");

    /// <summary>
    /// "gensweep:SEEDS" regenerates every randomizable level under SEEDS
    /// seeds (default 6, at most 100) and records what came out;
    /// "gensweep:SEEDS:INDEX" sweeps just that level. `stop` ends a sweep
    /// early and writes what it has. This is the data the
    /// randomizer's logic needs: whether a generator's solution count is
    /// fixed or varies, which sorting rule it picked, and how big the puzzle
    /// got (the only difficulty signal the game offers, since it ships no
    /// difficulty rating of any kind).
    /// </summary>
    private void StartGenSweep(string arg)
    {
        // SEEDS FIRST. The doc used to read gensweep:<index>, so gensweep:1013
        // named a level to a reader and 1013 seeds of every generator to the
        // code - over sixteen thousand regenerations, with no way to stop
        // them. The cap turns that misreading into a refusal.
        var parts = arg.Split(':');
        var seedCount = int.TryParse(parts[0].Trim(), out var n) ? Math.Max(1, n) : 6;
        if (seedCount > MaxSweepSeeds)
        {
            DevToolsPlugin.Log.LogWarning(
                $"gensweep: {seedCount} seeds is more than {MaxSweepSeeds}; the "
                + "first number is the seed count - gensweep:<seeds>:<index> "
                + "sweeps one level");
            return;
        }
        int? only = null;
        if (parts.Length > 1)
        {
            if (!int.TryParse(parts[1].Trim(), out var wanted))
            {
                DevToolsPlugin.Log.LogWarning($"gensweep: not a level index: {parts[1]}");
                return;
            }
            only = wanted;
        }
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
            if (only != null && idx != only) continue;
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

        if (_sweepJobs.Count == 0)
        {
            DevToolsPlugin.Log.LogWarning(only == null
                ? "gensweep: no randomizable level to sweep"
                : $"gensweep: level {only} is not a randomizable level, or its DLC is not installed");
            return;
        }

        _sweepOut.Clear();
        _sweepOut.AppendLine("levelIndex\tlevelId\tseed\tdeclaredSolutions\tlevelNumSolutions"
            + "\tobjectCount\tcontrollerCount\tcontrollers\trandomizer\tchosenSolutions\tarrangements");
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
        => _sweepOut.AppendLine(GenerationRow(index, seed.ToString()));

    /// <summary>
    /// "rules": gensweep's row for the level on screen, whatever its seed -
    /// gensweep itself only regenerates its fixed seeds (1000 + 7919k). Boot
    /// the seed first (boot:995:945554386), then ask. Built for Books
    /// (Randomized), where which rule is symmetric decides what Draggables_0
    /// means.
    /// </summary>
    private void ReportRules()
    {
        var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("rules: no level is running");
            return;
        }
        var index = Str(() => li.LevelIndex.ToString());
        var seed = Str(() => li.RandomSeed.ToString());
        DevToolsPlugin.Log.LogInfo("rules: levelIndex\tlevelId\tseed\tdeclaredSolutions\tlevelNumSolutions"
            + "\tobjectCount\tcontrollerCount\tcontrollers\trandomizer\tchosenSolutions\tarrangements");
        DevToolsPlugin.Log.LogInfo("rules: " + GenerationRow(int.TryParse(index, out var i) ? i : -1, seed));
    }

    private string GenerationRow(int index, string seed)
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
        // Each controller's name and solution lists AFTER generation: an
        // ending's id is that name and the index of the list entry it matched,
        // so this says which ids a seed can report (Books: Shuffle_0 and
        // Draggables_0 on a symmetric seed, Shuffle_0 and Shuffle_1 elsewhere).
        var arrangements = new StringBuilder();
        var ctrlCount = 0;
        try
        {
            var list = level!.objectControllers;
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                var oc = list![i];
                if (oc == null) continue;
                if (ctrlCount > 0) { controllers.Append('|'); arrangements.Append(';'); }
                var type = Str(() => oc.GetIl2CppType().Name);
                controllers.Append(type);
                arrangements.Append(Str(() => oc.gameObject.name)).Append('[')
                    .Append(Arrangements(oc, type)).Append(']');
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

        return $"{index}\t{id}\t{seed}\t{declared}\t{actual}\t{objects}"
            + $"\t{ctrlCount}\t{controllers}\t{randomizerName}\t{chosen}\t{arrangements}";
    }

    // ------------------------------------------------------------------ stop

    /// <summary>The most seeds one gensweep may ask for.</summary>
    private const int MaxSweepSeeds = 100;

    /// <summary>
    /// "stop": end whichever sweep is running - solutions, levelsweep or
    /// gensweep - and write the rows it has so far. Taken from the command
    /// file even mid-sweep; every other command waits in the file until the
    /// sweep is over, as all of them did before this existed.
    /// </summary>
    private void StopSweeps()
    {
        var stopped = new List<string>();
        if (_surveying)
        {
            StopSurvey();
            stopped.Add("solutions");
        }
        if (_dataTable.Running)
        {
            _dataTable.Stop();
            stopped.Add("levelsweep");
        }
        if (_sweeping)
        {
            File.WriteAllText(SweepFile, _sweepOut.ToString());
            _sweeping = false;
            DevToolsPlugin.Log.LogWarning(
                $"gensweep: STOPPED at {_sweepPos}/{_sweepJobs.Count}; the rows so far "
                + $"are in {SweepFile}");
            stopped.Add("gensweep");
        }
        DevToolsPlugin.Log.LogInfo(stopped.Count == 0
            ? "stop: no sweep is running"
            : $"stop: stopped {string.Join(", ", stopped)}");
    }

    private void StopSurvey()
    {
        // Release the prefab only once it has loaded; one still loading is
        // left to the game rather than pulled out from under it.
        try
        {
            if (_surveyTask == null || _surveyTask.IsCompleted) _surveyLevel?.ReleaseAssetsAndDestroyLevel();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"solutions: release failed: {e.Message}");
        }
        File.WriteAllText(SolutionFile, _surveyOut.ToString());
        _surveying = false;
        _surveyLevel = null;
        _surveyTask = null;
        DevToolsPlugin.Log.LogWarning(
            $"solutions: STOPPED at {_surveyIndex}/{_surveyQueue.Count}; the rows so far "
            + $"are in {SolutionFile}");
    }
}
