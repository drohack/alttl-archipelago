using System;
using System.Collections.Generic;
using ALTTLArchipelago.Core;
using ALTTLModKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ALTTLArchipelago;

/// <summary>
/// The beaten-or-starred counter above the track, and the star it borrows
/// from the game's own art.
/// </summary>
internal static partial class Badges
{
    private static float _sinceCounter;

    private static TextMeshProUGUI? _counter;

    /// <summary>
    /// How far along the goal is, on the level select.
    ///
    /// droha asked for a "levels beaten / needed" counter: the number only
    /// appeared in a toast that scrolls away, so the one screen where you
    /// decide what to play next never said how close you were.
    ///
    /// READS THE GOAL, not the beaten count. A star seed wants starred
    /// puzzles, and a counter that always said "beaten" would be quietly
    /// measuring the wrong thing on half the seeds - Checks.GoalProgress
    /// owns that choice so this does not have to.
    ///
    /// Everything structural here is copied from TickConnectedTag above and
    /// for the same reasons: the toast overlay as parent because the track
    /// scrolls and is rebuilt, the same visibility gate so it cannot float
    /// over a puzzle, and a place in RepaintSoon so it does not pop in a
    /// second after the screen has settled.
    /// </summary>
    internal static void TickGoalCounter()
    {
        _sinceCounter += Time.unscaledDeltaTime;
        if (_sinceCounter < 0.5f) return;
        _sinceCounter = 0f;

        var track = Track.Active ? Track.CampaignTrack() : null;
        var visible = track != null && track.gameObject.activeInHierarchy;

        var (done, needed, unit) = Checks.GoalProgress(Plugin.Seed);
        // A run with no goal to report is a run with nothing to say. Better
        // blank than "0 / 0".
        if (!visible || needed <= 0)
        {
            if (_counter != null) _counter.gameObject.SetActive(false);
            return;
        }

        if (_counter == null)
        {
            var root = Toasts.OverlayRoot;
            if (root == null) return;                // overlay not built yet

            // Under the connected tag, which owns the top-right corner and
            // is 28 high at y -18.
            _counter = MakeCorner("ApGoalCounter", root, new Vector2(-24f, -48f));
            if (_counter == null) return;
            BuildCounterStar();
        }

        _counter.gameObject.SetActive(true);

        // THE STAR GOAL GETS THE STAR, not the word. droha: "for star goal we
        // should have 0/50 [star icon]s instead of it saying stars or beaten."
        // It is the game's own level-select star, the same one the card wears
        // when a puzzle has nothing left on it, so the counter and the card
        // are plainly talking about the same thing.
        //
        // The beaten goal keeps its word. There is no icon in the game for
        // "finished any one way" - the Icon- sprites are per-LEVEL card art,
        // not per-anything-else - and inventing a glyph for it would be less
        // clear than the word, not more.
        var starred = Plugin.Seed?.GoalIsStars == true;
        if (_counterStar != null) _counterStar.gameObject.SetActive(starred);

        var colour = done >= needed ? "#6BC77A" : "#FFFFFFB0";
        _counter.text = starred
            ? $"<color={colour}>{done} / {needed}</color>"
            : $"<color={colour}>{done} / {needed}</color>"
              + $"<color=#FFFFFF80>  {unit}</color>";

        // The text shifts left to make room, rather than the star being laid
        // out around it - there is no layout group on the overlay and adding
        // one to position a single image would be more machinery than this
        // needs.
        var rt = _counter.rectTransform;
        rt.sizeDelta = new Vector2(starred ? 230f : 260f, 28f);
        rt.anchoredPosition = new Vector2(starred ? -50f : -24f, -48f);
    }

    private static Image? _counterStar;

    /// <summary>
    /// Borrow the level select's own star for the counter.
    ///
    /// By name from the loaded sprites, which is how FindStar below reaches
    /// the same art on a card - except that one scans a LevelIcon's children
    /// and there is no card here to scan. The names were read out of the
    /// running game with the DevTools `sprites` command rather than guessed:
    /// that command exists because this file's history contains two wrong
    /// guesses at a sprite name.
    ///
    /// Several candidates, in preference order. The level-select star first
    /// because matching the card is the whole point; the others are there so
    /// a renamed asset costs a plainer star rather than no counter.
    /// </summary>
    private static void BuildCounterStar()
    {
        if (_counter == null) return;

        var sprite = FindSpriteNamed(
            "LTL-LevelSelect-Star-solved", "Star-full", "Icon-Star-unlocked");
        if (sprite == null)
        {
            Plugin.Logger.LogInfo(
                "badges: no star sprite found, the counter will use its word");
            return;
        }

        var go = new GameObject("ApGoalStar");
        go.transform.SetParent(_counter.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(6f, 0f);
        rt.sizeDelta = new Vector2(20f, 20f);

        _counterStar = go.AddComponent<Image>();
        _counterStar.sprite = sprite;
        _counterStar.color = FallbackStar;
        _counterStar.raycastTarget = false;
        _counterStar.preserveAspect = true;
    }

    /// <summary>
    /// The first loaded sprite matching any of these names, or null.
    ///
    /// FindObjectsOfTypeAll because a sprite on an inactive object is still
    /// a perfectly good sprite, and the level select is inactive at the
    /// moment this runs.
    /// </summary>
    private static Sprite? FindSpriteNamed(params string[] names)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll(
                Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
            if (all == null) return null;

            // By preference order, not by scan order: a later name must not
            // win just because it sorts earlier in the asset table.
            foreach (var wanted in names)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var sprite = all[i]?.TryCast<Sprite>();
                    if (sprite == null) continue;
                    if (string.Equals(Str(() => sprite.name), wanted,
                                      StringComparison.Ordinal))
                    {
                        return sprite;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"badges: sprite search failed: {e.Message}");
        }
        return null;
    }
}
