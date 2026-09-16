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

namespace ALTTLDevTools;

/// <summary>
/// The resolution list and setting one.
///
/// RESOLUTION INDEXES ARE NOT STABLE and must never be used as identifiers.
/// </summary>
public partial class DevToolsBehaviour
{
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
}
