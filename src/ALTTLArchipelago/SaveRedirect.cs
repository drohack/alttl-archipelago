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

        // Read BEFORE the redirect, while SaveSystem.data is still the
        // player's own save. See CarryPromptsOver.
        var prompts = ReadPrompts();

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

        CarryPromptsOver(prompts);
        ReportLoadedSave();
    }

    /// <summary>
    /// The one-off prompts the game shows until you answer them.
    ///
    /// Colour assist, the "puzzles have more than one solution" tutorial, and
    /// the daily-tidy ones. The game records each as a bool in the SAVE, which
    /// is fine in vanilla where there is one save - and wrong here, because a
    /// run gets its own file. Every new run therefore looked like a fresh
    /// install and asked again.
    /// </summary>
    private readonly struct Prompts
    {
        internal Prompts(bool colour, bool solutions, bool dtSelector,
                         bool dtStreak, bool dtBadges)
        {
            Colour = colour;
            Solutions = solutions;
            DtSelector = dtSelector;
            DtStreak = dtStreak;
            DtBadges = dtBadges;
        }

        internal bool Colour { get; }
        internal bool Solutions { get; }
        internal bool DtSelector { get; }
        internal bool DtStreak { get; }
        internal bool DtBadges { get; }
    }

    private static Prompts ReadPrompts()
    {
        try
        {
            var d = SaveSystem.data;
            if (d == null) return default;
            return new Prompts(
                d.seenColourAssistPrompt,
                d.seenMultipleSolutionTutorial,
                d.seenDTSelectorPrompt,
                d.seenDTStreakTutorial,
                d.seenDTAllBadgesEarnedPrompt);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not read the prompt flags: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// Tell the run's save what the player has already been asked.
    ///
    /// Copied ONE WAY and only where the campaign says true. A player who has
    /// answered the colour-assist prompt should not be asked again just
    /// because a run has its own file; a player who has never seen it still
    /// gets it, once, which is the point of the prompt.
    ///
    /// Never written back the other way. These are trivial UI flags, but the
    /// campaign save is not opened by a run for anything, and carving out an
    /// exception for "harmless" writes is how that guarantee stops meaning
    /// something.
    ///
    /// That used to mean answering a prompt inside a run did not stick for the
    /// NEXT run, recorded here as "a smaller problem than the one being
    /// solved". It was not small enough: droha's campaign save has every flag
    /// false, so nothing was ever carried and every new seed asked again. The
    /// mod now keeps its own note in PromptMemory, which is unioned in below -
    /// still without writing a byte to the campaign save.
    /// </summary>
    private static void CarryPromptsOver(Prompts p)
    {
        try
        {
            var d = SaveSystem.data;
            if (d == null) return;

            // Anything answered in a PREVIOUS run counts too.
            var remembered = PromptMemory.Load();
            p = new Prompts(
                p.Colour || remembered.Colour,
                p.Solutions || remembered.Solutions,
                p.DtSelector || remembered.DtSelector,
                p.DtStreak || remembered.DtStreak,
                p.DtBadges || remembered.DtBadges);

            var carried = 0;
            if (p.Colour && !d.seenColourAssistPrompt)
            { d.seenColourAssistPrompt = true; carried++; }
            if (p.Solutions && !d.seenMultipleSolutionTutorial)
            { d.seenMultipleSolutionTutorial = true; carried++; }
            if (p.DtSelector && !d.seenDTSelectorPrompt)
            { d.seenDTSelectorPrompt = true; carried++; }
            if (p.DtStreak && !d.seenDTStreakTutorial)
            { d.seenDTStreakTutorial = true; carried++; }
            if (p.DtBadges && !d.seenDTAllBadgesEarnedPrompt)
            { d.seenDTAllBadgesEarnedPrompt = true; carried++; }

            Plugin.Logger.LogInfo(carried > 0
                ? $"carried {carried} already-answered prompt(s) into the run"
                : "no answered prompts to carry over - the run will ask as a "
                  + "fresh install would, and will remember the answers");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not carry the prompt flags: {e.Message}");
        }
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

    /// <summary>
    /// The folder the game keeps saves in.
    ///
    /// Taken from the game's own answer rather than rebuilt from an
    /// AppData guess, so it stays right on whatever platform layout the game
    /// decides on. Note GetSavePath returns a FULL FILE PATH, not a directory -
    /// treating it as one produced ".../save1.json/save1.json" and a backup
    /// that silently wrote nothing.
    /// </summary>
    internal static string? SaveDirectory()
    {
        try
        {
            var path = SaveSystem.GetSavePath(false);
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"could not resolve the save folder: {e.Message}");
            return null;
        }
    }

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
