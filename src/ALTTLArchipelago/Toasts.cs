using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// Transient messages in the corner: what you just found, what someone sent
/// you, whether the server is still there.
///
/// The game has no toast system to borrow. SkipTooltip and the tutorial prompts
/// are the closest things and neither is reusable, so this is our own canvas -
/// which after the connection pane is the safer bet anyway: nothing here is
/// cloned, re-parented or handed to the game, so nothing can be silently
/// replaced by a copy the way the modal content was.
///
/// It CANNOT take input. The canvas has no GraphicRaycaster and every label is
/// raycastTarget=false, so there is no path by which a message drifting over a
/// puzzle piece could eat the click that was meant to move it.
/// </summary>
internal static class Toasts
{
    /// <summary>
    /// How long a message stays fully visible before fading.
    ///
    /// Eight seconds, not four. On connecting, several land at once - the
    /// connection notice and every item the server replays - and at four
    /// seconds they were gone before they could be read.
    /// </summary>
    private const float HoldSeconds = 8f;

    /// <summary>How long the fade itself takes.</summary>
    private const float FadeSeconds = 1.5f;

    /// <summary>
    /// Most lines on screen at once. Beyond this the oldest is dropped
    /// immediately: a burst of twenty checks should not paint over the puzzle.
    /// </summary>
    private const int MaxVisible = 5;

    private sealed class Line
    {
        internal TextMeshProUGUI Label = null!;
        internal float Age;
    }

    private static GameObject? _canvas;
    private static Transform? _stack;
    private static readonly List<Line> _lines = new();

    /// <summary>
    /// Messages raised before the canvas could be built.
    ///
    /// Connecting happens at the title screen, where the fonts we borrow may
    /// not exist yet. Without this the very first messages - the ones about
    /// connecting - would be the only ones nobody ever sees.
    /// </summary>
    private static readonly Queue<(string Text, Color Colour)> _pending = new();

    internal static int Shown { get; private set; }

    /// <summary>
    /// Default text colour. Individual names inside a line are coloured with
    /// TextMeshPro rich text from ApPalette, so they match what the same
    /// message looks like in the Archipelago text client - anyone who plays
    /// multiworlds reads those colours as meaning.
    /// </summary>
    internal static readonly Color Plain = HexColor(ALTTLArchipelago.Core.ApPalette.White);

    /// <summary>Connection notices and other things the mod itself says.</summary>
    internal static readonly Color Notice = HexColor(ALTTLArchipelago.Core.ApPalette.Orange);

    private static Color HexColor(string hex)
        => new(
            Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255f);

    internal static void Show(string text, Color colour)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_stack == null)
        {
            // Bounded, so a long disconnected spell cannot grow without limit.
            if (_pending.Count < 20) _pending.Enqueue((text, colour));
            return;
        }

        try
        {
            AddLine(text, colour);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: could not show '{text}': {e.Message}");
        }
    }

    private static GameObject? _paw;
    private static float _pawAge;

    /// <summary>How long the swipe takes, end to end.</summary>
    private const float SwipeSeconds = 0.7f;

    /// <summary>
    /// Sweep a cat paw across the screen.
    ///
    /// Ours, on our own canvas, rather than the game's CatPaw: that is a
    /// per-level object which most levels simply do not have, so a trap relying
    /// on it showed nothing at all on the level where it fired. The point is to
    /// SEE something go through, not to reproduce the original animation.
    /// </summary>
    internal static void SweepPaw()
    {
        try
        {
            if (_stack == null || _canvas == null) return;
            if (_paw != null) UnityEngine.Object.Destroy(_paw);

            _paw = new GameObject("catpaw");
            _paw.transform.SetParent(_canvas.transform, false);

            var rect = _paw.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(320f, 320f);

            var image = _paw.AddComponent<Image>();
            image.sprite = FindPawSprite();
            image.color = image.sprite == null
                ? new Color(0.10f, 0.10f, 0.12f, 0.9f)   // silhouette
                : Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;

            _pawAge = 0f;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: no paw: {e.Message}");
        }
    }

    private static void TickPaw(float dt)
    {
        if (_paw == null) return;

        _pawAge += dt;
        if (_pawAge >= SwipeSeconds)
        {
            UnityEngine.Object.Destroy(_paw);
            _paw = null;
            return;
        }

        var t = _pawAge / SwipeSeconds;
        var rect = _paw.GetComponent<RectTransform>();
        if (rect == null) return;

        // In from the right, out to the left, dipping as it goes - a swipe
        // rather than a slide.
        rect.anchoredPosition = new Vector2(
            Mathf.Lerp(1200f, -1200f, t),
            Mathf.Sin(t * Mathf.PI) * -160f + 120f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-25f, 25f, t));
    }

    private static Sprite? _pawSprite;
    private static bool _pawSpriteSearched;

    /// <summary>
    /// A cat sprite from whatever the game has loaded, if there is one.
    ///
    /// Falls back to a plain dark silhouette, which still reads as something
    /// swiping past. Shipping art would mean shipping art.
    /// </summary>
    private static Sprite? FindPawSprite()
    {
        if (_pawSpriteSearched) return _pawSprite;
        _pawSpriteSearched = true;

        try
        {
            // The IL2CPP shim exposes only the non-generic overload, which
            // hands back UnityEngine.Object and needs casting per item.
            var all = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
            for (int i = 0; i < (all == null ? 0 : all.Count); i++)
            {
                var sprite = all![i]?.TryCast<Sprite>();
                var name = sprite == null ? "" : sprite.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("paw", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("cat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pawSprite = sprite;
                    Plugin.Logger.LogInfo($"toast: using '{name}' for the cat paw");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: could not find a paw sprite: {e.Message}");
        }
        return _pawSprite;
    }

    internal static void Tick(float dt)
    {
        try
        {
            TickPaw(dt);

            if (_stack == null)
            {
                if (_pending.Count > 0) Build();
                return;
            }

            while (_pending.Count > 0)
            {
                var (text, colour) = _pending.Dequeue();
                AddLine(text, colour);
            }

            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var line = _lines[i];
                line.Age += dt;

                if (line.Age >= HoldSeconds + FadeSeconds)
                {
                    if (line.Label != null) UnityEngine.Object.Destroy(line.Label.gameObject);
                    _lines.RemoveAt(i);
                    continue;
                }

                if (line.Age > HoldSeconds && line.Label != null)
                {
                    var left = 1f - (line.Age - HoldSeconds) / FadeSeconds;
                    var c = line.Label.color;
                    line.Label.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(left));
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: tick failed: {e.Message}");
        }
    }

    /// <summary>Tear down, so a reconnect does not stack two canvases.</summary>
    internal static void Destroy()
    {
        try
        {
            _lines.Clear();
            _pending.Clear();
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: could not tear down: {e.Message}");
        }
        _canvas = null;
        _stack = null;
    }

    private static void Build()
    {
        var font = FindFont();
        if (font == null) return;        // no text on screen yet; try again next tick

        try
        {
            _canvas = new GameObject("ALTTLArchipelagoToasts");
            UnityEngine.Object.DontDestroyOnLoad(_canvas);

            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the game's own UI. A message hidden behind a menu is the
            // same as no message.
            canvas.sortingOrder = 5000;

            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Deliberately NO GraphicRaycaster. Without one this canvas is
            // invisible to the input system and cannot swallow a click.

            var stack = new GameObject("stack");
            stack.transform.SetParent(_canvas.transform, false);

            var rect = stack.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(32f, 32f);
            rect.sizeDelta = new Vector2(760f, 0f);

            var layout = stack.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 4f;

            var fitter = stack.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _stack = stack.transform;
            Plugin.Logger.LogInfo("toasts: overlay ready");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"toast: could not build the overlay: {e.Message}");
            Destroy();
        }
    }

    private static void AddLine(string text, Color colour)
    {
        if (_stack == null) return;

        var go = new GameObject("toast");
        go.transform.SetParent(_stack, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = FindFont();
        label.text = text;
        label.color = colour;
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.enableWordWrapping = true;
        label.richText = true;              // colour tags from ApPalette

        // Belt and braces alongside the missing raycaster.
        label.raycastTarget = false;

        // A dark outline, because the message lands on whatever the puzzle
        // happens to be and light text on a light background is unreadable.
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 200);

        _lines.Add(new Line { Label = label });
        Shown++;

        while (_lines.Count > MaxVisible)
        {
            var oldest = _lines[0];
            if (oldest.Label != null) UnityEngine.Object.Destroy(oldest.Label.gameObject);
            _lines.RemoveAt(0);
        }
    }

    /// <summary>
    /// Borrow a font from whatever the game already has on screen, the same
    /// trick the connection pane uses. Loading our own would mean shipping one.
    /// </summary>
    private static TMP_FontAsset? _font;

    /// <summary>
    /// Borrow a font from whatever the game already has on screen, the same
    /// trick the connection pane uses. Loading our own would mean shipping one.
    ///
    /// Kept once found. The scan allocates an array of every TMP component in
    /// the scene, and it was run per toast LINE - so a reconnect, which replays
    /// the whole item list at once, did a full scene scan for each item in a
    /// single frame.
    /// </summary>
    private static TMP_FontAsset? FindFont()
    {
        if (_font != null) return _font;

        foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t != null && t.font != null) return _font = t.font;
        }
        return null;
    }
}
