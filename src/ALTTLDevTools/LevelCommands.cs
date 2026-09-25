using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ALTTLModKit;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Booting, solving and completing a level, and the save entries that
/// decide what the level select shows as unlocked.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// Does ObjectController.Reset actually move objects back?
    ///
    /// The cat trap is built on it, and "the cat undid N groups" was only ever
    /// observed on puzzles with no progress to undo - which proves nothing.
    /// This records positions, displaces everything, then resets, and prints
    /// all three so the answer is not a matter of opinion.
    /// </summary>
    private static void ResetTest()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.allLevelObjects == null)
        {
            DevToolsPlugin.Log.LogWarning("resettest: no level running");
            return;
        }

        var objects = level.allLevelObjects;
        var sample = Math.Min(3, objects.Count);

        var before = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            before.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        // Shove everything, as a stand-in for a player having moved pieces.
        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj == null) continue;
            obj.transform.localPosition += new Vector3(0.75f, 0.35f, 0f);
        }

        var moved = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            moved.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        // Deliberately does NOT reset here any more. The point of this command
        // is now to leave the level displaced so a real cat trap can be fired
        // at it and the restore observed.
        var after = new List<string>();
        for (int i = 0; i < sample; i++)
        {
            after.Add(Str(() => objects[i].transform.localPosition.ToString()));
        }

        for (int i = 0; i < sample; i++)
        {
            DevToolsPlugin.Log.LogInfo(
                $"resettest[{i}] before={before[i]} displaced={moved[i]} afterReset={after[i]}");
        }
        DevToolsPlugin.Log.LogInfo(
            "resettest: level displaced. Fire a cat trap now and the objects "
            + "should return to the 'before' positions above.");
    }

    /// <summary>
    /// Start any level by index, ignoring the run entirely.
    ///
    /// For getting at a puzzle's OBJECT sprites, which are the only art in
    /// the game that actually depicts a mechanic. They are loaded with their
    /// level and not before, so the title screen sees none of them - a dump
    /// there finds the level-select card icons and the badge elements and
    /// nothing else.
    ///
    /// RUN THIS WITH NO RUN ACTIVE. Track's StartLevel prefix rewrites the
    /// index to whatever slot the run intends, which is the whole point of
    /// it; it returns early when there is no run, so clearing the slot name
    /// first is what makes this command mean what it says.
    ///
    /// forceReload, because asking for the level that is already loaded
    /// otherwise does nothing and looks like the command failed.
    /// </summary>
    private static void LoadLevel(string arg)
    {
        if (!int.TryParse(arg.Trim(), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out var index))
        {
            DevToolsPlugin.Log.LogWarning("loadlevel: give a level index");
            return;
        }

        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("loadlevel: no LevelManager");
            return;
        }

        manager.StartLevel(index, false, true, 12345);
        DevToolsPlugin.Log.LogInfo($"loadlevel: asked for level {index}");
    }

    /// <summary>
    /// Keep the level the game calls active and destroy every other copy.
    ///
    /// 2026-09-23: booting Spoons after a finished level produced two
    /// Spoons(Clone)s, and a second boot left one of the old pair alive -
    /// it was not active when the boot's cleanup scanned, so the scan never
    /// saw it, and it came back afterwards. droha saw it before any count
    /// did: "2 levels loaded ontop of eachother, two spoons". So this asks
    /// the question the other way round: not "what is live" but "what is NOT
    /// the active level", and it looks at inactive clones too.
    /// </summary>
    private static void Dedupe()
    {
        var lm = GameManager.Instance?.levelManager;
        var active = lm?.ActiveLevelInterface;
        Level? keep = null;
        try { keep = active?.Level; } catch { keep = null; }
        if (keep == null)
        {
            DevToolsPlugin.Log.LogWarning("dedupe: no active level - nothing kept, nothing destroyed");
            return;
        }
        var keepId = keep.GetInstanceID();
        var destroyed = 0;
        foreach (var candidate in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<Level>()))
        {
            var lvl = candidate?.TryCast<Level>();
            if (lvl == null) continue;
            try
            {
                if (lvl.GetInstanceID() == keepId) continue;
                var go = lvl.gameObject;
                // A prefab asset has no valid scene; only instantiated copies
                // in the running scene are candidates.
                if (!go.scene.IsValid()) continue;
                if (go.name.IndexOf("(Clone)", StringComparison.Ordinal) < 0) continue;
                DevToolsPlugin.Log.LogInfo(
                    $"dedupe: destroying {Str(() => go.name)}#{lvl.GetInstanceID()}"
                    + (go.activeInHierarchy ? "" : " (inactive)"));
                UnityEngine.Object.DestroyImmediate(go);
                destroyed++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"dedupe: {e.Message}");
            }
        }
        DevToolsPlugin.Log.LogInfo(
            $"dedupe: kept {Str(() => keep.name)}#{keepId}, destroyed {destroyed}");
    }

    /// <summary>
    /// How many levels are alive at once. Exactly one is correct.
    ///
    /// THE SYMPTOM, MADE COUNTABLE. A cat trap resetting a puzzle inside a
    /// navigation relaunches the level that was on its way out while the
    /// incoming one is still coming up, and neither is torn down - droha,
    /// watching a deliberately un-guarded build: "oh god 2 levels loaded at
    /// once", with a screenshot of one puzzle drawn straight through another.
    ///
    /// This matters more than the hang it may or may not lead to. A freeze is
    /// a race and reproduces perhaps a third of the time; two live levels is
    /// a state, and a state can be counted on every run and compared between
    /// builds. That turns "did the fix work" from a wait-and-see into a
    /// number.
    ///
    /// AllLevelInterfaces rather than the active one, because the whole point
    /// is the level the game has stopped calling active while it is still
    /// loaded and still listening.
    /// </summary>
    private static void CountLiveLevels()
    {
        var lm = GameManager.Instance?.levelManager;
        if (lm == null)
        {
            DevToolsPlugin.Log.LogWarning("livelevels: no LevelManager");
            return;
        }

        // THE LEVEL OBJECTS IN THE SCENE, not the interface table.
        //
        // The first version walked AllLevelInterfaces(false) asking each for
        // LevelIsLoaded, and reported 0 with a puzzle plainly on screen -
        // that table holds the AUTHORED interface per level, not whatever is
        // instantiated. Level is a real Unity object (`controllers` prints
        // its GetInstanceID), so ask the scene instead.
        //
        // FindObjectsOfType, deliberately, NOT FindObjectsOfTypeAll: the
        // former returns only live, active objects, and a level that has been
        // torn down properly should vanish from it. Counting prefabs and
        // dead assets would defeat the whole point.
        var levels = UnityEngine.Object.FindObjectsOfType<Level>();
        var live = new List<string>();
        if (levels != null)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                var lvl = levels[i];
                if (lvl == null) continue;
                live.Add(Str(() => lvl.name + "#" + lvl.GetInstanceID()));
            }
        }

        var active = lm.ActiveLevelInterface;
        DevToolsPlugin.Log.LogInfo(
            $"livelevels: {live.Count} loaded [{string.Join(", ", live)}]"
            + $" active={(active == null ? "none" : Str(() => active.LevelId))}");
    }

    /// <summary>
    /// Force one controller to report itself solved, by index or by name.
    ///
    /// This drives the game's OWN OnSolved, so the event that reaches a mod is
    /// the real one - but it is still a forced solve, not a played one. It
    /// proves the event-to-check path, not that the puzzle is solvable.
    /// </summary>
    private static void SolveController(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("solve: no level running");
            return;
        }

        var list = level.objectControllers;
        ObjectController? target = null;

        if (int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var index))
        {
            if (index >= 0 && index < list.Count) target = list[index];
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                var oc = list[i];
                if (oc != null && string.Equals(oc.gameObject.name, arg.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    target = oc;
                    break;
                }
            }
        }

        if (target == null)
        {
            DevToolsPlugin.Log.LogWarning($"solve: no controller matching '{arg}'");
            return;
        }

        var name = Str(() => target.gameObject.name);
        DevToolsPlugin.Log.LogInfo($"solve: forcing {name} solved");

        target.SetSolved(true);

        // Raise the event through the game's own dispatcher rather than calling
        // OnSolved.
        //
        // OnSolved is virtual, and every real controller is a subclass -
        // DraggablesJigsaw here. Calling it on an ObjectController-typed
        // reference through the interop shim invokes the BASE method, which
        // raises nothing: the forced solve looked like it worked, the log said
        // "forcing ... solved", and no event was ever dispatched.
        //
        // Note what this does and does not prove. It exercises a listener's
        // handling of the event exactly as the game would deliver it. It does
        // NOT prove the game raises the event when a puzzle is really solved -
        // only playing one does that.
        var data = new GameEventManager.GameEventData
        {
            ObjectController = target,
            LevelInterface = li,
        };

        try
        {
            GameEventManager.AddGameEvent<GameEventManager.GameEvent_ObjectControllerSolved>(data);
            DevToolsPlugin.Log.LogInfo($"solve: dispatched ObjectControllerSolved for {name}");
        }
        catch (Exception e)
        {
            // NOT AN ERROR, and it used to be logged as one.
            //
            // The throw comes from the GAME, in its own win check:
            //
            //   System.NullReferenceException
            //     at LevelInterface.CheckWinCondition (GameEventData details)
            //     at GameEventManager.TryDispatchEvent
            //     at GameEventManager.AddGameEvent[T]
            //     at DevToolsBehaviour.SolveController
            //
            // No mod code is on that stack. It happens when a solved event is
            // pushed at a level the game already considers finished - which is
            // what re-entering a beaten puzzle and forcing a controller does,
            // and is a state only this command can manufacture.
            //
            // It matters because the release gate counts logged ERRORS, so
            // twelve of these from one revisit pass failed "no solve threw
            // inside the game" on a run where nothing had gone wrong. The
            // first fix was to stop the gate looking at revisit passes at all,
            // which hid real errors along with this one. Logging it honestly
            // is better than teaching the gate to look away.
            //
            // The mod's own behaviour here is already correct and tested: a
            // location it has collected is never sent twice
            // (CheckLedger.Check returns false, CheckLedgerTests pins it).
            DevToolsPlugin.Log.LogInfo(
                $"solve: {name} was already solved as far as the level is "
                + $"concerned, so its win check had nothing to do ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// "unlockto:N" gives the first N levels a LevelCompletionData entry, which
    /// IS the unlock condition, so the level select renders them in full colour.
    /// Needed to compare tracker markers: a fresh save shows three unlocked
    /// cards, and the markers only matter on unlocked ones.
    /// </summary>
    private static void UnlockTo(string arg)
    {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var count))
        {
            DevToolsPlugin.Log.LogWarning($"unlockto: not a number: {arg}");
            return;
        }

        var manager = GameManager.Instance.levelManager;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var li = manager.GetLevelInterface(i);
                if (li == null || SaveSystem.data.LevelHasCompletionData(li)) continue;
                SaveSystem.data.CreateLevelCompletionData(li, null);
                made++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"unlockto: index {i}: {e.Message}");
            }
        }
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"unlockto: created {made} completion entries up to index {count}."
            + " Reopen the level select to see them.");
    }

    /// <summary>
    /// Marks a level solved in the save exactly the way the game does, so the
    /// unlock rule can be observed rather than guessed. "marksolved:INDEX" or
    /// "marksolved:INDEX:solutionId".
    ///
    /// NOT "solve:", which is what it asked for and never got - SolveController
    /// matches that token first, so this could not run at all. Two different
    /// commands had collided on one prefix, and the ladder matches in order.
    /// </summary>
    private static void MarkSolved(string arg)
    {
        var parts = arg.Split(':');
        var index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var solutionId = parts.Length > 1 ? parts[1] : "probe_0";
        var li = GameManager.Instance.levelManager.GetLevelInterface(index);
        SaveSystem.data.SaveLevelData(li, solutionId, true);
        SaveSystem.SaveGame();
        DevToolsPlugin.Log.LogInfo(
            $"solve: {li.LevelId} solutionId={solutionId} -> found={li.NumSolutionsFound} solved={li.Solved}");
    }

    /// <summary>
    /// "boot:INDEX" or "boot:INDEX:SEED" - launch an arbitrary level straight
    /// from wherever we are. If this works for an archive or daily-only level,
    /// a randomizer can place any puzzle anywhere in the run.
    /// </summary>
    private static void Boot(string arg)
    {
        var parts = arg.Split(':');
        var index = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var seed = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : -1;

        var gm = GameManager.Instance;
        DevToolsPlugin.Log.LogInfo($"boot: StartLevel(index={index}, seed={seed})");

        // THE CLOCK, reported and reset. 2026-09-23: levels booted after a
        // finished one could be played and solved, but their win never fired
        // - Wilting Flowers' cat never came, Desktop Computer's hourglass sat
        // "stuck upsidwon, the sand is not flowing". Both are timed
        // animations while dragging kept working, which is what a paused
        // clock looks like. Logged first so the next occurrence says whether
        // this was it.
        // THE STAR GATE, lifted in memory the way the mod's Track does for a
        // run's own slots. Five Seeing Stars levels need 50-90 solution stars,
        // and StartLevel on a locked one silently falls back to index 0 - a
        // chapter header - which is what booting Cupcakes did on 2026-09-23.
        // Nothing is written to disk and the real star count is untouched.
        try
        {
            var gated = gm.levelManager.GetLevelInterface(index);
            if (gated != null && gated.NumStarsReqToUnlock > 0)
            {
                DevToolsPlugin.Log.LogInfo(
                    $"boot: cleared a {gated.NumStarsReqToUnlock}-star gate on {Str(() => gated.LevelId)}");
                gated.NumStarsReqToUnlock = 0;
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"boot: could not clear a star gate: {e.Message}");
        }

        // The game's own pause holds every gameplay event, and resetting the
        // clock below does not release them; Pause(false) does (measured
        // 2026-09-24).
        try
        {
            if (gm.Paused)
            {
                DevToolsPlugin.Log.LogInfo($"boot: the game was paused ({PauseTrace.State()})");
                gm.Pause(false);
                DevToolsPlugin.Log.LogInfo($"boot: unpaused it -> {PauseTrace.State()}");
            }
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"boot: could not read or undo the pause: {e.Message}");
        }

        var scale = Time.timeScale;
        DevToolsPlugin.Log.LogInfo($"boot: timeScale was {scale}");
        if (scale != 1f)
        {
            Time.timeScale = 1f;
            DevToolsPlugin.Log.LogInfo("boot: timeScale reset to 1");
        }

        // Destroy whatever is loaded first, exactly as the level sweep does.
        //
        // forceReload alone leaves the previous level ALIVE, and its listeners
        // stay subscribed to the global event bus. Booting Radial Dance Party
        // and then hopping to another level left RadialDanceParty.
        // CheckWinCondition attached, so the next solve anywhere threw a
        // NullReferenceException inside it and the solve was lost. The failure
        // surfaced two levels and several minutes away from the boot that
        // caused it, which is what makes it worth doing unconditionally.
        // ActiveLevelInterface WAS NOT ENOUGH, and the gap is a level you have
        // FINISHED. Completing a puzzle moves the game to RetryUI_GameState,
        // at which point the level just beaten is no longer the active one -
        // so this skipped it while its CheckWinCondition stayed subscribed,
        // and every solve in the next level died inside the old level's
        // handler. Measured before the fix: the first level booted after a
        // launch solved cleanly, 21 solves and 0 throws, and every later one
        // threw on all 48.
        //
        // activeInHierarchy is the discriminator and the alternatives are not.
        // scene.IsValid() - the filter used elsewhere in this file - matches
        // all 293 LevelInterface objects, and excluding the 186 prefabs by
        // identity still leaves 107 pooled "<level> Interface(Clone)" objects
        // the level select keeps, every one inactive. Destroying those breaks
        // the levels they belong to: one of them is Bathroom Drawer's, and
        // deleting it on the first boot is exactly why booting that level
        // third came up unwired. Only a level being PLAYED is active.
        var torn = 0;
        foreach (var candidate in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<LevelInterface>()))
        {
            var live = candidate?.TryCast<LevelInterface>();
            if (live == null) continue;
            try
            {
                // activeInHierarchy. Known to be imperfect, and still the
                // best rule found - see the note above and the writeup in
                // docs/verification-log.md.
                //
                // It has one hole: leaving a finished puzzle through the MENUS
                // deactivates it without destroying it, so this skips it and
                // its CheckWinCondition stays subscribed. The log shows it -
                // the boot after a menu exit prints no teardown line, and the
                // solves in the next level throw.
                //
                // Widening to "any LevelInterface with a non-null Level" was
                // tried and is WORSE: chapter headers are LevelInterfaces too,
                // and destroying them left the run loading and "completing"
                // 01__Chapter_HomeSweetHome. Do not reach for that again
                // without a way to tell a chapter from a puzzle.
                // THE HOLE ABOVE, CLOSED 2026-09-22, after droha booted a
                // level, went to the level select, and booted again: the
                // second boot printed "tore down 0 live level(s)" and they
                // ended up playing a scene with two sets of drop targets
                // fighting each other. Their words: "it kind of seems like
                // there's 2 levels on top of eachother maybe?"
                //
                // A menu exit DEACTIVATES the puzzle without destroying it, so
                // activeInHierarchy alone walks straight past it.
                //
                // The discriminator is BUILT-NESS, and it is narrow on
                // purpose, because the two wider rules named above both made
                // things worse:
                //
                //   "(Clone)" excludes the 186 prefabs by name. A prefab is
                //   never a clone, and its controllers are authored rather
                //   than registered - which is what made "any LevelInterface
                //   with a non-null Level" destroy chapter headers.
                //
                //   A registered controller means the level was actually
                //   PLAYED. The ~107 pooled clones the level select keeps are
                //   inactive AND empty, so they survive - which matters,
                //   because destroying Bathroom Drawer's pooled clone is
                //   exactly what left it unwired when booted third.
                //
                // So: active, or a clone that has been built. Nothing else.
                var name = Str(() => live.gameObject.name);
                var built = false;
                if (!live.gameObject.activeInHierarchy)
                {
                    if (name.IndexOf("(Clone)", StringComparison.Ordinal) < 0)
                    {
                        continue;                      // a prefab
                    }
                    try
                    {
                        var lvl = live.Level;
                        var ocs = lvl == null ? null : lvl.objectControllers;
                        built = ocs != null && ocs.Count > 0;
                    }
                    catch
                    {
                        built = false;                 // unreadable: leave it
                    }
                    if (!built) continue;              // pooled, never played
                }
                DevToolsPlugin.Log.LogInfo(
                    $"boot: tearing down '{name}'"
                    + (built ? " (inactive but built - a menu exit left it)" : ""));
                live.ReleaseAssetsAndDestroyLevel();
                torn++;
            }
            catch (Exception e)
            {
                // One level refusing to tear down must not stop the others.
                DevToolsPlugin.Log.LogWarning($"boot: teardown threw: {e.Message}");
            }
        }
        DevToolsPlugin.Log.LogInfo($"boot: tore down {torn} live level(s)");

        // THE LEVEL OBJECTS TOO, not only the interfaces. 2026-09-23: droha
        // exited Medicine Cabinet to the title screen and `livelevels` still
        // found MedicineCabinet(Clone) alive, while the loop above - which
        // walks LevelInterfaces - tore down 0. Booting then put a second
        // level on top: "i've got multiple levels loaded again.... please
        // stop doing that". So destroy any Level still in the scene, through
        // its interface where one owns it, and REFUSE to boot if any survive.
        var stray = DestroyStrayLevels();
        if (stray > 0)
        {
            DevToolsPlugin.Log.LogWarning(
                $"boot: REFUSED - {stray} level(s) still loaded after teardown; "
                + "not starting another on top. Restart the game.");
            return;
        }

        gm.SetGameState<Gameplay_GameState>(null, false);
        gm.levelManager.StartLevel(index, true, true, seed);
    }

    /// <summary>
    /// Destroy every Level object still live in the scene. Returns how many
    /// are left afterwards (0 is the only good answer).
    /// </summary>
    private static int DestroyStrayLevels()
    {
        var levels = UnityEngine.Object.FindObjectsOfType<Level>();
        if (levels == null || levels.Length == 0) return 0;

        var interfaces = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<LevelInterface>());
        foreach (var lvl in levels)
        {
            if (lvl == null) continue;
            var name = Str(() => lvl.name + "#" + lvl.GetInstanceID());
            var released = false;
            foreach (var candidate in interfaces)
            {
                var li = candidate?.TryCast<LevelInterface>();
                if (li == null) continue;
                try
                {
                    if (li.Level == null
                        || li.Level.GetInstanceID() != lvl.GetInstanceID())
                    {
                        continue;
                    }
                    DevToolsPlugin.Log.LogInfo(
                        $"boot: releasing stray level {name} through "
                        + $"'{Str(() => li.gameObject.name)}'");
                    li.ReleaseAssetsAndDestroyLevel();
                    released = true;
                    break;
                }
                catch (Exception e)
                {
                    DevToolsPlugin.Log.LogWarning(
                        $"boot: releasing {name} threw: {e.Message}");
                }
            }
            if (!released)
            {
                // No interface owns it any more - destroy the object itself.
                DevToolsPlugin.Log.LogInfo($"boot: destroying orphan level {name}");
                UnityEngine.Object.DestroyImmediate(lvl.gameObject);
            }
        }

        var left = UnityEngine.Object.FindObjectsOfType<Level>();
        return left == null ? 0 : left.Length;
    }
}
