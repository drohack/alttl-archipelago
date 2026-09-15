using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The one controller type that never carries a location.
///
/// Small, but it is the difference between a permanent false alarm and a
/// gate that passes - see ControllerTypes for the measurement behind it.
/// </summary>
public class ControllerTypesTests
{
    [Fact]
    public void PannablesNeverScores()
    {
        Assert.False(ControllerTypes.Scores("Pannables"));
    }

    [Theory]
    [InlineData("Draggables")]
    [InlineData("DraggablesJigsaw")]
    [InlineData("Containables")]
    [InlineData("TupperwareTower")]
    [InlineData("ComputerErrorsController")]
    public void EveryOtherMeasuredTypeScores(string type)
    {
        Assert.True(ControllerTypes.Scores(type));
    }

    [Fact]
    public void AnUnknownTypeScores()
    {
        // An unrecognised controller must still raise the mismatch warning.
        // Treating the unknown as unscored would silence the one signal that
        // says the table and the game have drifted apart.
        Assert.True(ControllerTypes.Scores("SomethingAddedInAPatch"));
        Assert.True(ControllerTypes.Scores(""));
        Assert.True(ControllerTypes.Scores(null));
    }
}
