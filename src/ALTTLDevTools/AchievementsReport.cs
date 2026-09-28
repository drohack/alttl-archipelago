using System;
using System.Collections.Generic;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// Every achievement the game knows, and the checkers on the level on screen.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// "achievements": every AchievementData asset loaded (id, name, whether
    /// the game thinks it is already met, description), then every component
    /// in the scene whose type name holds "Achievement", with the
    /// AchievementData it holds and, one line each, the level it watches and
    /// its other settings.
    ///
    /// THE QUESTIONS IT ANSWERS, for achievements as locations (backlog):
    /// which achievement each level's checker is wired to - scene data, not
    /// visible in the interop assembly - and whether one already on the
    /// player's Steam profile reads as met, which would stop its checker ever
    /// calling AchievementManager.SetAchievementMet again. Run it at the
    /// title, then after boot:&lt;index&gt; on a level with a checker.
    ///
    /// Read only. SteamAchievements in the mod withholds every award; this
    /// never calls the manager.
    /// </summary>
    private static void DumpAchievements()
    {
        var log = DevToolsPlugin.Log;

        var assets = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<AchievementData>()))
        {
            var data = obj == null ? null : obj.TryCast<AchievementData>();
            if (data == null) continue;
            assets++;
            log.LogInfo($"achievements: asset id={data.m_eAchievementID} "
                        + $"name='{Ascii(data.m_strName)}' achieved={data.m_bAchieved} "
                        + $"description='{Ascii(data.m_strDescription)}'");
        }
        log.LogInfo($"achievements: {assets} AchievementData asset(s) loaded");

        // THE WHOLE SCENE, not the level's hierarchy: measured 2026-09-27, the
        // checkers are not under the level (0 found on Eggs, Media Cabinet and
        // Water Glasses that way). Inactive objects included; prefabs, which
        // have no valid scene, are not.
        var level = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        var checkers = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<MonoBehaviour>()))
        {
            var behaviour = obj == null ? null : obj.TryCast<MonoBehaviour>();
            if (behaviour == null || behaviour.gameObject == null) continue;
            if (!behaviour.gameObject.scene.IsValid()) continue;
            var typeName = behaviour.GetIl2CppType().Name;
            if (typeName.IndexOf("Achievement", StringComparison.Ordinal) < 0) continue;
            if (typeName == "AchievementManager") continue;
            checkers++;
            log.LogInfo($"achievements: checker {typeName} at {PathOf(behaviour.transform)} "
                        + $"live={behaviour.isActiveAndEnabled} "
                        + $"holds {string.Join(", ", HeldAchievements(behaviour, typeName))}");
            foreach (var setting in CheckerSettings(behaviour, typeName))
                log.LogInfo($"achievements:   {typeName}.{setting}");
        }
        log.LogInfo($"achievements: {checkers} checker(s) in the scene"
                    + (level == null ? " (no level running)" : $" with {level.LevelId} running"));
    }

    /// <summary>
    /// The AchievementData a checker holds, read through its managed interop
    /// type: its properties are the Il2Cpp fields, so any of type
    /// AchievementData (or a list or array of them) is what it awards.
    /// </summary>
    private static List<string> HeldAchievements(MonoBehaviour behaviour, string typeName)
    {
        var held = new List<string>();
        try
        {
            var type = FindType(typeName);
            if (type == null) return new List<string> { "(no managed type)" };
            var typed = Activator.CreateInstance(type, behaviour.Pointer);

            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var property in t.GetProperties(Any))
                {
                    if (property.GetIndexParameters().Length > 0) continue;
                    var kind = property.PropertyType;
                    if (kind == typeof(AchievementData))
                    {
                        var one = property.GetValue(typed) as AchievementData;
                        held.Add($"{property.Name}={Describe(one)}");
                    }
                    else if (kind.Name.Contains("AchievementData"))
                    {
                        held.Add($"{property.Name}=({kind.Name}, not expanded)");
                    }
                }
            }
        }
        catch (Exception e)
        {
            held.Add($"(could not read: {e.GetType().Name})");
        }
        return held.Count > 0 ? held : new List<string> { "nothing typed AchievementData" };
    }

    /// <summary>
    /// Everything else a checker is configured with, one line each: the level
    /// it watches (a LevelInterface field, or a list of them), and its plain
    /// settings - object names, counts, times. That is what says where an
    /// achievement can be earned and what it asks for, which the interop
    /// assembly cannot: the method bodies are native.
    /// </summary>
    private static List<string> CheckerSettings(MonoBehaviour behaviour, string typeName)
    {
        var lines = new List<string>();
        try
        {
            var type = FindType(typeName);
            if (type == null) return lines;
            var typed = Activator.CreateInstance(type, behaviour.Pointer);

            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var property in t.GetProperties(Any))
                {
                    if (property.GetIndexParameters().Length > 0) continue;
                    var kind = property.PropertyType;
                    if (kind == typeof(AchievementData)) continue;
                    string? value = null;
                    try
                    {
                        var raw = property.GetValue(typed);
                        if (kind == typeof(LevelInterface))
                            value = LevelName(raw as LevelInterface);
                        else if (raw is Il2CppSystem.Collections.Generic.List<LevelInterface> levels)
                        {
                            var names = new List<string>();
                            for (int i = 0; i < levels.Count; i++) names.Add(LevelName(levels[i]));
                            value = $"[{string.Join(", ", names)}]";
                        }
                        else if (raw is Il2CppSystem.Collections.Generic.List<string> strings)
                        {
                            var items = new List<string>();
                            for (int i = 0; i < strings.Count; i++) items.Add(Ascii(strings[i]));
                            value = $"[{string.Join(", ", items)}]";
                        }
                        else if (kind == typeof(string) || kind.IsPrimitive)
                            value = raw is string text ? $"'{Ascii(text)}'" : raw?.ToString() ?? "null";
                    }
                    catch (Exception e)
                    {
                        value = $"<err:{e.GetType().Name}>";
                    }
                    if (value != null) lines.Add($"{property.Name} = {value}");
                }
            }
        }
        catch (Exception e)
        {
            lines.Add($"(could not read settings: {e.GetType().Name})");
        }
        return lines;
    }

    private static string LevelName(LevelInterface? level)
    {
        if (level == null) return "null";
        return $"{Str(() => level.LevelId)} (index {Str(() => level.LevelIndex.ToString())})";
    }

    private static string Describe(AchievementData? data)
        => data == null ? "null" : $"{data.m_eAchievementID}/'{Ascii(data.m_strName)}'/achieved={data.m_bAchieved}";

    /// <summary>The game's names hold typographic quotes; the log stays ASCII.</summary>
    private static string Ascii(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text!.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == (char)0x2019 || chars[i] == (char)0x2018) chars[i] = '\'';
            else if (chars[i] > 126) chars[i] = '?';
        }
        return new string(chars);
    }
}
