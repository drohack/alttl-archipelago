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
/// panel level with nothing left to find shows its panel and Tick presses its
/// arrow as soon as it is up - a pointer click on its Continue Button, the one
/// a player clicks.
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

        // THE PANEL ON SCREEN IS THE RetryMenu, and it is pressed the way a
        // player presses it: a pointer click on its Continue Button.
        //
        // Pressing ReplayMenu.NextLevel, a menu that is not shown, moved on
        // and left this panel up over the next puzzle (droha, 2026-09-25:
        // Candy's last solution, then Bones). Calling RetryMenu.NextLevel
        // directly moved on too, and STILL left it up: measured 2026-09-25 on
        // Paper Plane Supplies, RetryMenu active at alpha 1 over Broken Eggs.
        // The hiding lives in the button's pointer handling, not in
        // NextLevel; a pointer click on the same button advanced and hid it.
        var menu = UnityEngine.Object.FindObjectOfType<RetryMenu>();
        if (menu == null) return;

        // FULLY SHOWN, NOT JUST PRESENT. Pressed 0.4 s into RetryUI, while the
        // panel was still animating in, the game advanced and the panel's show
        // then finished over the next puzzle - measured 2026-09-25, Tea
        // Cabinet then Shells, with the Continue Button pressed the way a
        // player does. A player clicks a panel that has stopped moving.
        // Minimized counts: the panel shrinks to a bar when the pointer is
        // away, and a panel that did so before the press must still be pressed.
        bool settled;
        try { settled = (menu.Showing || menu.Minimized) && !menu.IsTransitioning; }
        catch { settled = true; }
        if (!settled)
        {
            _panelUp = -1f;
            return;
        }

        if (_panelUp < 0f) _panelUp = _waited;
        if (_waited - _panelUp < PressAfter) return;

        var slot = _continueSlot;
        _continueSlot = -1;
        if (PressContinue(menu))
        {
            Plugin.Logger.LogInfo($"retry panel: slot {slot} has nothing left to find - pressing the arrow");
            return;
        }

        Plugin.Logger.LogWarning(
            $"retry panel: slot {slot} - no Continue Button to press; calling NextLevel and hiding the panel");
        menu.NextLevel();
        menu.HideMenu(false);
    }

    /// <summary>
    /// A pointer click on the panel's Continue Button, through the
    /// EventSystem - down, up, click - as DevTools' press: does it.
    /// </summary>
    private static bool PressContinue(RetryMenu menu)
    {
        try
        {
            foreach (var button in menu.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                if (button == null || button.gameObject == null) continue;
                if (button.gameObject.name != "Continue Button") continue;

                var data = new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
                };
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                return UnityEngine.EventSystems.ExecuteEvents.Execute(
                    button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"retry panel: pressing Continue failed: {e.Message}");
        }
        return false;
    }

    private static void Log(string line)
    {
        if (line == _lastLogged) return;
        _lastLogged = line;
        Plugin.Logger.LogInfo(line);
    }
}
