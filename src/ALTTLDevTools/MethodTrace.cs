using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// "trace:Type.Method,Type.Method" patches the named game methods at runtime
/// and logs every call with the object it ran on and its arguments;
/// "trace:off" takes the patches out. Every overload of a name is traced.
///
/// Written to find which routine opens Jewelry Box's locked drawer when the
/// rings solve: the obvious handler never ran, xrefs can kill the game, and a
/// guess costs a build and a relaunch each time.
/// </summary>
internal static class MethodTrace
{
    private static Harmony? _harmony;
    private static int _lines;
    private const int MaxLines = 300;

    internal static void Start(string csv)
    {
        if (csv.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            DevToolsPlugin.Log.LogInfo("trace: off");
            return;
        }

        _harmony ??= new Harmony(DevToolsPlugin.Guid + ".trace");
        _lines = 0;
        var prefix = new HarmonyMethod(typeof(MethodTrace).GetMethod(
            nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));

        foreach (var entry in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = entry.Trim();
            var dot = name.LastIndexOf('.');
            if (dot <= 0) { DevToolsPlugin.Log.LogWarning($"trace: '{name}' is not Type.Method"); continue; }
            var type = FindType(name.Substring(0, dot));
            if (type == null) { DevToolsPlugin.Log.LogWarning($"trace: no type named {name.Substring(0, dot)}"); continue; }

            var method = name.Substring(dot + 1);
            var found = type.GetMethods(BindingFlags.Instance | BindingFlags.Static
                                        | BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == method).ToList();
            if (found.Count == 0) { DevToolsPlugin.Log.LogWarning($"trace: {type.Name} has no {method}"); continue; }
            foreach (var m in found)
            {
                try
                {
                    _harmony.Patch(m, prefix: prefix);
                    DevToolsPlugin.Log.LogInfo($"trace: watching {type.Name}.{m.Name}({m.GetParameters().Length} arg(s))");
                }
                catch (Exception e)
                {
                    DevToolsPlugin.Log.LogWarning($"trace: could not patch {type.Name}.{m.Name}: {e.Message}");
                }
            }
        }
    }

    private static Type? FindType(string name)
    {
        // The game's own assembly first, as SceneCommands.FindType: a bare
        // name like Match also names a .NET type.
        foreach (var gameFirst in new[] { true, false })
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var isGame = (asm.GetName().Name ?? "").StartsWith("Assembly-CSharp", StringComparison.Ordinal);
                if (isGame != gameFirst) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name == name || t.FullName == name) return t;
                }
            }
        }
        return null;
    }

    private static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        try
        {
            if (_lines >= MaxLines) return;
            _lines++;
            var on = Describe(__instance);
            var args = __args == null ? "" : string.Join(", ", __args.Select(Describe));
            DevToolsPlugin.Log.LogInfo(
                $"trace: {Time.frameCount} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}"
                + (on.Length > 0 ? $" on {on}" : "") + $" ({args})"
                + (_lines == MaxLines ? " - trace stops logging here" : ""));
        }
        catch
        {
            // A trace line is never worth a throw into the game's call.
        }
    }

    private static string Describe(object? o)
    {
        if (o == null) return "null";
        try
        {
            if (o is Component c) return $"'{c.gameObject.name}'";
            if (o is UnityEngine.Object u) return $"'{u.name}'";
            return o.ToString() ?? "?";
        }
        catch
        {
            return "?";
        }
    }
}
