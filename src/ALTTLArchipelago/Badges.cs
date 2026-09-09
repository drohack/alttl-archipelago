using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// A small badge in the corner of each card saying whether it is worth opening.
///
///   green            everything still to do here can be done now
///   green over red   some of it can, some of it cannot
///   red              none of it can, yet
///   star             nothing left to do
///
/// Split corner to corner rather than into two rectangles, which is what a card
/// tilted on the track needs in order to still read at a glance.
///
/// The card's own text is left completely alone. A level's name is its name - a
/// tracker that rewrote it to "[LOCKED] Books 3" was rejected, and rightly: the
/// badge carries the state and the name carries the name.
///
/// The drawing below is ported from the marker probe that was checked by eye in
/// game. What is new is where the state comes from - SlotProgress, reading the
/// generator's own requirements table.
/// </summary>
internal static class Badges
{
    private const string BadgeName = "ApTrackerBadge";
    private const string NameLabel = "ApCardName";
    private const string DimName = "ApLockedDim";

    /// <summary>Size of the badge, square, in the icon's own units.</summary>
    private const float BadgeSize = 30f;

    private static readonly Color Green = new Color(0.25f, 0.70f, 0.30f);
    private static readonly Color Red = new Color(0.85f, 0.20f, 0.20f);

    /// <summary>Fallback only - the game's own star colour is preferred.</summary>
    private static readonly Color FallbackStar = new Color(0.98f, 0.80f, 0.25f);

    /// <summary>
    /// The veil over a card the player cannot play yet.
    ///
    /// The GAME cannot show this. Its "locked" look - line art instead of full
    /// colour - is driven by LevelInterface.IsUnlocked, which is simply "this
    /// level has a LevelCompletionData row", and that save is keyed by levelId
    /// with no per-slot scoping. A run holding the same level in three slots is
    /// three cards pointing at ONE LevelInterface, so the moment the first is
    /// unlocked all three turn to full colour. That was reported as "when an
    /// Envelope level is in the pack, ALL Envelope levels look playable".
    ///
    /// Nothing in the save can express the difference, so it is drawn instead.
    /// </summary>
    private static readonly Color LockedVeil = new Color(0.05f, 0.05f, 0.08f, 0.55f);

    private static float _sinceRefresh;


    /// <summary>
    /// What each card last showed, so an unchanged track is not redrawn.
    /// Rebuilding every badge every second churns objects the game is
    /// animating, and the card tilt would fight it.
    /// </summary>
    /// <summary>
    /// The badge each SLOT is currently wearing, so an unchanged card is not
    /// repainted every tick.
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

    internal static void Reset()
    {
        _shown.Clear();
        _repeated.Clear();
        _repeatedBuilt = false;

        _dotsReported = false;

        if (_tag != null)
        {
            try { UnityEngine.Object.Destroy(_tag.gameObject); } catch { }
            _tag = null;
        }
        _sinceRefresh = 0f;
    }

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

    private static float _sinceTag;

    /// <summary>
    /// Paint the run's decorations on the NEXT frame rather than up to a
    /// second from now.
    ///
    /// The connected tag polls every 0.5s and the overview dots every 1s, on
    /// free-running timers that know nothing about the menu opening. Open the
    /// level select at the wrong moment in that cycle and you watch the
    /// Archipelago furniture arrive after the screen has already settled -
    /// reported as "the level select takes a moment to pop in all of the
    /// archipelago stuff. it's kind of jarring."
    ///
    /// Polling is still the right shape - there is no single reliable event
    /// for every route into that menu, which is why the track rebuild is on a
    /// timer too. What was wrong was letting a poll that exists to CATCH
    /// changes also decide when the FIRST paint happens.
    /// </summary>
    internal static void RepaintSoon()
    {
        _sinceTag = float.MaxValue;
        _sinceDots = float.MaxValue;
    }
    private static TextMeshProUGUI? _tag;

    /// <summary>
    /// Say whether the run is connected, on the level select.
    ///
    /// The main menu has had this since the pane was built
    /// (ConnectionPane.RefreshMenuIndicator), but the level select is where a
    /// player actually spends the run, and it was the one screen that never
    /// said. Losing the server mid-session is otherwise invisible until a check
    /// fails to land.
    ///
    /// Parented to the toast overlay rather than to the track. The track
    /// scrolls and is rebuilt whenever a pack arrives, and the main menu's own
    /// version of this needed a rich-text prefix on an existing label
    /// specifically because a free-standing object lost to that screen's
    /// layout. The overlay is the mod's own screen-space canvas, so it has no
    /// layout to lose to and nothing rebuilds it.
    ///
    /// Four states, matching the menu exactly - including "offline run", which
    /// is playing from the local cache and is NOT the same as having no run.
    /// </summary>
    internal static void TickConnectedTag()
    {
        _sinceTag += Time.unscaledDeltaTime;
        if (_sinceTag < 0.5f) return;
        _sinceTag = 0f;

        // Only while the run's own track is the thing on screen.
        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;

        if (!visible)
        {
            if (_tag != null) _tag.gameObject.SetActive(false);
            return;
        }

        if (_tag == null)
        {
            var root = Toasts.OverlayRoot;
            if (root == null) return;                // overlay not built yet

            var go = new GameObject("ApConnectedTag");
            go.transform.SetParent(root, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -18f);
            rt.sizeDelta = new Vector2(260f, 28f);

            _tag = go.AddComponent<TextMeshProUGUI>();
            _tag.alignment = TextAlignmentOptions.Right;
            _tag.fontSize = 18f;
            _tag.raycastTarget = false;
            _tag.richText = true;
        }

        _tag.gameObject.SetActive(true);

        string state;
        if (Plugin.IsConnected) state = "<color=#6BC77A>connected</color>";
        else if (Plugin.IsConnecting) state = "<color=#E6C759>connecting</color>";
        else if (Plugin.IsOffline) state = "<color=#E6C759>offline run</color>";
        else state = "<color=#FFFFFF80>offline</color>";

        _tag.text = $"{state}<color=#FFFFFF80>  Archipelago</color>";
    }

    private static float _sinceDots;
    private static bool _dotsReported;
    private const string DotName = "ApDotState";

    /// <summary>
    /// Colour the overview strip along the bottom of the level select, so the
    /// shape of the run is readable without scrolling it.
    ///
    /// Reached through Transform ONLY, deliberately. The typed route is
    /// LevelsOverviewScrollbar.levelOverviewItems, which is index-parallel to
    /// LevelSelect.Levels and therefore to Track._plan - but the element type
    /// of that list could not be confirmed from the interop assembly, and a
    /// guess that fails to compile costs more than this feature is worth. Every
    /// API used here is UnityEngine.Transform, GameObject and Image, so the
    /// worst case is that the search finds nothing and the strip stays as it
    /// was.
    ///
    /// A tinted CHILD rather than a tint on the dot itself: the game repaints
    /// these from UpdateOverviewAppearance whenever anything unlocks, and
    /// recolouring its own Image would be undone at a moment we do not control
    /// - the lesson already recorded in docs/verification-log.md about
    /// recolouring the game's graphics. The overlay survives that, and the poll
    /// puts back anything a rebuild removed.
    /// </summary>
    internal static void TickOverviewDots()
    {
        _sinceDots += Time.unscaledDeltaTime;
        if (_sinceDots < 1f) return;
        _sinceDots = 0f;

        if (!Track.Active) return;

        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (progress == null || abilities == null || state == null) return;

        var track = Track.CampaignTrack();
        if (track == null || !track.gameObject.activeInHierarchy) return;

        var strip = FindOverviewStrip(track.transform);
        if (strip == null) return;

        if (!_dotsReported)
        {
            _dotsReported = true;
            Plugin.Logger.LogInfo(
                $"badges: overview strip '{strip.name}' has {strip.childCount} dot(s)");
        }

        for (int i = 0; i < strip.childCount; i++)
        {
            var dot = strip.GetChild(i);
            if (dot == null) continue;

            var slot = Track.SlotAt(i);
            if (slot < 0)
            {
                RemoveChild(dot, DotName);
                continue;
            }

            var status = progress.StatusOf(
                slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

            // Mixed gets the SAME corner-to-corner split the card badge
            // uses, not a blended orange. The strip is a map of the badges
            // above it, and a third colour that appears nowhere on the cards
            // makes the reader learn two legends instead of one.
            var split = status == SlotStatus.Mixed;

            Color colour;
            switch (status)
            {
                case SlotStatus.Doable:   colour = Green; break;
                case SlotStatus.Locked:   colour = Red; break;
                case SlotStatus.Mixed:    colour = Color.white; break;   // the sprite carries it
                case SlotStatus.Complete: colour = FallbackStar; break;
                default: continue;
            }

            var existing = dot.Find(DotName);
            if (existing != null)
            {
                var img = existing.GetComponent<Image>();
                if (img != null)
                {
                    // The sprite decides which look this is, so it has to be
                    // set as well as the tint - a dot that went from Mixed to
                    // Doable would otherwise keep the diagonal and just recolour
                    // it, which reads as a solid green triangle.
                    var wanted = split ? DiagonalSprite() : null;
                    if (img.sprite != wanted) img.sprite = wanted;
                    if (img.color != colour) img.color = colour;
                }
                continue;
            }

            var go = new GameObject(DotName);
            go.transform.SetParent(dot, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            if (split) image.sprite = DiagonalSprite();
            image.color = colour;
            image.raycastTarget = false;

            go.transform.SetAsLastSibling();
        }
    }

    /// <summary>
    /// The container holding one dot per card, found by name because its type
    /// could not be pinned offline. Searched breadth-first from the track and
    /// then from the scene root above it, since the strip is a sibling of the
    /// track rather than a child.
    /// </summary>
    private static Transform? FindOverviewStrip(Transform from)
    {
        var root = from;
        while (root.parent != null) root = root.parent;

        return SearchForOverview(root, 0);
    }

    private static Transform? SearchForOverview(Transform node, int depth)
    {
        if (depth > 8) return null;

        for (int i = 0; i < node.childCount; i++)
        {
            var child = node.GetChild(i);
            if (child == null) continue;

            var name = child.name;
            if (name != null
                && name.IndexOf("overview", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0
                && child.childCount > 0)
            {
                return child;
            }

            var found = SearchForOverview(child, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static void RemoveChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) UnityEngine.Object.Destroy(child.gameObject);
    }

    private static float _sinceWhy;

    /// <summary>
    /// Answer a "why is this badge that colour" question left in a file.
    ///
    /// Diagnostic only. It exists because a badge that reads wrong is otherwise
    /// unarguable: the state is derived from the seed's requirements, what has
    /// been collected, packs held and abilities held, and only listing all four
    /// per location says which one is responsible.
    /// </summary>
    internal static void TickWhy(float dt)
    {
        // Off unless someone asked for it.
        //
        // This was a Path.Combine and a File.Exists every second, forever, in
        // the shipping mod - the DevTools command-file pattern copied into
        // player-facing code, for a diagnostic no shipping code reads. It is
        // genuinely useful when a badge looks wrong, so it is a switch rather
        // than a deletion.
        if (!Plugin.WhyProbeEnabled) return;

        _sinceWhy += dt;
        if (_sinceWhy < 1f) return;
        _sinceWhy = 0f;

        try
        {
            var path = System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath, "alttl-why.txt");
            if (!System.IO.File.Exists(path)) return;

            var text = System.IO.File.ReadAllText(path).Trim();
            System.IO.File.Delete(path);
            if (!int.TryParse(text, out var position)) return;

            Explain(position);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"why: {e.Message}");
        }
    }

    private static void Explain(int position)
    {
        var seed = Plugin.Seed;
        var progress = Checks.Progress;
        var abilities = Inventory.Abilities;
        var state = Track.State;
        if (seed == null || progress == null || abilities == null || state == null) return;

        var slot = Track.SlotAt(position);
        if (slot < 0)
        {
            Plugin.Logger.LogInfo($"why: position {position} is not a puzzle");
            return;
        }

        var entry = seed.Slots[slot];
        var status = progress.StatusOf(
            slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

        Plugin.Logger.LogInfo(
            $"why: position {position} = slot {slot} {entry.LevelId} "
            + $"#{entry.Instance}, badge {status}, {state.PacksHeld} packs held");

        foreach (var name in Checks.Router!.ForSlot(slot))
        {
            var collected = Checks.Ledger.IsCollected(name);
            var reachable = progress.IsReachable(name, state.PacksHeld, abilities);
            seed.Requirements.TryGetValue(name, out var need);

            Plugin.Logger.LogInfo(
                $"why:   {(collected ? "done" : "TODO")} "
                + $"{(reachable ? "reachable" : "BLOCKED  ")} {name}"
                + (need == null ? "" : $"  [needs {need.Packs} packs"
                    + (need.Abilities.Count > 0
                        ? ", " + string.Join(" + ", need.Abilities) : "")
                    + "]"));
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
                Remove(icon, DimName);
                RenumberDivider(icon, Track.PackNumberAt(i));
                continue;
            }

            var status = progress.StatusOf(
                slot, Checks.Ledger.IsCollected, state.PacksHeld, abilities);

            // Skip only when it is unchanged AND still on screen: the game
            // destroys our badge when it rebuilds the track, and a cached
            // "already green" would leave that card bare forever.
            if (_shown.TryGetValue(slot, out var was) && was == status
                && icon.transform.Find(BadgeName) != null)
            {
                continue;
            }

            SetDim(icon, status == SlotStatus.Locked);
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

    /// <summary>
    /// Grey a card out while its slot is still locked. See LockedVeil for why
    /// this is drawn rather than left to the game.
    /// </summary>
    private static void SetDim(LevelIcon icon, bool locked)
    {
        var existing = icon.transform.Find(DimName);

        if (!locked)
        {
            if (existing != null) Remove(icon, DimName);
            return;
        }

        if (existing != null) return;                // already veiled

        var go = new GameObject(DimName);
        go.transform.SetParent(icon.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = LockedVeil;
        // The click still has to reach the card: refusing it is the track's
        // job, and it now explains itself with a toast. Swallowing it here
        // would give the player silence instead.
        image.raycastTarget = false;

        go.transform.SetAsLastSibling();
    }

    internal static void SetBadge(LevelIcon icon, SlotStatus state)
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

    /// <summary>
    /// Read a value from the IL2CPP side without letting a throw escape.
    ///
    /// Reaching into a destroyed or half-built object throws rather than
    /// returning null, and a badge refresh is not worth taking the frame down
    /// for.
    /// </summary>
    private static string Str(Func<string?> read)
    {
        try { return read() ?? ""; }
        catch { return ""; }
    }
}
