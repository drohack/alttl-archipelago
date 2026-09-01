using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ALTTLProbe;

/// <summary>
/// The Phase 0 verification gate. Four questions the randomizer design rests
/// on that have never been observed working:
///
///   S1  does GameEvent_ObjectControllerSolved actually fire, per controller
///   S2  does a real solve report a real SolutionId
///   S3  can one controller be made inert without breaking the level
///   S6  can a card be held locked against the player
///
/// S4 (the controller dependency graph) is folded into the existing solution
/// survey as an extra column, and S5 (border recolour) is the tint command.
///
/// None of this ships. It exists to answer questions, and the answers are
/// recorded in docs/verification-log.md.
/// </summary>
internal static class Phase0
{
    private static string GameDir => Path.GetDirectoryName(Application.dataPath)!;
    private static string WatchLog => Path.Combine(GameDir, "BepInEx", "alttl-watch.log");

    // ------------------------------------------------------------- S1 and S2

    /// <summary>
    /// Appends one line per gameplay event with whatever identifying detail it
    /// carries. Written to its own file so a hand-play session produces a
    /// readable transcript instead of being buried in the BepInEx log.
    /// </summary>
    internal static void LogWatch(string label, GameEventManager.GameEventData data)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append("  ").Append(label.PadRight(26));

            var oc = data?.ObjectController;
            if (oc != null)
            {
                sb.Append(" controller=").Append(Quote(Safe(() => oc.gameObject.name)));
                sb.Append(" type=").Append(Safe(() => oc.GetIl2CppType().Name));
                sb.Append(" solutionIndex=").Append(Safe(() => oc.SolutionId.ToString()));
                sb.Append(" isSolved=").Append(Safe(() => oc.IsSolved.ToString()));
            }

            var lo = data?.LevelObject;
            if (lo != null) sb.Append(" object=").Append(Quote(Safe(() => lo.gameObject.name)));

            var li = data?.LevelInterface;
            if (li != null)
            {
                sb.Append(" level=").Append(Quote(Safe(() => li.LevelId)));
                sb.Append(" found=").Append(Safe(() => li.NumSolutionsFound.ToString()));
                sb.Append('/').Append(Safe(() => li.SolutionCount.ToString()));
            }

            if (!string.IsNullOrEmpty(data?.SolutionId))
                sb.Append(" SolutionId=").Append(Quote(data.SolutionId));

            File.AppendAllText(WatchLog, sb.ToString() + Environment.NewLine);
            ProbePlugin.Log.LogInfo(sb.ToString());
        }
        catch (Exception e)
        {
            ProbePlugin.Log.LogWarning($"watch log failed for {label}: {e.Message}");
        }
    }

    // -------------------------------------------------------------------- S3

    /// <summary>
    /// "inert:NAME" makes one controller's objects non-interactive and dim,
    /// using the game's own LevelObject API. "inert:list" names the
    /// controllers on the active level, "inert:off" restores everything.
    ///
    /// This is the most load-bearing unknown in the design: if a controller
    /// cannot be cleanly disabled, partial play collapses and abilities have
    /// to gate whole levels instead.
    /// </summary>
    internal static void Inert(string arg)
    {
        var active = GameManager.Instance.levelManager.ActiveLevelInterface;
        var level = active == null ? null : active.Level;
        if (level == null) { ProbePlugin.Log.LogWarning("inert: no active level"); return; }

        var controllers = level.objectControllers;
        var count = controllers == null ? 0 : controllers.Count;

        if (arg.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ProbePlugin.Log.LogInfo($"inert: {count} controllers on {Safe(() => level.LevelId)}");
            for (int i = 0; i < count; i++)
            {
                var c = controllers![i];
                if (c == null) continue;
                ProbePlugin.Log.LogInfo(
                    "   " + Quote(Safe(() => c.gameObject.name))
                    + " [" + Safe(() => c.GetIl2CppType().Name) + "]"
                    + " objects=" + Safe(() => c.ManagedObjects == null
                        ? "0" : c.ManagedObjects.Count.ToString())
                    + " deps=" + Safe(() => c.dependencies == null
                        ? "0" : c.dependencies.Count.ToString()));
            }
            return;
        }

        bool restore = arg.Equals("off", StringComparison.OrdinalIgnoreCase);
        int touchedControllers = 0, touchedObjects = 0;

        for (int i = 0; i < count; i++)
        {
            var c = controllers![i];
            if (c == null) continue;
            var name = Safe(() => c.gameObject.name);
            if (!restore && !name.Equals(arg, StringComparison.OrdinalIgnoreCase)) continue;

            touchedControllers++;
            var objects = c.ManagedObjects;
            for (int k = 0; k < (objects == null ? 0 : objects.Count); k++)
            {
                var lo = objects![k];
                if (lo == null) continue;
                try
                {
                    lo.SetInteractable(restore);
                    lo.SetPreventSelection(!restore);
                    Tint(lo, restore ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.6f));
                    touchedObjects++;
                }
                catch (Exception e)
                {
                    ProbePlugin.Log.LogWarning($"inert: object {k} threw: {e.Message}");
                }
            }
        }

        ProbePlugin.Log.LogInfo(
            $"inert: {(restore ? "restored" : "disabled")} {touchedControllers} controller(s),"
            + $" {touchedObjects} object(s)");
    }

    private static void Tint(LevelObject lo, Color c)
    {
        if (lo.renderer != null) lo.renderer.color = c;
        var subs = lo.subrenderers;
        for (int i = 0; i < (subs == null ? 0 : subs.Count); i++)
        {
            if (subs![i] != null) subs[i].color = c;
        }
    }

    // -------------------------------------------------------------------- S5

    /// <summary>
    /// "tint" recolours every level-select card border; "tint:refresh" then
    /// forces RefreshIconAppearance on each icon, to see whether the tint
    /// survives the game repainting them.
    /// </summary>
    internal static void TintCards(bool refreshAfter)
    {
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track == null) { ProbePlugin.Log.LogWarning("tint: open menu:levels first"); return; }

        var items = track.trackItems;
        int tinted = 0;
        for (int i = 0; i < (items == null ? 0 : items.Count); i++)
        {
            var icon = items![i];
            if (icon == null || icon.borderImage == null) continue;
            // Archipelago tracker convention, cycled so all four are visible.
            icon.borderImage.color = (i % 4) switch
            {
                0 => new Color(0.85f, 0.20f, 0.20f),   // red
                1 => new Color(0.90f, 0.75f, 0.15f),   // yellow
                2 => new Color(0.25f, 0.70f, 0.30f),   // green
                _ => new Color(0.45f, 0.45f, 0.45f),   // grey
            };
            tinted++;
        }
        ProbePlugin.Log.LogInfo($"tint: recoloured {tinted} borders");

        if (!refreshAfter) return;
        for (int i = 0; i < (items == null ? 0 : items.Count); i++)
        {
            if (items![i] != null) items[i].RefreshIconAppearance();
        }
        ProbePlugin.Log.LogInfo("tint: forced RefreshIconAppearance on every icon -"
            + " compare the screenshots to see whether the tint survived");
    }

    // -------------------------------------------------------------------- S6

    /// <summary>"lockcard:N" blocks a level index; "lockcard:off" clears.</summary>
    internal static void LockCard(string arg)
    {
        if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            CardLock.Blocked.Clear();
            ProbePlugin.Log.LogInfo("lockcard: cleared");
            return;
        }
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            ProbePlugin.Log.LogWarning($"lockcard: not a level index: {arg}");
            return;
        }
        CardLock.Blocked.Add(n);
        ProbePlugin.Log.LogInfo(
            $"lockcard: level {n} blocked ({CardLock.Blocked.Count} total,"
            + $" refusals so far {CardLock.Refusals})");
    }

    /// <summary>
    /// "clickcard:N" invokes LevelIcon.DoStartLevel on the icon for that level
    /// - the exact method a real click funnels into - so the lock guard can be
    /// exercised without synthetic mouse input.
    /// </summary>
    internal static void ClickCard(string arg)
    {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            ProbePlugin.Log.LogWarning($"clickcard: not a level index: {arg}");
            return;
        }
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track == null) { ProbePlugin.Log.LogWarning("clickcard: open menu:levels first"); return; }

        var items = track.trackItems;
        for (int i = 0; i < (items == null ? 0 : items.Count); i++)
        {
            var icon = items![i];
            if (icon == null || icon.level == null) continue;
            if (icon.level.LevelIndex != n) continue;
            var before = CardLock.Refusals;
            ProbePlugin.Log.LogInfo($"clickcard: invoking DoStartLevel on {icon.level.LevelId}");
            icon.DoStartLevel();
            ProbePlugin.Log.LogInfo(
                $"clickcard: returned; refusals {before} -> {CardLock.Refusals}");
            return;
        }
        ProbePlugin.Log.LogWarning($"clickcard: no icon on the track for level {n}");
    }

    private static string Quote(string s) => "\"" + s + "\"";

    private static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch (Exception e) { return "<err:" + e.GetType().Name + ">"; }
    }
}

/// <summary>
/// S6. LevelIcon.DoStartLevel is the single choke point every launch path
/// funnels through - OnPointerClick, IconSelected and UnlockableIconSelected
/// all end there - so one prefix is enough to hold a card locked.
/// </summary>
[HarmonyPatch]
internal static class CardLock
{
    internal static readonly HashSet<int> Blocked = new();

    /// <summary>
    /// Counts refusals so a test can assert the patch FIRED, not merely that
    /// it was applied. A patch that silently failed to attach looks identical
    /// to one that was never needed.
    /// </summary>
    internal static int Refusals;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.DoStartLevel))]
    internal static bool BeforeDoStartLevel(LevelIcon __instance)
    {
        try
        {
            var level = __instance.level;
            if (level == null) return true;
            if (!Blocked.Contains(level.LevelIndex)) return true;
            Refusals++;
            ProbePlugin.Log.LogInfo(
                $"lockcard: refused launch of {level.LevelId} (refusals={Refusals})");
            return false;
        }
        catch
        {
            return true;   // never let the guard break the game
        }
    }
}
