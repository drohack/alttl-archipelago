using System;
using HarmonyLib;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Stop the scroll wheel zooming the game while the window is not focused.
///
/// THE HALF THAT WAS ALREADY FIXED. ApplyFocusRule sets Rewired's
/// ignoreInputWhenAppNotInFocus, which covers everything routed through
/// Rewired. The game's own ZoomManager is not: it processes zoom itself, so
/// the wheel kept working with the window in the background. droha reported it
/// as "I thought we fixed that, but it seems to be lingering", and
/// "inconsistent" - which fits, because only one of the two paths was covered.
///
/// WHY THE GAME IS EVEN LISTENING. Application.runInBackground is TRUE in the
/// stock game - measured, and logged at every launch by the randomizer's
/// "stock: runInBackground=True" line, which is written before DevTools loads.
/// So the game keeps ticking and keeps polling while unfocused all on its own.
/// Nothing here turned that on.
///
/// WHY IT MATTERS BEYOND ANNOYANCE. It silently corrupts the evidence from a
/// scripted run. A probe that photographs each level to prove objects are
/// greyed out captured a frame zoomed deep into the microscope slide, because
/// the wheel was scrolled in another window while the run was going. The
/// numbers were still right - they come from object state, not pixels - but
/// every frame was useless.
///
/// DECLINING THE CALL rather than clearing m_allowZooming: that field is the
/// game's own state, driven by SetZoomEnabled / PauseZooming / ResumeZooming
/// around cutscenes and level completion, and writing it from outside would
/// fight whatever the game was in the middle of. Skipping one frame's
/// processing leaves all of that alone.
/// </summary>
[HarmonyPatch]
internal static class ZoomFocusGuard
{
    /// <summary>
    /// Refuse to process zoom while the window is in the background.
    ///
    /// Patched by NAME because ProcessZoom is private - there is no nameof for
    /// it - and by the same token this is a method the game could rename. It
    /// is registered in Plugin's patch list, and the "patched
    /// ZoomManager.ProcessZoom" line it prints at startup is the evidence it
    /// bound. If that line stops appearing, this has quietly stopped working.
    /// </summary>
    [HarmonyPatch(typeof(ZoomManager), "ProcessZoom")]
    [HarmonyPrefix]
    private static bool BeforeProcessZoom()
    {
        try
        {
            if (!DevToolsPlugin.IgnoreInputUnfocused) return true;
            return Application.isFocused;      // false = skip the original
        }
        catch (Exception e)
        {
            // A guard that throws must not take the camera with it.
            DevToolsPlugin.Log.LogWarning($"zoom guard: {e.Message}");
            return true;
        }
    }
}
