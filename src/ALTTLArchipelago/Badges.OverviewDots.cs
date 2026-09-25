using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The overview strip - one dot per slot, a map of the badges above it.
///
/// Shares the diagonal sprite with the card badge deliberately, so Mixed
/// reads the same in both places rather than teaching the player two legends.
/// </summary>
internal static partial class Badges
{
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

        FitStrip(strip);

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

                // BEATEN READS AS RED HERE, the same as its card badge.
                //
                // The strip answers one question - is there anything I can do
                // on this card right now - and for a beaten card that still
                // owes unreachable checks the answer is no, exactly as for a
                // locked one; its card badge is the same red square too. The
                // star is for a card with nothing left at all (droha,
                // 2026-09-24: "red, red/green, green, yellow star; no
                // overlapping").
                //
                // It still needs its own case rather than falling to the
                // default below: `default: continue` skips Paint entirely,
                // which would leave whatever tint the dot was last given.
                case SlotStatus.Beaten:   colour = Red; break;

                default: continue;
            }

            Paint(dot, colour, split);
        }
    }

    /// <summary>
    /// Put (or update) the tinted overlay on one overview dot.
    ///
    /// Extracted when the finale needed the same treatment from a second
    /// place in the loop. A tinted CHILD rather than the dot's own Image, for
    /// the reason in TickOverviewDots: the game repaints these itself.
    /// </summary>
    private static void Paint(Transform dot, Color colour, bool split)
    {
        var existing = dot.Find(DotName);
        if (existing != null)
        {
            var img = existing.GetComponent<Image>();
            if (img != null)
            {
                // The sprite decides which look this is, so it has to be set
                // as well as the tint - a dot that went from Mixed to Doable
                // would otherwise keep the diagonal and just recolour it,
                // which reads as a solid green triangle.
                var wanted = split ? DiagonalSprite() : null;
                if (img.sprite != wanted) img.sprite = wanted;
                if (img.color != colour) img.color = colour;
            }
            return;
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

    /// <summary>
    /// The strip's own scale before we touched it, so the fit is computed from
    /// a fixed baseline rather than by multiplying what is already there.
    ///
    /// Keyed by nothing - there is one strip - but reset whenever a different
    /// object turns up, because the level select is rebuilt and the Transform
    /// we measured may be gone.
    /// </summary>
    private static Transform? _fittedStrip;

    private static Vector3 _fittedBase = Vector3.one;

    /// <summary>
    /// Shrink the overview strip until it fits the screen.
    ///
    /// The game sizes this for its own widest campaign, about 85 cards. A run
    /// inserts a pack divider between blocks, so 79 puzzles builds 92 cards
    /// and the strip runs off the edge - reported from a full-length run. The
    /// default puzzle count now lands at 84 so this does nothing there, but a
    /// player is free to ask for 79 and should not be punished with a strip
    /// they cannot see the end of.
    ///
    /// SCALE, NOT SPACING. The mod has no handle on the layout - there is no
    /// layout component here, the game positions each dot itself - so the only
    /// lever that cannot fight it is the parent's scale.
    ///
    /// IDEMPOTENT, FROM A CACHED BASELINE. The game repaints these from
    /// UpdateOverviewAppearance at moments we do not control, and this runs on
    /// a one-second poll, so anything that multiplied the current value would
    /// shrink the strip to nothing over a minute of looking at the menu.
    /// </summary>
    private static void FitStrip(Transform strip)
    {
        try
        {
            if (!ReferenceEquals(_fittedStrip, strip))
            {
                _fittedStrip = strip;
                _fittedBase = strip.localScale;
            }

            var rect = strip.TryCast<RectTransform>();
            var parent = strip.parent == null
                ? null : strip.parent.TryCast<RectTransform>();
            if (rect == null || parent == null)
            {
                ReportFit($"no RectTransform (self={rect != null}, "
                          + $"parent={parent != null})");
                return;
            }

            // Measured from the dots, not from the strip's own rect: the rect
            // is whatever the game authored and need not bound its children.
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < strip.childCount; i++)
            {
                var dot = strip.GetChild(i);
                if (dot == null) continue;
                var dr = dot.TryCast<RectTransform>();
                if (dr == null) continue;
                var x = dr.anchoredPosition.x;
                if (x < min) min = x;
                if (x > max) max = x;
            }
            if (min > max)
            {
                ReportFit($"no dot positions to measure from {strip.childCount} child(ren)");
                return;
            }

            var span = (max - min) + DotAllowance;
            var room = RoomFor(strip);

            // Said out loud ONCE, whatever the verdict. A fit pass that only
            // logs when it acts is indistinguishable from one that never ran,
            // and that is exactly how the first attempt at this looked.
            ReportFit($"{strip.childCount} dots span {span:0} in {room:0}");

            if (span <= 0f || room <= 0f) return;

            // Only ever shrink. Widening a strip the game already fits would
            // be the mod inventing a layout rather than rescuing one.
            var wanted = Mathf.Clamp(room / span, MinStripScale, 1f);
            var target = _fittedBase * wanted;

            if ((strip.localScale - target).sqrMagnitude < 0.000001f) return;

            strip.localScale = target;
            Plugin.Logger.LogInfo(
                $"badges: overview strip scaled to {wanted:0.00} - "
                + $"{strip.childCount} dots span {span:0} in {room:0}");
        }
        catch (Exception e)
        {
            // Cosmetic, on a poll. A strip that stays too wide is far better
            // than an exception every second.
            Plugin.Logger.LogWarning($"badges: could not fit the strip: {e.Message}");
        }
    }

    /// <summary>
    /// Room for the last dot itself, which the leftmost-to-rightmost span of
    /// CENTRES leaves out. Approximate on purpose - being a few pixels
    /// conservative costs nothing and stops the final dot touching the edge.
    /// </summary>
    private const float DotAllowance = 24f;

    /// <summary>
    /// How small the strip may get. At the maximum 79 puzzles it needs about
    /// 0.9, so this floor is only reached by something unforeseen - and a
    /// strip too small to read is no more useful than one that overflows.
    /// </summary>
    private const float MinStripScale = 0.55f;

    /// <summary>
    /// How much width the strip actually has, in its own units.
    ///
    /// THE IMMEDIATE PARENT IS THE WRONG ANSWER and measuring it is why the
    /// first version of this never fired. The strip sits inside
    /// 'Levels Overview Scrollbar', whose width TRACKS ITS CONTENT: with 92
    /// dots it measured 2025 against a span of 2026, so the ratio was 0.9995
    /// and nothing ever looked like it overflowed. The real viewport is two
    /// levels up - 'Level Select' at 1920, the screen width.
    ///
    /// So take the NARROWEST ancestor. A content-sized box is by definition
    /// at least as wide as its content, so it can never be the smallest; the
    /// first fixed one above it wins. That holds without naming any of the
    /// game's objects, which matters because the names here are not ours.
    /// </summary>
    private static float RoomFor(Transform strip)
    {
        var room = float.MaxValue;
        var walk = strip.parent;
        for (int up = 0; up < 6 && walk != null; up++, walk = walk.parent)
        {
            var wr = walk.TryCast<RectTransform>();
            var width = wr == null ? 0f : wr.rect.width;
            if (width > 1f && width < room) room = width;
        }
        return room == float.MaxValue ? 0f : room;
    }

    private static string? _fitReported;

    /// <summary>One line per distinct measurement, so a 1s poll cannot spam.</summary>
    private static void ReportFit(string what)
    {
        if (_fitReported == what) return;
        _fitReported = what;
        Plugin.Logger.LogInfo($"badges: strip fit - {what}");
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
}
