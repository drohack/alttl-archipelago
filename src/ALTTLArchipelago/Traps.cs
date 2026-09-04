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

    /// <summary>Counts traps that actually scattered something.</summary>
    internal static int Sprung { get; private set; }

    internal static void Reset()
    {
        _applied = 0;
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
        var owed = Inventory.TrapsReceived - _applied;
        if (owed <= 0) return;

        // Only while a puzzle is actually open. A trap that arrives in a menu
        // waits for something to scatter rather than being wasted.
        var level = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level;
        if (level == null || level.allLevelObjects == null) return;

        for (int i = 0; i < owed; i++)
        {
            _applied++;
            Spring(level);
        }
    }

    private static void Spring(Level level)
    {
        try
        {
            var abilities = Inventory.Abilities;
            var controllers = level.objectControllers;

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
            Plugin.Logger.LogInfo($"trap: the cat undid {reset} group(s)");
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
