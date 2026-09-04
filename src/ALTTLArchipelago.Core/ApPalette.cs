namespace ALTTLArchipelago.Core;

/// <summary>
/// The Archipelago text client's colours, so a message in game reads the same
/// way as the same message in the client next to it.
///
/// The hex values and the rules for applying them are copied from
/// Archipelago's NetUtils.JSONtoTextParser - the colour table verbatim, and the
/// item rule from _handle_item_name. Two things there are easy to get wrong
/// from memory: a location is GREEN, not blue, and an item with no flags is
/// cyan rather than white.
///
/// Anyone who plays multiworlds reads these colours as meaning, so getting them
/// approximately right would be worse than not colouring at all: slateblue
/// where plum belongs says "useful" about something that is progression.
/// </summary>
public static class ApPalette
{
    public const string Black = "000000";
    public const string Red = "EE0000";
    public const string Green = "00FF7F";
    public const string Yellow = "FAFAD2";
    public const string Blue = "6495ED";
    public const string Magenta = "EE00EE";
    public const string Cyan = "00EEEE";
    public const string SlateBlue = "6D8BE8";
    public const string Plum = "AF99EF";
    public const string Salmon = "FA8072";
    public const string White = "FFFFFF";
    public const string Orange = "FF7700";

    /// <summary>Archipelago's item classification bits.</summary>
    [Flags]
    public enum ItemFlags
    {
        None = 0,
        Progression = 0b001,
        Useful = 0b010,
        Trap = 0b100,
    }

    /// <summary>
    /// The colour the text client would give this item.
    ///
    /// Order matters and is not arbitrary: an item that is both progression and
    /// useful shows as progression, because the client tests advancement first.
    /// </summary>
    public static string ForItem(ItemFlags flags)
    {
        if (flags == ItemFlags.None) return Cyan;
        if ((flags & ItemFlags.Progression) != 0) return Plum;
        if ((flags & ItemFlags.Useful) != 0) return SlateBlue;
        if ((flags & ItemFlags.Trap) != 0) return Salmon;
        return Cyan;
    }

    /// <summary>A location name.</summary>
    public static string ForLocation() => Green;

    /// <summary>A player name: magenta for you, yellow for anyone else.</summary>
    public static string ForPlayer(bool isSelf) => isSelf ? Magenta : Yellow;

    /// <summary>Wrap text in a TextMeshPro colour tag.</summary>
    public static string Paint(string text, string hex) => $"<color=#{hex}>{text}</color>";
}
