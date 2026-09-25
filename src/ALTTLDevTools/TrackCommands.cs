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
/// The level-select track: what it holds, what each card's badge is reading,
/// and clicking or scrolling to one.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// "why:N" explains the badge on the card at track position N.
    ///
    /// A badge is four states wide and says nothing about WHY. When one reads
    /// wrong - a card still half red after it looked finished - the only way to
    /// settle it is to list the locations the card holds and say, for each,
    /// whether it is collected and whether it is reachable.
    ///
    /// Read out of the mod through a file it writes, so the dev tools do not
    /// need to reference it.
    /// </summary>
    private static void WhyBadge(string arg)
    {
        var path = Path.Combine(GameDir, "BepInEx", "alttl-why.txt");
        File.WriteAllText(path, arg.Trim());
        DevToolsPlugin.Log.LogInfo($"why: asked the mod about track position {arg.Trim()}");
    }

    /// <summary>
    /// "clicktrack:N" clicks the card at track POSITION N.
    ///
    /// Distinct from clickcard, which takes a level index. Once the track holds
    /// dividers and a credits card, position is the only way to say "the third
    /// thing on screen" - which is what a player actually clicks.
    /// </summary>
    private static void ClickTrack(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var position))
        {
            DevToolsPlugin.Log.LogWarning($"clicktrack: not a number: {arg}");
            return;
        }

        // The POPULATED track, not merely the first one found.
        //
        // FindObjectOfType returns one arbitrary active instance, and the scene
        // holds more than one LevelsTrack. Picking the wrong (empty) one made
        // every position report "not on the track" - including positions that
        // had just been clicked successfully - while the mod's own log happily
        // said it had built 35 items. Inactive ones count too: the menu is
        // cached and rebuilt, so the live track is not always the active one.
        // Prefer a LIVE track, and only then a populated one.
        //
        // Both halves were learned the hard way. FindObjectOfType returns one
        // arbitrary ACTIVE instance and picked an empty track, so every
        // position reported "not on the track". Widening to
        // FindObjectsOfTypeAll and taking the fullest one fixed that and broke
        // something quieter: the scene keeps stale tracks around, so after a
        // few menu transitions the fullest track is a LEFTOVER. Clicking its
        // icons resolves the level name perfectly and then does nothing at all,
        // because the icon is not the one on screen. A silent no-op is far
        // worse than a warning.
        LevelsTrack? track = null;
        int best = -1;
        bool bestLive = false;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var candidate = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (candidate == null) continue;

            int n;
            bool live;
            try
            {
                n = candidate.trackItems == null ? 0 : candidate.trackItems.Count;
                live = candidate.gameObject != null && candidate.gameObject.activeInHierarchy;
            }
            catch { continue; }

            // A live track always beats a dead one, however full the dead one.
            if (live != bestLive ? live : n > best)
            {
                best = n;
                bestLive = live;
                track = candidate;
            }
        }

        // Refuse to click a dead track rather than doing it silently.
        //
        // After a level completes, the level select takes a moment to come up:
        // the game state already says Levels_GameState while every LevelsTrack
        // is still inactive. Clicking then resolved the level name correctly
        // and did absolutely nothing, so a scripted run looked like the game
        // was ignoring it. Saying "not up yet" turns a silent no-op into
        // something a caller can wait on and retry.
        if (track == null || !bestLive)
        {
            DevToolsPlugin.Log.LogWarning(
                $"clicktrack: the track is not up yet (best was a"
                + $" {(track == null ? "missing" : "DEAD")} track of {best} item(s))");
            return;
        }

        DevToolsPlugin.Log.LogInfo($"clicktrack: using a live track of {best} item(s)");

        var items = track == null ? null : track.trackItems;
        if (items == null || position < 0 || position >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning(
                $"clicktrack: position {position} is not on the track"
                + $" (best track found had {best} item(s))");
            return;
        }

        var icon = items[position];
        DevToolsPlugin.Log.LogInfo(
            $"clicktrack: position {position} is {Str(() => icon.level.LevelId)}");

        // OnPointerClick, not DoStartLevel. A real click enters here and does
        // selection and transition work on the way; going straight to
        // DoStartLevel skips all of it, which made a card that breaks the menu
        // on a real click look perfectly fine under test.
        icon.OnPointerClick(null);
    }

    /// <summary>
    /// "focus:N" hovers the Nth card, and reports what the menu header says.
    ///
    /// A mouse cannot be scripted here, and whether hovering shows a level's
    /// name is exactly the kind of thing that has to be observed rather than
    /// reasoned about.
    /// </summary>
    private static void FocusIcon(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            DevToolsPlugin.Log.LogWarning($"focus: not a number: {arg}");
            return;
        }

        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        var items = track == null ? null : track.trackItems;
        if (items == null || index < 0 || index >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning("focus: no such card");
            return;
        }

        var icon = items[index];
        icon.OnFocus();
        icon.IconFocus();

        var select = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        DevToolsPlugin.Log.LogInfo(
            $"focus: card {index} is {Str(() => icon.level.LevelId)}"
            + $"; title=\"{Str(() => select.menuTitle.text)}\""
            + $" subtitle=\"{Str(() => select.menuSubtitle.text)}\"");
    }

    /// <summary>
    /// Every cat-ish component in the running scene.
    ///
    /// The question this answers is "does THIS level have a built-in cat, and
    /// what class is it": CatSwipe turned out to be only a config helper - it
    /// has SetupSwipe, AddSwipeables and the mass and angular settings, but no
    /// trigger - so whatever performs a cat event is a class the static probe
    /// never named. Scanning a real scene names it, once, instead of guessing
    /// class names one compile at a time.
    ///
    /// Deliberately a scene-wide scan rather than a walk of the level's own
    /// object list: a cat that lives outside allLevelObjects is exactly the
    /// case a narrower scan would miss and then report as "no cat here".
    /// </summary>
    private static void ListCats()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var levelId = li == null ? "(none)" : Str(() => li.LevelId);

        var all = UnityEngine.Object.FindObjectsOfType<Component>();
        int hits = 0;
        var seen = new System.Collections.Generic.Dictionary<string, int>();

        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;

            string type;
            try { type = c.GetIl2CppType().Name; }
            catch { continue; }

            if (type.IndexOf("Cat", StringComparison.Ordinal) < 0
                && type.IndexOf("Paw", StringComparison.Ordinal) < 0
                && type.IndexOf("Swipe", StringComparison.Ordinal) < 0) continue;

            hits++;
            seen[type] = seen.TryGetValue(type, out var n) ? n + 1 : 1;
            DevToolsPlugin.Log.LogInfo(
                $"  {type} on '{Str(() => c.gameObject.name)}'"
                + $" active={Str(() => c.gameObject.activeInHierarchy.ToString())}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"cats: level={levelId} components={hits} distinctTypes={seen.Count}");
        foreach (var kv in seen)
        {
            DevToolsPlugin.Log.LogInfo($"cats: type {kv.Key} x{kv.Value}");
        }
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
    /// Rewrites the level-select track to an arbitrary list of level indices
    /// and rebuilds it. "reorder:1,1013,1017,40,1008,..." - the question being
    /// answered is whether a randomizer can put any puzzle in any slot of the
    /// campaign's own UI, including puzzles the campaign never contains.
    /// </summary>
    private static void ReorderTrack(string csv)
    {
        if (csv.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            LevelSelectOverride.Order = null;
            DevToolsPlugin.Log.LogInfo("reorder: override cleared");
            return;
        }

        var order = new List<int>();
        foreach (var part in csv.Split(','))
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                order.Add(n);
        }
        LevelSelectOverride.Order = order;
        DevToolsPlugin.Log.LogInfo($"reorder: {order.Count} levels queued");

        // The menu object is built once and reused, so reopening it does not
        // re-run Setup. Drive the two rebuild steps directly - the patches
        // hang off them either way.
        var menu = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogInfo("reorder: no live LevelSelect, will apply when one is built");
            return;
        }
        menu.SetLevels();
        menu.SetupSections();
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track != null)
        {
            track.Init();
            track.SetInitialScrollPosition();
        }
        DumpSections();
    }

    /// <summary>
    /// A colour as rrggbb, without ColorUtility - see DumpSections.
    /// </summary>
    private static string Hex(UnityEngine.Color c)
    {
        int r = UnityEngine.Mathf.Clamp((int)(c.r * 255f + 0.5f), 0, 255);
        int g = UnityEngine.Mathf.Clamp((int)(c.g * 255f + 0.5f), 0, 255);
        int b = UnityEngine.Mathf.Clamp((int)(c.b * 255f + 0.5f), 0, 255);
        return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
    }

    /// <summary>Level-select sections, readable only while that menu is open.</summary>
    private static void DumpSections()
    {
        var menu = UnityEngine.Object.FindObjectOfType<LevelSelect>();
        if (menu == null)
        {
            DevToolsPlugin.Log.LogWarning("sections: no LevelSelect in the scene - open menu:levels first");
            return;
        }
        var sections = menu.Sections;
        DevToolsPlugin.Log.LogInfo($"LevelSelect: {Str(() => sections == null ? "0" : sections.Count.ToString())} sections,"
            + $" activeSection={Str(() => menu.ActiveSection == null ? "-" : menu.ActiveSection.SectionTitle)},"
            + $" levels in track={Str(() => menu.Levels == null ? "0" : menu.Levels.Count.ToString())}");
        for (int i = 0; i < (sections == null ? 0 : sections.Count); i++)
        {
            var s = sections![i];
            DevToolsPlugin.Log.LogInfo(
                $"  section {Str(() => s.SectionIndex.ToString())} \"{Str(() => s.SectionTitle)}\""
                + $" trackStart={Str(() => s.TrackStartIndex.ToString())}"
                + $" levels={Str(() => s.SectionLevels == null ? "0" : s.SectionLevels.Count.ToString())}"
                + $" completion={Str(() => s.CompletionInfo == null ? "-" : s.CompletionInfo.CompletionPercentageString)}"
                // The colour is what a Background Change Trap moves, and it is
                // the only way to check that without eyeballing a screenshot.
                //
                // Formatted BY HAND. ColorUtility.ToHtmlStringRGB throws
                // IndexOutOfRangeException through interop - the same trap
                // already written up in Backgrounds.Palette, walked into again
                // here, and it reads as the SECTION lookup failing rather than
                // as the formatter failing.
                + $" bg={Str(() => Hex(s.BackgroundColor))}");
        }

        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track != null)
        {
            DevToolsPlugin.Log.LogInfo(
                $"LevelsTrack: items={Str(() => track.trackItems == null ? "0" : track.trackItems.Count.ToString())}"
                + $" levelCount={Str(() => track.LevelCount.ToString())}"
                + $" unlockAll={Str(() => track.UnlockAllLevels.ToString())}"
                + $" scrollable={Str(() => track.Scrollable.ToString())}");
            var items = track.trackItems;
            for (int i = 0; i < (items == null ? 0 : items.Count); i++)
            {
                var it = items![i];
                if (it == null) continue;
                DevToolsPlugin.Log.LogInfo(
                    $"  track[{i}] {Str(() => it.level == null ? "-" : it.level.LevelId)}"
                    + $" unlocked={Str(() => it.isUnlocked.ToString())}"
                    + $" unlockable={Str(() => it.isUnlockable.ToString())}");
            }
        }
    }
}
