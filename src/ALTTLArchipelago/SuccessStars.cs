using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// The stars that pop on the level-complete screen (LevelSuccess), set from
/// the run the way CardStars sets a card's: one per Solution location of the
/// slot, lit once collected.
///
/// The game fills them from the level's save row, which a generator puzzle
/// never writes: every LevelComplete in droha's run of 2026-09-25 logged
/// found=0, Pencils' second solution too, 30 s after its first was checked,
/// and droha saw only empty stars pop.
///
/// TWO MOMENTS, MEASURED ON PENCILS (RANDOMIZED). The stars pop during
/// LevelCompleteEarly, before LevelComplete, which is where Checks files this
/// completion's Solution. So they are set as they pop from what is already in,
/// and set again the moment Checks has filed the check (Refresh) - still
/// within the first moments of the pop. SetupCompletionStars is no use: it
/// runs when the NEXT level loads, with no stars built.
/// </summary>
[HarmonyPatch]
internal static class SuccessStars
{
    /// <summary>The screen that last popped its stars, and for which slot.</summary>
    private static LevelSuccess? _screen;
    private static int _slot = -1;

    [HarmonyPatch(typeof(LevelSuccess), nameof(LevelSuccess.PopCompletionStars))]
    [HarmonyPrefix]
    private static void BeforePop(LevelSuccess __instance)
    {
        _screen = __instance;
        _slot = Checks.CurrentSlot;
        Guarded(__instance, _slot, "pop");
    }

    /// <summary>
    /// Set the stars again once Checks has filed what this completion earned.
    /// Only for the slot whose stars just popped.
    /// </summary>
    internal static void Refresh(int slot)
    {
        if (_screen == null || slot < 0 || slot != _slot) return;
        Guarded(_screen, slot, "after the check");
    }

    private static void Guarded(LevelSuccess screen, int slot, string when)
    {
        try
        {
            Apply(screen, slot, when);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"success stars: {e.Message}");
        }
    }

    /// <summary>
    /// Does nothing when no run is up or the level is not one of its slots,
    /// so a puzzle played outside a run keeps the game's own stars.
    /// </summary>
    private static void Apply(LevelSuccess screen, int slot, string when)
    {
        var router = Checks.Router;
        if (router == null || screen == null || slot < 0) return;

        var stars = screen.stars;
        if (stars == null) return;

        var (lit, total) = router.SolutionStars(slot, Checks.Ledger.IsCollected);
        var all = total > 0 && lit >= total;

        // LevelSuccess.stars is the container; each star is a child built from
        // starPrefab. A LevelCompletionStar carries its toggle; a bare Toggle
        // is taken as it is, the way the card's stars are.
        var n = 0;
        for (int i = 0; i < stars.childCount; i++)
        {
            var child = stars.GetChild(i);
            var star = child.GetComponent<LevelCompletionStar>();
            var toggle = star != null ? star.starToggle : child.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle == null) continue;
            var on = all || n < lit;
            if (toggle.isOn != on) toggle.SetIsOnWithoutNotify(on);
            n++;
        }

        Plugin.Logger.LogInfo(
            $"success stars ({when}): slot {slot}, {lit} of {total} solution(s) in, {n} star(s) on screen");
    }
}
