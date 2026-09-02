namespace ALTTLArchipelago.Core;

/// <summary>
/// The fixed chapter layout of the run.
///
/// Sizes are vanilla's - 20 / 16 / 16 / 15 / 12, which is exactly 79 - and
/// they do NOT rescale when puzzle_count is lowered. A shorter run simply
/// truncates the track, so it ends part-way through a chapter.
///
/// That fixedness is not a style choice. Archipelago's location_name_to_id is
/// a ClassVar baked into the datapackage with a checksum: every location name
/// must be identical for every seed and every yaml. If chapter boundaries
/// moved with puzzle_count, a location called "Chapter 2 - Level 3" would
/// refer to different positions in different runs, which the datapackage
/// cannot express.
/// </summary>
public static class Chapters
{
    /// <summary>Puzzle slots per chapter, in order. Sums to 79.</summary>
    public static readonly IReadOnlyList<int> Sizes = new[] { 20, 16, 16, 15, 12 };

    /// <summary>The vanilla names, kept because the game still draws them.</summary>
    public static readonly IReadOnlyList<string> Titles = new[]
    {
        "Home Sweet Home",
        "Lost Recipe",
        "Nitty Gritty",
        "Inner Nature",
        "Near Earth Organizer",
    };

    public static int TotalSlots => Sizes.Sum();

    /// <summary>1-based chapter number for a 0-based slot index.</summary>
    public static int ChapterOf(int slotIndex)
    {
        if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        int seen = 0;
        for (int c = 0; c < Sizes.Count; c++)
        {
            seen += Sizes[c];
            if (slotIndex < seen) return c + 1;
        }
        throw new ArgumentOutOfRangeException(nameof(slotIndex),
            $"slot {slotIndex} is past the last chapter ({TotalSlots} slots)");
    }

    /// <summary>1-based position within its chapter, for a 0-based slot index.</summary>
    public static int PositionInChapter(int slotIndex)
    {
        int seen = 0;
        for (int c = 0; c < Sizes.Count; c++)
        {
            if (slotIndex < seen + Sizes[c]) return slotIndex - seen + 1;
            seen += Sizes[c];
        }
        throw new ArgumentOutOfRangeException(nameof(slotIndex));
    }
}
