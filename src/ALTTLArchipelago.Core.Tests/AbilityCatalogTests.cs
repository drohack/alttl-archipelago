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
