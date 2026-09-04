using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The badge tells a player which cards are worth opening. Wrong in one
/// direction wastes their time; wrong in the other hides work they could do.
/// </summary>
public class SlotProgressTests
{
    private static SlotData Seed()
    {
        var data = new SlotData
        {
            AbilityLocks = true,
            Abilities = new Dictionary<string, List<string>>
            {
                ["Swapping"] = new() { "Shuffleables" },
                ["Jigsaw"] = new() { "DraggablesJigsaw" },
            },
        };
        data.Slots.Add(new SlotEntry { LevelId = "Books 3", LevelIndex = 13, Instance = 1 });
        data.ControllerGroups["Books 3"] = new Dictionary<string, string>
        {
            ["Design (Shuffle)"] = "Design",
            ["Height (Draggables)"] = "Height",
        };
        data.Requirements["Books 3 - Design"] =
            new Requirement { Packs = 0, Abilities = new List<string> { "Swapping" } };
        data.Requirements["Books 3 - Height"] =
            new Requirement { Packs = 0, Abilities = new List<string>() };
        data.Requirements["Books 3 - Beaten"] =
            new Requirement { Packs = 3, Abilities = new List<string> { "Swapping" } };
        return data;
    }

    private static (SlotProgress, SlotData) Build()
    {
        var data = Seed();
        return (new SlotProgress(data, new CheckRouter(data)), data);
    }

    private static SlotStatus Status(
        SlotProgress progress, SlotData data, int packs,
        IEnumerable<string>? held = null, IEnumerable<string>? collected = null)
    {
        var abilities = new AbilityState(data);
        abilities.SetHeld(held ?? Array.Empty<string>());
        var done = new HashSet<string>(collected ?? Array.Empty<string>(), StringComparer.Ordinal);
        return progress.StatusOf(0, done.Contains, packs, abilities);
    }

    [Fact]
    public void ACardWhereSomethingIsDoableAndSomethingIsNotIsMixed()
    {
        var (progress, data) = Build();
        // Height needs nothing; Design needs Swapping; Beaten needs 3 packs.
        Assert.Equal(SlotStatus.Mixed, Status(progress, data, packs: 0));
    }

    [Fact]
    public void ACardIsLockedWhenNothingLeftCanBeReached()
    {
        var (progress, data) = Build();
        Assert.Equal(SlotStatus.Locked,
            Status(progress, data, packs: 0, collected: new[] { "Books 3 - Height" }));
    }

    [Fact]
    public void ACardIsDoableWhenEverythingLeftCanBeReached()
    {
        var (progress, data) = Build();
        Assert.Equal(SlotStatus.Doable,
            Status(progress, data, packs: 3, held: new[] { "Swapping" }));
    }

    [Fact]
    public void ACardWithNothingLeftIsComplete()
    {
        var (progress, data) = Build();
        Assert.Equal(SlotStatus.Complete, Status(progress, data, packs: 3,
            held: new[] { "Swapping" },
            collected: new[] { "Books 3 - Design", "Books 3 - Height", "Books 3 - Beaten" }));
    }

    [Fact]
    public void PacksGateReachabilityJustAsAbilitiesDo()
    {
        var (progress, data) = Build();
        var abilities = new AbilityState(data);
        abilities.SetHeld(new[] { "Swapping" });

        Assert.False(progress.IsReachable("Books 3 - Beaten", 2, abilities));
        Assert.True(progress.IsReachable("Books 3 - Beaten", 3, abilities));
    }

    [Fact]
    public void WithLocksOffOnlyPacksGate()
    {
        // The yaml can turn ability locks off. The badge must not go on
        // claiming a card is locked by an ability that is not enforced.
        var data = Seed();
        data.AbilityLocks = false;
        var progress = new SlotProgress(data, new CheckRouter(data));

        var abilities = new AbilityState(data);
        Assert.True(progress.IsReachable("Books 3 - Design", 0, abilities));
        Assert.False(progress.IsReachable("Books 3 - Beaten", 0, abilities));
    }

    [Fact]
    public void ALocationTheSeedDoesNotHaveIsNotTreatedAsBlocked()
    {
        // Otherwise a card would sit red forever over something that does not
        // exist in this seed at all.
        var (progress, data) = Build();
        var abilities = new AbilityState(data);
        Assert.True(progress.IsReachable("Something Else Entirely", 0, abilities));
    }

    [Fact]
    public void TheRealSeedHasSomethingDoableFromTheVeryStart()
    {
        // The property that matters most: a player who has just connected must
        // have at least one card worth opening. If every badge were red on
        // turn one, the run could not begin.
        var data = ExampleSeed.Load();
        var router = new CheckRouter(data);
        var progress = new SlotProgress(data, router);
        var track = new TrackState(data);
        var abilities = new AbilityState(data);

        var doable = 0;
        for (int i = 0; i < track.OpenSlots; i++)
        {
            var status = progress.StatusOf(i, _ => false, 0, abilities);
            if (status == SlotStatus.Doable || status == SlotStatus.Mixed) doable++;
        }

        Assert.True(doable > 0,
            "every card in the opening is locked, so the run cannot start");
    }
}
