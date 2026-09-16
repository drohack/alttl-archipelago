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
/// Reading the running scene: a type's members by reflection, an object's
/// world bounds, a level's layout, text anywhere on screen, and the frame watch.
/// </summary>
public partial class DevToolsBehaviour
{
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
}
