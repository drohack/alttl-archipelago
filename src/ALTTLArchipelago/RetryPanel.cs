using System;
using ALTTLArchipelago.Core;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// Whether a finished run puzzle stops on the three-button panel (restart,
/// pause menu, next arrow) or moves on: the panel while the slot has more
/// than one Solution location and some are still to find, otherwise on to
/// the next (Checks.OfferRetry). droha, 2026-09-25.
///
/// The game decides this per level from LevelInterface.ShowRetryMenu, which
/// LevelSuccess.LevelComplete reads (DevTools xrefs, 2026-09-25): 148 of the
/// game's 186 levels show the panel, every generator goes straight on
/// (docs/reference/level-endings.tsv). Answered only for the level that is running
/// and only while it is a run slot; anything else keeps the game's value.
/// Where the arrow leads is Navigation's.
///
/// EVERY RUN PUZZLE BUT A GENERATOR TAKES THE PANEL'S ROUTE, and moving on
/// goes through the panel's own NextLevel. Answering false sends a level down
/// the game's own straight-on route, and after that route the pause menu's
/// Exit does nothing on a level built for the panel: measured 2026-09-25 on
/// Seed Pods, where Exit never reached MainMenu.ExitGame. The five
/// non-generator levels built to go straight on - Radial Dance Party,
/// Tupperware Nesting and Tower (the campaign's run into the credits) and the
/// two DLC bosses - take the panel's route too: Tupperware Tower left the
/// next puzzle with no cursor in the 0.4.2 playtest (see CursorGuard).
///
/// GENERATORS TOO, since 2026-10-01 (Core AfterPuzzleRoute): one ending for
/// every run level. They kept the game's own straight-on route, which ended
/// on the Daily page for DailyGuard to turn into the next slot ("daily
/// guard: opening slot N instead", 105 times in the second player's 0.4.2 playtest). The
/// game decides that page in LevelManager.OnLevelCompleteTweenOutComplete,
/// which every way out of a finished level passes through, panel or not;
/// DailyGuard now answers it there, so the panel's route goes on to the next
/// slot for a generator as for any level.
///
/// WITH NOTHING LEFT TO FIND, THE PANEL IS NEVER SHOWN. It used to pop up and
/// be pressed once it settled: the trace (2026-09-27, Parts Organizer) shows
/// ShowMenu 67 frames after the completion and the press 203 frames after
/// that. droha, watching it, asked for the pop-up to go ("it would be nice to
/// skip that animation at the end when it tries to auto move on"). So
/// BeforeShowMenu refuses to show it for such a slot, and Tick calls the
/// panel's NextLevel on the next frame and hides it at once. The press on a
/// shown panel (PressContinue) remains as the fallback for a panel that
/// showed anyway.
/// </summary>
[HarmonyPatch]
internal static class RetryPanel
{
    private static string _lastLogged = "";

    /// <summary>The slot to move on from without the panel, or -1.</summary>
    private static int _continueSlot = -1;
    private static float _waited;
    private static float _panelUp;

    /// <summary>A panel refused by BeforeShowMenu, to move on from in Tick.</summary>
    private static RetryMenu? _refused;

    /// <summary>
    /// No re-arming until this time. The game reads ShowRetryMenu again after
    /// the arrow is pressed, while the finished slot is still current, and
    /// that re-armed it for a slot already left ("no panel came for slot N"
    /// 22 times in the 0.4.2 playtest log).
    /// </summary>
    private static float _quietUntil;

    /// <summary>Give up if no panel comes: nothing is pressed on a later one.</summary>
    private const float GiveUpAfter = 20f;

    /// <summary>Let a panel that did show settle before pressing, as a player would.</summary>
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

            var own = __result;
            var slot = Checks.CurrentSlot;

            var generator = !own && IsGenerator(slot);
            var route = AfterPuzzleRoute.For(offer.Value, Skips.FinishingTheLevel, own);
            if (route == AfterPuzzle.TheGames)
            {
                // The game's skip moves this level out by itself; a panel on
                // top of it came up over the next puzzle (Core AfterPuzzleRoute).
                Log($"retry panel: slot {slot} is being skipped and the game sends it straight on "
                    + "- its own route, no panel");
                return;
            }
            if (route == AfterPuzzle.Panel)
            {
                __result = true;
                Log($"retry panel: slot {slot}, {lit} of {total} solution(s) "
                    + $"in after this completion -> panel (the level's own: {(own ? "panel" : "next")})");
                return;
            }

            __result = true;                             // the panel's route, unseen

            if (UnityEngine.Time.unscaledTime < _quietUntil) return;
            Log($"retry panel: slot {slot}, {lit} of {total} solution(s) "
                + $"in after this completion -> next, without showing the panel "
                + (generator ? "(a generator)" : $"(the level's own: {(own ? "panel" : "next")})"));
            _continueSlot = slot;
            _waited = 0f;
            _panelUp = -1f;
            _refused = null;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"retry panel: {e.Message}");
        }
    }

    /// <summary>A slot launched with a baked seed: one of the game's generators.</summary>
    private static bool IsGenerator(int slot)
    {
        var state = Track.State;
        return state != null && slot >= 0 && slot < state.Slots.Count && state.Slots[slot].Seed >= 0;
    }

    /// <summary>Do not show the panel for a slot that is moving straight on.</summary>
    [HarmonyPatch(typeof(RetryMenu), nameof(RetryMenu.ShowMenu))]
    [HarmonyPrefix]
    private static bool BeforeShowMenu(RetryMenu __instance)
    {
        try
        {
            if (_continueSlot < 0 || Checks.CurrentSlot != _continueSlot) return true;
            _refused = __instance;
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Move on from a slot with nothing left to find: through the refused
    /// panel's NextLevel on the frame after, or - if the panel showed
    /// anyway - by pressing its arrow once it has settled.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (_continueSlot < 0) return;

        _waited += dt;
        if (Checks.CurrentSlot != _continueSlot || _waited > GiveUpAfter)
        {
            Plugin.Logger.LogInfo($"retry panel: no panel came for slot {_continueSlot}; nothing pressed");
            _continueSlot = -1;
            _refused = null;
            return;
        }

        if (_refused != null)
        {
            var menu = _refused;
            var slot = _continueSlot;
            _refused = null;
            _continueSlot = -1;
            _quietUntil = UnityEngine.Time.unscaledTime + 5f;

            // NOT AFTER ANOTHER ROUTE HAS LEFT. If something else has already
            // left it - DevTools replayselect pressing the hidden panel's
            // Level Select in the frame between, which opened the track - the
            // panel's NextLevel threw (DLC navigation probe, 2026-10-02:
            // "moving on failed: NullReferenceException at RetryMenu.NextLevel").
            // Only for the screens such a route leads to; any other state,
            // a transient one included, still moves on as before, so a hidden
            // panel can never be left with nothing to take the player on.
            var game = GameManager.Instance;
            var now = game == null || game.GameState == null ? "" : game.GameState.GetIl2CppType().Name;
            if (now == "Levels_GameState" || now == "DLCLevels_GameState" || now == "Title_GameState")
            {
                Plugin.Logger.LogInfo(
                    $"retry panel: slot {slot} left the post-level screen another way first "
                    + $"({now}); not moving on");
                return;
            }
            Plugin.Logger.LogInfo(
                $"retry panel: slot {slot} has nothing left to find - moving on without the panel");
            try
            {
                // NextLevel first: Navigation's prefix on it decides where to
                // (the next playable slot, or the track when there is none).
                menu.NextLevel();
                menu.HideMenu(true);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"retry panel: moving on failed: {e.Message}");
            }
            return;
        }

        var gm = GameManager.Instance;
        var state = gm == null || gm.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
        if (state != "RetryUI_GameState") return;

        // A PANEL THAT SHOWED ANYWAY - BeforeShowMenu did not run, or the game
        // showed it some other way - is pressed the way a player presses it:
        // a pointer click on its Continue Button. Pressing ReplayMenu.NextLevel,
        // a menu that is not shown, moved on and left this panel up over the
        // next puzzle (droha, 2026-09-25: Candy's last solution, then Bones);
        // calling RetryMenu.NextLevel on a SHOWN panel did the same (Paper
        // Plane Supplies over Broken Eggs). The hiding lives in the button's
        // pointer handling.
        var shown = UnityEngine.Object.FindObjectOfType<RetryMenu>();
        if (shown == null) return;

        // FULLY SHOWN, NOT JUST PRESENT. Pressed 0.4 s into RetryUI, while the
        // panel was still animating in, the game advanced and the panel's show
        // then finished over the next puzzle (Tea Cabinet then Shells,
        // 2026-09-25). Minimized counts: the panel shrinks to a bar when the
        // pointer is away.
        bool settled;
        try { settled = (shown.Showing || shown.Minimized) && !shown.IsTransitioning; }
        catch { settled = true; }
        if (!settled)
        {
            _panelUp = -1f;
            return;
        }

        if (_panelUp < 0f) _panelUp = _waited;
        if (_waited - _panelUp < PressAfter) return;

        var pressed = _continueSlot;
        _continueSlot = -1;
        _quietUntil = UnityEngine.Time.unscaledTime + 5f;
        if (PressContinue(shown))
        {
            Plugin.Logger.LogInfo($"retry panel: slot {pressed} has nothing left to find - pressing the arrow");
            return;
        }

        Plugin.Logger.LogWarning(
            $"retry panel: slot {pressed} - no Continue Button to press; calling NextLevel and hiding the panel");
        shown.NextLevel();
        shown.HideMenu(false);
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
