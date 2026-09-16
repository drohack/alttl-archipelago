using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The `why:` diagnostic: what a card's badge is reading, and why.
///
/// Debugging surface, not player-facing. It exists because "that badge looks
/// wrong" was answerable only by reasoning about three tables at once.
/// </summary>
internal static partial class Badges
{
    private static float _sinceWhy;

    /// <summary>
    /// Answer a "why is this badge that colour" question left in a file.
    ///
    /// Diagnostic only. It exists because a badge that reads wrong is otherwise
    /// unarguable: the state is derived from the seed's requirements, what has
    /// been collected, packs held and abilities held, and only listing all four
    /// per location says which one is responsible.
    /// </summary>
    internal static void TickWhy(float dt)
    {
        // Off unless someone asked for it.
        //
        // This was a Path.Combine and a File.Exists every second, forever, in
        // the shipping mod - the DevTools command-file pattern copied into
        // player-facing code, for a diagnostic no shipping code reads. It is
        // genuinely useful when a badge looks wrong, so it is a switch rather
        // than a deletion.
        if (!Plugin.WhyProbeEnabled) return;

        _sinceWhy += dt;
        if (_sinceWhy < 1f) return;
        _sinceWhy = 0f;

        try
        {
            var path = System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath, "alttl-why.txt");
            if (!System.IO.File.Exists(path)) return;

            var text = System.IO.File.ReadAllText(path).Trim();
            System.IO.File.Delete(path);
            if (!int.TryParse(text, out var position)) return;

            Explain(position);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"why: {e.Message}");
        }
    }

    private static void Explain(int position)
    {
        var seed = Plugin.Seed;
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (seed == null || progress == null || abilities == null || state == null) return;

        var slot = Track.SlotAt(position);
        if (slot < 0)
        {
            Plugin.Logger.LogInfo($"why: position {position} is not a puzzle");
            return;
        }

        var entry = seed.Slots[slot];
        var status = progress.StatusOf(
            slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

        Plugin.Logger.LogInfo(
            $"why: position {position} = slot {slot} {entry.LevelId} "
            + $"#{entry.Instance}, badge {status}, {state.PacksHeld} packs held");

        foreach (var name in Checks.Router!.ForSlot(slot))
        {
            var collected = Checks.Ledger.IsCollected(name);
            var reachable = progress.IsReachable(name, state.PacksHeld, abilities);
            seed.Requirements.TryGetValue(name, out var need);

            Plugin.Logger.LogInfo(
                $"why:   {(collected ? "done" : "TODO")} "
                + $"{(reachable ? "reachable" : "BLOCKED  ")} {name}"
                + (need == null ? "" : $"  [needs {need.Packs} packs"
                    + (need.Abilities.Count > 0
                        ? ", " + string.Join(" + ", need.Abilities) : "")
                    + "]"));
        }
    }
}
