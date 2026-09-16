using ALTTLArchipelago.Core;
using Xunit;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The run's sidecar file: what it holds, and when it has actually changed.
///
/// WHY THESE EXIST. The Core csproj records that the run state was one of four
/// Unity-free files left stranded in the plugin with no tests, and that two of
/// the five bugs a 2026-09-04 audit found were in exactly that untested code.
/// One of the two was CreditsPlayed, whose loss meant a run finished offline
/// never reported its goal - the multiworld waiting forever on a slot that had
/// genuinely finished. It had no test then. It does now.
/// </summary>
public class RunStateDataTests
{
    [Fact]
    public void ANewRunHoldsNothingAndSaysSo()
    {
        var state = new RunStateData();

        Assert.False(state.HasProgress);
        Assert.Equal(0, state.HintPagesOpened);
        Assert.False(state.CreditsPlayed);
        Assert.Empty(state.Owed);
        Assert.Empty(state.Beaten);
    }

    [Fact]
    public void CreditsPlayedSurvivesTheRoundTrip()
    {
        // THE 0.3.3 BUG, pinned. Finish offline, play the credits, quit,
        // come back: the flag has to still be there or the goal is never sent.
        var state = new RunStateData();
        Assert.True(state.NoteCreditsPlayed());

        var reloaded = RunStateData.FromJson(state.ToJson());

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.CreditsPlayed);
    }

    [Fact]
    public void NotingTheCreditsTwiceChangesNothingTheSecondTime()
    {
        var state = new RunStateData();

        Assert.True(state.NoteCreditsPlayed());
        Assert.False(state.NoteCreditsPlayed());
    }

    [Fact]
    public void ARunFileWrittenBeforeAFieldExistedStillLoads()
    {
        // Every key here predates creditsPlayed and hintPages. Both must
        // degrade to their empty value rather than failing the load, because
        // the alternative is a player losing a run to a mod update.
        const string old = """
            {"owed":["A"],"skipsUsed":2,"trapsSprung":1,"beaten":["B"]}
            """;

        var state = RunStateData.FromJson(old);

        Assert.NotNull(state);
        Assert.Equal(new[] { "A" }, state!.Owed);
        Assert.Equal(2, state.SkipsUsed);
        Assert.Equal(1, state.TrapsSprung);
        Assert.Equal(new[] { "B" }, state.Beaten);
        Assert.False(state.CreditsPlayed);
        Assert.Empty(state.HintPages);
    }

    [Fact]
    public void ARealRunFileFromBeforeCreditsPlayedStillLoads()
    {
        // Copied verbatim from a sidecar on droha's machine, written by the
        // mod before creditsPlayed existed. Synthetic JSON proves the parser
        // works; this proves it works on what is actually out there.
        const string onDisk =
            """{"owed":[],"skipsUsed":0,"trapsSprung":23,"beaten":[],"hintPages":[]}""";

        var state = RunStateData.FromJson(onDisk);

        Assert.NotNull(state);
        Assert.Equal(23, state!.TrapsSprung);
        Assert.False(state.CreditsPlayed);

        // And writing it back keeps every key it arrived with, so an older
        // mod reading the file afterwards still finds what it expects.
        var rewritten = state.ToJson();
        foreach (var key in new[] { "owed", "skipsUsed", "trapsSprung",
                                    "beaten", "hintPages" })
        {
            Assert.Contains($"\"{key}\"", rewritten);
        }
    }

    [Fact]
    public void GarbageIsNullRatherThanAnException()
    {
        // A corrupt sidecar must not stop the game starting.
        Assert.Null(RunStateData.FromJson("not json at all"));
        Assert.Null(RunStateData.FromJson(""));
        Assert.Null(RunStateData.FromJson("   "));
    }

    [Fact]
    public void TheJsonKeysAreTheOnesAlreadyOnDisk()
    {
        // Renaming any of these silently orphans every run file in existence:
        // the load succeeds, every field is its default, and the player's
        // offline progress is gone with no error anywhere.
        var state = new RunStateData();
        state.SpendSkip();
        state.SpendTrap(1);
        state.NoteCreditsPlayed();
        state.OpenHintPage("3:1");
        state.SetOwed(new[] { "A" });
        state.SetBeaten(new[] { "B" });

        var json = state.ToJson();

        Assert.Contains("\"owed\"", json);
        Assert.Contains("\"skipsUsed\"", json);
        Assert.Contains("\"trapsSprung\"", json);
        Assert.Contains("\"beaten\"", json);
        Assert.Contains("\"hintPages\"", json);
        Assert.Contains("\"creditsPlayed\"", json);
    }

    [Fact]
    public void SettingTheSameOwedListAgainReportsNoChange()
    {
        // The write debounce. The flush that calls this runs on a timer, so
        // without it an offline session re-saves an identical list every few
        // seconds for as long as it is offline.
        var state = new RunStateData();

        Assert.True(state.SetOwed(new[] { "A", "B" }));
        Assert.False(state.SetOwed(new[] { "A", "B" }));
        Assert.True(state.SetOwed(new[] { "A", "B", "C" }));
    }

    [Fact]
    public void AReorderedListCountsAsAChange()
    {
        // Order is content here, not incidental. Comparing as sets would make
        // a reordered ledger compare equal and skip the write that records it.
        var state = new RunStateData();

        Assert.True(state.SetOwed(new[] { "A", "B" }));
        Assert.True(state.SetOwed(new[] { "B", "A" }));
    }

    [Fact]
    public void ShorteningTheOwedListCountsAsAChange()
    {
        // The direction that matters most: checks being ACCEPTED is what
        // empties this list, and failing to persist that re-sends them.
        var state = new RunStateData();
        state.SetOwed(new[] { "A", "B" });

        Assert.True(state.SetOwed(new[] { "A" }));
        Assert.True(state.SetOwed(System.Array.Empty<string>()));
        Assert.Empty(state.Owed);
    }

    [Fact]
    public void SettingTheSameBeatenListAgainReportsNoChange()
    {
        var state = new RunStateData();

        Assert.True(state.SetBeaten(new[] { "Level Beaten - 1" }));
        Assert.False(state.SetBeaten(new[] { "Level Beaten - 1" }));
    }

    [Fact]
    public void AnOwedListIsCopiedRatherThanAliased()
    {
        // The caller owns its ledger and keeps mutating it. Holding the same
        // instance would make every later edit appear here unrecorded, and
        // the next SetOwed would then compare equal and skip the write.
        var caller = new List<string> { "A" };
        var state = new RunStateData();
        state.SetOwed(caller);

        caller.Add("B");

        Assert.Single(state.Owed);
        Assert.True(state.SetOwed(caller));
    }

    [Fact]
    public void APageStaysPaidFor()
    {
        // Leaving a puzzle and coming back must not charge a second Hint Page
        // for a page already read.
        var state = new RunStateData();

        Assert.True(state.OpenHintPage("3:1"));
        Assert.False(state.OpenHintPage("3:1"));
        Assert.True(state.IsHintPageOpen("3:1"));
        Assert.Equal(1, state.HintPagesOpened);
    }

    [Fact]
    public void PagesAreKeyedPerSlotNotPerLevel()
    {
        // A generator drawn twice into one run has two notepads, and paying
        // for one must not open the other.
        var state = new RunStateData();

        Assert.True(state.OpenHintPage("3:1"));
        Assert.True(state.OpenHintPage("9:1"));
        Assert.False(state.IsHintPageOpen("9:2"));
        Assert.Equal(2, state.HintPagesOpened);
    }

    [Fact]
    public void AnEmptyPageKeyBuysNothing()
    {
        var state = new RunStateData();

        Assert.False(state.OpenHintPage(""));
        Assert.Equal(0, state.HintPagesOpened);
    }

    [Fact]
    public void SpendingASkipAlwaysCounts()
    {
        var state = new RunStateData();

        Assert.True(state.SpendSkip());
        Assert.True(state.SpendSkip());
        Assert.Equal(2, state.SkipsUsed);
    }

    [Fact]
    public void SpringingNoTrapsChangesNothing()
    {
        // Called with whatever the item list produced, which is zero on most
        // reconnects. A write per reconnect for no change is worth avoiding,
        // and a negative must never walk the count backwards.
        var state = new RunStateData();

        Assert.False(state.SpendTrap(0));
        Assert.False(state.SpendTrap(-3));
        Assert.Equal(0, state.TrapsSprung);

        Assert.True(state.SpendTrap(2));
        Assert.Equal(2, state.TrapsSprung);
    }

    [Fact]
    public void ProgressIsReportedOnceThereIsAny()
    {
        var state = new RunStateData();
        Assert.False(state.HasProgress);

        state.SpendSkip();

        Assert.True(state.HasProgress);
        Assert.Contains("1 skip(s) used", state.Summary());
    }

    [Fact]
    public void CreditsPlayedAloneIsNotProgressWorthLogging()
    {
        // Deliberate: the summary line lists counts, and "0 checks owed, 0
        // skips used ..." on a fresh run is noise. Playing the credits is
        // reported by Credits, not here.
        var state = new RunStateData();
        state.NoteCreditsPlayed();

        Assert.False(state.HasProgress);
    }

    [Fact]
    public void AWholeRunSurvivesTheRoundTrip()
    {
        var state = new RunStateData();
        state.SetOwed(new[] { "Medicine Cabinet - Blue Bottles" });
        state.SetBeaten(new[] { "Spoons", "Books 3" });
        state.SpendSkip();
        state.SpendTrap(4);
        state.OpenHintPage("12:2");
        state.NoteCreditsPlayed();

        var reloaded = RunStateData.FromJson(state.ToJson());

        Assert.NotNull(reloaded);
        Assert.Equal(state.Owed, reloaded!.Owed);
        Assert.Equal(state.Beaten, reloaded.Beaten);
        Assert.Equal(1, reloaded.SkipsUsed);
        Assert.Equal(4, reloaded.TrapsSprung);
        Assert.True(reloaded.IsHintPageOpen("12:2"));
        Assert.True(reloaded.CreditsPlayed);
        Assert.Equal(state.Summary(), reloaded.Summary());
    }
}
