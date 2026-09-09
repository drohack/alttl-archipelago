using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ALTTLArchipelago;

/// <summary>
/// Filler that actually changes something you can see.
///
/// Three quarters of a default seed used to pay out in Title Theme, Colour
/// Scheme and Daily Badge - three names with no code behind them at all. The
/// Background Change Trap replaces them, and it recolours all three backdrops
/// the player ever looks at: the puzzle, the pause screen and the level select
/// track.
///
/// ONE ITEM, NOT TWO. This shipped as "Level Background" and "Menu Background",
/// which were separate items doing the same job on different screens and left
/// the level select untouched. A TRAP because a backdrop drawn from the game's
/// palette can land close to the colour of the pieces in front of it - Legible
/// below fights that where it can, and where it cannot the puzzle gets harder
/// to read, which droha decided was funny rather than broken.
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


    /// <summary>
    /// The colour chosen for the level currently on screen, and which level it
    /// was chosen for. Recomputed when either changes, because reading every
    /// renderer in the puzzle is not something to do on the frame path.
    /// </summary>
    private static string? _chosenFor;
    private static int _chosenCount = -1;
    private static Color? _chosen;

    /// <summary>
    /// How far apart two colours have to be before a piece is legible against
    /// the backdrop. Weighted towards green because eyes are.
    ///
    /// This is a threshold on the same scale as Distance below, whose maximum
    /// is 3 for black against white. 0.34 rejects a near-match and a little
    /// more; it is deliberately not aggressive, since refusing too much just
    /// pushes every level onto the same few palette entries.
    /// </summary>
    private const float MinContrast = 0.34f;

    /// <summary>
    /// Perceptual-ish distance between two colours: plain Euclidean RGB reads
    /// green as no more important than blue, which it very much is.
    /// </summary>
    private static float Distance(Color a, Color b)
    {
        var dr = a.r - b.r;
        var dg = a.g - b.g;
        var db = a.b - b.b;
        return Mathf.Sqrt(2f * dr * dr + 4f * dg * dg + 3f * db * db);
    }

    /// <summary>
    /// The colours the puzzle's pieces are actually drawn in.
    ///
    /// Read off the renderers rather than any colour table, because what
    /// matters is what is on screen. Capped: a big level has hundreds of
    /// objects and the answer stops changing long before that.
    /// </summary>
    private static List<Color> PieceColours(LevelInterface li)
    {
        var found = new List<Color>();
        try
        {
            var level = li.Level;
            var controllers = level?.objectControllers;
            if (controllers == null) return found;

            // Reached through objectControllers rather than off the Level
            // itself. Level is only ever used for FIELD access anywhere in this
            // codebase - backgroundColor and objectControllers - so whether it
            // is a Component at all is unverified, and GetComponentsInChildren
            // on it would be a guess that fails at COMPILE time. An
            // ObjectController is known to be one: Checks reads oc.gameObject.
            for (int i = 0; i < controllers.Count && found.Count < 200; i++)
            {
                var oc = controllers[i];
                var go = oc == null ? null : oc.gameObject;
                if (go == null) continue;

                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (sr == null) continue;
                    found.Add(sr.color);
                    if (found.Count >= 200) break;
                }
            }
        }
        catch
        {
            // Cosmetic. An unreadable level simply gets the unadjusted colour.
        }
        return found;
    }

    /// <summary>
    /// Step past a palette entry the pieces would vanish into.
    ///
    /// The bug this fixes: ColourFor takes backgroundColor OUT of one of the
    /// game's LevelColorSchemes and applies it to a level the scheme was never
    /// paired with. The game's own schemes keep a background away from the
    /// colours of the pieces in front of it; indexing the palette by a received
    /// -item count throws that pairing away, and a piece whose art has no
    /// outline then disappears into the backdrop completely.
    ///
    /// Rather than invent a colour, this walks the palette from the entry the
    /// count picked and takes the first that clears every piece on screen. A
    /// full lap without a winner keeps the original: a legible-but-wrong colour
    /// beats no background item working at all, and that case is worth a log
    /// line because it means the palette and the level really do clash.
    /// </summary>
    private static Color Legible(Color wanted, LevelInterface li, int count)
    {
        var palette = Palette();
        if (palette.Length == 0) return wanted;

        var pieces = PieceColours(li);
        if (pieces.Count == 0) return wanted;

        for (int step = 0; step < palette.Length; step++)
        {
            var candidate = palette[(count - 1 + step) % palette.Length];

            var worst = float.MaxValue;
            foreach (var piece in pieces)
            {
                // Something almost transparent is not what anyone is looking
                // at, and letting it veto a colour rejects nearly everything.
                if (piece.a < 0.2f) continue;
                var d = Distance(candidate, piece);
                if (d < worst) worst = d;
            }

            if (worst >= MinContrast)
            {
                if (step > 0)
                {
                    Plugin.Logger.LogInfo(
                        $"backgrounds: skipped {step} palette colour(s) the pieces "
                        + "would have vanished into");
                }
                return candidate;
            }
        }

        Plugin.Logger.LogWarning(
            "backgrounds: no palette colour clears this level's pieces, keeping the first");
        return wanted;
    }

    /// <summary>What the puzzle backdrop should be, or null to leave it alone.</summary>
    internal static Color? ForLevel()
    {
        var count = Inventory.BackgroundTraps;
        var wanted = ColourFor(count);
        if (wanted == null) return null;

        var li = GameManager.Instance?.levelManager?.ActiveLevelInterface;
        if (li == null) return wanted;

        // Cached per (level, count): Tick asks every frame, and scanning the
        // renderers that often would be a real cost for a cosmetic.
        var id = li.LevelId;
        if (_chosen != null && _chosenCount == count && _chosenFor == id) return _chosen;

        _chosenFor = id;
        _chosenCount = count;
        _chosen = Legible(wanted.Value, li, count);
        return _chosen;
    }

    /// <summary>What the pause screen should be, or null to leave it alone.</summary>
    internal static Color? ForMenu() => ColourFor(Inventory.BackgroundTraps);


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
    /// and the player holds a Background Change Trap. The write happens on the
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
