using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLModKit;

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
public static class Toasts
{
    /// <summary>
    /// How long a message stays fully visible before fading.
    ///
    /// Eight seconds, not four. On connecting, several land at once - the
    /// connection notice and every item the server replays - and at four
    /// seconds they were gone before they could be read.
    /// </summary>
    private const float HoldSeconds = 8f;

    /// <summary>
    /// The hold used while a backlog is waiting to be shown.
    ///
    /// A burst that overflows the screen should CLEAR, not trickle. At the
    /// full eight seconds a queue of fifteen takes two minutes to drain, by
    /// which time the player has left the level it was telling them about.
    /// </summary>
    private const float BusyHoldSeconds = 2.5f;

    /// <summary>How long the fade itself takes.</summary>
    private const float FadeSeconds = 1.5f;

    /// <summary>
    /// Most lines on screen at once. Beyond this, further lines WAIT - see
    /// _pending. They are not dropped, which is what this used to do.
    ///
    /// Five was too few and the eviction was the real bug: finishing a level
    /// can solve several controllers and the level in one frame, and a Skip
    /// reports every remaining location on the slot. The earliest lines were
    /// destroyed before rendering a single frame - indistinguishable, on
    /// screen, from never having been raised. droha: "I don't always see it
    /// showing my items being released/received."
    /// </summary>
    private const int MaxVisible = 10;

    private sealed class Line
    {
        internal TextMeshProUGUI Label = null!;
        internal float Age;
    }

    private static GameObject? _canvas;
    private static Transform? _stack;
    private static readonly List<Line> _lines = new();

    /// <summary>
    /// Messages waiting to be shown, for either of two reasons.
    ///
    /// The canvas may not exist yet: connecting happens at the title screen,
    /// where the fonts we borrow may not be loaded. Without this the very
    /// first messages - the ones about connecting - would be the only ones
    /// nobody ever sees.
    ///
    /// Or the screen is FULL. That case used to destroy the oldest line
    /// instead, which is why a burst read as messages never arriving. Holding
    /// them costs a few strings and they appear as room frees up.
    /// </summary>
    private static readonly Queue<(string Text, Color Colour)> _pending = new();

    /// <summary>Cap on the wait queue, so a long disconnect cannot grow it forever.</summary>
    private const int MaxPending = 200;


    /// <summary>
    /// Default text colour. Individual names inside a line are coloured with
    /// TextMeshPro rich text from ApPalette, so they match what the same
    /// message looks like in the Archipelago text client - anyone who plays
    /// multiworlds reads those colours as meaning.
    /// </summary>
    public static Color Plain { get; set; } = Color.white;

    /// <summary>Connection notices and other things the mod itself says.</summary>
    public static Color Notice { get; set; } = new(1f, 0.68f, 0.28f);

    /// <summary>Parse "RRGGBB" into a colour, for callers holding a palette.</summary>
    public static Color HexColor(string hex)
        => new(
            Convert.ToInt32(hex.Substring(0, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255f);

    /// <summary>
    /// Where to report a problem building or drawing the overlay.
    /// A delegate, so the kit does not need to know whose logger it is.
    /// </summary>
    public static Action<string>? OnWarning { get; set; }

    /// <summary>Where to report ordinary progress. Optional, like OnWarning.</summary>
    public static Action<string>? OnInfo { get; set; }

    /// <summary>
    /// Name of the overlay GameObject. Settable so two mods using the kit do
    /// not present the scene with two identically named canvases - which is
    /// exactly the confusion that made a badge get painted onto the Archive's
    /// track instead of ours.
    /// </summary>
    public static string CanvasName { get; set; } = "ModKitToasts";

    /// <summary>
    /// The overlay's root, for a consumer that wants to draw its own thing on
    /// it - an animation, a marker - without building a second canvas that
    /// would fight this one for sort order. Null until the overlay is up.
    /// </summary>
    public static Transform? OverlayRoot => _canvas == null ? null : _canvas.transform;

    /// <summary>
    /// Every toast, and every toast that did NOT make it, in the log.
    ///
    /// A run raises a few dozen of these, so the trail is cheap. It exists
    /// because "the toast message doesn't always pop up" was reported with no
    /// way to tell WHICH of four silences had happened: the overlay not up
    /// yet, the pending queue full, an eviction off the top of the stack, or
    /// the toast never being raised at all. Only the last is a bug in the
    /// caller, and without a line here all four look identical.
    /// </summary>
    public static void Show(string text, Color colour)
    {
        if (string.IsNullOrEmpty(text)) return;

        // No overlay yet, or no room on it. Both wait; neither discards.
        if (_stack == null || _lines.Count >= MaxVisible)
        {
            var why = _stack == null ? "the overlay is not up" : "the screen is full";
            if (_pending.Count < MaxPending)
            {
                _pending.Enqueue((text, colour));
                OnInfo?.Invoke($"toast queued, {why}: {text}");
            }
            else
            {
                OnWarning?.Invoke($"toast DROPPED, {_pending.Count} already "
                                  + $"waiting and {why}: {text}");
            }
            return;
        }

        OnInfo?.Invoke($"toast: {text}");

        try
        {
            AddLine(text, colour);
        }
        catch (Exception e)
        {
            Warn($"toast: could not show '{text}': {e.Message}");
        }
    }

    public static void Tick(float dt)
    {
        try
        {
    
            if (_stack == null)
            {
                if (_pending.Count > 0) Build();
                return;
            }

            // ONLY INTO FREE SPACE. Draining the whole queue in one frame is
            // what made MaxVisible a shredder rather than a limit.
            while (_pending.Count > 0 && _lines.Count < MaxVisible)
            {
                var (text, colour) = _pending.Dequeue();
                AddLine(text, colour);
            }

            // Something waiting means the lines on screen are in the way, so
            // they get the short hold. Read once per frame rather than per
            // line: a line must not expire on a different clock from its
            // neighbour just because the queue emptied mid-loop.
            var hold = _pending.Count > 0 ? BusyHoldSeconds : HoldSeconds;

            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var line = _lines[i];
                line.Age += dt;

                if (line.Age >= hold + FadeSeconds)
                {
                    if (line.Label != null) UnityEngine.Object.Destroy(line.Label.gameObject);
                    _lines.RemoveAt(i);
                    continue;
                }

                if (line.Age > hold && line.Label != null)
                {
                    var left = 1f - (line.Age - hold) / FadeSeconds;
                    var c = line.Label.color;
                    line.Label.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(left));
                }
            }
        }
        catch (Exception e)
        {
            Warn($"toast: tick failed: {e.Message}");
        }
    }

    /// <summary>Tear down, so a reconnect does not stack two canvases.</summary>
    public static void Destroy()
    {
        try
        {
            _lines.Clear();
            _pending.Clear();
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
        }
        catch (Exception e)
        {
            Warn($"toast: could not tear down: {e.Message}");
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
            _canvas = new GameObject(CanvasName);
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
            OnInfo?.Invoke("toasts: overlay ready");
        }
        catch (Exception e)
        {
            Warn($"toast: could not build the overlay: {e.Message}");
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

        // UNREACHABLE, and loud if it ever is. Show and Tick are the only two
        // ways in and both refuse once the screen is full, so overflow waits
        // in _pending instead of being destroyed here.
        //
        // This used to be the eviction loop, and it was the bug: the oldest
        // line was destroyed before it had rendered a single frame, which on
        // screen is indistinguishable from the message never being raised.
        // Kept as a tripwire rather than deleted, because the thing that
        // brings it back is a THIRD caller added later that skips the gate.
        if (_lines.Count > MaxVisible)
        {
            OnWarning?.Invoke($"toast: {_lines.Count} lines with a cap of "
                              + $"{MaxVisible} - a caller reached AddLine "
                              + "without checking for room; messages will be "
                              + "crowded off screen");
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

    private static void Warn(string message) => OnWarning?.Invoke(message);
}
