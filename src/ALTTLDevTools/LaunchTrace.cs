using HarmonyLib;

namespace ALTTLDevTools;

/// <summary>
/// Log every route the game takes into a level, with the arguments.
///
/// WHY. The mod applies a slot's baked generator seed in a prefix on
/// LevelManager.StartLevel. droha reported that Play and clicking the first
/// card open two DIFFERENT layouts of the same generator level, and the log
/// showed why one of them is wrong: the card path logs "now playing slot 0"
/// from the CLICK prefix and then never logs "launching with seed", so the
/// StartLevel prefix is not running on that route.
///
/// LevelManager has two entry points with the same shape - StartLevel and
/// SetActiveLevel - plus RestartLevel. Only the first is patched. This traces
/// all three so the question "which one does a card click use" is answered by
/// observation rather than by reading call sites that are not in the assembly.
///
/// DevTools only, and off unless asked for.
/// </summary>
[HarmonyPatch]
public static class LaunchTrace
{
    private static bool _on;

    internal static void Toggle()
    {
        _on = !_on;
        DevToolsPlugin.Log.LogInfo($"launchtrace: {(_on ? "on" : "off")}");
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.StartLevel))]
    [HarmonyPrefix]
    private static void BeforeStartLevel(int startLevelIndex, bool showTransition,
                                         bool forceReload, int randomSeed)
    {
        if (!_on) return;
        DevToolsPlugin.Log.LogInfo(
            $"launchtrace: StartLevel(index={startLevelIndex}, "
            + $"transition={showTransition}, forceReload={forceReload}, "
            + $"seed={randomSeed})");
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SetActiveLevel))]
    [HarmonyPrefix]
    private static void BeforeSetActiveLevel(int levelIndex, bool doTransitionIn,
                                             bool forceReload, int randomSeed)
    {
        if (!_on) return;
        DevToolsPlugin.Log.LogInfo(
            $"launchtrace: SetActiveLevel(index={levelIndex}, "
            + $"transition={doTransitionIn}, forceReload={forceReload}, "
            + $"seed={randomSeed})");
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.RestartLevel))]
    [HarmonyPrefix]
    private static void BeforeRestartLevel()
    {
        if (!_on) return;
        DevToolsPlugin.Log.LogInfo("launchtrace: RestartLevel()");
    }
}
