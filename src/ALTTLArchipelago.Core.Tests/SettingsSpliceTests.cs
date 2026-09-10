using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Splicing one object into a JSON document without disturbing the rest.
///
/// This edits the player's campaign save, so the tests care as much about
/// what it REFUSES to do as about what it does. A wrong span here does not
/// produce a wrong setting, it produces a truncated save.
/// </summary>
public class SettingsSpliceTests
{
    private const string Prefs = "playerPrefs";

    [Fact]
    public void ReplacesTheNamedObject()
    {
        var before = "{\"guid\":1,\"playerPrefs\":{\"resolution\":0},\"version\":\"3.6.1\"}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"resolution\":19}");

        Assert.Equal(
            "{\"guid\":1,\"playerPrefs\":{\"resolution\":19},\"version\":\"3.6.1\"}",
            after);
    }

    [Fact]
    public void EveryOtherByteSurvivesUntouched()
    {
        // The whole point. A number written as 1.0 must still read 1.0, and
        // key order must not move - a re-serialise would not promise either.
        var before = "{\"a\":1.0,\"b\":[1,2,3],\"playerPrefs\":{\"x\":1},"
                     + "\"z\":{\"nested\":{\"deep\":true}},\"trailing\":0.50}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"x\":2}")!;

        Assert.Contains("\"a\":1.0", after);
        Assert.Contains("\"trailing\":0.50", after);
        Assert.Contains("\"z\":{\"nested\":{\"deep\":true}}", after);
        Assert.Equal(before.Replace("{\"x\":1}", "{\"x\":2}"), after);
    }

    [Fact]
    public void NestedBracesInsideTheSettingsObjectAreCounted()
    {
        var before = "{\"playerPrefs\":{\"a\":{\"b\":{\"c\":1}},\"d\":2},\"after\":9}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"new\":1}");

        Assert.Equal("{\"playerPrefs\":{\"new\":1},\"after\":9}", after);
    }

    [Fact]
    public void ABraceInsideAStringDoesNotEndTheObject()
    {
        // The failure this would cause is not a wrong value, it is a save cut
        // in half at the brace.
        var before = "{\"playerPrefs\":{\"label\":\"a } brace\",\"n\":1},\"after\":9}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"n\":2}");

        Assert.Equal("{\"playerPrefs\":{\"n\":2},\"after\":9}", after);
    }

    [Fact]
    public void AnEscapedQuoteDoesNotEndTheString()
    {
        var before = "{\"playerPrefs\":{\"label\":\"say \\\" } now\",\"n\":1},\"z\":9}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"n\":2}");

        Assert.Equal("{\"playerPrefs\":{\"n\":2},\"z\":9}", after);
    }

    [Fact]
    public void WhitespaceAroundTheColonIsAllowed()
    {
        var before = "{ \"playerPrefs\" : { \"n\" : 1 }, \"z\" : 9 }";
        var after = SettingsSplice.Replace(before, Prefs, "{\"n\":2}");

        Assert.Equal("{ \"playerPrefs\" : {\"n\":2}, \"z\" : 9 }", after);
    }

    [Fact]
    public void TheSameTextInsideAStringValueIsNotMistakenForTheKey()
    {
        var before = "{\"note\":\"playerPrefs are here\",\"playerPrefs\":{\"n\":1}}";
        var after = SettingsSplice.Replace(before, Prefs, "{\"n\":2}");

        Assert.Equal(
            "{\"note\":\"playerPrefs are here\",\"playerPrefs\":{\"n\":2}}", after);
    }

    [Fact]
    public void AMissingKeyRefuses()
    {
        Assert.Null(SettingsSplice.Replace("{\"a\":1}", Prefs, "{\"n\":2}"));
    }

    [Fact]
    public void TwoOccurrencesRefuseRatherThanPickOne()
    {
        // Not valid JSON, but a corrupted or hand-edited save can look like
        // this and guessing which one to overwrite is not the mod's call.
        var before = "{\"playerPrefs\":{\"n\":1},\"x\":{\"playerPrefs\":{\"n\":3}}}";

        Assert.Null(SettingsSplice.Replace(before, Prefs, "{\"n\":2}"));
    }

    [Fact]
    public void AValueThatIsNotAnObjectRefuses()
    {
        Assert.Null(SettingsSplice.Replace("{\"playerPrefs\":null}", Prefs, "{}"));
        Assert.Null(SettingsSplice.Replace("{\"playerPrefs\":7}", Prefs, "{}"));
    }

    [Fact]
    public void AnUnclosedObjectRefuses()
    {
        Assert.Null(SettingsSplice.Replace(
            "{\"playerPrefs\":{\"n\":1", Prefs, "{\"n\":2}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    public void RubbishRefuses(string document)
    {
        Assert.Null(SettingsSplice.Replace(document, Prefs, "{\"n\":2}"));
    }

    [Fact]
    public void TheRealSaveShapeSplicesCleanly()
    {
        // The actual key order and neighbours from save1.json, so a change to
        // the game's save shape shows up here rather than in someone's file.
        var before = "{\"guid\":980171684,\"version\":\"3.6.1\","
                     + "\"dailyTidyProgress\":{\"CompleteCount\":0},"
                     + "\"playerPrefs\":{\"musicVolume\":0,\"resolution\":0,"
                     + "\"fullscreen\":false},"
                     + "\"levelCompletionData\":[{\"levelIndex\":0}]}";

        var after = SettingsSplice.Replace(
            before, Prefs,
            "{\"musicVolume\":0,\"resolution\":19,\"fullscreen\":false}")!;

        Assert.Contains("\"resolution\":19", after);
        Assert.Contains("\"levelCompletionData\":[{\"levelIndex\":0}]", after);
        Assert.Contains("\"guid\":980171684", after);
        Assert.DoesNotContain("\"resolution\":0", after);
    }
}
