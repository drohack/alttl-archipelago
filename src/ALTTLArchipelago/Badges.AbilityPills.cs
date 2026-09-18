using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The ability strip above the track: twelve pills, each an icon the mod
/// ships plus a three-letter caption, dimmed until the mechanic is held.
/// </summary>
internal static partial class Badges
{
    private static float _sincePills;

    private static RectTransform? _pills;

    private static readonly List<string> _pillOrder = new();

    /// <summary>
    /// The tiles, in the same order as _pillOrder.
    ///
    /// HELD, NOT LOOKED UP BY INDEX. The repaint used to walk the strip's
    /// children by position, which was true right up until a backing panel
    /// was added as the first child - and then every icon drew under its
    /// neighbour's label. Books appeared over SWP's slot reading STK, and
    /// the twelfth tile had nothing in it at all. An index into a child
    /// list is a fact about the hierarchy; this is a fact about the strip.
    /// </summary>
    private static readonly List<RectTransform> _pillTiles = new();

    /// <summary>Held, and locked. Deliberately far apart so a glance reads.</summary>
    private static readonly Color PillHeld = new(0.25f, 0.70f, 0.30f, 0.85f);

    private static readonly Color PillLocked = new(0.16f, 0.16f, 0.19f, 0.75f);

    /// <summary>
    /// Locked art: dark and half transparent, so a locked mechanic reads as
    /// absent at a glance while its shape is still there to be recognised
    /// once you hold it.
    /// </summary>
    private static readonly Color IconLocked = new(0.42f, 0.42f, 0.46f, 0.55f);

    /// <summary>
    /// Three letters per mechanic. Short enough to fit, long enough to guess.
    ///
    /// LETTERS FIRST, ICONS LATER, and both in the end.
    /// droha's call was to build the pills with text first and then go looking
    /// for icons, because the game's own sprites are borrowed by name and that
    /// had twice been guessed wrong in this file. The looking was done with the
    /// DevTools `sprites` and `spriteexport` commands, and it succeeded: the
    /// mod now ships thirteen icons of its own as embedded resources, which
    /// LoadPillArt draws. See docs/data/ability-icons.md for where each picture
    /// came from. The letters stayed: they are the caption UNDER each icon, so
    /// a picture nobody recognises still names its mechanic.
    /// </summary>
    private static readonly Dictionary<string, string> PillText =
        new(StringComparer.Ordinal)
        {
            ["Swapping"] = "SWP",
            ["Stacking"] = "STK",
            ["Ordering"] = "ORD",
            ["Gadgets"] = "GAD",
            ["Rotating"] = "ROT",
            ["Grids"] = "GRD",
            ["Tidying"] = "TDY",
            ["Containers"] = "CON",
            ["Drawer"] = "DRW",
            ["Sticking"] = "STI",
            ["Symmetry"] = "SYM",
            ["Jigsaw"] = "JIG",
            // Seeing Stars. The only DLC mechanic with no base-game
            // equivalent, so the only one that needed a new pill.
            ["Distributing"] = "DST",
        };

    /// <summary>
    /// A borrowed item sprite per mechanic, and how it was chosen.
    ///
    /// THE GAME HAS NO ABILITY ART. Abilities are the mod's invention, so
    /// there is nothing to look up - the Icon- family is per-LEVEL card art,
    /// not per-mechanic. What the game does have is a large set of small
    /// single-object badge sprites, and those are what these are.
    ///
    /// CHOSEN BY LOOKING, not by reading names. droha: "look for ones easy
    /// to distinguish at a glance." A name says nothing about silhouette or
    /// how something reads at twenty pixels, so the DevTools `spritegrid`
    /// command drew batches of candidates on screen at full size and at
    /// icon size, and three rounds of that threw out everything thin or
    /// low-contrast - a hammer, nails, callipers, a key, dice and scissors
    /// all vanish when small - and everything that collided with a
    /// neighbour on colour.
    ///
    /// The mapping is a metaphor, not a fact, which is why the letters stay
    /// underneath. Two coins changing hands for Swapping, a record for
    /// Rotating because it spins, a floppy disk for Gadgets, a sponge for
    /// Tidying, a box for Drawer, gem shards for Jigsaw because they fit
    /// together. Nobody would guess all twelve; with the label under each
    /// one nobody has to.
    /// </summary>
    private static readonly Dictionary<string, string> PillSprite =
        new(StringComparer.Ordinal)
        {
            ["Swapping"] = "Books",
            ["Stacking"] = "Cartridges",
            ["Ordering"] = "Pencils",
            ["Gadgets"] = "Lightbulb",
            ["Rotating"] = "Record",
            ["Grids"] = "GridTile",
            ["Tidying"] = "Breadtag",
            ["Containers"] = "EggCarton",
            ["Drawer"] = "Drawer",
            ["Sticking"] = "Stickers",
            ["Symmetry"] = "Wreath",
            ["Jigsaw"] = "Gingerbread",
            // Seeing Stars. The pizza BASE rather than a topping, even though
            // the toppings are what the mechanic distributes: all 48 of them
            // are small discs - pepperoni, olive, jalapeno - and a disc reads
            // as a generic dot at 36 pixels, which is how the hammer, the
            // nails and the callipers were rejected. The pan has a silhouette
            // and a colour nothing else in the strip uses.
            ["Distributing"] = "Pizza",
        };

    /// <summary>
    /// Decoded icons, keyed as above. Built once on first use.
    /// </summary>
    private static readonly Dictionary<string, Sprite> _pillArt =
        new(StringComparer.Ordinal);

    private static bool _pillArtLoaded;

    /// <summary>
    /// Load the ability icons from PNGs shipped inside this DLL.
    ///
    /// SHIPPED RATHER THAN FOUND, and that is the whole reason this is
    /// simple. Six of the twelve are objects out of the puzzles that use
    /// each mechanic, and those are Addressable assets: the game loads a
    /// level's art when the level opens and releases it when it closes. So
    /// they are not in memory on the level select, which is the one screen
    /// that needs them - measured, six of twelve resolved there and the
    /// other six drew as lettered plates.
    ///
    /// The alternative was to take them as they passed and hold a reference,
    /// which meant the strip filled in gradually as a player happened to
    /// visit the right puzzles. droha: "can we just save those as png and
    /// use them in game? That way we don't have to do all this run around."
    ///
    /// WHERE EACH ONE CAME FROM. Six are badge elements, which are the small
    /// item pictures that sit on a badge rather than an assembled badge, so
    /// the badges themselves stay free for whatever they might become. Six
    /// are objects lifted out of a puzzle that uses that mechanic and
    /// nothing else, so the picture and the lock mean the same thing:
    ///
    ///   Books        Badge1-Books2        badge element
    ///   Cartridges   Badge2-NES           badge element
    ///   Pencils      Badge1-Pencils       badge element
    ///   Lightbulb    badge3-lightbulb     badge element
    ///   Record       Badge1-Record        badge element
    ///   Stickers     Badge1-Stickers      badge element
    ///   GridTile     1x1-1                Procedural Grid Puzzle
    ///   Breadtag     Breadtag-red         Breadtags
    ///   EggCarton    Carton-front copy    Fridge (Something Eggstra)
    ///   Drawer       Drawer-Top+Bottom    Tool Drawer (Drawer Chores)
    ///   Wreath       Wreath               Wreath (Good Tidings)
    ///   Gingerbread  GingerbreadMan       Cookies Jigsaw (Good Tidings)
    /// </summary>
    private static void LoadPillArt()
    {
        if (_pillArtLoaded) return;
        _pillArtLoaded = true;

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;

                using var buffer = new System.IO.MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (bytes.Length == 0) continue;

                // The size is replaced by LoadImage; 2x2 is just a placeholder
                // cheap enough not to matter.
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes)) continue;
                // Icons are drawn small and never rotated, so clamping and
                // bilinear keeps the edges clean without a mip chain.
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));

                // "ALTTLArchipelago.Icons.Books.png" -> "Books"
                var key = resource;
                var dot = key.LastIndexOf('.', key.Length - 5);
                if (dot >= 0) key = key.Substring(dot + 1);
                key = key.Substring(0, key.Length - 4);

                _pillArt[key] = sprite;
            }

            Plugin.Logger.LogInfo($"badges: loaded {_pillArt.Count} ability icon(s)");
        }
        catch (Exception e)
        {
            // The strip falls back to lettered plates, which is what it
            // looked like before there was any art at all.
            Plugin.Logger.LogWarning($"badges: could not load the icons: {e.Message}");
        }
    }

    // Sized against a 1280x720 window, which is where this was judged by
    // eye. The overlay scales a 1920 reference down by two thirds, so 54
    // here is about 36 real pixels. At the 34 it was first built with, the
    // art landed at 23 pixels and most of the point of having art was lost.
    private const float PillWidth = 58f;

    private const float PillHeight = 72f;

    private const float PillIcon = 54f;

    private const float PillGap = 4f;

    /// <summary>
    /// Extra air between the two rows. Without it the second row's art
    /// crowds the first row's letters and the block reads as one smear.
    /// </summary>
    private const float PillRowGap = 10f;

    /// <summary>
    /// Tiles per row. Twelve in a line is a long thin ribbon across the top
    /// of the screen; six by two is a block the eye takes in at once, and it
    /// leaves the corner it sits in looking like one thing rather than a
    /// banner. droha's call.
    ///
    /// The row count is derived, not fixed, so a seed carrying Seeing Stars'
    /// Distributing wraps to a third row by itself - six, six and one.
    /// </summary>
    private const int PillsPerRow = 6;

    /// <summary>Room reserved under the art for the three-letter label.</summary>
    private const float LabelHeight = 15f;

    /// <summary>
    /// The size to draw a mechanic's icon at, keeping its own proportions.
    ///
    /// The longest side gets PillIcon and the other follows, so a tall
    /// picture and a wide one occupy the same visual weight rather than the
    /// same square. Falls back to a square when the art has not loaded, so a
    /// missing icon still leaves a tile the right shape.
    /// </summary>
    private static Vector2 FitIcon(string ability)
    {
        if (PillSprite.TryGetValue(ability, out var key)
            && _pillArt.TryGetValue(key, out var sprite)
            && sprite != null)
        {
            var w = sprite.rect.width;
            var h = sprite.rect.height;
            if (w > 0f && h > 0f)
            {
                return w >= h
                    ? new Vector2(PillIcon, PillIcon * h / w)
                    : new Vector2(PillIcon * w / h, PillIcon);
            }
        }
        return new Vector2(PillIcon, PillIcon);
    }

    /// <summary>
    /// Which mechanics you hold, as a row of pills on the level select.
    ///
    /// droha: "show ability locks on the level select - icons for the twelve
    /// mechanics, so you can see at a glance which you hold." Until now the
    /// only way to know was to open a puzzle and see what was greyed out.
    ///
    /// ALL OF THEM, ALWAYS, with the locked ones dimmed rather than absent.
    /// The strip never changes width, so a pill does not move under your eye
    /// as things unlock, and you can see what is still to come rather than
    /// only what you have.
    ///
    /// Except the ones this seed does not have. SlotData.Abilities is the
    /// seed's own catalogue and a seed can carry fewer than twelve; drawing
    /// a pill for a mechanic no item will ever grant would be a lie in the
    /// other direction. Nothing is drawn at all when ability locks are off,
    /// for the same reason - twelve dim pills would describe a restriction
    /// that is not in force.
    ///
    /// Top LEFT: the connected tag and the goal counter own the top right,
    /// and the toasts come up from the bottom left.
    /// </summary>
    internal static void TickAbilityPills()
    {
        _sincePills += Time.unscaledDeltaTime;
        if (_sincePills < 0.5f) return;
        _sincePills = 0f;

        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;
        var state = Inventory.Abilities;

        if (!visible || state == null || !state.LocksEnabled)
        {
            if (_pills != null) _pills.gameObject.SetActive(false);
            return;
        }

        LoadPillArt();

        if (_pills == null && !BuildPills()) return;
        _pills!.gameObject.SetActive(true);

        for (int i = 0; i < _pillOrder.Count && i < _pillTiles.Count; i++)
        {
            var child = _pillTiles[i];
            if (child == null) continue;

            var held = state.Has(_pillOrder[i]);

            // The icon is tinted rather than swapped: Image.color multiplies,
            // so full white is the art as drawn and a dark grey is the same
            // art dimmed. One sprite, two states, no second asset to find.
            var icon = child.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                if (icon.sprite == null
                    && PillSprite.TryGetValue(_pillOrder[i], out var wanted)
                    && _pillArt.TryGetValue(wanted, out var art))
                {
                    icon.sprite = art;
                    icon.enabled = true;
                }
                icon.color = held ? Color.white : IconLocked;
            }

            // The plate shows only where there is no art yet, so a tile
            // without its picture still reads as held or locked.
            var plate = child.GetComponent<Image>();
            if (plate != null)
            {
                plate.enabled = icon == null || icon.sprite == null;
                if (plate.enabled) plate.color = held ? PillHeld : PillLocked;
            }

            var label = child.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = held
                    ? new Color(1f, 1f, 1f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.30f);
            }
        }
    }

    /// <summary>
    /// Build the strip once, from the seed's own ability catalogue.
    ///
    /// Ordered by Core.Abilities.AllWithDlc rather than by the dictionary, so
    /// the pills sit in the same places on every seed and a player learns
    /// where to look. Returns false if the pieces are not ready yet, and the
    /// poll tries again.
    ///
    /// AllWithDlc, not All: a DLC mechanic is an ability item like any other
    /// and needs a pill. Iterating the base twelve would have drawn a strip
    /// with no Distributing pill while the item existed and gated DLC2 Pizza,
    /// so the one thing the strip is for - seeing what you hold - would have
    /// been silently wrong for that mechanic. The DLC pills sort after the
    /// base twelve, so a base-game seed's strip is unchanged.
    /// </summary>
    private static bool BuildPills()
    {
        var root = Toasts.OverlayRoot;
        var seed = Plugin.Seed;
        if (root == null || seed == null) return false;

        _pillOrder.Clear();
        _pillTiles.Clear();
        foreach (var ability in Abilities.AllWithDlc)
        {
            if (seed.Abilities.Count > 0 && !seed.Abilities.ContainsKey(ability))
            {
                continue;                 // not in this seed's catalogue
            }
            _pillOrder.Add(ability);
        }
        if (_pillOrder.Count == 0) return false;

        var strip = new GameObject("ApAbilityStrip");
        strip.transform.SetParent(root, false);
        _pills = strip.AddComponent<RectTransform>();
        _pills.anchorMin = new Vector2(0f, 1f);
        _pills.anchorMax = new Vector2(0f, 1f);
        _pills.pivot = new Vector2(0f, 1f);
        // CENTRED IN THE GAP, not just clear of it. At the left edge the
        // strip sat on top of the level select's own X - droha: "the icons
        // cover up the X in the level select, if they can fit between that
        // and the chapter headers that would be nice... can they be centered
        // between the X and chapter 1 title?"
        //
        // Measured off the screen at 1280x720 and converted to these units,
        // which are a 1920 reference: the X ends near 187 and the chapter
        // heading starts near 690, so the gap runs 187 to 690 and its middle
        // is 438. Half the block sits either side of that.
        var rows = (_pillOrder.Count + PillsPerRow - 1) / PillsPerRow;
        var columns = Math.Min(_pillOrder.Count, PillsPerRow);
        const float gapStart = 187f;
        const float gapEnd = 690f;
        var width = columns * (PillWidth + PillGap);
        var x = (gapStart + gapEnd) * 0.5f - width * 0.5f;
        _pills.anchoredPosition = new Vector2(Mathf.Max(gapStart, x), -18f);

        _pills.sizeDelta = new Vector2(width, rows * (PillHeight + PillRowGap));

        for (int i = 0; i < _pillOrder.Count; i++)
        {
            var pill = new GameObject("ApPill_" + _pillOrder[i]);
            pill.transform.SetParent(_pills, false);

            var rt = pill.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(
                (i % PillsPerRow) * (PillWidth + PillGap),
                -(i / PillsPerRow) * (PillHeight + PillRowGap));
            rt.sizeDelta = new Vector2(PillWidth, PillHeight);

            // A backing plate, used ONLY when there is no sprite for this
            // mechanic. Disabled otherwise so the art sits on the menu
            // rather than in a box.
            var plate = pill.AddComponent<Image>();
            plate.raycastTarget = false;
            plate.color = PillLocked;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(rt, false);
            var irt = iconGo.AddComponent<RectTransform>();

            // BOTTOM OF THE BOX, NOT THE TOP, and sized to the art rather
            // than to a square. These pictures are not square - the egg
            // carton is five times wider than it is tall - and a square box
            // with preserveAspect centres the drawing inside it, so a flat
            // object floated in the middle of its tile with a gap under it.
            // droha: "the egg carton should be bottom aligned, not top."
            //
            // Fitting the rect to the art and pinning it to the bottom puts
            // every icon on one baseline, which is also how the objects sit
            // in the puzzles they came from.
            irt.anchorMin = new Vector2(0.5f, 0f);
            irt.anchorMax = new Vector2(0.5f, 0f);
            irt.pivot = new Vector2(0.5f, 0f);
            irt.anchoredPosition = new Vector2(0f, LabelHeight + 2f);
            irt.sizeDelta = FitIcon(_pillOrder[i]);

            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.color = IconLocked;
            icon.enabled = false;

            var labelGo = new GameObject("Text");
            labelGo.transform.SetParent(rt, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, LabelHeight);

            _pillTiles.Add(rt);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = PillText.TryGetValue(_pillOrder[i], out var abbrev)
                ? abbrev
                : _pillOrder[i].Substring(0, Math.Min(3, _pillOrder[i].Length))
                    .ToUpperInvariant();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 12f;
            label.raycastTarget = false;
        }

        Plugin.Logger.LogInfo(
            $"badges: ability strip built with {_pillOrder.Count} pill(s)");
        return true;
    }
}
