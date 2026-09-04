using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class AbilityStateTests
{
    private static SlotData Seed(bool locks = true, params string[] starting)
    {
        return new SlotData
        {
            AbilityLocks = locks,
            Abilities = new Dictionary<string, List<string>>
            {
                ["Stacking"] = new() { "Stackables" },
                ["Ordering"] = new() { "DraggablesOrdered", "Pencils" },
            },
            StartingAbilities = new List<string>(starting),
        };
    }

    [Fact]
    public void AClassIsLockedUntilItsAbilityArrives()
    {
        var state = new AbilityState(Seed());
        Assert.True(state.IsClassLocked("Stackables"));

        state.Grant("Stacking");
        Assert.False(state.IsClassLocked("Stackables"));
    }

    [Fact]
    public void OneAbilityUnlocksEveryClassItCovers()
    {
        var state = new AbilityState(Seed());
        state.Grant("Ordering");

        Assert.False(state.IsClassLocked("DraggablesOrdered"));
        Assert.False(state.IsClassLocked("Pencils"));
    }

    [Fact]
    public void AnUnknownClassIsNeverLocked()
    {
        // Fails OPEN on purpose. A class the catalogue has not heard of means
        // the game has content our table does not know about; locking it would
        // make a puzzle unsolvable for a reason nobody can see.
        var state = new AbilityState(Seed());

        Assert.False(state.IsClassLocked("Draggables"));      // baseline verb
        Assert.False(state.IsClassLocked("SomethingNewInADlc"));
        Assert.False(state.IsClassLocked(""));
        Assert.False(state.IsClassLocked(null!));
    }

    [Fact]
    public void NothingIsLockedWhenTheYamlTurnedLocksOff()
    {
        var state = new AbilityState(Seed(locks: false));
        Assert.False(state.IsClassLocked("Stackables"));
        Assert.False(state.LocksEnabled);
    }

    [Fact]
    public void StartingAbilitiesAreHeldImmediately()
    {
        var state = new AbilityState(Seed(true, "Stacking"));
        Assert.True(state.Has("Stacking"));
        Assert.False(state.IsClassLocked("Stackables"));
    }

    [Fact]
    public void SettingTheHeldSetIsAbsoluteOverItems()
    {
        // Archipelago replays every item on reconnect. Absolute assignment
        // means a replay cannot leave the state disagreeing with the server.
        var state = new AbilityState(Seed());
        state.Grant("Stacking");
        state.SetHeld(new[] { "Ordering" });

        Assert.False(state.Has("Stacking"));
        Assert.True(state.Has("Ordering"));
    }

    [Fact]
    public void AStartingAbilitySurvivesTheItemListBeingReplaced()
    {
        // The bug this split exists for. A starting ability is known from
        // slot_data the moment the seed is, but it reaches the item stream on
        // its own schedule - one arrived a step AHEAD of slot_data in testing.
        // With a single set, the next absolute assignment took it away again.
        var state = new AbilityState(Seed(true, "Stacking"));
        state.SetHeld(new[] { "Ordering" });

        Assert.True(state.Has("Stacking"));
        Assert.False(state.IsClassLocked("Stackables"));
    }

    [Fact]
    public void ReplayingAnEmptyItemListKeepsTheStartingAbilities()
    {
        var state = new AbilityState(Seed(true, "Stacking"));
        state.SetHeld(Array.Empty<string>());

        Assert.True(state.Has("Stacking"));
        Assert.Contains("Stacking", state.Held);
    }

    [Fact]
    public void HasAllAnswersWhetherAGroupIsSolvable()
    {
        var state = new AbilityState(Seed());
        Assert.True(state.HasAll(Array.Empty<string>()));
        Assert.False(state.HasAll(new[] { "Stacking" }));

        state.Grant("Stacking");
        Assert.True(state.HasAll(new[] { "Stacking" }));
        Assert.False(state.HasAll(new[] { "Stacking", "Ordering" }));
    }

    [Fact]
    public void TheRealCatalogueMapsEveryClassToItsAbility()
    {
        // Built from the same abilities.json the generator used, so a class
        // gated in logic must be gated here too or the two disagree about what
        // a player can move.
        var state = new AbilityState(ExampleSeed.Load());
        var data = ExampleSeed.Load();

        foreach (var (ability, classes) in data.Abilities)
        {
            foreach (var cls in classes)
            {
                Assert.Equal(ability, state.AbilityFor(cls));
            }
        }
    }

    [Fact]
    public void EveryClassGatedInLogicIsGatedHereToo()
    {
        var data = ExampleSeed.Load();
        // No starting abilities: those survive SetHeld by design, and this test
        // is about a player who holds nothing at all.
        data.StartingAbilities = new List<string>();
        var state = new AbilityState(data);
        state.SetHeld(Array.Empty<string>());

        foreach (var (_, classes) in data.Abilities)
        {
            foreach (var cls in classes)
            {
                Assert.True(state.IsClassLocked(cls),
                    $"{cls} is gated by the generator but not by the mod");
            }
        }
    }

    [Fact]
    public void OnlyTheSeedsOwnAbilitiesCountAsAbilities()
    {
        // The server sends filler, traps and the beaten token down the same
        // channel. Treating an unrecognised name as an ability put "Daily
        // Badge" in the held set.
        var state = new AbilityState(Seed());

        Assert.True(state.IsAbility("Stacking"));
        Assert.True(state.IsAbility("Ordering"));
        Assert.False(state.IsAbility("Daily Badge"));
        Assert.False(state.IsAbility("Progressive Puzzle Pack"));
        Assert.False(state.IsAbility("Level Beaten"));
        Assert.False(state.IsAbility("Cat Trap"));
        Assert.False(state.IsAbility(""));
        Assert.False(state.IsAbility(null!));
    }

    [Fact]
    public void NoSpecialItemNameIsAlsoAnAbilityInARealSeed()
    {
        // If one ever were, the classification would send it down two paths at
        // once and the count that mattered would be short.
        var state = new AbilityState(ExampleSeed.Load());

        Assert.False(state.IsAbility(ItemNames.Pack));
        Assert.False(state.IsAbility(ItemNames.Credits));
        Assert.False(state.IsAbility(ItemNames.Skip));
        Assert.False(state.IsAbility(ItemNames.CatTrap));
        Assert.False(state.IsAbility(ItemNames.BeatenToken));
    }
}
