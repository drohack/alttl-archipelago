using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using static ALTTLDevTools.Helpers;

namespace ALTTLDevTools;

/// <summary>
/// Commands tools/probe-lock-roundtrip.py drives.
///
/// `objects:&lt;file&gt;` (old spelling `state:&lt;file&gt;`) writes one JSON line
/// per object of the running level:
/// what tools/probe-lock-roundtrip.py compares between a level loaded with
/// every ability held and the same level locked and then given everything
/// back. Per object: the controllers holding it, its flags, collider, body,
/// whether the Archipelago mod has it locked, and the colour of every sprite
/// the lock paints (and of every other sprite it shows). Per drawer: its
/// state, travel and the interactable states it saved. Per scrub object: how
/// far it is scrubbed.
///
/// `scrub:&lt;name|key&gt;[:&lt;fraction&gt;]` drives one scrub object (a cupboard
/// door, a wilting flower) the way a drag does: its handle (objectToReference)
/// is moved `fraction` of the way from StartPoint to EndPoint (default 0.8)
/// and the three handlers the game's pick-up, move and drop events reach -
/// ScrubPickedUp, Scrubbing, ScrubReleased - are called with it: the ones the
/// Archipelago mod refuses while the object is locked. Logs ScrubPercent
/// before, during and after; the handle is put back where it was.
/// </summary>
public partial class DevToolsBehaviour
{
    private static MethodInfo? _isLocked;
    private static bool _isLockedLooked;

    /// <summary>
    /// `objects:&lt;file&gt;` (old spelling `state:&lt;file&gt;`). `label` is the
    /// keyword it was sent as, so each spelling answers in its own name - the
    /// lock probe waits for "state: N object(s)".
    /// </summary>
    private static void State(string path, string label)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null || level.objectControllers == null)
        {
            DevToolsPlugin.Log.LogWarning($"{label}: no level running");
            return;
        }

        var root = level.transform;
        var objects = new Dictionary<int, LevelObject>();
        var holders = new Dictionary<int, List<string>>();
        var list = level.objectControllers;
        for (int i = 0; i < list.Count; i++)
        {
            var oc = list[i];
            if (oc == null) continue;
            var holder = $"{Str(() => oc.gameObject.name)}|{Str(() => oc.GetIl2CppType().Name)}"
                         + $"|{Str(() => oc.IsSolved.ToString())}";
            foreach (var obj in AllObjects(oc))
            {
                if (obj == null) continue;
                var id = obj.GetInstanceID();
                objects[id] = obj;
                if (!holders.TryGetValue(id, out var h)) holders[id] = h = new List<string>();
                h.Add(holder);
            }
        }
        foreach (var d in level.GetComponentsInChildren<Drawer>(true))
        {
            if (d != null && !objects.ContainsKey(d.GetInstanceID())) objects[d.GetInstanceID()] = d;
        }
        foreach (var s in level.GetComponentsInChildren<AnimScrubObject>(true))
        {
            if (s != null && !objects.ContainsKey(s.GetInstanceID())) objects[s.GetInstanceID()] = s;
        }

        // The handles the Archipelago mod votes with what they act on
        // (AbilityLocks.NoteHandles): a sticker's peel handle, and a rag,
        // which acts on every ClearableObject in the level. No controller
        // lists either, so they are found from their targets.
        var handleOf = new Dictionary<int, List<int>>();
        var clearables = new List<int>();
        var candles = new List<int>();
        foreach (var pair in new List<KeyValuePair<int, LevelObject>>(objects))
        {
            PluckObject? pluck = null;
            try
            {
                if (pair.Value.TryCast<ClearableObject>() != null) clearables.Add(pair.Key);
                else if (pair.Value.TryCast<Candle>() != null) candles.Add(pair.Key);
                else pluck = pair.Value.TryCast<StickerObject>()?.pluckObj;
            }
            catch { }
            if (pluck == null) continue;
            objects[pluck.GetInstanceID()] = pluck;
            handleOf[pluck.GetInstanceID()] = new List<int> { pair.Key };
        }
        foreach (var rag in level.GetComponentsInChildren<ClearingObject>(true))
        {
            if (rag == null) continue;
            objects[rag.GetInstanceID()] = rag;
            handleOf[rag.GetInstanceID()] = clearables;
        }
        // A match acts on every Candle in the level, as a rag on every
        // ClearableObject (AbilityLocks.NoteHandles).
        foreach (var match in level.GetComponentsInChildren<Match>(true))
        {
            if (match == null) continue;
            objects[match.GetInstanceID()] = match;
            handleOf[match.GetInstanceID()] = candles;
        }

        var keys = new Dictionary<int, string>();
        var used = new HashSet<string>();
        foreach (var pair in objects)
        {
            // Two objects on one GameObject share a transform: Calendar's
            // stickers and their peel handles. The later one is told apart by
            // its class; controllers' objects come first, so it is the same
            // one on every load.
            var key = KeyOf(pair.Value.transform, root);
            if (!used.Add(key))
            {
                key += "@" + Str(() => pair.Value.GetIl2CppType().Name);
                used.Add(key);
            }
            keys[pair.Key] = key;
        }

        int written = 0;
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            w.WriteLine("{\"kind\":\"level\",\"level\":" + Json(Str(() => li!.LevelId))
                        + ",\"objects\":" + objects.Count + ",\"frame\":" + Time.frameCount + "}");
            foreach (var pair in objects)
            {
                try
                {
                    var of = handleOf.TryGetValue(pair.Key, out var t)
                        ? t.ConvertAll(id => keys.TryGetValue(id, out var k) ? k : "?") : null;
                    w.WriteLine(ObjectLine(pair.Value, keys[pair.Key],
                        holders.TryGetValue(pair.Key, out var h) ? h : new List<string>(), keys, root, of));
                    written++;
                }
                catch (Exception e)
                {
                    w.WriteLine("{\"kind\":\"error\",\"key\":" + Json(keys[pair.Key]) + ",\"error\":" + Json(e.Message) + "}");
                }
            }
        }
        DevToolsPlugin.Log.LogInfo($"{label}: {written} object(s) of {Str(() => li!.LevelId)} written to {path}");
    }

    private static string ObjectLine(LevelObject obj, string key, List<string> holders,
                                     Dictionary<int, string> keys, Transform root,
                                     List<string>? handleOf = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"kind\":\"obj\",\"key\":").Append(Json(key));
        sb.Append(",\"name\":").Append(Json(Str(() => obj.gameObject.name)));
        sb.Append(",\"cls\":").Append(Json(Str(() => obj.GetIl2CppType().Name)));
        sb.Append(",\"holders\":[");
        for (int i = 0; i < holders.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Json(holders[i]));
        sb.Append(']');
        if (handleOf != null)
        {
            // A handle: the keys of the pieces it acts on (a peel handle's
            // sticker, a rag's ClearTargets).
            sb.Append(",\"handleOf\":[");
            for (int i = 0; i < handleOf.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Json(handleOf[i]));
            sb.Append(']');
        }
        var parent = obj.transform.parent;
        sb.Append(",\"parent\":").Append(Json(parent == null ? "" : KeyOf(parent, root)));
        sb.Append(",\"active\":").Append(B(obj.gameObject.activeInHierarchy));
        sb.Append(",\"inter\":").Append(B(obj.Interactable));
        sb.Append(",\"prevSel\":").Append(B(obj.PreventSelection));
        // The fields under those two properties, and the game's own verdict.
        sb.Append(",\"interField\":").Append(B(obj.m_interactable));
        sb.Append(",\"prevSelField\":").Append(B(obj.preventSelection));
        sb.Append(",\"selectable\":").Append(B(obj.Selectable));

        Collider2D? col = null;
        try { col = obj.collider; } catch { }
        Rigidbody2D? body = null;
        try { body = obj.rigidbody; } catch { }
        sb.Append(",\"col\":").Append(Json(col == null ? "none" : col.enabled ? "on" : "off"));
        sb.Append(",\"body\":").Append(Json(body == null ? "none" : body.simulated ? "sim" : "stop"));
        sb.Append(",\"extraLive\":").Append(ExtraLiveColliders(obj, col));

        var locked = LockedByMod(obj);
        sb.Append(",\"locked\":").Append(locked == null ? "null" : B(locked.Value));
        sb.Append(",\"dim\":").Append(B(IsDimmed(obj)));

        // What the lock paints (AbilityLocks.Tint): everything the object
        // shows, not a child object's sprites and no shadows. Then any other
        // sprite under it - with the lock as it is, none.
        var paint = new List<(string, SpriteRenderer)>();
        var seen = new HashSet<int>();
        void Add(string where, SpriteRenderer? r)
        {
            if (r != null && seen.Add(r.GetInstanceID())) paint.Add((where, r));
        }
        Add("renderer", obj.renderer);
        var subs = obj.subrenderers;
        var count = subs == null ? 0 : subs.Count;
        for (int i = 0; i < count; i++) Add($"sub{i}", subs![i]);
        if (obj.renderer == null && count == 0) Add("scrubPicture", ScrubPicture(obj));
        Add("self", obj.GetComponent<SpriteRenderer>());
        ArtUnder(obj.transform, obj.transform, Add);
        sb.Append(",\"paint\":[");
        for (int i = 0; i < paint.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Sprite(paint[i].Item1, paint[i].Item2));
        sb.Append(']');

        var art = new List<(string, SpriteRenderer)>();
        ArtUnder(obj.transform, obj.transform, (where, r) =>
        {
            if (r != null && !seen.Contains(r.GetInstanceID())) art.Add((where, r));
        });
        sb.Append(",\"art\":[");
        for (int i = 0; i < art.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Sprite(art[i].Item1, art[i].Item2));
        sb.Append(']');

        var drawer = obj.TryCast<Drawer>();
        if (drawer != null)
        {
            var open = drawer.FullyOpenPosition;
            var shut = drawer.FullyClosedPosition;
            var travel = open == null || shut == null ? 0f : Vector3.Distance(open.position, shut.position);
            sb.Append(",\"drawer\":{\"state\":").Append(Json(Str(() => drawer.CurrentState.ToString())));
            sb.Append(",\"open\":").Append(B(drawer.Open));
            sb.Append(",\"sliding\":").Append(B(drawer.IsSlidingDrawer));
            sb.Append(",\"tray\":").Append(B(drawer.IsTray));
            sb.Append(",\"fixedBox\":").Append(B(drawer.IsFixedBox));
            sb.Append(",\"travel\":").Append(F(travel));
            var held = drawer.ContainedObjects;
            sb.Append(",\"holds\":").Append(held == null ? 0 : held.Count);
            sb.Append(",\"saved\":[");
            var saved = drawer.m_contentsInitialInteractableStates;
            var first = true;
            if (saved != null)
            {
                foreach (var kv in saved)
                {
                    if (kv.Key == null) continue;
                    var k = keys.TryGetValue(kv.Key.GetInstanceID(), out var known) ? known : KeyOf(kv.Key.transform, root);
                    sb.Append(first ? "" : ",").Append('[').Append(Json(k)).Append(',').Append(B(kv.Value)).Append(']');
                    first = false;
                }
            }
            sb.Append("]}");
        }

        var scrub = obj.TryCast<AnimScrubObject>();
        if (scrub != null)
        {
            var anim = scrub.animationToScrub;
            var t = -1f;
            try { if (anim != null) t = anim.GetCurrentAnimatorStateInfo(0).normalizedTime; } catch { }
            sb.Append(",\"scrub\":{\"time\":").Append(F(t));
            sb.Append(",\"handle\":").Append(Json(scrub.objectToReference == null ? "" : KeyOf(scrub.objectToReference.transform, root)));
            sb.Append('}');
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Sprites under t that belong to t's object: no child object's, no shadows.</summary>
    private static void ArtUnder(Transform t, Transform top, Action<string, SpriteRenderer> add)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child == null || child.GetComponent<LevelObject>() != null) continue;
            if (child.gameObject.name.Contains("Shadow")) continue;
            var r = child.GetComponent<SpriteRenderer>();
            if (r != null) add(KeyOf(child, top), r);
            ArtUnder(child, top, add);
        }
    }

    private static string Sprite(string where, SpriteRenderer r)
    {
        var c = r.color;
        var draws = r.enabled && r.gameObject.activeInHierarchy;
        return "[" + Json(where) + "," + B(draws) + "," + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a)
               + "," + B(IsDimColour(r)) + "]";
    }

    /// <summary>Live colliders under the object that are not obj.collider and not a child object's.</summary>
    private static int ExtraLiveColliders(LevelObject obj, Collider2D? main)
    {
        var n = 0;
        foreach (var c in obj.GetComponentsInChildren<Collider2D>(true))
        {
            if (c == null || (main != null && c.GetInstanceID() == main.GetInstanceID())) continue;
            var owner = c.GetComponentInParent<LevelObject>();
            if (owner == null || owner.GetInstanceID() != obj.GetInstanceID()) continue;
            var rb = c.attachedRigidbody;
            if (c.enabled && c.gameObject.activeInHierarchy && (rb == null || rb.simulated)) n++;
        }
        return n;
    }

    /// <summary>The mod's AbilityLocks.IsLocked, found by name; null when the mod has none.</summary>
    private static bool? LockedByMod(LevelObject obj)
    {
        if (!_isLockedLooked)
        {
            _isLockedLooked = true;
            _isLocked = ModMethod("AbilityLocks", "IsLocked");
        }
        if (_isLocked == null) return null;
        try { return _isLocked.Invoke(null, new object[] { obj }) as bool?; }
        catch { return null; }
    }

    // ------------------------------------------------------------ flip:

    private static float _flipUntil;
    private static string _flipName = "";
    private static readonly Dictionary<int, string> _flipLast = new();
    private static bool _flipListening;

    private static bool FlipArmed => _flipName.Length > 0 && Time.unscaledTime < _flipUntil;

    /// <summary>
    /// `flip:&lt;name&gt;[:&lt;seconds&gt;]` (default 20 s): every frame, each level
    /// object with that name whose collider, Interactable or selection changed,
    /// with the frame; and meanwhile the game events that could have done it.
    /// Listens and reads only, nothing patched. Written because a runtime trace
    /// of DragObjectBase.SetInteractable crashed the game (2026-09-27) while
    /// looking for what switches PawPrints' locked cloth back on.
    /// </summary>
    private static void StartFlip(string arg)
    {
        if (IsOff(arg))
        {
            _flipUntil = 0f;
            DevToolsPlugin.Log.LogInfo("flip: off");
            return;
        }

        var parts = arg.Split(':');
        var secs = 20f;
        if (parts.Length > 1) float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out secs);
        _flipName = parts[0].Trim();
        _flipUntil = Time.unscaledTime + secs;
        _flipLast.Clear();
        AttachFlipEvents();
        DevToolsPlugin.Log.LogInfo($"flip: watching '{_flipName}' for {secs:0.#} s");
    }

    private static void TickFlip()
    {
        if (!FlipArmed) return;
        try
        {
            var li = GameManager.Instance.levelManager.ActiveLevelInterface;
            var level = li == null ? null : li.Level;
            if (level == null) return;
            foreach (var obj in level.GetComponentsInChildren<LevelObject>(true))
            {
                if (obj == null || obj.gameObject.name != _flipName) continue;
                Collider2D? col = null;
                try { col = obj.collider; } catch { }
                var now = $"active={B(obj.gameObject.activeInHierarchy)} col={(col == null ? "none" : col.enabled ? "on" : "off")}"
                          + $" inter={B(obj.m_interactable)} prevSel={B(obj.PreventSelection)} locked={LockedByMod(obj)}";
                var id = obj.GetInstanceID();
                if (_flipLast.TryGetValue(id, out var was) && was == now) continue;
                _flipLast[id] = now;
                DevToolsPlugin.Log.LogInfo($"flip: {Time.frameCount} '{KeyOf(obj.transform, level.transform)}' {now}");
            }
        }
        catch
        {
            // A level mid-teardown; the next frame looks again.
        }
    }

    private static void AttachFlipEvents()
    {
        if (_flipListening) return;
        _flipListening = true;
        FlipOn<GameEventManager.GameEvent_LevelTransitionIn>("LevelTransitionIn");
        FlipOn<GameEventManager.GameEvent_LevelTransitionInComplete>("LevelTransitionInComplete");
        FlipOn<GameEventManager.GameEvent_InterludeStart>("InterludeStart");
        FlipOn<GameEventManager.GameEvent_InterludeEnd>("InterludeEnd");
        FlipOn<GameEventManager.GameEvent_ObjectStateChange>("ObjectStateChange");
        FlipOn<GameEventManager.GameEvent_CollisionGridObjectChange>("CollisionGridObjectChange");
        FlipOn<GameEventManager.GameEvent_ObjectControllerEnteredPhase>("ObjectControllerEnteredPhase");
        FlipOn<GameEventManager.GameEvent_ObjectControllerSolved>("ObjectControllerSolved");
        FlipOn<GameEventManager.GameEvent_LevelResumed>("LevelResumed");
        FlipOn<GameEventManager.GameEvent_LevelReset>("LevelReset");
        FlipOn<GameEventManager.GameEvent_LevelRandomized>("LevelRandomized");
        FlipOn<GameEventManager.GameEvent_LevelConditionMet>("LevelConditionMet");
        FlipOn<GameEventManager.GameEvent_MenuClose>("MenuClose");
        FlipOn<GameEventManager.GameEvent_MenuDeactivated>("MenuDeactivated");
        FlipOn<GameEventManager.GameEvent_ModalDismissed>("ModalDismissed");
        FlipOn<GameEventManager.GameEvent_MusicQueueTriggered>("MusicQueueTriggered");
        FlipOn<GameEventManager.GameEvent_ObjectPositioned>("ObjectPositioned");
        FlipOn<GameEventManager.GameEvent_ObjectPositionedComplete>("ObjectPositionedComplete");
        FlipOn<GameEventManager.GameEvent_ObjectDrawerChanged>("ObjectDrawerChanged");
        FlipOn<GameEventManager.GameEvent_SurfaceCleaned>("SurfaceCleaned");
    }

    private static void FlipOn<T>(string label) where T : GameEventManager.GameEvent
    {
        Il2CppSystem.Action<GameEventManager.GameEventData> action =
            (Action<GameEventManager.GameEventData>)(_ =>
            {
                if (FlipArmed) DevToolsPlugin.Log.LogInfo($"flip: {Time.frameCount} event {label}");
            });
        KeepAlive.Add(action);
        GameEventManager.AddEventListener<T>(action);
    }

    /// <summary>A list's LevelObjects, read by Count and indexer; empty on any error.</summary>
    private static List<LevelObject> ListItems(Func<object?> read)
    {
        var found = new List<LevelObject>();
        try
        {
            var list = read();
            if (list == null) return found;
            var type = list.GetType();
            var countProp = type.GetProperty("Count");
            var itemProp = type.GetProperty("Item");
            if (countProp == null || itemProp == null) return found;
            var count = (int)(countProp.GetValue(list) ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (itemProp.GetValue(list, new object[] { i }) is LevelObject obj && obj != null) found.Add(obj);
            }
        }
        catch
        {
            // What was read before the error is kept.
        }
        return found;
    }

    /// <summary>A key that names the object the same way on every load: names and sibling places from the level down.</summary>
    private static string KeyOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        var node = t;
        while (node != null && node != root)
        {
            parts.Add($"{node.gameObject.name}#{SameNameIndex(node)}");
            node = node.parent;
        }
        parts.Reverse();
        return (node == null ? "~/" : "") + string.Join("/", parts);
    }

    /// <summary>
    /// Which of its same-named siblings a node is. Not the sibling index: a
    /// drawer re-parents its contents as it opens and closes, and their order
    /// differs from load to load - Paper Plane Supplies' Chalk Tray was child
    /// 16 of the drawer's Contents in one run and 15 in the next (2026-09-27),
    /// which the probe read as objects gone and colours not back.
    /// </summary>
    private static int SameNameIndex(Transform node)
    {
        var parent = node.parent;
        if (parent == null) return 0;
        var name = node.gameObject.name;
        int n = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == node) return n;
            if (child.gameObject.name == name) n++;
        }
        return n;
    }

    private static void Scrub(string arg)
    {
        var li = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = li == null ? null : li.Level;
        if (level == null)
        {
            DevToolsPlugin.Log.LogWarning("scrub: no level running");
            return;
        }

        var target = arg.Trim();
        var fraction = 0.8f;
        var cut = target.LastIndexOf(':');
        if (cut > 0 && float.TryParse(target.Substring(cut + 1), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var f))
        {
            fraction = f;
            target = target.Substring(0, cut);
        }

        AnimScrubObject? scrub = null;
        foreach (var s in level.GetComponentsInChildren<AnimScrubObject>(true))
        {
            if (s == null) continue;
            var id = target.Contains("#") ? KeyOf(s.transform, level.transform) : Str(() => s.gameObject.name);
            if (id.Equals(target, StringComparison.OrdinalIgnoreCase)) { scrub = s; break; }
        }
        if (scrub == null)
        {
            DevToolsPlugin.Log.LogWarning($"scrub: no scrub object named '{target}'");
            return;
        }
        var handle = scrub.objectToReference;
        if (handle == null)
        {
            DevToolsPlugin.Log.LogWarning($"scrub: '{target}' has no handle (objectToReference)");
            return;
        }

        var before = scrub.ScrubPercent;
        var from = scrub.StartPoint;
        var to = scrub.EndPoint;
        var was = handle.transform.position;
        var during = before;
        var data = new GameEventManager.GameEventData();
        data.LevelObject = handle;
        data.GameObject = handle.gameObject;
        try
        {
            scrub.ScrubPickedUp(data);
            for (int i = 1; i <= 5; i++)
            {
                var p = Vector2.Lerp(from, to, fraction * i / 5f);
                handle.transform.position = new Vector3(p.x, p.y, was.z);
                data.Position = p;
                scrub.Scrubbing(data);
            }
            during = scrub.ScrubPercent;
            scrub.ScrubReleased(data);
        }
        finally
        {
            handle.transform.position = was;
        }
        DevToolsPlugin.Log.LogInfo(
            $"scrub: '{Str(() => scrub.gameObject.name)}' percent before={F(before)} during={F(during)}"
            + $" after={F(scrub.ScrubPercent)} (handle moved {F(fraction)} of the way)");
    }

    private static string B(bool b) => b ? "true" : "false";

    private static string F(float f) => float.IsNaN(f) || float.IsInfinity(f)
        ? "null"
        : Math.Round(f, 3).ToString(CultureInfo.InvariantCulture);

}
