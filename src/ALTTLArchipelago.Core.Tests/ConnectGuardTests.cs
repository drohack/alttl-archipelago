using ALTTLArchipelago.Core;
using Xunit;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The decision a login still has to pass, which until now had no test.
///
/// It could not have one: the sequence lived inline in Connection.Connect,
/// between a socket handshake and an event wiring, and reaching it needs a
/// live ArchipelagoSession - a concrete class whose nine helper properties
/// would all have to be stubbed to be non-null before a single assertion
/// could run.
///
/// That is not a footnote about testing convenience. BOTH halves of this
/// sequence were added in separate releases for the same reason each time: the
/// guard existed, and was skipped on exactly the path where it was
/// authoritative. 0.3.3 added the version pair check; the same release found
/// that Problems() was enforced on the offline start and the cache write and
/// merely logged on the live connect, which is the one that builds the run.
/// </summary>
public class ConnectGuardTests
{
    /// <summary>
    /// A payload with nothing wrong with it - the REAL one the generator
    /// produces, not a hand-built stand-in.
    ///
    /// A synthetic "playable" payload is the wrong fixture here: it proves
    /// only that the fixture satisfies Problems(), and getting it wrong reads
    /// as the guard misbehaving. This loads the golden seed the apworld's own
    /// tests write, so "accepted" means a seed a player could actually be
    /// handed. Each call reparses, so a test may mutate its copy freely.
    /// </summary>
    // ---- DLC ------------------------------------------------------------

    [Fact]
    public void ASeedNeedingNoDlcConnectsOnAMachineWithNone()
    {
        var verdict = ConnectGuard.Evaluate(Playable(), "0.3.3",
                                            Array.Empty<string>());
        Assert.Null(verdict.Refusal);
    }

    [Fact]
    public void ASeedNeedingADlcThePlayerLacksIsRefusedByName()
    {
        var slot = Playable();
        slot.SeeingStars = true;

        var verdict = ConnectGuard.Evaluate(slot, "0.3.3", new[] { "DLC1" });

        Assert.NotNull(verdict.Refusal);
        // The name a player would recognise from their store page, not DLC2.
        Assert.Contains("Seeing Stars", verdict.Refusal);
    }

    [Fact]
    public void BothMissingDlcsAreNamed()
    {
        var slot = Playable();
        slot.CupboardsAndDrawers = true;
        slot.SeeingStars = true;

        var verdict = ConnectGuard.Evaluate(slot, "0.3.3", Array.Empty<string>());

        Assert.NotNull(verdict.Refusal);
        Assert.Contains("Cupboards and Drawers", verdict.Refusal);
        Assert.Contains("Seeing Stars", verdict.Refusal);
    }

    [Fact]
    public void OwningTheDlcTheSeedNeedsConnects()
    {
        var slot = Playable();
        slot.CupboardsAndDrawers = true;

        var verdict = ConnectGuard.Evaluate(slot, "0.3.3",
                                            new[] { "DLC1", "DLC2" });

        Assert.Null(verdict.Refusal);
    }

    /// <summary>
    /// A caller that cannot read the game's DLC list must not turn that into a
    /// refused connection - the failure it would cause is worse than the one
    /// it guards against.
    /// </summary>
    [Fact]
    public void AnUnknownDlcListSkipsTheCheckRatherThanRefusing()
    {
        var slot = Playable();
        slot.CupboardsAndDrawers = true;
        slot.SeeingStars = true;

        Assert.Null(ConnectGuard.Evaluate(slot, "0.3.3", null).Refusal);
        // And the two-argument overload behaves the same way, so no existing
        // caller starts refusing seeds because this field was added.
        Assert.Null(ConnectGuard.Evaluate(slot, "0.3.3").Refusal);
    }

    /// <summary>
    /// The flags decide, not the draw.
    ///
    /// A seed generated with a DLC enabled can draw none of its levels by
    /// chance. It is still a seed built for someone who owns that DLC, and
    /// deciding from the slots instead would let the same yaml connect on one
    /// machine and not another depending on the roll.
    /// </summary>
    [Fact]
    public void TheFlagsDecideEvenWhenNoSlotDrewADlcLevel()
    {
        var slot = Playable();
        slot.SeeingStars = true;
        Assert.DoesNotContain(slot.Slots, s => s.Dlc == "DLC2");

        Assert.NotNull(ConnectGuard.Evaluate(slot, "0.3.3",
                                             Array.Empty<string>()).Refusal);
    }

    private static SlotData Playable(string worldVersion = "0.3.3")
    {
        var slot = ExampleSeed.Load();
        slot.WorldVersion = worldVersion;
        return slot;
    }

    /// <summary>The same payload, made incoherent in one specific way.</summary>
    private static SlotData WithNoPacks(string worldVersion = "0.3.3")
    {
        var slot = Playable(worldVersion);
        slot.PackSize = 0;
        slot.PackBoundaries = new List<int>();
        return slot;
    }

    [Fact]
    public void AMatchingPairIsPlayedWithNothingToSay()
    {
        var verdict = ConnectGuard.Evaluate(Playable("0.3.3"), "0.3.3");

        Assert.True(verdict.Accepted);
        Assert.Null(verdict.Refusal);
        Assert.Null(verdict.Warning);
    }

    [Fact]
    public void AMismatchedApworldIsRefused()
    {
        // The whole point: location ids move between releases, so a 0.3.1 mod
        // on a 0.3.2 seed sends the wrong checks under the right names - into
        // other people's worlds, where it cannot be walked back.
        var verdict = ConnectGuard.Evaluate(Playable("0.3.2"), "0.3.1");

        Assert.False(verdict.Accepted);
        Assert.Contains("0.3.2", verdict.Refusal);
        Assert.Contains("0.3.1", verdict.Refusal);
    }

    [Fact]
    public void TheVersionIsCheckedBeforeTheRestOfThePayload()
    {
        // ORDER IS THE CONTRACT. A mismatched apworld can make every other
        // field look wrong, and "install the matching pair" is an answer the
        // player can act on where "pack_size is 0" is not.
        var verdict = ConnectGuard.Evaluate(WithNoPacks("0.9.9"), "0.3.3");

        Assert.False(verdict.Accepted);
        Assert.Contains("0.9.9", verdict.Refusal);
        Assert.DoesNotContain("cannot be played", verdict.Refusal);
    }

    [Fact]
    public void AnIncoherentPayloadIsRefusedRatherThanLogged()
    {
        // THE BUG THIS REPLACED. pack_size 0 with no boundaries leaves
        // OpenSlots at 0 forever - a track that never opens a single card -
        // and the live connect path used to log one warning and start the run
        // anyway.
        var verdict = ConnectGuard.Evaluate(WithNoPacks(), "0.3.3");

        Assert.False(verdict.Accepted);
        Assert.Contains("this seed cannot be played", verdict.Refusal);
    }

    [Fact]
    public void ARunWithNoSlotsIsRefused()
    {
        var slot = Playable();
        slot.Slots = new List<SlotEntry>();

        var verdict = ConnectGuard.Evaluate(slot, "0.3.3");

        Assert.False(verdict.Accepted);
        Assert.Contains("this seed cannot be played", verdict.Refusal);
    }

    [Fact]
    public void EverySeparateProblemIsNamed()
    {
        // The player gets the whole list, not the first one, so a badly
        // generated seed is diagnosed in one attempt rather than N.
        var slot = WithNoPacks();
        slot.Slots = new List<SlotEntry>();

        var verdict = ConnectGuard.Evaluate(slot, "0.3.3");

        Assert.False(verdict.Accepted);
        Assert.Contains("; ", verdict.Refusal);
    }

    [Fact]
    public void ASeedTooOldToNameItsVersionIsAllowedWithAWarning()
    {
        // An UNKNOWN, not a mismatch. Refusing would strand runs that are very
        // probably fine - every seed generated before 0.3.3 carries no version
        // field at all.
        var verdict = ConnectGuard.Evaluate(Playable(""), "0.3.3");

        Assert.True(verdict.Accepted);
        Assert.Equal(ConnectGuard.UnknownVersionWarning, verdict.Warning);
    }

    [Fact]
    public void AnUnreadableModVersionDisablesTheCheckRatherThanTheConnection()
    {
        // Empty means the assembly version could not be read, which is a
        // packaging oddity. Refusing every seed over it would be a far worse
        // failure than the one being guarded.
        var verdict = ConnectGuard.Evaluate(Playable("0.3.1"), "");

        Assert.True(verdict.Accepted);
        Assert.Null(verdict.Warning);
    }

    [Fact]
    public void AnOldSeedWithABrokenPayloadIsStillRefused()
    {
        // The version being unknown must not wave the rest of the payload
        // through - the two guards are independent.
        var verdict = ConnectGuard.Evaluate(WithNoPacks(""), "0.3.3");

        Assert.False(verdict.Accepted);
        Assert.Contains("this seed cannot be played", verdict.Refusal);
    }

    [Fact]
    public void TheRefusalIsWrittenForAPlayerNotALog()
    {
        // It goes straight into the connection pane, so it has to name the fix.
        var verdict = ConnectGuard.Evaluate(Playable("0.3.2"), "0.3.1");

        Assert.Contains("install the matching pair",
                        verdict.Refusal!.ToLowerInvariant());
    }
}
