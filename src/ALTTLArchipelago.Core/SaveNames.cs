using System.Globalization;
using System.Text;

namespace ALTTLArchipelago.Core;

/// <summary>
/// What an Archipelago run's save file is called.
///
/// The game keeps one save, "save1", and SaveData.levelCompletionData is a
/// flat list with no scoping - only lastPlayedLevels is keyed. So a randomized
/// run recording progress through the normal path would write it straight into
/// the player's campaign. The mod redirects the filename instead, which means
/// the campaign file is never opened at all: isolation by construction rather
/// than by remembering to be careful, and a crash mid-run cannot corrupt
/// something that was never open.
///
/// The name must be:
///
///   STABLE for a given slot and seed, so quitting and rejoining the same
///   multiworld resumes rather than starting over;
///   DISTINCT between seeds, so two multiworlds do not share progress;
///   SAFE as a filename, because a slot name is whatever a player typed into
///   their yaml - it can contain slashes, colons, quotes or nothing at all.
///
/// Pure string handling, so it lives here and is tested without a game.
/// </summary>
public static class SaveNames
{
    /// <summary>The game's own save, which the mod must never write to.</summary>
    public const string Vanilla = "save1";

    /// <summary>Marks our files so they are obvious in the save folder.</summary>
    public const string Prefix = "save_ap_";

    /// <summary>
    /// Room to keep the name readable while staying clear of path limits. The
    /// save folder path is already long on Windows, and the game appends its
    /// own extension plus a ".tmp" for the atomic write.
    /// </summary>
    private const int MaxPartLength = 24;

    /// <summary>
    /// The save name for one slot in one seed.
    ///
    /// The seed goes in because a player can be in two multiworlds with the
    /// same slot name, and sharing a save between them would merge the runs.
    /// </summary>
    public static string ForSession(string? slotName, string? seed)
    {
        var slot = Sanitise(slotName, "player");
        var room = Sanitise(seed, "seed");
        return $"{Prefix}{slot}_{room}";
    }

    /// <summary>Whether a save name is one of ours rather than the game's.</summary>
    public static bool IsArchipelago(string? name)
        => name != null && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Reduce arbitrary text to something safe in a filename on every platform.
    ///
    /// Deliberately strict - letters, digits, dash and underscore only - rather
    /// than filtering a blocklist of illegal characters. A slot name comes from
    /// a player's yaml and may hold anything at all, and the cost of being
    /// wrong is a save that silently fails to write.
    /// </summary>
    private static string Sanitise(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var builder = new StringBuilder(raw!.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) && c < 128)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c == '-' || c == '_')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
            {
                // Anything else - spaces, punctuation, accents, CJK - collapses
                // to a single dash, so "Droha's Room!!" does not become
                // "drohasroom" and collide with a genuinely different name.
                builder.Append('-');
            }
        }

        var cleaned = builder.ToString().Trim('-');
        if (cleaned.Length == 0)
        {
            // Everything was stripped - a name written entirely in a non-Latin
            // script, say. Falling back to a bare "player" would give EVERY
            // such name the same save, silently merging two people's runs, so
            // the hash of the original keeps them apart.
            return $"{fallback}-{ShortHash(raw!)}";
        }

        if (cleaned.Length > MaxPartLength)
        {
            // Truncating alone could make two long names collide, so a short
            // hash of the ORIGINAL is appended to keep distinct inputs
            // distinct.
            var head = cleaned.Substring(0, MaxPartLength - 5).TrimEnd('-');
            cleaned = $"{head}-{ShortHash(raw)}";
        }

        return cleaned;
    }

    /// <summary>
    /// A stable 4-character hash. Not security, just collision avoidance, and
    /// written out rather than using string.GetHashCode() because that is
    /// randomised per process in .NET Core - the name has to be the same on
    /// every launch or a run cannot be resumed.
    /// </summary>
    private static string ShortHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }
            return (hash % 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
        }
    }
}
