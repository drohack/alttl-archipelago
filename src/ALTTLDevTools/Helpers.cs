using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// The helpers every command file shares, each written once: reading a value
/// that may throw, JSON escaping, finding a type by name, finding the live
/// level-select track, and calling into the Archipelago mod by name.
///
/// Imported with `using static ALTTLDevTools.Helpers;`. Four copies of Str,
/// three JSON escapers and two FindTypes that disagreed with each other lived
/// in the command files until 2026-09-28.
/// </summary>
internal static class Helpers
{
    /// <summary>
    /// Reads one value, turning any IL2CPP-side failure into a marker string
    /// rather than aborting the whole dump. A partial dump beats no dump.
    /// </summary>
    internal static string Str(Func<string?> f)
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

    /// <summary>
    /// A JSON string literal, quotes included. Control characters and
    /// anything outside printable ASCII are \u-escaped, so the file is ASCII
    /// and reads the same on a Windows console as in a JSON parser.
    /// </summary>
    internal static string Json(string? s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s ?? "")
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20 || ch > 0x7e) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// A loaded type by name, for members:, xrefs:, trace: and the
    /// achievement report.
    ///
    /// The game's own assembly first: `members:Match` found .NET's regex
    /// Match before Candles' match (2026-09-27). An exact Name or FullName
    /// match anywhere beats a case-insensitive one. An assembly that only
    /// partly loads still offers the types it could load: the two copies this
    /// replaces disagreed on both points, so `trace:` could miss a type that
    /// `members:` found.
    /// </summary>
    internal static Type? FindType(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return null;
        foreach (var exact in new[] { true, false })
        {
            foreach (var gameFirst in new[] { true, false })
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var isGame = (asm.GetName().Name ?? "").StartsWith("Assembly-CSharp", StringComparison.Ordinal);
                    if (isGame != gameFirst) continue;
                    Type?[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        if (exact ? t.Name == name || t.FullName == name
                                  : string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return t;
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The level-select track a player is looking at: a LIVE one (active in
    /// the hierarchy) beats any dead one, and among those the fullest wins.
    /// Null when the scene holds none. `live` says whether the one returned
    /// is on screen; a caller that clicks or scrolls must refuse a dead one.
    ///
    /// Both halves were learned the hard way. FindObjectOfType returns one
    /// arbitrary ACTIVE instance and picked an empty track, so every
    /// position reported "not on the track". Widening to
    /// FindObjectsOfTypeAll and taking the fullest one fixed that and broke
    /// something quieter: the scene keeps stale tracks around, so after a
    /// few menu transitions the fullest track is a LEFTOVER. Clicking its
    /// icons resolves the level name perfectly and then does nothing at all,
    /// because the icon is not the one on screen.
    /// </summary>
    internal static LevelsTrack? FindLiveTrack(out int items, out bool live)
    {
        LevelsTrack? best = null;
        items = -1;
        live = false;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var candidate = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (candidate == null) continue;

            int n;
            bool isLive;
            try
            {
                n = candidate.trackItems == null ? 0 : candidate.trackItems.Count;
                isLive = candidate.gameObject != null && candidate.gameObject.activeInHierarchy;
            }
            catch { continue; }

            if (isLive != live ? isLive : n > items)
            {
                best = candidate;
                items = n;
                live = isLive;
            }
        }
        if (best == null) items = 0;
        return best;
    }

    /// <summary>
    /// A static method of the Archipelago mod, found by name: DevTools has
    /// no compile-time reference to the mod, so it cannot agree with it by
    /// construction. Null when the mod is not loaded or has no such method.
    /// </summary>
    internal static MethodInfo? ModMethod(string typeName, string method)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "ALTTLArchipelago") continue;
            return asm.GetType("ALTTLArchipelago." + typeName)?.GetMethod(method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }
        return null;
    }

    /// <summary>Whether the Archipelago mod's assembly is loaded at all.</summary>
    internal static bool ModLoaded()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "ALTTLArchipelago") return true;
        }
        return false;
    }

    /// <summary>
    /// Call one of the mod's DebugHooks with the command's argument and log
    /// the line it answers with - how revoke: and traps: reach the mod's real
    /// lock and trap count.
    /// </summary>
    internal static void CallModHook(string hook, string arg, string label)
    {
        var method = ModMethod("DebugHooks", hook);
        if (method == null)
        {
            DevToolsPlugin.Log.LogWarning(
                $"{label}: the Archipelago mod (DebugHooks.{hook}) is not loaded");
            return;
        }
        var line = method.Invoke(null, new object[] { arg }) as string;
        DevToolsPlugin.Log.LogInfo(line ?? $"{label}: no answer from the mod");
    }
}
