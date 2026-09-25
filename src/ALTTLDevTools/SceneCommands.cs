using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Reflection;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
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
    /// What a game method calls: "xrefs:ReplayMenu.LevelSelect".
    ///
    /// Il2CppInterop's cross-reference scan of the method's native code, each
    /// call resolved back to a managed name where it can be. The interop
    /// assembly has no method bodies, so without this the only way to learn
    /// which routine a button runs is to patch a guess and see if it fires.
    /// Built 2026-09-24 after two such guesses for the post-level route into
    /// a DLC's level select (GoToLevelSelectForLevel, ContextualState) both
    /// installed and never ran.
    /// </summary>
    ///
    /// "xrefs:Type.Method|Type.Candidate,Type.Candidate" names what to look
    /// for instead: each call's target is compared with those methods' native
    /// entry points and nothing is resolved, since resolving is what froze.
    private static void ListXrefs(string arg)
    {
        var bar = arg.IndexOf('|');
        var candidates = bar < 0 ? null : CandidatePointers(arg.Substring(bar + 1));
        if (bar >= 0) arg = arg.Substring(0, bar);

        var dot = arg.LastIndexOf('.');
        if (dot <= 0 || dot == arg.Length - 1)
        {
            DevToolsPlugin.Log.LogWarning($"xrefs: want Type.Method, got '{arg}'");
            return;
        }
        var wanted = arg.Substring(0, dot).Trim();
        var method = arg.Substring(dot + 1).Trim();

        var found = FindType(wanted);
        if (found == null)
        {
            DevToolsPlugin.Log.LogWarning($"xrefs: no type named {wanted}");
            return;
        }

        int overloads = 0;
        foreach (var m in found.GetMethods(Any))
        {
            if (!string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase)) continue;
            overloads++;
            var ps = string.Join(", ", Array.ConvertAll(m.GetParameters(), x => x.ParameterType.Name));
            DevToolsPlugin.Log.LogInfo($"xrefs: {found.Name}.{m.Name}({ps})");

            int n = 0;
            foreach (var x in Il2CppInterop.Common.XrefScans.XrefScanner.XrefScan(m))
            {
                string what;
                if (candidates != null)
                {
                    what = x.Type == Il2CppInterop.Common.XrefScans.XrefType.Method
                        ? $"calls 0x{x.Pointer:X}"
                          + (candidates.TryGetValue(x.Pointer, out var name) ? $" = {name}" : "")
                        : $"global 0x{x.Pointer:X}";
                }
                else if (x.Type == Il2CppInterop.Common.XrefScans.XrefType.Method)
                {
                    // Logged BEFORE resolving. Resolving ReplayMenu.LevelSelect's
                    // eighth call froze the game for good (2026-09-24), and a
                    // freeze leaves no line to say which pointer did it.
                    DevToolsPlugin.Log.LogInfo($"  [{n}] resolving 0x{x.Pointer:X}");
                    MethodBase? callee = null;
                    try { callee = Il2CppInterop.Runtime.XrefScans.XrefInstanceExtensions.TryResolve(x); }
                    catch { }
                    what = callee == null
                        ? $"calls <unresolved 0x{x.Pointer:X}>"
                        : $"calls {callee.DeclaringType?.Name}.{callee.Name}";
                }
                else
                {
                    // The pointer only: reading a global as an object
                    // dereferences whatever it names, and a global is metadata
                    // as often as it is an object.
                    what = $"global 0x{x.Pointer:X}";
                }
                DevToolsPlugin.Log.LogInfo($"  {what}");
                n++;
            }
            DevToolsPlugin.Log.LogInfo($"xrefs: {n} reference(s)");
        }
        if (overloads == 0)
            DevToolsPlugin.Log.LogWarning($"xrefs: {found.Name} has no method {method}");
    }

    private const System.Reflection.BindingFlags Any =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.DeclaredOnly;

    private static Type? FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t != null && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }
        return null;
    }

    /// <summary>
    /// Native entry point -> "Type.Method" for each named candidate, every
    /// overload. The entry point is the first field of the method's
    /// Il2CppMethodInfo, which the generated class holds a pointer to.
    /// </summary>
    private static Dictionary<long, string> CandidatePointers(string list)
    {
        var map = new Dictionary<long, string>();
        foreach (var raw in list.Split(','))
        {
            var item = raw.Trim();
            var dot = item.LastIndexOf('.');
            if (dot <= 0) continue;
            var type = FindType(item.Substring(0, dot));
            if (type == null)
            {
                DevToolsPlugin.Log.LogWarning($"xrefs: no type named {item.Substring(0, dot)}");
                continue;
            }
            var wanted = item.Substring(dot + 1);
            foreach (var m in type.GetMethods(Any))
            {
                if (!string.Equals(m.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var field = Il2CppInterop.Common.Il2CppInteropUtils
                        .GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
                    var info = field == null ? IntPtr.Zero : (IntPtr)field.GetValue(null)!;
                    if (info == IntPtr.Zero) continue;
                    var entry = System.Runtime.InteropServices.Marshal.ReadIntPtr(info);
                    map[entry.ToInt64()] = $"{type.Name}.{m.Name}";
                    DevToolsPlugin.Log.LogInfo($"xrefs: candidate {type.Name}.{m.Name} at 0x{entry.ToInt64():X}");
                }
                catch (Exception e)
                {
                    DevToolsPlugin.Log.LogWarning($"xrefs: no entry point for {item}: {e.Message}");
                }
            }
        }
        return map;
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
            // AllObjects, NOT ManagedObjects. This read ManagedObjects until
            // 2026-09-22 and so under-reported a group's footprint by whatever
            // its subclass keeps to itself - Containables hides three lists,
            // Stickables and StackablesY one each, Dirtyables keeps its coins
            // out entirely. The same trap made `freeze` disable ONE collider
            // where the lock disables fifty-six, and that instrument reported
            // confidently wrong answers twice before anyone checked it against
            // a level whose answer was known.
            //
            // It matters more here than it looks: this command exists to find
            // objects sitting physically on top of other objects, and a group
            // measured at a fraction of its real size is a group whose overlap
            // silently does not register. The failure is a MISSING lead, which
            // is the quiet kind.
            foreach (var obj in AllObjects(oc))
            {
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

    /// <summary>Has this object any SpriteRenderer the dimmer could paint?</summary>
    private static bool HasAnyRenderer(LevelObject obj)
    {
        if (obj.renderer != null) return true;
        var subs = obj.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            if (subs![i] != null) return true;
        }
        return false;
    }

    /// <summary>
    /// Is this object wearing the randomizer's "locked" grey?
    ///
    /// The shade is AbilityLocks.Locked - (0.55, 0.55, 0.55, 0.6). Compared
    /// with a tolerance because a colour that has been through a float round
    /// trip is not reliably equal to the constant that set it.
    /// </summary>
    private static bool IsDimmed(LevelObject obj)
    {
        // THE SUBRENDERERS COUNT TOO, and missing them produced three
        // false findings. The randomizer paints obj.renderer AND every entry
        // in obj.subrenderers; an object whose visual lives only on the
        // subrenderers has a null renderer, so checking the main one alone
        // reported it as untouched. Measured on AnimScrubbables,
        // ScrollFieldGroupsController and CandlesObjectController, all three
        // of which read "0 dimmed" while being perfectly well locked.
        if (IsDimColour(obj.renderer)) return true;
        var subs = obj.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            if (IsDimColour(subs![i])) return true;
        }
        return false;
    }

    private static bool IsDimColour(SpriteRenderer? r)
    {
        if (r == null) return false;
        var c = r.color;
        return Near(c.r, 0.55f) && Near(c.g, 0.55f)
            && Near(c.b, 0.55f) && Near(c.a, 0.6f);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

    /// <summary>
    /// Try HARDER to stop an object being picked up, and find out what works.
    ///
    /// WHY THIS EXISTS. The randomizer's ability locks call
    /// SetInteractable(false), SetPreventSelection(true) and paint the object
    /// grey - and droha demonstrated on Calendar that a fully "locked" sticker
    /// still drags, unsticks and re-sticks. DevTools' own inert: makes the
    /// same three calls and behaves the same way, so the mechanism itself is
    /// cosmetic rather than the mod misusing it.
    ///
    /// LevelObject also carries a Collider2D. Dragging starts from a pointer
    /// hit, so removing the collider should stop the pickup outright where a
    /// flag did not. This command exists to have that confirmed by hand before
    /// AbilityLocks is changed to rely on it - the flags were never verified
    /// that way, which is exactly how the locks came to be cosmetic.
    ///
    ///   freeze:&lt;controller&gt;   disable the colliders of its objects
    ///   freeze:off             put every collider back
    /// </summary>
    private static readonly Dictionary<int, bool> _frozen = new();

    /// <summary>
    /// Per controller, how many of its objects a POINTER could actually hit
    /// right now.
    ///
    /// WHY THIS EXISTS, and it replaces a question that was being put to a
    /// human. "Can the player reach this group yet" has been answered three
    /// ways in this project and all three were wrong for the same reason:
    /// `dependsOn` is silent about edges the game does not express that way,
    /// the sweep's drawer containment was measured against the one hand audit
    /// available and came back wrong in BOTH directions, and the release
    /// harness force-solves by setting a flag, which bypasses the physics it
    /// is supposed to be measuring. The fourth way was "ask droha to try
    /// dragging it", which is fine for a judgement call and absurd for
    /// something the engine already knows.
    ///
    /// It knows because a pointer hit needs two things and both are readable:
    /// the GameObject has to be active in the hierarchy, and its collider has
    /// to exist and be enabled. An object shut inside a drawer fails one or
    /// the other. That is the whole measurement.
    ///
    /// HOW TO USE IT, which matters more than the numbers. Run it at boot,
    /// then solve whatever opens the container, then run it again. Anything
    /// that becomes touchable in between was gated by the thing you solved -
    /// which is exactly the dependsOn edge the table is missing. One run on
    /// its own says very little.
    ///
    /// AllObjects, not ManagedObjects: Dirtyables keeps its coins elsewhere,
    /// and Containables, Stickables and StackablesY each hide a list. The
    /// `locks` command read only the one and reported "objects=1" for a level
    /// full of freely clickable coins.
    /// </summary>
    private static void Reachable()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("reachable: no level running");
            return;
        }

        var list = level.objectControllers;
        // `stuck` is the column that matters: untouchable AND not yet placed,
        // i.e. the player cannot get at it and it is not finished either.
        // `done` is untouchable because it is already where it belongs, which
        // is not a gate and must never be counted as one.
        DevToolsPlugin.Log.LogInfo(
            $"reachable: {Str(() => li!.LevelId)} -- controller\ttype\ttotal"
            + "\ttouchable\tinactive\tnoCollider\tcolliderOff\tstuck\tdone");

        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            int total = 0, touchable = 0, inactive = 0, missing = 0, off = 0;
            int stuck = 0, done = 0;
            foreach (var obj in AllObjects(oc))
            {
                total++;

                // THE DISTINCTION THE `locks` COMMAND DOES NOT MAKE, and the
                // reason a whole afternoon of measurement had to be thrown
                // away on 2026-09-22.
                //
                // `blocked` there is !Interactable || PreventSelection, which
                // is "the player cannot touch this". That is TWO different
                // situations wearing one number: an object gated behind
                // something the player has not done, and an object already
                // sitting in its correct place. Both are untouchable and only
                // the first is a missing requirement.
                //
                // It is why the count CLIMBS as a level settles - the game
                // places things during setup - and why archive levels came
                // back 100 per cent blocked and looked like a dozen findings.
                // `placed` separates them, and the game has carried the flag
                // all along.
                bool untouchable;
                try { untouchable = !obj.Interactable || obj.PreventSelection; }
                catch { untouchable = false; }
                if (untouchable)
                {
                    bool settled;
                    try { settled = obj.placed; }
                    catch { settled = false; }
                    if (settled) done++; else stuck++;
                }

                bool live;
                try { live = obj.gameObject.activeInHierarchy; }
                catch { live = false; }
                if (!live) { inactive++; continue; }

                Collider2D? col;
                try { col = obj.collider; }
                catch { col = null; }
                if (col == null) { missing++; continue; }

                bool on;
                try { on = col.enabled; }
                catch { on = false; }
                if (on) touchable++; else off++;
            }

            DevToolsPlugin.Log.LogInfo(
                $"reachable:   {Str(() => oc.gameObject.name)}\t"
                + $"{Str(() => oc.GetIl2CppType().Name)}\t"
                + $"{total}\t{touchable}\t{inactive}\t{missing}\t{off}\t"
                + $"{stuck}\t{done}");
        }
        DevToolsPlugin.Log.LogInfo("reachable: done");
    }

    private static void Freeze(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("freeze: no level running");
            return;
        }

        var off = arg.Equals("off", StringComparison.OrdinalIgnoreCase);
        var list = level.objectControllers;
        var touched = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var name = Str(() => oc.gameObject.name);
            if (!off && !name.Equals(arg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // AllObjects, NOT ManagedObjects. This read ManagedObjects until
            // 2026-09-22, which is the trap the docstring directly below this
            // method exists to warn about - and this method walked straight
            // into it. Drawer Controller manages ONE object, the handle, and
            // holds fifty-six. So `freeze:Drawer Controller`, meant to
            // simulate not having the Drawer ability, disabled the handle's
            // collider and left the drawer fully operable. droha opened and
            // closed it and moved everything inside, which read as the level
            // not being gated at all - and would have been written into
            // levels.json as a correction if the run had been on a level
            // whose answer was not already known.
            //
            // The mod's own AbilityLocks never had this bug: Collect() takes
            // ManagedObjects AND the subclass's private lists. An instrument
            // that freezes less than the thing it simulates is not a
            // simulation.
            foreach (var obj in AllObjects(oc))
            {
                if (obj == null) continue;
                try
                {
                    var col = obj.collider;
                    if (col == null) continue;
                    var id = col.GetInstanceID();
                    if (off)
                    {
                        if (_frozen.TryGetValue(id, out var was))
                        {
                            col.enabled = was;
                        }
                    }
                    else
                    {
                        if (!_frozen.ContainsKey(id)) _frozen[id] = col.enabled;
                        col.enabled = false;
                    }
                    touched++;
                }
                catch
                {
                    // One awkward object must not abandon the rest.
                }
            }
        }

        if (off) _frozen.Clear();
        DevToolsPlugin.Log.LogInfo(
            $"freeze: {(off ? "restored" : "disabled")} {touched} collider(s)");
    }

    /// <summary>
    /// Every LevelObject a controller manages, including the ones it keeps to
    /// itself.
    ///
    /// MANAGEDOBJECTS IS NOT THE FULL SET, and reporting as though it were is
    /// what let a broken lock look healthy. Dirtyables registers ONE object
    /// and keeps its coins in dirtyObjects/cleanerObjects; Containables hides
    /// three more lists, Stickables and StackablesY one each. The `locks`
    /// command counted only ManagedObjects and so reported "objects=1" for a
    /// level full of coins, every one of them freely clickable.
    ///
    /// GetType() IS NOT THE CONTROLLER'S CLASS. Everything in
    /// Level.objectControllers is an ObjectController wrapper whatever it
    /// really is, so reflection over the managed type can never see a
    /// subclass's members. The real class comes from GetIl2CppType().Name,
    /// and the wrapper for it has to be found by name and cast to.
    ///
    /// DUPLICATED FROM AbilityLocks ON PURPOSE. DevTools does not reference
    /// the randomizer - see the note on this assembly - and an instrument
    /// that imported the thing it measures would agree with it by
    /// construction. This is the one kind of duplication worth having.
    /// </summary>
    private static List<LevelObject> AllObjects(ObjectController oc)
    {
        var found = new List<LevelObject>();
        var seen = new HashSet<int>();

        void Take(object? list)
        {
            if (list == null) return;
            var type = list.GetType();
            var countProp = type.GetProperty("Count");
            var itemProp = type.GetProperty("Item");
            if (countProp == null || itemProp == null) return;

            int count;
            try { count = (int)(countProp.GetValue(list) ?? 0); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (itemProp.GetValue(list, new object[] { i }) is not LevelObject obj
                        || obj == null) continue;
                    if (seen.Add(obj.GetInstanceID())) found.Add(obj);
                }
                catch { }
            }
        }

        Take(oc.ManagedObjects);

        var (concrete, extras) = ExtraLists(oc);
        if (extras.Length == 0 || concrete == null) return found;

        object? self;
        try
        {
            var cast = typeof(Il2CppObjectBase)
                .GetMethod(nameof(Il2CppObjectBase.TryCast))
                ?.MakeGenericMethod(concrete);
            self = cast?.Invoke(oc, null);
        }
        catch { return found; }
        if (self == null) return found;

        foreach (var prop in extras)
        {
            try { Take(prop.GetValue(self)); }
            catch { }
        }
        return found;
    }

    /// <summary>The LevelObject collections a controller class declares itself.</summary>
    private static (Type?, PropertyInfo[]) ExtraLists(ObjectController oc)
    {
        string cls;
        try { cls = oc.GetIl2CppType()?.Name ?? ""; }
        catch { return (null, NoExtras); }

        if (_extraLists.TryGetValue(cls, out var known)) return known;

        Type? concrete = null;
        var found = new List<PropertyInfo>();
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t != null && t.Name == cls
                        && typeof(ObjectController).IsAssignableFrom(t))
                    {
                        concrete = t;
                        break;
                    }
                }
                if (concrete != null) break;
            }

            if (concrete != null)
            {
                const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var prop in concrete.GetProperties(Declared))
                {
                    if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                    var pt = prop.PropertyType;
                    if (!pt.IsGenericType) continue;
                    var args = pt.GetGenericArguments();
                    if (args.Length == 1 && typeof(LevelObject).IsAssignableFrom(args[0]))
                    {
                        found.Add(prop);
                    }
                }
            }
        }
        catch { }

        var answer = (concrete, found.ToArray());
        _extraLists[cls] = answer;
        return answer;
    }

    private static readonly PropertyInfo[] NoExtras = new PropertyInfo[0];
    private static readonly Dictionary<string, (Type?, PropertyInfo[])> _extraLists = new();

    /// <summary>
    /// Dump which controllers claim which objects, for the open level.
    ///
    /// WHY A DUMP RATHER THAN A COUNT. `locks` reports `shared` as a number,
    /// which says an object is claimed twice but not by WHOM - and the
    /// question that matters is whether a gated group's objects are all also
    /// held by a group the player can unlock some other way. Books 3 leaks
    /// because its Shuffleables books are all held by a baseline Draggables
    /// group; Spoons leaks only once you hold ONE of its two abilities.
    ///
    /// The second shape cannot be seen in a zero-ability run at all, and it
    /// cannot be brute-forced either: Archipelago items cannot be un-sent, so
    /// every partial holding would need its own server session - about 35 of
    /// them for the non-DLC levels alone.
    ///
    /// Membership makes it a static question. Given which controllers hold
    /// which objects, "is group X freed when the player holds Y" is
    /// arithmetic over the ability table, answerable for every subset at
    /// once, offline, from one sweep with nothing sent.
    ///
    ///   sharing:new     start a fresh file
    ///   sharing:append  add this level to it
    /// </summary>
    private static void DumpSharing(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("sharing: no level running");
            return;
        }

        var levelId = Str(() => li!.LevelId);
        var path = Path.Combine(GameDir, "BepInEx", "alttl-sharing.tsv");
        var fresh = !File.Exists(path)
            || tag.Trim().Equals("new", StringComparison.OrdinalIgnoreCase);

        var rows = new List<string>();
        if (fresh) rows.Add("level\tcontroller\ttype\tobjectId\tobjectName");

        var list = level.objectControllers;
        var wrote = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var cname = Str(() => oc.gameObject.name);
            var ctype = Str(() => oc.GetIl2CppType().Name);
            foreach (var obj in AllObjects(oc))
            {
                rows.Add(string.Join("\t", new[]
                {
                    levelId, cname, ctype,
                    Str(() => obj.GetInstanceID().ToString()),
                    Str(() => obj.gameObject.name),
                }));
                wrote++;
            }
        }

        if (fresh) File.WriteAllLines(path, rows);
        else File.AppendAllLines(path, rows);

        DevToolsPlugin.Log.LogInfo(
            $"sharing: {levelId} wrote {wrote} row(s) from {list.Count} controller(s) -> {path}");
    }

    /// <summary>
    /// What the ability locks have actually done to this level's objects.
    ///
    /// WHY THIS IS NOT `controllers`. That command reports each controller's
    /// class and solved flag, which is enough to GUESS at gating by mapping the
    /// class through the ability table - and a harness that guesses that way is
    /// re-deriving the answer from the same table it is supposed to be
    /// auditing. It would agree with a wrong table every time.
    ///
    /// This reports what is on the screen instead. The randomizer's dimmer
    /// gates a puzzle by calling SetInteractable(false) / SetPreventSelection
    /// (true) on the LevelObjects a controller manages; those are the game's
    /// own properties, so reading them back says what the PLAYER can touch,
    /// whatever any table claims.
    ///
    /// DEVTOOLS STILL DOES NOT KNOW THE RANDOMIZER EXISTS, deliberately - see
    /// the note on this assembly. Nothing here references the mod; it reads
    /// game state that happens to be what the mod wrote.
    ///
    /// THE OTHER REASON THIS EXISTS: the mod's own "abilities: N locked" log
    /// line is emitted only when the summary CHANGES, once a second, and not at
    /// all when locks are off or no run is connected. Absence of that line
    /// means four different things, and an incremental log reader races it.
    /// This is asked for on demand and answers about right now.
    /// </summary>
    private static void ReportLocks()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("locks: no level running");
            return;
        }

        var list = level.objectControllers;
        var totalObjects = 0;
        var totalBlocked = 0;
        var totalDimmed = 0;

        // WHICH OBJECTS TWO CONTROLLERS BOTH CLAIM. The randomizer's dimmer
        // merges per object and lets UNLOCKED WIN, so an object held by a
        // locked controller and an open one is left fully playable. A
        // controller can therefore declare an ability and gate nothing at all,
        // which is the difference between what a level's table says it needs
        // and what it actually needs. Counting the overlap is what makes that
        // difference visible instead of looking like a broken lock.
        var owners = new Dictionary<int, int>();
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var managed = AllObjects(oc);
            for (int j = 0; j < (managed == null ? 0 : managed.Count); j++)
            {
                var obj = managed![j];
                if (obj == null) continue;
                try
                {
                    var id = obj.GetInstanceID();
                    owners[id] = owners.TryGetValue(id, out var n) ? n + 1 : 1;
                }
                catch { }
            }
        }

        var lines = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            var objects = 0;
            var blocked = 0;
            var dimmed = 0;
            var shared = 0;
            var norenderer = 0;
            var managed = AllObjects(oc);
            for (int j = 0; j < managed.Count; j++)
            {
                var obj = managed![j];
                if (obj == null) continue;
                objects++;
                try
                {
                    // TWO DIFFERENT QUESTIONS, and conflating them produced a
                    // wrong answer the first time this ran.
                    //
                    // `blocked` is "the player cannot touch this", which the
                    // GAME also causes on its own - measured: plain Draggables
                    // objects inside a closed drawer read non-interactive with
                    // no ability lock anywhere near them, and every such level
                    // looked like a table mismatch.
                    //
                    // `dimmed` is the randomizer's own signature: it repaints a
                    // locked object's renderer to a specific grey. Nothing else
                    // writes that exact colour, so this is the count that
                    // answers "did the ability lock do this".
                    if (!obj.Interactable || obj.PreventSelection) blocked++;
                    if (IsDimmed(obj)) dimmed++;
                    // NOTHING TO TINT. An object with no SpriteRenderer on it
                    // or its subrenderers cannot be greyed however correctly it
                    // is locked - it would be non-interactive and look
                    // completely normal, which is a lock with no feedback.
                    // Counted so "not dimmed" can be told apart from "not
                    // dimmable".
                    if (!HasAnyRenderer(obj)) norenderer++;
                    if (owners.TryGetValue(obj.GetInstanceID(), out var n)
                        && n > 1) shared++;
                }
                catch
                {
                    // One awkward object must not abandon the rest of the level.
                }
            }

            totalObjects += objects;
            totalBlocked += blocked;
            totalDimmed += dimmed;
            lines.Add($"  [{i}] {Str(() => oc.gameObject.name)}"
                      + $" type={Str(() => oc.GetIl2CppType().Name)}"
                      + $" objects={objects} blocked={blocked} dimmed={dimmed}"
                      + $" shared={shared} norenderer={norenderer}"
                      + $" solved={Str(() => oc.IsSolved.ToString())}");
        }

        // The header first, so a harness can wait on one line and then read the
        // list - the same shape as `controllers`, whose header/list split is
        // already documented as a trap for anything that waits on the header
        // and reads immediately.
        DevToolsPlugin.Log.LogInfo(
            $"locks: {Str(() => li!.LevelId)} {list.Count} controller(s),"
            + $" {totalDimmed} of {totalObjects} object(s) dimmed,"
            + $" {totalBlocked} not interactive");
        foreach (var line in lines) DevToolsPlugin.Log.LogInfo(line);
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
