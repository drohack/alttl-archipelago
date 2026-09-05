using System;
using HarmonyLib;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Filler that actually changes something you can see.
///
/// Three quarters of a default seed used to pay out in Title Theme, Colour
/// Scheme and Daily Badge - three names with no code behind them at all. These
/// two replace them: Level Background recolours the puzzle backdrop, Menu
/// Background recolours the pause screen.
///
/// THE COLOUR IS A FUNCTION OF THE COUNT, not a reaction to an arrival, and
/// that is the whole design. Archipelago replays the entire item list on every
/// connect, so a handler that stepped the palette forward when an item landed
/// would step it forward again on every login, and the player would find a
/// different colour every time they started the game. Indexing by how many
/// have arrived makes a replay land exactly where it already was. It is the
/// same rule that fixed the pack-doubling bug, applied before it could bite.
/// </summary>
internal static class Backgrounds
{
    /// <summary>
    /// The game's own background colours, read once.
    ///
    /// Using the shipped palette rather than inventing colours means a
    /// recoloured puzzle still looks like the game drew it - these are the
    /// exact values the levels themselves are authored against.
    /// </summary>
    private static Color[]? _palette;

    private static Color[] Palette()
    {
        if (_palette != null) return _palette;

        try
        {
            foreach (var obj in UnityEngine.Object.FindObjectsOfTypeAll(
                         Il2CppInterop.Runtime.Il2CppType.Of<ColorSchemesData>()))
            {
                var data = obj == null ? null : obj.TryCast<ColorSchemesData>();
                if (data == null) continue;

                // levelColorSchemes rather than the BackgroundColors property
                // that wraps it - one layer fewer, and BackgroundColors is
                // recomputed on every access. Both work; measured.
                //
                // Worth recording what did NOT go wrong here, because it cost
                // an hour: reading the palette appeared to fail with "Index was
                // outside the bounds of the array" on element zero, through
                // four different access paths, while Length and Count both
                // reported 10. None of the indexers was at fault. The throw
                // came from ColorUtility.ToHtmlStringRGB in the LOGGING, and
                // interop reported it as an array bounds error. Do not read an
                // exception's text as naming the call that raised it.
                var schemes = data.levelColorSchemes;
                if (schemes == null || schemes.Length == 0) continue;

                var built = new Color[schemes.Length];
                for (int i = 0; i < schemes.Length; i++)
                {
                    var scheme = schemes[i];
                    if (scheme != null) built[i] = scheme.backgroundColor;
                }
                _palette = built;

                Plugin.Logger.LogInfo($"backgrounds: palette of {built.Length} colours");
                return _palette;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"backgrounds: could not read the palette: {e.Message}");
        }

        // Empty rather than null, so every caller below is a no-op instead of
        // a special case. A run with no palette simply keeps stock colours.
        _palette = Array.Empty<Color>();
        return _palette;
    }


    /// <summary>
    /// The colour for a given number of received items, or null for none held.
    ///
    /// Wraps with a modulo rather than clamping at the end of the palette: a
    /// long run can hold more of these than the game has colours, and stopping
    /// on the last one would make every item after it do nothing.
    /// </summary>
    private static Color? ColourFor(int count)
    {
        if (count <= 0) return null;

        var palette = Palette();
        if (palette.Length == 0) return null;

        // count - 1 so the FIRST item lands on palette[0]. Off by one here is
        // invisible in testing and means the first background item you are
        // ever sent skips a colour.
        return palette[(count - 1) % palette.Length];
    }


    /// <summary>What the puzzle backdrop should be, or null to leave it alone.</summary>
    internal static Color? ForLevel() => ColourFor(Inventory.LevelBackgrounds);

    /// <summary>What the pause screen should be, or null to leave it alone.</summary>
    internal static Color? ForMenu() => ColourFor(Inventory.MenuBackgrounds);


    /// <summary>
    /// Paint the level.
    ///
    /// Called from Checks.EnterSlot and from Inventory.Apply, NOT from a
    /// postfix on LevelManager.StartLevel - that was the first attempt and it
    /// silently lost. The level's own setup runs after StartLevel returns and
    /// writes the camera from its own colour, so ours was overwritten a
    /// fraction of a second later, with a perfectly healthy log and a stock
    /// backdrop on screen.
    ///
    /// Written once per level start rather than polled per frame: the game
    /// only sets the backdrop when a level loads, so there is nothing to fight
    /// for the rest of the puzzle.
    /// </summary>
    internal static void ApplyToLevel()
    {
        try
        {
            if (!Track.Active) return;

            var wanted = ForLevel();
            if (wanted == null) return;

            var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
            if (li == null) return;

            li.BackgroundColor = wanted.Value;

            var level = li.Level;
            if (level != null) level.backgroundColor = wanted.Value;
        }
        catch (Exception e)
        {
            // Cosmetic. Never let a colour stop a puzzle loading.
            Plugin.Logger.LogWarning($"backgrounds: level recolour failed: {e.Message}");
        }
    }


    /// <summary>
    /// Hold the backdrop at the colour the player has earned.
    ///
    /// A per-frame check rather than a one-shot write, and it took three
    /// attempts to accept that. A postfix on LevelManager.StartLevel lost, and
    /// so did a call from Checks.EnterSlot: in both cases
    /// LevelInterface.BackgroundColor read back correctly afterwards while
    /// Camera.main.backgroundColor - the thing that actually renders - still
    /// held the level's own colour. The game paints the camera somewhere later
    /// in level setup than either hook, and chasing the exact moment is a
    /// worse bet than simply outlasting it.
    ///
    /// The cost is one Color comparison per frame, and only while a run is on
    /// and the player holds a Level Background. The write happens on the
    /// handful of frames where the game has just overwritten us. That is a
    /// fair price for a feature that otherwise silently does nothing.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (!Track.Active) return;

            var wanted = ForLevel();
            if (wanted == null) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (cam.backgroundColor == wanted.Value) return;

            cam.backgroundColor = wanted.Value;
        }
        catch
        {
            // Cosmetic, and on the frame path. Silence is right here: a
            // warning per frame would bury the log.
        }
    }



}
