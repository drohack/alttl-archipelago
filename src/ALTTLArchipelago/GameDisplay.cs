using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// The three things the mod needs from the game's own display settings.
///
/// Kept apart from <see cref="DisplayGuard"/> so the policy - which size to
/// hold, and when - reads without the interop noise, and so the awkward
/// parts each carry the reason they are awkward.
/// </summary>
internal static class GameDisplay
{
    /// <summary>
    /// The settings screen, open or not.
    ///
    /// FindObjectOfType only sees ACTIVE objects, and the settings screen is
    /// switched off almost all of the time - including at the title, which is
    /// exactly when this runs. FindObjectsOfTypeAll does not have that
    /// problem. Its list is populated during the game's own startup, so it is
    /// readable well before anyone opens the screen.
    /// </summary>
    internal static SettingsMenu? FindSettingsMenu()
    {
        try
        {
            // The interop shim only exposes the non-generic overload, which
            // returns UnityEngine.Object, so the type comes from Il2CppType
            // and each result needs a TryCast.
            var found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<SettingsMenu>());
            if (found == null) return null;

            for (int i = 0; i < found.Length; i++)
            {
                var menu = found[i]?.TryCast<SettingsMenu>();
                if (menu != null && menu.resolutions != null) return menu;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"display: could not find the settings menu: {e.Message}");
        }
        return null;
    }

    /// <summary>
    /// Where a SIZE sits in the list this display produced.
    ///
    /// The whole point of the guard: the answer is looked up fresh every
    /// time, because the same size has a different index on a different
    /// monitor and a stored index is a stored lie.
    /// </summary>
    internal static int IndexOf(SettingsMenu menu, int width, int height)
    {
        var list = menu.resolutions;
        if (list == null) return -1;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].width == width && list[i].height == height) return i;
        }
        return -1;
    }

    /// <summary>
    /// Apply a size, to the window AND to the save.
    ///
    /// BOTH, because they are separate stores and either one alone drifts.
    /// SettingsMenu.SetResolution moves the window and writes Unity's own
    /// registry; measured, it does not touch Prefs. Writing only that leaves
    /// the save still naming the old index, and the save is the one the game
    /// reads at startup - so the next launch would undo this.
    ///
    /// Fullscreen is left exactly as it is. The size was the complaint; the
    /// window mode is the player's to set, and a guard that quietly changed
    /// it would be the same kind of bug wearing different clothes.
    /// </summary>
    internal static void Apply(SettingsMenu menu, int index, int width, int height)
    {
        menu.SetResolution(index);

        try
        {
            var data = SaveSystem.data;
            if (data == null || data.playerPrefs == null) return;

            data.playerPrefs.SetResolution(index);
            SaveSystem.SaveGame();
        }
        catch (Exception e)
        {
            // The window is already the right size; only the memory of it
            // failed. Worth saying, not worth abandoning.
            Plugin.Logger.LogWarning(
                $"display: set {width}x{height} but could not record it: {e.Message}");
        }
    }
}
