using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// The objects of the running level and what the ability locks did to them:
/// its controllers, what a pointer can reach, what is dimmed and frozen, the
/// drawers, colliders, bounds and layout, object sharing, the cat-event
/// objects, and the two hooks into the Archipelago mod (revoke:, traps:).
///
/// AllObjects is the rule every command here keeps: a controller's FULL
/// object set, not just ManagedObjects. The lock-probe commands the round
/// trip drives (objects:, scrub:, flip:) are in LockProbeCommands.cs.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>
    /// The controllers the RUNNING level has registered.
    ///
    /// Registered, not walked from the prefab: only the registered set raises
    /// GameEvent_ObjectControllerSolved, and the two differ - MedicineCabinet
    /// shows 14 on the prefab and 13 at runtime. A location built from the
    /// prefab set would include one that can never be checked.
    /// </summary>
    private static void ListControllers()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("controllers: no level running");
            return;
        }

        var list = level.objectControllers;
        // The Level's instance id answers whether re-entering a puzzle reuses
        // the loaded level (progress kept) or rebuilds it (progress lost).
        DevToolsPlugin.Log.LogInfo(
            $"controllers: {list.Count} registered on {Str(() => li!.LevelId)}"
            + $" levelInstance={Str(() => level.GetInstanceID().ToString())}"
            + $" solvedNow={Str(() => level.numSolutions.ToString())}");
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            DevToolsPlugin.Log.LogInfo(
                $"  [{i}] {Str(() => oc.gameObject.name)}"
                + $" type={Str(() => oc.GetIl2CppType().Name)}"
                + $" solved={Str(() => oc.IsSolved.ToString())}");
        }
    }

    /// <summary>
    /// What the ability locks have actually done to this level's objects.
    ///
    /// WHY THIS IS NOT `controllers`. That command reports each controller's
    /// class and solved flag, which is enough to GUESS at gating by mapping the
    /// class through the ability table - and a harness that guesses that way is
    /// re-deriving the answer from the same table it is supposed to be
    /// auditing. It would agree with a wrong table every time.
    ///
    /// This reports what is on the screen instead. The randomizer's dimmer
    /// gates a puzzle by calling SetInteractable(false) / SetPreventSelection
    /// (true) on the LevelObjects a controller manages; those are the game's
    /// own properties, so reading them back says what the PLAYER can touch,
    /// whatever any table claims.
    ///
    /// DEVTOOLS STILL DOES NOT KNOW THE RANDOMIZER EXISTS, deliberately - see
    /// the note on this assembly. Nothing here references the mod; it reads
    /// game state that happens to be what the mod wrote.
    ///
    /// THE OTHER REASON THIS EXISTS: the mod's own "abilities: N locked" log
    /// line is emitted only when the summary CHANGES, once a second, and not at
    /// all when locks are off or no run is connected. Absence of that line
    /// means four different things, and an incremental log reader races it.
    /// This is asked for on demand and answers about right now.
    /// </summary>
    private static void ReportLocks()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("locks: no level running");
            return;
        }

        var list = level.objectControllers;
        var totalObjects = 0;
        var totalBlocked = 0;
        var totalDimmed = 0;

        // WHICH OBJECTS TWO CONTROLLERS BOTH CLAIM. The randomizer's dimmer
        // merges per object and lets UNLOCKED WIN, so an object held by a
        // locked controller and an open one is left fully playable. A
        // controller can therefore declare an ability and gate nothing at all,
        // which is the difference between what a level's table says it needs
        // and what it actually needs. Counting the overlap is what makes that
        // difference visible instead of looking like a broken lock.
        var owners = new Dictionary<int, int>();
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var managed = AllObjects(oc);
            for (int j = 0; j < (managed == null ? 0 : managed.Count); j++)
            {
                var obj = managed![j];
                if (obj == null) continue;
                try
                {
                    var id = obj.GetInstanceID();
                    owners[id] = owners.TryGetValue(id, out var n) ? n + 1 : 1;
                }
                catch { }
            }
        }

        var lines = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            var objects = 0;
            var blocked = 0;
            var dimmed = 0;
            var shared = 0;
            var norenderer = 0;
            var managed = AllObjects(oc);
            for (int j = 0; j < managed.Count; j++)
            {
                var obj = managed![j];
                if (obj == null) continue;
                objects++;
                try
                {
                    // TWO DIFFERENT QUESTIONS, and conflating them produced a
                    // wrong answer the first time this ran.
                    //
                    // `blocked` is "the player cannot touch this", which the
                    // GAME also causes on its own - measured: plain Draggables
                    // objects inside a closed drawer read non-interactive with
                    // no ability lock anywhere near them, and every such level
                    // looked like a table mismatch.
                    //
                    // `dimmed` is the randomizer's own signature: it repaints a
                    // locked object's renderer to a specific grey. Nothing else
                    // writes that exact colour, so this is the count that
                    // answers "did the ability lock do this".
                    if (!obj.Interactable || obj.PreventSelection) blocked++;
                    if (IsDimmed(obj)) dimmed++;
                    // NOTHING TO TINT. An object with no SpriteRenderer on it
                    // or its subrenderers cannot be greyed however correctly it
                    // is locked - it would be non-interactive and look
                    // completely normal, which is a lock with no feedback.
                    // Counted so "not dimmed" can be told apart from "not
                    // dimmable".
                    if (!HasAnyRenderer(obj)) norenderer++;
                    if (owners.TryGetValue(obj.GetInstanceID(), out var n)
                        && n > 1) shared++;
                }
                catch
                {
                    // One awkward object must not abandon the rest of the level.
                }
            }

            totalObjects += objects;
            totalBlocked += blocked;
            totalDimmed += dimmed;
            lines.Add($"  [{i}] {Str(() => oc.gameObject.name)}"
                      + $" type={Str(() => oc.GetIl2CppType().Name)}"
                      + $" objects={objects} blocked={blocked} dimmed={dimmed}"
                      + $" shared={shared} norenderer={norenderer}"
                      + $" solved={Str(() => oc.IsSolved.ToString())}");
        }

        // The header first, so a harness can wait on one line and then read the
        // list - the same shape as `controllers`, whose header/list split is
        // already documented as a trap for anything that waits on the header
        // and reads immediately.
        DevToolsPlugin.Log.LogInfo(
            $"locks: {Str(() => li!.LevelId)} {list.Count} controller(s),"
            + $" {totalDimmed} of {totalObjects} object(s) dimmed,"
            + $" {totalBlocked} not interactive");
        foreach (var line in lines) DevToolsPlugin.Log.LogInfo(line);
    }

    /// <summary>
    /// Per controller, how many of its objects a POINTER could actually hit
    /// right now.
    ///
    /// WHY THIS EXISTS, and it replaces a question that was being put to a
    /// human. "Can the player reach this group yet" has been answered three
    /// ways in this project and all three were wrong for the same reason:
    /// `dependsOn` is silent about edges the game does not express that way,
    /// the sweep's drawer containment was measured against the one hand audit
    /// available and came back wrong in BOTH directions, and the release
    /// harness force-solves by setting a flag, which bypasses the physics it
    /// is supposed to be measuring. The fourth way was "ask droha to try
    /// dragging it", which is fine for a judgement call and absurd for
    /// something the engine already knows.
    ///
    /// It knows because a pointer hit needs two things and both are readable:
    /// the GameObject has to be active in the hierarchy, and its collider has
    /// to exist and be enabled. An object shut inside a drawer fails one or
    /// the other. That is the whole measurement.
    ///
    /// HOW TO USE IT, which matters more than the numbers. Run it at boot,
    /// then solve whatever opens the container, then run it again. Anything
    /// that becomes touchable in between was gated by the thing you solved -
    /// which is exactly the dependsOn edge the table is missing. One run on
    /// its own says very little.
    ///
    /// AllObjects, not ManagedObjects: Dirtyables keeps its coins elsewhere,
    /// and Containables, Stickables and StackablesY each hide a list. The
    /// `locks` command read only the one and reported "objects=1" for a level
    /// full of freely clickable coins.
    /// </summary>
    private static void Reachable()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("reachable: no level running");
            return;
        }

        var list = level.objectControllers;
        // `stuck` is the column that matters: untouchable AND not yet placed,
        // i.e. the player cannot get at it and it is not finished either.
        // `done` is untouchable because it is already where it belongs, which
        // is not a gate and must never be counted as one.
        DevToolsPlugin.Log.LogInfo(
            $"reachable: {Str(() => li!.LevelId)} -- controller\ttype\ttotal"
            + "\ttouchable\tinactive\tnoCollider\tcolliderOff\tstuck\tdone");

        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            int total = 0, touchable = 0, inactive = 0, missing = 0, off = 0;
            int stuck = 0, done = 0;
            foreach (var obj in AllObjects(oc))
            {
                total++;

                // THE DISTINCTION THE `locks` COMMAND DOES NOT MAKE, and the
                // reason a whole afternoon of measurement had to be thrown
                // away on 2026-09-22.
                //
                // `blocked` there is !Interactable || PreventSelection, which
                // is "the player cannot touch this". That is TWO different
                // situations wearing one number: an object gated behind
                // something the player has not done, and an object already
                // sitting in its correct place. Both are untouchable and only
                // the first is a missing requirement.
                //
                // It is why the count CLIMBS as a level settles - the game
                // places things during setup - and why archive levels came
                // back 100 per cent blocked and looked like a dozen findings.
                // `placed` separates them, and the game has carried the flag
                // all along.
                bool untouchable;
                try { untouchable = !obj.Interactable || obj.PreventSelection; }
                catch { untouchable = false; }
                if (untouchable)
                {
                    bool settled;
                    try { settled = obj.placed; }
                    catch { settled = false; }
                    if (settled) done++; else stuck++;
                }

                bool live;
                try { live = obj.gameObject.activeInHierarchy; }
                catch { live = false; }
                if (!live) { inactive++; continue; }

                Collider2D? col;
                try { col = obj.collider; }
                catch { col = null; }
                if (col == null) { missing++; continue; }

                bool on;
                try { on = col.enabled; }
                catch { on = false; }
                if (on) touchable++; else off++;
            }

            DevToolsPlugin.Log.LogInfo(
                $"reachable:   {Str(() => oc.gameObject.name)}\t"
                + $"{Str(() => oc.GetIl2CppType().Name)}\t"
                + $"{total}\t{touchable}\t{inactive}\t{missing}\t{off}\t"
                + $"{stuck}\t{done}");
        }
        DevToolsPlugin.Log.LogInfo("reachable: done");
    }

    /// <summary>
    /// Try HARDER to stop an object being picked up, and find out what works.
    ///
    /// WHY THIS EXISTS. The randomizer's ability locks call
    /// SetInteractable(false), SetPreventSelection(true) and paint the object
    /// grey - and droha demonstrated on Calendar that a fully "locked" sticker
    /// still drags, unsticks and re-sticks. DevTools' old inert: command made
    /// the same three calls and behaved the same way, so the mechanism itself
    /// was cosmetic rather than the mod misusing it.
    ///
    /// LevelObject also carries a Collider2D. Dragging starts from a pointer
    /// hit, so removing the collider should stop the pickup outright where a
    /// flag did not. This command exists to have that confirmed by hand before
    /// AbilityLocks is changed to rely on it - the flags were never verified
    /// that way, which is exactly how the locks came to be cosmetic.
    ///
    ///   freeze:&lt;controller&gt;   disable the colliders of its objects
    ///   freeze:off             put every collider back
    /// </summary>
    private static readonly Dictionary<int, bool> _frozen = new();

    private static void Freeze(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("freeze: no level running");
            return;
        }

        var off = arg.Equals("off", StringComparison.OrdinalIgnoreCase);
        var list = level.objectControllers;
        var touched = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var name = Str(() => oc.gameObject.name);
            if (!off && !name.Equals(arg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // AllObjects, NOT ManagedObjects. This read ManagedObjects until
            // 2026-09-22, which is the trap the docstring directly below this
            // method exists to warn about - and this method walked straight
            // into it. Drawer Controller manages ONE object, the handle, and
            // holds fifty-six. So `freeze:Drawer Controller`, meant to
            // simulate not having the Drawer ability, disabled the handle's
            // collider and left the drawer fully operable. droha opened and
            // closed it and moved everything inside, which read as the level
            // not being gated at all - and would have been written into
            // levels.json as a correction if the run had been on a level
            // whose answer was not already known.
            //
            // The mod's own AbilityLocks never had this bug: Collect() takes
            // ManagedObjects AND the subclass's private lists. An instrument
            // that freezes less than the thing it simulates is not a
            // simulation.
            foreach (var obj in AllObjects(oc))
            {
                if (obj == null) continue;
                try
                {
                    var col = obj.collider;
                    if (col == null) continue;
                    var id = col.GetInstanceID();
                    if (off)
                    {
                        if (_frozen.TryGetValue(id, out var was))
                        {
                            col.enabled = was;
                        }
                    }
                    else
                    {
                        if (!_frozen.ContainsKey(id)) _frozen[id] = col.enabled;
                        col.enabled = false;
                    }
                    touched++;
                }
                catch
                {
                    // One awkward object must not abandon the rest.
                }
            }
        }

        if (off) _frozen.Clear();
        DevToolsPlugin.Log.LogInfo(
            $"freeze: {(off ? "restored" : "disabled")} {touched} collider(s)");
    }

    /// <summary>
    /// Every Collider2D under each of a controller's objects, and whether it
    /// is still in the physics world.
    ///
    /// WHY THIS EXISTS. The randomizer's lock turns off obj.collider and stops
    /// obj.rigidbody, and `reachable` reads the same single collider. droha and
    /// the second player, 2026-09-25, on Sewing Box: greyed buttons still pushed things
    /// around, and items went grey and stayed movable. Either needs a collider
    /// the lock does not reach - one on a child, or a second one on the object.
    ///
    /// LIVE means enabled, active in the hierarchy, and on a body that is
    /// simulated or on no body at all. A collider on a body that is not
    /// simulated is out of the physics world and raycasts do not see it
    /// (Unity staff on discussions.unity.com), so it is not live.
    ///
    ///   colliders:&lt;controller&gt;   one line per object of that controller
    ///   colliders:all            the same for every controller
    /// </summary>
    private static void Colliders(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("colliders: no level running");
            return;
        }

        var all = arg.Equals("all", StringComparison.OrdinalIgnoreCase);
        var list = level.objectControllers;
        int objects = 0, extraLive = 0;
        DevToolsPlugin.Log.LogInfo(
            $"colliders: {Str(() => li!.LevelId)} -- controller\tobject\tdimmed"
            + "\tmain\tbody\tall\tlive\textra live (not obj.collider)");

        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var cname = Str(() => oc.gameObject.name);
            if (!all && !cname.Equals(arg, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var obj in AllObjects(oc))
            {
                if (obj == null) continue;
                objects++;

                Collider2D? main = null;
                try { main = obj.collider; } catch { }
                Rigidbody2D? body = null;
                try { body = obj.rigidbody; } catch { }

                var mainText = main == null ? "none" : (main.enabled ? "on" : "off");
                var bodyText = body == null ? "none" : (body.simulated ? "simulated" : "stopped");

                int total = 0, live = 0;
                var extras = new List<string>();
                try
                {
                    foreach (var col in obj.GetComponentsInChildren<Collider2D>(true))
                    {
                        if (col == null) continue;
                        total++;
                        var rb = col.attachedRigidbody;
                        var isLive = col.enabled && col.gameObject.activeInHierarchy
                                     && (rb == null || rb.simulated);
                        if (!isLive) continue;
                        live++;
                        if (main != null && col.GetInstanceID() == main.GetInstanceID()) continue;
                        extras.Add($"{col.gameObject.name}/{col.GetIl2CppType().Name}"
                                   + (rb == null ? "/nobody" : ""));
                    }
                }
                catch (Exception e)
                {
                    extras.Add($"(could not walk: {e.Message})");
                }
                extraLive += extras.Count;

                DevToolsPlugin.Log.LogInfo(
                    $"colliders:   {cname}\t{Str(() => obj.gameObject.name)}\t"
                    + $"{(IsDimmed(obj) ? "dimmed" : "-")}\t{mainText}\t{bodyText}\t"
                    + $"{total}\t{live}\t{(extras.Count == 0 ? "-" : string.Join(", ", extras))}");
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"colliders: done, {objects} object(s), {extraLive} live collider(s) "
            + "that are not obj.collider");
    }

    /// <summary>
    /// Every Drawer in the level and what it holds: the interactable state the
    /// drawer RECORDED for each piece (m_contentsInitialInteractableStates)
    /// beside the piece's flags, body and collider now. Or run one drawer's own
    /// OpenDrawer or CloseDrawer, which needs hands otherwise.
    ///
    ///   drawers                 every drawer and its contents
    ///   drawers:open:&lt;name|key&gt;  that drawer's OpenDrawer(null), as the game calls it (key: as state: writes it)
    ///   drawers:close:&lt;name&gt;   that drawer's CloseDrawer(null)
    ///
    /// Written for Sewing Box: safety pins that start in a drawer stayed
    /// unpickable with Ordering given back (droha, 2026-09-26), and every
    /// drawer level left in the lock tests can carry the same fault.
    /// </summary>
    private static void Drawers(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null)
        {
            DevToolsPlugin.Log.LogWarning("drawers: no level running");
            return;
        }

        var parts = arg.Split(new[] { ':' }, 2);
        var verb = parts[0].Trim().ToLowerInvariant();
        var drawers = level.GetComponentsInChildren<Drawer>(true);

        if (verb == "open" || verb == "close")
        {
            var name = parts.Length > 1 ? parts[1].Trim() : "";
            // A name, or a state: key (names repeat: Nesting Boxes has thirteen drawers).
            var byKey = name.Contains("#");
            foreach (var d in drawers)
            {
                if (d == null) continue;
                var id = byKey ? KeyOf(d.transform, level.transform) : Str(() => d.gameObject.name);
                if (!id.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                DevToolsPlugin.Log.LogInfo($"drawers: running {(verb == "open" ? "OpenDrawer" : "CloseDrawer")} on '{name}'");
                if (verb == "open") d.OpenDrawer(null);
                else d.CloseDrawer(null);
                return;
            }
            DevToolsPlugin.Log.LogWarning($"drawers: no drawer named '{name}'");
            return;
        }

        foreach (var d in drawers)
        {
            if (d == null) continue;
            var recorded = d.m_contentsInitialInteractableStates;
            var held = d.ContainedObjects;
            DevToolsPlugin.Log.LogInfo(
                $"drawers: '{Str(() => d.gameObject.name)}' class={Str(() => d.GetIl2CppType().Name)}"
                + $" state={Str(() => d.CurrentState.ToString())}"
                + $" open={Str(() => d.Open.ToString())} sliding={Str(() => d.IsSlidingDrawer.ToString())}"
                + $" tray={Str(() => d.IsTray.ToString())} interactable={Str(() => d.Interactable.ToString())}"
                + $" preventSelection={Str(() => d.PreventSelection.ToString())}"
                + $" holds={(held == null ? 0 : held.Count)} recorded={(recorded == null ? 0 : recorded.Count)}");

            var seen = new HashSet<int>();
            if (recorded != null)
            {
                foreach (var kv in recorded)
                {
                    var obj = kv.Key;
                    if (obj == null) continue;
                    seen.Add(obj.GetInstanceID());
                    DevToolsPlugin.Log.LogInfo($"drawers:   {Content(obj, kv.Value.ToString())}");
                }
            }
            if (held == null) continue;
            for (int i = 0; i < held.Count; i++)
            {
                var obj = held[i];
                if (obj == null || seen.Contains(obj.GetInstanceID())) continue;
                DevToolsPlugin.Log.LogInfo($"drawers:   {Content(obj, "-")}");
            }
        }
        DevToolsPlugin.Log.LogInfo($"drawers: done, {drawers.Length} drawer(s)");
    }

    private static string Content(LevelObject obj, string recorded)
    {
        Collider2D? col = null;
        try { col = obj.collider; } catch { }
        Rigidbody2D? body = null;
        try { body = obj.rigidbody; } catch { }
        return $"'{Str(() => obj.gameObject.name)}' recorded={recorded}"
               + $" interactable={Str(() => obj.Interactable.ToString())}"
               + $" preventSelection={Str(() => obj.PreventSelection.ToString())}"
               + $" body={(body == null ? "none" : body.simulated ? "simulated" : "stopped")}"
               + $" collider={(col == null ? "none" : col.enabled ? "on" : "off")}"
               + $" {(IsDimmed(obj) ? "dimmed" : "-")}";
    }

    /// <summary>
    /// Dump which controllers claim which objects, for the open level.
    ///
    /// WHY A DUMP RATHER THAN A COUNT. `locks` reports `shared` as a number,
    /// which says an object is claimed twice but not by WHOM - and the
    /// question that matters is whether a gated group's objects are all also
    /// held by a group the player can unlock some other way. Books 3 leaks
    /// because its Shuffleables books are all held by a baseline Draggables
    /// group; Spoons leaks only once you hold ONE of its two abilities.
    ///
    /// The second shape cannot be seen in a zero-ability run at all, and it
    /// cannot be brute-forced either: Archipelago items cannot be un-sent, so
    /// every partial holding would need its own server session - about 35 of
    /// them for the non-DLC levels alone.
    ///
    /// Membership makes it a static question. Given which controllers hold
    /// which objects, "is group X freed when the player holds Y" is
    /// arithmetic over the ability table, answerable for every subset at
    /// once, offline, from one sweep with nothing sent.
    ///
    ///   sharing:new     start a fresh file
    ///   sharing:append  add this level to it
    /// </summary>
    private static void DumpSharing(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning("sharing: no level running");
            return;
        }

        var levelId = Str(() => li!.LevelId);
        var path = Path.Combine(GameDir, "BepInEx", "alttl-sharing.tsv");
        var fresh = !File.Exists(path)
            || tag.Trim().Equals("new", StringComparison.OrdinalIgnoreCase);

        var rows = new List<string>();
        if (fresh) rows.Add("level\tcontroller\ttype\tobjectId\tobjectName");

        var list = level.objectControllers;
        var wrote = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var cname = Str(() => oc.gameObject.name);
            var ctype = Str(() => oc.GetIl2CppType().Name);
            foreach (var obj in AllObjects(oc))
            {
                rows.Add(string.Join("\t", new[]
                {
                    levelId, cname, ctype,
                    Str(() => obj.GetInstanceID().ToString()),
                    Str(() => obj.gameObject.name),
                }));
                wrote++;
            }
        }

        if (fresh) File.WriteAllLines(path, rows);
        else File.AppendAllLines(path, rows);

        DevToolsPlugin.Log.LogInfo(
            $"sharing: {levelId} wrote {wrote} row(s) from {list.Count} controller(s) -> {path}");
    }

    /// <summary>
    /// Every managed object's world bounds, grouped by controller.
    ///
    /// Feeds the blocking question the plan flagged and the generator audit
    /// could not answer: an ability-locked group is dimmed and immovable, so if
    /// one of its objects sits physically on top of a FREE group's objects, a
    /// part check we call reachable may not be. Logic looser than the game is
    /// the dangerous direction, because it makes a seed unwinnable.
    ///
    /// Bounds rather than positions, because overlap is about extent: two
    /// objects can have distant centres and still be stacked. Renderer bounds
    /// are already in world space, so no transform maths is needed here - and
    /// doing it here rather than offline is what keeps this honest, since the
    /// numbers come from the same renderer the player sees.
    ///
    /// This only finds CANDIDATES. Whether an overlap actually prevents solving
    /// the free group depends on where its pieces need to travel, which needs a
    /// person to try. Reported as a list to review, never as a verdict.
    /// </summary>
    private static void DumpBounds(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning($"bounds: no level running for '{tag}'");
            return;
        }

        var safe = tag.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
        if (safe.Length == 0) safe = "bounds";

        var path = Path.Combine(GameDir, "BepInEx", $"alttl-bounds-{safe}.tsv");
        var rows = new List<string> { "controller\ttype\tobject\tcx\tcy\tex\tey" };

        var list = level.objectControllers;
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;

            var cname = Str(() => oc.gameObject.name);
            var ctype = Str(() => oc.GetIl2CppType().Name);
            // AllObjects, NOT ManagedObjects. This read ManagedObjects until
            // 2026-09-22 and so under-reported a group's footprint by whatever
            // its subclass keeps to itself - Containables hides three lists,
            // Stickables and StackablesY one each, Dirtyables keeps its coins
            // out entirely. The same trap made `freeze` disable ONE collider
            // where the lock disables fifty-six, and that instrument reported
            // confidently wrong answers twice before anyone checked it against
            // a level whose answer was known.
            //
            // It matters more here than it looks: this command exists to find
            // objects sitting physically on top of other objects, and a group
            // measured at a fraction of its real size is a group whose overlap
            // silently does not register. The failure is a MISSING lead, which
            // is the quiet kind.
            foreach (var obj in AllObjects(oc))
            {
                if (obj == null) continue;
                var r = obj.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                rows.Add(string.Join("\t", new[]
                {
                    cname, ctype, Str(() => obj.gameObject.name),
                    b.center.x.ToString("F3"), b.center.y.ToString("F3"),
                    b.extents.x.ToString("F3"), b.extents.y.ToString("F3"),
                }));
            }
        }

        File.WriteAllLines(path, rows);
        DevToolsPlugin.Log.LogInfo(
            $"bounds: wrote {rows.Count - 1} object(s) across {list.Count} controller(s)"
            + $" for {Str(() => li!.LevelId)} -> {path}");
    }

    /// <summary>
    /// "tree:&lt;name&gt;" dumps the child tree of every object in the active
    /// level whose name contains &lt;name&gt;: each node's components, and on a
    /// renderer its sprite, colour and whether it draws. On an AnimScrubObject
    /// the animated picture and the grabbed handle (animationToScrub,
    /// objectToReference) can sit anywhere in the scene, so their trees follow.
    ///
    /// Written for Wilting Flowers: `locks` reads its three flowers as having
    /// no renderer at all, so the lock could not grey them, and which object
    /// draws a flower was a guess.
    /// </summary>
    private static void Tree(string filter)
    {
        filter = (filter ?? "").Trim();
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || filter.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("tree: needs a running level and a name, e.g. tree:Flower");
            return;
        }

        var matches = new List<Transform>();
        foreach (var t in level.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (Str(() => t.gameObject.name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            // A match inside another match is already in that match's tree.
            var inside = false;
            foreach (var m in matches)
            {
                if (t.IsChildOf(m)) { inside = true; break; }
            }
            if (!inside) matches.Add(t);
        }

        DevToolsPlugin.Log.LogInfo($"tree: {matches.Count} object(s) named like '{filter}' in {Str(() => li!.LevelId)}");
        var lines = 0;
        foreach (var t in matches)
        {
            if (lines > 400) { DevToolsPlugin.Log.LogInfo("tree: stopped at 400 lines"); break; }
            DevToolsPlugin.Log.LogInfo($"tree: {PathOf(t)}");
            lines += TreeNode(t, 1);

            var scrub = t.GetComponent<AnimScrubObject>();
            if (scrub == null) continue;
            var anim = scrub.animationToScrub;
            var handle = scrub.objectToReference;
            DevToolsPlugin.Log.LogInfo(
                $"tree:   animationToScrub={(anim == null ? "null" : PathOf(anim.transform))}"
                + $" enabled={Str(() => anim!.enabled.ToString())}");
            if (anim != null && !anim.transform.IsChildOf(t)) lines += TreeNode(anim.transform, 2);
            DevToolsPlugin.Log.LogInfo(
                $"tree:   objectToReference={(handle == null ? "null" : PathOf(handle.transform))}");
            if (handle != null && !handle.transform.IsChildOf(t)) lines += TreeNode(handle.transform, 2);
        }
        DevToolsPlugin.Log.LogInfo("tree: done");
    }

    private static int TreeNode(Transform t, int depth)
    {
        if (t == null || depth > 8) return 0;

        var parts = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            var name = Str(() => c.GetIl2CppType().Name);
            if (name == "Transform") continue;
            var sprite = c.TryCast<SpriteRenderer>();
            var renderer = c.TryCast<Renderer>();
            if (sprite != null)
            {
                var col = sprite.color;
                name += $"(sprite={Str(() => sprite.sprite == null ? "none" : sprite.sprite.name)}"
                        + $" colour={col.r:0.00},{col.g:0.00},{col.b:0.00},{col.a:0.00}"
                        + $" enabled={sprite.enabled} order={sprite.sortingOrder})";
            }
            else if (renderer != null)
            {
                name += $"(enabled={renderer.enabled})";
            }
            parts.Add(name);
        }

        var pad = new string(' ', depth * 2);
        DevToolsPlugin.Log.LogInfo(
            $"tree: {pad}{Str(() => t.gameObject.name)} active={t.gameObject.activeSelf}"
            + (parts.Count > 0 ? $" [{string.Join(", ", parts)}]" : ""));

        var lines = 1;
        for (int i = 0; i < t.childCount; i++) lines += TreeNode(t.GetChild(i), depth + 1);
        return lines;
    }

    /// <summary>
    /// Write the whole level's layout to a file, for diffing.
    ///
    /// Records the PARENT and the placed flag beside the position, because
    /// position alone is what made the last two rounds of cat-trap testing lie.
    /// A piece posted into an envelope and then "restored" sat at the correct
    /// coordinates while still parented to the envelope, so every position-only
    /// check passed and dragging the envelope still dragged the piece.
    ///
    /// World position as well as local: a piece can be at the right LOCAL
    /// offset under the wrong parent and be in completely the wrong place on
    /// screen, which is precisely the failure a local-only dump hides.
    ///
    /// Usage is snapshot, disturb, trap, snapshot, diff the two files. If the
    /// trap put the level back, they are byte-identical.
    /// </summary>
    private static void DumpLayout(string tag)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.allLevelObjects == null)
        {
            DevToolsPlugin.Log.LogWarning($"layout: no level running, nothing written for '{tag}'");
            return;
        }

        var safe = new string(tag.Trim().ToCharArray());
        foreach (var bad in Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
        if (safe.Length == 0) safe = "layout";

        var path = Path.Combine(GameDir, "BepInEx", $"alttl-layout-{safe}.tsv");
        var objects = level.allLevelObjects;
        var rows = new List<string> { "idx	name	parent	world	local	rot	placed	active" };

        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            if (obj == null) { rows.Add($"{i}	(null)"); continue; }
            var t = obj.transform;
            rows.Add(string.Join("	", new[]
            {
                i.ToString(),
                Str(() => obj.gameObject.name),
                Str(() => t.parent == null ? "(root)" : t.parent.name),
                // Rounded: physics settles to values that wobble in the last
                // decimal place between frames, and an exact dump would report
                // a difference on every run whether or not anything moved.
                Str(() => Round(t.position)),
                Str(() => Round(t.localPosition)),
                Str(() => t.localEulerAngles.z.ToString("F1")),
                Str(() => obj.placed.ToString()),
                Str(() => obj.gameObject.activeInHierarchy.ToString()),
            }));
        }

        File.WriteAllLines(path, rows);
        DevToolsPlugin.Log.LogInfo(
            $"layout: wrote {objects.Count} object(s) for '{safe}'"
            + $" level={Str(() => li!.LevelId)}"
            + $" instance={Str(() => level.GetInstanceID().ToString())} -> {path}");
    }

    private static string Round(Vector3 v)
        => $"({v.x.ToString("F2")}, {v.y.ToString("F2")}, {v.z.ToString("F2")})";

    /// <summary>
    /// Make the Archipelago mod treat these abilities as NOT held - its real
    /// lock, not a DevTools freeze - so one seed holding every ability can
    /// stand in for any held set in a hand test. `revoke:none` gives them all
    /// back. Answered by the mod's DebugHooks.Revoke.
    /// </summary>
    private static void Revoke(string csv) => CallModHook("Revoke", csv, "revoke");

    /// <summary>traps:off / traps:on, answered by the mod's DebugHooks.Traps.</summary>
    private static void Traps(string arg) => CallModHook("Traps", arg, "traps");

    /// <summary>Has this object any SpriteRenderer the dimmer could paint?</summary>
    private static bool HasAnyRenderer(LevelObject obj)
    {
        if (obj.renderer != null) return true;
        var subs = obj.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            if (subs![i] != null) return true;
        }
        return ScrubPicture(obj) != null;
    }

    /// <summary>
    /// The sprite a scrub object is drawn with when it has none of its own:
    /// Wilting Flowers' flowers are logic only, and the flower on screen is on
    /// their animationToScrub (tree:Flower). The randomizer greys this one.
    /// </summary>
    private static SpriteRenderer? ScrubPicture(LevelObject obj)
    {
        var scrub = obj.TryCast<AnimScrubObject>();
        var anim = scrub == null ? null : scrub.animationToScrub;
        return anim == null ? null : anim.GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Is this object wearing the randomizer's "locked" grey?
    ///
    /// The shade is AbilityLocks.Locked - (0.55, 0.55, 0.55, 0.6). Compared
    /// with a tolerance because a colour that has been through a float round
    /// trip is not reliably equal to the constant that set it.
    /// </summary>
    private static bool IsDimmed(LevelObject obj)
    {
        // THE SUBRENDERERS COUNT TOO, and missing them produced three
        // false findings. The randomizer paints obj.renderer AND every entry
        // in obj.subrenderers; an object whose visual lives only on the
        // subrenderers has a null renderer, so checking the main one alone
        // reported it as untouched. Measured on AnimScrubbables,
        // ScrollFieldGroupsController and CandlesObjectController, all three
        // of which read "0 dimmed" while being perfectly well locked.
        if (IsDimColour(obj.renderer)) return true;
        var subs = obj.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            if (IsDimColour(subs![i])) return true;
        }
        if (IsDimColour(ScrubPicture(obj))) return true;
        if (IsDimColour(obj.GetComponent<SpriteRenderer>())) return true;
        // An object drawn by the sprites under it (Bathroom Drawer's Floor,
        // Craft Supplies' Knob2, Candles' Body), not counting other objects.
        return DimArtUnder(obj.transform);
    }

    private static bool DimArtUnder(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null || child.GetComponent<LevelObject>() != null) continue;
            if (IsDimColour(child.GetComponent<SpriteRenderer>())) return true;
            if (DimArtUnder(child)) return true;
        }
        return false;
    }

    private static bool IsDimColour(SpriteRenderer? r)
    {
        // ONLY A SPRITE THAT DRAWS. Bathroom Drawer's own renderer is switched
        // off while it is shut, so its grey counted as "dimmed" with nothing
        // grey on screen (droha, 2026-09-26: "the drawer is not dimmed").
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
        var c = r.color;
        return Near(c.r, 0.55f) && Near(c.g, 0.55f)
            && Near(c.b, 0.55f) && Near(c.a, 0.6f);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

    /// <summary>
    /// Every LevelObject a controller manages, including the ones it keeps to
    /// itself.
    ///
    /// MANAGEDOBJECTS IS NOT THE FULL SET, and reporting as though it were is
    /// what let a broken lock look healthy. Dirtyables registers ONE object
    /// and keeps its coins in dirtyObjects/cleanerObjects; Containables hides
    /// three more lists, Stickables and StackablesY one each. The `locks`
    /// command counted only ManagedObjects and so reported "objects=1" for a
    /// level full of coins, every one of them freely clickable.
    ///
    /// GetType() IS NOT THE CONTROLLER'S CLASS. Everything in
    /// Level.objectControllers is an ObjectController wrapper whatever it
    /// really is, so reflection over the managed type can never see a
    /// subclass's members. The real class comes from GetIl2CppType().Name,
    /// and the wrapper for it has to be found by name and cast to.
    ///
    /// DUPLICATED FROM AbilityLocks ON PURPOSE. DevTools does not reference
    /// the randomizer - see the note on this assembly - and an instrument
    /// that imported the thing it measures would agree with it by
    /// construction. This is the one kind of duplication worth having.
    /// </summary>
    private static List<LevelObject> AllObjects(ObjectController oc)
    {
        var found = new List<LevelObject>();
        var seen = new HashSet<int>();

        void Take(object? list)
        {
            if (list == null) return;
            var type = list.GetType();
            var countProp = type.GetProperty("Count");
            var itemProp = type.GetProperty("Item");
            if (countProp == null || itemProp == null) return;

            int count;
            try { count = (int)(countProp.GetValue(list) ?? 0); }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (itemProp.GetValue(list, new object[] { i }) is not LevelObject obj
                        || obj == null) continue;
                    if (seen.Add(obj.GetInstanceID())) found.Add(obj);
                }
                catch { }
            }
        }

        Take(oc.ManagedObjects);

        var (concrete, extras) = ExtraLists(oc);
        if (extras.Length == 0 || concrete == null) return found;

        object? self;
        try
        {
            var cast = typeof(Il2CppObjectBase)
                .GetMethod(nameof(Il2CppObjectBase.TryCast))
                ?.MakeGenericMethod(concrete);
            self = cast?.Invoke(oc, null);
        }
        catch { return found; }
        if (self == null) return found;

        foreach (var prop in extras)
        {
            try { Take(prop.GetValue(self)); }
            catch { }
        }
        return found;
    }

    /// <summary>The LevelObject collections a controller class declares itself.</summary>
    private static (Type?, PropertyInfo[]) ExtraLists(ObjectController oc)
    {
        string cls;
        try { cls = oc.GetIl2CppType()?.Name ?? ""; }
        catch { return (null, NoExtras); }

        if (_extraLists.TryGetValue(cls, out var known)) return known;

        Type? concrete = null;
        var found = new List<PropertyInfo>();
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t != null && t.Name == cls
                        && typeof(ObjectController).IsAssignableFrom(t))
                    {
                        concrete = t;
                        break;
                    }
                }
                if (concrete != null) break;
            }

            if (concrete != null)
            {
                const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var prop in concrete.GetProperties(Declared))
                {
                    if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                    var pt = prop.PropertyType;
                    if (!pt.IsGenericType) continue;
                    var args = pt.GetGenericArguments();
                    if (args.Length == 1 && typeof(LevelObject).IsAssignableFrom(args[0]))
                    {
                        found.Add(prop);
                    }
                }
            }
        }
        catch { }

        var answer = (concrete, found.ToArray());
        _extraLists[cls] = answer;
        return answer;
    }

    private static readonly PropertyInfo[] NoExtras = new PropertyInfo[0];

    private static readonly Dictionary<string, (Type?, PropertyInfo[])> _extraLists = new();

    /// <summary>
    /// Pick pieces up and drop them, for real, one after another.
    ///
    /// EXISTS BECAUSE A BUG NEEDED QUARTER-SECOND TIMING TO REPRODUCE.
    /// Dropping a piece starts a LeanTween settle animation, and a cat trap
    /// landing while one is running used to leave a dead callback throwing
    /// every frame. Asking a human to spring a trap inside that window is not
    /// a test; droha, reasonably: "how do I time that? It needs to be timed
    /// to like the quarter second."
    ///
    /// So this drops piece after piece with a short gap, which keeps SOMETHING
    /// settling for as long as it runs. A trap sent any time during that lands
    /// mid-animation without anyone having to aim.
    ///
    /// THE GAME'S OWN SETTLE, not a flag flip. docs/dev/release-testing.md
    /// records that every short reproducer written for this project used
    /// DevTools' `complete` instead of solving, and all of them came back
    /// clean while the bug reproduced in the full run. The settle tween only
    /// exists once a piece is dropped, so this calls each piece's
    /// DragObject.Snap() by reflection - the method a drop runs - in one
    /// frame. It is not a pointer drag: see the note in the body.
    ///
    ///     jiggle          every piece in the level, once
    ///     jiggle:5        the first five
    /// </summary>
    private static void JigglePieces(string arg)
    {
        var want = int.MaxValue;
        if (!string.IsNullOrEmpty(arg)
            && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var parsed))
        {
            want = parsed;
        }

        var pieces = UnityEngine.Object.FindObjectsOfType<DragObject>();
        if (pieces == null || pieces.Length == 0)
        {
            DevToolsPlugin.Log.LogWarning("jiggle: no DragObject in the scene");
            return;
        }

        // ObjectPlaced is what starts the settle animation, and it is reached
        // by REFLECTION rather than a synthetic drag.
        //
        // The first version dispatched pointerDown/beginDrag/drag/endDrag
        // through the EventSystem and started no tween at all - 24 drags,
        // zero detached tweens - because DragObject has no OnDrag at all: the
        // interop shows OnPointerDown, OnBeginDrag and OnEndDrag but no drag
        // handler, so the sequence never amounted to a placement. Calling the
        // method the crash names is both simpler and exactly on target.
        // Snap(), not ObjectPlaced(GameEventData). The crash lives in a
        // closure inside ObjectPlaced, but that overload wants a game event
        // we have no honest way to synthesise - and Snap is what actually
        // runs the settle: the type carries snapMoveTween, snapEase and
        // m_snapTweenID right beside it.
        System.Reflection.MethodInfo? placed = null;
        foreach (var m in typeof(DragObject).GetMethods(
                     System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic
                     | System.Reflection.BindingFlags.Instance))
        {
            if (m.Name != "Snap") continue;
            if (m.GetParameters().Length != 0) continue;
            placed = m;
            break;
        }

        if (placed == null)
        {
            // Say what IS there. "No zero-argument Snap" is true and
            // useless; the overload list is what picks the next move.
            DevToolsPlugin.Log.LogWarning(
                "jiggle: no zero-argument Snap on DragObject - "
                + "candidates follow");
            foreach (var m in typeof(DragObject).GetMethods(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.Instance))
            {
                var n = m.Name;
                if (n.IndexOf("Place", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Snap", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Drag", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                var ps = m.GetParameters();
                var sig = new System.Text.StringBuilder(n).Append('(');
                for (int j = 0; j < ps.Length; j++)
                {
                    if (j > 0) sig.Append(", ");
                    sig.Append(ps[j].ParameterType.Name);
                }
                DevToolsPlugin.Log.LogInfo($"jiggle:   {sig.Append(')')}");
            }
            return;
        }

        var moved = 0;
        var failed = 0;
        for (int i = 0; i < pieces.Length && moved < want; i++)
        {
            var piece = pieces[i];
            if (piece == null || piece.gameObject == null) continue;
            if (!piece.gameObject.activeInHierarchy) continue;

            try
            {
                placed.Invoke(piece, null);
                moved++;
            }
            catch (Exception e)
            {
                if (failed++ == 0)
                {
                    DevToolsPlugin.Log.LogWarning(
                        $"jiggle: Snap threw: {e.Message}");
                }
            }
        }

        DevToolsPlugin.Log.LogInfo(
            $"jiggle: placed {moved} of {pieces.Length} piece(s), {failed} "
            + "threw; anything settling now is what a trap has to survive");
    }

    /// <summary>
    /// Every cat-ish component in the running scene.
    ///
    /// The question this answers is "does THIS level have a built-in cat, and
    /// what class is it": CatSwipe turned out to be only a config helper - it
    /// has SetupSwipe, AddSwipeables and the mass and angular settings, but no
    /// trigger - so whatever performs a cat event is a class the static probe
    /// never named. Scanning a real scene names it, once, instead of guessing
    /// class names one compile at a time.
    ///
    /// Deliberately a scene-wide scan rather than a walk of the level's own
    /// object list: a cat that lives outside allLevelObjects is exactly the
    /// case a narrower scan would miss and then report as "no cat here".
    /// </summary>
    private static void ListCats()
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var levelId = li == null ? "(none)" : Str(() => li.LevelId);

        var all = UnityEngine.Object.FindObjectsOfType<Component>();
        int hits = 0;
        var seen = new System.Collections.Generic.Dictionary<string, int>();

        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;

            string type;
            try { type = c.GetIl2CppType().Name; }
            catch { continue; }

            if (type.IndexOf("Cat", StringComparison.Ordinal) < 0
                && type.IndexOf("Paw", StringComparison.Ordinal) < 0
                && type.IndexOf("Swipe", StringComparison.Ordinal) < 0) continue;

            hits++;
            seen[type] = seen.TryGetValue(type, out var n) ? n + 1 : 1;
            DevToolsPlugin.Log.LogInfo(
                $"  {type} on '{Str(() => c.gameObject.name)}'"
                + $" active={Str(() => c.gameObject.activeInHierarchy.ToString())}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"cats: level={levelId} components={hits} distinctTypes={seen.Count}");
        foreach (var kv in seen)
        {
            DevToolsPlugin.Log.LogInfo($"cats: type {kv.Key} x{kv.Value}");
        }
    }
}
