using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using UnityEngine;
using UnityEngine.UI;

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
        _completedAt = 0f;
    }

    /// <summary>
    /// When the last level completed, so a trap landing on the way out can be
    /// told from one landing mid-puzzle. See Tick.
    /// </summary>
    private static float _completedAt;

    /// <summary>
    /// How long after a completion a trap still counts as too late.
    ///
    /// Generous on purpose: the window that matters is the level teardown and
    /// the load of the next one, which is not instant, and a trap arriving a
    /// second into the new puzzle has nothing to undo there either.
    /// </summary>
    private const float CompletionGrace = 3f;

    /// <summary>Told by Checks when a level finishes.</summary>
    internal static void NoteCompletion() => _completedAt = Time.unscaledTime;

    /// <summary>
    /// Spring any traps that have arrived but not yet gone off.
    ///
    /// Counted rather than reacted to, like every other item: Archipelago
    /// replays the list on reconnect, and a trap that fired again on every
    /// login would be a nasty surprise for someone with a flaky connection.
    /// </summary>
    internal static void Tick(float dt)
    {
        TickPaw(dt);

        var owed = Inventory.TrapsReceived - _applied;
        if (owed <= 0) return;

        // A trap that arrives outside a puzzle MISSES, rather than waiting.
        //
        // Holding one gains nothing: every level is rebuilt from scratch when
        // it is opened - measured, a new Level instance each time, for normal
        // and generator levels alike - so a trap springing as you walk in undoes
        // work that the game had already discarded. All it would do is announce
        // a setback that did not happen.
        var active = GameManager.Instance?.levelManager?.ActiveLevelInterface;

        // A LEVEL STILL LOADING IS NOT ONE TO KNOCK OVER.
        //
        // THE THEORY, WHICH IS NOT PROVEN - read docs/data/trap-freeze-repro.md
        // before trusting it. 29 attempts across four builds, including one
        // reduced to exactly 0.3.1's Spring, failed to reproduce droha's
        // hang; in 7 of the last 8 the trap fired squarely inside the
        // post-completion navigation and the game carried on. So what follows
        // explains the shape of the guard, not a demonstrated cause.
        //
        // ResetLevel re-enters a load that has not finished. The level load
        // is an async state machine, and ActiveLevelInterface.Level is
        // already set partway through it - so a trap ticking mid-load finds
        // what looks like a perfectly good puzzle and tells the game to
        // rebuild it underneath itself. SetActiveLevel never returns. No
        // exception, no error: the game simply stops, and clicking does
        // nothing.
        //
        // droha's log is consistent with it, on the RELEASED 0.3.1 build:
        // "when finishing a level I got a background change trap... it reset
        // and when I clicked anywhere the game fully froze. Had to alt+F4."
        // The window is right there - "track: slot 53 ... launching with
        // seed 307681145, forceReload" and then, on the very next line,
        // "trap: 1 cat(s) reset the puzzle" - with not one exception in all
        // 1,549 lines. Consistent with is not the same as caused by, and the
        // reproduction went looking for the difference and did not find it.
        //
        // It is NOT the earlier cat trap freeze, which drowned in
        // NullReferenceExceptions from tweens holding destroyed pieces. 0.3.1
        // predates every one of those guards: it has no animation cancel to
        // blame and no completion grace to have caught this.
        //
        // WHAT THE GUARD IS ACTUALLY WORTH, measured rather than argued
        // (tools/probe-trap-window.py): a trap arriving mid-load is HELD and
        // springs once the level settles, where before it hit `level == null`
        // and was silently spent. It cannot strand a trap on the level
        // select, because ActiveLevelInterface is null there and this test
        // never runs. Both verified in game.
        //
        // The completion grace below would not cover it either, even now: it
        // measures time since the last completion, and a level that reloads
        // for any other reason is not a completion. Ask the level what it is
        // doing instead of inferring it from a clock.
        if (active != null && (!active.LevelIsLoaded || active.IsTransitioning))
        {
            // HELD, NOT SPENT. Unlike a trap with no puzzle to hit, this one
            // has a target - it just cannot be applied safely this frame. The
            // next tick will find the level settled and spring it properly.
            return;
        }

        var level = active?.Level;
        if (level == null || level.allLevelObjects == null)
        {
            _applied += owed;
            RunState.SpendTrap(owed);
            Plugin.Logger.LogInfo($"trap: {owed} cat(s) found nothing to knock over");
            return;
        }

        // A PUZZLE YOU HAVE JUST FINISHED IS NOT ONE TO KNOCK OVER.
        //
        // Completing a level starts the move to the next slot. Resetting it
        // in that window relaunches the level that was on its way out, which
        // throws the pending navigation away - so the run sits on a reset
        // copy of the puzzle it just solved and never advances.
        //
        // droha: "the cat trap went off, but it didn't go to the next level.
        // It did some of the animation... but never moved to the next level
        // like it normally does." The log shows exactly that - navigation
        // queued slot 1, then the trap relaunched slot 0 on top of it.
        //
        // Treated as a miss for the same reason a trap outside a puzzle is:
        // there is no work left to undo, so springing announces a setback
        // that did not happen.
        var since = Time.unscaledTime - _completedAt;
        if (_completedAt > 0f && since < CompletionGrace)
        {
            _applied += owed;
            RunState.SpendTrap(owed);
            Plugin.Logger.LogInfo(
                $"trap: {owed} cat(s) arrived {since:0.0}s after the puzzle was "
                + "finished, too late to knock anything over");
            return;
        }

        // One reset covers any number of cats: the puzzle can only go back to
        // its opening state once, and resetting N times in a row would just
        // replay the animation into an already-reset level.
        _applied += owed;
        RunState.SpendTrap(owed);
        Spring(owed, level);
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
    private static void Spring(int cats, Level level)
    {
        try
        {
            Toasts.Show("A cat has been through your puzzle", Toasts.Notice);
            SweepPaw();
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

            // STOP THE ANIMATIONS FIRST, or the reset is a time bomb.
            //
            // DragObject.ObjectPlaced starts a LeanTween tween to settle a
            // piece, and that tween holds a closure over the object. Resetting
            // the level destroys the object while the tween is still
            // registered, so LeanTween goes on calling the callback every
            // frame against a dead reference - forever.
            //
            // droha hit this as the game freezing after a cat trap. It is not
            // frozen: Player.log had 6,583 identical
            // NullReferenceExceptions in DragObject+<>c__DisplayClass94_0
            // .<ObjectPlaced>b__1, thrown from LeanTween.update, and the
            // exception storm is what makes it unresponsive. It needs a
            // trap to land in the window between placing a piece and the
            // tween finishing, which is why it was rare and why the first
            // report had no log left to read.
            //
            // The game never hits this because nothing in the game resets a
            // level mid-animation. The trap does, so the trap cleans up.
            CancelAnimations(level);

            manager.ResetLevel();

            // The reset puts every object back to its own colour, including
            // the ones an ability lock had dimmed. Waiting for the once-a-
            // second pass to re-dim them shows the player a puzzle they
            // cannot actually touch, fully lit, for up to a second.
            Abilities.HoldDim();

            Plugin.Logger.LogInfo($"trap: {cats} cat(s) reset the puzzle");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: could not spring: {e.Message}");
        }
    }

    /// <summary>
    /// Cancel the PIECES' animations before the level under them is torn down.
    ///
    /// Dropping a piece starts a LeanTween settle tween whose callback holds
    /// the piece. Resetting the level destroys the piece while the tween is
    /// still registered, so LeanTween calls a dead reference every frame -
    /// droha's freeze, 6,583 NullReferenceExceptions in
    /// DragObject+&lt;&gt;c__DisplayClass94_0.&lt;ObjectPlaced&gt;b__1.
    ///
    /// PER OBJECT, NOT cancelAll(). The first fix cancelled every tween in
    /// the game and stopped the exceptions - and hung it instead. The level
    /// load is an async state machine that AWAITS its own tweens, so killing
    /// those means OnLevelInitComplete never runs and SetActiveLevel never
    /// returns. droha again, with the tell that made it obvious: "no error
    /// this time", and Player.log ending inside
    /// &lt;SetActiveLevel&gt;d__48.MoveNext.
    ///
    /// So only the level's own objects, which are the ones about to be
    /// destroyed and the only ones whose tweens can be left dangling.
    /// </summary>
    private static void CancelAnimations(Level level)
    {
        try
        {
            var objects = level.allLevelObjects;
            if (objects == null) return;

            var seen = new HashSet<int>();
            var cancelled = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null) continue;

                Cancel(obj.gameObject, seen, ref cancelled);
                var renderer = obj.renderer;
                if (renderer != null) Cancel(renderer.gameObject, seen, ref cancelled);
            }

            // AND THE DETACHED TWEENS, which are the ones that matter.
            //
            // Sweeping the level's objects was not enough, and neither was
            // sweeping every DragObject after it: 14 objects cancelled still
            // left 4,638 exceptions, and all seven DragObjects still left
            // 2,139. The settle tween is not attached to any of them - see
            // CancelDetached for where it actually lives.
            var detached = CancelDetached(seen, ref cancelled);

            if (cancelled > 0)
            {
                // The detached count is called out separately: zero there
                // means the sweep found nothing to cancel, which looks exactly
                // like a sweep that ran and missed. It is the number that
                // proved the fix - three traps, one detached tween each,
                // zero exceptions where there had been 6,583.
                Plugin.Logger.LogInfo(
                    $"trap: stopped animations on {cancelled} object(s), "
                    + $"{detached} of them detached, before the reset");
            }
        }
        catch (Exception e)
        {
            // Better a stuck animation than no reset at all - the paw swipe
            // has already played by now.
            Plugin.Logger.LogWarning($"trap: could not cancel animations: {e.Message}");
        }
    }

    /// <summary>
    /// Cancel the DETACHED value tweens - the ones that actually crash.
    ///
    /// DragObject.ObjectPlaced animates a dropped piece with
    /// LeanTween.value(...), which has no GameObject of its own, so LeanTween
    /// parks it on an internal holder called "~LeanTween". The callback
    /// closes over the piece; the reset destroys the piece; the tween lives
    /// on and throws every frame.
    ///
    /// THIS IS WHY THREE TARGETED CANCELS MISSED. The LevelObject, its sprite
    /// renderer and all seven DragObjects were cancelled and the storm came
    /// back every time, because the tween's transform was never any of them.
    /// The dump settled it - seven live tweens on '~LeanTween', one per fly.
    ///
    /// And it is why cancelAll() hung the game instead: the same list holds
    /// tweens on 'Main Camera' and 'Completion Stars', which the level load
    /// awaits. Those are left alone here.
    /// </summary>
    private static int CancelDetached(HashSet<int> seen, ref int cancelled)
    {
        var found = 0;
        try
        {
            var all = LeanTween.tweens;
            if (all == null) return 0;

            var limit = Math.Min(all.Length, LeanTween.tweenMaxSearch + 1);
            for (int i = 0; i < limit; i++)
            {
                var d = all[i];
                if (d == null || !d.toggle) continue;

                var t = d.trans;
                // A detached tween has no meaningful transform of its own:
                // either none at all, or LeanTween's own holder object.
                var isDetached = t == null
                    || t.name.StartsWith("~LeanTween", StringComparison.Ordinal);
                if (!isDetached) continue;

                found++;
                if (t == null) continue;
                Cancel(t.gameObject, seen, ref cancelled);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning(
                $"trap: could not cancel the detached tweens: {e.Message}");
        }
        return found;
    }

    private static void Cancel(GameObject? go, HashSet<int> seen, ref int cancelled)
    {
        if (go == null) return;
        if (!seen.Add(go.GetInstanceID())) return;

        try
        {
            LeanTween.cancel(go);
            cancelled++;
        }
        catch
        {
            // One object refusing is not a reason to leave the rest running.
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
            // RE-SEARCH WHILE THE CLIP IS STILL NULL. The latch used to be
            // set on the first attempt whatever the outcome, so the whole
            // session's cat sound was decided by whichever level happened to
            // be open when the first trap sprang - and a level with no cat in
            // it settled the question permanently.
            //
            // That was nearly invisible until 2026-09-09, because 11 of the 13
            // levels carrying a CatSwipe are campaign levels and campaign
            // levels almost never appeared in a run. With base_weight they do.
            //
            // Still latched once found: the clip is shared, so a successful
            // search never needs repeating.
            if (!_catSoundSearched || _catSound == null)
            {
                var swipe = UnityEngine.Object.FindObjectOfType<CatSwipe>();
                if (swipe != null)
                {
                    _catSound = swipe.catSound;
                    _catSoundSearched = true;
                }
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

    // ---- the cat, which is not a toast -------------------------------------
    //
    // This lived in the toast overlay because it borrows its canvas, which made
    // a general notification system carry a cat animation and one IL2CPP
    // dependency it otherwise had no need of. It draws onto Toasts.OverlayRoot
    // instead: still one canvas, still the right sort order, no cat in the kit.

    private static GameObject? _paw;
    private static float _pawAge;

    /// <summary>How long the swipe takes, end to end.</summary>
    private const float SwipeSeconds = 0.7f;

    /// <summary>
    /// Sweep a cat paw across the screen.
    ///
    /// Ours, on our own canvas, rather than the game's CatPaw: that is a
    /// per-level object which most levels simply do not have, so a trap relying
    /// on it showed nothing at all on the level where it fired. The point is to
    /// SEE something go through, not to reproduce the original animation.
    /// </summary>
    internal static void SweepPaw()
    {
        try
        {
            var root = Toasts.OverlayRoot;
            if (root == null) return;
            if (_paw != null) UnityEngine.Object.Destroy(_paw);

            _paw = new GameObject("catpaw");
            _paw.transform.SetParent(root, false);

            var rect = _paw.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(320f, 320f);

            var image = _paw.AddComponent<Image>();
            image.sprite = FindPawSprite();
            image.color = image.sprite == null
                ? new Color(0.10f, 0.10f, 0.12f, 0.9f)   // silhouette
                : Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;

            _pawAge = 0f;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: no paw: {e.Message}");
        }
    }

    private static void TickPaw(float dt)
    {
        if (_paw == null) return;

        _pawAge += dt;
        if (_pawAge >= SwipeSeconds)
        {
            UnityEngine.Object.Destroy(_paw);
            _paw = null;
            return;
        }

        var t = _pawAge / SwipeSeconds;
        var rect = _paw.GetComponent<RectTransform>();
        if (rect == null) return;

        // In from the right, out to the left, dipping as it goes - a swipe
        // rather than a slide.
        rect.anchoredPosition = new Vector2(
            Mathf.Lerp(1200f, -1200f, t),
            Mathf.Sin(t * Mathf.PI) * -160f + 120f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-25f, 25f, t));
    }

    private static Sprite? _pawSprite;
    private static bool _pawSpriteSearched;

    /// <summary>
    /// A cat sprite from whatever the game has loaded, if there is one.
    ///
    /// Falls back to a plain dark silhouette, which still reads as something
    /// swiping past. Shipping art would mean shipping art.
    /// </summary>
    private static Sprite? FindPawSprite()
    {
        if (_pawSpriteSearched) return _pawSprite;
        _pawSpriteSearched = true;

        try
        {
            // The IL2CPP shim exposes only the non-generic overload, which
            // hands back UnityEngine.Object and needs casting per item.
            var all = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var sprite = all![i]?.TryCast<Sprite>();
                var name = sprite == null ? "" : sprite.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("paw", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("cat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pawSprite = sprite;
                    Plugin.Logger.LogInfo($"trap: using '{name}' for the cat paw");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"trap: could not find a paw sprite: {e.Message}");
        }
        return _pawSprite;
    }
}
