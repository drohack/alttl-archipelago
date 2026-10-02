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
        // GIVE BACK WHAT IS LOCKED BEFORE FORGETTING IT. A reconnect, or an
        // offline run taken over by the server, begins again with the level
        // still open and its pieces frozen and grey. Cleared first, the next
        // pass took our own "not interactable", collider off and grey for the
        // game's, and put those back when the ability came: Books held by a
        // reconnect stayed "9 of 9 dimmed, 9 not interactive" with Swapping
        // granted (2026-09-29). So everything is unlocked through the records
        // (a pass with nothing locked), and the new session's pass below locks
        // again from what the game really has.
        if (_flagged.Count > 0 || _colliders.Count > 0 || _bodies.Count > 0 || _tinted.Count > 0)
        {
            _releasing = true;
            try { Apply(Released); }
            finally { _releasing = false; }
        }

        _lastSummary = "";
        // Colours and class names belong to objects that are gone; ids get
        // reused, so a stale entry would answer for the wrong object.
        _original.Clear();
        _tinted.Clear();
        _fadingIn.Clear();
        _classes.Clear();
        _colliders.Clear();
        _bodies.Clear();
        _flagged.Clear();
        _savedWhileLocked.Clear();
        _gameInteractable.Clear();
        HeldIndexes.Clear();
        _heldArt.Clear();
        _heldButtons.Clear();
        _clearers = null;
        _matches = null;
        _handlesSaid = "";

        // NOT CLEARED: _drawerMoveRefused. A move the game asked of a drawer
        // still on screen waits for this session's Drawer as it did for the
        // last one's; it is played only on an object the pass is unlocking,
        // and only once it casts to a Drawer.
        ApplyNow();
    }

    /// <summary>Nothing locked: the release pass Reset runs.</summary>
    private static readonly AbilityState Released = new(new SlotData { AbilityLocks = false });

    /// <summary>
    /// Reset's release pass is running: it frees pieces but must not play a
    /// refused drawer move, which belongs to the Drawer ability arriving.
    /// </summary>
    private static bool _releasing;

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
    /// level finishing its entrance.
    ///
    /// NOT ControllerChanged OR ControllerCountChanged. Their event data
    /// carries a Rewired Controller - an INPUT DEVICE, not an ObjectController
    /// - so they say nothing about level objects. ControllerChanged fired 13
    /// to 40 times a second while a player moved the mouse (81,810 times in
    /// droha's 2026-09-25 run), each one a full pass plus half a second of
    /// per-frame passes, so during play the hold never lapsed.
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
            Redim<GameEventManager.GameEvent_LevelTransitionInComplete>("TransitionInComplete");
            // The level's intro ending: the game switches PawPrints' cloth
            // back on the frame after it, collider and Interactable, while the
            // lock still holds it (DevTools flip:, 2026-09-27: InterludeEnd at
            // frame 3184, the cloth on at 3185). With no pass after it the
            // locked cloth cleaned the first screen (droha, 2026-09-27).
            //
            // ONLY ON A LEVEL WITH A RAG, because nothing else needs it. Run on
            // every level it first looked as if it left drawer pieces that
            // could not be picked up (Paper Plane Supplies, Bathroom Drawer);
            // that was the probe's keys misreading drawer contents reshuffled
            // between loads, and with the keys fixed both levels pass with the
            // pass on every level (tools/probe-lock-roundtrip.py, 2026-09-27).
            Redim<GameEventManager.GameEvent_InterludeEnd>("InterludeEnd", LevelHasRag);
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

    private static void Redim<T>(string label, Func<bool>? only = null) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(_ =>
            {
                try
                {
                    _eventCounts[label] = _eventCounts.TryGetValue(label, out var n) ? n + 1 : 1;
                    var state = Inventory.Abilities;
                    if (state == null || !state.LocksEnabled) return;
                    if (only != null && !only()) return;
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
        if (_reentered > 0)
        {
            // Evidence for the Fruit Stickers stack overflow: a pass that
            // re-entered itself means one of our own writes raised a re-dim
            // event synchronously.
            Plugin.Logger.LogWarning(
                $"abilities: a lock pass re-entered itself {_reentered} time(s) on {levelId}");
            _reentered = 0;
        }
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
    /// The held abilities changed: one arrived, or DevTools revoke:. Lock again
    /// now, and reload the level instead of unlocking it in place when it is
    /// one of ObjectLock.ResetOnUnlockLevels - there the game marked covered
    /// pieces fixed while the lock held them, and the unlock gave them back
    /// pickable but impossible to put down (droha, Cat Food Cans, 2026-09-27),
    /// or tinted only the pieces that were free after the intro (Seed Pods,
    /// Clover).
    /// </summary>
    internal static void AbilitiesChanged()
    {
        var before = _flagged.Count;
        ApplyNow();
        try
        {
            var levelId = GameManager.Instance?.levelManager?.ActiveLevelInterface?.LevelId ?? "";
            if (ObjectLock.ResetOnUnlock(levelId, before, _flagged.Count))
            {
                Traps.ResetQuietly($"{levelId}: {before - _flagged.Count} piece(s) unlocked mid-level, "
                    + "reloaded so the game sets its pieces up again");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: reset on unlock failed: {e.Message}");
        }
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

    /// <summary>
    /// A pass is running. Guards against the pass re-entering itself.
    ///
    /// A pass writes flags, colliders and colours, and a listener above runs
    /// a pass when the game reports a change. If a write raises one of those
    /// events synchronously, the pass calls itself until the stack is gone:
    /// a 0xc00000fd with no line in any log, which is what Fruit Stickers did
    /// twice under 0.4.1 after droha moved stickers (2026-09-25). Hints has
    /// the same guard for the same reason. A nested call only marks the pass
    /// dirty; the running one repeats once, then stops.
    /// </summary>
    private static bool _applying;
    private static bool _again;
    private static int _reentered;

    private static void Apply(AbilityState state)
    {
        if (_applying)
        {
            _again = true;
            _reentered++;
            return;
        }

        _applying = true;
        try
        {
            int passes = 0;
            do
            {
                _again = false;
                ApplyOnce(state);
            }
            while (_again && ++passes < 2);
        }
        finally
        {
            _applying = false;
        }
    }

    private static void ApplyOnce(AbilityState state)
    {
        try
        {
            var active = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            var level = active?.Level;
            var controllers = level?.objectControllers;
            var levelId = active?.LevelId ?? "";
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

                // By class, bar the one-level overrides: Books (Randomized)'s
                // symmetric Draggables locks as its Shuffle (ObjectLock.LockedAs).
                // Collect still finds the controller's lists by its real class.
                var cls = ObjectLock.LockClass(levelId, controller.gameObject?.name ?? "", ClassOf(controller));
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

                Collect(controller, cls, isLocked, levelId, votes, byId);
            }

            NoteHandles(level!, levelId, votes, byId);

            var objects = ApplyWanted(votes, byId);
            HoldIndexes(active, controllers, levelId, state);

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
    /// Which indexed states of a level in ObjectLock.IndexHoldLevels stay
    /// where they are while its Indexables group is locked: IndexHold refuses
    /// the buttons on them. A piece another, free group holds keeps moving;
    /// only its state stays.
    ///
    /// NOT THE GAME'S LockIndex, which the first build set: the game writes it
    /// too, and Robot 2's key clears it when it comes out (RobotKeyLockIndex),
    /// so the head moved without Ordering (droha's hand test, 2026-10-01).
    ///
    /// Keyed on the level's copy, not its id: a reload of the same level
    /// kept the old copy's ten ids beside the new ten.
    /// </summary>
    private static void HoldIndexes(LevelInterface? active,
                                    Il2CppSystem.Collections.Generic.List<ObjectController> controllers,
                                    string levelId, AbilityState state)
    {
        var copy = active?.Level == null ? 0 : active.Level.GetInstanceID();
        if (copy != _indexLevelCopy)
        {
            _indexLevelCopy = copy;
            HeldIndexes.Clear();           // ids of a level that is gone
            _heldArt.Clear();
            _heldButtons.Clear();
        }
        if (!ObjectLock.IndexHoldLevels.Contains(levelId))
        {
            HeldIndexes.Clear();
            _heldArt.Clear();
            _heldButtons.Clear();
            return;
        }
        var settled = active != null && active.LevelIsLoaded && !active.IsTransitioning;
        ObjectLock.IndexHoldArt.TryGetValue(levelId, out var artNames);

        var before = HeldIndexes.Count;
        for (int i = 0; i < controllers.Count; i++)
        {
            var controller = controllers[i];
            var ix = controller == null ? null : controller.TryCast<Indexables>();
            var all = ix?.AllIndexes;
            if (all == null) continue;

            var cls = ObjectLock.LockClass(levelId, controller!.gameObject?.name ?? "", ClassOf(controller));
            var hold = ObjectLock.HoldsIndexes(levelId, state.IsClassLocked(cls));
            for (int a = 0; a < all.Count; a++)
            {
                var attr = all[a];
                if (attr == null) continue;
                if (hold && settled) HeldIndexes.Add(attr.GetInstanceID());
                else if (!hold) HeldIndexes.Remove(attr.GetInstanceID());
                if (artNames != null && (settled || !hold)) HoldArt(attr, artNames, hold);
            }
        }
        HoldButtons(active);

        if (HeldIndexes.Count == before) return;
        Plugin.Logger.LogInfo(HeldIndexes.Count > 0
            ? $"abilities: holding {HeldIndexes.Count} indexed state(s) on {levelId} where they are"
            : $"abilities: indexed states on {levelId} given back");
    }

    /// <summary>
    /// The colliders of every button (IndexIncrementTrigger) on a held state:
    /// off while it is held, on again when it is given back. A press never
    /// reaches the button, so none of the game's press handling runs half-way.
    ///
    /// NOT A REFUSED PRESS, which the two builds before this made: a press
    /// refused mid-handling - its state change refused, or the whole handler -
    /// left Robot 3's hearts container switched off once Ordering came, so no
    /// heart would go in; with no press while held they went in (droha's A/B,
    /// 2026-10-01). It also covers Robot 9's antennas, whose clicks come
    /// through their buttons' colliders.
    /// </summary>
    private static void HoldButtons(LevelInterface? active)
    {
        var level = active?.Level;
        if (level == null) return;
        var triggers = level.GetComponentsInChildren<IndexIncrementTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            var trigger = triggers[i];
            var attr = trigger == null ? null : trigger.IndexedAttribute;
            if (attr == null) continue;
            var held = HeldIndexes.Contains(attr.GetInstanceID());
            var colliders = trigger.GetComponents<Collider2D>();
            for (int c = 0; c < colliders.Length; c++)
            {
                var collider = colliders[c];
                if (collider == null) continue;
                var id = collider.GetInstanceID();
                if (held)
                {
                    if (!collider.enabled) continue;
                    collider.enabled = false;
                    _heldButtons[id] = collider;
                }
                else if (_heldButtons.Remove(id))
                {
                    collider.enabled = true;
                }
            }
        }
    }

    /// <summary>Button colliders HoldButtons turned off, to turn back on.</summary>
    private static readonly Dictionary<int, Collider2D> _heldButtons = new();

    /// <summary>
    /// Grey, or give back, the art ObjectLock.IndexHoldArt names on a held
    /// state's owner. PaintArtUnder leaves it alone while it is held: the
    /// owner is a free piece, and its pass would hand the colour back.
    /// </summary>
    private static void HoldArt(IndexedAttribute attr, IReadOnlyList<string> names, bool hold)
    {
        var owner = attr.attachedObject != null ? attr.attachedObject.transform : attr.transform;
        foreach (var name in names)
        {
            var child = owner.Find(name);
            var renderer = child == null ? null : child.GetComponent<SpriteRenderer>();
            if (renderer == null) continue;
            var id = renderer.GetInstanceID();
            if (hold) _heldArt.Add(id);
            else if (!_heldArt.Remove(id)) continue;
            Paint(renderer, hold);
        }
    }

    /// <summary>Instance ids of the IndexedAttributes held where they are (HoldIndexes, IndexHold).</summary>
    internal static readonly HashSet<int> HeldIndexes = new();
    /// <summary>SpriteRenderers greyed with a held state (HoldArt).</summary>
    private static readonly HashSet<int> _heldArt = new();
    private static int _indexLevelCopy;

    /// <summary>
    /// Note one controller's vote on each object it manages.
    ///
    /// Unlocked wins among the groups that act on an object. A second group
    /// that is locked must not take back what the first one released - see
    /// the two-pass note in Apply - and a drawer or cupboard does not release
    /// what another group holds (ObjectLock).
    /// </summary>
    private static void Collect(ObjectController controller, string cls, bool isLocked,
                                string levelId,
                                Dictionary<int, ObjectLock> votes,
                                Dictionary<int, LevelObject> byId)
    {
        Note(controller.ManagedObjects, cls, isLocked, levelId, votes, byId);

        // AND ANYTHING THE SUBCLASS KEEPS TO ITSELF. See ExtraLists.
        var (concrete, extras) = ExtraLists(controller);
        if (extras.Length == 0) return;

        var self = Recast(controller, concrete);
        if (self == null) return;

        foreach (var extra in extras)
        {
            try
            {
                Note(extra.GetValue(self), cls, isLocked, levelId, votes, byId);
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
    private static void Note(object? list, string cls, bool isLocked, string levelId,
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
                if (!votes.TryGetValue(id, out var vote)) votes[id] = vote = new ObjectLock(levelId);
                vote.Add(cls, isLocked);
            }
            catch
            {
                // One awkward object must not abandon the rest of the level.
            }
        }
    }

    /// <summary>
    /// A HANDLE VOTES WITH WHAT IT ACTS ON. Two things a player drags are in
    /// no group's list, so no vote locked them and a locked part could be
    /// played through them (droha's hand tests, 2026-09-27):
    ///   - a sticker's peel handle, StickerObject.pluckObj. Sticky Drawer's
    ///     stickers draw nothing; the gum is the handle, and only the drawer
    ///     held it, so with Drawer held its Stickables solved without Sticking.
    ///   - a rag, ClearingObject. PawPrints' paw prints and coffee spill were
    ///     cleaned up with Tidying missing, their colliders off. Nothing lists
    ///     what a rag clears - PawPrints' three cloths read an empty
    ///     ClearTargets at load (DevTools state:) - so a rag acts on every
    ///     ClearableObject in its level.
    ///   - a match, Match. Candles' match is in no group; lit, its flame grew
    ///     and shrank the grey candles with Gadgets missing (droha,
    ///     2026-09-27). A match acts on every Candle in its level.
    /// A handle gets one group vote per target, locked as that target is, so
    /// unlocked still wins: a handle that serves any free piece stays free.
    /// This is not the reference walk SetFlagsOnOwnClass refuses: a handle only
    /// votes, and only these links are followed.
    /// </summary>
    private static void NoteHandles(Level level, string levelId,
                                    Dictionary<int, ObjectLock> votes,
                                    Dictionary<int, LevelObject> byId)
    {
        var handles = new List<(LevelObject Handle, List<int> Targets)>();
        var clearables = new List<int>();
        var candles = new List<int>();
        int plucks = 0, rags = 0, matches = 0;
        foreach (var pair in byId)
        {
            try
            {
                if (pair.Value.TryCast<ClearableObject>() != null)
                {
                    clearables.Add(pair.Key);
                    continue;
                }
                if (pair.Value.TryCast<Candle>() != null)
                {
                    candles.Add(pair.Key);
                    continue;
                }
                var pluck = pair.Value.TryCast<StickerObject>()?.pluckObj;
                if (pluck == null) continue;
                handles.Add((pluck, new List<int> { pair.Key }));
                plucks++;
            }
            catch
            {
                // Not a sticker, or an unreadable one: it keeps the votes it has.
            }
        }
        foreach (var rag in Clearers(level))
        {
            handles.Add((rag, clearables));
            rags++;
        }
        foreach (var match in Matches(level))
        {
            handles.Add((match, candles));
            matches++;
        }
        var cleared = rags > 0 ? clearables.Count : 0;
        var lit = matches > 0 ? candles.Count : 0;

        foreach (var (handle, targets) in handles)
        {
            var id = handle.GetInstanceID();
            foreach (var target in targets)
            {
                // Only a target something votes on counts: a vote with no
                // holders reads locked, so a handle must not get one unasked.
                if (!votes.TryGetValue(target, out var held)) continue;
                if (!votes.TryGetValue(id, out var vote)) votes[id] = vote = new ObjectLock(levelId);
                byId[id] = handle;
                vote.Add(HandleVote, !held.Unlocked);
            }
        }

        var said = $"{levelId}|{plucks}|{rags}|{cleared}|{matches}|{lit}";
        if (handles.Count > 0 && said != _handlesSaid)
        {
            _handlesSaid = said;
            Plugin.Logger.LogInfo($"abilities: handles vote with what they act on: {plucks} peel "
                + $"handle(s), {rags} rag(s) over {cleared} piece(s), {matches} match(es) over "
                + $"{lit} candle(s)");
        }
    }

    /// <summary>The holder class a handle's votes are noted under: a group, never a cover.</summary>
    private const string HandleVote = "Handle";

    /// <summary>The running level's rags, found once per level.</summary>
    private static List<ClearingObject> Clearers(Level level)
    {
        var id = level.GetInstanceID();
        if (_clearers != null && _clearersFor == id) return _clearers;
        _clearersFor = id;
        _clearers = new List<ClearingObject>();
        try
        {
            foreach (var rag in level.GetComponentsInChildren<ClearingObject>(true))
            {
                if (rag != null) _clearers.Add(rag);
            }
        }
        catch
        {
            // No rags found is the old behaviour.
        }
        return _clearers;
    }

    private static List<ClearingObject>? _clearers;
    private static int _clearersFor;

    /// <summary>The running level's matches, found once per level.</summary>
    private static List<Match> Matches(Level level)
    {
        var id = level.GetInstanceID();
        if (_matches != null && _matchesFor == id) return _matches;
        _matchesFor = id;
        _matches = new List<Match>();
        try
        {
            foreach (var match in level.GetComponentsInChildren<Match>(true))
            {
                if (match != null) _matches.Add(match);
            }
        }
        catch
        {
            // No matches found is the old behaviour.
        }
        return _matches;
    }

    private static List<Match>? _matches;
    private static int _matchesFor;

    /// <summary>The running level has a rag, as the last pass found it (Clearers).</summary>
    private static bool LevelHasRag()
    {
        try
        {
            var level = GameManager.Instance?.levelManager?.ActiveLevelInterface?.Level;
            return level != null && _clearers != null && _clearers.Count > 0
                   && _clearersFor == level.GetInstanceID();
        }
        catch
        {
            return false;
        }
    }
    private static string _handlesSaid = "";

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
    ///
    /// A COVER IS NOT FROZEN AT ALL (solid, ObjectLock.IsCover). A locked door
    /// or drawer front that stops blocking the pointer lets whatever sits
    /// behind it be grabbed through it: Tea Cabinet's locked doors did that
    /// (droha, 2026-09-25). Keeping the collider alone is not enough - Unity
    /// staff: "When not simulated, any attached Collider2D ... also do not
    /// participate in the physics simulation. Raycasts won't see them." So a
    /// cover keeps its body too, and only the flags and the tint lock it;
    /// whether the flags hold it shut is a hand test per cover class.
    /// </summary>
    private static void Freeze(LevelObject obj, bool isLocked, bool solid = false, bool inDrawer = false)
    {
        var col = obj.collider;
        var rb = obj.rigidbody;

        if (isLocked && !solid)
        {
            // Rigidbody first: stop it moving BEFORE removing what holds it.
            // What is recorded is the GAME'S state: as first frozen, then "on"
            // whenever the game has turned it back on since (ours is off, so on
            // is never ours). PawPrints' Clearables start off and the game turns
            // them on after we first freeze them; the first record put them
            // back off, dead, once their ability came (tools/probe-lock-roundtrip.py,
            // 2026-09-27).
            if (rb != null)
            {
                var rid = rb.GetInstanceID();
                if (!_bodies.ContainsKey(rid) || rb.simulated) _bodies[rid] = rb.simulated;
                rb.simulated = false;
            }
            if (col != null)
            {
                var cid = col.GetInstanceID();
                if (!_colliders.ContainsKey(cid) || col.enabled) _colliders[cid] = col.enabled;
                col.enabled = false;
            }
            return;
        }

        // Unlocking - and a COVER, which keeps its collider and body: one an
        // earlier pass froze, before the vote made it a cover, gets them back
        // here. Sticky Drawer's gum and stickers are held by another group
        // while the level loads and then by the drawer alone; frozen at load,
        // they stayed without collider or body as locked covers once every
        // ability was withheld (tools/probe-lock-roundtrip.py, 2026-09-27).
        // Reverse order: give it something to stand on first. On now means
        // the game turned it on after our last write: it stands.
        if (col != null && _colliders.TryGetValue(col.GetInstanceID(), out var wasOn))
        {
            if (!col.enabled) col.enabled = wasOn;
            _colliders.Remove(col.GetInstanceID());
        }
        if (rb != null && _bodies.TryGetValue(rb.GetInstanceID(), out var wasSim))
        {
            // A PIECE OF A DRAWER SET GETS ITS PHYSICS BACK whatever was
            // recorded. Sewing Box pieces recorded "stopped" when first
            // frozen, and putting that back left them dead with their ability
            // held: droha, 2026-09-26, with Ordering given back, "i can move 1
            // of the 3 zippers, and 1 of the safteypins, and none of the
            // needles, but they look colored in". They start stopped and the
            // game turns them on after our first freeze, as PawPrints' do;
            // the record now follows that (above), and without this line
            // Sewing Box comes back whole (tools/probe-lock-roundtrip.py,
            // 2026-09-27). It stays as the fail-open fallback.
            if (!rb.simulated) rb.simulated = inDrawer || wasSim;
            _bodies.Remove(rb.GetInstanceID());
        }
    }

    /// <summary>What each collider and body was before we touched it.</summary>
    private static readonly Dictionary<int, bool> _colliders = new();
    private static readonly Dictionary<int, bool> _bodies = new();

    /// <summary>Objects whose interaction flags we set locked, by object id.</summary>
    private static readonly HashSet<int> _flagged = new();

    /// <summary>The game's own Interactable on each object we hold locked (ApplyWanted).</summary>
    private static readonly Dictionary<int, bool> _gameInteractable = new();

    /// <summary>Whether the lock is holding this object right now.</summary>
    internal static bool IsLocked(LevelObject? obj)
    {
        try { return obj != null && _flagged.Contains(obj.GetInstanceID()); }
        catch { return false; }
    }

    /// <summary>
    /// A locked scrubbed-open object (a cupboard door, Wilting Flowers'
    /// flowers) does not scrub.
    ///
    /// The flags do not stop it: droha, 2026-09-26, Tea Cabinet without
    /// Gadgets, "the cabinet doors are dimmed, but i can move them now". The
    /// door keeps its collider on purpose (a cover, so nothing behind it can be
    /// grabbed through it), so the drag reaches AnimScrubObject's own
    /// handlers; they are refused while the object is locked. SetScrubPosition
    /// is left alone: the game also uses it to place the door on load.
    /// </summary>
    [HarmonyPatch(typeof(AnimScrubObject), nameof(AnimScrubObject.ScrubPickedUp))]
    [HarmonyPrefix]
    private static bool BeforeScrubPickedUp(AnimScrubObject __instance) => !RefuseScrub(__instance);

    [HarmonyPatch(typeof(AnimScrubObject), nameof(AnimScrubObject.Scrubbing))]
    [HarmonyPrefix]
    private static bool BeforeScrubbing(AnimScrubObject __instance) => !RefuseScrub(__instance);

    [HarmonyPatch(typeof(AnimScrubObject), nameof(AnimScrubObject.ScrubReleased))]
    [HarmonyPrefix]
    private static bool BeforeScrubReleased(AnimScrubObject __instance) => !RefuseScrub(__instance);

    private static readonly HashSet<int> _scrubRefusalSaid = new();

    private static bool RefuseScrub(AnimScrubObject scrub)
    {
        try
        {
            if (!IsLocked(scrub)) return false;
            if (_scrubRefusalSaid.Add(scrub.GetInstanceID()))
            {
                Plugin.Logger.LogInfo(
                    $"abilities: '{scrub.gameObject?.name ?? "?"}' is locked - its scrub is refused");
            }
            return true;
        }
        catch
        {
            return false;   // fail open: a scrub is never lost to an error here
        }
    }

    /// <summary>
    /// A locked drawer does not move when the GAME moves it either.
    ///
    /// The flags only stop the player. droha, 2026-09-26, Jewelry Box without
    /// Drawer: "when i complete the rings it still opens up the drawer
    /// automatically that's greyed out ... i also can't close it". DevTools
    /// trace: the rings solving runs DrawerLockTriggerControllerSolved on each
    /// drawer, the game's own unlock, and on the Brooches Drawer that calls
    /// OpenDrawer(null). Closing is refused too: Kitchen Utensils Drawers
    /// swaps its drawers (DoDrawerSwap = CloseDrawer on one, OpenDrawer on the
    /// other), and refusing only the open left both shut with nothing
    /// reachable. So both are refused while the drawer is locked, whatever
    /// asked, and the latest move is remembered with its callback: once the
    /// drawer unlocks it is made (ReplayRefusedDrawer), so the level still
    /// moves on when the ability arrives. The game's own unlock is left to
    /// happen.
    /// </summary>
    [HarmonyPatch(typeof(Drawer), nameof(Drawer.OpenDrawer))]
    [HarmonyPrefix]
    private static bool BeforeOpenDrawer(Drawer __instance, Il2CppSystem.Action __0)
        => !RefuseDrawerMove(__instance, true, __0);

    [HarmonyPatch(typeof(Drawer), nameof(Drawer.CloseDrawer))]
    [HarmonyPrefix]
    private static bool BeforeCloseDrawer(Drawer __instance, Il2CppSystem.Action __0)
        => !RefuseDrawerMove(__instance, false, __0);

    private static bool RefuseDrawerMove(Drawer drawer, bool open, Il2CppSystem.Action? callback)
    {
        try
        {
            if (!IsLocked(drawer)) return false;
            var id = drawer.GetInstanceID();
            _drawerMoveRefused[id] = (open, callback);
            if (_drawerRefusalSaid.Add(id))
            {
                Plugin.Logger.LogInfo(
                    $"abilities: drawer '{drawer.gameObject?.name ?? "?"}' is locked - "
                    + "the game's own open or close waits for the ability");
            }
            return true;
        }
        catch
        {
            return false;   // fail open: never strand a level on an error here
        }
    }

    /// <summary>The latest move refused on a locked drawer, with the callback it carried.</summary>
    private static readonly Dictionary<int, (bool Open, Il2CppSystem.Action? Callback)> _drawerMoveRefused = new();
    private static readonly HashSet<int> _drawerRefusalSaid = new();

    /// <summary>
    /// A drawer whose own move was refused, now unlocked: make the move the
    /// game last asked for, and hand the game its callback back.
    /// </summary>
    private static void ReplayRefusedDrawer(int id, LevelObject obj)
    {
        if (!_drawerMoveRefused.TryGetValue(id, out var wanted)) return;
        _drawerMoveRefused.Remove(id);
        try
        {
            var drawer = obj.TryCast<Drawer>();
            if (drawer == null || drawer.Open == wanted.Open) return;
            Plugin.Logger.LogInfo(
                $"abilities: drawer '{obj.gameObject?.name ?? "?"}' unlocked - "
                + $"{(wanted.Open ? "opening" : "closing")} it, as the game asked");
            if (wanted.Open) drawer.OpenDrawer(wanted.Callback);
            else drawer.CloseDrawer(wanted.Callback);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: moving a drawer on unlock failed: {e.Message}");
        }
    }

    /// <summary>
    /// A drawer that shuts over a piece we hold locked saves OUR "not
    /// interactable", and would give it back when it opens after the ability
    /// has arrived.
    ///
    /// The drawer saves its contents' interactable state as it closes and puts
    /// it back as it opens (SetContainedObjectsInteractable, both ways; DevTools
    /// trace: and drawers:, 2026-09-26). droha, Sewing Box with Ordering given
    /// back: "i can't move the saftypins that are in drawers, even thought the
    /// drawer and saftypins are both colored in and moveable", and opening and
    /// closing never cleared it, since each close saved the dead state again.
    /// Reproduced with DevTools: lock, close, unlock, open left Zipper (2) and
    /// SafetyPin (5) not interactable. So each piece saved while locked is
    /// noted here, and freed in the drawer's save when it unlocks
    /// (FreeSavedState). While it stays locked the save is left alone: opening
    /// then gives back the lock's own state.
    /// </summary>
    [HarmonyPatch(typeof(Drawer), nameof(Drawer.SetContainedObjectsInteractable))]
    [HarmonyPostfix]
    private static void AfterDrawerSetsContents(Drawer __instance)
    {
        try
        {
            if (_flagged.Count == 0) return;
            var saved = __instance.m_contentsInitialInteractableStates;
            if (saved == null || saved.Count == 0) return;
            foreach (var kv in saved)
            {
                var obj = kv.Key;
                if (obj == null || kv.Value) continue;
                var id = obj.GetInstanceID();
                if (_flagged.Contains(id)) _savedWhileLocked[id] = __instance;
            }
        }
        catch
        {
            // Never throw into the drawer's own routine.
        }
    }

    // DrawerExpandable, the one Drawer subclass, has its own
    // SetContainedObjectsInteractable (interop metadata). DO NOT PATCH IT:
    // patched, it ran 100 times on the title screen before any level loaded,
    // threw NullReferenceException in Harmony's wrapper each time, and the
    // game crashed on both launches (2026-09-26, 20:43 and 20:45). A method
    // of a drawer running on the title screen says its native code is shared
    // with other methods, so a patch on it hooks them too. It is not needed:
    // DevTools drawers: on all 31 levels with drawers found only Daggers' four
    // Box drawers of this class, empty at load, and a locked piece cannot be
    // picked up to put in one.

    /// <summary>Pieces a drawer saved as not interactable while we held them, and that drawer.</summary>
    private static readonly Dictionary<int, Drawer> _savedWhileLocked = new();

    /// <summary>
    /// A piece just unlocked that a drawer saved while it was locked: the save
    /// says free now. A drawer opened since has used its save up, and our own
    /// unlock has already freed the piece.
    /// </summary>
    private static void FreeSavedState(int id, LevelObject obj)
    {
        if (!_savedWhileLocked.TryGetValue(id, out var drawer)) return;
        _savedWhileLocked.Remove(id);
        try
        {
            if (drawer == null) return;
            var saved = drawer.m_contentsInitialInteractableStates;
            if (saved == null || !saved.ContainsKey(obj)) return;
            saved[obj] = true;
            Plugin.Logger.LogInfo(
                $"abilities: '{obj.gameObject?.name ?? "?"}' was shut in drawer "
                + $"'{drawer.gameObject?.name ?? "?"}' while locked - it comes out free when the drawer opens");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: freeing a drawer's saved state failed: {e.Message}");
        }
    }

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
    /// The collider freeze (Freeze) is the other half of a lock: flags alone
    /// do not stop every class, and the collider alone once dropped
    /// SomethingEggstra Fridge's eggs off the screen, which is why Freeze
    /// stops the rigidbody first.
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

            // A DRAWER THAT SLIDES FOLLOWS ITS DRAWER LOCK, even when another
            // group frees it. Daggers' four drawers are also held by a
            // Draggables group, so they slid open while their Box stayed
            // locked: droha, 2026-09-26, "i would expect the box and drawers
            // to be tied together so they're both not draggable if you can't
            // do either". Locked as a cover, so nothing in it can be grabbed
            // through it. Trays and boxes with no travel are not this case.
            var heldByDrawerLock = !isLocked && pair.Value.DrawerSetLocked && Slides(obj);
            if (heldByDrawerLock) isLocked = true;

            // A DRAWER PIECE THE GAME ITSELF WILL NOT LET THE PLAYER PICK UP
            // IS NOT LOCKED AT ALL, if it cannot slide either. Sewing Box's box
            // "can't move in the first place" (droha, 2026-09-25), and locking
            // it greyed it at 60% over everything in it: "the whole box and
            // drawers are greyed out" (2026-09-26). The game's own
            // PreventSelection tells it from the trays and boxes a player
            // carries (GameHoldsStill); travel could not, since no tray has
            // any. Once flagged, the flags are ours, so a piece we locked stays
            // locked.
            if (isLocked && pair.Value.IsDrawerSetPart && !_flagged.Contains(pair.Key)
                && GameHoldsStill(obj)) continue;
            try
            {
                // The flags only on an object WE lock, and back once when it
                // unlocks - the rule the tint and the freeze already follow.
                // Writing "interactable" to every free object on every pass
                // overrode flags the game drives itself. Fruit Stickers, with
                // nothing locked, crashed with a stack overflow while stickers
                // were dragged: twice in the run (droha, 2026-09-25), and again
                // on 0.4.1 after 10 s of dragging (2026-09-26). This build ran
                // a minute of sticking, peeling and dragging off with no crash.
                // It also stopped the ControllerChanged re-dims that ran these
                // writes every frame of a drag; which of the two was the
                // trigger is not separated.
                if (isLocked)
                {
                    // THE GAME'S OWN INTERACTABLE is kept while we hold the
                    // object: as it was when we locked it, then any true the
                    // game sets meanwhile (a phase starting). Read before our
                    // write, so a true is never ours.
                    if (_flagged.Add(pair.Key)) _gameInteractable[pair.Key] = obj.m_interactable;
                    else if (obj.m_interactable) _gameInteractable[pair.Key] = true;
                    obj.SetInteractable(false);
                    obj.SetPreventSelection(true);
                    SetFlagsOnOwnClass(obj, true);
                }
                else if (_flagged.Remove(pair.Key))
                {
                    // Back to the game's own Interactable, not "true": putting
                    // true back made pieces the game keeps fixed movable once
                    // their ability arrived - Sewing Box's Curved Needles
                    // Container, Wilting Flowers' Dirt (tools/probe-lock-roundtrip.py,
                    // 2026-09-27). A piece the game freed while we held it
                    // comes back free, and one it has set since our last write
                    // keeps its value. Selection is only ever ours to undo:
                    // SetPreventSelection does not map onto a field we could
                    // read back (DevTools state:), so it goes back to false.
                    var give = obj.m_interactable
                               || !_gameInteractable.TryGetValue(pair.Key, out var own) || own;
                    _gameInteractable.Remove(pair.Key);
                    obj.SetInteractable(give);
                    obj.SetPreventSelection(false);
                    SetFlagsOnOwnClass(obj, false);
                    FreeSavedState(pair.Key, obj);
                }
                Freeze(obj, isLocked, pair.Value.IsCover || heldByDrawerLock, pair.Value.HeldByDrawerSet);
                Tint(obj, isLocked);
                if (!isLocked && !_releasing) ReplayRefusedDrawer(pair.Key, obj);
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
    /// A Drawer that cannot move at all: no travel, and if it is a tray (which
    /// moves by being carried), the game's own PreventSelection (or not
    /// Interactable) on it. Asked only of an object we have not flagged, so
    /// the flags are the game's, and asked again on every pass (DrawerChanged
    /// and EnteredPhase run one), so a piece the game frees later is locked
    /// then. A drawer with travel always locks, whatever its flags say for
    /// the moment. Measured 2026-09-26 with Drawer revoked: Sewing Box's Box (a
    /// tray) and the fixed Main Tray and Main Box read preventSelection=True;
    /// Lunch Tray's four trays and Nesting Boxes' twelve boxes, which the
    /// player carries, read False (a baseline group frees those anyway), and
    /// every tray has no travel, so travel alone could not tell them apart.
    /// Each answer is said once per drawer. Not a Drawer, or any error:
    /// false, which keeps the lock.
    /// </summary>
    private static bool GameHoldsStill(LevelObject obj)
    {
        try
        {
            var drawer = obj.TryCast<Drawer>();
            if (drawer == null) return false;

            var travel = Travel(drawer);
            // What can MOVE decides, not the flags: Jewelry Box's Main Box is
            // IsSlidingDrawer with no travel, and locking it greyed the whole
            // box over the drawers' contents (screenshot, 2026-09-26). A tray
            // moves by being carried, so it is still only if the game will not
            // let it be picked up; anything else with no travel cannot move.
            var slides = travel > 0.05f;
            var still = !slides && (!drawer.IsTray || obj.PreventSelection || !obj.Interactable);

            var name = obj.gameObject?.name ?? "?";
            if (_drawersSaid.Add($"{name}|{still}"))
            {
                Plugin.Logger.LogInfo(
                    $"abilities: drawer '{name}' travel {travel:0.00} (sliding={drawer.IsSlidingDrawer}"
                    + $" tray={drawer.IsTray} fixedBox={drawer.IsFixedBox})"
                    + (still ? " - cannot be moved at all; left alone and not greyed" : " - locked"));
            }
            return still;
        }
        catch
        {
            return false;
        }
    }

    private static readonly HashSet<string> _drawersSaid = new(StringComparer.Ordinal);

    /// <summary>How far a drawer moves between closed and fully open.</summary>
    private static float Travel(Drawer drawer)
    {
        var open = drawer.FullyOpenPosition;
        var closed = drawer.FullyClosedPosition;
        return open == null || closed == null
            ? 0f
            : Vector3.Distance(open.position, closed.position);
    }

    /// <summary>
    /// A Drawer that slides: any travel at all. Said once per drawer when it
    /// is locked this way. Not a Drawer, or any error: false.
    /// </summary>
    private static bool Slides(LevelObject obj)
    {
        try
        {
            var drawer = obj.TryCast<Drawer>();
            if (drawer == null || Travel(drawer) <= 0.05f) return false;
            var name = obj.gameObject?.name ?? "?";
            if (_drawersSaid.Add($"{name}|follows"))
            {
                Plugin.Logger.LogInfo(
                    $"abilities: drawer '{name}' slides - it follows its drawer lock "
                    + "although another group holds it");
            }
            return true;
        }
        catch
        {
            return false;
        }
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
        var count = subs == null ? 0 : subs.Count;
        for (int i = 0; i < count; i++)
        {
            Paint(subs![i], isLocked);
        }

        // A SCRUB OBJECT CAN DRAW NOTHING ITSELF. Wilting Flowers' flowers are
        // logic only; the flower on screen is the SpriteRenderer on their
        // animationToScrub, a separate object under "Scrubbers" (DevTools
        // tree:Flower, 2026-09-26). The cupboard doors draw their own, so
        // this is only for an object with nothing else to grey.
        if (obj.renderer == null && count == 0) PaintScrubPicture(obj, isLocked);

        // EVERY SPRITE THE OBJECT SHOWS, at any depth, not only renderer and
        // subrenderers: its own SpriteRenderer when renderer is another one
        // (Eggs), and every sprite under it that is no other object's.
        // Drawers first: Bathroom Drawer's own renderer is off while it is
        // shut and its Floor child draws it (droha, 2026-09-26, "the drawer is
        // not dimmed"), and Craft Supplies draws a knob on a grandchild. Then
        // everything: Candles' candles are drawn by Body, Holder and Tip
        // children and stayed in full colour while locked, as did Spice Jars'
        // lids and Pencils' wood and lead (tools/probe-lock-roundtrip.py,
        // 2026-09-27). Not another object's sprites, not shadows, and not a
        // sprite that is not drawing (target outlines).
        Paint(obj.GetComponent<SpriteRenderer>(), isLocked);
        PaintArtUnder(obj.transform, isLocked);
    }

    private static void PaintArtUnder(Transform t, bool isLocked)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null) continue;
            if (child.GetComponent<LevelObject>() != null) continue;
            if (child.gameObject.name.Contains("Shadow")) continue;
            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer != null && (!isLocked || renderer.enabled)
                && !(!isLocked && _heldArt.Contains(renderer.GetInstanceID()))) Paint(renderer, isLocked);
            PaintArtUnder(child, isLocked);
        }
    }

    private static void PaintScrubPicture(LevelObject obj, bool isLocked)
    {
        var scrub = obj.TryCast<AnimScrubObject>();
        var anim = scrub == null ? null : scrub.animationToScrub;
        if (anim == null) return;
        Paint(anim.GetComponent<SpriteRenderer>(), isLocked);
    }

    private static void Paint(SpriteRenderer? renderer, bool isLocked)
    {
        if (renderer == null) return;

        var id = renderer.GetInstanceID();
        var now = renderer.color;

        if (!isLocked)
        {
            // Unlocked: hand back its colour, once, while our grey is still
            // on it (alpha aside: a fade on it moves only that). If the game
            // has painted it since, the game's colour stands.
            if (_tinted.Remove(id) && _original.TryGetValue(id, out var own) && OurGrey(now))
            {
                renderer.color = own;
            }
            _original.Remove(id);
            _fadingIn.Remove(id);
            return;
        }

        if (!_tinted.Contains(id) || !OurGrey(now))
        {
            if (now.a < 0.05f)
            {
                // A sprite that draws nothing is not greyed: clear by design,
                // or about to fade in. Greyed, Daggers' clear drawer masks
                // drew grey boxes, and given back opaque (the old guard) they
                // and Books (Randomized)' clear book sprites turned solid
                // (tools/probe-lock-roundtrip.py, 2026-09-27). One that shows
                // up later is greyed then.
                _tinted.Remove(id);
                _original.Remove(id);
                _fadingIn.Add(id);
                return;
            }
            // First sight of the game's colour. A sprite that was clear and
            // now shows is fading in, and what it fades to is opaque: taking
            // the part-way colour made Radial Dance Party's Cat Toys vanish.
            if (!_original.ContainsKey(id))
            {
                _original[id] = _fadingIn.Remove(id) ? new Color(now.r, now.g, now.b, 1f) : now;
            }
        }
        _tinted.Add(id);

        // Only when it is actually changing: a hold re-asserts locked objects
        // every frame, and on a settled level every write here would be a
        // redundant call into IL2CPP.
        if (renderer.color == Locked) return;
        renderer.color = Locked;
    }

    /// <summary>Our grey, whatever its alpha: a fade the game runs on a greyed sprite moves only that.</summary>
    private static bool OurGrey(Color c)
        => Math.Abs(c.r - Locked.r) < 0.01f && Math.Abs(c.g - Locked.g) < 0.01f && Math.Abs(c.b - Locked.b) < 0.01f;

    /// <summary>Sprites seen clear while locked: when one shows, it is fading in (Paint).</summary>
    private static readonly HashSet<int> _fadingIn = new();
}
