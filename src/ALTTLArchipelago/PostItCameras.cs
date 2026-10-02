using System;
using HarmonyLib;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Post-It Notes (Randomized): a note camera still drawing when the level is
/// torn down or rebuilt is switched off first, so nothing frees a texture it
/// is drawing into.
///
/// Each of the level's 15 notes (NoteVisualSetter) takes a texture from a
/// shared RenderTextureManager pool about 5 frames after the level is built,
/// switches its line camera on to draw into it, and switches it off again
/// within 2 s. In some loads the cameras stay on for good; then the textures
/// are freed under them and Unity logs "Releasing render texture that is set
/// as Camera.targetTexture!" once per note: 15 lines (DevTools trace:,
/// cameras:, values:, 2026-10-01; 7 of 7 runs that logged them had the
/// cameras on at load, none of the others).
///
/// When: only when the level starts straight after another level finishes
/// (2 of 6 by the panel's route, 9 of 18 by the old Daily page rescue), never
/// from a card (0 of 5). Why the notes' own snapshot leaves the cameras on is
/// still open (docs/dev/backlog.md); this is the backup droha asked for
/// alongside finding that out.
///
/// WHERE: LevelManager.DestroyLevel, which every way out of a finished level
/// ends in (the tween-out tail-calls it), and the level's own CleanUp, which
/// a rebuild of the level over itself runs first. A CleanUp hook alone never
/// fired: at a finish nothing in the randomizer or the pool is called (trace),
/// the textures go with the level.
///
/// Only during a run, and only the cameras the randomizer itself lists. The
/// level is finished or being rebuilt at these points, so nothing on screen
/// depends on them; a camera switched off keeps its last picture. Its own
/// class, so a failed patch costs only this (Plugin's table).
/// </summary>
[HarmonyPatch]
internal static class PostItCameras
{
    [HarmonyPatch(typeof(LevelManager), "DestroyLevel", new[] { typeof(LevelInterface) })]
    [HarmonyPrefix]
    private static void BeforeDestroyLevel(LevelInterface __0)
    {
        try
        {
            if (!Track.Active || __0 == null) return;
            var level = __0.Level;
            if (level == null) return;
            var randomizer = level.GetComponent<PostItNote_LevelRandomizer>();
            if (randomizer != null) StopDrawing(randomizer, "torn down");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"post-it cameras: {e.Message}");
        }
    }

    [HarmonyPatch(typeof(PostItNote_LevelRandomizer), "CleanUp", new Type[0])]
    [HarmonyPrefix]
    private static void BeforeCleanUp(PostItNote_LevelRandomizer __instance)
    {
        try
        {
            if (!Track.Active || __instance == null) return;
            StopDrawing(__instance, "rebuilt");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"post-it cameras: {e.Message}");
        }
    }

    private static void StopDrawing(PostItNote_LevelRandomizer randomizer, string when)
    {
        var cameras = randomizer.cameras;
        if (cameras == null) return;

        var stopped = 0;
        for (int i = 0; i < cameras.Count; i++)
        {
            var cam = cameras[i];
            if (cam == null || cam.targetTexture == null) continue;
            cam.targetTexture = null;
            cam.enabled = false;
            stopped++;
        }
        if (stopped > 0)
        {
            Plugin.Logger.LogInfo(
                $"post-it cameras: {stopped} note camera(s) still drawing as the level is {when} - "
                + "switched off first");
        }
    }
}
