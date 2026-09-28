using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// Give the cursor back when something hides it in the middle of a run puzzle.
///
/// The 0.4.2 playtest (backlog, "No cursor after Tupperware Tower"): after
/// Tupperware Tower the game went straight on into Breadtags in the same
/// second, and the cursor was gone inside Breadtags - fine in the pause menu,
/// back after Restart. Measured 2026-09-27 with DevTools `cursor`: Tupperware
/// Tower hides the cursor the moment its Tower group is solved, and in the
/// campaign the credits come next, which play with no cursor anyway. A level
/// booted straight after that hidden state came in WITH its cursor, so what
/// hid it in Breadtags is not known; the harness cannot finish Tupperware
/// Tower (release_e2e KNOWN_UNFORCEABLE), so it waits for a hand test.
///
/// So this is a safety net, not a cause: a hide that lands while a run puzzle
/// is loaded, unsolved, not transitioning and not in an interlude is undone on
/// the next frame, and logged - so the next occurrence names itself.
/// </summary>
[HarmonyPatch]
internal static class CursorGuard
{
    private static bool _restore;

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.SetCursorState))]
    [HarmonyPostfix]
    private static void AfterSetCursorState(GameCursor.CursorState state)
    {
        if (state == GameCursor.CursorState.Hidden) Check("SetCursorState(Hidden)");
    }

    [HarmonyPatch(typeof(GameCursor), nameof(GameCursor.HideCursor))]
    [HarmonyPostfix]
    private static void AfterHideCursor() => Check("HideCursor");

    private static void Check(string how)
    {
        try
        {
            if (!Track.Active || Checks.CurrentSlot < 0) return;

            var gm = GameManager.Instance;
            var state = gm?.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
            if (state != "Gameplay_GameState") return;

            var li = gm!.levelManager?.ActiveLevelInterface;
            if (li == null || li.IsCredits || !li.LevelIsLoaded || li.IsTransitioning) return;
            // A finished puzzle hides it for its own ending; that one stays.
            if (li.Solved) return;
            if (InInterlude(li)) return;
            if (GameCursor.Input == null || GameCursor.Input.cursorMode != GameCursor.CursorMode.Human) return;

            _restore = true;
            Plugin.Logger.LogInfo(
                $"cursor: {how} while slot {Checks.CurrentSlot} ({li.LevelId}) was playable - showing it again");
        }
        catch
        {
            // A diagnostic safety net is not worth a throw into the game's call.
        }
    }

    /// <summary>Put the cursor back, a frame after the hide it answers.</summary>
    internal static void Tick()
    {
        if (!_restore) return;
        _restore = false;
        try
        {
            var cursor = GameCursor.Input;
            if (cursor != null && cursor.cursorState == GameCursor.CursorState.Hidden)
            {
                cursor.SetCursorState(GameCursor.CursorState.Hand);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"cursor: could not show it again: {e.Message}");
        }
    }

    /// <summary>An interlude hides the cursor on purpose while it plays.</summary>
    private static bool InInterlude(LevelInterface li)
    {
        var root = li.transform.root;
        if (root == null) return false;
        foreach (var interlude in root.GetComponentsInChildren<Interlude>(true))
        {
            if (interlude != null && interlude.InterludeInProgress) return true;
        }
        return false;
    }
}
