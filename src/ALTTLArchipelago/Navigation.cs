using System;
using ALTTLArchipelago.Core;
using ALTTLModKit;
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
    /// The campaign level to hand the game's routing when we want the run's
    /// own track, for callers outside this file.
    ///
    /// Exposed for DlcGuard, which needs exactly the level this file already
    /// picks - one that is NOT in the run, so it cannot be mistaken for a slot
    /// if it ends up as the active level interface.
    /// </summary>
    internal static LevelInterface? CampaignLevelForRouting()
        => CampaignLevel(GameManager.Instance?.levelManager);

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
                var ours = container.Find(ResetEntryName);
                if (ours != null) ours.gameObject.SetActive(false);
                _skipEntry = null;
                _hintEntry = null;
                _resetEntry = null;
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

            _resetEntry = EnsureResetEntry(container);
            if (_resetEntry != null) Annotate(_resetEntry, $"{Backgrounds.ResetsAvailable}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not fix the pause menu: {e.Message}");
        }
    }

    /// <summary>The pause-menu entry that spends a Background Reset Token.</summary>
    private const string ResetEntryName = "AP Reset Background";

    /// <summary>
    /// Add the Reset Background entry under Hint, cloned from it the way
    /// ConnectionPane.AddMenuButton clones Settings on the title screen: the
    /// clone inherits the font, hover and layout of the entries around it.
    ///
    /// Its name must not start with Skip, Hint or Levels - the loop above
    /// matches entries by that prefix. Its localiser is destroyed, which the
    /// Hint entry's must never be (see Caption): this object is ours, and a
    /// live localiser would put "Hint" back over our label.
    /// </summary>
    private static Transform? EnsureResetEntry(Transform container)
    {
        try
        {
            var entry = container.Find(ResetEntryName);
            if (entry == null)
            {
                if (_hintEntry == null) return null;
                var clone = UnityEngine.Object.Instantiate(_hintEntry.gameObject, container);
                clone.name = ResetEntryName;
                clone.transform.SetSiblingIndex(_hintEntry.GetSiblingIndex() + 1);

                foreach (var label in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (label == null) continue;
                    var localiser = label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
                    if (localiser != null) UnityEngine.Object.Destroy(localiser);
                    label.text = "Reset Background";
                }

                var button = clone.GetComponent<UnityEngine.UI.Button>();
                if (button != null)
                {
                    // Replaced, not cleared: RemoveAllListeners keeps the
                    // persistent listener that opens the hint notepad.
                    button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    button.onClick.AddListener(new Action(OnResetBackground));
                }
                else
                {
                    Plugin.Logger.LogWarning(
                        "navigation: the Hint entry has no Button; Reset Background will not respond");
                }

                entry = clone.transform;
                Plugin.Logger.LogInfo("navigation: added Reset Background to the pause menu");
            }

            if (!entry.gameObject.activeSelf) entry.gameObject.SetActive(true);
            return entry;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not add Reset Background: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reset Background was pressed: spend one token and put every backdrop
    /// back to the game's own colour, until the next Background Change Trap.
    /// </summary>
    private static void OnResetBackground()
    {
        try
        {
            var traps = Inventory.BackgroundTraps;
            switch (BackgroundResets.Check(Backgrounds.ResetsAvailable, traps, RunState.BackgroundResetAt))
            {
                case BackgroundResetRefusal.NoneHeld:
                    Toasts.Show("No Background Reset Token yet", Toasts.Notice);
                    return;
                case BackgroundResetRefusal.NothingToReset:
                    Toasts.Show("The background is already the game's own", Toasts.Notice);
                    return;
            }

            if (!RunState.SpendBackgroundReset(traps)) return;
            Backgrounds.RestoreOwnColours();

            var left = Backgrounds.ResetsAvailable;
            Plugin.Logger.LogInfo($"backgrounds: reset by a token at {traps} trap(s); {left} token(s) left");
            Toasts.Show($"Background reset - {left} token(s) left", Toasts.Notice);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"backgrounds: reset failed: {e.Message}");
        }
    }

    /// <summary>
    /// Recolour the pause screen, if the player has been sent a Background
    /// Change Trap.
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
                Backgrounds.RememberMenuImage(image);
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
    /// (ConnectionPane.SetLabel). That works and is wrong here: its label is
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
            if (_skipEntry == null && _hintEntry == null && _resetEntry == null) return;

            if (_skipEntry != null && _skipEntry.gameObject.activeInHierarchy)
                Annotate(_skipEntry, $"{Skips.Available}");

            if (_hintEntry != null && _hintEntry.gameObject.activeInHierarchy)
                Annotate(_hintEntry, HintTag());

            if (_resetEntry != null && _resetEntry.gameObject.activeInHierarchy)
                Annotate(_resetEntry, $"{Backgrounds.ResetsAvailable}");
        }
        catch
        {
            // On the frame path. A warning per frame would bury the log, and
            // the menu is rebuilt often enough to recover on its own.
            _skipEntry = null;
            _hintEntry = null;
            _resetEntry = null;
        }
    }

    private static Transform? _skipEntry;
    private static Transform? _hintEntry;
    private static Transform? _resetEntry;


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
    /// started. ConnectionPane destroys it (ConnectionPane.SetLabel) because
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

        // NOTHING PLAYABLE: the level select, not the game's own next level.
        // Asked here, at the press, because the finished slot's check is
        // filed by now - GetNextLevelIndex is also asked while the post-level
        // screen builds, before it is. droha, 2026-09-26: "it would be more
        // visually better to know when you are blocked".
        if (Track.NextPlayableSlot() < 0)
        {
            ShowTrackFromThePostLevel(which);
            return false;
        }

        Plugin.Logger.LogInfo($"navigation: {which}, letting the game advance");
        return true;
    }

    /// <summary>Seconds left to open the track once the title settles, or 0 when idle.</summary>
    private static float _trackAfterTitle;
    private static float _titleSettled;
    private const float TrackAfterTitlePatience = 5f;

    /// <summary>Seconds left to look for a title menu left up under the track, or 0.</summary>
    private static float _titleLeftCheck;

    /// <summary>Seconds left for the post-level Level Select to press and land, or 0 when idle.</summary>
    private static float _postLevelWait;
    private static bool _postLevelPressed;
    private static string _postLevelWhy = "";
    private const float PostLevelPatience = 4f;

    /// <summary>
    /// Show the run's level select because nothing is playable, straight from
    /// the post-level screen: the game's own post-level Level Select,
    /// ReplayMenu.LevelSelect. droha, 2026-09-28: "it first goes to the main
    /// menu? it should hopefully go directly to the level select".
    ///
    /// It makes the same call as the title's Levels button,
    /// SetGameState&lt;Levels_GameState&gt; (the interop's xref cache: both
    /// methods reach it through one generic instance), and it is how the
    /// release harness leaves every beaten puzzle (DevTools replayselect).
    /// GoToLevelSelectForLevel is NOT the same thing from here: it builds the
    /// half-made menu BeforeReplayLevelSelect describes.
    ///
    /// Pressed from TickPostLevelSelect on a later frame, once nothing is
    /// moving: the game's own method does nothing while the menu system is
    /// transitioning, and this is asked from inside a press. If the game has
    /// not left the post-level screen a few seconds later, the title route
    /// takes over.
    /// </summary>
    private static void ShowTrackFromThePostLevel(string why)
    {
        Plugin.Logger.LogInfo(
            $"navigation: nothing playable after {why} - the level select, by the post-level Level Select");
        Toasts.Show("Nothing to play yet - waiting on items", Toasts.Notice);
        _postLevelWhy = why;
        _postLevelPressed = false;
        _postLevelWait = PostLevelPatience;
    }

    private static void TickPostLevelSelect(float dt)
    {
        if (_postLevelWait <= 0f) return;
        _postLevelWait -= dt;

        try
        {
            var gm = GameManager.Instance;
            var mm = gm?.menuManager;
            var state = gm?.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;

            if (!_postLevelPressed)
            {
                if (gm == null || mm == null || gm.IsTransitioning || mm.IsTransitioning)
                {
                    if (_postLevelWait <= 0f) PostLevelFallback($"the game never settled ({state})");
                    return;
                }

                var replay = FindEvenIfInactive<ReplayMenu>();
                if (replay == null)
                {
                    PostLevelFallback("no ReplayMenu");
                    return;
                }

                _postLevelPressed = true;
                _postLevelWait = PostLevelPatience;
                Plugin.Logger.LogInfo($"navigation: pressing the post-level Level Select (state: {state})");
                replay.LevelSelect();
                return;
            }

            // The run's track, or a DLC's own level select, which DlcGuard
            // leaves for the track.
            if (state == "Levels_GameState" || state == "DLCLevels_GameState")
            {
                _postLevelWait = 0f;
                Plugin.Logger.LogInfo($"navigation: the level select is up ({state})");
                return;
            }

            if (_postLevelWait <= 0f) PostLevelFallback($"the state is still {state}");
        }
        catch (Exception e)
        {
            // The game's own routine raising partway, as it does about one
            // press in eight for a player too (release_e2e KNOWN_ERRORS). The
            // state check on the next frames decides whether it got there.
            Plugin.Logger.LogWarning($"navigation: the post-level Level Select raised: {e.Message}");
            if (!_postLevelPressed) PostLevelFallback(e.Message);
        }
    }

    private static void PostLevelFallback(string reason)
    {
        _postLevelWait = 0f;
        Plugin.Logger.LogWarning($"navigation: the post-level Level Select did not open the track: {reason}");
        ShowTrackByWayOfTitle(_postLevelWhy, toast: false);
    }

    /// <summary>
    /// Show the run's level select because nothing is playable, by way of the
    /// title: the title first, then its own Levels button once it has
    /// settled - the order a player takes. From the Daily page a track opened
    /// directly drew with no Close button and its cards never launched
    /// (DailyGuard, measured 2026-09-25), so the daily guard takes this route;
    /// after a puzzle it is the fallback for ShowTrackFromThePostLevel.
    /// </summary>
    internal static void ShowTrackByWayOfTitle(string why, bool toast = true)
    {
        try
        {
            Plugin.Logger.LogInfo(
                $"navigation: nothing playable after {why} - the track, by way of the title");
            if (toast) Toasts.Show("Nothing to play yet - waiting on items", Toasts.Notice);
            _trackAfterTitle = TrackAfterTitlePatience;
            _titleSettled = 0f;
            GameManager.Instance?.SetGameState<Title_GameState>(null, false);
        }
        catch (Exception e)
        {
            _trackAfterTitle = 0f;
            Plugin.Logger.LogWarning($"navigation: could not leave for the title: {e.Message}");
        }
    }

    /// <summary>
    /// The second half of ShowTrackByWayOfTitle: once the title is up and
    /// still, press its Levels button.
    ///
    /// TitleMenu.LevelSelect, not a forced Levels_GameState. The forced state
    /// is what the daily guard used, and in the 0.4.2 playtest it once left
    /// the title's own menu drawn under the track (droha's log, after
    /// Post-It Notes #3; screenshot main-menu-level-select-bug). The title's
    /// own button tears its menu down the way a player's press does. The
    /// forced state stays as the fallback for a title with no TitleMenu.
    /// </summary>
    internal static void TickTrackAfterTitle(float dt)
    {
        TickPostLevelSelect(dt);
        TickTitleLeftUnder(dt);
        if (_trackAfterTitle <= 0f) return;
        _trackAfterTitle -= dt;

        try
        {
            var gm = GameManager.Instance;
            var state = gm?.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
            // THE MENU'S OWN TRANSITION TOO. GameManager.IsTransitioning says
            // nothing about the title menu sliding in, and a track opened half
            // a second into it launched nothing (measured 2026-09-25); the
            // same route taken seconds later worked.
            var mm = gm?.menuManager;
            var menuBusy = mm == null || mm.IsTransitioning
                           || mm.ActiveMenu == null || !mm.ActiveMenu.Interactive;
            // AND THE ACTIVE MENU MUST BE THE TITLE'S. Measured 2026-09-27:
            // leaving the Daily page, the state read Title_GameState while the
            // menu system's active menu was still the DailyTidyMenu, which is
            // interactive - so "settled" passed, Levels was pressed on a title
            // the menu system had not switched to, and the title stayed drawn
            // under the track (the 0.4.2 playtest's screenshot). The patience
            // below still presses once it runs out; TickTitleLeftUnder is the
            // backstop for that case.
            var onTitle = mm?.ActiveMenu?.TryCast<TitleMenu>() != null;
            if (gm == null || state != "Title_GameState" || gm.IsTransitioning || menuBusy
                || (!onTitle && _trackAfterTitle > 1f))
            {
                _titleSettled = 0f;
                if (_trackAfterTitle <= 0f)
                {
                    Plugin.Logger.LogWarning(
                        "navigation: the title never settled; the track was not opened - press Levels");
                }
                return;
            }

            // A beat on a still title, as a player would take.
            _titleSettled += dt;
            if (_titleSettled < 0.5f) return;

            _trackAfterTitle = 0f;
            _titleSettled = 0f;

            var active = mm!.ActiveMenu;
            var title = active.TryCast<TitleMenu>() ?? FindEvenIfInactive<TitleMenu>();
            if (title != null)
            {
                Plugin.Logger.LogInfo(
                    $"navigation: title is up - pressing its Levels (active menu: {active.GetIl2CppType().Name})");
                title.LevelSelect();
                _titleLeftCheck = 6f;
                return;
            }

            Plugin.Logger.LogInfo("navigation: title is up, no TitleMenu - opening the track directly");
            gm.SetGameState<Levels_GameState>(null, false);
        }
        catch (Exception e)
        {
            _trackAfterTitle = 0f;
            Plugin.Logger.LogWarning($"navigation: could not open the track: {e.Message}");
        }
    }

    /// <summary>
    /// After the track opens by way of the title: if the title's own menu is
    /// still up under it, take it down.
    ///
    /// MEASURED, 2026-09-27: finishing the last playable puzzle (a generator,
    /// so by the Daily page) and pressing the title's Levels left TitleMenu
    /// active at alpha 1 under the LevelSelect (DevTools menus) - the 0.4.2
    /// playtest's "title drawn under the level select" (screenshot
    /// main-menu-level-select-bug), reproduced. A forced Levels_GameState did
    /// the same in the playtest. MenuManager.DeactivateMenu is the menu
    /// system's own way to take one down.
    /// </summary>
    private static void TickTitleLeftUnder(float dt)
    {
        if (_titleLeftCheck <= 0f) return;
        _titleLeftCheck -= dt;
        try
        {
            var gm = GameManager.Instance;
            var mm = gm?.menuManager;
            var state = gm?.GameState == null ? "" : gm.GameState.GetIl2CppType().Name;
            if (gm == null || mm == null || state != "Levels_GameState" || gm.IsTransitioning
                || mm.IsTransitioning) return;

            _titleLeftCheck = 0f;
            var title = FindEvenIfInactive<TitleMenu>();
            if (title == null || !title.gameObject.activeInHierarchy) return;

            Plugin.Logger.LogInfo("navigation: the title menu was left up under the track - taking it down");
            mm.DeactivateMenu(title);
        }
        catch (Exception e)
        {
            _titleLeftCheck = 0f;
            Plugin.Logger.LogWarning($"navigation: could not take the title down: {e.Message}");
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
    /// not its save list. The detail is in docs/history/verification-log.md - read it
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
    internal static bool GoToTrack(string which)
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
    private static bool BeforePauseLevelSelect()
    {
        // The credits end through their own routine here too, or they play
        // on under the track exactly as they did under the title (see
        // EndCreditsIfRunning). Their ending then lands on the track itself.
        if (EndCreditsIfRunning("pause menu Levels")) return false;
        return GoToTrack("pause menu");
    }

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
///
    /// KNOWN GAP, MEASURED 2026-09-17: a DLC puzzle lands on THAT DLC's own
    /// level select, because the game routes by the level. A run made of
    /// Seeing Stars puzzles drops the player into the Seeing Stars menu after
    /// every puzzle, with the finished level still loaded behind it, and the
    /// mod then tries to paint the run's cards onto a track it does not own:
    ///
    ///     track: card at position 17 but the plan covers 0 (39 cards on the track)
    ///
    /// REDIRECTING HERE DOES NOT FIX IT. Sending only the DLC case through
    /// GoToTrack was tried and reintroduced exactly the failure described
    /// above, in full: "no active control named Close Button" on every press,
    /// then
    ///
    ///     menu failed: Il2CppException: NullReferenceException
    ///       at MenuManager.TransitionMenuOut
    ///       at MenuManager.CloseActiveMenu
    ///
    /// The half-built menu is a property of calling GoToLevelSelectForLevel
    /// from here instead of letting this method run, so "only for DLC levels"
    /// bought nothing.
    ///
    /// WHERE THE DLC DECISION IS NOT, measured: this method reads
    /// LevelInterface.IsDLCLevel itself (DevTools xrefs, 2026-09-24/25), yet
    /// neither an IsDLCLevel postfix answering false nor blanking the finished
    /// level's DLC details for the length of this call (2026-09-25) kept the
    /// DLC menu from being built. A GoToLevelSelectForLevel prefix and a
    /// Gameplay_GameState.ContextualState postfix never ran on the route. The
    /// scan stops at this method's eighth call, so the rest is unread. DlcGuard
    /// still leaves the DLC menu once the game reaches it.
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
    internal static T? FindEvenIfInactive<T>() where T : UnityEngine.Component
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
    /// End the credits with the game's own Credits.CreditsComplete, if they
    /// are what is running. True when it did.
    ///
    /// Leaving the credits any other way left them playing under whatever
    /// came next: droha, 2026-09-25, "when i exit out of the credits, and hit
    /// play from the main menu, or go to a level in level select, the
    /// credits still playing". Both pause-menu routes out, Exit and Levels,
    /// come through here. The ending asks for the next level, which
    /// AfterGetNextLevelIndex answers with the run's track.
    ///
    /// Found through the ACTIVE LEVEL first, then a scene search, then one
    /// that includes inactive objects: the first version only tried
    /// FindObjectOfType, which skips inactive objects, and droha's exit fell
    /// through to the forced title with the credits still on screen.
    /// </summary>
    private static bool EndCreditsIfRunning(string which)
    {
        if (!Track.Active) return false;
        var active = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        if (active?.IsCredits != true) return false;

        var (credits, via) = FindCredits(active);
        if (credits == null)
        {
            Plugin.Logger.LogWarning(
                $"navigation: {which} during the credits, but no Credits object was "
                + "found - the credits may keep playing");
            return false;
        }

        Plugin.Logger.LogInfo(
            $"navigation: {which} during the credits -> Credits.CreditsComplete ({via})");
        try
        {
            credits.CreditsComplete();
        }
        catch (Exception e)
        {
            // EXPECTED, AND HARMLESS. CreditsComplete asks for the next level
            // mid-call; AfterGetNextLevelIndex answers by opening the track
            // right there, and the rest of the game's own routine then trips
            // over what it tore down: a NullReferenceException raised inside
            // the native Credits.CreditsComplete (measured 2026-09-25). The
            // credits have ended and the track is whole (Close button, cards
            // launch).
            Plugin.Logger.LogInfo(
                $"navigation: the credits ended; the game's own ending tripped afterwards ({e.GetType().Name})");
        }
        return true;
    }

    /// <summary>The running Credits, and how it was found, or (null, "").</summary>
    private static (global::Credits?, string) FindCredits(LevelInterface active)
    {
        try
        {
            var viaLevel = active.Level?.TryCast<global::Credits>();
            if (viaLevel != null) return (viaLevel, "the active level");
        }
        catch
        {
            // Fall through to the searches.
        }

        var inScene = UnityEngine.Object.FindObjectOfType<global::Credits>();
        if (inScene != null) return (inScene, "a scene search");

        foreach (var obj in UnityEngine.Object.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<global::Credits>()))
        {
            var one = obj == null ? null : obj.TryCast<global::Credits>();
            // A prefab asset is in no scene; only a live copy is the credits.
            if (one != null && one.gameObject != null && one.gameObject.scene.IsValid())
            {
                return (one, "a search including inactive objects");
            }
        }
        return (null, "");
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
    /// docs/history/verification-log.md, "Beating a level can drop the run onto the
    /// Daily Tidy page": the vanilla route decides where to go
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

            // THE CREDITS END THROUGH THEIR OWN ROUTINE. The credits scene has
            // no ExitGameToTitle, so this fell to the forced title state
            // below, and the credits sequence kept running under it: droha,
            // 2026-09-25, "when i exit out of the credits, and hit play from
            // the main menu, or go to a level in level select, the credits
            // still playing". Credits.CreditsComplete is the game's own end of
            // the credits. See EndCreditsIfRunning.
            if (EndCreditsIfRunning("pause menu Exit")) return false;

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

            // THE CREDITS DO NOT HAVE A NEXT. They are the end of the run, and
            // answering this with the next unfinished slot dropped the player
            // straight into another puzzle the moment the ending finished -
            // droha: "after it played the credits it loaded into a new level.
            // Not what I was expecting... probably just dump them back to
            // level select."
            //
            // Sent to the track rather than the title: the run is finished and
            // the level select is where you can see it, including the cards
            // still unstarred if the goal was beaten rather than starred.
            var active = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            if (active != null && active.IsCredits)
            {
                Plugin.Logger.LogInfo(
                    "navigation: the credits have no next level - back to the track");
                GoToTrack("the credits");
                return;
            }

            var next = Track.NextPlayableSlot();
            // Nothing playable: leave the game's answer alone. This postfix
            // also runs while the post-level screen builds, so it must not
            // navigate; the press itself (LetTheGameAdvance) goes to the
            // track instead.
            if (next < 0) return;

            __result = Track.ArmSlot(next);
            Plugin.Logger.LogInfo($"navigation: next -> slot {next} (level {__result})");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"navigation: could not pick the next level: {e.Message}");
        }
    }
}
