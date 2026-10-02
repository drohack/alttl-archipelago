using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// Asking the loaded game about itself: a type's members by reflection, what
/// a method calls (xrefs:, which can freeze the game), and text anywhere in
/// the scene. trace: is MethodTrace.
/// </summary>
public partial class DevToolsBehaviour
{
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

        var found = FindType(wanted);
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
    /// The live values of a game type's own properties, on every instance in
    /// the scene (prefabs and assets left out, at most six):
    /// "values:PostItNote_LevelRandomizer" or "values:Type:filter". Numbers,
    /// flags, text, enums, vectors, an object's name, a list's count. Built to
    /// compare a Post-It Notes load whose note cameras stay on with one where
    /// the level puts them away (2026-10-01).
    /// </summary>
    private static void ListValues(string arg)
    {
        var parts = arg.Split(':');
        var wanted = parts[0].Trim();
        var filter = parts.Length > 1 ? parts[1].Trim() : "";

        var found = FindType(wanted);
        if (found == null)
        {
            DevToolsPlugin.Log.LogWarning($"values: no type named {wanted}");
            return;
        }

        Il2CppSystem.Type il2cpp;
        try
        {
            il2cpp = Il2CppInterop.Runtime.Il2CppType.From(found);
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"values: {wanted} is not a game type: {e.Message}");
            return;
        }

        const BindingFlags Own = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var shown = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(il2cpp))
        {
            if (obj == null) continue;
            var comp = obj.TryCast<Component>();
            if (comp != null && !comp.gameObject.scene.IsValid()) continue;
            object instance;
            try
            {
                instance = Activator.CreateInstance(found, obj.Pointer)!;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"values: could not wrap {obj.name}: {e.Message}");
                continue;
            }
            DevToolsPlugin.Log.LogInfo(
                $"values: {found.Name} on '{obj.name}'"
                + (comp != null ? $" active={comp.gameObject.activeInHierarchy}" : ""));
            foreach (var pr in found.GetProperties(Own))
            {
                if (!pr.CanRead || pr.GetIndexParameters().Length > 0) continue;
                if (filter.Length > 0 && pr.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                DevToolsPlugin.Log.LogInfo($"values:   {pr.Name} = {Str(() => ShowValue(pr.GetValue(instance)))}");
            }
            if (++shown >= 6) break;
        }
        DevToolsPlugin.Log.LogInfo($"values: {shown} instance(s) of {found.Name}");
    }

    private static string ShowValue(object? v)
    {
        switch (v)
        {
            case null: return "null";
            case bool or int or long or float or double or string or Enum: return v.ToString() ?? "";
            case Vector2 v2: return $"({v2.x:0.###}, {v2.y:0.###})";
            case Vector3 v3: return $"({v3.x:0.###}, {v3.y:0.###}, {v3.z:0.###})";
            case UnityEngine.Object u: return $"'{u.name}'";
        }
        var count = v.GetType().GetProperty("Count")?.GetValue(v)
                    ?? v.GetType().GetProperty("Length")?.GetValue(v);
        return count != null ? $"{v.GetType().Name} count={count}" : v.GetType().Name;
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

    private const System.Reflection.BindingFlags Any =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.DeclaredOnly;

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
}
