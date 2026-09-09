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
        // THE SWEEP IS LOSSY ON PHASED LEVELS AND CANNOT BE FIXED BY WAITING.
        //
        // Two wrong conclusions have been written here, in order, and both
        // came from measuring the same thing twice and calling the agreement
        // proof.
        //
        // The first said Radial Dance Party has 13 controllers and
        // TupperwareNesting 9, and that a flat 20-frame settle caught only 0
        // and 2. The second, 2026-09-04, "refuted" it: booting both with the
        // sweep's own flags and waiting 25 SECONDS still gave 0 and 2, so 0
        // and 2 were recorded as the true counts.
        //
        // They are not. On 2026-09-08 droha played TupperwareNesting in a real
        // seed and the mod's runtime guard watched SEVEN controllers register.
        // These levels reveal their controllers as the player SOLVES the
        // previous group, so no delay reveals anything - 25 seconds and 20
        // frames measure the identical state. Booting a level and waiting IS
        // what the sweep does, which is why re-running it looked like
        // confirmation.
        //
        // SUPERSEDED 2026-09-09, AND THE ANSWER WAS IN THE GAME ALL ALONG.
        // This comment used to name the prefab survey as the authority for
        // what a phased level contains. The survey is only a superset: it sees
        // every authored controller but cannot say which of them the level
        // ever reveals, so it cannot tell a phase from a ghost.
        //
        // The LEVEL DECLARES ITS PHASES. PhasedLevel.phases,
        // TupperwareNesting.GetPhaseControllers() and RadialDanceParty.dances
        // are ordered lists naming exactly which controllers appear and in
        // what order, and the sweep now records them in `phases` below. Three
        // levels in the game have one: PawPrints, TupperwareNesting and Radial
        // Dance Party. Anything in the prefab, absent at boot, and named in no
        // phase list is a ghost - which is now a deduction rather than a
        // judgement call.
        //
        // The sweep is still lossy: it records only phase one, so `phases`
        // exists precisely so a regeneration cannot lose the rest silently.
        // SurveyCrossCheckTests pins the remaining disagreements, and
        // tools/classify-controllers.py turns all of this into one row per
        // controller.
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
    /// <summary>
    /// How many hint pages a level's randomizer supplies, if it has one.
    ///
    /// <paramref name="pool"/> picks between the authored list and what this
    /// generated layout actually selected from it. They differ - Pencils holds
    /// five and uses two - so the generator wants the larger of the two: a
    /// Hint Page pool that is short leaves a page nobody can afford, while one
    /// that is long only leaves a spare, and pages are fungible.
    /// </summary>
    private static int RandomizerHintCount(Level level, bool pool)
    {
        if (level == null) return 0;

        var best = 0;
        foreach (var rnd in level.GetComponentsInChildren<LevelRandomizer>(true))
        {
            if (rnd == null) continue;
            try
            {
                var list = pool ? rnd.RandomizerHints : rnd.GetRandomizerHints();
                if (list != null && list.Count > best) best = list.Count;
            }
            catch
            {
                // A randomizer that throws on either accessor contributes
                // nothing rather than aborting the sweep.
            }
        }
        return best;
    }


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
        // BOTH daily flags, because either one puts a level in the daily pool
        // and only recording the first one badly misleads.
        //
        // IsDailyTidy alone is 16 of the 111 levels a seed can draw. Add the
        // holiday sets - MerryMess, TrickOrTidy, GoodTidings, SomethingEggstra
        // - and it is 36, a third of every run. A guard written against the
        // 16, or worse against the "six daily-exclusive generators", is a
        // guard that misses most of the cases: droha was routed to the Daily
        // Tidy page three separate times before this flag existed to make the
        // real size of the problem visible.
        row.Append($", \"isHolidayDaily\": {Bool(() => li != null && li.IsHolidayDaily)}");
        // WHICH SUBCLASS THIS LEVEL IS. Named levelClass, not levelType:
        // the dump already has a levelType and it means something else (Puzzle
        // versus Chapter). One string that names every phased
        // and bespoke level for free - PhasedLevel, RadialDanceParty,
        // TupperwareNestingLevel, PawPrintsLevel and the rest. Nothing in this
        // project recorded it, which is why "is this level phased" was
        // answered by guessing at controller counts for three sessions.
        row.Append($", \"levelClass\": {Json(Str(() => level == null ? "?" : level.GetIl2CppType().Name))}");

        // THE LEVEL'S OWN DECLARED PHASE ORDER, where it has one.
        row.Append($", \"phases\": {PhasesOf(level)}");

        // THE DRAWERS, and what each one waits on. Drawer carries
        // UnlockOnSolvedControllers and OpenOnSolvedControllers - directed,
        // authored data saying exactly which controller opens it. The eight
        // drawer edges in levels.json were hand-written from a playtest report
        // instead of read from here.
        row.Append($", \"drawers\": {DrawersOf(level)}");

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

                // THE INSTANCE IDS OF THE OBJECTS THIS CONTROLLER MOVES.
                //
                // Recorded so the classifier can see when two controllers own
                // the SAME object. Coins 1 (Shape) has an Ordered group of 6
                // and a Stacked group of 6 over what looks like one set of six
                // coins, and the mod's ability lock writes each controller's
                // objects in turn - so a locked group re-locks what an
                // unlocked one just freed and the level goes dead. Counts
                // cannot show that; identities can.
                row.Append(", \"objectIds\": [");
                try
                {
                    var managed = oc.ManagedObjects;
                    for (int k = 0; k < (managed == null ? 0 : managed.Count); k++)
                    {
                        var obj = managed![k];
                        if (obj == null) continue;
                        if (k > 0) row.Append(", ");
                        row.Append(obj.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                    }
                }
                catch { /* leave the list empty */ }
                row.Append(']');

                // The OBJECT-level gate, which is a different mechanism from
                // the controller-level one below. dependenciesPlacedFirst is
                // the game's own "these must be placed before this one can
                // be" flag - the authored answer to the assemble-then-arrange
                // question that was inferred by hand for Candy Canes.
                row.Append($", \"objectsGatedFirst\": {Str(() => GatedFirstCount(oc).ToString())}");
                row.Append($", \"matchDependencySolutions\": {Bool(() => oc.matchDependencySolutions)}");

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

        // The SECOND source of hint pages, and the reason hintImages alone was
        // wrong. A LevelRandomizer carries its own List<Sprite>
        // RandomizerHints and a virtual GetRandomizerHints(); Pencils and Books
        // override it. Pencils reports hintImages = 0 while
        // GetRandomizerHints() returns 2, so the generator minted no Hint Page
        // for a puzzle with two real pages, and the mod told the player it had
        // no hint at all. Both numbers are recorded because they disagree:
        // the field is the authored pool, the method is what THIS layout uses.
        row.Append($", \"randomizerHintPool\": {Str(() => RandomizerHintCount(level, pool: true).ToString())}");
        row.Append($", \"randomizerHints\": {Str(() => RandomizerHintCount(level, pool: false).ToString())}");
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

    /// <summary>
    /// How many of a controller's objects are gated behind other objects.
    ///
    /// LevelObject.dependenciesPlacedFirst is the game's own directed
    /// "these have to be placed before this one can be" flag. It is what makes
    /// Candy Canes unplayable without Jigsaw: the five canes to order do not
    /// exist as orderable things until the five jigsaw pairs are matched. That
    /// was worked out by hand from object counts; it was readable all along.
    /// </summary>
    private static int GatedFirstCount(ObjectController oc)
    {
        var gated = 0;
        try
        {
            var managed = oc.ManagedObjects;
            for (int i = 0; i < (managed == null ? 0 : managed.Count); i++)
            {
                var obj = managed![i];
                if (obj == null) continue;
                if (!obj.dependenciesPlacedFirst) continue;
                if (!obj.HasDependencies) continue;
                gated++;
            }
        }
        catch { /* best effort */ }
        return gated;
    }

    /// <summary>
    /// The level's declared phase order, as a JSON array of controller names.
    ///
    /// FOUR MECHANISMS, because the game has four and they do not share a base
    /// type. PhasedLevel carries an ordered phases array; TupperwareNesting and
    /// RadialDance are ObjectControllers that publish their own chain; and
    /// PawPrintsLevel uses section view controllers. An empty array means the
    /// level declares no phases, which is the common case.
    ///
    /// This is the answer to "which puzzles have mini solutions", read rather
    /// than inferred from a controller count that cannot see past phase one.
    /// </summary>
    private static string PhasesOf(Level? level)
    {
        var names = new List<string>();
        try
        {
            if (level != null)
            {
                var phased = level.TryCast<PhasedLevel>();
                if (phased != null && phased.phases != null)
                {
                    foreach (var phase in phased.phases)
                    {
                        if (phase == null) continue;
                        var pc = phase.PhaseController;
                        names.Add(pc == null ? "(none)" : pc.gameObject.name);
                    }
                }

                // A SectionViewController is a plain data object, not a
                // Component - the controller for the section is its
                // `clearables`. The compiler caught this; the metadata dump
                // did not, because a List<T> prints its arity and not its T.
                var paws = level.TryCast<PawPrintsLevel>();
                if (paws != null && paws.SectionViewControllers != null)
                {
                    foreach (var view in paws.SectionViewControllers)
                    {
                        if (view == null) continue;
                        var clearables = view.clearables;
                        names.Add(clearables == null
                            ? "(no clearables)" : clearables.gameObject.name);
                    }
                }

                var radial = level.TryCast<RadialDanceParty>();
                if (radial != null && radial.dances != null)
                {
                    // dances is the authored set; nextDance chains them. Walk
                    // the chain from whichever dance nothing points at, so the
                    // recorded order is the order they are played.
                    for (int i = 0; i < radial.dances.Count; i++)
                    {
                        var dance = radial.dances[i];
                        if (dance == null) continue;
                        var next = dance.nextDance;
                        names.Add(dance.gameObject.name
                                  + (next == null ? "" : " -> " + next.gameObject.name));
                    }
                }

                // TupperwareNesting is a controller, not a level - and it must
                // be found in the HIERARCHY, not in objectControllers.
                //
                // The first version looked in the level's registered list and
                // came back empty, which read exactly like "this level declares
                // no phases". It declares plenty; the component that declares
                // them is `Nested Tupperware`, which is itself one of the
                // things that has not registered yet at boot. Asking the
                // registered list to describe what is unregistered is the same
                // mistake the whole sweep has been making, one level down.
                //
                // GetComponentsInChildren with `true` includes inactive, which
                // is the entire point.
                var nesters = level.gameObject.GetComponentsInChildren<TupperwareNesting>(true);
                for (int i = 0; i < (nesters == null ? 0 : nesters.Length); i++)
                {
                    var tn = nesters![i];
                    if (tn == null) continue;
                    var phaseControllers = tn.GetPhaseControllers();
                    for (int k = 0; k < (phaseControllers == null ? 0 : phaseControllers.Count); k++)
                    {
                        var pc = phaseControllers![k];
                        if (pc != null) names.Add(pc.gameObject.name);
                    }
                }
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"levelsweep: phases threw: {e.Message}");
        }

        var sb = new StringBuilder("[");
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Json(names[i]));
        }
        return sb.Append(']').ToString();
    }

    /// <summary>
    /// Every drawer in the level and what it waits on.
    ///
    /// Drawer.UnlockOnSolvedControllers and OpenOnSolvedControllers are
    /// directed, authored lists: solve THESE and the drawer opens. That is
    /// exactly the containment gate that was hand-written into levels.json
    /// after a playtester found Tool Drawer marked completable while the game
    /// kept it shut.
    ///
    /// Found by walking the level's transform rather than through
    /// DrawerController, because a Drawer is a DragObject and need not be
    /// owned by a controller the sweep already has.
    /// </summary>
    private static string DrawersOf(Level? level)
    {
        var sb = new StringBuilder("[");
        try
        {
            if (level == null) return "[]";
            var drawers = level.gameObject.GetComponentsInChildren<Drawer>(true);
            for (int i = 0; i < (drawers == null ? 0 : drawers.Length); i++)
            {
                var drawer = drawers![i];
                if (drawer == null) continue;
                if (sb.Length > 1) sb.Append(", ");
                sb.Append('{');
                sb.Append($"\"name\": {Json(Str(() => drawer.gameObject.name))}");
                sb.Append($", \"contains\": {Str(() => (drawer.ContainedObjects == null ? 0 : drawer.ContainedObjects.Count).ToString())}");
                sb.Append($", \"unlockOn\": {NamesOf(drawer.UnlockOnSolvedControllers)}");
                sb.Append($", \"openOn\": {NamesOf(drawer.OpenOnSolvedControllers)}");
                sb.Append($", \"subDrawers\": {Str(() => (drawer.SubDrawers == null ? 0 : drawer.SubDrawers.Count).ToString())}");
                sb.Append('}');
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"levelsweep: drawers threw: {e.Message}");
        }
        return sb.Append(']').ToString();
    }

    /// <summary>
    /// A drawer's trigger list, as "controller#solutionId" strings.
    ///
    /// The element type is ObjectController.SolutionDetails, not
    /// ObjectController - the drawer waits on a SPECIFIC solution of a
    /// controller, not merely on the controller being solved. Worth keeping:
    /// a multi-solution controller can open a drawer on one arrangement and
    /// not another, and a plain controller name would lose that.
    /// </summary>
    /// <summary>
    /// The same, for a plain controller list.
    ///
    /// Two overloads because the two drawer lists genuinely differ:
    /// UnlockOnSolvedControllers names a specific solution, while
    /// OpenOnSolvedControllers names the controller alone. Only the compiler
    /// would have told us; the metadata prints both as List`1.
    /// </summary>
    private static string NamesOf(
        Il2CppSystem.Collections.Generic.List<ObjectController>? list)
    {
        var sb = new StringBuilder("[");
        try
        {
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                var oc = list![i];
                if (oc == null) continue;
                if (sb.Length > 1) sb.Append(", ");
                sb.Append(Json(oc.gameObject.name));
            }
        }
        catch { /* best effort */ }
        return sb.Append(']').ToString();
    }

    private static string NamesOf(
        Il2CppSystem.Collections.Generic.List<ObjectController.SolutionDetails>? list)
    {
        var sb = new StringBuilder("[");
        try
        {
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                var entry = list![i];
                if (entry == null) continue;
                var oc = entry.controller;
                if (oc == null) continue;
                if (sb.Length > 1) sb.Append(", ");
                sb.Append(Json(oc.gameObject.name + "#" + entry.solutionId));
            }
        }
        catch { /* best effort */ }
        return sb.Append(']').ToString();
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
