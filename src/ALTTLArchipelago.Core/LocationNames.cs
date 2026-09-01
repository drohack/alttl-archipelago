using System.Globalization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Location naming, shared by the apworld and the mod.
///
/// Names are built from the SLOT, not from the level alone, because the same
/// generator can occupy several slots in one run with different seeds - and
/// two instances of "Books (Randomized)" are genuinely different puzzles that
/// need distinct checks.
///
/// Solutions are numbered ORDINALLY: the Nth distinct solution a player finds
/// on a level is that level's Solution N. That is deliberate. The game's own
/// internal solution id turned out to be opaque - MedicineCabinet, whose 13
/// controllers include none called "Draggables", reports "Draggables_0" - so
/// nothing may be built on the string. The game already dedupes distinct
/// solutions into its save, so the ordinal is well defined and order
/// independent from Archipelago's point of view.
/// </summary>
public static class LocationNames
{
    /// <summary>Slot numbers are 1-based and zero-padded so names sort naturally.</summary>
    public static string SlotPrefix(int slotIndex, string levelId)
        => string.Format(CultureInfo.InvariantCulture, "Slot {0:00} - {1}", slotIndex + 1, levelId);

    public static string Solution(int slotIndex, string levelId, int solutionNumber)
        => string.Format(CultureInfo.InvariantCulture, "{0} - Solution {1}",
            SlotPrefix(slotIndex, levelId), solutionNumber);

    public static string Controller(int slotIndex, string levelId, string groupName)
        => $"{SlotPrefix(slotIndex, levelId)} - {groupName}";

    /// <summary>
    /// The event location that grants one "Level Beaten" token. These are
    /// zero-cost event locations rather than pool items, so the "beat N
    /// levels" gate costs no item slots and still forces the fill to spread
    /// progression across the run.
    /// </summary>
    public static string Beaten(int slotIndex, string levelId)
        => $"{SlotPrefix(slotIndex, levelId)} - Beaten";

    /// <summary>
    /// Every location a slot contributes.
    ///
    /// One per distinct solution, always. Plus one per controller group, but
    /// ONLY when the level has more than one group - on a single-group level
    /// the controller check and the first solution check would be the same
    /// event, and minting both would double count.
    /// </summary>
    public static IReadOnlyList<string> ForSlot(int slotIndex, LevelInfo level)
    {
        var names = new List<string>();

        for (int n = 1; n <= level.SolutionCount; n++)
        {
            names.Add(Solution(slotIndex, level.LevelId, n));
        }

        var groups = ControllerGroups.For(level);
        if (groups.Count > 1)
        {
            foreach (var g in groups)
            {
                names.Add(Controller(slotIndex, level.LevelId, g.Name));
            }
        }

        return names;
    }
}
