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
/// What screen every level finishes on, for all of them at once.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// What screen every level ends on, for all of them at once.
    ///
    /// THE QUESTION THIS ANSWERS. droha: some levels finish on the
    /// three-button panel - restart, pause menu, next arrow - and others drop
    /// you straight into the next puzzle. Nobody knew whether that was the
    /// game's own design or something the mod introduced, and guessing was
    /// how the last three of these went wrong.
    ///
    /// NO LEVEL LOADING. LevelManager.AllLevelInterfaces holds every level's
    /// interface at once, so the whole table can be read from the title
    /// screen in one frame. Loading 111 levels to ask each one a question it
    /// can answer while asleep would take an hour and prove the same thing.
    ///
    /// The flags, and why each is here:
    ///   PreventRetryMenu    authored per level, in the scene data
    ///   DoPreventRetryMenu  the computed answer - authored AND anything else
    ///   ShowRetryMenu       what the game will actually do
    ///   CompleteSilently    finishes with no completion beat at all
    ///   TransitionSilently  moves on with no transition
    ///   IsDailyTidy         the mod forces this false during a run, so a
    ///                       daily-pool level takes a different path than it
    ///                       does in vanilla - the one place the mod is
    ///                       implicated
    ///
    /// Printed as TSV so it can go straight into docs/data/ and be diffed
    /// against apworld/alttl/data/levels.json.
    /// </summary>
    private static void DumpEndings()
    {
        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("endings: no LevelManager yet");
            return;
        }

        // The FIELDS, not AllLevelInterfaces - that one is a method taking a
        // bool whose meaning is not recoverable from a signature-only
        // interop assembly, and guessing an argument is how the last few of
        // these went wrong. m_allLevelInterfaces first because the name says
        // it is the complete set.
        var all = manager.m_allLevelInterfaces;
        var which = "m_allLevelInterfaces";
        if (all == null || all.Length == 0)
        {
            all = manager.LevelInterfaces;
            which = "LevelInterfaces";
        }
        if (all == null || all.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("endings: no level interfaces");
            return;
        }
        DevToolsPlugin.Log.LogInfo($"endings: reading {all.Length} from {which}");

        DevToolsPlugin.Log.LogInfo(
            "endings\tlevelIndex\tlevelId\tpreventRetryMenu\tdoPreventRetryMenu"
            + "\tshowRetryMenu\tcompleteSilently\ttransitionSilently"
            + "\tisDailyTidy\tisHolidayDaily\tisCampaign\tisArchive");

        var panels = 0;
        var silent = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var li = all[i];
            if (li == null) continue;

            // Every read is wrapped: these are computed properties on an
            // interop type, and one of them throwing must not cost the other
            // hundred and ten rows.
            var row = string.Join("\t",
                Num(() => li.LevelIndex),
                Str(() => li.LevelId),
                Flag(() => li.PreventRetryMenu),
                Flag(() => li.DoPreventRetryMenu),
                Flag(() => li.ShowRetryMenu),
                Flag(() => li.CompleteSilently),
                Flag(() => li.TransitionSilently),
                Flag(() => li.IsDailyTidy),
                Flag(() => li.IsHolidayDaily),
                Flag(() => li.IsCampaignLevel),
                Flag(() => li.IsArchiveLevel));

            DevToolsPlugin.Log.LogInfo("endings\t" + row);

            try
            {
                if (li.ShowRetryMenu) panels++;
                if (li.CompleteSilently) silent++;
            }
            catch
            {
                // Counted best-effort; the rows above are the real output.
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"endings: {all.Length} level(s), {panels} showing the retry panel, "
            + $"{silent} completing silently");
    }

    private static string Flag(Func<bool> read)
    {
        try { return read() ? "1" : "0"; }
        catch { return "?"; }
    }

    private static string Num(Func<int> read)
    {
        try { return read().ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }
}
