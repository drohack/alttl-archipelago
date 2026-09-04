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
    /// <summary>Counts redirects, so a test can assert the patch FIRED.</summary>
    internal static int Redirects { get; private set; }

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
    /// Send an exit to the run's track, using the game's own routine.
    ///
    /// Two other approaches were tried against the running game and neither
    /// worked, so this is not a first guess:
    ///
    /// - patching the navigation calls. The exit buttons reach the generic
    ///   SetGameState&lt;T&gt;, which cannot be patched under IL2CPP; the
    ///   non-generic overload and Back were never called at all.
    /// - making the game route itself via LevelInterface.IsArchived. Tried
    ///   three ways, each verified in game: patching the getter for the exit
    ///   only; holding that patch for the whole level lifetime, in case the
    ///   destination is cached at level start; and finally SETTING the property
    ///   - it has a setter - on the live interface, confirmed by a probe
    ///   reporting archived=False while the level ran. All three still landed
    ///   on the Archive. The routing does not depend on that flag.
    /// - filing the run's completion data in the CAMPAIGN list instead of the
    ///   archive one, in case the routing followed the save rather than the
    ///   level. Also landed on the Archive.
    ///
    /// Four disproved hypotheses, each checked in the running game. Whatever
    /// picks the destination is not the level's flags and not its save list, so
    /// taking the button over is not a shortcut around a tidier fix - it is the
    /// fix.
    ///
    /// What is left is to take over the button and call GoToLevelSelectForLevel
    /// ourselves - the game's OWN routine, which does the teardown, the
    /// transition and the menu setup - handing it a campaign level so it picks
    /// the campaign track. The only thing we supply is which track we want.
    ///
    /// This is exhaustive rather than piecemeal: exactly three classes in the
    /// game have a LevelSelect method - MainMenu, ReplayMenu and TitleMenu -
    /// and TitleMenu already goes to the campaign track.
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
            Redirects++;
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

    /// <summary>
    /// Catch the exit routes, not just the question.
    ///
    /// ContextualState is what the game ASKS, and patching it was not enough:
    /// leaving a puzzle through the pause menu still landed on the Archive,
    /// because that path sets the state directly. Both entry points are covered
    /// here, and both log, so "it still goes to the wrong place" is answerable
    /// from the log instead of by argument.
    /// </summary>
    /// <summary>
    /// A trace of every state change while a run is on.
    ///
    /// Kept because guessing which call an exit button takes cost several
    /// rounds of "still broken": the pause menu logs its own button and then no
    /// SetGameState and no Back at all, because it uses the GENERIC
    /// SetGameState<T>. That is the kind of thing worth being able to see
    /// rather than reason about.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetGameState),
        new[] { typeof(Il2CppSystem.Type), typeof(GameStateData), typeof(bool) })]
    [HarmonyPrefix]
    private static void BeforeSetGameState(Il2CppSystem.Type t)
    {
        if (Track.Active) Plugin.Logger.LogInfo($"navigation: SetGameState({t?.Name})");
    }
}
