using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// Keep the run out of the game's Daily Tidy machinery.
///
/// THIRTY-SIX of the 111 levels a seed can draw are in the game's daily-tidy
/// pool - a third of every run. Not six.
///
/// The six was written here as fact and was wrong by a factor of six. It came
/// from reasoning about the "(Randomized)" level names instead of asking the
/// game, and it survived because nothing checked it. The real list is every
/// holiday set (MerryMess, TrickOrTidy, GoodTidings, SomethingEggstra) plus
/// SpiderWeb, Buttons, Shells, Telescope, Clock, Calendar and more. It is now
/// recorded per level as isDailyTidy in levels.json, straight out of the game's
/// own DailyTidyManager, and pinned by a test - see DailyPoolTests.
///
/// Believing it was six is what made this bug take three attempts. A guard
/// aimed at a handful of generator levels looked adequate; a guard that has to
/// hold for a third of every run does not, and the difference is the whole
/// reason droha kept landing on the Daily page.
///
/// Vanilla treats finishing one as finishing today's daily, and that is two
/// separate problems:
///
/// 1. **It routes you out of the run.** Reported as "still auto-sent to the
///    Daily page when finishing a daily puzzle - happens on completion, not on
///    next". Navigation already redirects the post-level BUTTONS
///    (RetryMenu.NextLevel, ReplayMenu.LevelSelect, GetNextLevelIndex), but for
///    a daily-pool level the retry menu is never shown at all, so there is no
///    button to press: the completion tween ends straight in DailyTidy state.
///    Patching the buttons could never have caught this.
///
/// 2. **It writes to the player's REAL daily save.** The completion count, the
///    streak and the badge round all advance, and the badge prompt fires - which
///    is the "getting badges/stickers popups from daily puzzles" report. The
///    run's own save redirect does not cover this: SaveRedirect scopes the
///    LEVEL data to save_ap_&lt;slot&gt;_&lt;seed&gt;, while the daily counters live in the
///    profile. So a run has been quietly advancing daily progress the player
///    never played, which is the same class of bug the redirect exists to
///    prevent.
///
/// The primary fix is the game's own switch: LevelInterface.ReactToDailyCompleting
/// is what the completion chain reads to decide a level is a daily. Answering
/// false while a run is active is a read-only change - it mutates no state and
/// leaves vanilla untouched the moment the run ends.
///
/// THE PRIMARY FIX WAS NOT ENOUGH, AND THE REASON IS INSTRUCTIVE. On
/// 2026-09-08 droha finished SpiderWeb, pressed the next arrow, and landed on
/// the Daily page again - with NOTHING in the log.
///
/// The arrow WAS the trigger. What the silence means is narrower and it is
/// worth stating precisely, because getting it wrong sends the next person
/// hunting for a phantom: the control droha pressed was not
/// RetryMenu.NextLevel or ReplayMenu.NextLevel, the two this mod patches. A
/// daily-pool level does not get the ordinary retry menu, so the arrow on that
/// completion screen belongs to the daily flow and goes where the daily flow
/// goes. Patching named buttons can only ever cover the buttons somebody
/// thought to name.
///
/// droha put the better idea plainly: "we should be able to tell when the game
/// is trying to go to this menu right? and not allow it? and use our next
/// level activation instead."
///
/// So the guard moved to the DESTINATION, in two layers.
///
/// The first is prevention: while a run owns the game, no level answers yes to
/// IsDailyTidy or IsHolidayDaily, so the completion chain never decides to go
/// there. That covers all 36 daily levels without needing to know which control
/// the player pressed.
///
/// The second is TickRescue, a watchdog on the game STATE. Refusing named
/// entry points was tried and does not generalise: GameManager.SetGameState
/// has a non-generic overload this class does prefix, but SetGameState&lt;T&gt;
/// is a separate native function that the prefix never sees, and it cannot be
/// patched - see the note above InstallOptional for how trying deadlocked the
/// game. Watching where the game ENDED UP needs no list of routes and cannot
/// be defeated by a new one. The run then starts its own next puzzle, which is
/// what the player was asking for when they pressed the arrow.
///
/// TickRescue is the backstop behind the backstop: if the game reaches the
/// Daily page by some path that never calls SetGameState at all, being THERE is
/// itself the trigger. "Never" was the requirement, so the check is on the
/// state rather than on the routes to it.
///
/// The DailyTidyManager guards below are belt and braces for the save half, and
/// are resolved BY NAME rather than declared as attribute patches. The interop
/// assembly carries no method bodies, so the routing above is read off the
/// signature surface; if one of these names is wrong, name resolution logs it
/// and the feature degrades, instead of a missing-method exception taking every
/// patch in this class down with it.
/// </summary>
[HarmonyPatch]
internal static class DailyGuard
{
    /// <summary>
    /// Tell the game a level is not a daily while the run owns it.
    ///
    /// A postfix on the getter rather than a call to SetReactToDailyCompleting:
    /// the setter would have to be driven at every launch and unwound at every
    /// exit, and anything that missed an exit would leave the player's real
    /// dailies suppressed. Answering the question is stateless.
    /// </summary>
    [HarmonyPatch(typeof(LevelInterface),
                  nameof(LevelInterface.ReactToDailyCompleting),
                  MethodType.Getter)]
    [HarmonyPostfix]
    private static void AfterReactToDailyCompleting(ref bool __result)
    {
        if (__result && Track.Active) __result = false;
    }

    /// <summary>
    /// While the run owns the game, no level is a daily.
    ///
    /// ReactToDailyCompleting was the wrong switch on its own, and the scale of
    /// the problem is why. This class was written believing six levels were
    /// affected - the "(Randomized)" ones. The real number is THIRTY-SIX of the
    /// 111 in the pool: every holiday set (MerryMess, TrickOrTidy, GoodTidings,
    /// SomethingEggstra), plus SpiderWeb, Buttons, Shells, Telescope and the
    /// rest. A third of every run is drawn from the daily pool, so this is the
    /// common case rather than an edge.
    ///
    /// Answering the two questions the completion chain actually asks is what
    /// stops the routing happening at all, rather than bouncing off it after
    /// the fact. Both are read-only: nothing is written, and vanilla is
    /// untouched the moment the run ends.
    /// </summary>
    [HarmonyPatch(typeof(LevelInterface), nameof(LevelInterface.IsDailyTidy),
                  MethodType.Getter)]
    [HarmonyPostfix]
    private static void AfterIsDailyTidy(ref bool __result)
    {
        if (__result && Track.Active) __result = false;
    }

    [HarmonyPatch(typeof(LevelInterface), nameof(LevelInterface.IsHolidayDaily),
                  MethodType.Getter)]
    [HarmonyPostfix]
    private static void AfterIsHolidayDaily(ref bool __result)
    {
        if (__result && Track.Active) __result = false;
    }

    /// <summary>
    /// Refuse a call outright. Used as a prefix on the daily bookkeeping.
    /// </summary>
    private static bool SkipWhileRunning() => !Track.Active;

    /// <summary>The state the run must never enter.</summary>
    private const string DailyState = "DailyTidy_GameState";

    /// <summary>
    /// Seconds to wait before taking the run somewhere else.
    ///
    /// Not zero. The trigger fires inside the game's own transition, and
    /// starting a level from in there is the kind of re-entrancy that has cost
    /// this project days elsewhere - see Track.TickScroll, where work done
    /// during a transition was simply overwritten. Two frames of the previous
    /// screen is a much smaller price than the page being avoided.
    /// </summary>
    private const float RescueDelay = 0.1f;

    /// <summary>How long to keep waiting for the game to stop transitioning.</summary>
    private const float RescuePatience = 3f;

    /// <summary>
    /// How many times to try before giving up and saying so.
    ///
    /// THIS EXISTS BECAUSE THE FIRST VERSION RAN AWAY. The rescue called
    /// Track.LaunchSlot, which calls StartLevel - and StartLevel loads a level
    /// UNDERNEATH whatever screen is up without changing the game state. So the
    /// state stayed DailyTidy, the watchdog saw it again on the next frame,
    /// launched the next slot, and went round for as long as the game was open:
    /// hundreds of levels initialised on top of each other, and a Unity
    /// ArgumentException on every lap. droha watched it happen.
    ///
    /// The real fix is below - leave through the game's own state transition,
    /// which is the thing that actually moves the player. This cap is the
    /// admission that a watchdog which cannot verify its own success is a loop
    /// waiting to happen, and that failing loudly once beats trying forever.
    /// </summary>
    private const int RescueAttempts = 3;

    private static float _rescueIn = -1f;
    private static float _rescueWaited;
    private static int _attempts;
    private static bool _gaveUp;

    /// <summary>
    /// Refuse to enter the Daily Tidy state while a run owns the game.
    ///
    /// Matched on the type NAME rather than on Il2CppType.Of&lt;T&gt;(). The
    /// argument is an Il2CppSystem.Type whose managed wrapper is a different
    /// object on each crossing, so reference equality is not reliable, and the
    /// name is what the game's own state history is keyed on anyway.
    ///
    /// This catches the non-generic entry ONLY. SetGameState&lt;T&gt; is a
    /// separate native function and this prefix never sees it - proved by
    /// driving the generic and watching this stay silent while the state
    /// changed anyway. It cannot be patched; see the note above
    /// InstallOptional. TickRescue is what covers that route.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetGameState),
                  new[] { typeof(Il2CppSystem.Type), typeof(GameStateData), typeof(bool) })]
    [HarmonyPrefix]
    private static bool BeforeSetGameState(Il2CppSystem.Type t)
    {
        try
        {
            if (!Track.Active) return true;                   // vanilla behaviour
            if (t == null || t.Name != DailyState) return true;
            return RefuseDailyState();
        }
        catch (Exception e)
        {
            // A guard that throws must not take the game's navigation with it.
            Plugin.Logger.LogWarning($"daily guard: could not check the state: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Refuse, and arrange for the run to go somewhere useful instead.
    /// Shared by both SetGameState entry points.
    /// </summary>
    private static bool RefuseDailyState()
    {
        if (!Track.Active) return true;

        Plugin.Logger.LogInfo(
            "daily guard: refused a transition to the Daily Tidy page; "
            + "the run will open its own next puzzle");
        ArmRescue();
        return false;
    }

    private static void ArmRescue()
    {
        if (_rescueIn > 0f || _gaveUp) return;                // already armed
        _rescueIn = RescueDelay;
        _rescueWaited = 0f;
    }

    /// <summary>
    /// Put the player back in the run, and catch the Daily page if it ever
    /// gets there anyway.
    ///
    /// The second half is what makes "never" true. Refusing SetGameState closes
    /// the routes through it, but a route that reaches the state some other way
    /// would slip past - so the tick also asks the plain question, "are we on
    /// the Daily page during a run", and leaves if the answer is yes. Being
    /// there is the trigger, which needs no list of routes.
    /// </summary>
    internal static void TickRescue(float dt)
    {
        if (!Track.Active)
        {
            _rescueIn = -1f;
            _attempts = 0;
            _gaveUp = false;
            return;
        }

        var here = InDailyState();

        // Out of it, by our doing or the player's: the counter is spent.
        if (!here && _rescueIn < 0f)
        {
            _attempts = 0;
            _gaveUp = false;
            return;
        }

        // The backstop. Cheap: a string compare on the active state.
        if (here && _rescueIn < 0f) ArmRescue();

        if (_rescueIn < 0f) return;

        _rescueIn -= dt;
        if (_rescueIn > 0f) return;

        // Never start a level mid-transition. Give the game a few seconds to
        // settle, then go anyway rather than leaving the player stranded.
        _rescueWaited += dt;
        var gm = GameManager.Instance;
        if (gm != null && gm.IsTransitioning && _rescueWaited < RescuePatience)
        {
            _rescueIn = 0.05f;
            return;
        }

        _rescueIn = -1f;

        if (++_attempts > RescueAttempts)
        {
            // Loudly, once. A silent give-up here is how the player ends up
            // stuck on the page this whole class exists to avoid.
            _gaveUp = true;
            Plugin.Logger.LogError(
                $"daily guard: still on the Daily Tidy page after {RescueAttempts} "
                + "attempts to leave - giving up rather than looping. Use the "
                + "pause menu to get back to the run.");
            return;
        }

        Rescue(gm);
    }

    private static bool InDailyState()
    {
        try
        {
            var state = GameManager.Instance?.GameState;
            return state != null && state.GetIl2CppType().Name == DailyState;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Open the run's next puzzle, or its track if there is no next puzzle.
    ///
    /// THROUGH THE GAME'S OWN STATE TRANSITION, never by calling StartLevel.
    /// StartLevel loads a level under the screen that is already up, which
    /// leaves the menu in place and the state unchanged - the runaway loop
    /// described on RescueAttempts. Queueing the slot and letting
    /// Gameplay_GameState ask for its level index is the same route Play uses,
    /// and it is the transition that actually moves the player.
    ///
    /// The track is the fallback rather than the title screen: a run with
    /// nothing playable is waiting on an item, and the track is where that is
    /// legible. Landing on the title would look like the run had ended.
    /// </summary>
    private static void Rescue(GameManager? gm)
    {
        try
        {
            gm ??= GameManager.Instance;
            if (gm == null) return;

            var slot = Track.NextUnfinishedSlot();
            if (slot >= 0)
            {
                Plugin.Logger.LogInfo($"daily guard: opening slot {slot} instead");
                TitleScreen.QueueSlotForGameplay(slot);
                gm.SetGameState<Gameplay_GameState>(null, false);
                return;
            }

            Plugin.Logger.LogInfo(
                "daily guard: nothing playable to open, showing the track instead");
            gm.SetGameState<Levels_GameState>(null, false);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"daily guard: could not leave the daily page: {e.Message}");
        }
    }

    /// <summary>
    /// Patch the daily bookkeeping by name, reporting what was and was not
    /// found. Called after PatchAll so a miss costs only these guards.
    /// </summary>
    internal static void InstallOptional(Harmony harmony)
    {
        InstallDailyGuards(harmony);
    }

    /// <summary>
    /// DO NOT PATCH SetGameState&lt;T&gt;. It cannot be made to discriminate, and
    /// trying deadlocks the game.
    ///
    /// The reasoning that led here was sound and the conclusion was still
    /// wrong, so the whole chain is written down.
    ///
    /// IL2CPP compiles a generic method once per type argument, so
    /// SetGameState&lt;DailyTidy_GameState&gt; looked like a different native
    /// function from SetGameState(Type, ...) - and it measurably is: with only
    /// the non-generic patched, driving the generic put the game on the Daily
    /// page while the prefix never logged a thing. The obvious next step was to
    /// patch the closed generic through MakeGenericMethod.
    ///
    /// That step is the trap. IL2CPP shares ONE native implementation between
    /// every reference-type instantiation of a generic method, so patching
    /// SetGameState&lt;DailyTidy_GameState&gt; patches SetGameState&lt;T&gt; for
    /// every T. The prefix cannot recover T from shared code, so it refuses
    /// every state transition in the game - including the rescue's own
    /// SetGameState&lt;Gameplay_GameState&gt;. The result was a game frozen on
    /// the Daily page refusing its way out, with the log alternating "opening
    /// slot 2 instead" and "refused a transition" as the guard fought itself.
    ///
    /// So the generic route is covered by TickRescue instead. A watchdog on the
    /// STATE needs no list of routes and cannot be defeated by a new one, which
    /// is what "never go to this page" actually requires.
    /// </summary>

    /// <summary>
    /// Patch the daily bookkeeping by name, reporting what was and was not
    /// found. A miss here costs only these guards.
    /// </summary>
    private static void InstallDailyGuards(Harmony harmony)
    {
        var skip = new HarmonyMethod(
            typeof(DailyGuard).GetMethod(nameof(SkipWhileRunning),
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static));

        foreach (var (typeName, methodName) in new (string, string)[]
                 {
                     ("DailyTidyManager", "OnDailyTidyComplete"),
                     ("DailyTidyManager", "AdvanceDailyTidyCompletionCount"),
                     ("DailyTidyMenu",    "SetupForReturnFromCompletedLevel"),
                 })
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var method = type == null ? null : AccessTools.Method(type, methodName);
                if (method == null)
                {
                    Plugin.Logger.LogWarning(
                        $"daily guard: {typeName}.{methodName} not found, "
                        + "the run may still touch daily progress");
                    continue;
                }

                harmony.Patch(method, prefix: skip);
                Plugin.Logger.LogInfo($"daily guard: {typeName}.{methodName} gated on the run");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning(
                    $"daily guard: could not gate {typeName}.{methodName}: {e.Message}");
            }
        }
    }
}
