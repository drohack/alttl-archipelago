using System.Globalization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// The level select's section star count as the game writes it: "1/15 (7%)"
/// (LevelsCompletionInfo.CompletionPercentageString, read on the run's track
/// 2026-09-30). The percentage rounds to the nearest whole number, which
/// matches every value seen: 1 of 15 reads 7%, 1 of 17 reads 6%.
/// </summary>
public static class CompletionText
{
    public static string Of(int lit, int total)
    {
        var percent = total <= 0 ? 0 : (int)Math.Round(100.0 * lit / total, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{lit}/{total} ({percent}%)");
    }
}
