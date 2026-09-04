using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The title screen, while a run is on.
///
/// Two changes, both because a run is not the campaign:
///
/// - Play opens the puzzle a player is most likely to want next, which for a
///   randomized track is not "the next one along".
/// - Archive and Daily Tidy are hidden. Their puzzles are part of the run now,
///   reached through the track; entering them any other way earns no checks and
///   the screens look like a broken level select, which is exactly how the
///   Archive was first reported as a bug.
/// </summary>
internal static class TitleScreen
{
    /// <summary>
    /// Main-menu entries hidden while connected, by GameObject name prefix.
    ///
    /// Each one leads somewhere that earns no checks and looks like part of the
    /// run when it is not:
    ///
    /// - Archive and Daily Tidy: their puzzles ARE in the run, reached through
    ///   the track. The Archive is itself a level select, which is why landing
    ///   on it read as a broken track rather than a different screen.
    /// - Shuffle: endless random campaign levels. Inactive unless the save has
    ///   New Game Plus, so most players never see it - which is exactly why it
    ///   would be missed.
    /// </summary>
    private static readonly string[] HiddenButtons = { "Archive", "Daily", "Shuffle" };

    /// <summary>
    /// Whole sections hidden while connected, by GameObject name.
    ///
    /// The DLC block is hidden entire - heading, both entries and its arrows -
    /// rather than button by button, which would leave a heading over nothing.
    /// No DLC level can appear in a run: the level table is base, archive and
    /// generator only, so these lead nowhere a run can use.
    /// </summary>
    private static readonly string[] HiddenSections = { "DLC Menu" };


    /// <summary>
    /// Hide the menus a run does not use.
    ///
    /// A postfix on SetupTitleScreen for the same reason the Archipelago button
    /// is added there: that is where the game builds the menu, so anything done
    /// earlier is undone. It runs on every build, so connecting and
    /// disconnecting both settle correctly the next time the screen is shown.
    /// </summary>
    [HarmonyPatch(typeof(TitleMenu), nameof(TitleMenu.SetupTitleScreen))]
    [HarmonyPostfix]
    private static void AfterSetupTitleScreen(TitleMenu __instance) => Apply(__instance);

    /// <summary>
    /// Re-apply to the live title screen.
    ///
    /// Connecting happens AT the title screen, so SetupTitleScreen has already
    /// run by then - without this the menus stay visible until the screen is
    /// next rebuilt, which for someone connecting and pressing Play is never.
    /// </summary>
    internal static void Refresh()
    {
        try
        {
            var menu = UnityEngine.Object.FindObjectOfType<TitleMenu>();
            if (menu != null) Apply(menu);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"title: could not refresh: {e.Message}");
        }
    }

    private static void Apply(TitleMenu __instance)
    {
        try
        {
            var container = __instance.MainMenuContainer;
            if (container == null) return;

            var connected = Track.Active;

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child == null || child.GetComponent<Button>() == null) continue;
                if (!Matches(child.name, HiddenButtons)) continue;
                Show(child, !connected, connected);
            }

            // The DLC block lives in its own container, not the main menu, so
            // walking only MainMenuContainer missed it entirely.
            foreach (var section in HiddenSections)
            {
                var found = FindDeep(__instance.transform, section);
                if (found != null) Show(found, !connected, connected);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"title: could not adjust the menu: {e.Message}");
        }
    }

    private static bool Matches(string name, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Set one object's visibility, quietly if it is already right.
    ///
    /// Restoring matters as much as hiding: the mod must not permanently remove
    /// parts of someone's game, so everything comes back when not connected.
    /// </summary>
    private static void Show(Transform target, bool visible, bool connected)
    {
        if (target.gameObject.activeSelf == visible) return;

        target.gameObject.SetActive(visible);
        Plugin.Logger.LogInfo(
            $"title: {target.name} {(visible ? "restored" : "hidden for the run")}");
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null) continue;
            if (child.name == name) return child;

            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// The slot Play asked for, consumed by the next GetLevelIndex.
    /// </summary>
    private static int _playSlot = -1;

    /// <summary>
    /// Tell the gameplay state which level to load.
    ///
    /// This is the question Play's state transition asks, and answering it is
    /// what makes the game start OUR puzzle through its own path - transition,
    /// menu teardown and all - rather than a level being loaded underneath a
    /// title screen that never went away.
    ///
    /// Only answers a request Play actually made: every other route into
    /// gameplay already knows its own level.
    /// </summary>
    [HarmonyPatch(typeof(Gameplay_GameState), nameof(Gameplay_GameState.GetLevelIndex))]
    [HarmonyPostfix]
    private static void AfterGetLevelIndex(ref int __result)
    {
        try
        {
            var slot = _playSlot;
            _playSlot = -1;                       // one press, one answer
            if (!Track.Active || slot < 0) return;

            var index = Track.ArmSlot(slot);
            if (index < 0) return;

            __result = index;
            Plugin.Logger.LogInfo($"title: gameplay will load slot {slot} (level {index})");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"title: could not steer Play: {e.Message}");
        }
    }

    /// <summary>
    /// Play opens the FARTHEST puzzle that still has something doable in it.
    ///
    /// Vanilla Play resumes where the campaign left off, which in a run means
    /// nothing - and it happily landed on a puzzle already beaten. Farthest
    /// rather than first because the track only moves forwards: everything
    /// behind is either finished or waiting on an item, and the useful place to
    /// be dropped is the edge of your progress.
    ///
    /// "Something doable" means at least one location that is neither collected
    /// nor blocked by an ability or pack you do not have. A card whose only
    /// remaining checks are blocked is not somewhere to send anyone.
    /// </summary>
    [HarmonyPatch(typeof(TitleMenu), nameof(TitleMenu.PlayGame))]
    [HarmonyPrefix]
    private static bool BeforePlayGame()
    {
        try
        {
            if (!Track.Active) return true;         // vanilla behaviour

            _playSlot = -1;
            var slot = Track.FarthestPlayableSlot();
            if (slot < 0)
            {
                // Nothing doable anywhere: show them the track rather than
                // dropping them into a puzzle they cannot progress.
                Plugin.Logger.LogInfo("title: nothing playable, opening the track");
                Toasts.Show("Nothing to play yet - waiting on items", Toasts.Notice);
                GameManager.Instance.SetGameState<Levels_GameState>(null, false);
                return false;
            }

            // Let the game's own Play run, and answer the question it asks.
            //
            // Calling StartLevel directly loaded a level and left the title
            // screen up - Play appeared to do nothing, four times over, while
            // the log happily reported a launch. PlayGame does the state
            // transition; the level it loads comes from
            // Gameplay_GameState.GetLevelIndex, which is answered below.
            _playSlot = slot;
            Plugin.Logger.LogInfo($"title: Play -> slot {slot}, letting the game start it");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"title: Play failed, using the game's own: {e.Message}");
            return true;
        }
    }
}
