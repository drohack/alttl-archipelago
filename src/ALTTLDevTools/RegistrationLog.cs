using System;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Record every controller registration, with the frame it happened on.
///
/// WHY THIS EXISTS. "Does this level reveal controllers as you solve it, or is
/// that thing in the prefab dead weight?" has been answered wrong three times
/// in this project, each time by counting controllers once and treating the
/// count as the level's contents. TupperwareNesting reports 2 at boot and
/// registers 7 by the time a player has worked through it; Radial Dance Party
/// reports 0 and nobody yet knows whether its ten rings ever register at all.
/// Two separate investigations "confirmed" the boot-time numbers by measuring
/// the same instant twice - a 25-second wait and a 20-frame wait observe
/// exactly the same state when the trigger is a SOLVE rather than time.
///
/// A timeline settles it by construction. A controller that registers on the
/// boot frame is always-on; one that registers later is phase-revealed, and
/// the log says what preceded it; one that never appears is a ghost, and no
/// amount of waiting will change that. The distinction stops being a judgement
/// call.
///
/// Level.RegisterObjectController is the single funnel every controller goes
/// through, so one postfix catches all of them regardless of which bespoke
/// Level subclass is driving the reveal.
///
/// Off unless asked for. It writes a line per registration, and a level sweep
/// registers a couple of hundred.
/// </summary>
[HarmonyPatch]
public static class RegistrationLog
{
    private static bool _on;
    private static StringBuilder? _out;

    private static string LogFile => Path.Combine(
        Path.GetDirectoryName(Application.dataPath)!, "BepInEx", "alttl-registrations.tsv");

    internal static bool Active => _on;

    /// <summary>Start recording. Clears anything already collected.</summary>
    internal static void Start()
    {
        _out = new StringBuilder();
        _out.AppendLine("frame\tlevelId\tlevelType\tcontroller\tcontrollerType\tregisteredSoFar");
        _on = true;
        DevToolsPlugin.Log.LogInfo("registrations: recording started");
    }

    /// <summary>Stop and write what was collected.</summary>
    internal static void Stop()
    {
        _on = false;
        if (_out == null)
        {
            DevToolsPlugin.Log.LogWarning("registrations: nothing recorded");
            return;
        }

        try
        {
            File.WriteAllText(LogFile, _out.ToString());
            DevToolsPlugin.Log.LogInfo($"registrations: written to {LogFile}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"registrations: could not write: {e.Message}");
        }
        _out = null;
    }

    /// <summary>
    /// Note one registration.
    ///
    /// PATCHED ON THE CONTROLLER, NOT THE LEVEL. Both
    /// Level.RegisterObjectController(ObjectController) and
    /// LevelInterface.RegisterObjectController(ObjectController) exist and look
    /// like the obvious funnel; a patch on the Level one installed cleanly and
    /// recorded exactly zero events across a 111-level sweep. Controllers
    /// register THEMSELVES, through the no-argument
    /// ObjectController.RegisterObjectController(), and that is the only method
    /// actually on the path.
    ///
    /// A postfix, not a prefix: the controller has been added to the level's
    /// list by the time this runs, so the running total is meaningful and a
    /// throw here cannot stop a level building itself.
    /// </summary>
    [HarmonyPatch(typeof(ObjectController), nameof(ObjectController.RegisterObjectController))]
    [HarmonyPostfix]
    private static void AfterRegister(ObjectController __instance)
    {
        if (!_on || _out == null) return;

        try
        {
            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            var level = li == null ? null : li.Level;
            var levelId = li == null ? "?" : li.LevelId;
            var levelType = level == null ? "?" : level.GetIl2CppType().Name;
            var name = __instance == null ? "?" : __instance.gameObject.name;
            var type = __instance == null ? "?" : __instance.GetIl2CppType().Name;
            var count = level?.objectControllers == null
                ? -1 : level.objectControllers.Count;

            _out.Append(Time.frameCount.ToString(CultureInfo.InvariantCulture))
                .Append('\t').Append(levelId)
                .Append('\t').Append(levelType)
                .Append('\t').Append(name)
                .Append('\t').Append(type)
                .Append('\t').Append(count.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }
        catch
        {
            // Diagnostic only, and on the level-build path. A throw here would
            // break the thing being measured.
        }
    }
}
