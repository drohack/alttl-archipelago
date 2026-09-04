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

    /// <summary>
    /// Wrap text in a TextMeshPro colour tag.
    ///
    /// The text is escaped first. It is not ours: item names come from other
    /// games' apworlds, and the sender's name is a slot name or an alias the
    /// other PLAYER chose and can change with /alias mid-game. Both land in a
    /// TextMeshPro label, which parses rich text - so a player called
    /// "&#60;size=400%&#62;" resized someone else's notifications.
    /// </summary>
    public static string Paint(string text, string hex)
        => $"<color=#{hex}>{Escape(text)}</color>";

    /// <summary>
    /// Make text safe to put inside a rich-text label.
    ///
    /// Wrapped in &#60;noparse&#62;, which makes TextMeshPro render the span
    /// literally. The obvious breakout - content containing its own closing
    /// noparse tag - is removed first, so there is nothing to escape out of.
    ///
    /// The numeric reference "&amp;#60;" was tried first and rejected after
    /// looking at it: TMP does NOT decode it, so a name showed up on screen as
    /// the raw escape. It was safe and unreadable. Only checking the rendering
    /// caught that.
    ///
    /// Text with no angle bracket is returned untouched, so the overwhelmingly
    /// common case carries no wrapper at all.
    /// </summary>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.IndexOf('<') < 0) return text;

        var safe = System.Text.RegularExpressions.Regex.Replace(
            text, "</noparse>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return $"<noparse>{safe}</noparse>";
    }
}
