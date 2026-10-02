using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ALTTLModKit;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// The window and the sound: screenshots, the resolution list and setting
/// one, and muting.
///
/// RESOLUTION INDEXES ARE NOT STABLE and must never be used as identifiers.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// Every camera in the scene that draws into a texture, and every
    /// RenderTextureManager's pool, to compare a Post-It Notes finish that
    /// logs Unity's "Releasing render texture that is set as
    /// Camera.targetTexture!" with one that does not. Its notes draw through
    /// cameras of their own into textures from that pool, and its CleanUp
    /// releases the whole pool (members:, xrefs:, 2026-10-01).
    /// `cameras:all` lists every camera in the scene, drawing or not.
    /// </summary>
    private static void ReportCameras(string arg)
    {
        var every = arg.Trim() == "all";
        var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Camera>());
        int inScene = 0, drawing = 0;
        foreach (var obj in all)
        {
            var cam = obj == null ? null : obj.TryCast<Camera>();
            if (cam == null || !cam.gameObject.scene.IsValid()) continue;
            inScene++;
            var rt = cam.targetTexture;
            if (rt != null) drawing++;
            if (rt == null && !every) continue;
            var parent = cam.transform.parent;
            DevToolsPlugin.Log.LogInfo(
                $"cameras:   {Str(() => (parent == null ? "" : parent.name + "/") + cam.name)}"
                + $" enabled={cam.enabled} active={cam.gameObject.activeInHierarchy}"
                + (rt == null ? " -> screen"
                   : $" -> texture #{rt.GetInstanceID()} '{rt.name}' created={rt.IsCreated()}"));
        }
        DevToolsPlugin.Log.LogInfo($"cameras: {inScene} in the scene, {drawing} drawing into a texture");

        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<RenderTextureManager>()))
        {
            var rtm = obj == null ? null : obj.TryCast<RenderTextureManager>();
            if (rtm == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"cameras: pool '{rtm.name}' all={Str(() => rtm.allTextures.Count.ToString())}"
                + $" available={Str(() => rtm.availableTextures.Count.ToString())}"
                + $" used={Str(() => rtm.usedTextures.Count.ToString())}");
            Try("pool textures", () =>
            {
                var created = 0;
                var ids = new List<string>();
                for (int i = 0; i < rtm.allTextures.Count; i++)
                {
                    var t = rtm.allTextures[i]?.TryCast<RenderTexture>();
                    if (t == null) { ids.Add("null"); continue; }
                    if (t.IsCreated()) created++;
                    ids.Add("#" + t.GetInstanceID());
                }
                DevToolsPlugin.Log.LogInfo(
                    $"cameras:   {created} of {rtm.allTextures.Count} created: {string.Join(" ", ids)}");
            });
        }
    }

    /// <summary>
    /// What the game's resolution list actually contains, with indices.
    ///
    /// The save stores the player's choice as an INDEX into this list, and
    /// the list is built from the monitor the game opened on - so the same
    /// number means different things on different displays, and a stale
    /// index silently changes the window size. droha, who worked this out
    /// first: "the game changes the resolution list depending on what
    /// monitor opened it, so a number doesn't help me here."
    ///
    /// Printing the list is the only way to turn "index 0" into something a
    /// person can check.
    /// </summary>
    private static void DumpResolutions()
    {
        var all = Screen.resolutions;
        DevToolsPlugin.Log.LogInfo(
            $"resolutions: {(all == null ? 0 : all.Length)} available, "
            + $"current {Screen.width}x{Screen.height}, "
            + $"fullScreen={Screen.fullScreen} mode={Screen.fullScreenMode}");

        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var r = all![i];
            var here = r.width == Screen.width && r.height == Screen.height
                ? "  <- current size" : "";
            DevToolsPlugin.Log.LogInfo(
                $"resolutions:  [{i}] {r.width}x{r.height}{here}");
        }

        DumpGameList();
        DumpSavedChoice();
    }

    /// <summary>
    /// The GAME's own resolution list, which is not Unity's.
    ///
    /// SettingsMenu builds its own list for the dropdown, and Prefs.resolution
    /// is an index into THAT, not into Screen.resolutions. Unity's list had
    /// 135 entries here - every size repeated once per refresh rate - and no
    /// dropdown shows 135 rows, so the two cannot be the same list, and an
    /// index read against the wrong one is meaningless.
    ///
    /// This is the list that has to be indexed to answer "what is the saved
    /// choice actually asking for".
    /// </summary>
    private static void DumpGameList()
    {
        var menu = FindSettingsMenu();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogInfo(
                "resolutions: no SettingsMenu in the scene, so the game's own "
                + "list cannot be read from here");
            return;
        }

        var list = menu.resolutions;
        if (list == null)
        {
            DevToolsPlugin.Log.LogInfo(
                "resolutions: SettingsMenu found but its list is null - it is "
                + "populated when the settings screen opens");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"resolutions: game list has {list.Count} entry(s)");
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var here = r.width == Screen.width && r.height == Screen.height
                ? "  <- current size" : "";
            DevToolsPlugin.Log.LogInfo(
                $"resolutions:  game[{i}] {r.width}x{r.height}{here}");
        }
    }

    /// <summary>What the save believes the player picked.</summary>
    private static void DumpSavedChoice()
    {
        try
        {
            var data = SaveSystem.data;
            if (data == null || data.playerPrefs == null)
            {
                DevToolsPlugin.Log.LogInfo("resolutions: no save data loaded yet");
                return;
            }

            var prefs = data.playerPrefs;
            DevToolsPlugin.Log.LogInfo(
                $"resolutions: saved choice is index {prefs.resolution}, "
                + $"fullscreen={prefs.fullscreen}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning(
                $"resolutions: could not read the save: {e.Message}");
        }
    }

    /// <summary>
    /// SettingsMenu, whether or not its screen is open.
    ///
    /// FindObjectOfType only sees ACTIVE objects and the settings screen
    /// spends nearly all its life switched off, so this uses
    /// FindObjectsOfTypeAll, which does not.
    /// </summary>
    private static SettingsMenu? FindSettingsMenu()
    {
        try
        {
            // The interop shim only offers the non-generic overload, and it
            // hands back UnityEngine.Object, so the type comes from
            // Il2CppType and the result needs a TryCast.
            var found = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<SettingsMenu>());
            if (found == null) return null;
            for (int i = 0; i < found.Length; i++)
            {
                var menu = found[i]?.TryCast<SettingsMenu>();
                if (menu != null) return menu;
            }
            return null;
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning(
                $"resolutions: could not find the menu: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Set the resolution BY SIZE, through the game's own setter.
    ///
    ///     setres 1280 720
    ///
    /// By size and not by index, because the index is not a stable name for
    /// anything. droha, after a run of tests that each reported a different
    /// number for the same window: "the game changes the resolution list
    /// depending on what monitor opened it, so a number doesn't help me
    /// here. What did we say about indexes - they change. Don't use that as
    /// valid information." So the width and height are the input, and the
    /// index is looked up against the list the game has built right now.
    ///
    /// Through SettingsMenu.SetResolution rather than Screen.SetResolution,
    /// so the game persists the choice the same way it does when a person
    /// picks it from the dropdown. Calling Unity directly would move the
    /// window and leave the game's own idea of the setting untouched, which
    /// is exactly the split that made this bug hard to see.
    /// </summary>
    private static void SetResolution(string arg)
    {
        var parts = (arg ?? "").Replace(":", " ").Replace("x", " ")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var height))
        {
            DevToolsPlugin.Log.LogWarning("setres: give a size, as 'setres 1280 720'");
            return;
        }

        var menu = FindSettingsMenu();
        if (menu == null || menu.resolutions == null)
        {
            DevToolsPlugin.Log.LogWarning(
                "setres: no SettingsMenu with a populated list; open the "
                + "settings screen once so the game builds it");
            return;
        }

        var list = menu.resolutions;
        var index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].width != width || list[i].height != height) continue;
            index = i;
            break;
        }

        if (index < 0)
        {
            DevToolsPlugin.Log.LogWarning(
                $"setres: {width}x{height} is not in the game's list of "
                + $"{list.Count}; run 'resolutions' to see what is");
            return;
        }

        menu.SetFullscreen(false);
        menu.SetResolution(index);

        // AND THE SAVE, which is a separate store that can disagree.
        // SettingsMenu.SetResolution moves the window and writes Unity's
        // registry; it does not appear to touch Prefs. Setting only one of
        // the two is how the save came to hold index 0 - native - while the
        // registry held 1280x720, and the save is the one that wins at boot.
        var persisted = "not saved";
        try
        {
            var data = SaveSystem.data;
            if (data != null && data.playerPrefs != null)
            {
                data.playerPrefs.SetResolution(index);
                data.playerPrefs.SetFullscreen(false);
                SaveSystem.SaveGame();
                persisted = $"save now holds index {data.playerPrefs.resolution}";
            }
        }
        catch (Exception e)
        {
            persisted = $"save write failed: {e.Message}";
        }

        DevToolsPlugin.Log.LogInfo(
            $"setres: asked the game for {width}x{height} (its index {index}), "
            + $"windowed; {persisted}");
    }

    /// <summary>
    /// `shot:C:/path.png` captures the window as it is; `shot:C:/path.png|3`
    /// renders it at three times the size (at most 8).
    ///
    /// SUPERSIZE RATHER THAN A BIGGER WINDOW. Unity renders the frame at a
    /// multiple of the current resolution, so a detailed capture costs
    /// nothing but time - no resolution change, no window rebuild, and
    /// nothing of the player's display touched. Changing the resolution to
    /// take a picture would move the size the mod remembers, which is the one
    /// thing this project has been asked repeatedly not to do.
    /// </summary>
    private static void Screenshot(string arg)
    {
        var size = 1;
        var bar = arg.LastIndexOf('|');
        if (bar > 0
            && int.TryParse(arg.Substring(bar + 1), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 1)
        {
            size = Math.Min(parsed, 8);
            arg = arg.Substring(0, bar);
        }

        ScreenCapture.CaptureScreenshot(arg, size);
        DevToolsPlugin.Log.LogInfo($"screenshot requested: {arg} at {size}x");
    }

    /// <summary>
    /// `mute` / `mute:off` (`unmute`): hold AudioListener.volume at zero, or
    /// let it go - the same switch as the MuteAudio config setting.
    /// </summary>
    private static void SetMute(bool mute)
    {
        DevToolsPlugin.MuteAudio = mute;
        UnityEngine.AudioListener.volume = mute ? 0f : 1f;
        DevToolsPlugin.Log.LogInfo(
            $"audio: {(mute ? "muted" : "unmuted")}"
            + $" (AudioListener.volume={UnityEngine.AudioListener.volume:0.##})");
    }
}
