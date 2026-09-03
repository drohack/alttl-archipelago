using System;
using System.IO;
using ALTTLArchipelago.Core;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// Keeps an Archipelago run out of the player's campaign save.
///
/// The game has ONE save, "save1", and SaveData.levelCompletionData is a flat
/// list with no scoping. A randomized run recording progress the normal way
/// would write it straight into the campaign. So instead of being careful
/// about what we write, we change where the game writes: while a session is
/// active, SaveSystem.GetSaveFilename returns save_ap_<slot>_<seed> and the
/// campaign file is never opened at all.
///
/// That distinction matters. Isolation by construction survives a crash, an
/// alt-F4 and a power cut, because the campaign file was never open to be
/// half-written. An archive-and-restore scheme does not.
///
/// The patch is a NO-OP whenever no session is active, so an installed-but-
/// unconnected mod leaves the game byte-identical to vanilla.
/// </summary>
internal static class SaveRedirect
{
    /// <summary>The save name to use, or null for the game's own.</summary>
    private static string? _active;

    /// <summary>Avoids re-checking the disk on every connect in one session.</summary>
    private static bool _backupChecked;

    internal static string? ActiveName => _active;
    internal static bool IsRedirected => _active != null;

    /// <summary>
    /// Point the game at this session's save and load it.
    ///
    /// Loading is not optional. SaveSystem.data still holds the CAMPAIGN save
    /// read at startup; leaving it there would mean the first SaveGame() writes
    /// campaign progress into the Archipelago file, and every unlock the run
    /// grants would be applied on top of the player's real progress.
    /// </summary>
    internal static void Begin(string slotName, string seed)
    {
        var name = SaveNames.ForSession(slotName, seed);

        BackUpCampaignSaveOnce();

        _active = name;
        Plugin.Logger.LogInfo($"save redirected to {name}");

        try
        {
            SaveSystem.LoadGame();
        }
        catch (Exception e) when (IsAlreadyCompletedTask(e))
        {
            // Benign and expected. LoadGame completes the same
            // TaskCompletionSource it completed during startup, so a SECOND
            // call always throws here - after the data has already been read.
            // ReportLoadedSave below confirms the swap really happened, which
            // is what actually matters.
            Plugin.Logger.LogInfo("reloaded (the startup load task was already complete)");
        }
        catch (Exception e)
        {
            // Anything else is not understood, so it is loud.
            Plugin.Logger.LogWarning($"LoadGame threw: {e}");
        }

        ReportLoadedSave();
    }

    /// <summary>
    /// Say what is actually in memory after the swap.
    ///
    /// Worth logging every time: the failure this guards against - campaign
    /// data still resident and about to be written into the run's file - is
    /// invisible until it has already happened.
    /// </summary>
    private static void ReportLoadedSave()
    {
        try
        {
            var data = SaveSystem.data;
            if (data == null)
            {
                Plugin.Logger.LogWarning("no save data loaded");
                return;
            }
            var entries = data.levelCompletionData?.Count ?? 0;
            Plugin.Logger.LogInfo($"active save now holds {entries} completion entries");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not inspect the loaded save: {e.Message}");
        }
    }

    /// <summary>
    /// Back to the campaign save. Called on disconnect so the title screen
    /// shows the player's real progress again rather than the run's.
    /// </summary>
    internal static void End()
    {
        if (_active == null) return;

        Plugin.Logger.LogInfo($"save redirect cleared, back to {SaveNames.Vanilla}");
        _active = null;

        try
        {
            SaveSystem.LoadGame();
        }
        catch (Exception e) when (IsAlreadyCompletedTask(e))
        {
            // Same benign case as in Begin: LoadGame re-completes a
            // TaskCompletionSource that startup already completed. The data
            // still loads.
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not reload the campaign save: {e.Message}");
        }

        ReportLoadedSave();
    }

    /// <summary>
    /// Copy the campaign save aside, once ever.
    ///
    /// Redundant if the redirect works, which is the point - it is the thing
    /// that helps if the redirect has a bug. Timestamped and never overwritten,
    /// so a later bad run cannot destroy an earlier good backup.
    /// </summary>
    private static void BackUpCampaignSaveOnce()
    {
        if (_backupChecked) return;
        _backupChecked = true;

        try
        {
            var path = CampaignSavePath();
            if (path == null || !File.Exists(path))
            {
                Plugin.Logger.LogInfo("no campaign save to back up");
                return;
            }

            // Once EVER, not once per launch. A per-process flag looked right
            // and produced a new backup on every start - three of them in one
            // testing session, all identical, quietly filling the save folder.
            var directory = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path) + ".backup-*";
            if (directory != null && Directory.GetFiles(directory, pattern).Length > 0)
            {
                return;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backup = $"{path}.backup-{stamp}";
            if (File.Exists(backup)) return;

            File.Copy(path, backup);
            Plugin.Logger.LogInfo($"campaign save backed up to {Path.GetFileName(backup)}");
        }
        catch (Exception e)
        {
            // A failed backup must not stop the player connecting - the
            // redirect is what actually protects the save.
            Plugin.Logger.LogWarning($"could not back up the campaign save: {e.Message}");
        }
    }

    /// <summary>
    /// The full path of the CAMPAIGN save.
    ///
    /// GetSavePath already returns a complete file path, filename and
    /// extension included - despite the name, it is not a directory. Combining
    /// it with GetSaveFilename produced ".../save1.json\save1.json", which
    /// does not exist, so the backup silently did nothing.
    ///
    /// It also honours the redirect, which is exactly why the redirect works
    /// at all: patching GetSaveFilename is enough to move every read and write.
    /// The redirect is therefore turned off around this call so the answer is
    /// the game's own.
    /// </summary>
    /// <summary>
    /// The benign "already completed" case.
    ///
    /// Matched on the MESSAGE, not the type: the exception crosses the IL2CPP
    /// boundary wrapped in an Il2CppException, so catching InvalidOperationException
    /// never fired and the warning kept appearing for something harmless.
    /// </summary>
    private static bool IsAlreadyCompletedTask(Exception e)
        => e.Message.Contains("final state", StringComparison.Ordinal);

    private static string? CampaignSavePath()
    {
        try
        {
            var was = _active;
            _active = null;
            var path = SaveSystem.GetSavePath(false);
            _active = was;

            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not resolve the save path: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The redirect, on GetSavePath.
    ///
    /// THIS is the method that matters. Patching GetSaveFilename alone looked
    /// right and was not enough: SaveGame() went on writing to save1.json, and
    /// a test that wrote progress while connected modified the campaign save.
    /// GetSavePath does not build its answer by calling GetSaveFilename, so
    /// the filename patch never reached the write path.
    ///
    /// Rewriting the last path component rather than rebuilding the path keeps
    /// the game's own directory, extension and platform handling intact - we
    /// only change WHICH file inside it.
    /// </summary>
    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.GetSavePath))]
    [HarmonyPostfix]
    private static void RedirectSavePath(ref string __result)
    {
        if (_active == null || string.IsNullOrEmpty(__result)) return;

        try
        {
            var directory = Path.GetDirectoryName(__result);
            var extension = Path.GetExtension(__result);
            if (string.IsNullOrEmpty(directory)) return;

            __result = Path.Combine(directory, _active + extension);
        }
        catch (Exception e)
        {
            // Leaving __result alone means writing to the campaign save, which
            // is the one outcome this class exists to prevent - so it is loud.
            Plugin.Logger.LogError(
                $"COULD NOT REDIRECT THE SAVE PATH, disconnecting to protect "
                + $"the campaign save: {e.Message}");
            _active = null;
        }
    }

    /// <summary>
    /// Kept alongside the path patch because other code asks for the name on
    /// its own, and the two must not disagree about which save is in use.
    /// </summary>
    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.GetSaveFilename))]
    [HarmonyPostfix]
    private static void RedirectSaveFilename(ref string __result)
    {
        if (_active != null) __result = _active;
    }
}
