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
/// The background catalogue and forcing one, for checking the Background
/// Change Trap's palette.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// The game's own palette of level background colours.
    ///
    /// This is the catalogue a Background Change Trap indexes into, so its SIZE
    /// decides where the modulo wraps. Worth reading rather than assuming.
    /// </summary>
    private static void ReportBackgroundCatalogue()
    {
        var found = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<ColorSchemesData>()))
        {
            var data = obj == null ? null : obj.TryCast<ColorSchemesData>();
            if (data == null) continue;
            found++;

            var schemes = data.levelColorSchemes;
            DevToolsPlugin.Log.LogInfo(
                $"bgcatalogue: {data.name}"
                + $" levelColorSchemes={(schemes == null ? -1 : schemes.Length)}"
                + $" BackgroundColors={Str(() => data.BackgroundColors.Count.ToString())}");

            // Four ways to reach the same ten colours. Both the obvious ones
            // throw "Index was outside the bounds of the array" through
            // interop while Count and Length report 10 perfectly happily, so
            // this tries each and says which survived - guessing a third time
            // would be worse than measuring once.
            Try("schemes[i].backgroundColor", () =>
            {
                for (int i = 0; i < schemes.Length; i++)
                {
                    var scheme = schemes[i];
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   arr[{i}] {Rgb(scheme.backgroundColor)}");
                }
            });

            Try("BackgroundColors[i]", () =>
            {
                var colours = data.BackgroundColors;
                for (int i = 0; i < colours.Count; i++)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   list[{i}] {Rgb(colours[i])}");
                }
            });

            Try("BackgroundColors foreach", () =>
            {
                var n = 0;
                foreach (var c in data.BackgroundColors)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   iter[{n++}] {Rgb(c)}");
                }
            });

            Try("BackgroundColors.ToArray", () =>
            {
                var arr = data.BackgroundColors.ToArray();
                for (int i = 0; i < arr.Length; i++)
                {
                    DevToolsPlugin.Log.LogInfo(
                        $"bgcatalogue:   toarr[{i}] {Rgb(arr[i])}");
                }
            });
        }
        if (found == 0) DevToolsPlugin.Log.LogWarning("bgcatalogue: no ColorSchemesData loaded");
    }

    /// <summary>
    /// A colour as plain numbers.
    ///
    /// Not ColorUtility.ToHtmlStringRGB: every one of four different ways to
    /// read the palette failed with the same "Index was outside the bounds of
    /// the array", including on element zero, and the only thing all four had
    /// in common was that call. Formatting the channels by hand removes it
    /// from the experiment.
    /// </summary>
    private static string Rgb(Color c)
        => $"({c.r:F3},{c.g:F3},{c.b:F3})";

    /// <summary>Run a probe and report whether it survived.</summary>
    private static void Try(string what, Action body)
    {
        try
        {
            body();
            DevToolsPlugin.Log.LogInfo($"bgcatalogue: OK   {what}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"bgcatalogue: FAIL {what}: {e.Message}");
        }
    }

    /// <summary>
    /// Write a background colour onto the running level and say what changed.
    ///
    /// The point is to find out whether the WRITE IS READ. Both of these are
    /// plain fields, so assigning them always appears to succeed - the failure
    /// mode is that nothing on screen moves, with a perfectly healthy log. Take
    /// a screenshot after this; never trust the line it prints.
    /// </summary>
    private static void SetLevelBackground(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        if (li == null)
        {
            DevToolsPlugin.Log.LogWarning("bgset: no level running");
            return;
        }

        var text = arg.StartsWith("#") ? arg : "#" + arg;
        if (!ColorUtility.TryParseHtmlString(text, out var wanted))
        {
            DevToolsPlugin.Log.LogWarning($"bgset: cannot parse colour {arg}");
            return;
        }

        DevToolsPlugin.Log.LogInfo(
            $"bgset: before BackgroundColor={Str(() => Rgb(li.BackgroundColor))}"
            + $" Active={Str(() => Rgb(li.ActiveBackgroundColor))}");

        try { li.BackgroundColor = wanted; }
        catch (Exception e) { DevToolsPlugin.Log.LogWarning($"bgset: LevelInterface write threw: {e.Message}"); }

        try
        {
            var level = li.Level;
            if (level != null) level.backgroundColor = wanted;
        }
        catch (Exception e) { DevToolsPlugin.Log.LogWarning($"bgset: Level write threw: {e.Message}"); }

        DevToolsPlugin.Log.LogInfo(
            $"bgset: after  BackgroundColor={Str(() => Rgb(li.BackgroundColor))}"
            + $" Active={Str(() => Rgb(li.ActiveBackgroundColor))}");

        // Whatever actually paints the backdrop, name it. The camera clear
        // colour is the most likely and the cheapest to check.
        try
        {
            var cam = Camera.main;
            if (cam != null)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"bgset: Camera.main clearFlags={cam.clearFlags}"
                    + $" background={Rgb(cam.backgroundColor)}");
            }
        }
        catch { }
    }

    /// <summary>
    /// What draws the pause screen's background. Do not assume one Image.
    /// </summary>
    private static void ReportMenuBackground()
    {
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<MainMenu>()))
        {
            var menu = obj == null ? null : obj.TryCast<MainMenu>();
            if (menu == null || menu.gameObject == null) continue;
            if (!menu.gameObject.activeInHierarchy) continue;

            DevToolsPlugin.Log.LogInfo($"menubg: MainMenu {PathOf(menu.transform)}");
            var buttons = menu.ButtonsContainer;
            DevToolsPlugin.Log.LogInfo(
                $"menubg: ButtonsContainer {(buttons == null ? "null" : PathOf(buttons))}");
            foreach (var img in menu.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null || img.gameObject == null) continue;
                var rt = img.gameObject.GetComponent<RectTransform>();
                var size = rt == null ? "?" : $"{rt.rect.width:F0}x{rt.rect.height:F0}";
                DevToolsPlugin.Log.LogInfo(
                    $"menubg:   Image {PathOf(img.transform)}"
                    + $" active={img.gameObject.activeSelf}"
                    + $" size={size}"
                    + $" color={Rgb(img.color)}"
                    + $" sprite={(img.sprite == null ? "none" : img.sprite.name)}");
            }
            return;
        }
        DevToolsPlugin.Log.LogWarning("menubg: no active MainMenu - open the pause menu first");
    }
}
