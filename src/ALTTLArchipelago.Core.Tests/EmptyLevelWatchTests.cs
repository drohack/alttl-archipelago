using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// "LEVEL LOADED EMPTY" is for a puzzle that came up with nothing in it. The
/// base gate of 2026-09-25 raised it for 01__Chapter_HomeSweetHome - a
/// chapter card, which never has objects or controllers - at the title after
/// a Skip's fallback went back to the track, and the watch also shows the
/// player a "failed to load" toast.
/// </summary>
public class EmptyLevelWatchTests
{
    [Fact]
    public void APuzzleWithNothingInItLoadedEmpty()
    {
        Assert.True(EmptyLevelWatch.LoadedEmpty(isChapter: false, isCredits: false,
                                                controllers: 0, objects: 0));
    }

    [Fact]
    public void AChapterCardIsNeverAnEmptyPuzzle()
    {
        Assert.False(EmptyLevelWatch.LoadedEmpty(isChapter: true, isCredits: false,
                                                 controllers: 0, objects: 0));
    }

    [Fact]
    public void TheCreditsAreNeverAnEmptyPuzzle()
    {
        Assert.False(EmptyLevelWatch.LoadedEmpty(isChapter: false, isCredits: true,
                                                 controllers: 0, objects: 0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(5, 30)]
    public void APuzzleWithSomethingInItDidNot(int controllers, int objects)
    {
        Assert.False(EmptyLevelWatch.LoadedEmpty(false, false, controllers, objects));
    }
}
