using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Colouring messages, and not letting other people's text become markup.
///
/// The strings painted here are not ours: item names come from other games'
/// apworlds, and the sender is a slot name or an alias the other player chose
/// and can change mid-game with /alias.
/// </summary>
public class ApPaletteTests
{
    [Fact]
    public void OrdinaryTextIsWrappedInAColourTag()
    {
        Assert.Equal(
            "<color=#FFFFFF>Spoons</color>",
            ApPalette.Paint("Spoons", "FFFFFF"));
    }

    [Fact]
    public void APlayerCannotOpenATagInsideYourToast()
    {
        // The case that motivated this: an alias of "<size=400%>" resized
        // someone else's notifications, because the name goes into a
        // TextMeshPro label and TMP parses rich text.
        var painted = ApPalette.Paint("<size=400%>", "FFFFFF");

        // The tag text survives - it is the player's name and they should see
        // it - but it sits inside noparse, so TMP renders it instead of obeying
        // it. "Does not contain <size" would be the wrong assertion: it would
        // also pass if the name had been silently mangled.
        Assert.Equal("<color=#FFFFFF><noparse><size=400%></noparse></color>", painted);
        Assert.StartsWith("<color=#FFFFFF><noparse>", painted);
    }

    [Fact]
    public void APlayerCannotCloseOurColourTagEarly()
    {
        // The other half: escaping out of the wrapper to recolour the rest of
        // the line, or leave a tag open that bleeds into later toasts.
        var painted = ApPalette.Paint("</color><color=#FF0000>bad", "FFFFFF");

        // Only the opening brackets are escaped, which is enough: with no "<"
        // left there is nothing for TMP to read as a tag, and the stray ">"
        // characters are just text.
        Assert.Equal(
            "<color=#FFFFFF><noparse></color><color=#FF0000>bad</noparse></color>",
            painted);
    }

    [Fact]
    public void ANameKeepsItsCharactersRatherThanLosingThem()
    {
        // Escaped, not stripped. "<3" is a plausible thing to have in a name
        // and must still READ as "<3" on screen, which is why noparse is used
        // rather than a numeric reference TMP does not decode.
        Assert.Equal("<noparse><3</noparse>", ApPalette.Escape("<3"));
    }

    [Fact]
    public void ContentCannotEscapeTheNoparseWrapper()
    {
        // The one way noparse can be broken: content carrying its own closing
        // tag. Removed before wrapping, so what is left cannot get out.
        Assert.Equal(
            "<noparse><b>bold</noparse>",
            ApPalette.Escape("<b>bold</noparse>"));
    }

    [Fact]
    public void TextWithNoTagIsUntouched()
    {
        // No wrapper at all in the common case.
        Assert.Equal("Spoons", ApPalette.Escape("Spoons"));
    }

    [Fact]
    public void AClosingBracketAloneIsLeftAlone()
    {
        // Only the opening bracket starts a tag. Escaping the closing one too
        // would mangle ordinary text like "-> next" for no benefit.
        Assert.Equal("a > b", ApPalette.Escape("a > b"));
    }

    [Fact]
    public void EmptyAndNullAreSafe()
    {
        Assert.Equal("", ApPalette.Escape(""));
        Assert.Equal("", ApPalette.Escape(null!));
    }
}
