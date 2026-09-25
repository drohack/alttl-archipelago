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
///
/// A LEVEL BUILT FOR THE PANEL KEEPS IT, AND THE ARROW IS PRESSED FOR IT.
/// Answering false for such a level sends it down the game's own straight-on
/// route, and after that route the pause menu's Exit does nothing: measured
/// 2026-09-25 on Seed Pods, where Exit never reached MainMenu.ExitGame, while
/// after Post-It Notes (a generator, straight on by design) it worked. So a
/// panel level with nothing left to find shows its panel and Tick presses the
/// arrow as soon as it is up - ReplayMenu.NextLevel, the route every release
/// gate has driven.
/// </summary>
[HarmonyPatch]
internal static class RetryPanel
{
    private static string _lastLogged = "";

    /// <summary>The slot whose panel Tick should press through, or -1.</summary>
    private static int _continueSlot = -1;
    private static float _waited;
    private static float _panelUp;

    /// <summary>Give up if no panel comes: nothing is pressed on a later one.</summary>
    private const float GiveUpAfter = 20f;

    /// <summary>Let the panel settle before pressing, as a player would.</summary>
    private const float PressAfter = 0.4f;

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

            var through = !offer.Value && __result;
            Log($"retry panel: slot {Checks.CurrentSlot}, {lit} of {total} solution(s) "
                + $"in after this completion -> "
                + (offer.Value ? "panel" : through ? "next, through the panel's arrow" : "next")
                + $" (the level's own: {(__result ? "panel" : "next")})");

            if (through)
            {
                _continueSlot = Checks.CurrentSlot;
                _waited = 0f;
                _panelUp = -1f;
                return;                                  // the game shows its panel
            }
            __result = offer.Value;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"retry panel: {e.Message}");
        }
    }

    /// <summary>
    /// Press the arrow on a panel level with nothing left to find, once its
    /// panel is up (RetryUI_GameState).
    /// </summary>
    internal static void Tick(float dt)
    {
        if (_continueSlot < 0) return;

        _waited += dt;
        if (Checks.CurrentSlot != _continueSlot || _waited > GiveUpAfter)
        {
            Plugin.Logger.LogInfo($"retry panel: no panel came for slot {_continueSlot}; nothing pressed");
            _continueSlot = -1;
            return;
        }

        var gm = GameManager.Instance;
        var state = gm == null || gm.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
        if (state != "RetryUI_GameState") return;

        // Found even while inactive, the way DevTools' `next` finds it: the
        // arrow a player presses on this panel runs ReplayMenu.NextLevel.
        var menu = Navigation.FindEvenIfInactive<ReplayMenu>();
        if (menu == null) return;

        if (_panelUp < 0f) _panelUp = _waited;
        if (_waited - _panelUp < PressAfter) return;

        var slot = _continueSlot;
        _continueSlot = -1;
        Plugin.Logger.LogInfo($"retry panel: slot {slot} has nothing left to find - pressing the arrow");
        menu.NextLevel();
    }

    private static void Log(string line)
    {
        if (line == _lastLogged) return;
        _lastLogged = line;
        Plugin.Logger.LogInfo(line);
    }
}
