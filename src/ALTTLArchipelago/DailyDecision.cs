using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// A finished run puzzle never goes to the Daily Tidy page: the game's own
/// decision is answered where it is made, so the daily guard's rescue never
/// has to run (droha, 2026-10-01: "If we know what levels go there can't we
/// just prevent those levels from doing it?").
///
/// WHERE THE GAME DECIDES. Every way out of a finished level ends in
/// LevelManager.OnLevelCompleteTweenOutComplete(LevelInterface), the tween
/// callback of TransitionLevelOut: the game's straight-on route and the
/// retry panel's Continue alike. From the interop xref cache and the
/// function's machine code (2026-10-01) it does:
///
///     if ((li.DailyDates.Length > 0 || li is randomized) && !li.IsArchiveLevel)
///         StopGame(true); SetGameState&lt;DailyTidy_GameState&gt;(...);
///     else
///         on to the next level
///     DestroyLevel(li);
///
/// The condition is IsDailyTidy's body compiled inline, which is why
/// DailyGuard's IsDailyTidy postfix never sees it. Every generator passes it
/// (they are randomized), so every generator finish went to the Daily page
/// and was rescued from it: 105 times in the second player's 0.4.2 playtest. DevTools
/// trace on the gate's seed showed it in that order on Stamps (Randomized):
/// the tween-out, get_IsArchiveLevel, StopGame(True), the Daily state.
///
/// THE ONE REAL CALL IN THAT CONDITION is get_IsArchiveLevel, so it is
/// answered true for the level leaving, once, inside that one call, and the
/// game takes its own other branch: the one every archive and campaign level
/// takes. Nothing else changes: get_IsArchiveLevel is read many times around
/// a finish and a launch (the same trace), so the answer is scoped to the
/// first read inside the tween-out, for that level's interface only.
///
/// Its own class so a failed patch costs only this (Plugin's table). Not a
/// SetGameState&lt;T&gt; patch: that freezes the game (DailyGuard).
/// </summary>
[HarmonyPatch]
internal static class DailyDecision
{
    /// <summary>The interface of the run puzzle leaving, while its tween-out runs.</summary>
    private static IntPtr _leaving = IntPtr.Zero;

    [HarmonyPatch(typeof(LevelManager), "OnLevelCompleteTweenOutComplete")]
    [HarmonyPrefix]
    private static void BeforeTweenOut(LevelInterface __0)
    {
        _leaving = IntPtr.Zero;
        try
        {
            if (!Track.Active || __0 == null) return;
            _leaving = __0.Pointer;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"daily decision: {e.Message}");
        }
    }

    /// <summary>Cleared however the tween-out ends, a throw included.</summary>
    [HarmonyPatch(typeof(LevelManager), "OnLevelCompleteTweenOutComplete")]
    [HarmonyFinalizer]
    private static Exception? TweenOutFinally(Exception? __exception)
    {
        _leaving = IntPtr.Zero;
        return __exception;
    }

    [HarmonyPatch(typeof(LevelInterface), nameof(LevelInterface.IsArchiveLevel), MethodType.Getter)]
    [HarmonyPostfix]
    private static void AfterIsArchiveLevel(LevelInterface __instance, ref bool __result)
    {
        try
        {
            if (_leaving == IntPtr.Zero || __instance == null || __instance.Pointer != _leaving) return;
            _leaving = IntPtr.Zero;                      // the condition's read only
            if (__result) return;
            __result = true;
            Plugin.Logger.LogInfo(
                $"daily decision: {__instance.LevelId} leaves as a run puzzle, not a daily");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"daily decision: {e.Message}");
        }
    }
}
