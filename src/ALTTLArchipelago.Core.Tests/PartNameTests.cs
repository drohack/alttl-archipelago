using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Part names end up in Archipelago location names, so like display names they
/// are frozen once shipped. A failure here means seeds break.
/// </summary>
public class PartNameTests
{
    private static ControllerInfo C(string name, string type = "Draggables")
        => new() { Name = name, Type = type };

    [Theory]
    [InlineData("Blue Bottles Draggables", "Blue Bottles")]
    [InlineData("Swabs Containables", "Swabs")]
    [InlineData("Contact Lenses Draggables", "Contact Lenses")]
    [InlineData("Chalk DraggablesOrdered", "Chalk")]
    [InlineData("ChalkMint Jigsaw", "Chalk Mint")]
    [InlineData("Containables - Keys", "Keys")]
    [InlineData("CandleStateController", "Candle State")]
    [InlineData("Creams Draggables Stacked", "Creams Stacked")]
    // Already clean, must be untouched.
    [InlineData("Match Reindeer", "Match Reindeer")]
    [InlineData("Interlocking", "Interlocking")]
    [InlineData("Crumbs", "Crumbs")]
    public void TidyingIsStable(string raw, string expected)
    {
        Assert.Equal(expected, PartNames.Tidy(raw));
    }

    [Fact]
    public void ANameMadeEntirelyOfTypeWordsSurvives()
    {
        // A controller literally called "Draggables" is still the thing the
        // player solves; reducing it to "" would produce a nameless location.
        Assert.Equal("Draggables", PartNames.Tidy("Draggables"));
        Assert.Equal("Controller", PartNames.Tidy("Controller"));
    }

    [Fact]
    public void CollidingNamesKeepTheirRawForm()
    {
        // Record Player really does have two controllers that both tidy to the
        // same thing. Two locations sharing a name would merge two checks.
        var level = new LevelInfo
        {
            LevelId = "Record Player",
            SolutionCount = 1,
            Controllers = new List<ControllerInfo>
            {
                C("Needle & Knobs"),
                C("Needle & Knobs Draggables"),
                C("Something Else Draggables"),
            },
        };

        var names = PartNames.ForLevel(level);

        Assert.Equal("Needle & Knobs", names["Needle & Knobs"]);
        Assert.Equal("Needle & Knobs Draggables", names["Needle & Knobs Draggables"]);
        // The non-colliding one still gets tidied.
        Assert.Equal("Something Else", names["Something Else Draggables"]);
    }

    [Fact]
    public void EveryLevelInTheTableHasUniquePartNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "levels.json");
        var table = LevelTable.FromJson(File.ReadAllText(path));

        foreach (var level in table.Levels)
        {
            var groups = ControllerGroups.For(level);
            var display = groups.Select(g => g.DisplayName).ToList();
            var dupes = display.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key);
            Assert.True(!dupes.Any(),
                $"{level.LevelId} has duplicate part names: {string.Join(", ", dupes)}");
            Assert.All(display, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        }
    }

    [Fact]
    public void HintTextIsShortAndSaysWhere()
    {
        Assert.Equal("Ch.1 Level 1", HintText.ForSlot(0));
        Assert.Equal("Ch.2 Level 3", HintText.ForSlot(22));
        Assert.Equal("Ch.5 Level 12", HintText.ForSlot(78));
    }

    [Fact]
    public void AWholeHintLineReadsWell()
    {
        // What the server will actually print, end to end.
        var level = new LevelInfo
        {
            LevelId = "MedicineCabinet",
            SolutionCount = 1,
            Controllers = new List<ControllerInfo>
            {
                C("Blue Bottles Draggables", "DraggablesOrdered"),
                C("Swabs Containables", "Containables"),
            },
        };
        var group = ControllerGroups.For(level).Single(g => g.Name == "Blue Bottles Draggables");
        var location = LocationNames.Part(level.LevelId, 1, group.DisplayName);

        Assert.Equal("Medicine Cabinet - Blue Bottles", location);
        Assert.Equal("Ch.2 Level 3", HintText.ForSlot(22));
    }

    [Fact]
    public void StrippingATypeWordDoesNotLeaveEmptyBracketsBehind()
    {
        // Some controllers wear the type inside brackets. Removing the word
        // without the brackets shipped five location names reading like
        // "Books 3 - Height ()".
        Assert.Equal("Height", PartNames.Tidy("Height (Draggables)"));
        Assert.Equal("Match Label", PartNames.Tidy("Match Label (Draggables)"));
    }

    [Fact]
    public void BracketsThatStillHoldSomethingAreKept()
    {
        // Only the EMPTIED brackets go. "(Shuffle)" is not a type word, so it
        // survives and stays part of the name.
        Assert.Equal("Design (Shuffle)", PartNames.Tidy("Design (Shuffle)"));
    }
}
