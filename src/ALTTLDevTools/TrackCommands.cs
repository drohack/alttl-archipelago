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
/// The level-select track: clicking, hovering and scrolling to a card, what
/// each card's badge and icon are reading, its sections, the credits card and
/// the skip prompt. Positions here are TRACK positions (dividers and the
/// credits card count); only clickcard: takes a level index.
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
    /// need to reference it. THE MOD ONLY READS THAT FILE WITH [Diagnostics]
    /// BadgeWhyProbe = true in its config; otherwise this writes a request
    /// nobody answers.
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

        // The LIVE, populated track: FindLiveTrack says why both halves
        // matter. A silent no-op is far worse than a warning.
        var track = FindLiveTrack(out var best, out var bestLive);

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

        var track = FindLiveTrack(out _, out var live);
        if (track == null || !live)
        {
            DevToolsPlugin.Log.LogWarning("focus: the track is not up yet - open menu:levels first");
            return;
        }
        var items = track.trackItems;
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
    /// "scrolltrack:N" scrolls the level select to the Nth card and reports
    /// how the icon is drawn (the game's unlocked flag and which art is on).
    /// Written to look at cards in packs not yet opened (backlog item 8:
    /// filled icons there) without a mouse to scroll with.
    /// </summary>
    private static void ScrollTrack(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            DevToolsPlugin.Log.LogWarning($"scrolltrack: not a number: {arg}");
            return;
        }

        var track = FindLiveTrack(out _, out var live);
        if (track == null || !live)
        {
            DevToolsPlugin.Log.LogWarning("scrolltrack: the track is not up yet - open menu:levels first");
            return;
        }
        var items = track.trackItems;
        if (items == null || index < 0 || index >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning("scrolltrack: no such card");
            return;
        }

        track.SetScrollToItem(index);
        var icon = items[index];
        DevToolsPlugin.Log.LogInfo(
            $"scrolltrack: card {index} is {Str(() => icon.level.LevelId)}"
            + $" unlocked={Str(() => icon.isUnlocked.ToString())}"
            + $" defaultArt={Str(() => icon.defaultLevelIcon.activeSelf.ToString())}"
            + $" unlockedArt={Str(() => icon.unlockedLevelIcon.activeSelf.ToString())}"
            + $" current={Str(() => icon.currentIcon.name)}");
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

        var track = FindLiveTrack(out _, out var live);
        if (track != null)
        {
            DevToolsPlugin.Log.LogInfo(
                $"LevelsTrack ({(live ? "live" : "DEAD - not on screen")}):"
                + $" items={Str(() => track.trackItems == null ? "0" : track.trackItems.Count.ToString())}"
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

    /// <summary>
    /// "iconinfo:N" dumps one icon's child tree with the components and sizes
    /// on each node. Guessing at a hierarchy wastes a build-and-launch cycle;
    /// reading it costs one command. This is what showed that LevelIcon has two
    /// whole presentations and swaps which is active.
    /// </summary>
    private static void IconInfo(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            DevToolsPlugin.Log.LogWarning($"iconinfo: not a track position: {arg}");
            return;
        }

        // SAY SO BEFORE TOUCHING ANYTHING. This call hung the game once, with
        // "command: iconinfo:84" in the log and then nothing at all - not the
        // out-of-range warning it should have printed instantly, not an
        // exception, no further frames. With no output between the dispatch
        // and the first Unity call there was no way to tell which of them had
        // stopped, and the only diagnosis available was a guess.
        //
        // The index is also checked against nothing-in-particular first, so a
        // plainly silly number is rejected without a scene search. 84 was a
        // LEVEL index handed to a function that wants a TRACK position; the
        // track had ten items.
        DevToolsPlugin.Log.LogInfo($"iconinfo: looking up track position {index}");

        if (index < 0 || index > 512)
        {
            DevToolsPlugin.Log.LogWarning(
                $"iconinfo: {index} is not a track position - this takes the "
                + "card's place on the track, not a level index");
            return;
        }

        var track = FindLiveTrack(out _, out var live);
        if (track == null || !live)
        {
            DevToolsPlugin.Log.LogWarning("iconinfo: open menu:levels first");
            return;
        }

        var items = track.trackItems;
        if (items == null || index < 0 || index >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning(
                $"iconinfo: index {index} out of range (0..{(items?.Count ?? 0) - 1})");
            return;
        }

        var icon = items[index];
        if (icon == null) { DevToolsPlugin.Log.LogWarning("iconinfo: null icon"); return; }

        DevToolsPlugin.Log.LogInfo($"iconinfo: index {index} level={Str(() => icon.level?.LevelId)}");
        DumpIcon(icon.transform, 0);
    }

    private static void DumpIcon(Transform t, int depth)
    {
        if (t == null || depth > 7) return;

        var pad = new string(' ', depth * 2);
        var rt = t.TryCast<RectTransform>();
        var size = rt != null ? $" size={rt.rect.width:0}x{rt.rect.height:0}" : "";

        var components = "";
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            var name = Str(() => c.GetIl2CppType().Name);
            if (name == "RectTransform" || name == "Transform") continue;
            components += (components.Length > 0 ? "," : "") + name;
        }

        // Sprite name and colour on any Image, so the right art can be picked
        // by reading rather than by guessing and rebuilding.
        var img = t.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
        {
            components += $" sprite={Str(() => img.sprite?.name)}"
                + $" colour={Str(() => $"{img.color.r:0.00},{img.color.g:0.00},{img.color.b:0.00},{img.color.a:0.00}")}";
        }

        // A card's solution stars are Toggles (LevelIcon.SetCompletionStars
        // sets them from the save row), so their state is the star's state.
        var toggle = t.GetComponent<UnityEngine.UI.Toggle>();
        if (toggle != null) components += $" isOn={Str(() => toggle.isOn.ToString())}";

        DevToolsPlugin.Log.LogInfo(
            $"  {pad}{t.gameObject.name}{size} active={t.gameObject.activeSelf}"
            + $" sibling={t.GetSiblingIndex()}"
            + (components.Length > 0 ? $" [{components}]" : ""));

        for (int i = 0; i < t.childCount; i++) DumpIcon(t.GetChild(i), depth + 1);
    }

    /// <summary>
    /// "clickcard:N" invokes LevelIcon.DoStartLevel on the icon for level
    /// INDEX N - the exact method a real click funnels into - without
    /// synthetic mouse input. clicktrack: takes a track position instead.
    /// </summary>
    private static void ClickCard(string arg)
    {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            DevToolsPlugin.Log.LogWarning($"clickcard: not a level index: {arg}");
            return;
        }
        // The live track; "open menu:levels first" is what release_e2e retries on.
        var track = FindLiveTrack(out _, out var live);
        if (track == null || !live) { DevToolsPlugin.Log.LogWarning("clickcard: open menu:levels first"); return; }

        var items = track.trackItems;
        for (int i = 0; i < (items == null ? 0 : items.Count); i++)
        {
            var icon = items![i];
            if (icon == null || icon.level == null) continue;
            if (icon.level.LevelIndex != n) continue;
            DevToolsPlugin.Log.LogInfo($"clickcard: invoking DoStartLevel on {icon.level.LevelId}");
            icon.DoStartLevel();
            DevToolsPlugin.Log.LogInfo("clickcard: returned");
            return;
        }
        DevToolsPlugin.Log.LogWarning($"clickcard: no icon on the track for level {n}");
    }

    /// <summary>
    /// "starcalls": how many times the game's own card-star methods ran since
    /// the last call, then zero the counts (StarTrace).
    /// </summary>
    private static void ReportStarCalls()
        => DevToolsPlugin.Log.LogInfo($"starcalls: {StarTrace.TakeCounts()}");

    /// <summary>
    /// Force the level-select skip prompt on screen, and say what it reads.
    ///
    /// Written to settle a question that a hierarchy scan could not: a label
    /// at Menus/Level Select/Levels Track/Skip Tooltip reads "Skipppable" in
    /// the object tree, but it has a localiser and had never been activated,
    /// so that string may be nothing more than the placeholder baked into the
    /// prefab. What a player actually sees is only knowable by showing it.
    /// </summary>
    private static void ShowSkipTooltip()
    {
        var found = 0;
        foreach (var obj in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelsTrack>()))
        {
            var track = obj == null ? null : obj.TryCast<LevelsTrack>();
            if (track == null || track.gameObject == null) continue;
            if (!track.gameObject.activeInHierarchy) continue;

            var tip = track.skipTooltip;
            if (tip == null)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"skiptip: {PathOf(track.transform)} has no skipTooltip");
                continue;
            }

            found++;
            DevToolsPlugin.Log.LogInfo(
                $"skiptip: {PathOf(tip.transform)}"
                + $" showing={Str(() => tip.Showing.ToString())}"
                + $" expire={Str(() => tip.skipExpireTime.ToString())}"
                + $" before={Str(() => tip.skipText.text)}");

            tip.Show(true);
            tip.StartSkipTooltip();

            DevToolsPlugin.Log.LogInfo(
                $"skiptip: shown, now reads {Str(() => tip.skipText.text)}"
                + $" live={tip.gameObject.activeInHierarchy}");
        }

        if (found == 0)
        {
            DevToolsPlugin.Log.LogWarning(
                "skiptip: no active LevelsTrack - open the level select first");
        }
    }

    /// <summary>
    /// What state the credits card is actually in on the level select.
    ///
    /// droha: "when i went back to the level select i see a level with a hand
    /// print as the icon. it's greyed out like I can't play it. I think it's
    /// the credits but I can't tell."
    ///
    /// The suspicion to test is that Track.ApplyUnlocks creates completion
    /// data for the chapter dividers and for the run's open slots, and the
    /// credits card is neither - it is appended to the track separately, after
    /// the loop, so nothing ever sets unlockedOnLevelSelect on it. A card with
    /// no completion row draws locked. This reads the three things that would
    /// settle it rather than inferring from a screenshot.
    /// </summary>
    private static void ReportCreditsCard()
    {
        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no LevelManager");
            return;
        }

        LevelInterface? credits = null;
        var all = manager.m_allLevelInterfaces;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var li = all[i];
                if (li == null) continue;
                var isCredits = false;
                try { isCredits = li.IsCredits; } catch { continue; }
                if (isCredits) { credits = li; break; }
            }
        }

        if (credits == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no credits level found");
            return;
        }

        var has = Str(() =>
            SaveSystem.data.LevelHasCompletionData(credits).ToString());
        var flag = "-";
        try
        {
            if (SaveSystem.data.LevelHasCompletionData(credits))
            {
                var entry = SaveSystem.data.GetLevelCompletionData(credits);
                flag = entry == null
                    ? "no entry" : entry.unlockedOnLevelSelect.ToString();
            }
        }
        catch (Exception e) { flag = "threw: " + e.Message; }

        // The card's OWN art, by name. The mod picks no icon for this card -
        // it puts the game's credits level on the track and the LevelIcon
        // draws whatever that level carries - so naming the sprite settles
        // whether the hand print is authored for the credits or something we
        // caused. droha: "is that for the credits, or you just picked it?"
        var locked = Str(() => credits.LockedIcon == null
            ? "none" : credits.LockedIcon.name);
        var unlocked = Str(() => credits.UnlockedIcon == null
            ? "none" : credits.UnlockedIcon.name);

        DevToolsPlugin.Log.LogInfo(
            $"creditscard: lockedIcon={locked} unlockedIcon={unlocked}");

        DevToolsPlugin.Log.LogInfo(
            "creditscard: id=" + Str(() => credits.LevelId)
            + " index=" + Str(() => credits.LevelIndex.ToString())
            + " isUnlocked=" + Str(() => credits.IsUnlocked.ToString())
            + " hasCompletionData=" + has
            + " unlockedOnLevelSelect=" + flag);
    }
}
