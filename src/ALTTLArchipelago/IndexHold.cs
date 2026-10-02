using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// The backstop behind AbilityLocks.HoldButtons, which turns a held state's
/// button colliders off so no press reaches it: a press that reaches one
/// anyway is refused (IndexIncrementTrigger.OnTriggerClicked) and logged as
/// a warning. Refusing a press mid-handling is what left Robot 3's hearts
/// container switched off (droha's A/B, 2026-10-01), so this line in a log
/// says a button has a collider HoldButtons does not know about.
///
/// ONLY THE PRESS, NOT THE STATE. The first builds also refused
/// IndexedAttribute.Next, Prev and SetIndex. The game sets states itself: Robot
/// 8's head by SetIndex every frame, which the hold kept at 0 until Ordering
/// came, and the state change it then fired mid-level left Robot 3's hearts
/// container switched off, so no heart would go in (DevTools trace, droha's
/// A/B, 2026-10-01).
///
/// Its own patch class, so a game update that renames this costs only this.
/// </summary>
[HarmonyPatch]
internal static class IndexHold
{
    /// <summary>Held states refused at least once, for one line each.</summary>
    private static readonly HashSet<int> _said = new();

    [HarmonyPatch(typeof(IndexIncrementTrigger), nameof(IndexIncrementTrigger.OnTriggerClicked))]
    [HarmonyPrefix]
    private static bool BeforeTriggerClicked(IndexIncrementTrigger __instance)
    {
        if (AbilityLocks.HeldIndexes.Count == 0) return true;
        try
        {
            var attr = __instance == null ? null : __instance.IndexedAttribute;
            if (attr == null) return true;
            var id = attr.GetInstanceID();
            if (!AbilityLocks.HeldIndexes.Contains(id)) return true;
            if (_said.Count > 64) _said.Clear();
            if (_said.Add(id))
            {
                var name = attr.attachedObject != null ? attr.attachedObject.gameObject.name : attr.gameObject.name;
                Plugin.Logger.LogWarning(
                    $"abilities: a press reached held '{name}' past its turned-off button; refused");
            }
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"abilities: index hold failed, letting the press through: {e.Message}");
            return true;
        }
    }
}
