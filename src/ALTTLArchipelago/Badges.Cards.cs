using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The three things drawn ON a card: the corner badge, the puzzle's name
/// underneath, and a divider's pack number.
///
/// One feature, not three - Refresh paints all of them in a single pass over
/// the track, gated by the same _shown cache, so a card either repaints or it
/// does not. Splitting them would mean three passes and three caches that could
/// disagree about what a card is currently showing.
///
/// NO VEIL, droha 2026-09-25: a locked card and a beaten one with nothing
/// reachable are both just the red square - "the player can always hover to
/// see stars, or see x/n beaten in the top right corner". The veil it used to
/// draw marked "not playable yet", which the game's own look cannot say for a
/// level repeated in several slots; clicking such a card still explains itself
/// with a toast.
/// </summary>
internal static partial class Badges
{
    private const string BadgeName = "ApTrackerBadge";

    private const string NameLabel = "ApCardName";

    /// <summary>Size of the badge, square, in the icon's own units.</summary>
    private const float BadgeSize = 30f;

    private static float _sinceRefresh;

    /// <summary>
    /// The badge each SLOT is currently wearing, so an unchanged card is not
    /// repainted every tick. Rebuilding every badge every second churns
    /// objects the game is animating, and the card tilt would fight it.
    ///
    /// Keyed by slot rather than by track position on purpose. Position is not
    /// stable: a pack arriving inserts a divider, and every card after it
    /// shifts by one, so a position-keyed entry silently starts describing its
    /// neighbour. The badge survives that today only because the entry is also
    /// checked against the badge still being on screen and a rebuild destroys
    /// it - which is a second mechanism covering for the first. Slot is a
    /// bijection with the non-divider cards and needs no covering.
    /// </summary>
    private static readonly Dictionary<int, SlotStatus> _shown = new();

    /// <summary>
    /// Level ids the run contains more than once, so their cards can say which
    /// instance they are. Rebuilt when the cache is reset.
    /// </summary>
    private static readonly HashSet<string> _repeated = new(StringComparer.Ordinal);

    private static bool _repeatedBuilt;

    /// <summary>
    /// Refresh the badges while the track is on screen.
    ///
    /// Polled for the same reason as the rest: the game rebuilds the icons
    /// whenever the menu opens, taking our badges with them, and no event
    /// reliably fires after that has finished.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (!Track.Active) return;

        _sinceRefresh += dt;
        if (_sinceRefresh < 1f) return;
        _sinceRefresh = 0f;

        try
        {
            Refresh();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: refresh failed: {e.Message}");
        }
    }

    private static void Refresh()
    {
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (progress == null || abilities == null || state == null) return;

        // The CAMPAIGN track specifically. Archive and DLC menus have a
        // LevelsTrack too, and a plain search returns whichever exists - so
        // while the Archive page was open, badges were painted onto ITS cards
        // using our slot indices. That is what made a badge look stale until
        // you entered a level and came back: it was never our track.
        var track = Track.CampaignTrack();
        var items = track == null ? null : track.trackItems;
        if (items == null) return;

        // Nothing to repaint when no card is on screen.
        //
        // The level select is built once and cached, so trackItems stays
        // populated for the whole session - which meant this full recompute ran
        // every second while the player was inside a puzzle, looking at none of
        // it. Track.TickTrackIntegrity has always had this check; the badge
        // path never did.
        //
        // Safe to skip: the badges are repainted from scratch on the next tick
        // once the track is up again, which is the same thing that already
        // happens after the game rebuilds the icons.
        if (!track!.gameObject.activeInHierarchy) return;

        for (int i = 0; i < items.Count; i++)
        {
            var icon = items[i];
            if (icon == null) continue;

            // Track POSITION is not slot index. Pack dividers and the credits
            // card are cards too, so from the first divider onwards every badge
            // would report a different puzzle's state - and the dividers
            // themselves wore one.
            var slot = Track.SlotAt(i);
            if (slot < 0)
            {
                Remove(icon, BadgeName);
                Remove(icon, NameLabel);
                RenumberDivider(icon, Track.PackNumberAt(i));
                continue;
            }

            var status = progress.StatusOf(
                slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

            // Every refresh, before the unchanged-badge skip below: a check
            // collected while the track is up changes the stars and not
            // necessarily the badge.
            CardStars.Apply(icon, i);

            // Skip only when it is unchanged AND still on screen: the game
            // destroys our badge when it rebuilds the track, and a cached
            // "already green" would leave that card bare forever.
            if (_shown.TryGetValue(slot, out var was) && was == status
                && icon.transform.Find(BadgeName) != null)
            {
                continue;
            }

            SetBadge(icon, status);
            SetName(icon, slot);
            _shown[slot] = status;
        }
    }

    /// <summary>
    /// Put the right number on a pack divider.
    ///
    /// The dividers are the game's own five chapter cards, reused - a run can
    /// have far more packs than the game has chapters, and reusing one is much
    /// better than leaving fifty-eight cards in an unbroken block. What reuse
    /// costs is the number printed on the card: pack 6 borrows chapter 1's
    /// card and announces itself as "1".
    ///
    /// Found by VALUE rather than by name, because the chapter card's
    /// hierarchy is not documented and guessing an object name has already
    /// cost this project a day. Any label on the card whose text is just an
    /// integer is the number; nothing else on a chapter card is.
    ///
    /// Reapplied every refresh rather than once: the game rebuilds these when
    /// the track changes, and a number written once would revert the first
    /// time a pack arrives.
    /// </summary>
    private static void RenumberDivider(LevelIcon icon, int pack)
    {
        if (pack <= 0) return;

        try
        {
            var wanted = pack.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var text in icon.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null) continue;

                var current = text.text;
                if (string.IsNullOrWhiteSpace(current)) continue;
                if (current == wanted) continue;

                // Only a bare number. A chapter card also carries its title,
                // and rewriting that would replace "Lost Recipe" with "6".
                var trimmed = current.Trim();
                if (trimmed.Length > 2) continue;
                if (!int.TryParse(trimmed,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) continue;

                text.text = wanted;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: could not renumber a divider: {e.Message}");
        }
    }

    private static void SetBadge(LevelIcon icon, SlotStatus state)
    {
        Remove(icon, BadgeName);

        var root = new GameObject(BadgeName);
        root.transform.SetParent(icon.transform, false);

        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-6f, -6f);
        rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);

        switch (state)
        {
            case SlotStatus.Doable:
                AddFill(root, Green, 0f, 1f);
                break;
            case SlotStatus.Locked:
                AddFill(root, Red, 0f, 1f);
                break;
            case SlotStatus.Mixed:
                // Split corner to corner, top-right down to bottom-left: green
                // in the upper-left triangle, red in the lower-right.
                AddDiagonal(root);
                break;
            case SlotStatus.Beaten:
                // PLAIN RED, the same as Locked. The star means everything on
                // the card is done, and nothing else. droha, 2026-09-24, on the
                // red-with-star this used to draw: "there should be no time
                // that the level is fully completed and there's still things
                // to get/are locked. it should just be red, red/green, green,
                // yellow star; no overlapping." And 2026-09-25, on the veil
                // that still told the two apart: "can we just have a red square
                // for both?"
                AddFill(root, Red, 0f, 1f);
                break;
            case SlotStatus.Complete:
                AddStar(root, icon);
                break;
        }

        // uGUI paints in hierarchy order, so last means on top of the artwork.
        // This is the whole reason the badge works where borderImage does not.
        root.transform.SetAsLastSibling();
    }

    /// <summary>
    /// The split badge, drawn from a generated sprite.
    ///
    /// uGUI has no triangle: Image draws a rect, and its Filled modes cut
    /// radially or along an axis, never corner to corner. So the diagonal is
    /// painted into a small texture once and reused. Rooted in a static
    /// because a Texture2D with no managed reference is collectable, and a
    /// collected texture leaves a blank badge rather than an error.
    /// </summary>
    private static Sprite? _diagonal;

    private static Texture2D? _diagonalTexture;

    private static void AddDiagonal(GameObject parent)
    {
        var go = new GameObject("Diagonal");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = DiagonalSprite();
        image.color = Color.white;      // the sprite carries the colours
        image.raycastTarget = false;
    }

    private static Sprite DiagonalSprite()
    {
        if (_diagonal != null) return _diagonal;

        // 64 for a 30-point badge, so the diagonal is sampled down rather than
        // up and does not come out as a staircase.
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Texture coordinates put y at the BOTTOM, so the line through the
        // top-right and bottom-left corners is y = x. Above it (y > x) is the
        // upper-left triangle.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Blend across a pixel and a half rather than stepping, so the
                // edge does not alias at badge size.
                float t = Mathf.Clamp01(((y - x) / 1.5f) + 0.5f);
                texture.SetPixel(x, y, Color.Lerp(Red, Green, t));
            }
        }
        texture.Apply();

        _diagonalTexture = texture;
        _diagonal = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f));
        return _diagonal;
    }

    /// <summary>One solid slab, spanning [from, to] horizontally.</summary>
    private static void AddFill(GameObject parent, Color colour, float from, float to)
    {
        var go = new GameObject("Fill");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(from, 0f);
        rt.anchorMax = new Vector2(to, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = colour;
        // Never eat a click meant for the card underneath.
        image.raycastTarget = false;
    }

    /// <summary>
    /// The game's own star, so "done" looks like the game's idea of done rather
    /// than a fifth invented colour.
    /// </summary>
    private static void AddStar(GameObject parent, LevelIcon icon)
    {
        var go = new GameObject("Star");
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.raycastTarget = false;

        var (sprite, colour) = FindStar(icon);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = colour;
        }
        else
        {
            // No star art found - a plain gold square still reads as "done"
            // and is better than an invisible badge.
            image.color = colour;
        }
    }

    /// <summary>
    /// Borrow the star sprite and colour from the icon itself rather than
    /// shipping art. Prefers the completion star actually used on cards, and
    /// falls back to the locked-star icon, which every card carries.
    /// </summary>
    private static (Sprite?, Color) FindStar(LevelIcon icon)
    {
        // Search by SPRITE NAME, and be exact about the suffix. Every card
        // carries three star sprites and picking the wrong one is silent:
        //
        //   LTL-LevelSelect-Star-solved     the earned star   <- this one
        //   LTL-LevelSelect-Star-unsolved   an empty outline
        //   LTL-LevelSelect-Star-locked     a drab grey star
        //
        // Both earlier attempts got this wrong. Walking a fixed path landed on
        // "-locked" and drew a grey smudge; then "any star that is not locked"
        // landed on "-unsolved" and drew a hollow outline. "Contains solved" is
        // no good either, because "unsolved" contains "solved".
        Sprite? fallback = null;

        foreach (var image in icon.GetComponentsInChildren<Image>(true))
        {
            var sprite = image == null ? null : image.sprite;
            if (sprite == null) continue;

            var name = Str(() => sprite.name);
            if (name.IndexOf("star", StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (name.EndsWith("-solved", StringComparison.OrdinalIgnoreCase))
            {
                // Tinted gold, NOT drawn as-is. The sprite is a white
                // silhouette that the game tints at runtime, so drawing it
                // untinted gives a white star that disappears against the
                // pale cards - tried, and it vanished on four of six.
                //
                // The game's own earned-star tint could not be read off a live
                // card: it only draws that star on a SOLVED level, and writing
                // save data with solve: sets the found count without setting
                // solved. So this gold is ours, pending a real solved card to
                // sample.
                return (sprite, FallbackStar);
            }
            fallback ??= sprite;
        }

        // No earned-star art on this card. A star-shaped outline in gold still
        // reads as "done"; a grey one would read as "locked", the opposite of
        // the truth.
        return (fallback, FallbackStar);
    }

    private static void Remove(LevelIcon icon, string name)
    {
        var existing = icon.transform.Find(name);
        if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
    }

    /// <summary>
    /// Whether the run holds this level in more than one slot. Answered once
    /// per level and cached, because it is asked for every card on every
    /// rebuild.
    /// </summary>
    private static bool IsRepeated(SlotData seed, string levelId)
    {
        if (!_repeatedBuilt)
        {
            _repeatedBuilt = true;
            var once = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in seed.Slots)
            {
                if (!once.Add(s.LevelId)) _repeated.Add(s.LevelId);
            }
        }

        return _repeated.Contains(levelId);
    }

    /// <summary>
    /// The puzzle's name, under the card.
    ///
    /// The card carries no name of its own - its whole hierarchy holds only a
    /// locked-star count and a chapter number - so in a randomized track, where
    /// the art is the only clue and the order is not the one anyone knows,
    /// there is no way to find a particular puzzle or to match a hint to a
    /// card.
    ///
    /// Just the name. No lock state, no check counts: the badge already says
    /// all of that, and a name with data bolted onto it stops reading as a
    /// name.
    /// </summary>
    private static void SetName(LevelIcon icon, int slot)
    {
        try
        {
            var seed = Plugin.Seed;
            if (seed == null || slot < 0 || slot >= seed.Slots.Count) return;
            if (icon.transform.Find(NameLabel) != null) return;   // already there

            var entry = seed.Slots[slot];
            var text = DisplayNames.For(entry.LevelId);

            // Number EVERY instance once a level is in the run more than once,
            // so a repeated pair reads "#1" and "#2" rather than a bare name
            // followed by "#2" - which looks like the same card twice at a
            // glance, and was reported as exactly that.
            //
            // Cosmetic only, and deliberately NOT matched in the location
            // table: locations.instance_tag leaves the first instance
            // unnumbered, and it must keep doing so. Location ids are
            // positional (see the headers on locations.py and items.py), so
            // emitting "#1" there would rename every location in the
            // datapackage and invalidate every seed in flight. The label and
            // the AP name therefore differ by this one suffix, on purpose.
            if (IsRepeated(seed, entry.LevelId)) text += $" #{entry.Instance}";

            var go = new GameObject(NameLabel);
            go.transform.SetParent(icon.transform, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            // Well clear of the card. The card GROWS when hovered, so a label
            // tucked close underneath ends up sitting on top of the art at the
            // exact moment the player is looking at it.
            rect.anchoredPosition = new Vector2(0f, -28f);
            rect.sizeDelta = new Vector2(60f, 54f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = FindFont(icon);
            label.text = text;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Top;
            label.enableWordWrapping = true;
            label.raycastTarget = false;       // never eat a click meant for the card
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 180);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: could not name a card: {e.Message}");
        }
    }

    /// <summary>Borrow a font from the card itself, so it matches the menu.</summary>
    private static TMPro.TMP_FontAsset? FindFont(LevelIcon icon)
    {
        foreach (var t in icon.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t != null && t.font != null) return t.font;
        }
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t != null && t.font != null) return t.font;
        }
        return null;
    }
}
