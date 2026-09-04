using System;
using HarmonyLib;

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
            if (!Track.Active) return;

            var container = __instance.ButtonsContainer;
            if (container == null) return;

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child == null) continue;

                var wanted = child.name.StartsWith("Levels", StringComparison.Ordinal)
                             || child.name.StartsWith("Skip", StringComparison.Ordinal);
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
