using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;

namespace ALTTLArchipelago;

/// <summary>
/// The run's star count under each section title of the level select:
/// "- 1/15 (7%)".
///
/// The game writes it in LevelSelect.SetMenuDetails from the section's
/// LevelsCompletionInfo, counted from the levels' save rows - the source the
/// cards' art, the overview strip and the complete screen's stars were taken
/// off one by one in 0.4.0 to 0.4.4. Generators record no solution in the
/// save, and a location the server sends or a Skip releases never reaches it,
/// so a pack of finished generators read "0/15 (0%)" with every card starred
/// (2026-09-29), beside a Collect Stars goal counting the run's own.
///
/// Rewritten here from the run: each slot's hover stars (Core
/// CheckRouter.StarsOf), in the game's format (CompletionText). After every
/// SetMenuDetails (a section change), and on the badges' refresh, so a check
/// collected while the track is up shows at once.
/// </summary>
[HarmonyPatch]
internal static class SectionStars
{
    /// <summary>The count's label, under the section title (DevTools uitree, 2026-09-30).</summary>
    private const string LabelPath = "Header/Details Layout/Completion";

    [HarmonyPatch(typeof(LevelSelect), nameof(LevelSelect.SetMenuDetails))]
    [HarmonyPostfix]
    private static void AfterSetMenuDetails(LevelSelect __instance)
    {
        try
        {
            Apply(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"section stars: {e.Message}");
        }
    }

    internal static void Apply(LevelSelect? select)
    {
        var router = Checks.Router;
        if (router == null || select == null || !Track.IsCampaignSelect(select)) return;

        var section = select.ActiveSection;
        if (section == null) return;

        var label = select.transform.Find(LabelPath)?.GetComponent<TextMeshProUGUI>();
        if (label == null) return;

        // Card positions, not slots: the section's first card and its count
        // (Track.AfterSetupSections builds them), dividers and the credits
        // card counting as no slot.
        var slots = new List<int>();
        var start = section.TrackStartIndex;
        var count = section.SectionLevels == null ? 0 : section.SectionLevels.Count;
        for (int position = start; position < start + count; position++)
        {
            var slot = Track.SlotAt(position);
            if (slot >= 0) slots.Add(slot);
        }

        var (lit, total) = router.StarsOf(slots, Checks.Ledger.IsCollected);
        var text = ALTTLArchipelago.Core.CompletionText.Of(lit, total);
        if (label.text != text) label.text = text;
    }
}
