using System.Text.Json;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Parses the REAL payload the generator produces, not a hand-written mock.
///
/// docs/data/slot-data-example.json is written by the apworld's own tests from
/// an actual generated seed. Parsing a mock would only prove the mock matches
/// the DTO; parsing the generator's output proves the two languages agree,
/// which is the thing that can actually break. A field renamed in Python fails
/// here rather than reading as null in a player's game.
/// </summary>
public class SlotDataTests
{
    private static SlotData Example() => ExampleSeed.Load();

    [Fact]
    public void TheGeneratorsPayloadParses()
    {
        var data = Example();

        Assert.NotEmpty(data.Slots);
        Assert.NotEmpty(data.Requirements);
        Assert.NotEmpty(data.Abilities);
        Assert.True(data.PackSize > 0);
        Assert.True(data.PackTotal > 0);
        Assert.True(data.LevelsToBeat > 0);
    }

    [Fact]
    public void NothingSilentlyParsedAsADefault()
    {
        // The failure this guards is a renamed JSON key: System.Text.Json does
        // not complain about an unmatched property, it just leaves the default
        // sitting there. So assert the values are ones no default could give.
        var data = Example();

        Assert.All(data.Slots, slot =>
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.LevelId));
            Assert.True(slot.LevelIndex >= 0, $"{slot.LevelId} kept the -1 default");
            Assert.Contains(slot.Source, new[] { "generator", "archive", "base" });
            Assert.True(slot.Instance >= 1);
        });

        Assert.All(data.Requirements, entry =>
            Assert.True(entry.Value.Packs >= 0, entry.Key));
    }

    [Fact]
    public void GeneratorSlotsCarryASeedAndFixedLevelsDoNot()
    {
        foreach (var slot in Example().Slots)
        {
            if (slot.IsGenerator)
            {
                Assert.True(slot.Seed > 0, $"{slot.LevelId} is a generator with no seed");
            }
            else
            {
                Assert.Equal(-1, slot.Seed);
            }
        }
    }

    [Fact]
    public void TheRealPayloadHasNoProblems()
    {
        Assert.Empty(Example().Problems());
    }

    [Fact]
    public void ProblemsCatchesAPayloadThatWouldBreakTheMod()
    {
        // Each of these would otherwise fail somewhere far from the cause.
        Assert.Contains("no slots", new SlotData().Problems());

        var badPacks = new SlotData
        {
            Slots = { new SlotEntry { LevelId = "x", LevelIndex = 0 } },
            PackSize = 0,
        };
        Assert.Contains(badPacks.Problems(), p => p.Contains("pack_size"));

        var unreachable = new SlotData
        {
            Slots = { new SlotEntry { LevelId = "x", LevelIndex = 0 } },
            PackTotal = 2,
            Requirements = { ["somewhere"] = new Requirement { Packs = 5 } },
        };
        Assert.Contains(unreachable.Problems(), p => p.Contains("needs 5 packs"));

        var ghostAbility = new SlotData
        {
            Slots = { new SlotEntry { LevelId = "x", LevelIndex = 0 } },
            Requirements = { ["somewhere"] = new Requirement { Abilities = { "Nope" } } },
        };
        Assert.Contains(ghostAbility.Problems(), p => p.Contains("unknown ability"));
    }

    [Fact]
    public void AMissingFieldFallsBackToTheOptionDefault()
    {
        // A server predating a field sends it missing, not wrong. The mod must
        // then behave like the yaml default rather than like zero.
        var sparse = SlotData.FromJson("""{"slots": []}""");

        Assert.Equal(4, sparse.PackSize);
        Assert.Equal(40, sparse.LevelsToBeat);
        Assert.True(sparse.AbilityLocks);
        Assert.Equal(10, sparse.CatTrapChance);
        Assert.Empty(sparse.StartingAbilities);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredRatherThanFatal()
    {
        // A newer generator may add a field this mod predates. Refusing to
        // parse would turn a forward-compatible change into a broken install.
        var data = SlotData.FromJson(
            """{"slots": [], "pack_size": 3, "something_new": {"a": 1}}""");

        Assert.Equal(3, data.PackSize);
    }

    [Fact]
    public void EveryAbilityGatingALocationIsInTheCatalogue()
    {
        var data = Example();
        foreach (var (name, requirement) in data.Requirements)
        {
            foreach (var ability in requirement.Abilities)
            {
                Assert.True(data.Abilities.ContainsKey(ability),
                    $"{name} needs '{ability}', which the catalogue does not list");
            }
        }
    }

    [Fact]
    public void TheFingerprintIsStableForOneSeed()
    {
        // It names the save file, so it must be identical every launch or a
        // run cannot be resumed.
        Assert.Equal(Example().Fingerprint(), Example().Fingerprint());
        Assert.Equal(8, Example().Fingerprint().Length);
    }

    [Fact]
    public void DifferentDrawsGetDifferentFingerprints()
    {
        // Two multiworlds sharing a fingerprint would share a save file and
        // merge their progress - the exact bug this replaced.
        var a = Example();
        var b = Example();
        b.Slots[0].Seed += 1;
        Assert.NotEqual(a.Fingerprint(), b.Fingerprint());

        var c = Example();
        c.Slots[0].LevelId += "x";
        Assert.NotEqual(a.Fingerprint(), c.Fingerprint());

        var d = Example();
        d.Slots.RemoveAt(0);
        Assert.NotEqual(a.Fingerprint(), d.Fingerprint());
    }

    [Fact]
    public void AnEmptyPayloadStillFingerprints()
    {
        Assert.False(string.IsNullOrEmpty(new SlotData().Fingerprint()));
    }

    [Fact]
    public void AbilityClassesMatchTheSharedCatalogue()
    {
        // The mod dims objects by controller CLASS, so these strings have to
        // agree with Core's own catalogue or the wrong objects get locked.
        var data = Example();
        foreach (var (ability, classes) in data.Abilities)
        {
            Assert.NotEmpty(classes);
            foreach (var cls in classes)
            {
                Assert.Equal(ability, Abilities.ForClass(cls));
            }
        }
    }
}
