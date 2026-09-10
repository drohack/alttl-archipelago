using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ALTTLModKit;

/// <summary>
/// Stops the game reacting to keystrokes meant for a text box.
///
/// The game drives input through Rewired, and its maps are live all the time.
/// Typing into the connection pane therefore did two unwanted things, both
/// reported from play:
///
///   - the mouse cursor drifted, because keys are also bound to cursor and
///     mouse-button actions;
///   - Tab closed the dialog instead of moving to the next field, because the
///     UI module treats it as a navigation or cancel action.
///
/// So while a field has focus, the KEYBOARD's Rewired maps are switched off
/// and the input module's navigation actions are unbound. Everything is put
/// back exactly as it was on the way out.
///
/// KEYBOARD ONLY, AND THAT IS THE WHOLE POINT. This used to switch off every
/// map for every player, on the reasoning - written here, and wrong - that
/// "the mouse still works, it is Unity's pointer handling that clicks the
/// buttons, not Rewired's maps". The scene's EventSystem carries the GAME's
/// RewiredStandaloneInputModule, and that module reads mouse buttons from
/// Rewired's own IMouseInputSource, not from UnityEngine.Input. Disabling
/// every map therefore blinded it to the mouse RELEASE: its state stayed
/// pressed, so moving the mouse kept sending drag events to the focused
/// field and a second click never produced a fresh press.
///
/// droha reported exactly that - click once and the whole row highlights,
/// then "moving the mouse around highlights different things, like I'm
/// dragging it", and a second click does not put the caret where you clicked.
///
/// The drift this guard exists to stop comes from KEYS bound to cursor and
/// mouse-button actions, which live in the keyboard maps, so narrowing the
/// suppression keeps the original fix and gives the mouse back.
///
/// Restoring matters more than disabling. Leaving the game's input disabled
/// after closing a dialog would be far worse than the bug being fixed.
///
/// AND RESTORING MEANS PER MAP. Switching every keyboard map back on is not
/// the same as putting them back: Rewired's normal idiom is to enable and
/// disable map CATEGORIES as the context changes, so a blanket enable hands
/// back maps the game had deliberately off. Each map's state is recorded
/// before it is touched and written back afterwards. That was a corner case
/// while this only ran with our own dialog open, and stopped being one when
/// suppression grew to cover the game being alt-tabbed away from - it now
/// runs during ordinary play, for as long as the player is elsewhere.
/// </summary>
public static class TypingGuard
{
    private static bool _suppressed;

    /// <summary>
    /// What the guard is allowed to switch off. Set from the mod config so a
    /// player can bisect a mouse or keyboard problem without a rebuild.
    ///
    /// It exists because the first attempt to narrow the suppression appeared
    /// not to work: droha's pointer still stuck in the connection pane. The
    /// real cause was the binding, not the theory - the lookup asked for a
    /// method that does not exist and silently fell back to disabling every
    /// map, mouse included, while reporting "keyboard-only". See TryBind.
    ///
    /// Keeping the switch is worth the few lines anyway. Off is the control
    /// case for any future report of this shape: if the mouse behaves with
    /// the guard disabled it is this class, and if it does not it is not.
    /// </summary>
    public enum Mode
    {
        /// <summary>Switch nothing off. The cursor will drift while typing.</summary>
        Off,

        /// <summary>Keyboard maps only. The default.</summary>
        Keyboard,

        /// <summary>Every map, for every player. The pre-0.3.2 behaviour.</summary>
        All,

        /// <summary>Leave Rewired alone; only unbind UI navigation actions.</summary>
        NavigationOnly,
    }

    public static Mode Suppression { get; set; } = Mode.Keyboard;

    /// <summary>
    /// Does this process own the foreground window?
    ///
    /// ASKED OF WINDOWS, NOT UNITY. Application.isFocused reported true with
    /// Notepad plainly in front - the focus-change log never fired once,
    /// which can only happen if the value never moved. Whether that is the
    /// interop or the player build hardly matters; the OS knows, and one
    /// P/Invoke is cheaper than trusting a property that has already lied.
    ///
    /// Fails OPEN. If the call cannot be made we report focused, because
    /// suppressing input on a game that is actually in front would be far
    /// worse than the bug being fixed.
    /// </summary>
    private static bool HasFocus()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return true;

            GetWindowThreadProcessId(window, out var owner);
            if (owner == 0) return true;

            if (_ourProcess == 0)
            {
                _ourProcess = (uint)System.Diagnostics.Process
                    .GetCurrentProcess().Id;
            }
            return owner == _ourProcess;
        }
        catch
        {
            return true;
        }
    }

    private static uint _ourProcess;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window, out uint processId);

    private static bool _modeReported;
    private static bool _wasUnfocused;
    private static int _flips;
    private static int _flipsReported;

    // The input module's action ids, saved so they can be put back.
    private static int _horizontal, _vertical, _submit, _cancel;
    private static Rewired.Integration.UnityUI.RewiredStandaloneInputModule? _module;

    public static bool Suppressed => _suppressed;

    /// <summary>
    /// Look Rewired up at startup rather than on the first keystroke.
    ///
    /// Resolving lazily meant a failure only showed after someone had already
    /// hit the bug, and the absence of the log line was the only clue. Now the
    /// log says on every launch whether the mouse-drift fix can work at all.
    /// </summary>
    public static void Probe()
    {
        if (!_mapsLookedUp) ResolveMapHelper();
    }

    public static void Suppress()
    {
        if (_suppressed) return;
        _suppressed = true;

        if (!_modeReported)
        {
            _modeReported = true;
            Say($"suppression mode is {Suppression}");
        }

        if (Suppression == Mode.Off)
        {
            Warn("suppression is OFF; the cursor will drift while typing");
            return;
        }

        if (Suppression != Mode.NavigationOnly) SetRewiredMapsEnabled(false);

        try
        {
            _module = UnityEngine.Object.FindObjectOfType<
                Rewired.Integration.UnityUI.RewiredStandaloneInputModule>();
            if (_module != null)
            {
                // -1 is not a real action, so navigation, submit and cancel
                // stop firing. Pointer input is untouched.
                _horizontal = _module.HorizontalActionId;
                _vertical = _module.VerticalActionId;
                _submit = _module.SubmitActionId;
                _cancel = _module.CancelActionId;

                _module.HorizontalActionId = -1;
                _module.VerticalActionId = -1;
                _module.SubmitActionId = -1;
                _module.CancelActionId = -1;
            }
        }
        catch (Exception e)
        {
            Warn($"could not unbind UI navigation: {e.Message}");
        }
    }

    /// <summary>
    /// Whether the guard is holding input down right now, for diagnostics.
    /// </summary>
    public static bool IsSuppressing => _suppressed;

    public static void Restore()
    {
        if (!_suppressed) return;
        _suppressed = false;

        SetRewiredMapsEnabled(true);

        try
        {
            if (_module != null)
            {
                _module.HorizontalActionId = _horizontal;
                _module.VerticalActionId = _vertical;
                _module.SubmitActionId = _submit;
                _module.CancelActionId = _cancel;
            }
        }
        catch (Exception e)
        {
            Warn($"COULD NOT restore UI navigation: {e.Message}");
        }
        _module = null;
    }

    /// <summary>
    /// ReInput.controllers.maps.SetAllMapsEnabled, reached by reflection.
    ///
    /// Il2CppInterop renames members unpredictably - the assembly exports
    /// get_controllers, get_Controllers and get_maps, so the generated
    /// property names cannot be relied on at compile time and binding to the
    /// wrong one is a build error rather than something that degrades.
    ///
    /// Resolved lazily and retried until it works - see SetRewiredMapsEnabled.
    /// Rewired is not initialised at plugin load, so an eager probe that cached
    /// the miss disabled the fix for the whole session. If it can never be
    /// found the pane still works and the mouse just drifts while typing, which
    /// is the bug this improves rather than a new one.
    /// </summary>
    /// <summary>Every player's map helper, with its SetAllMapsEnabled.</summary>
    private static readonly System.Collections.Generic.List<
        (object Helper, System.Reflection.MethodInfo Method, bool KeyboardOnly)>
        _mapHelpers = new();
    private static bool _mapsLookedUp;

    /// <summary>
    /// The maps switched off, and what each one's state WAS.
    ///
    /// This is the whole difference between a restore and a blanket enable.
    /// SetAllMapsEnabled(true, Keyboard) turns every keyboard map on, which
    /// is not what was there if the game had any of them deliberately off -
    /// and Rewired's normal idiom is exactly that, enabling and disabling map
    /// CATEGORIES as the context changes.
    ///
    /// It never mattered much while this only ran with a text box in our own
    /// dialog focused. Suppression now also covers the game being alt-tabbed
    /// away from, so it runs during ordinary play, for as long as the player
    /// is in another window. A wrong restore stopped being a corner case.
    /// </summary>
    private static readonly System.Collections.Generic.List<
        (Rewired.ControllerMap Map, bool WasEnabled)> _held = new();

    /// <summary>Whether the last suppression used the per-map path.</summary>
    private static bool _heldPrecisely;
    private static bool _heldReported;

    private static void SetRewiredMapsEnabled(bool enabled)
    {
        if (enabled) RestoreMaps();
        else SuppressMaps();
    }

    /// <summary>
    /// Switch off the keyboard maps, remembering each one's state first.
    /// </summary>
    private static void SuppressMaps()
    {
        try
        {
            // Retried until it works, NOT cached on failure. Rewired is not
            // initialised at plugin load, and an eager probe that cached the
            // miss disabled the fix for the whole session.
            if (!_mapsLookedUp) ResolveMapHelper();

            _held.Clear();
            _heldPrecisely = false;

            // Mode.All is the deliberate blunt instrument - a bisect setting,
            // not a normal one - so it keeps the blunt implementation.
            if (Suppression == Mode.All)
            {
                Blanket(false);
                return;
            }

            var alreadyOff = 0;
            foreach (var (helper, method, keyboardOnly) in _mapHelpers)
            {
                var maps = helper as Rewired.Player.ControllerHelper.MapHelper;
                if (maps == null || !keyboardOnly)
                {
                    // No typed handle on this one, so it can only be done the
                    // old way. Rare enough to be worth saying out loud.
                    Warn("Rewired: no typed map helper, falling back to "
                         + "switching every keyboard map off and on again");
                    Invoke(method, helper, keyboardOnly, false);
                    continue;
                }

                var found = new Il2CppSystem.Collections.Generic.List<
                    Rewired.ControllerMap>();
                maps.GetAllMaps(Rewired.ControllerType.Keyboard, found);

                for (int i = 0; i < found.Count; i++)
                {
                    var map = found[i];
                    if (map == null) continue;

                    var was = map.enabled;
                    if (!was) alreadyOff++;
                    _held.Add((map, was));
                    map.enabled = false;
                }
                _heldPrecisely = true;
            }

            // Said once so the log positively confirms the per-map path is
            // in use, rather than leaving it to be inferred from the absence
            // of the fallback warning - and said again whenever any map was
            // ALREADY off, because that is the case this path exists for and
            // the one a blanket restore would get wrong.
            if (_heldPrecisely && (!_heldReported || alreadyOff > 0))
            {
                _heldReported = true;
                Say($"Rewired: {_held.Count} keyboard map(s) held individually, "
                    + $"{alreadyOff} of them already off");
            }
        }
        catch (Exception e)
        {
            Warn($"could not disable Rewired maps: {e.Message}");
        }
    }

    /// <summary>
    /// Put every map back to the state it was actually in.
    /// </summary>
    private static void RestoreMaps()
    {
        try
        {
            if (!_heldPrecisely)
            {
                Blanket(true);
                return;
            }

            var restored = 0;
            foreach (var (map, was) in _held)
            {
                try
                {
                    if (map == null) continue;
                    map.enabled = was;
                    restored++;
                }
                catch
                {
                    // A controller unplugged while the game was in the
                    // background takes its maps with it. Nothing to put back,
                    // and no reason to abandon the others.
                }
            }

            _held.Clear();
            _heldPrecisely = false;
            if (restored == 0) Blanket(true);
        }
        catch (Exception e)
        {
            // Re-enabling failing is far worse than disabling failing: the
            // game would be left unable to take input. So this falls back to
            // the blunt version rather than leaving the keyboard dead.
            Warn($"COULD NOT RE-ENABLE Rewired maps: {e.Message}");
            try { Blanket(true); } catch { }
        }
    }

    /// <summary>
    /// SetAllMapsEnabled across every helper. The old behaviour, kept for
    /// Mode.All and as the fallback when there is no typed handle.
    /// </summary>
    private static void Blanket(bool enabled)
    {
        var wholeLot = Suppression == Mode.All;
        foreach (var (helper, method, keyboardOnly) in _mapHelpers)
        {
            if (keyboardOnly && wholeLot)
            {
                // Mode.All was asked for but this helper only offers the
                // narrow call. Sweep every controller type by hand.
                foreach (Rewired.ControllerType type in
                         Enum.GetValues(typeof(Rewired.ControllerType)))
                {
                    method.Invoke(helper, new object[] { enabled, type });
                }
                continue;
            }

            Invoke(method, helper, keyboardOnly, enabled);
        }
    }

    private static void Invoke(System.Reflection.MethodInfo method, object helper,
                               bool keyboardOnly, bool enabled)
    {
        if (keyboardOnly)
        {
            method.Invoke(helper,
                new object[] { enabled, Rewired.ControllerType.Keyboard });
        }
        else
        {
            method.Invoke(helper, new object[] { enabled });
        }
    }

    private static void ResolveMapHelper()
    {
        var reInput = typeof(Rewired.ReInput);

        // SetAllMapsEnabled is PER PLAYER in Rewired -
        // ReInput.players.GetPlayer(n).controllers.maps - not on the global
        // controller helper. Checked there first and it was not present.
        var players = Get(reInput, null, "players", "Players");
        if (players == null)
        {
            // Null until the game's InputManager starts, which is after plugin
            // load. Left unresolved so the next call retries.
            return;
        }

        // EVERY player, not just the first. Binding one helper would leave a
        // second player's maps still driving the cursor.
        _mapHelpers.Clear();
        foreach (var player in AllPlayers(players))
        {
            var controllers = Get(player.GetType(), player, "controllers", "Controllers");
            if (controllers == null) continue;

            foreach (var property in controllers.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0) continue;
                if (TryBind(SafeGet(() => property.GetValue(controllers)))) break;
            }
        }

        if (_mapHelpers.Count == 0)
        {
            Warn(
                "Rewired: no per-player map helper found; the mouse will drift while typing");
            return;
        }

        _mapsLookedUp = true;
        foreach (var (_, method, keyboardOnly) in _mapHelpers)
        {
            var ps = method.GetParameters();
            var sig = new System.Text.StringBuilder(method.Name).Append('(');
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sig.Append(", ");
                sig.Append(ps[i].ParameterType.Name);
            }
            // The SIGNATURE, not a summary. "keyboard-only" was printed for a
            // binding that disabled everything, and a label cannot be checked.
            Say($"Rewired: bound {sig.Append(')')} "
                + (keyboardOnly ? "- keyboard maps only" : "- ALL maps"));
        }
        Say($"Rewired maps reachable for {_mapHelpers.Count} player(s)");
    }

    /// <summary>
    /// Every Rewired player. Obfuscation means the collection is reached by
    /// shape rather than name: AllPlayers if it is there, otherwise GetPlayer
    /// over the first few ids, which covers the single-player case this game
    /// actually is.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<object> AllPlayers(object players)
    {
        var found = new System.Collections.Generic.List<object>();
        var type = players.GetType();

        // The collection, under any of the casings the interop might have
        // produced. Lowercase first: Rewired's own API is allPlayers.
        var all = Get(type, players, "allPlayers", "AllPlayers", "players", "Players");
        if (all is System.Collections.IEnumerable list)
        {
            foreach (var p in list)
            {
                if (p != null) found.Add(p);
            }
        }

        if (found.Count > 0) return found;

        // Fall back to asking HOW MANY there are and indexing that far.
        //
        // Never probe blindly: GetPlayer on an id that does not exist makes
        // Rewired print a multi-line ERROR with a full system dump, and an
        // earlier version that tried ids 0-3 filled the log with three of them
        // every time the dialog opened.
        var count = Get(type, players, "playerCount", "PlayerCount") as int?;
        if (count is null or < 1)
        {
            Warn("Rewired: could not determine the player count");
            return found;
        }

        var getPlayer = type.GetMethod("GetPlayer", new[] { typeof(int) });
        if (getPlayer == null) return found;

        for (int id = 0; id < count.Value; id++)
        {
            var p = SafeGet(() => getPlayer.Invoke(players, new object[] { id }));
            if (p != null) found.Add(p);
        }
        return found;
    }

    private static object? SafeGet(Func<object?> get)
    {
        try { return get(); } catch { return null; }
    }

    /// <summary>
    /// Bind the narrowest map switch this build of Rewired offers.
    ///
    /// SetMapsEnabled(bool, ControllerType) is preferred because switching off
    /// only the keyboard leaves the mouse source alive for the UI module - see
    /// the note at the top of this file for what happens when it does not.
    /// SetAllMapsEnabled is the fallback, and it is worth a warning: it fixes
    /// the cursor drift and reintroduces the stuck-pointer bug.
    /// </summary>
    private static bool TryBind(object? candidate)
    {
        if (candidate == null) return false;

        // SetAllMapsEnabled, not SetMapsEnabled. This helper has no
        // SetMapsEnabled(bool, ControllerType) at all - its two-argument
        // overloads take a category id or name, and the per-controller-type
        // switch is SetAllMapsEnabled(bool, ControllerType).
        //
        // Asking for the wrong name cost a whole round of testing: the lookup
        // found nothing, fell through to SetAllMapsEnabled(bool), and disabled
        // EVERY map including the mouse - while the log cheerfully reported
        // "1 of them keyboard-only". droha tested that build and the pointer
        // still stuck, which looked like the theory being wrong when it was
        // the binding being wrong.
        var narrow = candidate.GetType().GetMethod(
            "SetAllMapsEnabled",
            new[] { typeof(bool), typeof(Rewired.ControllerType) });
        if (narrow != null)
        {
            _mapHelpers.Add((candidate, narrow, true));
            return true;
        }

        var all = candidate.GetType().GetMethod(
            "SetAllMapsEnabled", new[] { typeof(bool) });
        if (all == null) return false;

        Warn("Rewired: no per-controller-type map switch; falling back to "
             + "disabling ALL maps, which leaves the mouse stuck while a text "
             + "box has focus");
        _mapHelpers.Add((candidate, all, false));
        return true;
    }

    private static object? Get(Type type, object? instance, params string[] names)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;

        foreach (var name in names)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                try
                {
                    var property = t.GetProperty(name, Flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        return property.GetValue(instance);
                    }
                    var field = t.GetField(name, Flags);
                    if (field != null) return field.GetValue(instance);
                }
                catch
                {
                    // A getter that throws is not an answer. Keep looking up
                    // the chain rather than failing the whole lookup.
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Keep suppression in step with whether a text box has focus, and move
    /// focus on Tab.
    ///
    /// Polled rather than event-driven because TMP_InputField's focus changes
    /// through several paths - clicking, Escape, the modal closing - and
    /// missing one would leave the game's input switched off.
    /// </summary>
    public static void Tick(Func<TMPro.TMP_InputField?> focused, Action focusNext)
    {
        var field = focused();

        // ALT-TABBED AWAY COUNTS TOO.
        //
        // The game keeps polling Rewired while it is in the background, and
        // its keys are bound to cursor and mouse-button actions - so typing
        // in another window walks the game's cursor across its own menus and
        // presses things. droha: "typing still causes the mouse to move while
        // not focused. It's really annoying as it often opens up the settings
        // page and sometimes changes settings."
        //
        // DevTools already sets ReInput.configuration
        // .ignoreInputWhenAppNotInFocus, and it demonstrably does not stop
        // this. It is also the wrong home: DevTools never ships, so a player
        // would have the bug with no way to reach the setting.
        //
        // Suppressing the same maps the typing guard suppresses is exactly
        // the right shape - keyboard only, restored the moment focus comes
        // back, and it leaves the mouse alone so clicking back into the game
        // behaves normally.
        var unfocused = !HasFocus();
        if (unfocused != _wasUnfocused)
        {
            _wasUnfocused = unfocused;
            // Said out loud because "nothing suppressed" and "the game still
            // thinks it is focused" look identical from outside.
            Trace(unfocused ? "the game lost focus" : "the game regained focus");
        }
        var wantsSuppression = field != null || unfocused;

        // COUNT THE TRANSITIONS. isFocused is polled, and if it flickers -
        // TMP clearing focus for a frame while the pointer is down, say -
        // this would disable and re-enable Rewired every couple of frames.
        // Rewired's mouse source would be torn down and rebuilt underneath
        // the UI module continuously, which is one of the few things that
        // could leave it convinced a button is still held.
        //
        // Cheap and bounded: two ints and a log line every 20 flips. If the
        // pane is open and this is silent, thrashing is ruled out and the
        // stuck pointer is somewhere else entirely.
        if (wantsSuppression && !_suppressed)
        {
            _flips++;
            Suppress();
        }
        else if (!wantsSuppression && _suppressed)
        {
            _flips++;
            Restore();
        }

        if (_flips >= _flipsReported + 20)
        {
            _flipsReported = _flips;
            Warn($"suppression has flipped {_flips} times - if the game has "
                 + "not been alt-tabbed that often, focus is flickering, "
                 + "which would keep resetting the input state underneath "
                 + "the mouse");
        }

        // Tab is a TEXT BOX thing. Reading it while merely unfocused would
        // move the caret in a pane nobody is looking at, in response to a Tab
        // pressed in another application.
        if (field == null) return;

        // Read from Unity's own key state, because Rewired is switched off at
        // this point and would report nothing.
        if (Input.GetKeyDown(KeyCode.Tab)) focusNext();
    }

    /// <summary>
    /// Where to report a problem. A delegate rather than a plugin's logger,
    /// which was the only thing tying this to one mod.
    /// </summary>
    public static Action<string>? OnWarning { get; set; }

    /// <summary>
    /// Routine commentary. Separate from OnWarning because this class only
    /// had the one channel, so "the game lost focus" - which happens every
    /// time you alt-tab - came out at WARNING level alongside things that
    /// actually need attention. droha: "why is the focus log a warning?"
    /// </summary>
    public static Action<string>? OnInfo { get; set; }

    /// <summary>
    /// Per-alt-tab chatter. Debug rather than info: it fires every time the
    /// window changes hands and tells a reader nothing they did not just do
    /// themselves. It earns its place only when the question is "did the
    /// guard notice?", which is a debugging question.
    /// </summary>
    public static Action<string>? OnDebug { get; set; }

    private static void Warn(string message) => OnWarning?.Invoke(message);

    private static void Say(string message)
        => (OnInfo ?? OnWarning)?.Invoke(message);

    private static void Trace(string message)
        => (OnDebug ?? OnInfo ?? OnWarning)?.Invoke(message);
}
