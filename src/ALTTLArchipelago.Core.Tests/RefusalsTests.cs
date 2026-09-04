using ALTTLArchipelago.Core;
using Archipelago.MultiClient.Net.Enums;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Which login refusals are worth retrying. Getting this wrong either strands
/// the player offline or spends nine seconds backing off from a typo.
/// </summary>
public class RefusalsTests
{
    [Theory]
    [InlineData(ConnectionRefusedError.InvalidSlot)]
    [InlineData(ConnectionRefusedError.InvalidGame)]
    [InlineData(ConnectionRefusedError.InvalidPassword)]
    [InlineData(ConnectionRefusedError.IncompatibleVersion)]
    [InlineData(ConnectionRefusedError.InvalidItemsHandling)]
    public void SomethingOnlyThePlayerCanFixIsNotRetried(ConnectionRefusedError code)
    {
        Assert.True(Refusals.IsTerminal(new[] { code }));
    }

    [Fact]
    public void SlotAlreadyTakenIsRetried()
    {
        // The usual cause is an earlier socket of ours that has not timed out.
        // Waiting is exactly what helps.
        Assert.False(Refusals.IsTerminal(
            new[] { ConnectionRefusedError.SlotAlreadyTaken }));
    }

    [Fact]
    public void ARefusalTheLibraryDoesNotRecogniseIsRetried()
    {
        // UnknownError means the server sent a code this library has no name
        // for. That is a reason to be cautious, not a reason to conclude the
        // player must fix something. Retrying an unknown refusal costs seconds;
        // refusing to retry a transient one strands them offline.
        Assert.False(Refusals.IsTerminal(
            new[] { ConnectionRefusedError.UnknownError }));
    }

    [Fact]
    public void OneTerminalCodeAmongTransientOnesIsStillTerminal()
    {
        Assert.True(Refusals.IsTerminal(new[]
        {
            ConnectionRefusedError.SlotAlreadyTaken,
            ConnectionRefusedError.InvalidPassword,
        }));
    }

    [Fact]
    public void NoCodesAtAllIsRetried()
    {
        // A refusal with no codes carries no claim about why. It was arriving
        // as null in practice, which is how the distinction got discarded in
        // the first place.
        Assert.False(Refusals.IsTerminal(null));
        Assert.False(Refusals.IsTerminal(Array.Empty<ConnectionRefusedError>()));
    }
}
