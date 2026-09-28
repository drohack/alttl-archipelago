using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// No Steam achievements and no Steam stats while this mod is loaded.
///
/// droha, 2026-09-26: Kat got Steam achievements playing the run's credits,
/// and "we should never send any steam achievements when archipelago mod is
/// enabled". A run replays the game out of order, with skips, admin sends and
/// cat traps, so nothing it does says what the achievement claims.
///
/// STOPPED AT STEAMWORKS, which is every route. The interop's own caller
/// counts: SteamUserStats.SetAchievement has ONE caller in the game
/// (AchievementManager) and each SetStat overload one (StatManager). The
/// stats matter as much as the achievements: Steam unlocks a stat-based
/// achievement itself once a counter crosses its line. The game's own funnel,
/// AchievementManager.SetAchievementMet (43 callers), is stopped too, so its
/// local "achieved" flag stays honest and the log can name what was held back.
///
/// On unless [Steam] AllowAchievements is set; the default is to block, as
/// droha asked, whether or not a run is connected.
/// </summary>
[HarmonyPatch]
internal static class SteamAchievements
{
    /// <summary>Names already logged, so a re-sent achievement says so once.</summary>
    private static readonly HashSet<string> _said = new(StringComparer.Ordinal);

    private static bool Blocked => !Plugin.AllowAchievements;

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetAchievementMet))]
    [HarmonyPrefix]
    private static bool BeforeSetAchievementMet(AchievementData achievement)
    {
        // A CHECK FIRST, whatever Steam is allowed. Every award goes through
        // here, including one the player's profile already holds (Exacting
        // Eggs, 2026-09-28), so a run with `achievements` on sees them all.
        string id = "";
        try { id = achievement?.m_eAchievementID.ToString() ?? ""; }
        catch { id = ""; }
        if (id.Length > 0)
        {
            try { Checks.OnAchievement(id); }
            catch (Exception e) { Plugin.Logger.LogWarning($"achievements: could not file {id}: {e.Message}"); }
        }

        if (!Blocked) return true;
        // The id when the name is empty: one award in the 0.4.2 playtest
        // logged as '' because its name was.
        string name;
        try { name = achievement?.m_strName ?? ""; }
        catch { name = ""; }
        if (name.Length == 0) name = id.Length > 0 ? id : "?";
        Say($"achievements: withheld '{name}' - Steam achievements are off while the Archipelago mod is loaded");
        return false;
    }

    [HarmonyPatch(typeof(Steamworks.SteamUserStats), nameof(Steamworks.SteamUserStats.SetAchievement))]
    [HarmonyPrefix]
    private static bool BeforeSetAchievement(string pchName, ref bool __result)
    {
        if (!Blocked) return true;
        __result = false;
        Say($"achievements: withheld Steam achievement '{pchName}'");
        return false;
    }

    [HarmonyPatch(typeof(Steamworks.SteamUserStats), nameof(Steamworks.SteamUserStats.SetStat),
                  new[] { typeof(string), typeof(int) })]
    [HarmonyPrefix]
    private static bool BeforeSetStatInt(string pchName, ref bool __result)
    {
        if (!Blocked) return true;
        __result = false;
        Say($"achievements: withheld Steam stat '{pchName}'");
        return false;
    }

    [HarmonyPatch(typeof(Steamworks.SteamUserStats), nameof(Steamworks.SteamUserStats.SetStat),
                  new[] { typeof(string), typeof(float) })]
    [HarmonyPrefix]
    private static bool BeforeSetStatFloat(string pchName, ref bool __result)
    {
        if (!Blocked) return true;
        __result = false;
        Say($"achievements: withheld Steam stat '{pchName}'");
        return false;
    }

    /// <summary>
    /// Every achievement earnable in the run, whatever the Steam profile holds.
    ///
    /// The game copies the profile's unlocks into each AchievementData's
    /// m_bAchieved, and a checker may skip one already met. droha, 2026-09-28:
    /// "we need the achievements to not care about what the user has". (Keep
    /// Away, which looked like that, stayed silent with the flag cleared too,
    /// and is left out of the checks for it.)
    ///
    /// Only the in-memory flag: SetAchievementMet is stopped above, so nothing
    /// sets it back or reaches Steam. Cleared at every slot entry, restarts
    /// included, in case the game reads the profile again mid-session.
    /// </summary>
    internal static void ForgetUnlocks()
    {
        try
        {
            int cleared = 0;
            foreach (var obj in UnityEngine.Object.FindObjectsOfTypeAll(
                         Il2CppInterop.Runtime.Il2CppType.Of<AchievementData>()))
            {
                var data = obj == null ? null : obj.TryCast<AchievementData>();
                if (data == null || !data.m_bAchieved) continue;
                data.m_bAchieved = false;
                cleared++;
            }
            if (cleared > 0)
            {
                Plugin.Logger.LogInfo(
                    $"achievements: {cleared} held on the Steam profile made earnable in the run");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"achievements: could not clear the held flags: {e.Message}");
        }
    }

    private static void Say(string line)
    {
        try
        {
            if (_said.Add(line)) Plugin.Logger.LogInfo(line);
        }
        catch
        {
            // A log line is not worth a throw into the game's call.
        }
    }
}
