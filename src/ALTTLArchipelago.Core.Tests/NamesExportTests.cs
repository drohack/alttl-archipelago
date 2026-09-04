using System.Text.Json;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// Exports every player-facing name to apworld/alttl/data/names.json, so the
/// Python apworld can read them instead of reimplementing DisplayNames and
/// PartNames.
///
/// Those two are fiddly - camel-case splitting, pack prefixes moving to
/// suffixes, an override for "Jack O'Lanterns", per-level collision fallback -
/// and a second implementation in another language would drift. Drift here is
/// not cosmetic: these strings ARE Archipelago location names, so a mismatch
/// means the generator and the mod disagree about what a check is called.
///
/// Normally this test asserts the committed file is current. Run with
/// ALTTL_WRITE_GOLDEN=1 to regenerate it after an intentional change - and
/// remember that an intentional change breaks seeds already in flight.
/// </summary>
public class NamesExportTests
{
    /// <summary>
    /// One controller group as the apworld sees it. The abilities travel WITH
    /// the display name rather than in a parallel map, so the two cannot drift
    /// apart - a part whose name is exported without its requirement is exactly
    /// the bug this file exists to prevent.
    /// </summary>
    private sealed class PartEntry
    {
        public string display { get; set; } = "";
        public List<string> abilities { get; set; } = new();

        /// <summary>
        /// The controller GameObject names this group is made of.
        ///
        /// Usually just the key, but a mutually-dependent pair merges into one
        /// group with two members. The mod needs this to answer the only
        /// question it gets asked at runtime - "this controller just solved,
        /// which location is that?" - because the solved event carries a
        /// GameObject name, not a group.
        /// </summary>
        public List<string> members { get; set; } = new();
    }

    private sealed class LevelNames
    {
        public string display { get; set; } = "";
        public Dictionary<string, PartEntry> parts { get; set; } = new();
    }

    private static string RepoDataDir()
    {
        // bin/Debug/net10.0 -> up to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "apworld")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "apworld", "alttl", "data");
    }

    private static string Build()
    {
        var table = LevelTable.FromJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")));

        var byLevel = new SortedDictionary<string, LevelNames>(StringComparer.Ordinal);
        foreach (var level in table.Levels)
        {
            var entry = new LevelNames { display = DisplayNames.For(level.LevelId) };
            foreach (var g in ControllerGroups.For(level))
            {
                entry.parts[g.Name] = new PartEntry
                {
                    display = g.DisplayName,
                    // Sorted so the export is stable across runs; already the
                    // transitive closure over one-way dependencies.
                    abilities = g.Abilities.OrderBy(a => a, StringComparer.Ordinal).ToList(),
                    members = g.Members.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                };
            }
            byLevel[level.LevelId] = entry;
        }

        var payload = new Dictionary<string, object>
        {
            ["_comment"] = "Generated from ALTTLArchipelago.Core by NamesExportTests. "
                + "Do not hand edit. Regenerate with ALTTL_WRITE_GOLDEN=1 dotnet test. "
                + "These strings are Archipelago location names: changing one breaks "
                + "seeds already in flight.",
            ["maxGeneratorInstances"] = LocationNames.MaxGeneratorInstances,
            ["credits"] = LocationNames.Credits,
            // Pinned across languages for the same reason the location names
            // are: the mod matches these as literal strings, so a mismatch is
            // silent rather than loud.
            ["items"] = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["pack"] = ItemNames.Pack,
                ["credits"] = ItemNames.Credits,
                ["skip"] = ItemNames.Skip,
                ["catTrap"] = ItemNames.CatTrap,
                ["beatenToken"] = ItemNames.BeatenToken,
            },
            ["levels"] = byLevel,
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public void TheExportedNamesAreCurrent()
    {
        var path = Path.Combine(RepoDataDir(), "names.json");
        var built = Build();

        if (Environment.GetEnvironmentVariable("ALTTL_WRITE_GOLDEN") == "1")
        {
            File.WriteAllText(path, built);
            return;
        }

        Assert.True(File.Exists(path),
            $"names.json missing. Generate it with: ALTTL_WRITE_GOLDEN=1 dotnet test");
        Assert.Equal(built.Replace("\r\n", "\n"), File.ReadAllText(path).Replace("\r\n", "\n"));
    }
}
