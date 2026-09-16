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
internal static partial class Badges
{
    private static readonly Color Green = new Color(0.25f, 0.70f, 0.30f);

    private static readonly Color Red = new Color(0.85f, 0.20f, 0.20f);

    /// <summary>Fallback only - the game's own star colour is preferred.</summary>
    private static readonly Color FallbackStar = new Color(0.98f, 0.80f, 0.25f);

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
        if (_counter != null)
        {
            try { UnityEngine.Object.Destroy(_counter.gameObject); } catch { }
            _counter = null;
            _counterStar = null;      // a child of the counter, already gone
        }
        if (_pills != null)
        {
            try { UnityEngine.Object.Destroy(_pills.gameObject); } catch { }
            _pills = null;
            _pillOrder.Clear();
            _pillTiles.Clear();
        }
        _sinceRefresh = 0f;
    }

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
        _sinceCounter = float.MaxValue;
        _sincePills = float.MaxValue;
        _sinceSubtitle = float.MaxValue;
        _subtitle = null;
    }

    /// <summary>
    /// A right-aligned label in the overlay's top-right corner.
    ///
    /// Factored out when the counter arrived: the tag and the counter are
    /// the same object bar a position, and a third one is coming for the
    /// ability pills. Six sites in this file open-code the same
    /// GameObject / RectTransform / graphic dance, and this is the first
    /// two of them collapsed.
    /// </summary>
    private static TextMeshProUGUI? MakeCorner(string name, Transform root,
                                               Vector2 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(260f, 28f);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Right;
        text.fontSize = 18f;
        text.raycastTarget = false;
        text.richText = true;
        return text;
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
