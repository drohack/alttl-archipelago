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
/// ResetLevel - the same code behind the pause menu's Reset button. Three
/// cleverer versions were tried and all three broke puzzles:
///
/// - displacing objects by a random offset took pieces off the line, shelf or
///   grid they belong to, and in a puzzle whose only move is reordering along
///   an axis there was then no way to put them back;
/// - swapping positions among the objects themselves looked safe, and is not:
///   Popcorn arranges its pieces on THREE separate lines, so a swap across
///   lines leaves an arrangement the puzzle can never accept;
/// - restoring each object's opening transform and parent, which is the one
///   that looks obviously correct and is the most misleading. A piece is not
///   merely SOMEWHERE, it is IN something - stuck to a surface, in a drawer, in
///   a grid cell, nested, or on a stack - and LevelObject exposes none of those
///   links (probed: no RemoveFromSurface, no attachedToObject, no
///   m_originalParent on it). Pieces went back to the right spot still attached
///   to the envelope they had been posted into, and dragging the envelope
///   dragged them with it.
///
/// ResetLevel knows every one of those mechanisms because it IS the game's, so
/// it cannot produce a state the level does not understand. Losing progress is
/// the trap; making a puzzle unsolvable is a bug.
///
/// What it deliberately does NOT touch:
///
/// - objects whose controller is ability-locked. They are dimmed and the player
///   cannot put them back, so moving them would be permanent.
/// - anything at all when no level is running.
/// </summary>
internal static class Traps
{
    /// <summary>
    /// Traps already accounted for, INCLUDING ones from previous sessions.
    ///
    /// Seeded from the run state rather than starting at zero: the item list is
    /// replayed in full on every reconnect, so a counter that began at zero
    /// re-fired every cat in the run's history each time you logged in.
    /// </summary>
    private static int _applied;


    internal static void Reset()
    {
        _applied = RunState.TrapsSprung;
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
            RunState.SpendTrap(owed);
            Plugin.Logger.LogInfo($"trap: {owed} cat(s) found nothing to knock over");
            return;
        }

        // One reset covers any number of cats: the puzzle can only go back to
        // its opening state once, and resetting N times in a row would just
        // replay the animation into an already-reset level.
        _applied += owed;
        RunState.SpendTrap(owed);
        Spring(owed);
    }

    /// <summary>
    /// Undo the puzzle with the game's own reset.
    ///
    /// The paw is ours and lives on our own overlay canvas rather than in the
    /// level, so the reset underneath it cannot destroy it mid-swipe.
    ///
    /// The animation is played BEFORE the reset for the same reason a cat is
    /// startling: the swipe should be what the player sees happen to the
    /// puzzle, not an explanation offered afterwards.
    /// </summary>
    private static void Spring(int cats)
    {
        try
        {
            Toasts.Show("A cat has been through your puzzle", Toasts.Notice);
            Toasts.SweepPaw();
            PlayCatSound();

            var manager = GameManager.Instance?.levelManager;
            if (manager == null)
            {
                // The animation has already played, so say plainly that the
                // puzzle survived rather than leaving a silent mismatch between
                // what was shown and what happened.
                Plugin.Logger.LogWarning(
                    "trap: no level manager, so the cat only made noise");
                return;
            }

            manager.ResetLevel();
            Plugin.Logger.LogInfo($"trap: {cats} cat(s) reset the puzzle");
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
