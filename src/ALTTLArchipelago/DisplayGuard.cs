using System;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Keep the window at the SIZE the player chose, not the index they chose.
///
/// THE BUG THIS EXISTS FOR. The game stores the display choice as
/// Prefs.resolution, an index into SettingsMenu.resolutions - a list it
/// rebuilds from the monitor the game happened to open on. Sorted largest
/// first, so index 0 is that monitor's native resolution, whatever monitor
/// that is. The index is applied at startup and OVERRIDES Unity's own stored
/// width and height: measured, with the registry holding 1280x720 and
/// "use native" off, the game still opened 3840x2160.
///
/// So an index that stops meaning what it meant silently becomes "native".
/// A save here held 35 against a list of 27 entries - out of range, from a
/// different display - and the game fell back to filling the screen. In
/// windowed mode a window the size of the monitor looks exactly like
/// fullscreen, which is how it was reported: "why is my game always opening
/// in full screen mode? I set it to windowed every time." Then, every boot
/// having come up native, "it's not remembering my selection".
///
/// THE FIX IS TO REMEMBER THE SIZE. A width and a height mean the same thing
/// on every display; an index does not. droha, who worked this out before I
/// did: "the game changes the resolution list depending on what monitor
/// opened it, so a number doesn't help me here. What did we say about
/// indexes - they change. Don't use that as valid information."
///
/// WHAT IT WILL NOT DO is pick a size. It only ever re-applies the last size
/// the player was actually running at, and if that size is not offered by
/// the current display it does nothing at all and leaves the game to it.
/// Being opinionated about someone's monitor is how this became a complaint
/// in the first place.
/// </summary>
internal static class DisplayGuard
{
    /// <summary>The size to hold, as width and height. Zero means "not known yet".</summary>
    private static int _rememberedWidth;
    private static int _rememberedHeight;

    /// <summary>Set from the mod config at startup, and written back when it changes.</summary>
    internal static Action<string>? OnRemember { get; set; }

    private static bool _fixed;
    private static float _since;
    private static float _sinceBoot;

    /// <summary>
    /// The size the game opened at, taken on the very first tick.
    ///
    /// Kept so the settle-time correction can tell "the game opened at the
    /// wrong size" from "the player has already changed it". Without it, a
    /// player who opens the settings and picks a size within the first few
    /// seconds would have it taken straight back off them, which is the
    /// exact behaviour this class exists to stop.
    /// </summary>
    private static int _bootWidth;
    private static int _bootHeight;

    /// <summary>
    /// How long to let the game finish opening before touching anything.
    ///
    /// The startup apply happens somewhere in the game's own boot, and
    /// correcting the size before that would just be overwritten by it. This
    /// is a settle, not a race: after it, whatever is on screen is what the
    /// game meant to do, and only then is it worth disagreeing.
    /// </summary>
    private const float SettleSeconds = 6f;

    /// <summary>
    /// Take the remembered size from the config, once, at startup.
    /// An unparseable or empty value means nothing is remembered yet, which
    /// is the correct state for a first run.
    /// </summary>
    internal static void Remember(string setting)
    {
        _rememberedWidth = 0;
        _rememberedHeight = 0;
        if (string.IsNullOrWhiteSpace(setting)) return;

        var parts = setting.Split('x');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[0].Trim(), out var w)) return;
        if (!int.TryParse(parts[1].Trim(), out var h)) return;
        if (w <= 0 || h <= 0) return;

        _rememberedWidth = w;
        _rememberedHeight = h;
    }

    internal static void Tick(float dt)
    {
        if (_bootWidth == 0)
        {
            _bootWidth = Screen.width;
            _bootHeight = Screen.height;
        }

        _sinceBoot += dt;
        if (_sinceBoot < SettleSeconds) return;

        // Once a second is plenty. Nothing here is urgent, and the only
        // thing being watched is a player using a menu.
        _since += dt;
        if (_since < 1f) return;
        _since = 0f;

        try
        {
            if (!_fixed)
            {
                _fixed = true;
                Restore();
                return;
            }

            Track();
        }
        catch (Exception e)
        {
            // A wrong window size is a nuisance. Throwing every second in
            // the ticker over one is worse, so this only ever complains once.
            _fixed = true;
            Plugin.Logger.LogWarning($"display: giving up on the size guard: {e.Message}");
        }
    }

    /// <summary>
    /// Put the window back to the remembered size, if the game did not.
    /// </summary>
    private static void Restore()
    {
        if (_rememberedWidth == 0) return;                  // nothing to hold
        if (Screen.width == _rememberedWidth
            && Screen.height == _rememberedHeight)
        {
            return;                                         // already right
        }

        // ALREADY CHANGED IS ALREADY ANSWERED. If the size is no longer what
        // the game booted with, the player has been into the settings during
        // the settle and that is the newer instruction of the two. droha, on
        // the guard: "it should not override to this always, it's whatever
        // the user sets it to - it should just keep that setting."
        if (Screen.width != _bootWidth || Screen.height != _bootHeight)
        {
            Plugin.Logger.LogInfo(
                $"display: the size was changed to {Screen.width}x{Screen.height} "
                + "since startup, so that is what gets remembered");
            return;
        }

        var menu = GameDisplay.FindSettingsMenu();
        if (menu == null) return;

        var index = GameDisplay.IndexOf(menu, _rememberedWidth, _rememberedHeight);
        if (index < 0)
        {
            // NOT AN ERROR. A display that cannot do 1280x720 is a display
            // that cannot do it; forcing the nearest thing would be picking
            // for them.
            Plugin.Logger.LogInfo(
                $"display: {_rememberedWidth}x{_rememberedHeight} is not offered "
                + "by this display, so the game's own choice stands");
            return;
        }

        GameDisplay.Apply(menu, index, _rememberedWidth, _rememberedHeight);
        Plugin.Logger.LogInfo(
            $"display: put the window back to {_rememberedWidth}x"
            + $"{_rememberedHeight} (index {index} on this display), which the "
            + $"game had opened at {Screen.width}x{Screen.height}");
    }

    /// <summary>
    /// Notice the player changing the size, so the next boot holds the new one.
    /// </summary>
    private static void Track()
    {
        var w = Screen.width;
        var h = Screen.height;
        if (w <= 0 || h <= 0) return;
        if (w == _rememberedWidth && h == _rememberedHeight) return;

        _rememberedWidth = w;
        _rememberedHeight = h;
        OnRemember?.Invoke($"{w}x{h}");
        Plugin.Logger.LogInfo($"display: remembering {w}x{h} for next time");
    }
}
