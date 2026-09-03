using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The save name decides which file a run writes to, so a bug here is a bug
/// that eats someone's campaign progress or silently merges two multiworlds.
/// Worth more tests than a string function usually gets.
/// </summary>
public class SaveNamesTests
{
    [Fact]
    public void ItIsNeverTheGamesOwnSave()
    {
        // The single most important property in this file.
        string[] hostile = { "1", "save1", "", "   ", "../save1", "save1.json" };
        foreach (var slot in hostile)
        {
            foreach (var seed in hostile)
            {
                var name = SaveNames.ForSession(slot, seed);
                Assert.NotEqual(SaveNames.Vanilla, name);
                Assert.True(SaveNames.IsArchipelago(name), name);
            }
        }
    }

    [Fact]
    public void TheSameSessionAlwaysGetsTheSameName()
    {
        // Otherwise quitting and rejoining a multiworld starts from nothing.
        var first = SaveNames.ForSession("droha", "ABC123");
        var second = SaveNames.ForSession("droha", "ABC123");
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsDoNotShareASave()
    {
        Assert.NotEqual(SaveNames.ForSession("droha", "seed-one"),
                        SaveNames.ForSession("droha", "seed-two"));
    }

    [Fact]
    public void DifferentSlotsDoNotShareASave()
    {
        Assert.NotEqual(SaveNames.ForSession("droha", "seed"),
                        SaveNames.ForSession("someone", "seed"));
    }

    [Theory]
    [InlineData("Droha's Room!!")]
    [InlineData("../../etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("name with spaces")]
    [InlineData("emoji-free but \"quoted\"")]
    [InlineData("nul")]
    [InlineData("a/b\\c:d*e?f")]
    public void AnyNameAPlayerCouldTypeProducesASafeFilename(string slot)
    {
        var name = SaveNames.ForSession(slot, "seed");

        // Letters, digits, dash and underscore only - nothing a filesystem or
        // a path parser can misread.
        Assert.All(name, c =>
            Assert.True(char.IsLetterOrDigit(c) || c == '-' || c == '_',
                $"{name} contains {c}"));
        Assert.DoesNotContain("..", name);
        Assert.True(SaveNames.IsArchipelago(name));
    }

    [Fact]
    public void AnEmptyNameStillProducesSomethingUsable()
    {
        // A blank slot name is a misconfiguration, not a reason to write to a
        // file called "save_ap__".
        var name = SaveNames.ForSession("", "");
        Assert.Equal("save_ap_player_seed", name);
    }

    [Fact]
    public void NamesThatDifferOnlyPastTheTruncationPointStayDistinct()
    {
        // Truncating alone would collide these, and colliding means two
        // multiworlds quietly sharing one save.
        var a = new string('a', 40) + "FIRST";
        var b = new string('a', 40) + "SECOND";
        Assert.NotEqual(SaveNames.ForSession(a, "seed"),
                        SaveNames.ForSession(b, "seed"));
    }

    [Fact]
    public void LongNamesAreKeptShortEnoughForAPath()
    {
        var name = SaveNames.ForSession(new string('x', 200), new string('y', 200));
        Assert.True(name.Length < 64, $"{name.Length} chars is too long for a path");
    }

    [Fact]
    public void NonLatinNamesDoNotCollapseToNothing()
    {
        // Sanitising drops non-ASCII, so two different non-Latin names must
        // still be told apart by the hash rather than both becoming "player".
        var a = SaveNames.ForSession("\u3042\u3044\u3046", "seed");
        var b = SaveNames.ForSession("\u3048\u304a\u304b", "seed");
        Assert.NotEqual(a, b);
        Assert.True(SaveNames.IsArchipelago(a));
        Assert.True(SaveNames.IsArchipelago(b));
    }

    [Fact]
    public void TheVanillaSaveIsNotMistakenForOurs()
    {
        Assert.False(SaveNames.IsArchipelago(SaveNames.Vanilla));
        Assert.False(SaveNames.IsArchipelago(null));
        Assert.False(SaveNames.IsArchipelago(""));
    }
}
