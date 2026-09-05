using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Where the game takes you when a puzzle ends.
///
/// The game answers that CONTEXTUALLY, from where the level came from: finish
/// an archive puzzle and you go back to the Archive, finish a daily one and you
/// go to Daily Tidy. That is exactly right in vanilla and exactly wrong here,
/// because a run is drawn from all three sources at once. In play it meant
/// beating a puzzle dropped you on the Archive page - which looks like a broken
/// level select, with no chapters and the wrong levels on it, and reads as a
/// bug in the track rather than as a different screen.
///
/// While a run is on, every puzzle belongs to the run, so every puzzle returns
/// to the run's track.
/// </summary>
internal static class Navigation
{


    private static LevelInterface? _home;

    /// <summary>
    /// Any ordinary campaign level, used only as the level we claim to be
    /// leaving so the game routes us to the campaign track.
    /// </summary>
    private static LevelInterface? CampaignLevel(LevelManager? manager)
    {
        if (_home != null) return _home;
        if (manager == null) return null;

        try
        {
            var all = manager.LevelInterfaces;
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var level = all![i];
                if (level == null || level.IsArchived || level.IsCredits) continue;
                if (level.LevelType == LevelType.Chapter) continue;
                _home = level;
                break;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: no campaign level found: {e.Message}");
        }
        return _home;
    }

    /// <summary>
    /// Put Levels and Skip back into the pause menu.
    ///
    /// The game hides both on daily-tidy levels, which is right in vanilla -
    /// a daily has no campaign track to return to and cannot be skipped. But a
    /// run draws generator puzzles from exactly that pool, so opening one left
    /// the pause menu with no way back to the track at all: Resume, Hint,
    /// Settings, Reset, Exit. The only escape was quitting to the title.
    ///
    /// A postfix, so the game makes its decision first and this restores the
    /// two entries a run needs. Skip comes back too - whether a skip is
    /// actually allowed is decided by holding a Skip item, not by which pool
    /// the puzzle came from.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.ShowHideMenuItems))]
    [HarmonyPostfix]
    private static void AfterShowHideMenuItems(MainMenu __instance)
    {
        try
        {
            var container = __instance.ButtonsContainer;
            if (container == null) return;

            // No run: put back anything a previous run wrote and stop. NOT an
            // early return before touching the menu - the counts live on a
            // persistent label, so leaving without clearing them strands a
            // stray "0" beside Let It Be in an ordinary game.
            if (!Track.Active)
            {
                for (int i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child != null) Restore(child);
                }
                _skipEntry = null;
                _hintEntry = null;
                return;
            }

            // Re-applied on every open, which is both the natural refresh and
            // a requirement: the menu is rebuilt, so anything painted outside
            // this moment is lost.
            TintMenuBackground(__instance);

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child == null) continue;

                var isSkip = child.name.StartsWith("Skip", StringComparison.Ordinal);
                var isHint = child.name.StartsWith("Hint", StringComparison.Ordinal);

                // Both entries carry a count, whether or not they needed
                // restoring - the Hint entry is never hidden, so tagging it
                // inside the restore branch below would never run.
                //
                // Remembered as well as written, so TickMenuCounts can put the
                // count back each frame without walking the menu again.
                if (isSkip) { _skipEntry = child; Annotate(child, $"{Skips.Available}"); }
                else if (isHint) { _hintEntry = child; Annotate(child, HintTag()); }

                var wanted = child.name.StartsWith("Levels", StringComparison.Ordinal)
                             || isSkip;
                if (!wanted || child.gameObject.activeSelf) continue;

                child.gameObject.SetActive(true);
                Plugin.Logger.LogInfo($"navigation: restored {child.name} to the pause menu");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not fix the pause menu: {e.Message}");
        }
    }

    /// <summary>
    /// Recolour the pause screen, if the player has been sent a Menu
    /// Background.
    ///
    /// Two things about finding it are not obvious, and both were got wrong
    /// first:
    ///
    /// It is NOT a sibling of the buttons. The pause menu is laid out as
    /// Main Menu/Buttons beside Main Menu/Theme/&lt;theme&gt;/Background, so
    /// walking up from ButtonsContainer and checking each ancestor's direct
    /// children never reaches it.
    ///
    /// And there is more than one. Each theme - MenuCampaign, MenuDLC1 and
    /// friends - carries its own full-screen Background, so a search that
    /// includes inactive objects finds three and picks by hierarchy order,
    /// which is to say by luck. Asking only for objects live in the hierarchy
    /// leaves exactly the theme the player is looking at.
    ///
    /// Deliberately NOT written to MenuTheme.backgroundColor, which would be
    /// the obvious place: a MenuTheme is a shared asset, so editing it leaks
    /// the colour into every other menu for the rest of the session and there
    /// is nothing to put it back.
    /// </summary>
    private static void TintMenuBackground(MainMenu menu)
    {
        try
        {
            var wanted = Backgrounds.ForMenu();
            if (wanted == null) return;

            // false: active in the hierarchy only. This is the discriminator,
            // not a tidy-up.
            foreach (var image in menu.GetComponentsInChildren<UnityEngine.UI.Image>(false))
            {
                if (image == null || image.gameObject == null) continue;
                if (!string.Equals(image.gameObject.name, "Background",
                                   StringComparison.Ordinal)) continue;

                // Alpha is preserved: the pause screen sits over the puzzle,
                // and forcing it opaque would hide the thing the player paused
                // to think about.
                var colour = wanted.Value;
                colour.a = image.color.a;
                image.color = colour;
                return;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: menu recolour failed: {e.Message}");
        }
    }


    /// <summary>
    /// What to put beside the Hint entry: how many are held, or a plain note
    /// that this puzzle has no hint to uncover in the first place.
    ///
    /// Saying "no hint" matters more than it looks. Six of the game's levels
    /// have an empty notepad, and without this the player opens it, finds
    /// nothing, and cannot tell whether the puzzle has no hint or whether the
    /// mod ate it.
    /// </summary>
    private static string HintTag()
    {
        try
        {
            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            var pages = li?.HintImages == null ? 0 : li.HintImages.Count;
            if (pages == 0) return "no hint";
        }
        catch
        {
            // Fall through to the count; a wrong number reads better than a
            // pause menu that throws.
        }
        return $"{Hints.Available}";
    }


    /// <summary>
    /// Write a small tag in front of a menu entry's own label.
    ///
    /// A sibling text object is NOT the way to do this, and that is settled
    /// by having tried: ConnectionPane records two attempts that both lost to
    /// the menu's layout, because the entries are right-aligned inside a rect
    /// wider than the word. Prefixing the label the menu already positions
    /// sidesteps the whole problem.
    ///
    /// Rewriting the text means killing the localiser first, or the next
    /// refresh puts the stock string back. Note the entry named "Skip..."
    /// actually READS "Let It Be" - matching on the object name rather than
    /// the caption is deliberate.
    /// </summary>
    private static void Annotate(Transform entry, string tag)
    {
        foreach (var label in entry.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null) continue;
            label.text = $"{Opener}{tag}{Closer}{Caption(label)}";
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }
    }


    /// <summary>
    /// Keep the counts on screen while the pause menu is open.
    ///
    /// Writing them once in the ShowHideMenuItems postfix is not enough, and
    /// this is the second time that lesson has been learned on this menu. The
    /// entries carry a LocalizeStringEvent which refreshes AFTER the postfix
    /// runs and rewrites the label with the plain caption, so the count
    /// appeared for a frame and then vanished - the probe showed "Hint" and
    /// "Let It Be" with no tag at all.
    ///
    /// ConnectionPane's answer was to destroy the localiser
    /// (ConnectionPane.cs:170-183). That works and is wrong here: its label is
    /// written once and never revisited, whereas these two are permanent
    /// objects under Menus/Main Menu, so destroying their localiser would
    /// freeze both entries in whatever language was loaded at the time, for
    /// the rest of the session, including after the run ends.
    ///
    /// Re-applying instead lets the localiser win a frame and win it back the
    /// next, which also means a language change is picked up rather than
    /// fought: Caption() re-reads the entry whenever it does not find our own
    /// markup.
    ///
    /// Only runs while a run is on AND the menu is actually open, and only
    /// touches two cached transforms - no scene search, nothing while playing.
    /// </summary>
    internal static void TickMenuCounts()
    {
        try
        {
            if (!Track.Active) return;
            if (_skipEntry == null && _hintEntry == null) return;

            if (_skipEntry != null && _skipEntry.gameObject.activeInHierarchy)
                Annotate(_skipEntry, $"{Skips.Available}");

            if (_hintEntry != null && _hintEntry.gameObject.activeInHierarchy)
                Annotate(_hintEntry, HintTag());
        }
        catch
        {
            // On the frame path. A warning per frame would bury the log, and
            // the menu is rebuilt often enough to recover on its own.
            _skipEntry = null;
            _hintEntry = null;
        }
    }

    private static Transform? _skipEntry;
    private static Transform? _hintEntry;


    /// <summary>
    /// Put a menu entry back the way the game wrote it.
    ///
    /// Needed because the tag is not transient. The label is a persistent
    /// object under Menus/Main Menu rather than something rebuilt per open, so
    /// a count written during a run stayed on the entry afterwards - a player
    /// who left the run found a stray "0" beside Let It Be in an ordinary
    /// game, with nothing to remove it.
    /// </summary>
    private static void Restore(Transform entry)
    {
        foreach (var label in entry.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null) continue;
            if (!_captions.TryGetValue(label.GetInstanceID(), out var original)) continue;
            label.text = original;
        }
    }


    /// <summary>
    /// The entry's own caption, without any tag of ours.
    ///
    /// Remembered on first sight rather than parsed back out of the label each
    /// time. Parsing worked, but it meant the caption only survived as long as
    /// our own markup stayed well formed, and there was nothing to restore
    /// from once a run ended.
    ///
    /// The localiser is deliberately NOT destroyed here, which is where this
    /// started. ConnectionPane destroys it (ConnectionPane.cs:170-183) because
    /// its label is written once and never revisited; this one is rewritten on
    /// every menu open, so it can simply lose a race with the localiser and
    /// win it again a moment later. Destroying it would freeze these two
    /// entries in whatever language the player happened to be using.
    /// </summary>
    private static string Caption(TextMeshProUGUI label)
    {
        var id = label.GetInstanceID();
        var text = label.text ?? "";

        // Our own markup means the localiser has not refreshed since we last
        // wrote; the remembered caption is the real one.
        if (text.StartsWith(Opener, StringComparison.Ordinal))
        {
            if (_captions.TryGetValue(id, out var known)) return known;

            // No memory of it - recover the caption from our own markup rather
            // than baking the tag in permanently.
            var cut = text.IndexOf(Closer, StringComparison.Ordinal);
            text = cut >= 0 ? text.Substring(cut + Closer.Length) : text;
        }

        _captions[id] = text;
        return text;
    }

    private static readonly System.Collections.Generic.Dictionary<int, string>
        _captions = new();


    /// <summary>
    /// The wrapper our tag is written in. Smaller and dimmer than the entry
    /// itself so it reads as a note about the action rather than part of its
    /// name, matching ConnectionPane's menu indicator.
    ///
    /// The voffset is what centres it. Inline text of a smaller size shares
    /// the big text's BASELINE, so a 55% tag beside a full-size caption sits
    /// low and reads as a subscript. Raising it by roughly the difference
    /// between the two cap-height centres puts it on the caption's optical
    /// middle. It is outside the size tag deliberately: em there means the
    /// entry's own font size, so the shift stays right whatever size the menu
    /// is drawn at.
    /// </summary>
    private const string Opener = "<voffset=0.18em><size=55%><color=#FFFFFF80>";

    /// <summary>Closes the tag and separates it from the game's caption.</summary>
    private const string Closer = "</color></size></voffset>  ";


    /// <summary>
    /// Send Continue to the run's next puzzle, launching it ourselves.
    ///
    /// Answering GetNextLevelIndex is not enough, and the reason is the bug
    /// this fixes. The index we hand back is correct; the game then routes by
    /// the level's KIND, and a daily-pool level (995-1000, isDailyTidy) is
    /// routed to the Daily Tidy page rather than loaded into the run. So
    /// finishing a puzzle whose next slot happened to be one of those dropped
    /// the player out of their run entirely. It looked intermittent because it
    /// depends on what the next slot is.
    ///
    /// Launching it ourselves is the same thing clicking the card does, and
    /// that path has never had this problem: StartLevel loads a level rather
    /// than asking where a level of that kind belongs. ArmSlot sets the pending
    /// slot for this frame, so the seed and forceReload are filled in by
    /// Track.BeforeStartLevel exactly as they are for a click.
    ///
    /// The state is set BEFORE the load. Skipping that is what once left a
    /// level running underneath a title screen that never went away - and here
    /// the screen in question is the post-level retry UI.
    /// </summary>
    private static bool GoToNext(string which)
    {
        try
        {
            if (!Track.Active) return true;              // not a run; vanilla

            var next = Track.NextUnfinishedSlot();
            if (next < 0)
            {
                // Nothing left to play. The game's own answer is as good as
                // ours, and better than inventing one.
                Plugin.Logger.LogInfo($"navigation: {which}, nothing unfinished left");
                return true;
            }

            var gm = GameManager.Instance;
            var manager = gm?.levelManager;
            if (gm == null || manager == null) return true;

            var index = Track.ArmSlot(next);
            if (index < 0) return true;

            Plugin.Logger.LogInfo(
                $"navigation: {which} -> slot {next} (level {index}), launching it");

            gm.SetGameState<Gameplay_GameState>(null, false);
            manager.StartLevel(index, true, true, -1);
            return false;
        }
        catch (Exception e)
        {
            // Fail open: the game's own Continue is better than none.
            Plugin.Logger.LogWarning($"navigation: {which} failed, using the game's: {e.Message}");
            return true;
        }
    }

    [HarmonyPatch(typeof(RetryMenu), nameof(RetryMenu.NextLevel))]
    [HarmonyPrefix]
    private static bool BeforeRetryNext() => GoToNext("post-level Continue");

    [HarmonyPatch(typeof(ReplayMenu), nameof(ReplayMenu.NextLevel))]
    [HarmonyPrefix]
    private static bool BeforeReplayNext() => GoToNext("replay Next");

    /// <summary>
    /// Send an exit to the run's track, using the game's own routine.
    ///
    /// Taking the button over is the fix, not a shortcut around a tidier one.
    /// FOUR tidier ones were tried against the running game and all four
    /// failed: patching the navigation calls, and three separate ways of making
    /// the game route itself off LevelInterface.IsArchived, plus filing the
    /// run's completion data in the campaign list. Every one still landed on
    /// the Archive. Whatever picks the destination is not the level's flags and
    /// not its save list. The detail is in docs/verification-log.md - read it
    /// before replacing this with something that looks cleaner.
    ///
    /// What works is calling GoToLevelSelectForLevel ourselves - the game's OWN
    /// routine, which does the teardown, the transition and the menu setup -
    /// handing it a campaign level so it picks the campaign track. The only
    /// thing we supply is which track we want.
    ///
    /// Exhaustive rather than piecemeal: exactly three classes in the game have
    /// a LevelSelect method - MainMenu, ReplayMenu and TitleMenu - and
    /// TitleMenu already goes to the campaign track.
    /// </summary>
    private static bool GoToTrack(string which)
    {
        try
        {
            if (!Track.Active) return true;              // vanilla behaviour

            var manager = GameManager.Instance?.levelManager;
            var home = CampaignLevel(manager);
            if (manager == null || home == null) return true;

            Plugin.Logger.LogInfo($"navigation: {which} -> the run's track");
            manager.GoToLevelSelectForLevel(home);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not reach the track: {e.Message}");
            return true;
        }
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.LevelSelect))]
    [HarmonyPrefix]
    private static bool BeforePauseLevelSelect() => GoToTrack("pause menu");

    [HarmonyPatch(typeof(ReplayMenu), nameof(ReplayMenu.LevelSelect))]
    [HarmonyPrefix]
    private static bool BeforeReplayLevelSelect() => GoToTrack("post-level menu");

    /// <summary>
    /// Make "next" mean the next puzzle in the RUN.
    ///
    /// The arrow asks the level manager for the next index, which in vanilla is
    /// the next campaign level - meaningless here, and it was sending players
    /// at levels outside the run. Answering with the next OPEN, unfinished slot
    /// makes the arrow follow the track a player is actually playing.
    ///
    /// It also arms the pending slot, so the launch gets its baked seed and a
    /// forced reload. Without that a generator level reached through the arrow
    /// comes up empty, exactly as it did from the level select.
    /// </summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.GetNextLevelIndex))]
    [HarmonyPostfix]
    private static void AfterGetNextLevelIndex(ref int __result)
    {
        try
        {
            if (!Track.Active) return;

            var next = Track.NextUnfinishedSlot();
            if (next < 0) return;                  // nothing left; leave it alone

            __result = Track.ArmSlot(next);
            Plugin.Logger.LogInfo($"navigation: next -> slot {next} (level {__result})");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not pick the next level: {e.Message}");
        }
    }
}
