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

    /// <summary>Forget the cached home level. Called when a run ends.</summary>
    internal static void Reset() => _home = null;

    /// <summary>
    /// Any ordinary campaign level, used only as the level we claim to be
    /// leaving so the game routes us to the campaign track.
    ///
    /// PREFERABLY ONE THE RUN DOES NOT CONTAIN. This used to take the first
    /// campaign level it found - index 1, Cat Frame - and that was safe only
    /// by accident: campaign levels were almost never in a seed, because 57 of
    /// the 69 could not be drawn at all. Now that the campaign is a rollable
    /// source, the first one found is quite often one of the run's own cards.
    ///
    /// That matters because this level is handed to GoToLevelSelectForLevel on
    /// every pause-menu Levels press, which can leave it as the active level
    /// interface. A later StartLevel that arrives with no index of its own
    /// resolves through ActiveLevelInterface (Track.BeforeStartLevel), so it
    /// would resolve to THIS level's slot and file its checks there - a check
    /// credited to a puzzle the player never opened.
    ///
    /// A level outside the run cannot be mistaken for a slot, so prefer one.
    /// Falling back to any campaign level if the run somehow holds them all is
    /// fine: the old behaviour is still better than no route to the track.
    /// </summary>
    private static LevelInterface? CampaignLevel(LevelManager? manager)
    {
        if (_home != null) return _home;
        if (manager == null) return null;

        try
        {
            LevelInterface? fallback = null;
            var all = manager.LevelInterfaces;
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var level = all![i];
                if (level == null || level.IsArchived || level.IsCredits) continue;
                if (level.LevelType == LevelType.Chapter) continue;

                fallback ??= level;
                if (Track.SlotForLevelIndex(level.LevelIndex) >= 0) continue;

                _home = level;
                break;
            }
            _home ??= fallback;
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

                // Hint is restored too. The game hides it while a level is
                // still loading, so opening the pause menu early - which is
                // exactly what someone does when they already know the puzzle
                // - showed a menu with no Hint entry at all, and it only
                // appeared after resuming and pausing again. On a puzzle with
                // no hint the entry now reads "no hint" rather than being
                // absent, which is a better answer than silence either way.
                var wanted = child.name.StartsWith("Levels", StringComparison.Ordinal)
                             || isSkip || isHint;
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
        // Hints.PagesHere, not LevelInterface.HintImages. The two disagree on
        // any level with a randomizer - Pencils reports no images and still
        // has two pages - and reading the wrong one told the player a puzzle
        // had no hint while its notepad opened a real one.
        if (Hints.PagesHere() == 0) return "no hint";
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
    /// Let the GAME advance to the next puzzle.
    ///
    /// These used to call GoToNext, which launched the level itself with
    /// StartLevel. That is why a playtester saw the puzzle they had just
    /// finished still sitting behind the new one: StartLevel with forceReload
    /// does NOT release the level being left, and nothing else did either.
    /// DevTools has known this for a while and says so in its own boot
    /// command - "forceReload alone leaves the previous level ALIVE, and its
    /// listeners stay subscribed" - which is also why one arrow press used to
    /// poison a whole test session and why the e2e gives the arrow a throwaway
    /// game of its own.
    ///
    /// GoToNext existed because vanilla routes by the level's KIND and a
    /// daily-pool level (995-1000) went to the Daily Tidy page instead of the
    /// run. DailyGuard now answers false to
    /// LevelInterface.ReactToDailyCompleting while a run is active, so that
    /// reason is gone - the same obsolete workaround that was removed from
    /// ReplayMenu.LevelSelect for the same reason on the same day.
    ///
    /// What still has to be ours is WHICH level is next: AfterGetNextLevelIndex
    /// answers the game's own question with the run's next open slot. The game
    /// then does the transition, and releases what it is leaving, because that
    /// is its job and it is better at it.
    /// </summary>
    [HarmonyPatch(typeof(RetryMenu), nameof(RetryMenu.NextLevel))]
    [HarmonyPrefix]
    private static bool BeforeRetryNext() => LetTheGameAdvance("post-level Continue");

    [HarmonyPatch(typeof(ReplayMenu), nameof(ReplayMenu.NextLevel))]
    [HarmonyPrefix]
    private static bool BeforeReplayNext() => LetTheGameAdvance("replay Next");

    private static bool LetTheGameAdvance(string which)
    {
        if (!Track.Active) return true;
        Plugin.Logger.LogInfo($"navigation: {which}, letting the game advance");
        return true;
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

    /// <summary>
    /// Let the GAME open the level select after a puzzle.
    ///
    /// This used to redirect to GoToTrack, because vanilla routes by the
    /// level's KIND and a daily-pool level sent the player to the Daily Tidy
    /// page instead of the run. DailyGuard now answers false to
    /// LevelInterface.ReactToDailyCompleting while a run is active, so that
    /// reason is gone - the game's own routing lands on the campaign track,
    /// which is what the run has replaced.
    ///
    /// Why it matters that the game opens it: GoToLevelSelectForLevel produces
    /// a level select WITHOUT its own Close button - measured, "no active
    /// control named Close Button" on every attempt - and the same
    /// half-opened menu is what MenuManager.TransitionMenuOut then cannot tear
    /// down, which is the NullReferenceException this harness has been logging
    /// all along. One cause, both symptoms.
    ///
    /// The pause menu's LevelSelect keeps its redirect: it fires mid-level,
    /// where there is no completion to route from and the kind-based routing
    /// never applied.
    /// </summary>
    [HarmonyPatch(typeof(ReplayMenu), nameof(ReplayMenu.LevelSelect))]
    [HarmonyPrefix]
    private static bool BeforeReplayLevelSelect()
    {
        if (!Track.Active) return true;
        Plugin.Logger.LogInfo(
            "navigation: post-level Level Select, letting the game open it");

        // A FINALIZER TO SWALLOW VANILLA'S EXCEPTION WAS TRIED HERE AND MUST
        // NOT BE TRIED AGAIN.
        //
        // ReplayMenu.LevelSelect raises a NullReferenceException on roughly
        // one call in eight against a run's state, and absorbing it looked
        // free: the menu is already open when it lands, so the log went clean
        // and nothing visible changed. It was not free. The exception is the
        // game ABORTING a routine partway; letting it return normally instead
        // carries on from a state the game never intended to reach, and the
        // run collapsed - 1 puzzle beaten instead of 8, with slots cycling
        // "not finishable yet" forever and no blocking reason.
        //
        // One logged exception per run is the cheaper half of that trade.
        return true;
    }


    /// <summary>
    /// The first live instance of a component, including inactive ones.
    ///
    /// FindObjectOfType only sees active objects, and the pieces of the menu
    /// system are switched off most of the time - which is exactly when this
    /// is asked for. Resources.FindObjectsOfTypeAll also returns prefabs, so
    /// the scene check is what keeps this to real objects.
    /// </summary>
    private static T? FindEvenIfInactive<T>() where T : UnityEngine.Component
    {
        try
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(
                         Il2CppInterop.Runtime.Il2CppType.Of<T>()))
            {
                var found = obj == null ? null : obj.TryCast<T>();
                if (found == null || found.gameObject == null) continue;
                if (!found.gameObject.scene.IsValid()) continue;   // a prefab
                return found;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not look for a {typeof(T).Name}: {e.Message}");
        }
        return null;
    }

    /// <summary>
    /// Make the pause menu's Exit leave the level.
    ///
    /// MEASURED, not guessed. A probe on MainMenu.ExitGame was shipped first
    /// precisely because two different causes were possible and the repairs
    /// differ; the probe answered it:
    ///
    ///     navigation: Exit pressed (run active: True)
    ///     clickbutton: invoking Exit Button (Button on Exit Button)
    ///     after the press, 12 button(s) are active
    ///
    /// So the click lands, the handler runs, and nothing happens - the same 12
    /// buttons are still on screen afterwards. That rules out the other
    /// candidate, which was ConnectionPane having permanently hijacked the
    /// game's shared modal confirm button (a real defect, fixed separately in
    /// 0.3.1, but not this one).
    ///
    /// This is the failure class already recorded for LevelSelect in
    /// docs/verification-log.md:300-345: the vanilla route decides where to go
    /// from the level's KIND, and a run's levels are reached in a way that
    /// leaves it with no answer, so it goes nowhere at all.
    ///
    /// The title screen rather than the run's track, because Levels already
    /// goes to the track and two buttons doing the same thing would be worse
    /// than one doing nothing. The run is untouched by leaving: progress lives
    /// in the session save, and the title screen's Archipelago entry reports
    /// the connection, so Play or Levels comes straight back to it.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.ExitGame))]
    [HarmonyPrefix]
    private static bool BeforePauseExit()
    {
        try
        {
            if (!Track.Active) return true;              // vanilla behaviour

            var gm = GameManager.Instance;
            if (gm == null) return true;

            // The game's OWN route out, not a forced state change.
            //
            // The first version of this fix called
            // SetGameState<Title_GameState> directly. That works, but it is
            // the same thing DevTools' menu: command does, and watching that
            // command fail seven times out of seven with a
            // NullReferenceException inside MenuManager.TransitionMenuOut is
            // not a foundation to build a player-facing button on: forcing the
            // state slams past whatever the menu system was doing, and whether
            // it survives depends on what happened to be open.
            //
            // ExitGameToTitle.BackToTitle() is the routine the game itself uses
            // to leave a game for the title screen, so the menu system unwinds
            // the way it expects to. Same principle as the Levels button, which
            // goes through GoToLevelSelectForLevel rather than forcing
            // Levels_GameState.
            var exit = FindEvenIfInactive<ExitGameToTitle>();
            if (exit != null)
            {
                Plugin.Logger.LogInfo(
                    "navigation: pause menu Exit -> the title screen (BackToTitle)");
                exit.BackToTitle();
                return false;
            }

            // Only if the game has no such component. Kept because a working
            // Exit through a blunt route beats the button doing nothing, which
            // is the bug being fixed.
            // The SAME marker as the route above, plus how it got there.
            //
            // These logged different things, and the e2e asserted on the first
            // one - so a run where the component was missing reported "the
            // pause menu Exit leaves the level: FAIL" while Exit was in fact
            // working perfectly through this branch. An assertion that depends
            // on WHICH branch ran is testing the implementation, not the
            // behaviour.
            Plugin.Logger.LogInfo(
                "navigation: pause menu Exit -> the title screen (forced; "
                + "no ExitGameToTitle in the scene)");
            gm.SetGameState<Title_GameState>(null, false);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: Exit could not reach the title: {e.Message}");
            return true;
        }
    }


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
