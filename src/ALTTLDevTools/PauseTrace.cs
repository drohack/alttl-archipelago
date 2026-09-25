using System;
using HarmonyLib;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Log every call to GameManager.Pause and every focus change the game is
/// told about. A paused game holds every gameplay event (measured
/// 2026-09-24); three gate runs that day lost their events with the clock at
/// 0 and nothing in the log said why. It cannot name the caller:
/// Il2CppSystem.Diagnostics.StackTrace returned 0 frames here, even for the
/// game's own Pause(false) at a level start (2026-09-25).
/// </summary>
[HarmonyPatch]
internal static class PauseTrace
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Pause))]
    [HarmonyPostfix]
    private static void AfterPause(bool __0) => Report($"Pause({__0})");

    /// <summary>Patched by NAME: OnApplicationFocus is private.</summary>
    [HarmonyPatch(typeof(GameManager), "OnApplicationFocus")]
    [HarmonyPostfix]
    private static void AfterFocus(bool __0) => Report($"OnApplicationFocus({__0})");

    private static void Report(string call)
    {
        try
        {
            DevToolsPlugin.Log.LogInfo($"game: {call} -> {State()}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"game: {call}, state unreadable: {e.Message}");
        }
    }

    /// <summary>The game's pause state, for `time`, `boot` and the trace.</summary>
    internal static string State()
    {
        var gm = GameManager.Instance;
        if (gm == null) return $"no GameManager, timeScale={Time.timeScale}";
        string Read(string name)
        {
            try { return Traverse.Create(gm).Property(name).GetValue()?.ToString() ?? "null"; }
            catch { return "?"; }
        }
        return $"Paused={Read("Paused")} timeScale={Time.timeScale}";
    }
}
