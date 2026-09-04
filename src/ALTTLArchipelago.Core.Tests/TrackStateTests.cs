using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

public class TrackStateTests
{
    private static SlotData Seed(int slots, params int[] boundaries)
    {
        var data = new SlotData
        {
            PackBoundaries = new List<int>(boundaries),
            PackTotal = boundaries.Length - 1,
            PackSize = 4,
        };
        for (int i = 0; i < slots; i++)
        {
            data.Slots.Add(new SlotEntry { LevelId = $"L{i}", LevelIndex = i });
        }
        return data;
    }

    [Fact]
    public void NothingBeyondTheFreeOpeningIsVisibleAtTheStart()
    {
        var track = new TrackState(Seed(12, 4, 8, 12));

        Assert.Equal(4, track.OpenSlots);
        Assert.True(track.IsOpen(3));
        Assert.False(track.IsOpen(4));
    }

    [Fact]
    public void EachPackOpensExactlyItsBlock()
    {
        var track = new TrackState(Seed(12, 4, 8, 12));

        track.SetPacksHeld(1);
        Assert.Equal(8, track.OpenSlots);
        track.SetPacksHeld(2);
        Assert.Equal(12, track.OpenSlots);
    }

    [Fact]
    public void HoldingEveryPackOpensEverySlot()
    {
        var track = new TrackState(Seed(12, 4, 8, 12));
        track.SetPacksHeld(track.PackTotal);
        Assert.Equal(12, track.OpenSlots);
        Assert.True(track.IsOpen(11));
    }

    [Fact]
    public void MorePacksThanTheSeedHasIsNotAnError()
    {
        // Archipelago resends items on reconnect, and a generator change can
        // leave an old save holding more packs than the new seed contains.
        // Clamping beats indexing past the table.
        var track = new TrackState(Seed(12, 4, 8, 12));
        track.SetPacksHeld(99);
        Assert.Equal(12, track.OpenSlots);

        track.SetPacksHeld(-5);
        Assert.Equal(4, track.OpenSlots);
    }

    [Fact]
    public void SettingPacksIsAbsoluteNotCumulative()
    {
        // The item list is replayed in full on every reconnect. Counting
        // arrivals would double the track the second time.
        var track = new TrackState(Seed(12, 4, 8, 12));
        track.SetPacksHeld(1);
        track.SetPacksHeld(1);
        track.SetPacksHeld(1);
        Assert.Equal(8, track.OpenSlots);
    }

    [Fact]
    public void ItReportsExactlyWhichSlotsAPackRevealed()
    {
        var track = new TrackState(Seed(12, 4, 8, 12));
        track.SetPacksHeld(1);

        Assert.Equal(new[] { 4, 5, 6, 7 }, track.SlotsRevealedBy(0));
        Assert.Empty(track.SlotsRevealedBy(1));
        // Going backwards reveals nothing rather than a negative range.
        Assert.Empty(track.SlotsRevealedBy(2));
    }

    [Fact]
    public void APayloadWithNoBoundariesFallsBackToTheOpening()
    {
        // A generator too old to send them. Opening everything would hand the
        // player the whole run; opening nothing would strand them.
        var data = Seed(12);
        data.PackBoundaries.Clear();
        data.PackSize = 4;

        var track = new TrackState(data);
        Assert.Equal(4, track.OpenSlots);
    }

    [Fact]
    public void TheRealGeneratorPayloadOpensTheWholeRun()
    {
        var data = ExampleSeed.Load();
        var track = new TrackState(data);

        Assert.True(track.OpenSlots > 0, "nothing is open at the start");
        Assert.True(track.OpenSlots < data.Slots.Count,
            "the whole run is open before any pack arrives");

        track.SetPacksHeld(track.PackTotal);
        Assert.Equal(data.Slots.Count, track.OpenSlots);
    }
}
