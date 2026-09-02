using System;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLDevTools;

/// <summary>
/// Phase 0 S5: the tracker marker on a level-select card.
///
/// SETTLED - it is a small box in the card's top-right corner, and it carries
/// four states:
///
///   green            everything still to do on this card is doable now
///   split            something is doable, something is still locked (green
///                    upper-left, red lower-right, split corner to corner)
///   red              everything still to do is locked
///   star             all done (the game's own star, in the game's own colour)
///
/// Note the states are about what is LEFT: a card with three checks of which
/// two are collected and one is reachable is green, not mixed. "Mixed" means
/// the remaining work is genuinely split.
///
/// Three carriers were built and compared before settling on this one
/// (screenshots and the full reasoning are in docs/verification-log.md):
///
///   border      LevelIcon.borderImage, the original plan. Where it renders it
///               looks better than a badge, but it renders on only SOME cards
///               and no available predicate explained which - not isUnlocked,
///               not LevelHasCompletionData, not the active presentation
///               subtree. It lives inside "Default Level Icon", one of two
///               presentations the icon swaps between. Unpredictable is worse
///               than plain, so it is out.
///   outline     a larger Image behind the card. REJECTED by the user: it
///               swamps the card instead of ringing it.
///   background  recolouring the game's own background Image. REJECTED by the
///               user as confusing - it repaints the whole card, so the art
///               that tells cards apart is lost. It is also destructive and
///               does not heal: neither RefreshIconAppearance nor leaving and
///               re-entering the menu restores the original colour, because the
///               level select is built once and cached.
///
/// The badge is our own GameObject, so nothing the game repaints can touch it.
/// Verified to survive RefreshIconAppearance and a full menu round-trip.
///
/// NOTHING IS LABELLED. An earlier version wrote the carrier and lock state
/// under each card to make a comparison screenshot unambiguous; that was a
/// diagnostic and is gone. A card shows its level name and its badge, and no
/// other text.
/// </summary>
internal static class Markers
{
    private const string BadgeName = "ApTrackerBadge";

    /// <summary>Size of the badge, square, in the icon's own units.</summary>
    private const float BadgeSize = 30f;

    internal enum MarkerState
    {
        /// <summary>Everything still to do here is reachable now.</summary>
        Doable,
        /// <summary>Some of what is left is reachable, some is not.</summary>
        Mixed,
        /// <summary>Nothing still to do here is reachable.</summary>
        Locked,
        /// <summary>Nothing left to do.</summary>
        Complete,
    }

    private static readonly Color Green = new Color(0.25f, 0.70f, 0.30f);
    private static readonly Color Red = new Color(0.85f, 0.20f, 0.20f);

    /// <summary>Fallback only - the game's own star colour is preferred.</summary>
    private static readonly Color FallbackStar = new Color(0.98f, 0.80f, 0.25f);

    internal static void Apply(string mode)
    {
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track == null)
        {
            DevToolsPlugin.Log.LogWarning("marker: open menu:levels first");
            return;
        }

        var items = track.trackItems;
        int count = items == null ? 0 : items.Count;

        if (mode.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < count; i++)
            {
                if (items![i] != null) items[i].RefreshIconAppearance();
            }
            DevToolsPlugin.Log.LogInfo(
                $"marker: forced RefreshIconAppearance on {count} icons -"
                + " the badges should be untouched");
            return;
        }

        bool off = mode.Equals("off", StringComparison.OrdinalIgnoreCase);
        bool cycle = mode.Equals("states", StringComparison.OrdinalIgnoreCase);
        if (!off && !cycle)
        {
            DevToolsPlugin.Log.LogWarning(
                $"marker: unknown mode '{mode}'. Use states, refresh or off");
            return;
        }

        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            var icon = items![i];
            if (icon == null) continue;

            Remove(icon, BadgeName);
            if (off) continue;

            try
            {
                // Demo only: cycle the four states so one screenshot shows all
                // of them. The real mod computes the state from the checks left
                // on the card.
                SetBadge(icon, (MarkerState)(i % 4));
                applied++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"marker: icon {i}: {e.Message}");
            }
        }

        DevToolsPlugin.Log.LogInfo(off
            ? $"marker: cleared from {count} icons"
            : $"marker: badges on {applied} of {count} icons,"
              + " cycling doable / mixed / locked / complete");
    }

    /// <summary>
    /// Put the badge for one state on one card. Idempotent - it removes any
    /// badge already there, so it is safe to call on every state change.
    /// </summary>
    internal static void SetBadge(LevelIcon icon, MarkerState state)
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
            case MarkerState.Doable:
                AddFill(root, Green, 0f, 1f);
                break;
            case MarkerState.Locked:
                AddFill(root, Red, 0f, 1f);
                break;
            case MarkerState.Mixed:
                // Split corner to corner, top-right down to bottom-left: green
                // in the upper-left triangle, red in the lower-right.
                AddDiagonal(root);
                break;
            case MarkerState.Complete:
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
    /// "iconinfo:N" dumps one icon's child tree with the components and sizes
    /// on each node. Guessing at a hierarchy wastes a build-and-launch cycle;
    /// reading it costs one command. This is what showed that LevelIcon has two
    /// whole presentations and swaps which is active.
    /// </summary>
    internal static void IconInfo(int index)
    {
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track == null)
        {
            DevToolsPlugin.Log.LogWarning("iconinfo: open menu:levels first");
            return;
        }

        var items = track.trackItems;
        if (items == null || index < 0 || index >= items.Count)
        {
            DevToolsPlugin.Log.LogWarning(
                $"iconinfo: index {index} out of range (0..{(items?.Count ?? 0) - 1})");
            return;
        }

        var icon = items[index];
        if (icon == null) { DevToolsPlugin.Log.LogWarning("iconinfo: null icon"); return; }

        DevToolsPlugin.Log.LogInfo($"iconinfo: index {index} level={Str(() => icon.level?.LevelId)}");
        Dump(icon.transform, 0);
    }

    private static void Dump(Transform t, int depth)
    {
        if (t == null || depth > 7) return;

        var pad = new string(' ', depth * 2);
        var rt = t.TryCast<RectTransform>();
        var size = rt != null ? $" size={rt.rect.width:0}x{rt.rect.height:0}" : "";

        var components = "";
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            var name = Str(() => c.GetIl2CppType().Name);
            if (name == "RectTransform" || name == "Transform") continue;
            components += (components.Length > 0 ? "," : "") + name;
        }

        // Sprite name and colour on any Image, so the right art can be picked
        // by reading rather than by guessing and rebuilding.
        var img = t.GetComponent<Image>();
        if (img != null)
        {
            components += $" sprite={Str(() => img.sprite?.name)}"
                + $" colour={Str(() => $"{img.color.r:0.00},{img.color.g:0.00},{img.color.b:0.00},{img.color.a:0.00}")}";
        }

        DevToolsPlugin.Log.LogInfo(
            $"  {pad}{t.gameObject.name}{size} active={t.gameObject.activeSelf}"
            + $" sibling={t.GetSiblingIndex()}"
            + (components.Length > 0 ? $" [{components}]" : ""));

        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
    }

    private static string Str(Func<string?> f)
    {
        try { return f() ?? "null"; } catch { return "?"; }
    }
}
