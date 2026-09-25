using System;
using HarmonyLib;

namespace ALTTLArchipelago;

/// <summary>
/// The hover stars on a run's card: one per Solution location of its slot,
/// lit once collected (CheckRouter.SolutionStars). droha, 2026-09-24: they
/// start empty for each seed and fill as its solutions are completed, never
/// synced with the player's own save; and a skipped card's stars all fill.
///
/// The game sets them from the level's save row, which a generator puzzle
/// never writes: Spider Web, finished in a run, kept its star off
/// (2026-09-25). So the game's pass is overwritten right after it runs -
/// SetCompletionStars runs for every card each time the track is built - and
/// the badge refresh applies it again while the track is up, for a check
/// collected with the track on screen.
/// </summary>
[HarmonyPatch]
internal static class CardStars
{
    [HarmonyPatch(typeof(LevelIcon), nameof(LevelIcon.SetCompletionStars))]
    [HarmonyPostfix]
    private static void AfterSetCompletionStars(LevelIcon __instance)
    {
        try
        {
            Apply(__instance, PositionOf(__instance));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"card stars: {e.Message}");
        }
    }

    /// <summary>
    /// Set one card's stars from the run. Does nothing for a card that is not
    /// one of the run's puzzles, or when no run is up.
    /// </summary>
    internal static void Apply(LevelIcon icon, int position)
    {
        var router = Checks.Router;
        if (router == null || icon == null || position < 0) return;
        var slot = Track.SlotAt(position);
        if (slot < 0) return;

        var stars = icon.stars;
        if (stars == null) return;

        var (lit, total) = router.SolutionStars(slot, Checks.Ledger.IsCollected);
        // All of them once every Solution location is in, even if the card
        // draws more stars than the seed has Solution locations.
        var all = total > 0 && lit >= total;

        // Each star is a bare Toggle under LevelIcon.stars: the card's
        // "Small Completion Star(Clone)" objects carry no LevelCompletionStar
        // (iconinfo, 2026-09-25).
        var n = 0;
        for (int i = 0; i < stars.childCount; i++)
        {
            var toggle = stars.GetChild(i).GetComponent<UnityEngine.UI.Toggle>();
            if (toggle == null) continue;
            var on = all || n < lit;
            if (toggle.isOn != on) toggle.SetIsOnWithoutNotify(on);
            n++;
        }
    }

    /// <summary>
    /// The card's position on the campaign track, or -1. Found through the
    /// card's own parents: this runs for every card on every build, which is
    /// too often to search the scene each time.
    /// </summary>
    private static int PositionOf(LevelIcon icon)
    {
        LevelSelect? select = null;
        for (var t = icon.transform; t != null && select == null; t = t.parent)
        {
            select = t.GetComponent<LevelSelect>();
        }
        if (!Track.IsCampaignSelect(select)) return -1;

        var items = select!.levelsTrack == null ? null : select.levelsTrack.trackItems;
        if (items == null) return -1;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item != null && item.Pointer == icon.Pointer) return i;
        }
        return -1;
    }
}
