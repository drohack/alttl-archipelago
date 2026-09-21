using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The predicate Checks.Earned withholds a check on, pinned here because
/// Earned itself cannot be tested.
///
/// Earned is private static in ALTTLArchipelago, the assembly that
/// references the game and that CI deliberately never builds. What it
/// does is one call - IsReachable(location, int.MaxValue, abilities) -
/// and that call's semantics ARE the contract, so they get tested even
/// though the caller cannot be.
///
/// Every case below is a decision someone made for a reason. Loosen one
/// and the mod hands out an item the logic says is unreachable; tighten
/// one and it withholds a check a player genuinely earned, which is the
/// worse of the two because a player cannot tell it happened.
/// </summary>
public class EarnedContractTests
{
    private static SlotData Seed()
    {
        var data = new SlotData
        {
            AbilityLocks = true,
            Abilities = new Dictionary<string, List<string>>
            {
                ["Swapping"] = new() { "Shuffleables" },
                ["Distributing"] = new() { "Distributables" },
            },
        };
        data.Slots.Add(new SlotEntry { LevelId = "DLC2 Pizza", LevelIndex = 1210, Instance = 1 });

        // Needs an ability and no packs.
        data.Requirements["DLC2 Pizza - Toppings"] =
            new Requirement { Packs = 0, Abilities = new List<string> { "Distributing" } };
        // Needs packs and an ability - the case the two predicates differ on.
        data.Requirements["DLC2 Pizza - Beaten"] =
            new Requirement { Packs = 3, Abilities = new List<string> { "Distributing" } };
        // Needs nothing at all.
        data.Requirements["DLC2 Pizza - Solution 1"] =
            new Requirement { Packs = 0, Abilities = new List<string>() };
        return data;
    }

    private static (SlotProgress, AbilityState) Build(params string[] held)
    {
        var data = Seed();
        var abilities = new AbilityState(data);
        abilities.SetHeld(held);
        return (new SlotProgress(data, new CheckRouter(data)), abilities);
    }

    /// <summary>
    /// What Earned passes for packs. Not a magic number where it is used:
    /// it neutralises the pack half of the predicate on purpose.
    /// </summary>
    private const int EarnedPacks = int.MaxValue;

    [Fact]
    public void TheGateWithholdsALocationWhoseAbilityIsMissing()
    {
        var (progress, abilities) = Build();
        Assert.False(progress.IsReachable("DLC2 Pizza - Toppings", EarnedPacks, abilities));
    }

    [Fact]
    public void TheGateFilesItOnceTheAbilityArrives()
    {
        var (progress, abilities) = Build("Distributing");
        Assert.True(progress.IsReachable("DLC2 Pizza - Toppings", EarnedPacks, abilities));
    }

    [Fact]
    public void TheGateIgnoresPacksOnPurpose()
    {
        // Beaten wants 3 packs. Earned passes int.MaxValue so the pack half
        // cannot fire, because the real reading was Track.State?.PacksHeld ?? 0
        // and that FAILS CLOSED before the track is built - which cost a gate
        // run the Paper Plane Supplies draggables check. Packs are enforced by
        // the track deciding which cards open, not by check filing.
        var (progress, abilities) = Build("Distributing");
        Assert.True(progress.IsReachable("DLC2 Pizza - Beaten", EarnedPacks, abilities));
    }

    [Fact]
    public void TheGateFilesALocationTheSeedDoesNotHave()
    {
        // Fails OPEN on an unknown name. A location this seed never minted is
        // not "unreachable", it is not a location, and treating it as blocked
        // would withhold a check forever.
        var (progress, abilities) = Build();
        Assert.True(progress.IsReachable("Some Level - Invented", EarnedPacks, abilities));
    }

    [Fact]
    public void TheGateIsOpenWhenLocksAreOff()
    {
        var data = Seed();
        data.AbilityLocks = false;
        var abilities = new AbilityState(data);
        abilities.SetHeld(Array.Empty<string>());
        var progress = new SlotProgress(data, new CheckRouter(data));

        Assert.True(progress.IsReachable("DLC2 Pizza - Toppings", EarnedPacks, abilities));
    }

    [Fact]
    public void ALocationNeedingNothingIsAlwaysEarned()
    {
        var (progress, abilities) = Build();
        Assert.True(progress.IsReachable("DLC2 Pizza - Solution 1", EarnedPacks, abilities));
    }

    /// <summary>
    /// THE ASYMMETRY, pinned rather than removed.
    ///
    /// Earned withholds using int.MaxValue packs, but the two paths that
    /// re-offer a withheld check - SweepAlreadySolved and
    /// FileSolutionsAlreadyEarned - pass the real Track.State?.PacksHeld ?? 0.
    /// The recovery is therefore STRICTER than the gate that withholds, and
    /// the obvious worry is a check that is withheld and then never re-offered.
    ///
    /// Read 2026-09-20 and it cannot strand one: Checks.EnterSlot sets
    /// _auditedCount = 0, so re-entering the card re-runs the sweep from
    /// scratch, and a player cannot be standing in a slot whose pack has not
    /// opened. The divergence is a one-visit delay in the window before the
    /// track exists, not a lost check.
    ///
    /// Left as it is deliberately. Making the recovery use int.MaxValue too
    /// would file pack-gated checks from a state the logic says cannot reach
    /// them, which is the direction that actually breaks a multiworld.
    /// </summary>
    [Fact]
    public void TheRecoveryPathIsStricterThanTheGateBeforeTheTrackExists()
    {
        var (progress, abilities) = Build("Distributing");
        const string beaten = "DLC2 Pizza - Beaten";

        Assert.True(progress.IsReachable(beaten, EarnedPacks, abilities));
        Assert.False(progress.IsReachable(beaten, 0, abilities));
    }

    [Fact]
    public void AndTheyAgreeOnceThePacksAreHeld()
    {
        var (progress, abilities) = Build("Distributing");
        const string beaten = "DLC2 Pizza - Beaten";

        Assert.True(progress.IsReachable(beaten, EarnedPacks, abilities));
        Assert.True(progress.IsReachable(beaten, 3, abilities));
    }
}
