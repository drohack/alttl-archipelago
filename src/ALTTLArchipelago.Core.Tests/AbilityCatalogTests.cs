using System.Text.Json;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The ability grouping is authored data shared across two languages: the
/// Python apworld reads apworld/alttl/data/abilities.json, and the C# mod has
/// it compiled into Abilities.cs because loading a file at runtime inside a
/// BepInEx plugin is one more thing to ship and get wrong.
///
/// Two copies means drift, and drift here is silent and severe: the generator
/// would place items the mod never applies, or gate levels the mod thinks are
/// free. Since a cross-language derivation is not practical, a test enforces
/// the equality instead - the same trick cw4 uses where an import cycle blocks
/// a derivation.
/// </summary>
public class AbilityCatalogTests
{
    private sealed class Catalog
    {
        public List<string> baseline { get; set; } = new();
        public List<string> notPuzzles { get; set; } = new();
        public Dictionary<string, List<string>> abilities { get; set; } = new();
        public Dictionary<string, Dictionary<string, List<string>>> dlcAbilities
        { get; set; } = new();
    }

    private static Catalog Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "abilities.json");
        Assert.True(File.Exists(path), $"abilities.json missing at {path}");
        return JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path))!;
    }

    [Fact]
    public void TheSharedFileAndTheCompiledMapAgree()
    {
        var json = Load();

        Assert.Equal(
            json.abilities.Keys.OrderBy(k => k, StringComparer.Ordinal),
            Abilities.All.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (ability, classes) in json.abilities)
        {
            Assert.Equal(
                classes.OrderBy(c => c, StringComparer.Ordinal),
                Abilities.ClassesFor(ability).OrderBy(c => c, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The DLC blocks agree too, including the empty one.
    ///
    /// DLC1 is listed with no abilities rather than left out, and that is the
    /// case worth testing: an absent key and an empty one look the same to a
    /// reader and mean different things to a merge. Asserting the key exists
    /// keeps "this DLC adds no mechanic" a recorded answer rather than a gap.
    /// </summary>
    [Fact]
    public void TheDlcAbilityBlocksAgree()
    {
        var json = Load();

        Assert.Equal(
            json.dlcAbilities.Keys.OrderBy(k => k, StringComparer.Ordinal),
            Abilities.Dlc.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (dlc, abilities) in json.dlcAbilities)
        {
            Assert.Equal(
                abilities.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Abilities.Dlc[dlc].OrderBy(k => k, StringComparer.Ordinal));

            foreach (var (ability, classes) in abilities)
            {
                Assert.Equal(
                    classes.OrderBy(c => c, StringComparer.Ordinal),
                    Abilities.ClassesFor(ability)
                             .OrderBy(c => c, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>
    /// DLC abilities come after every base one, because that is what makes
    /// adding a DLC safe: item ids are positional, so a thirteenth name
    /// inserted among the twelve renumbers Cat Trap, Background Change Trap
    /// and Hint Page and repoints every seed in flight.
    /// </summary>
    [Fact]
    public void EveryDlcAbilityIsAppendedAfterTheBaseTwelve()
    {
        Assert.Equal(Abilities.All, Abilities.AllWithDlc.Take(Abilities.All.Count));
        Assert.Equal(
            Abilities.AllWithDlc.Skip(Abilities.All.Count).OrderBy(a => a, StringComparer.Ordinal),
            Abilities.Dlc.SelectMany(kv => kv.Value).OrderBy(a => a, StringComparer.Ordinal));
        Assert.Empty(Abilities.All.Intersect(Abilities.Dlc.SelectMany(kv => kv.Value)));
    }

    [Fact]
    public void BaselineAndNonPuzzleSetsAgree()
    {
        var json = Load();

        Assert.Equal(
            json.baseline.OrderBy(c => c, StringComparer.Ordinal),
            Abilities.Baseline.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(
            json.notPuzzles.OrderBy(c => c, StringComparer.Ordinal),
            Abilities.NotPuzzles.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void NoClassIsClaimedTwice()
    {
        var json = Load();
        var all = json.abilities.Values.SelectMany(v => v)
            .Concat(json.baseline)
            .Concat(json.notPuzzles)
            .ToList();

        var dupes = all.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Controller classes in two buckets: " + string.Join(", ", dupes));
    }
}
