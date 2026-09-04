using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// The cat trap: your tidy puzzle, untidied.
///
/// The game has a cat that swipes things off a surface, but only a handful of
/// levels contain one - CatSwipe and CatPaw are per-level objects, not a system
/// - so a trap that relied on it would do nothing on most of the run. This
/// scatters the puzzle ourselves, which works everywhere.
///
/// It UNDOES the puzzle rather than rearranging it, by calling the game's own
/// ObjectController.Reset on each group. Two cleverer versions were tried and
/// both broke puzzles:
///
/// - displacing objects by a random offset took pieces off the line, shelf or
///   grid they belong to, and in a puzzle whose only move is reordering along
///   an axis there was then no way to put them back;
/// - swapping positions among the objects themselves looked safe, and is not:
///   Popcorn arranges its pieces on THREE separate lines, so a swap across
///   lines leaves an arrangement the puzzle can never accept.
///
/// Reset is the game's own answer to "put this back how it started", so it
/// cannot produce a state the level does not understand. Losing progress IS the
/// trap; making a puzzle unsolvable is a bug.
///
/// What it deliberately does NOT touch:
///
/// - objects whose controller is ability-locked. They are dimmed and the player
///   cannot put them back, so moving them would be permanent.
/// - anything at all when no level is running.
/// </summary>
internal static class Traps
{
    private static int _applied;

    /// <summary>
    /// Where every object stood when the level opened.
    ///
    /// ObjectController.Reset does NOT move anything - measured, objects stay
    /// exactly where they were put - so a trap built on it undid nothing while
    /// cheerfully announcing that a cat had been through. Restoring the opening
    /// layout is the only thing that actually undoes a puzzle, and it is safe by
    /// construction: it is a state the level itself produced.
    ///
    /// Taken once per level. Levels are rebuilt from scratch on every entry, so
    /// what is on screen at load IS the base state.
    /// </summary>
    private static readonly List<(LevelObject Obj, Vector3 Pos, Quaternion Rot)> _opening = new();

    private static int _snapshotOf;

    /// <summary>Counts traps that actually scattered something.</summary>
    internal static int Sprung { get; private set; }

    /// <summary>Traps that arrived with no puzzle open, and so missed.</summary>
    internal static int Missed { get; private set; }

    internal static void Reset()
    {
        _applied = 0;
        _opening.Clear();
        _snapshotOf = 0;
    }

    /// <summary>
    /// Spring any traps that have arrived but not yet gone off.
    ///
    /// Counted rather than reacted to, like every other item: Archipelago
    /// replays the list on reconnect, and a trap that fired again on every
    /// login would be a nasty surprise for someone with a flaky connection.
    /// </summary>
    internal static void Tick()
    {
        RememberOpeningLayout();

        var owed = Inventory.TrapsReceived - _applied;
        if (owed <= 0) return;

        // A trap that arrives outside a puzzle MISSES, rather than waiting.
        //
        // Holding one gains nothing: every level is rebuilt from scratch when
        // it is opened - measured, a new Level instance each time, for normal
        // and generator levels alike - so a trap springing as you walk in undoes
        // work that the game had already discarded. All it would do is announce
        // a setback that did not happen.
        var level = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level;
        if (level == null || level.allLevelObjects == null)
        {
            _applied += owed;
            Missed += owed;
            Plugin.Logger.LogInfo($"trap: {owed} cat(s) found nothing to knock over");
            return;
        }

        for (int i = 0; i < owed; i++)
        {
            _applied++;
            Spring(level);
        }
    }

    /// <summary>Snapshot the layout the moment a new level is up.</summary>
    private static void RememberOpeningLayout()
    {
        try
        {
            var level = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level;
            if (level == null || level.allLevelObjects == null)
            {
                _snapshotOf = 0;
                return;
            }

            var id = level.GetInstanceID();
            if (id == _snapshotOf) return;

            _snapshotOf = id;
            _opening.Clear();

            var objects = level.allLevelObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null) continue;
                _opening.Add((obj, obj.transform.localPosition, obj.transform.localRotation));
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: could not record the layout: {e.Message}");
        }
    }

    private static void Spring(Level level)
    {
        try
        {
            var abilities = Inventory.Abilities;
            var controllers = level.objectControllers;

            // Put everything back where the level started it. This is what
            // actually undoes the puzzle; the controller Reset below only
            // clears the solved flags so the level agrees with the screen.
            int moved = 0;
            foreach (var (obj, pos, rot) in _opening)
            {
                try
                {
                    if (obj == null) continue;
                    obj.transform.localPosition = pos;
                    obj.transform.localRotation = rot;
                    obj.SetPlaced(false);
                    moved++;
                }
                catch
                {
                    // One awkward object must not abandon the rest.
                }
            }

            int reset = 0;
            for (int i = 0; i < (controllers == null ? 0 : controllers.Count); i++)
            {
                var controller = controllers![i];
                if (controller == null) continue;

                // A locked group is dimmed and unsolvable, so undoing it would
                // take away work the player cannot redo.
                var cls = controller.GetIl2CppType()?.Name ?? "";
                if (abilities != null && abilities.LocksEnabled
                    && abilities.IsClassLocked(cls)) continue;

                try
                {
                    controller.Reset();
                    reset++;
                }
                catch
                {
                    // One awkward controller must not abandon the rest.
                }
            }

            Sprung++;
            Plugin.Logger.LogInfo(
                $"trap: the cat put back {moved} object(s) and undid {reset} group(s)");
            Toasts.Show("A cat has been through your puzzle", Toasts.Notice);
            Toasts.SweepPaw();
            PlayCatSound();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: could not spring: {e.Message}");
        }
    }

    private static AudioClip? _catSound;
    private static bool _catSoundSearched;

    /// <summary>
    /// The game's own cat noise, if this level happens to have one.
    ///
    /// Sourced from a CatSwipe in the scene rather than shipped: only some
    /// levels have one, so most of the time the trap is silent. That is the
    /// documented degradation - the scatter is the trap, the sound is a bonus
    /// on the levels that can provide it.
    /// </summary>
    private static void PlayCatSound()
    {
        try
        {
            if (!_catSoundSearched)
            {
                _catSoundSearched = true;
                var swipe = UnityEngine.Object.FindObjectOfType<CatSwipe>();
                if (swipe != null) _catSound = swipe.catSound;
            }

            if (_catSound == null) return;

            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (listener == null) return;

            AudioSource.PlayClipAtPoint(_catSound, listener.transform.position);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: no cat noise: {e.Message}");
        }
    }
}
