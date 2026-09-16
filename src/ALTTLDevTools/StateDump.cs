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
/// The whole level table as JSON - the dump every other tool reads.
///
/// tools/check-game-facts.py holds apworld/alttl/data/levels.json up against
/// what this writes. TAKE THE DUMP WITH THE RANDOMIZER MOD MOVED OUT of
/// BepInEx/plugins entirely: its daily guard answers IsDailyTidy false while a
/// run is active, so a contaminated dump reports zero daily levels and looks
/// like proof there are none.
/// </summary>
public partial class DevToolsBehaviour
{
    // ------------------------------------------------------------------ dump

    private static void Dump()
    {
        var gm = GameManager.Instance;
        var j = new Json();

        j.Open();
        j.Prop("gameVersion", Str(() => Application.version));
        j.Prop("dumpedAt", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));

        DumpLevels(j, gm);
        DumpDailyTidy(j, gm);
        DumpArchive(j, gm);
        DumpDlc(j, gm);

        j.Close();

        File.WriteAllText(DumpFile, j.ToString());
        DevToolsPlugin.Log.LogInfo($"dump written to {DumpFile} ({j.ToString().Length} bytes)");
    }

    private static void DumpLevels(Json j, GameManager gm)
    {
        j.Key("levels");
        j.OpenArray();
        try
        {
            var all = gm.levelManager.AllLevelInterfaces(false);
            var count = all == null ? 0 : all.Length;
            DevToolsPlugin.Log.LogInfo($"AllLevelInterfaces: {count}");
            for (int i = 0; i < count; i++)
            {
                var li = all![i];
                if (li == null) continue;
                j.Open();
                j.Prop("levelId", Str(() => li.LevelId));
                j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                j.Prop("levelType", Str(() => li.LevelType.ToString()));
                j.Prop("addressablesKey", Str(() => li.AddressablesKey));
                j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                j.Prop("numSolutionsFound", Str(() => li.NumSolutionsFound.ToString()));
                j.Prop("isUnlocked", Str(() => li.IsUnlocked.ToString()));
                j.Prop("isManualUnlock", Str(() => li.IsManualUnlock.ToString()));
                j.Prop("numStarsReqToUnlock", Str(() => li.NumStarsReqToUnlock.ToString()));
                j.Prop("isCredits", Str(() => li.IsCredits.ToString()));
                j.Prop("isArchived", Str(() => li.IsArchived.ToString()));
                j.Prop("isDailyTidy", Str(() => li.IsDailyTidy.ToString()));
                j.Prop("isRandomizable", Str(() => li.IsRandomizable.ToString()));
                j.Prop("hasColourblindMode", Str(() => li.HasColourblindMode.ToString()));
                j.Prop("keepScore", Str(() => li.KeepScore.ToString()));
                j.Prop("maxScore", Str(() => li.MaxScore.ToString()));
                j.Prop("chapterNumber", Str(() => li.ChapterDetails == null
                    ? "" : li.ChapterDetails.chapterNumber.ToString()));
                j.Prop("chapterTitle", Str(() => li.ChapterDetails == null
                    ? "" : li.ChapterDetails.chapterTitle));
                j.Prop("dlcKey", Str(() => li.DLCDetails == null ? "" : li.DLCDetails.key));
                j.Prop("dailyDateCount", Str(() => li.DailyDates == null
                    ? "0" : li.DailyDates.Length.ToString()));
                j.Close();
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpLevels: {e}");
        }
        j.CloseArray();
    }

    private static void DumpDailyTidy(Json j, GameManager gm)
    {
        j.Key("dailyTidy");
        j.Open();
        try
        {
            var dt = gm.DailyTidyManager;
            j.Prop("initialized", Str(() => dt.DailyTidyInitialized.ToString()));
            j.Prop("dailyDateString", Str(() => dt.DailyDateString));
            j.Prop("completeCount", Str(() => dt.DailyCompleteCount.ToString()));
            j.Prop("currentStreak", Str(() => dt.CurrentStreak.ToString()));
            j.Prop("sequenceRandomSeed", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.randomSeed.ToString()));
            j.Prop("sequenceCount", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.sequenceCount.ToString()));
            j.Prop("levelRepeatMinDays", Str(() => dt.DailyTidySequencingDetails == null
                ? "" : dt.DailyTidySequencingDetails.levelRepeatMinDays.ToString()));

            j.Key("levels");
            j.OpenArray();
            var levels = dt.GetDailyTidyLevels(true);
            var n = levels == null ? 0 : levels.Length;
            DevToolsPlugin.Log.LogInfo($"daily tidy levels (incl. holidays): {n}");
            for (int i = 0; i < n; i++)
            {
                var li = levels![i];
                if (li == null) continue;
                j.Open();
                j.Prop("levelId", Str(() => li.LevelId));
                j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                j.Prop("isRandomizable", Str(() => li.IsRandomizable.ToString()));
                j.Prop("isHolidayDaily", Str(() => li.IsHolidayDaily.ToString()));
                j.Prop("dailyDateCount", Str(() => li.DailyDates == null
                    ? "0" : li.DailyDates.Length.ToString()));
                j.Close();
            }
            j.CloseArray();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpDailyTidy: {e}");
        }
        j.Close();
    }

    private static void DumpArchive(Json j, GameManager gm)
    {
        j.Key("archive");
        j.Open();
        try
        {
            var am = gm.ArchiveManager;

            j.Key("groups");
            j.OpenArray();
            var groups = am.ArchiveGroups;
            var gcount = groups == null ? 0 : groups.Count;
            DevToolsPlugin.Log.LogInfo($"archive groups: {gcount}");
            for (int i = 0; i < gcount; i++)
            {
                var g = groups![i];
                if (g == null) continue;
                j.Open();
                j.Prop("groupTitle", Str(() => g.GroupTitle));
                j.Key("levels");
                j.OpenArray();
                var lv = g.Levels;
                for (int k = 0; k < (lv == null ? 0 : lv.Count); k++)
                {
                    var li = lv![k];
                    if (li == null) continue;
                    j.Open();
                    j.Prop("levelId", Str(() => li.LevelId));
                    j.Prop("levelIndex", Str(() => li.LevelIndex.ToString()));
                    j.Prop("solutionCount", Str(() => li.SolutionCount.ToString()));
                    j.Close();
                }
                j.CloseArray();
                j.Close();
            }
            j.CloseArray();

            // Per-level solution ids the archive knows about. This is the one
            // place the game spells out what "a different way to solve it" is
            // actually called.
            j.Key("solutionIds");
            j.Open();
            try
            {
                var dict = am.archiveLevelSolutionsDict;
                if (dict != null)
                {
                    var keys = new List<string>();
                    var ke = dict.Keys.GetEnumerator();
                    while (ke.MoveNext()) keys.Add(ke.Current);
                    DevToolsPlugin.Log.LogInfo($"archive solution dict entries: {keys.Count}");
                    foreach (var key in keys)
                    {
                        var arr = dict[key];
                        var sb = new StringBuilder();
                        for (int i = 0; i < (arr == null ? 0 : arr.Length); i++)
                        {
                            if (i > 0) sb.Append('|');
                            sb.Append(arr![i]);
                        }
                        j.Prop(key, sb.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"solution dict: {e.Message}");
            }
            j.Close();
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpArchive: {e}");
        }
        j.Close();
    }

    private static void DumpDlc(Json j, GameManager gm)
    {
        j.Key("dlc");
        j.OpenArray();
        try
        {
            var dm = gm.DLCManager;
            var info = dm.DLCInfo;
            for (int i = 0; i < (info == null ? 0 : info.Count); i++)
            {
                var d = info![i];
                if (d == null) continue;
                j.Open();
                j.Prop("key", Str(() => d.key));
                j.Prop("name", Str(() => d.name));
                j.Prop("appId", Str(() => d.appID.ToString()));
                j.Prop("installed", Str(() => d.Installed.ToString()));
                j.Prop("levelCount", Str(() => d.levelInterfaces == null
                    ? "0" : d.levelInterfaces.Count.ToString()));
                j.Close();
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogError($"DumpDlc: {e}");
        }
        j.CloseArray();
    }

    /// <summary>Minimal JSON writer. Avoids a Newtonsoft dependency.</summary>
    private sealed class Json
    {
        private readonly StringBuilder _sb = new();
        private bool _needComma;

        private void Comma()
        {
            if (_needComma) _sb.Append(',');
            _needComma = false;
        }

        public void Open() { Comma(); _sb.Append('{'); }
        public void Close() { _sb.Append('}'); _needComma = true; }
        public void OpenArray() { Comma(); _sb.Append('['); }
        public void CloseArray() { _sb.Append(']'); _needComma = true; }

        public void Key(string k)
        {
            Comma();
            _sb.Append(Quote(k)).Append(':');
        }

        public void Prop(string k, string v)
        {
            Comma();
            _sb.Append(Quote(k)).Append(':').Append(Quote(v));
            _needComma = true;
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public override string ToString() => _sb.ToString();
    }
}
