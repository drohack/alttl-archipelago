using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Objects you have not been given the mechanic for, made unmovable and dim.
///
/// The gate is per CONTROLLER CLASS, not per object: the generator's logic says
/// "this group needs Swapping", and the class of the controller managing those
/// objects is what Swapping unlocks. So the loop asks AbilityState one question
/// per controller and applies the answer to everything it manages.
///
/// Re-applied rather than toggled. Every pass sets the full state of every
/// object it touches, so an ability arriving mid-level takes effect on the next
/// pass without needing to know what changed - and running twice cannot leave
/// something half-locked.
///
/// It FAILS OPEN throughout. A class the catalogue does not recognise is left
/// alone, and any error abandons the pass with everything movable. Locking too
/// little means a player can move something logic assumed they could not, which
/// is untidy. Locking too much means a puzzle cannot be finished, and there is
/// nothing on screen to explain why.
/// </summary>
internal static class Abilities
{
    /// <summary>Dim grey at partial alpha, the shade S3 confirmed reads as "not yet".</summary>
    private static readonly Color Locked = new(0.55f, 0.55f, 0.55f, 0.6f);

    private static float _sincePass;

    /// <summary>
    /// Each renderer's colour before we ever touched it, by instance id.
    ///
    /// Restoring to white would be a guess, and a wrong one on any level whose
    /// sprites are tinted by design - those objects would come back bleached
    /// the moment their ability arrived, with nothing to say why. The dimming
    /// probe this was ported from restored to white because it only ever ran
    /// on one level, by hand, for a few seconds.
    /// </summary>
    private static readonly Dictionary<int, Color> _original = new();

    /// <summary>
    /// What the last pass concluded, so the log speaks only when it changes.
    /// A line every second would bury everything else.
    /// </summary>
    private static string _lastSummary = "";


    internal static void Reset()
    {
        _lastSummary = "";
        _sincePass = 0f;
        // Colours and class names belong to objects that are gone; ids get
        // reused, so a stale entry would answer for the wrong object.
        _original.Clear();
        _classes.Clear();
    }

    /// <summary>
    /// A slow poll rather than a hook on level load.
    ///
    /// Controllers self-register in their own Start, so there is no single
    /// moment that is reliably after all of them - and an ability can arrive at
    /// any time while a level is open. Polling covers both without needing to
    /// know which happened.
    /// </summary>
    internal static void Tick(float dt)
    {
        var state = Inventory.Abilities;
        if (state == null || !state.LocksEnabled) return;

        _sincePass += dt;
        if (_sincePass < 1f) return;
        _sincePass = 0f;

        Apply(state);
    }

    private static void Apply(AbilityState state)
    {
        try
        {
            var level = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level;
            var controllers = level?.objectControllers;
            if (controllers == null || controllers.Count == 0)
            {
                _lastSummary = "";       // next level starts quiet
                return;
            }

            int locked = 0, unlocked = 0, objects = 0;
            var missing = new SortedSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < controllers.Count; i++)
            {
                var controller = controllers[i];
                if (controller == null) continue;

                var cls = ClassOf(controller);
                var isLocked = state.IsClassLocked(cls);

                if (isLocked)
                {
                    locked++;
                    var ability = state.AbilityFor(cls);
                    if (!string.IsNullOrEmpty(ability)) missing.Add(ability!);
                }
                else
                {
                    unlocked++;
                }

                objects += SetControllerLocked(controller, isLocked);
            }

            var summary = $"{locked} locked, {unlocked} open, {objects} objects"
                + (missing.Count > 0 ? $", waiting on {string.Join(", ", missing)}" : "");
            if (summary == _lastSummary) return;

            _lastSummary = summary;
            Plugin.Logger.LogInfo($"abilities: {summary}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: pass failed, leaving everything movable: {e.Message}");
        }
    }

    /// <summary>Apply one controller's state to every object it manages.</summary>
    private static int SetControllerLocked(ObjectController controller, bool isLocked)
    {
        var managed = controller.ManagedObjects;
        if (managed == null) return 0;

        int touched = 0;
        for (int i = 0; i < managed.Count; i++)
        {
            var obj = managed[i];
            if (obj == null) continue;
            try
            {
                obj.SetInteractable(!isLocked);
                obj.SetPreventSelection(isLocked);
                Tint(obj, isLocked);
                touched++;
            }
            catch
            {
                // One awkward object must not abandon the rest of the level.
            }
        }
        return touched;
    }

    /// <summary>
    /// The controller's class name, resolved once per object.
    ///
    /// GetIl2CppType().Name is an interop type resolution plus a native string
    /// marshal, and this pass asked it for every controller every second for a
    /// value that is fixed for the object's lifetime. Keyed by instance id, and
    /// cleared with the rest of the state when a run ends.
    /// </summary>
    private static readonly Dictionary<int, string> _classes = new();

    private static string ClassOf(ObjectController controller)
    {
        var id = controller.GetInstanceID();
        if (_classes.TryGetValue(id, out var known)) return known;

        var cls = controller.GetIl2CppType()?.Name ?? "";

        // Levels are rebuilt constantly - every entry, and every cat trap - so
        // without a bound this grows for as long as the game is open.
        if (_classes.Count > 512) _classes.Clear();
        _classes[id] = cls;
        return cls;
    }

    private static void Tint(LevelObject obj, bool isLocked)
    {
        Paint(obj.renderer, isLocked);

        var subs = obj.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            Paint(subs![i], isLocked);
        }
    }

    private static void Paint(SpriteRenderer? renderer, bool isLocked)
    {
        if (renderer == null) return;

        var id = renderer.GetInstanceID();
        if (!_original.TryGetValue(id, out var was))
        {
            // First sighting. Whatever it looks like now IS its own colour -
            // this runs before we have changed anything about it.
            was = renderer.color;
            _original[id] = was;
        }

        var want = isLocked ? Locked : was;

        // Only when it is actually changing. This pass re-asserts every object
        // every second by design, so on a settled level every one of these was
        // a redundant write into IL2CPP - a few hundred a second on a level
        // where nothing had moved.
        if (renderer.color == want) return;
        renderer.color = want;
    }
}
