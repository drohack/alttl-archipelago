using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using HarmonyLib;
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
    /// Reached through Transform, deliberately. The typed route is
    /// LevelsOverviewScrollbar.levelOverviewItems, which is index-parallel to
    /// LevelSelect.Levels and therefore to Track._plan - its element type
    /// could not be confirmed when this was written. It is List&lt;Image&gt;,
    /// and DrawShutSquaresLocked below uses it; this poll still searches, and
    /// every API it uses is UnityEngine.Transform, GameObject and Image, so
    /// the worst case is that the search finds nothing and the strip stays as
    /// it was.
    ///
    /// A tinted CHILD rather than a tint on the dot itself: the game repaints
    /// these from UpdateOverviewAppearance whenever anything unlocks, and
    /// recolouring its own Image would be undone at a moment we do not control
    /// - the lesson already recorded in docs/history/verification-log.md about
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

            // A pack not opened yet keeps the game's own locked look instead of
            // our red, which is also what a card locked by an ability gets.
            // droha, 2026-09-25: "it's hard to tell how many packs you actually
            // have open" - wanted an empty box on the strip for those packs.
            var slot = Track.SlotAt(i);
            if (slot < 0 || !state.IsOpen(slot))
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

    /// <summary>Redraw a shut pack's squares locked after the game draws them (DrawShutSquaresLocked).</summary>
    [HarmonyPatch(typeof(LevelsOverviewScrollbar), nameof(LevelsOverviewScrollbar.UpdateOverviewAppearance))]
    [HarmonyPostfix]
    private static void AfterUpdateOverviewAppearance(LevelsOverviewScrollbar __instance)
        => DrawShutSquaresLocked(__instance, -1);

    [HarmonyPatch(typeof(LevelsOverviewScrollbar), nameof(LevelsOverviewScrollbar.OverviewItemUnlock))]
    [HarmonyPostfix]
    private static void AfterOverviewItemUnlock(LevelsOverviewScrollbar __instance, int itemIndex)
        => DrawShutSquaresLocked(__instance, itemIndex);

    /// <summary>
    /// A square in a pack not opened yet draws locked, whatever its level's
    /// save row says: the strip's half of Track.DrawLockedIfShut.
    ///
    /// The game draws a square from the LEVEL's save row, and every copy of a
    /// level in the run shares one. droha, 2026-09-28, on an unopened Pack 3
    /// whose Batteries (Randomized) and Stamps (Randomized) squares were solid
    /// because their copies in the opening are open. On that strip the six
    /// solid squares in shut packs were exactly the copies of a level open in
    /// the first 15 slots; the other 47 were outlines.
    ///
    /// Two methods set a square's sprite (the interop's xref cache):
    /// UpdateOverviewAppearance, for every square, called by LayoutOverview,
    /// RefreshOverviewItems and LevelsTrack.SetThemeColors; and
    /// OverviewItemUnlock, for one. Each picks one of six variants with
    /// Random.Range and sets it there and then, so a postfix sees the final
    /// sprite. The two arrays are the same six chalk squares, outline and
    /// filled (Default-Square-01..06 and Unlocked-Square-01..06 in level0), so
    /// swapping index for index keeps the variant.
    ///
    /// An open pack's squares are left alone: the overlay covers them.
    /// </summary>
    private static void DrawShutSquaresLocked(LevelsOverviewScrollbar bar, int only)
    {
        try
        {
            var state = Track.State;
            if (state == null || bar == null) return;

            // The Archive and DLC menus each have a strip too.
            var track = bar.levelsTrack;
            if (track == null || !Track.IsCampaignSelect(track.levelSelect)) return;

            var squares = bar.levelOverviewItems;
            var locked = bar.defaultSprites;
            var unlocked = bar.unlockedSprites;
            if (squares == null || locked == null || unlocked == null) return;

            // THE COLOUR TOO. With the sprite swapped, some squares still wore
            // the game's highlightColor: cream outlines among white ones, 35
            // of 65 shut squares, matching no field of their levels' save
            // rows (measured 2026-09-28 on the run's seed served fresh over
            // its finished save, every level there finished). The locked
            // look is the outline in regularColor, as every locked square in
            // 0.4.3's own screenshot is.
            var plain = bar.regularColor;

            var from = only < 0 ? 0 : only;
            var to = only < 0 ? squares.Count : Math.Min(only + 1, squares.Count);
            for (int i = from; i < to; i++)
            {
                var slot = Track.SlotAt(i);
                if (slot < 0 || state.IsOpen(slot)) continue;

                var square = squares[i];
                var sprite = square == null ? null : square.sprite;
                if (sprite == null) continue;

                for (int k = 0; k < unlocked.Length && k < locked.Length; k++)
                {
                    var filled = unlocked[k];
                    if (filled == null || filled.Pointer != sprite.Pointer) continue;
                    square!.sprite = locked[k];
                    break;
                }
                if (square!.color != plain) square.color = plain;
            }
        }
        catch (Exception e)
        {
            // Cosmetic. The click guard is what keeps the pack shut.
            Plugin.Logger.LogWarning($"badges: could not draw a shut square locked: {e.Message}");
        }
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

    /// <summary>Where the game put the strip; put back, since the bar now moves with it.</summary>
    private static Vector2 _fittedPosition;

    /// <summary>The game's width for the bar the strip sits in, and the one we set.</summary>
    private static float _fittedBarWidth;

    private static float _appliedBarWidth = -1f;

    /// <summary>
    /// Shrink the overview strip until it fits the screen.
    ///
    /// The game sizes this for its own widest campaign, about 85 cards. A run
    /// inserts a pack divider between blocks, so 79 puzzles builds 92 cards
    /// and the strip runs off the edge - reported from a full-length run. The
    /// default of 70 puzzles fits as it is, but a run may ask for up to 130
    /// (146 cards) and should not be punished with a strip it cannot see the
    /// end of.
    ///
    /// SCALE, NOT SPACING. The mod has no handle on the layout - there is no
    /// layout component here, the game positions each dot itself - so the only
    /// lever that cannot fight it is the strip's scale.
    ///
    /// AND THE BAR NARROWED WITH IT. The strip sits in 'Levels Overview
    /// Scrollbar', which carries the Unity Scrollbar the player drags and its
    /// Scroll Handle, and which the game sizes to the unscaled dots. Scaling
    /// only the strip left that bar 3037 wide on a 1920 screen at 130
    /// puzzles, its ends 558 past each edge, so a drag reached about the
    /// middle 62% of the track and the handle was drawn off-screen (droha,
    /// 2026-09-28; measured with DevTools `uitree`). The bar is centred and
    /// the strip is anchored to its left edge, so narrowing the bar by the
    /// same factor puts the scaled dots exactly inside it, centred, with the
    /// drag range and the handle on screen. Its width, not its scale: the
    /// game squeezes the bar's scale when it is grabbed.
    ///
    /// IDEMPOTENT, FROM A CACHED BASELINE. The game repaints these from
    /// UpdateOverviewAppearance at moments we do not control, and this runs on
    /// a one-second poll, so anything that multiplied the current value would
    /// shrink the strip to nothing over a minute of looking at the menu. A
    /// bar width that is not the one we set is the game laying it out again,
    /// and becomes the new baseline.
    /// </summary>
    private static void FitStrip(Transform strip)
    {
        try
        {
            var rect = strip.TryCast<RectTransform>();
            var bar = strip.parent == null
                ? null : strip.parent.TryCast<RectTransform>();
            if (rect == null || bar == null)
            {
                ReportFit($"no RectTransform (self={rect != null}, "
                          + $"parent={bar != null})");
                return;
            }

            if (!ReferenceEquals(_fittedStrip, strip))
            {
                _fittedStrip = strip;
                _fittedBase = strip.localScale;
                _fittedPosition = rect.anchoredPosition;
                _appliedBarWidth = -1f;
            }
            var barWidth = bar.sizeDelta.x;
            if (Mathf.Abs(barWidth - _appliedBarWidth) > 0.5f) _fittedBarWidth = barWidth;

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
            var width = _fittedBarWidth * wanted;

            if ((strip.localScale - target).sqrMagnitude < 0.000001f
                && (rect.anchoredPosition - _fittedPosition).sqrMagnitude < 0.01f
                && Mathf.Abs(barWidth - width) < 0.5f) return;

            strip.localScale = target;
            rect.anchoredPosition = _fittedPosition;
            bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
            _appliedBarWidth = width;
            Plugin.Logger.LogInfo(
                $"badges: overview strip scaled to {wanted:0.00}, its bar "
                + $"{_fittedBarWidth:0} -> {width:0} wide - "
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
    /// Room for the end dots themselves, which the leftmost-to-rightmost span
    /// of CENTRES leaves out, and a little air past them. At 24 a 130-puzzle
    /// strip touched both screen edges (2026-09-28: 0 and 4 px at 720p); the
    /// opening's squares are wider than a dot.
    /// </summary>
    private const float DotAllowance = 64f;

    /// <summary>
    /// How small the strip may get. At the maximum 130 puzzles (146 cards) it
    /// needs 0.60 (measured 2026-09-28), so this floor is only reached by
    /// something unforeseen - and a strip too small to read is no more useful
    /// than one that overflows.
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
    ///
    /// FROM THE GRANDPARENT: FitStrip narrows the bar itself, and a bar we
    /// narrowed would then be the smallest - each poll would fit to the last
    /// and shrink it again.
    /// </summary>
    private static float RoomFor(Transform strip)
    {
        var room = float.MaxValue;
        var walk = strip.parent == null ? null : strip.parent.parent;
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
