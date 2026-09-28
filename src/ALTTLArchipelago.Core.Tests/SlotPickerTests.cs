using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Play, the next arrow and the daily guard pick playable slots only, and -1
/// sends the player to the level select. droha, 2026-09-26 and the 0.4.2
/// playtest: after Shells #2 and Trim Plant #2 the daily guard opened puzzles
/// "waiting on Sticking" and "waiting on Gadgets".
/// </summary>
public class SlotPickerTests
{
    /// <summary>
    /// Three one-level slots: A needs nothing, B needs Swapping, C needs
    /// nothing but sits behind the first pack (two slots open at the start).
    /// </summary>
    private static (SlotPicker Picker, TrackState Track, HashSet<string> Done, AbilityState Abilities) Build()
    {
        var data = new SlotData
        {
            AbilityLocks = true,
            Abilities = new Dictionary<string, List<string>> { ["Swapping"] = new() { "Shuffleables" } },
        };
        data.PackBoundaries.AddRange(new[] { 2, 3 });
        foreach (var (id, index) in new[] { ("A", 1), ("B", 2), ("C", 3) })
        {
            data.Slots.Add(new SlotEntry { LevelId = id, LevelIndex = index, Instance = 1 });
        }
        data.Requirements["A - Solution 1"] = new Requirement { Packs = 0, Abilities = new List<string>() };
        data.Requirements["B - Solution 1"] = new Requirement { Packs = 0, Abilities = new List<string> { "Swapping" } };
        data.Requirements["C - Solution 1"] = new Requirement { Packs = 1, Abilities = new List<string>() };

        var router = new CheckRouter(data);
        var track = new TrackState(data);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var abilities = new AbilityState(data);
        return (new SlotPicker(track, router, done.Contains, new SlotProgress(data, router), abilities),
                track, done, abilities);
    }

    [Fact]
    public void ABlockedOrShutSlotIsNeverPickedAndNothingPlayableIsMinusOne()
    {
        var (picker, _, done, _) = Build();
        Assert.Equal(0, picker.Next(-1));
        Assert.Equal(0, picker.Next(0));          // wraps back to the only playable one

        done.Add("A - Solution 1");               // A finished; B blocked; C shut
        Assert.Equal(-1, picker.Next(0));
        Assert.Equal(-1, picker.First());
        Assert.Equal(-1, picker.Farthest());
        Assert.False(picker.AnyPlayableBesides(0));
    }

    [Fact]
    public void APackOrAnAbilityMakesASlotPlayableAgain()
    {
        var (picker, track, done, abilities) = Build();
        done.Add("A - Solution 1");

        abilities.SetHeld(new[] { "Swapping" });
        Assert.Equal(1, picker.Next(0));

        track.SetPacksHeld(1);
        Assert.Equal(2, picker.Farthest());
        Assert.Equal(1, picker.First());
        Assert.True(picker.AnyPlayableBesides(1));
    }

    [Fact]
    public void BeforeTheRunIsWiredEveryOpenSlotCountsAsPlayable()
    {
        var (_, track, done, _) = Build();
        var data = new SlotData();
        var router = new CheckRouter(data);
        var open = new SlotPicker(track, router, done.Contains, progress: null, abilities: null);
        Assert.Equal(1, open.Next(0));
    }
}
