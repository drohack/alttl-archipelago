using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// Whether a finished run puzzle shows the three-button panel (restart, pause
/// menu, next arrow) or goes straight on: the panel while the slot has more
/// than one Solution location and some are still to find, otherwise straight
/// on (Checks.OfferRetry). droha, 2026-09-25.
///
/// The game decides this per level from LevelInterface.ShowRetryMenu, which
/// LevelSuccess.LevelComplete reads (DevTools xrefs, 2026-09-25): 148 of the
/// game's 186 levels show the panel, every generator goes straight on
/// (docs/data/level-endings.tsv). Answered only for the level that is running
/// and only while it is a run slot; anything else keeps the game's value.
/// Where the arrow or the straight-on route leads is Navigation's.
/// </summary>
[HarmonyPatch]
internal static class RetryPanel
{
    private static string _lastLogged = "";

    [HarmonyPatch(typeof(LevelInterface), nameof(LevelInterface.ShowRetryMenu),
                  MethodType.Getter)]
    [HarmonyPostfix]
    private static void AfterShowRetryMenu(LevelInterface __instance, ref bool __result)
    {
        try
        {
            var active = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            if (active == null || __instance == null || active.Pointer != __instance.Pointer) return;

            var offer = Checks.OfferRetry(out var lit, out var total);
            if (offer == null) return;

            var line = $"retry panel: slot {Checks.CurrentSlot}, {lit} of {total} solution(s) "
                       + $"in after this completion -> {(offer.Value ? "panel" : "next")} "
                       + $"(the level's own: {(__result ? "panel" : "next")})";
            if (line != _lastLogged)
            {
                _lastLogged = line;
                Plugin.Logger.LogInfo(line);
            }
            __result = offer.Value;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"retry panel: {e.Message}");
        }
    }
}
