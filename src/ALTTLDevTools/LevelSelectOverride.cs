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
