using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using ALTTLArchipelago.Core;
using HarmonyLib;
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
///
/// NOT CALLED Abilities, which is what it was. That name is taken by
/// ALTTLArchipelago.Core.Abilities - the twelve-ability CATALOGUE - and this
/// file imports that namespace, so the local name won every unqualified
/// mention inside this assembly. Badges.cs, the one place that wants the
/// catalogue, was forced to write it out in full to get past the shadow. This
/// is a dimmer, not a catalogue, and the name now says so.
/// </summary>
[HarmonyPatch]
internal static class AbilityLocks
{
    /// <summary>Dim grey at partial alpha, the shade S3 confirmed reads as "not yet".</summary>
    private static readonly Color Locked = new(0.55f, 0.55f, 0.55f, 0.6f);

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
    /// Renderers WE have painted grey, by instance id. Only these are ever
    /// restored, and only once, when their lock lifts.
    ///
    /// The first version re-asserted the recorded colour on EVERY object every
    /// second, unlocked ones included. Radial Dance Party's Cat Toys fade in
    /// as their dance starts, so the "own colour" first recorded was
    /// transparent, and the pass then forced them back to invisible once a
    /// second - droha, holding Rotating, the one ability the level needs:
    /// "the items dissapeared after it loaded in". Locks off, it played to
    /// the end. An object we never greyed out is none of our business.
    /// </summary>
    private static readonly HashSet<int> _tinted = new();

    /// <summary>
    /// What the last pass concluded, so the log speaks only when it changes.
    /// A line every second would bury everything else.
    /// </summary>
    private static string _lastSummary = "";


    internal static void Reset()
    {
        _lastSummary = "";
        // Colours and class names belong to objects that are gone; ids get
        // reused, so a stale entry would answer for the wrong object.
        _original.Clear();
        _tinted.Clear();
        _classes.Clear();
        _colliders.Clear();
        _bodies.Clear();
    }

    /// <summary>
    /// Lock the moment a controller registers, not on the next poll.
    ///
    /// PATCHED ON THE CONTROLLER, NOT THE LEVEL, and that distinction cost
    /// DevTools a 111-level sweep to learn: Level.RegisterObjectController and
    /// LevelInterface.RegisterObjectController both exist and both look like
    /// the funnel, and a patch on the Level one installs cleanly and records
    /// nothing at all. Controllers register THEMSELVES through this no-argument
    /// method. See RegistrationLog in ALTTLDevTools, which found it.
    ///
    /// WHY THIS REPLACED THE POLL. Locks used to land only on the once-a-second
    /// pass, so every level opened with up to a second of everything live.
    /// droha, testing Fruit Stickers: "for a second when the level loaded i was
    /// able to move one, then it stopped." A second is enough to pick a piece
    /// up and put it somewhere, which is the exact thing the lock exists to
    /// prevent.
    ///
    /// HoldDim rather than a single pass, because a controller's ManagedObjects
    /// need not be populated by the time it registers, and half a second of
    /// per-frame passes settles that without this method having to know the
    /// order. It is also what already handles a level rebuilding itself.
    ///
    /// A postfix, and it cannot throw out: a level must still build itself if
    /// the dimmer has a bad day.
    /// </summary>
    [HarmonyPatch(typeof(ObjectController), nameof(ObjectController.RegisterObjectController))]
    [HarmonyPostfix]
    private static void AfterRegister()
    {
        try
        {
            var state = Inventory.Abilities;
            if (state == null || !state.LocksEnabled) return;
            HoldDim();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: register hook failed: {e.Message}");
        }
    }

    /// <summary>
    /// Per-frame passes while a rebuild settles (see HoldDim), and nothing
    /// otherwise.
    ///
    /// THERE IS NO ONCE-A-SECOND PASS ANY MORE. It existed for two things,
    /// and both now have an event: an ability arriving (Inventory re-applies
    /// when AbilityState.Version moves) and the game rebuilding objects under
    /// us (the listeners in AttachGameEvents).
    /// </summary>
    internal static void Tick(float dt)
    {
        if (Time.unscaledTime >= _holdUntil) return;

        var state = Inventory.Abilities;
        if (state == null || !state.LocksEnabled) return;
        Apply(state);
    }

    /// <summary>
    /// Game events after which the game may have re-lit or re-enabled
    /// objects we locked: a drawer moving, a phase starting, a reset, a
    /// controller set changing, a level finishing its entrance.
    /// </summary>
    internal static void AttachGameEvents()
    {
        if (_eventsAttached) return;
        try
        {
            Redim<GameEventManager.GameEvent_ObjectDrawerChanged>("DrawerChanged");
            Redim<GameEventManager.GameEvent_ObjectControllerEnteredPhase>("EnteredPhase");
            Redim<GameEventManager.GameEvent_LevelReset>("LevelReset");
            Redim<GameEventManager.GameEvent_LevelRandomized>("LevelRandomized");
            Redim<GameEventManager.GameEvent_ControllerChanged>("ControllerChanged");
            Redim<GameEventManager.GameEvent_ControllerCountChanged>("ControllerCountChanged");
            Redim<GameEventManager.GameEvent_LevelTransitionInComplete>("TransitionInComplete");
            OnLevelEnd<GameEventManager.GameEvent_LevelComplete>();
            OnLevelEnd<GameEventManager.GameEvent_LevelExited>();
            _eventsAttached = true;
            Plugin.Logger.LogInfo("abilities: listening for rebuilds");
        }
        catch (Exception e)
        {
            // GameEventManager may not exist on the first frames; the next
            // Begin tries again.
            Plugin.Logger.LogWarning($"abilities: could not attach listeners: {e.Message}");
        }
    }

    private static bool _eventsAttached;

    // The IL2CPP side holds these weakly; rooting them here keeps them firing.
    private static readonly List<Il2CppSystem.Action<GameEventManager.GameEventData>> KeepAlive = new();

    /// <summary>How often each re-dim event fired on the current level.</summary>
    private static readonly SortedDictionary<string, int> _eventCounts = new(StringComparer.Ordinal);

    private static void Redim<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(_ =>
            {
                try
                {
                    _eventCounts[label] = _eventCounts.TryGetValue(label, out var n) ? n + 1 : 1;
                    var state = Inventory.Abilities;
                    if (state == null || !state.LocksEnabled) return;
                    HoldDim();
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"abilities: {label} handler failed: {e.Message}");
                }
            });
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    private static void OnLevelEnd<T>() where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(_ =>
            {
                try
                {
                    ReportEvents(GameManager.Instance?.levelManager?.ActiveLevelInterface?.LevelId ?? "?");
                }
                catch
                {
                    // A diagnostic line is not worth a throw into IL2CPP.
                }
            });
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    /// <summary>
    /// One line per level: which re-dim events fired and how often. Called
    /// when the level ends, so it is evidence, not noise.
    /// </summary>
    private static void ReportEvents(string levelId)
    {
        if (_eventCounts.Count == 0) return;
        var parts = new List<string>();
        foreach (var pair in _eventCounts) parts.Add($"{pair.Key}={pair.Value}");
        Plugin.Logger.LogInfo($"abilities: re-dim events on {levelId}: {string.Join(", ", parts)}");
        _eventCounts.Clear();
    }

    /// <summary>
    /// Re-dim right now: an ability arrived, or a new seed began.
    /// </summary>
    internal static void ApplyNow()
    {
        var state = Inventory.Abilities;
        if (state == null || !state.LocksEnabled) return;

        Apply(state);
    }

    /// <summary>
    /// Until when the dimming runs every frame. Outside it, nothing runs.
    /// </summary>
    private static float _holdUntil;

    /// <summary>
    /// Keep re-dimming for a short while, not just once.
    ///
    /// A single pass is not enough after a level rebuild. Calling ApplyNow
    /// the instant the reset is requested dims objects the game is about to
    /// restore: it puts every piece back to its own colour AFTER our call -
    /// later in the frame, or on one of the next few - and the locked ones
    /// then sit fully lit until something dims them again.
    ///
    /// droha, watching MedicineCabinet with Containers and Ordering locked:
    /// "when the cat trap triggers I see the locked pieces as full colored in
    /// before it snaps to greyed out." The log showed the re-dim running
    /// immediately every time, which is exactly why once was not the answer.
    ///
    /// Half a second of per-frame passes covers the rebuild without pinning
    /// the cost on the normal path, where nothing is changing the colours.
    /// </summary>
    internal static void HoldDim(float seconds = 0.5f)
    {
        _holdUntil = Time.unscaledTime + seconds;
        ApplyNow();
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

            int locked = 0, unlocked = 0;
            var missing = new SortedSet<string>(StringComparer.Ordinal);

            // TWO PASSES, BECAUSE ONE OBJECT CAN BELONG TO TWO CONTROLLERS.
            //
            // This used to lock objects inside the controller loop, so the LAST
            // controller to touch a shared object decided its fate. Coins 1
            // (Shape) has an Ordered group of six and a Stacked group of six
            // over the same six coins: holding Ordering but not Stacking, the
            // ordering group freed the coins and the stacking group immediately
            // re-locked them. droha reported the card offering work while the
            // level had nothing to interact with, and that is this - not the
            // dependency bug that produced the same symptom on Candy Canes.
            //
            // An object is locked only if EVERY group that acts on it is
            // locked. Anything else takes work away from a player who has
            // earned it, and the failure is invisible: the card is honest, the
            // logic is right, and the level is simply dead. A drawer or
            // cupboard is not such a group: see ObjectLock.
            var votes = new Dictionary<int, ObjectLock>();   // objectId -> its owners
            var byId = new Dictionary<int, LevelObject>();

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

                Collect(controller, cls, isLocked, votes, byId);
            }

            var objects = ApplyWanted(votes, byId);

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

    /// <summary>
    /// Note one controller's vote on each object it manages.
    ///
    /// Unlocked wins among the groups that act on an object. A second group
    /// that is locked must not take back what the first one released - see
    /// the two-pass note in Apply - and a drawer or cupboard does not release
    /// what another group holds (ObjectLock).
    /// </summary>
    private static void Collect(ObjectController controller, string cls, bool isLocked,
                                Dictionary<int, ObjectLock> votes,
                                Dictionary<int, LevelObject> byId)
    {
        Note(controller.ManagedObjects, cls, isLocked, votes, byId);

        // AND ANYTHING THE SUBCLASS KEEPS TO ITSELF. See ExtraLists.
        var (concrete, extras) = ExtraLists(controller);
        if (extras.Length == 0) return;

        var self = Recast(controller, concrete);
        if (self == null) return;

        foreach (var extra in extras)
        {
            try
            {
                Note(extra.GetValue(self), cls, isLocked, votes, byId);
            }
            catch
            {
                // A property that throws on read must not cost us the level.
            }
        }
    }

    /// <summary>
    /// Note every LevelObject in a list, however that list has to be read.
    ///
    /// TAKES object RATHER THAN A TYPED LIST ON PURPOSE. An Il2Cpp collection
    /// wrapper does not reliably implement the MANAGED IEnumerable&lt;T&gt; -
    /// it may only carry the Il2Cpp-side interface - so a typed parameter
    /// would compile, cast to null at runtime, and silently lock nothing. The
    /// fast path is tried first and Count/indexer reflection is the fallback,
    /// so this works whichever interface the wrapper happens to expose.
    /// </summary>
    private static void Note(object? list, string cls, bool isLocked,
                             Dictionary<int, ObjectLock> votes,
                             Dictionary<int, LevelObject> byId)
    {
        if (list == null) return;

        foreach (var obj in Walk(list))
        {
            if (obj == null) continue;
            try
            {
                var id = obj.GetInstanceID();
                byId[id] = obj;
                if (!votes.TryGetValue(id, out var vote)) votes[id] = vote = new ObjectLock();
                vote.Add(cls, isLocked);
            }
            catch
            {
                // One awkward object must not abandon the rest of the level.
            }
        }
    }

    /// <summary>
    /// Yield a list's LevelObjects, by interface if possible and by Count and
    /// indexer if not. Anything that is not a LevelObject is skipped.
    /// </summary>
    private static IEnumerable<LevelObject> Walk(object list)
    {
        if (list is IEnumerable<LevelObject> typed)
        {
            foreach (var obj in typed) yield return obj;
            yield break;
        }

        var type = list.GetType();
        var countProp = type.GetProperty("Count");
        var itemProp = type.GetProperty("Item");
        if (countProp == null || itemProp == null) yield break;

        int count;
        try { count = (int)(countProp.GetValue(list) ?? 0); }
        catch { yield break; }

        for (int i = 0; i < count; i++)
        {
            LevelObject? obj = null;
            try { obj = itemProp.GetValue(list, new object[] { i }) as LevelObject; }
            catch { /* a hole in the list is not the end of the level */ }
            if (obj != null) yield return obj;
        }
    }

    /// <summary>
    /// The object lists a controller declares itself, beyond ManagedObjects.
    ///
    /// MANAGEDOBJECTS IS NOT ALWAYS A CONTROLLER'S FULL SET, and assuming it
    /// was left a whole puzzle playable while it looked locked. droha, on
    /// Coins 2 (Dirtyness) with no abilities at all: "coins are dimmed
    /// (instant), but i can click on them to change their color? and i was
    /// able to get a solution."
    ///
    /// Dirtyables declares its own dirtyObjects and cleanerObjects and handles
    /// ObjectClicked itself; ManagedObjects held exactly ONE object, which is
    /// why the lock report said objects=1 for a level full of coins. Every
    /// coin still went grey, because the tint walks subrenderers - so the
    /// level looked completely locked and was completely playable. That
    /// combination is the worst case: the screen says one thing, the game
    /// does another, and nothing in a log disagrees.
    ///
    /// Found by REFLECTION rather than by naming Dirtyables, for the same
    /// reason the collider is taken from every class rather than from
    /// stickers: the bug is an assumption about the base class, so anything
    /// that assumption is wrong about should be covered, including whatever a
    /// future game update adds. Filtered to lists whose element type really is
    /// a LevelObject, so a list of solutions or hints is not swept in.
    ///
    /// Cached per TYPE, not per instance - it is the same answer for every
    /// controller of a class, and the lookup is pure reflection.
    /// </summary>
    private static (Type?, PropertyInfo[]) ExtraLists(ObjectController controller)
    {
        var cls = ClassOf(controller);
        if (_extraLists.TryGetValue(cls, out var known)) return known;

        // GetType() IS NOT THE CONTROLLER'S CLASS, and believing it was is why
        // the first attempt at this found nothing at all. Every controller
        // here comes out of Level.objectControllers, a list of ObjectController
        // wrappers, so the MANAGED type is always the base regardless of what
        // the object really is - reflection on it can never see dirtyObjects.
        // The Il2Cpp side knows the truth, which is what ClassOf already asks
        // for, and the wrapper type has to be found from that name and cast to.
        var concrete = WrapperFor(cls, typeof(ObjectController));
        var found = new List<PropertyInfo>();

        if (concrete != null)
        {
            try
            {
                const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var p in concrete.GetProperties(Declared))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (!HoldsLevelObjects(p.PropertyType)) continue;
                    found.Add(p);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning(
                    $"abilities: could not inspect {cls}, locking only its "
                    + $"ManagedObjects: {e.Message}");
            }
        }

        if (found.Count > 0)
        {
            Plugin.Logger.LogInfo(
                $"abilities: {cls} keeps objects outside ManagedObjects: "
                + string.Join(", ", found.ConvertAll(p => p.Name)));
        }

        var answer = (concrete, found.ToArray());
        _extraLists[cls] = answer;
        return answer;
    }

    private static readonly Dictionary<string, (Type?, PropertyInfo[])> _extraLists = new();

    /// <summary>The managed wrapper Type for an Il2Cpp class name, or null.</summary>
    private static Type? WrapperFor(string cls, Type mustDeriveFrom)
    {
        if (string.IsNullOrEmpty(cls)) return null;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t != null && t.Name == cls && mustDeriveFrom.IsAssignableFrom(t))
                {
                    return t;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The same object, seen as its real class, so its own members can be
    /// reached. TryCast is the interop idiom used throughout this mod.
    /// </summary>
    private static object? Recast(Il2CppObjectBase controller, Type? concrete)
    {
        if (concrete == null) return null;
        try
        {
            var cast = typeof(Il2CppObjectBase)
                .GetMethod(nameof(Il2CppObjectBase.TryCast))
                ?.MakeGenericMethod(concrete);
            return cast?.Invoke(controller, null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Is this a collection of LevelObjects?
    ///
    /// Checks the GENERIC ARGUMENT rather than asking whether the type is an
    /// IEnumerable&lt;LevelObject&gt;, because Il2CppSystem's List does not
    /// necessarily implement the managed interface - see Note. A subclass
    /// element type counts: Dirtyables' coins are not plain LevelObjects.
    /// </summary>
    private static bool HoldsLevelObjects(Type type)
    {
        if (typeof(IEnumerable<LevelObject>).IsAssignableFrom(type)) return true;
        if (!type.IsGenericType) return false;

        var args = type.GetGenericArguments();
        return args.Length == 1 && typeof(LevelObject).IsAssignableFrom(args[0]);
    }

    /// <summary>
    /// Take the collider away so nothing can click it, and stop the rigidbody
    /// so it cannot fall while the collider is gone.
    ///
    /// BOTH HALVES WERE LEARNED THE HARD WAY, an hour apart.
    ///
    /// The collider is the half that works. Flags alone do not stop an
    /// interaction on every class: a sticker's real drag lives on a separate
    /// PluckObject, StickerObject redeclares PreventSelection as a get-only
    /// property that cannot be written, and no combination of the flags this
    /// dimmer can reach moves either of them. A pointer cannot hit what has no
    /// collider, whatever a subclass overrides, so this holds everywhere
    /// rather than per class. droha, on Calendar, after a fix that set every
    /// flag correctly: "stickers still moveable, but dimmed."
    ///
    /// The rigidbody is the half that stops the cure being worse. A collider
    /// is also what holds a physics object up, and the first version of this
    /// removed it alone. droha, on SomethingEggstra Fridge: "i saw the eggs in
    /// the background be dimmed and fall off of the screen." Five of six eggs
    /// left the level. Stopping simulation pins the object exactly where it
    /// is, so a locked puzzle piece stays part of the puzzle.
    ///
    /// Both originals are remembered ONCE. Re-locking runs every pass, and a
    /// second pass must not record "already disabled" as the state to put
    /// back when the ability finally arrives.
    /// </summary>
    private static void Freeze(LevelObject obj, bool isLocked)
    {
        var col = obj.collider;
        var rb = obj.rigidbody;

        if (isLocked)
        {
            // Rigidbody first: stop it moving BEFORE removing what holds it.
            if (rb != null)
            {
                var rid = rb.GetInstanceID();
                if (!_bodies.ContainsKey(rid)) _bodies[rid] = rb.simulated;
                rb.simulated = false;
            }
            if (col != null)
            {
                var cid = col.GetInstanceID();
                if (!_colliders.ContainsKey(cid)) _colliders[cid] = col.enabled;
                col.enabled = false;
            }
            return;
        }

        // Unlocking reverses the order: give it something to stand on first.
        if (col != null && _colliders.TryGetValue(col.GetInstanceID(), out var wasOn))
        {
            col.enabled = wasOn;
            _colliders.Remove(col.GetInstanceID());
        }
        if (rb != null && _bodies.TryGetValue(rb.GetInstanceID(), out var wasSim))
        {
            rb.simulated = wasSim;
            _bodies.Remove(rb.GetInstanceID());
        }
    }

    /// <summary>What each collider and body was before we touched it.</summary>
    private static readonly Dictionary<int, bool> _colliders = new();
    private static readonly Dictionary<int, bool> _bodies = new();

    /// <summary>
    /// Set the flags on the object's OWN class, not just the base class.
    ///
    /// WHY THIS IS NOT JUST obj.SetInteractable. droha found Fruit Stickers'
    /// objects greyed out and draggable anyway, and the game's class layout
    /// explains it: StickerObject derives from LevelObject and re-declares
    /// SetInteractable and PreventSelection as NEW rather than override.
    /// Calling them through a LevelObject reference writes the base class's
    /// fields while the sticker reads its own, so every flag is set and
    /// nothing consults them. The object still went grey, because the tint is
    /// on the renderer and that is not shadowed - which is precisely why this
    /// looked correct for so long.
    ///
    /// It is the SAME MISTAKE as the one ExtraLists fixes, one level down:
    /// a base-typed reference cannot see what a subclass redeclares. So the
    /// cure is the same - find the real class, cast to it, and call the
    /// members that actually belong to the object.
    ///
    /// WHAT THIS REPLACED, AND WHY IT HAD TO GO. The first fix disabled each
    /// locked object's Collider2D instead. It did stop the sticker drag, and
    /// eleven of twelve controller classes passed by hand - but colliders are
    /// also what holds a physics object up. droha, on SomethingEggstra Fridge:
    /// "i saw the eggs in the background be dimmed and fall off of the
    /// screen." Five of the six eggs left the level. A lock that deletes the
    /// puzzle is worse than one that does not hold, so nothing here touches
    /// colliders any more.
    ///
    /// Cached per CLASS. Most classes shadow nothing and cost one lookup ever.
    /// </summary>
    private static void SetFlagsOnOwnClass(LevelObject obj, bool isLocked, int depth = 0)
    {
        var (concrete, gates, nested) = Shadowed(obj);
        if (gates.Count == 0 && nested.Length == 0) return;

        var self = Recast(obj, concrete);
        if (self == null) return;

        foreach (var (member, whenLocked) in gates)
        {
            var want = isLocked ? whenLocked : !whenLocked;
            try
            {
                if (member is PropertyInfo prop) prop.SetValue(self, want);
                else if (member is MethodInfo call) call.Invoke(self, new object[] { want });
            }
            catch
            {
                // The base call already ran. Fail open, never half-locked.
            }
        }

        // DELIBERATELY NOT FOLLOWED: the LevelObjects this one holds.
        //
        // An earlier version walked them, reasoning that a sticker's drag
        // lives on its pluckObj. It was unnecessary - the collider freeze
        // already stops the sticker - and it was actively dangerous. The log
        // it printed gave the game away: "ContainableObject holds its own
        // LevelObject(s): _Container_k__BackingField, StartContainer,
        // Container". An egg's container on SomethingEggstra Fridge is the
        // strawberry basket, which belongs to the UNLOCKED Draggables group,
        // so following the reference locked a piece the player had every
        // right to move.
        //
        // That is the direction with teeth. Locking too little leaves a check
        // earnable early; locking too much can make a puzzle impossible with
        // nothing on screen to explain why. The reference graph does not
        // respect the group boundaries the gate is defined in terms of, so it
        // is not something to walk.
        _ = nested;
        _ = depth;
    }

    /// <summary>
    /// Every interaction gate the object's OWN class redeclares.
    ///
    /// SEARCHES PROPERTIES AS WELL AS METHODS, and that distinction is the
    /// whole bug. The first attempt looked only for SetInteractable and
    /// SetPreventSelection methods, found none on StickerObject, and reported
    /// success while Calendar's stickers stayed draggable. What StickerObject
    /// actually redeclares is the PROPERTY PreventSelection - LevelObject
    /// declares one too, so the base call writes the base's copy and the
    /// sticker goes on reading its own.
    ///
    /// The value each gate wants while locked differs - PreventSelection
    /// true, Interactable and Selectable false - so it is carried alongside
    /// the member rather than assumed.
    /// </summary>
    private static (Type?, List<(MemberInfo, bool)>, PropertyInfo[]) Shadowed(LevelObject obj)
    {
        string cls;
        try { cls = obj.GetIl2CppType()?.Name ?? ""; }
        catch { return (null, NoGates, NoNested); }

        if (_shadowed.TryGetValue(cls, out var known)) return known;

        Type? concrete = null;
        var gates = new List<(MemberInfo, bool)>();
        var nested = new List<PropertyInfo>();
        try
        {
            concrete = WrapperFor(cls, typeof(LevelObject));
            if (concrete != null && concrete != typeof(LevelObject))
            {
                const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                var oneBool = new[] { typeof(bool) };

                foreach (var (name, whenLocked) in Gates)
                {
                    var prop = concrete.GetProperty(name, Declared);
                    if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
                    {
                        gates.Add((prop, whenLocked));
                        continue;
                    }

                    var call = concrete.GetMethod("Set" + name, Declared, null, oneBool, null);
                    if (call != null) gates.Add((call, whenLocked));
                }

                foreach (var prop in concrete.GetProperties(Declared))
                {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    if (!prop.CanRead) continue;
                    if (!typeof(LevelObject).IsAssignableFrom(prop.PropertyType)) continue;
                    nested.Add(prop);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning(
                $"abilities: could not inspect {cls}, using the base flags only: {e.Message}");
        }

        if (gates.Count > 0)
        {
            var names = new List<string>();
            foreach (var (member, _) in gates) names.Add(member.Name);
            Plugin.Logger.LogInfo(
                $"abilities: {cls} redeclares {string.Join(", ", names)}; "
                + "setting them on the object's own class");
        }
        else
        {
            // ONCE PER CLASS, and worth the line. Twice now a fix has been
            // declared working on the strength of an absent log line, when
            // the truth was that the class never reached this code at all.
            // Saying what WAS seen makes the difference visible.
            Plugin.Logger.LogInfo(
                $"abilities: {cls} -> base flags only"
                + (concrete == null ? " (no wrapper type found)" : ""));
        }

        if (nested.Count > 0)
        {
            var held = new List<string>();
            foreach (var prop in nested) held.Add(prop.Name);
            Plugin.Logger.LogInfo(
                $"abilities: {cls} holds its own LevelObject(s): {string.Join(", ", held)}");
        }

        var answer = (concrete, gates, nested.ToArray());
        _shadowed[cls] = answer;
        return answer;
    }

    /// <summary>
    /// The interaction gates worth chasing onto a subclass, and what each one
    /// reads while an object is locked.
    /// </summary>
    private static readonly (string Name, bool WhenLocked)[] Gates =
    {
        ("PreventSelection", true),
        ("Interactable", false),
        ("Selectable", false),
    };

    private static readonly List<(MemberInfo, bool)> NoGates = new();
    private static readonly PropertyInfo[] NoNested = new PropertyInfo[0];

    private static readonly Dictionary<string, (Type?, List<(MemberInfo, bool)>, PropertyInfo[])> _shadowed = new();

    /// <summary>
    /// Apply the settled state, once per object. Returns how many were touched.
    ///
    /// The count is now DISTINCT objects rather than controller-object pairs,
    /// so a level with shared objects stops over-reporting. It reads lower than
    /// it used to on exactly the levels this fix is about.
    /// </summary>
    private static int ApplyWanted(Dictionary<int, ObjectLock> votes,
                                   Dictionary<int, LevelObject> byId)
    {
        int touched = 0;
        foreach (var pair in votes)
        {
            if (!byId.TryGetValue(pair.Key, out var obj) || obj == null) continue;
            var isLocked = !pair.Value.Unlocked;
            try
            {
                obj.SetInteractable(!isLocked);
                obj.SetPreventSelection(isLocked);
                SetFlagsOnOwnClass(obj, isLocked);
                Freeze(obj, isLocked);
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
    /// marshal, and every pass (each frame of a hold, each event) would ask it
    /// for every controller for a value that is fixed for the object's lifetime. Keyed by instance id, and
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

        if (!isLocked)
        {
            // Unlocked: hand back what we took, once, and then leave it to the
            // game - including any fade the level is running on it.
            if (_tinted.Remove(id) && _original.TryGetValue(id, out var own))
            {
                renderer.color = own;
            }
            return;
        }

        if (!_original.ContainsKey(id))
        {
            // First time we grey it: whatever it looks like now is its own
            // colour, EXCEPT mid-fade. A sprite caught fading in reads nearly
            // transparent, and restoring that later would make it vanish, so
            // record it as fully opaque instead.
            var was = renderer.color;
            if (was.a < 0.05f) was.a = 1f;
            _original[id] = was;
        }
        _tinted.Add(id);

        // Only when it is actually changing: a hold re-asserts locked objects
        // every frame, and on a settled level every write here would be a
        // redundant call into IL2CPP.
        if (renderer.color == Locked) return;
        renderer.color = Locked;
    }
}
