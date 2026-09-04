using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ALTTLArchipelago;

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
/// So while a field has focus, ALL Rewired maps are switched off and the input
/// module's navigation actions are unbound. The mouse still works - it is
/// Unity's pointer handling that clicks the buttons, not Rewired's maps - and
/// everything is put back exactly as it was on the way out.
///
/// Restoring matters more than disabling. Leaving the game's input disabled
/// after closing a dialog would be far worse than the bug being fixed.
/// </summary>
internal static class TypingGuard
{
    private static bool _suppressed;

    // The input module's action ids, saved so they can be put back.
    private static int _horizontal, _vertical, _submit, _cancel;
    private static Rewired.Integration.UnityUI.RewiredStandaloneInputModule? _module;

    internal static bool Suppressed => _suppressed;

    /// <summary>
    /// Look Rewired up at startup rather than on the first keystroke.
    ///
    /// Resolving lazily meant a failure only showed after someone had already
    /// hit the bug, and the absence of the log line was the only clue. Now the
    /// log says on every launch whether the mouse-drift fix can work at all.
    /// </summary>
    internal static void Probe()
    {
        if (!_mapsLookedUp) ResolveMapHelper();
    }

    internal static void Suppress()
    {
        if (_suppressed) return;
        _suppressed = true;

        SetRewiredMapsEnabled(false);

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
            Plugin.Logger.LogWarning($"could not unbind UI navigation: {e.Message}");
        }
    }

    internal static void Restore()
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
            Plugin.Logger.LogError($"COULD NOT restore UI navigation: {e.Message}");
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
        (object Helper, System.Reflection.MethodInfo Method)> _mapHelpers = new();
    private static bool _mapsLookedUp;

    private static void SetRewiredMapsEnabled(bool enabled)
    {
        try
        {
            // Retried until it works, NOT cached on failure. Rewired is not
            // initialised at plugin load, and an eager probe that cached the
            // miss disabled the fix for the whole session.
            if (!_mapsLookedUp) ResolveMapHelper();

            foreach (var (helper, method) in _mapHelpers)
            {
                method.Invoke(helper, new object[] { enabled });
            }
        }
        catch (Exception e)
        {
            // Re-enabling failing is far worse than disabling failing: the
            // game would be left unable to take input.
            if (enabled) Plugin.Logger.LogError($"COULD NOT RE-ENABLE Rewired maps: {e.Message}");
            else Plugin.Logger.LogWarning($"could not disable Rewired maps: {e.Message}");
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
            Plugin.Logger.LogWarning(
                "Rewired: no per-player map helper found; the mouse will drift while typing");
            return;
        }

        _mapsLookedUp = true;
        Plugin.Logger.LogInfo($"Rewired maps reachable for {_mapHelpers.Count} player(s)");
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
            Plugin.Logger.LogWarning("Rewired: could not determine the player count");
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

    private static bool TryBind(object? candidate)
    {
        if (candidate == null) return false;

        var method = candidate.GetType().GetMethod(
            "SetAllMapsEnabled", new[] { typeof(bool) });
        if (method == null) return false;

        _mapHelpers.Add((candidate, method));
        return true;
    }

    private static object? Get(Type type, object? instance, params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name);
            if (property != null) return property.GetValue(instance);
            var field = type.GetField(name);
            if (field != null) return field.GetValue(instance);
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
    internal static void Tick(Func<TMPro.TMP_InputField?> focused, Action focusNext)
    {
        var field = focused();
        var wantsSuppression = field != null;

        if (wantsSuppression && !_suppressed) Suppress();
        else if (!wantsSuppression && _suppressed) Restore();

        if (!wantsSuppression) return;

        // Tab moves to the next box. Read from Unity's own key state, because
        // Rewired is switched off at this point and would report nothing.
        if (Input.GetKeyDown(KeyCode.Tab)) focusNext();
    }
}
