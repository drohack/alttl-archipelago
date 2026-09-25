using HarmonyLib;

namespace ALTTLDevTools;

/// <summary>
/// Counts calls to LevelIcon's own hover-star methods, for `starcalls`:
/// when the game repaints a card's stars decides when the mod's run stars
/// have to be applied so the game does not paint over them.
/// </summary>
[HarmonyPatch]
internal static class StarTrace
{
    private static int _set;
    private static int _init;

    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.SetCompletionStars))]
    [HarmonyPostfix]
    private static void AfterSet() => _set++;

    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.InitIconSolutionStars))]
    [HarmonyPostfix]
    private static void AfterInit() => _init++;

    /// <summary>The counts since the last call, then zero them.</summary>
    internal static string TakeCounts()
    {
        var counts = $"SetCompletionStars={_set} InitIconSolutionStars={_init} since the last starcalls";
        _set = 0;
        _init = 0;
        return counts;
    }
}
