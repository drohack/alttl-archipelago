using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ALTTLModKit;
using UnityEngine;

namespace ALTTLDevTools;

/// <summary>
/// Finding, exporting and displaying the game's loaded sprites.
///
/// How the twelve ability icons the mod ships were found - see
/// docs/data/ability-icons.md.
/// </summary>
public partial class DevToolsBehaviour
{
    /// <summary>Sprite names seen by the last `newsprites` call.</summary>
    private static readonly HashSet<string> _spritesSeen =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Sprite names that have appeared since the last time this was run.
    ///
    /// Loading a level adds its objects to the loaded set, so the DIFFERENCE
    /// is exactly that level's art - no guessing at names, and no wading
    /// through the nine hundred that were already there.
    /// </summary>
    private static void DumpNewSprites()
    {
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        var fresh = new List<string>();

        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0) continue;
            if (_spritesSeen.Add(name)) fresh.Add(name);
        }

        fresh.Sort(StringComparer.Ordinal);
        foreach (var name in fresh) DevToolsPlugin.Log.LogInfo($"newsprites:  {name}");
        DevToolsPlugin.Log.LogInfo(
            $"newsprites: {fresh.Count} new, {_spritesSeen.Count} seen so far");
    }

    /// <summary>
    /// Write named sprites out as PNG files.
    ///
    /// So a human can LOOK at them somewhere other than inside the game.
    /// The grid command puts candidates on screen, which answers "does this
    /// read at icon size", but comparing a dozen options properly means
    /// having the images in hand - droha, reasonably: "is there a way for
    /// me to see these easily? You're just kind of giving the icon name and
    /// a description."
    ///
    /// THROUGH A RENDER TEXTURE, because the game's textures are not
    /// readable. Reading sprite.texture directly throws on an imported
    /// texture without Read/Write enabled, which is all of them; blitting
    /// to a RenderTexture and reading THAT back is the standard way round
    /// it and needs no asset changes.
    ///
    /// Cropped to textureRect, because these are atlased: the whole texture
    /// is a sheet of dozens of sprites, and exporting it would produce the
    /// same sheet a dozen times.
    ///
    ///     spriteexport:Badge1-Books2,badge3-Tape|C:/somewhere
    /// </summary>
    private static void ExportSprites(string arg)
    {
        var parts = arg.Split('|');
        var names = new List<string>();
        foreach (var raw in parts[0].Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0) names.Add(name);
        }
        var dir = parts.Length > 1 ? parts[1].Trim() : "";
        if (dir.Length == 0 || names.Count == 0)
        {
            DevToolsPlugin.Log.LogWarning(
                "spriteexport: give names and a folder, as "
                + "'spriteexport:A,B|C:/folder'");
            return;
        }

        try { System.IO.Directory.CreateDirectory(dir); }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"spriteexport: {e.Message}");
            return;
        }

        var found = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0 || found.ContainsKey(name)) continue;
            if (names.Contains(name)) found[name] = sprite;
        }

        var written = 0;
        foreach (var name in names)
        {
            if (!found.TryGetValue(name, out var sprite)) continue;
            if (WriteSpritePng(sprite, name, dir)) written++;
        }

        DevToolsPlugin.Log.LogInfo(
            $"spriteexport: wrote {written} of {names.Count} to {dir}");
    }

    private static bool WriteSpritePng(Sprite sprite, string name, string dir)
    {
        RenderTexture? rt = null;
        RenderTexture? previous = null;
        Texture2D? readable = null;
        try
        {
            var source = sprite.texture;
            if (source == null) return false;

            var rect = sprite.textureRect;
            var w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var h = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            rt = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);

            previous = RenderTexture.active;
            RenderTexture.active = rt;

            readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // FLIPPED IN Y. Graphics.Blit writes the RenderTexture upside
            // down on D3D relative to the source, while sprite.textureRect
            // is measured from the bottom of the source. Reading at the
            // rect as given returned a neighbouring sprite from the atlas -
            // asking for stacked books and getting a bottle opener.
            var y = source.height - Mathf.RoundToInt(rect.y) - h;
            readable.ReadPixels(new Rect(rect.x, y, w, h), 0, 0, false);
            readable.Apply();

            var bytes = ImageConversion.EncodeToPNG(readable);
            if (bytes == null || bytes.Length == 0) return false;

            // The names carry no path characters today, but a sprite name is
            // the game's to choose and a stray slash would write outside the
            // folder we were given.
            var safe = name;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(bad, '_');
            }

            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(dir, safe + ".png"), bytes);
            return true;
        }
        catch (Exception e)
        {
            DevToolsPlugin.Log.LogWarning($"spriteexport: {name}: {e.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    private static GameObject? _spriteGrid;

    /// <summary>
    /// Draw named sprites in a grid, so a human can SEE them.
    ///
    /// A sprite name says nothing about silhouette, colour or how it reads
    /// at twenty pixels, and the question this exists for is exactly that:
    /// droha, on picking art for the ability pills, "look for ones easy to
    /// distinguish at a glance". That cannot be answered from a name list,
    /// and it cannot be answered by extracting textures either - the game's
    /// are not readable without a RenderTexture round trip. Putting them on
    /// the screen the game is already drawing sidesteps both problems.
    ///
    /// Each entry is drawn twice - large enough to identify, and at pill
    /// size - because an icon that is obvious at 56 pixels and mud at 20 is
    /// no use for a legend.
    ///
    ///     spritegrid:Badge1-Books1,Badge5-Broom
    ///     spritegrid:off
    /// </summary>
    private static void ShowSpriteGrid(string arg)
    {
        if (_spriteGrid != null)
        {
            UnityEngine.Object.Destroy(_spriteGrid);
            _spriteGrid = null;
        }
        if (string.IsNullOrWhiteSpace(arg) || arg == "off")
        {
            DevToolsPlugin.Log.LogInfo("spritegrid: cleared");
            return;
        }

        var wanted = new List<string>();
        foreach (var raw in arg.Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0) wanted.Add(name);
        }

        var found = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        for (int i = 0; all != null && i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;
            var name = Str(() => sprite.name);
            if (name.Length == 0 || found.ContainsKey(name)) continue;
            if (wanted.Contains(name)) found[name] = sprite;
        }

        var canvasGo = new GameObject("ApSpriteGrid");
        _spriteGrid = canvasGo;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;          // over everything, including toasts
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // An opaque backing, so a pale icon is not judged against whatever
        // happens to be behind it.
        var bg = new GameObject("BG");
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.11f, 0.13f, 0.12f, 1f);

        const int columns = 6;
        const float cell = 300f;
        const float rowHeight = 165f;

        var shown = 0;
        foreach (var name in wanted)
        {
            if (!found.TryGetValue(name, out var sprite)) continue;

            var col = shown % columns;
            var row = shown / columns;
            var x = 40f + col * cell;
            var y = -40f - row * rowHeight;

            AddGridSprite(canvasGo.transform, sprite, x, y, 96f);
            AddGridSprite(canvasGo.transform, sprite, x + 110f, y - 30f, 26f);
            AddGridLabel(canvasGo.transform, name, x, y - 100f, cell - 20f);
            shown++;
        }

        var missing = new List<string>();
        foreach (var n in wanted) if (!found.ContainsKey(n)) missing.Add(n);
        DevToolsPlugin.Log.LogInfo(
            $"spritegrid: showing {shown} of {wanted.Count}"
            + (missing.Count > 0
                ? $"; not loaded: {string.Join(", ", missing)}"
                : ""));
    }

    private static void AddGridSprite(Transform parent, Sprite sprite,
                                      float x, float y, float size)
    {
        var go = new GameObject("s");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(size, size);

        var image = go.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void AddGridLabel(Transform parent, string text,
                                     float x, float y, float width)
    {
        var go = new GameObject("t");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, 40f);

        var label = go.AddComponent<TMPro.TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 16f;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    /// <summary>
    /// Every loaded sprite whose name contains a substring.
    ///
    /// EXISTS SO NOBODY GUESSES A SPRITE NAME AGAIN. The mod borrows the
    /// game's art by name in two places, and the comment history on
    /// Badges.FindStar records two wrong guesses before the right name was
    /// found. Asking the runtime what it has costs one command.
    ///
    /// The immediate use is the ability strip: it draws lettered pills
    /// because the mod ships no art, and the question of whether the game
    /// already has a per-mechanic icon is answerable rather than arguable.
    ///
    ///     sprites star        everything with "star" in the name
    ///     sprites             everything, which is a lot
    /// </summary>
    private static void DumpSprites(string filter)
    {
        filter = (filter ?? "").Trim();

        var all = Resources.FindObjectsOfTypeAll(
            Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
        if (all == null)
        {
            DevToolsPlugin.Log.LogWarning("sprites: nothing loaded");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < all.Length; i++)
        {
            var sprite = all[i]?.TryCast<Sprite>();
            if (sprite == null) continue;

            var name = Str(() => sprite.name);
            if (string.IsNullOrEmpty(name)) continue;
            if (filter.Length > 0
                && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            // Deduped: the same sprite is referenced from many objects, and
            // an undeduped dump of this is thousands of identical lines.
            if (!seen.Add(name)) continue;

            DevToolsPlugin.Log.LogInfo($"sprites:  {name}");
        }

        DevToolsPlugin.Log.LogInfo(
            $"sprites: {seen.Count} distinct name(s) of {all.Length} loaded"
            + (filter.Length > 0 ? $" matching '{filter}'" : ""));
    }

    /// <summary>
    /// What state the credits card is actually in on the level select.
    ///
    /// droha: "when i went back to the level select i see a level with a hand
    /// print as the icon. it's greyed out like I can't play it. I think it's
    /// the credits but I can't tell."
    ///
    /// The suspicion to test is that Track.ApplyUnlocks creates completion
    /// data for the chapter dividers and for the run's open slots, and the
    /// credits card is neither - it is appended to the track separately, after
    /// the loop, so nothing ever sets unlockedOnLevelSelect on it. A card with
    /// no completion row draws locked. This reads the three things that would
    /// settle it rather than inferring from a screenshot.
    /// </summary>
    private static void ReportCreditsCard()
    {
        var manager = GameManager.Instance?.levelManager;
        if (manager == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no LevelManager");
            return;
        }

        LevelInterface? credits = null;
        var all = manager.m_allLevelInterfaces;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var li = all[i];
                if (li == null) continue;
                var isCredits = false;
                try { isCredits = li.IsCredits; } catch { continue; }
                if (isCredits) { credits = li; break; }
            }
        }

        if (credits == null)
        {
            DevToolsPlugin.Log.LogWarning("creditscard: no credits level found");
            return;
        }

        var has = Str(() =>
            SaveSystem.data.LevelHasCompletionData(credits).ToString());
        var flag = "-";
        try
        {
            if (SaveSystem.data.LevelHasCompletionData(credits))
            {
                var entry = SaveSystem.data.GetLevelCompletionData(credits);
                flag = entry == null
                    ? "no entry" : entry.unlockedOnLevelSelect.ToString();
            }
        }
        catch (Exception e) { flag = "threw: " + e.Message; }

        // The card's OWN art, by name. The mod picks no icon for this card -
        // it puts the game's credits level on the track and the LevelIcon
        // draws whatever that level carries - so naming the sprite settles
        // whether the hand print is authored for the credits or something we
        // caused. droha: "is that for the credits, or you just picked it?"
        var locked = Str(() => credits.LockedIcon == null
            ? "none" : credits.LockedIcon.name);
        var unlocked = Str(() => credits.UnlockedIcon == null
            ? "none" : credits.UnlockedIcon.name);

        DevToolsPlugin.Log.LogInfo(
            $"creditscard: lockedIcon={locked} unlockedIcon={unlocked}");

        DevToolsPlugin.Log.LogInfo(
            "creditscard: id=" + Str(() => credits.LevelId)
            + " index=" + Str(() => credits.LevelIndex.ToString())
            + " isUnlocked=" + Str(() => credits.IsUnlocked.ToString())
            + " hasCompletionData=" + has
            + " unlockedOnLevelSelect=" + flag);
    }
}
