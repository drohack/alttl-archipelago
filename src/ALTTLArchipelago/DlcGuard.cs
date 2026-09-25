using System;

namespace ALTTLArchipelago;

/// <summary>
/// Keep a run out of a DLC's own level select.
///
/// THE PROBLEM. The game routes a finished level by asking what it was, and a
/// DLC puzzle belongs to its DLC - so LevelManager.GoToLevelSelectForLevel
/// sends the player to the Seeing Stars menu rather than the run's track, with
/// the finished level still loaded behind it. Measured over one eight-puzzle
/// DLC run: seven transitions into DLCLevels_GameState, and the mod then tried
/// to paint the run's cards onto a track it does not own -
/// "card at position 17 but the plan covers 0 (39 cards on the track)".
///
/// TWO THINGS THAT DID NOT WORK, both recorded so they are not tried again:
///
///  1. Redirecting the post-level menu through Navigation.GoToTrack. That
///     calls GoToLevelSelectForLevel, which produces a level select with no
///     Close button and then a NullReferenceException inside
///     MenuManager.TransitionMenuOut - the failure
///     Navigation.BeforeReplayLevelSelect already documents.
///  2. A postfix on LevelInterface.IsDLCLevel answering false during a run.
///     It binds and it answers, and the routing ignores it: the DLC state was
///     still entered seven times in a full run afterwards. Whatever
///     GoToLevelSelectForLevel asks, it is not that.
///
/// WHAT WORKS IS REFUSING THE STATE, which is what DailyGuard does for the
/// Daily Tidy page - the same shape of problem, solved the same way. The
/// transition is declined and the campaign level select is opened instead,
/// via the generic SetGameState rather than GoToLevelSelectForLevel, so the
/// half-built menu never exists.
///
/// DEFERRED, NOT IMMEDIATE. Switching state from inside a SetGameState prefix
/// is re-entrancy, and DailyGuard's comment is blunt about what that has cost
/// this project before. The switch is armed and happens on a later frame.
///
/// CLOSE FROM THE TRACK, 2026-09-24. The track this opens closes back to the
/// DLC menu it was opened from, so reopening the track there looped on every
/// press; that arrival now goes to the title (see _enteredFrom). Stopping the
/// post-level route from reaching the DLC menu at all is still open: see
/// Navigation.BeforeReplayLevelSelect for where the decision was measured.
/// </summary>
internal static class DlcGuard
{
    /// <summary>The state a DLC's own level select runs in.</summary>
    private const string DlcState = "DLCLevels_GameState";

    /// <summary>The campaign level select, which the run's track lives in.</summary>
    private const string LevelsState = "Levels_GameState";

    /// <summary>Seconds to wait before opening the track, or -1 when idle.</summary>
    private static float _openIn = -1f;

    /// <summary>The state the last frame ended in, by type name.</summary>
    private static string? _lastState;

    /// <summary>
    /// The state the game was in when it last entered the DLC state.
    ///
    /// LEVELS MEANS CLOSE WAS PRESSED ON THE RUN'S TRACK. The track's Close
    /// goes back to wherever the level select was opened from, and when this
    /// guard opened it, that is the DLC menu. Opening the track again there is
    /// what made Close loop forever (measured 2026-09-24: one reopen per
    /// press), so that arrival goes to the title instead: the player asked to
    /// leave the level select.
    /// </summary>
    private static string? _enteredFrom;

    // THE SetGameState PREFIX USED TO LIVE HERE, AND IT WAS NEVER INSTALLED.
    //
    // It was written to refuse a transition into DLCLevels_GameState, and the
    // file recorded that it "sees NOTHING on the DLC route ... so every one of
    // them came through the generic overload". That explanation was wrong.
    // DlcGuard was simply missing from the list of classes Plugin patches, so
    // NONE of its attributes were ever applied; only Tick, which Update calls
    // directly, ever ran. Measured 2026-09-18 by adding it to that list.
    //
    // Adding it there then produced "PATCH FAILED, dlc guard IS DISABLED: IL
    // Compile Error" and took the whole class down with it - which is exactly
    // why DailyGuard installs its own SetGameState patch by NAME through
    // InstallOptional rather than by attribute. Since this prefix has never
    // once run, and the routing redirect above stops the DLC level select
    // being built at all, it is deleted rather than reinstated. Tick remains
    // as the backstop for anything that still reaches the state.

    /// <summary>
    /// Open the run's track a moment after a refusal, and catch the generic
    /// route that no prefix can see.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (!Track.Active)
        {
            _openIn = -1f;
            _lastState = null;
            _enteredFrom = null;
            return;
        }

        var state = StateName();
        if (state != _lastState)
        {
            if (state == DlcState) _enteredFrom = _lastState;
            _lastState = state;
        }

        // The backstop: SetGameState<T> bypasses the prefix entirely, so the
        // only way to know is to look at where the game ended up.
        if (_openIn < 0f && state == DlcState) Arm();

        if (_openIn < 0f) return;

        _openIn -= dt;
        if (_openIn > 0f) return;

        // Never switch mid-transition; the game overwrites work done there.
        var gm = GameManager.Instance;
        if (gm != null && gm.IsTransitioning)
        {
            _openIn = 0.05f;
            return;
        }

        _openIn = -1f;

        // Once only: if the title route fails and the game is still here next
        // time, the track opens as it always did, rather than retrying forever.
        var closed = _enteredFrom == LevelsState;
        _enteredFrom = null;
        if (closed)
        {
            Plugin.Logger.LogInfo(
                "dlc guard: the run's track was closed; going to the title");
            ToTitle(gm);
            return;
        }

        Plugin.Logger.LogInfo("dlc guard: opening the run's track");
        Open(gm);
        // Logged rather than silent: this is the ONLY evidence the guard is
        // working. Two earlier attempts were called fixed on the strength of
        // warnings NOT appearing, and both were wrong.
    }

    private static void Arm()
    {
        if (_openIn > 0f) return;
        // Two frames of the previous screen is a much smaller price than the
        // re-entrancy of switching state from inside the game's own switch.
        _openIn = 0.1f;
    }

    private static void Open(GameManager? gm)
    {
        try
        {
            gm ??= GameManager.Instance;
            if (gm == null) return;

            // NAVIGATE, DO NOT JUST SWITCH STATE. This used the generic
            // SetGameState<Levels_GameState>, and the log could not tell the
            // difference: the state came out Levels_GameState and every
            // assertion passed. The SCREEN did not. Measured 2026-09-18 from a
            // screenshot - after finishing DLC2 Broken Vases the Seeing Stars
            // menu was already built, switching the state did not close it,
            // and BOTH LEVEL SELECTS RENDERED AT ONCE: the DLC's title and its
            // "1/17 (6%)" header over the run's track, two progress strips,
            // two sets of cards, with the finished level still loaded. The
            // giveaway in the log is "track: built 8 items" twice.
            //
            // GoToLevelSelectForLevel is the game's own navigation and it does
            // the teardown, the transition and the menu setup. Navigation's
            // pause-menu route already relies on exactly that, handing it a
            // campaign level to reach the campaign track; this asks for the
            // same thing from here.
            var home = Navigation.CampaignLevelForRouting();
            if (home != null && gm.levelManager != null)
            {
                gm.levelManager.GoToLevelSelectForLevel(home);
                return;
            }

            // No campaign level to route through - better a switched state
            // than none at all.
            gm.SetGameState<Levels_GameState>(null, false);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning(
                $"dlc guard: could not open the run's track: {e.Message}");
        }
    }

    /// <summary>
    /// Leave for the title screen, the same call as DevTools' menu:title.
    ///
    /// release_e2e.to_title records that call throwing only while it tore
    /// down a HALF-BUILT menu. The DLC menu this leaves was opened by the
    /// game's own Close, the case that call has run cleanly on.
    /// </summary>
    private static void ToTitle(GameManager? gm)
    {
        try
        {
            gm ??= GameManager.Instance;
            gm?.SetGameState<Title_GameState>(null, false);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning(
                $"dlc guard: could not go to the title: {e.Message}");
        }
    }

    private static string? StateName()
    {
        try
        {
            var state = GameManager.Instance?.GameState;
            return state == null ? null : state.GetIl2CppType().Name;
        }
        catch { return null; }
    }
}
