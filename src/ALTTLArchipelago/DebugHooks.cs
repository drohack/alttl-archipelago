using System;
using System.Collections.Generic;
using System.Linq;

namespace ALTTLArchipelago;

/// <summary>
/// Entry points for DevTools, which finds this type by name (it has no
/// compile-time reference to the mod). Test hooks only; nothing in the mod
/// calls them.
/// </summary>
public static class DebugHooks
{
    /// <summary>
    /// Treat these abilities as not held, whatever the server sent, and
    /// re-apply the locks now. "none" (or empty) releases them all.
    ///
    /// So one seed holding every ability stands in for any held set in a
    /// hand test: droha, 2026-09-26, "can't you just devtools it? why do you
    /// always go back to making a seed?" and "please just add an option in
    /// devtools to revoke abilities". The set lives on the session's
    /// AbilityState, which every connect rebuilds, so it never outlasts the
    /// session it was set in.
    /// </summary>
    /// <returns>A line for the DevTools log.</returns>
    public static string Revoke(string csv)
    {
        try
        {
            var state = Inventory.Abilities;
            if (state == null) return "revoke: no run is active";

            var names = (csv ?? "")
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(n => !n.Equals("none", StringComparison.OrdinalIgnoreCase))
                .ToList();

            state.Withhold(names);
            AbilityLocks.AbilitiesChanged();
            Badges.RepaintSoon();
            Checks.NoteAbilitiesChanged();

            var withheld = state.Withheld.Count == 0 ? "none" : string.Join(", ", state.Withheld);
            Plugin.Logger.LogInfo($"abilities: revoked for testing: {withheld}");
            return $"revoke: now revoked {withheld}";
        }
        catch (Exception e)
        {
            return $"revoke: failed: {e.Message}";
        }
    }

    /// <summary>
    /// "off": count no Background Change Traps for the rest of this game
    /// session, so every level loads in its own colour; "on" counts them
    /// again. A level already on screen keeps the colour it was painted with
    /// until it is loaded again - the mod does not keep the level's own.
    /// </summary>
    /// <returns>A line for the DevTools log.</returns>
    public static string Traps(string arg)
    {
        try
        {
            var off = (arg ?? "").Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
            Inventory.BackgroundTrapsOffForTesting = off;
            Inventory.Recount();
            Plugin.Logger.LogInfo(
                $"backgrounds: background traps {(off ? "ignored" : "counted")} for testing");
            return $"traps: background traps {(off ? "off" : "on")}, "
                   + $"{Inventory.BackgroundTraps} counted - reload the level to repaint it";
        }
        catch (Exception e)
        {
            return $"traps: failed: {e.Message}";
        }
    }
}
