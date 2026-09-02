using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Spike: put a readable level name on each level-select card.
///
/// The question this answers is whether Archipelago location names can be
/// content-based ("Cookies Jigsaw (Good Tidings) - Match Reindeer") rather
/// than positional ("Chapter 1 - Level 5 - Part 3"). Content names are far
/// more useful in a hint, but only if the player can connect the name to a
/// card - and the game never shows a level name anywhere.
///
/// So we add one. If this renders legibly the naming question is settled.
///
/// Throwaway: the real version belongs in the mod, driven by the shared level
/// table, and shown on focus rather than always.
/// </summary>
internal static class CardLabels
{
    private static readonly (string Prefix, string Pack)[] Packs =
    {
        ("GoodTidings_", "Good Tidings"),
        ("TrickOrTidy_", "Trick or Tidy"),
        ("MerryMess_", "Merry Mess"),
        ("NeatStreak_", "Drawer Chores"),
        ("SomethingEggstra ", "Something Eggstra"),
        ("SnackPack ", "Snack Pack"),
    };

    /// <summary>Mirror of ALTTLArchipelago.Core.DisplayNames, duplicated here
    /// only because DevTools deliberately shares no code with the mod.</summary>
    private static string Display(string levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId)) return "";
        foreach (var (prefix, pack) in Packs)
        {
            if (!levelId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var stem = levelId.Substring(prefix.Length);
            if (stem == "JackOLanterns") stem = "Jack O'Lanterns";
            else stem = Regex.Replace(stem, @"(?<=[a-z])(?=[A-Z])", " ");
            stem = stem.Replace("(", "").Replace(")", "");
            return Collapse(stem) + " (" + pack + ")";
        }
        return Collapse(Regex.Replace(levelId, @"(?<=[a-z])(?=[A-Z])", " "));
    }

    private static string Collapse(string s)
        => string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Adds a name label beneath every card on the open track.</summary>
    internal static void Apply(bool on)
    {
        var track = UnityEngine.Object.FindObjectOfType<LevelsTrack>();
        if (track == null)
        {
            DevToolsPlugin.Log.LogWarning("cardlabels: open menu:levels first");
            return;
        }

        var font = FindFont();
        if (font == null)
        {
            DevToolsPlugin.Log.LogWarning("cardlabels: no TMP font found in the scene");
            return;
        }

        var items = track.trackItems;
        int labelled = 0;
        for (int i = 0; i < (items == null ? 0 : items.Count); i++)
        {
            var icon = items![i];
            if (icon == null || icon.level == null) continue;

            var existing = icon.transform.Find("ApNameLabel");
            if (!on)
            {
                if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
                continue;
            }
            if (existing != null) continue;

            try
            {
                var go = new GameObject("ApNameLabel");
                go.transform.SetParent(icon.transform, false);

                var rt = go.AddComponent<RectTransform>();
                // Beneath the card, spanning a little wider than it so two
                // short words do not wrap.
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -14f);
                rt.sizeDelta = new Vector2(260f, 56f);

                var text = go.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.text = Display(icon.level.LevelId);
                text.fontSize = 22f;
                text.alignment = TextAlignmentOptions.Top;
                text.enableWordWrapping = true;
                text.color = new Color(1f, 1f, 1f, 0.92f);
                labelled++;
            }
            catch (Exception e)
            {
                DevToolsPlugin.Log.LogWarning($"cardlabels: {icon.level.LevelId}: {e.Message}");
            }
        }

        DevToolsPlugin.Log.LogInfo(on
            ? $"cardlabels: labelled {labelled} cards"
            : "cardlabels: removed");
    }

    /// <summary>
    /// Borrow the game's own font atlas rather than shipping one. Prefer a
    /// live in-use component, because a font pulled from FindObjectsOfTypeAll
    /// can be one the game never actually renders with.
    /// </summary>
    private static TMP_FontAsset? FindFont()
    {
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t != null && t.font != null) return t.font;
        }
        // The IL2CPP interop exposes only the non-generic overload here.
        foreach (var o in Resources.FindObjectsOfTypeAll(
                     Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>()))
        {
            var f = o == null ? null : o.TryCast<TMP_FontAsset>();
            if (f != null) return f;
        }
        return null;
    }
}
