using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// The campaign save's level entries: reading them (`unlocks`) and the three
/// commands that WRITE THE SAVE - unlockto:, marksolved: and resetlevels.
/// Never run those against a save that matters.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>`resetlevels`: level completion data back to a fresh save. Writes the save.</summary>
    private static void ResetLevels()
    {
        SaveSystem.data.ResetLevelData();
        SaveSystem.data.AddFirstLevelToCompletionData();
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo("level completion data reset to a fresh save");
    }

    /// <summary>
    /// The unlock picture for the base campaign: what is unlocked now, what
    /// the game says it would unlock next, and how the chapters partition the
    /// level list.
    /// </summary>
    private static void DumpUnlocks()
    {
        var gm = GameManager.Instance;
        var lm = gm.levelManager;
        var sb = new StringBuilder();

        sb.AppendLine("levelIndex\tlevelId\ttype\tchapter\tsolutionCount\tfound\tsolved\tcompleted"
                      + "\tisUnlocked\tunlockedOnLevelSelect\thasSaveEntry\tskipped\thintUsed"
                      + "\tstore\tsolutionsInSave\tisDailyTidy");

        var all = lm.AllLevelInterfaces(false);
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            var idx = Str(() => li.LevelIndex.ToString());
            // EVERY level, not just the base campaign.
            //
            // This filtered to index < 100 with the comment "base campaign
            // only: everything else has a synthetic index >= 100". The indices
            // are not synthetic - they are the game's own LevelInterface
            // .LevelIndex - and the filter hid exactly the levels droha
            // reported the empty completion star on. Procedural Grid Puzzle is
            // 1000, the randomized Pencils/Post-It/Stamps/Batteries/Books are
            // 995 to 999, and every DLC level is 1100 or above. The one
            // instrument that could answer the question could not see the
            // question.
            if (!int.TryParse(idx, out _)) continue;
            sb.Append(idx).Append('\t')
              .Append(Str(() => li.LevelId)).Append('\t')
              .Append(Str(() => li.LevelType.ToString())).Append('\t')
              .Append(Str(() => li.ChapterDetails == null ? "" : li.ChapterDetails.chapterNumber.ToString())).Append('\t')
              .Append(Str(() => li.SolutionCount.ToString())).Append('\t')
              .Append(Str(() => li.NumSolutionsFound.ToString())).Append('\t')
              .Append(Str(() => li.Solved.ToString())).Append('\t')
              .Append(Str(() => li.Completed.ToString())).Append('\t')
              .Append(Str(() => li.IsUnlocked.ToString())).Append('\t')
              .Append(Str(() => li.IsUnlockedOnLevelSelect().ToString())).Append('\t')
              .Append(Str(() => SaveSystem.data.LevelHasCompletionData(li).ToString())).Append('\t')
              .Append(Str(() => li.Skipped.ToString())).Append('\t')
              .Append(Str(() => li.HintUsed.ToString())).Append('\t')
              // WHICH STORE, AND HOW MANY SOLUTIONS ARE IN IT.
              //
              // The save keeps campaign and archive progress in two separate
              // lists, which Checks.SeedSolutionsFromSave found out the hard
              // way - reading only levelCompletionData found nothing for the
              // 26 archive levels and quietly did nothing, which looked
              // exactly like the fix working. A star that lights on some
              // levels and not others is the same shape of question, so the
              // store is a column rather than an assumption.
              .Append(StoreOf(li)).Append('\t')
              .Append(SolutionsInSave(li)).Append('\t')
              .Append(Str(() => li.IsDailyTidy.ToString()))
              .AppendLine();
        }

        File.WriteAllText(Path.Combine(GameDir, "BepInEx", "alttl-unlocks.tsv"), sb.ToString());

        // Chapter membership, straight from the authored ChapterDetails.
        var chapters = new StringBuilder();
        var chapterInterfaces = lm.GetAllChapterInterfaces();
        for (int i = 0; i < (chapterInterfaces == null ? 0 : chapterInterfaces.Length); i++)
        {
            var ch = chapterInterfaces![i];
            if (ch == null) continue;
            var cd = ch.ChapterDetails;
            var members = new StringBuilder();
            try
            {
                var list = cd.levelIndicesInChapter;
                for (int k = 0; k < (list == null ? 0 : list.Count); k++)
                {
                    if (k > 0) members.Append(',');
                    members.Append(list![k]);
                }
            }
            catch (Exception e) { members.Append("<err:").Append(e.GetType().Name).Append('>'); }

            chapters.AppendLine($"chapter {Str(() => cd.chapterNumber.ToString())}"
                + $"\t{Str(() => cd.chapterTitle)}"
                + $"\tinterfaceIndex={Str(() => ch.LevelIndex.ToString())}"
                + $"\tcompletion={Str(() => ch.ChapterCompletionPercentage.ToString())}%"
                + $"\tmembers=[{members}]");
        }
        File.WriteAllText(Path.Combine(GameDir, "BepInEx", "alttl-chapters.txt"), chapters.ToString());

        // What the game itself thinks comes next. Scoped to the base campaign
        // (indices below 100) so DLC and event levels do not skew the totals.
        var campaign = new Il2CppSystem.Collections.Generic.List<LevelInterface>();
        for (int i = 0; i < (all == null ? 0 : all.Length); i++)
        {
            var li = all![i];
            if (li == null) continue;
            try { if (li.LevelIndex < 100) campaign.Add(li); } catch { }
        }
        var info = new SaveData.LevelsCompletionInfo(campaign);
        DevToolsPlugin.Log.LogInfo(
            "campaign: levels=" + Str(() => info.LevelsCount.ToString())
            + " unlocked=" + Str(() => info.UnlockedCount.ToString())
            + " fullyUnlocked=" + Str(() => info.IsFullyUnlocked.ToString())
            + " unsolved=" + Str(() => info.UnsolvedCount.ToString())
            + " allSolved=" + Str(() => info.AllLevelsSolved.ToString())
            + " solutions=" + Str(() => info.SolutionsCount.ToString())
            + " solutionsFound=" + Str(() => info.SolutionsFoundCount.ToString())
            + " completion=" + Str(() => info.CompletionPercentageString)
            + " allSolutionsFound=" + Str(() => info.AllSolutionsFound.ToString())
            + " lastUnlocked=" + Str(() => info.LastUnlockedLevel == null ? "-" : info.LastUnlockedLevel.LevelId)
            + " firstUnsolved=" + Str(() => info.FirstUnsolvedLevel == null ? "-" : info.FirstUnsolvedLevel.LevelId)
            + " toUnlockOnSelect=" + Str(() => info.LevelsToUnlockOnSelect == null
                  ? "-" : info.LevelsToUnlockOnSelect.Count.ToString())
            + " | nextLevelIndex=" + Str(() => lm.GetNextLevelIndex().ToString())
            + " gameCompleteCheck=" + Str(() => lm.GameCompleteCheck().ToString()));

        DevToolsPlugin.Log.LogInfo("unlocks written to alttl-unlocks.tsv / alttl-chapters.txt");
    }

    /// <summary>
    /// Which of the save's two completion lists holds this level's row, or
    /// "-" when neither does.
    ///
    /// Reported rather than assumed. Track.ApplyUnlocks creates a row for
    /// every level it reveals, so "has a row" is nearly always true and says
    /// nothing; WHICH list it landed in, and whether that row carries any
    /// solutions, is the part that differs between a level whose star lights
    /// and one whose star does not.
    /// </summary>
    private static string StoreOf(LevelInterface li)
    {
        try
        {
            var data = SaveSystem.data;
            if (data == null) return "<no save>";
            var id = li.LevelId;
            if (RowFor(data.levelCompletionData, id) != null) return "campaign";
            if (RowFor(data.archiveCompletionData, id) != null) return "archive";
            return "-";
        }
        catch (Exception e) { return "<err:" + e.GetType().Name + ">"; }
    }

    /// <summary>
    /// How many solutions the save's row records for this level.
    ///
    /// "null" and "0" are printed as different things ON PURPOSE.
    /// Track.ApplyUnlocks calls CreateLevelCompletionData(level, null), so a
    /// revealed-but-unplayed level can carry a row whose solutions list was
    /// never constructed. A count of 0 means the list exists and is empty,
    /// which is a different state and possibly a different bug.
    /// </summary>
    private static string SolutionsInSave(LevelInterface li)
    {
        try
        {
            var data = SaveSystem.data;
            if (data == null) return "-";
            var id = li.LevelId;
            var row = RowFor(data.levelCompletionData, id)
                      ?? RowFor(data.archiveCompletionData, id);
            if (row == null) return "-";
            var solutions = row.solutions;
            return solutions == null ? "null" : solutions.Count.ToString();
        }
        catch (Exception e) { return "<err:" + e.GetType().Name + ">"; }
    }

    /// <summary>
    /// The save row for a level id, or null. Both lists are searched by the
    /// callers above, in that order, for the reason Checks.SolutionIdsFor
    /// records: reading only the campaign list misses every archive level.
    /// </summary>
    private static SaveData.LevelCompletionData? RowFor(
        Il2CppSystem.Collections.Generic.List<SaveData.LevelCompletionData>? all,
        string levelId)
    {
        if (all == null) return null;
        for (int i = 0; i < all.Count; i++)
        {
            var entry = all[i];
            if (entry != null && entry.levelId == levelId) return entry;
        }
        return null;
    }

    /// <summary>
    /// "unlockto:N" gives the first N levels a LevelCompletionData entry, which
    /// IS the unlock condition, so the level select renders them in full colour.
    /// Needed to compare tracker markers: a fresh save shows three unlocked
    /// cards, and the markers only matter on unlocked ones.
    /// </summary>
    private static void UnlockTo(string arg)
    {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var count))
        {
            DevToolsPlugin.Log.LogWarning($"unlockto: not a number: {arg}");
            return;
        }

        var manager = GameManager.Instance.levelManager;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var li = manager.GetLevelInterface(i);
                if (li == null || SaveSystem.data.LevelHasCompletionData(li)) continue;
                SaveSystem.data.CreateLevelCompletionData(li, null);
                made++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"unlockto: index {i}: {e.Message}");
            }
        }
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"unlockto: created {made} completion entries up to index {count}."
            + " Reopen the level select to see them.");
    }

    /// <summary>
    /// Marks a level solved in the save exactly the way the game does, so the
    /// unlock rule can be observed rather than guessed. "marksolved:INDEX" or
    /// "marksolved:INDEX:solutionId".
    ///
    /// NOT "solve:", which is what it asked for and never got - SolveController
    /// matched that token first, so this could not run at all: two commands
    /// had collided on one prefix in the old if/else ladder. The command table
    /// looks keywords up whole, so that cannot happen again.
    /// </summary>
    private static void MarkSolved(string arg)
    {
        var parts = arg.Split(':');
        var index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var solutionId = parts.Length > 1 ? parts[1] : "probe_0";
        var li = GameManager.Instance.levelManager.GetLevelInterface(index);
        SaveSystem.data.SaveLevelData(li, solutionId, true);
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"marksolved: {li.LevelId} solutionId={solutionId} -> found={li.NumSolutionsFound} solved={li.Solved}");
    }
}
