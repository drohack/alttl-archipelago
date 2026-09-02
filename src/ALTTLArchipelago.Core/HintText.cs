using System.Globalization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// The per-seed "where is it" text attached to each location.
///
/// Location names have to be identical across every seed, so they carry
/// content and cannot carry position. Archipelago's own answer to exactly this
/// problem is World.extend_hint_information, a per-seed
/// {player: {location_id: text}} map whose value the server renders as the
/// entrance of a hint:
///
///     [Hint]: droha's Swapping is at Medicine Cabinet - Blue Bottles
///             in droha's World at Ch.2 Level 3.
///
/// So the static name says WHAT and this says WHERE, and a hint carries both.
/// Abbreviated because it sits inside an already long line.
/// </summary>
public static class HintText
{
    /// <summary>"Ch.2 Level 3" for a 0-based slot index.</summary>
    public static string ForSlot(int slotIndex)
        => string.Format(CultureInfo.InvariantCulture, "Ch.{0} Level {1}",
            Chapters.ChapterOf(slotIndex), Chapters.PositionInChapter(slotIndex));

    /// <summary>Where the credits card sits: always the end of the track.</summary>
    public const string CreditsLocation = "the end of the track";
}
