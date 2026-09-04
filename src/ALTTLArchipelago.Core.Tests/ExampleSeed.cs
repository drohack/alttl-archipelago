using ALTTLArchipelago.Core;

namespace ALTTLArchipelago.Core.Tests;

/// <summary>
/// The real payload the generator produces, shared by every test that wants
/// one.
///
/// docs/data/slot-data-example.json is written by the apworld's own tests from
/// an actual generated seed. Tests parse THAT rather than a hand-written mock,
/// because a mock only ever proves the mock matches the DTO - it is the two
/// languages agreeing that can actually break.
/// </summary>
internal static class ExampleSeed
{
    internal static string Path()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "docs", "data")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!.FullName, "docs", "data",
                                      "slot-data-example.json");
    }

    internal static SlotData Load()
    {
        var path = Path();
        Assert.True(File.Exists(path),
            $"{path} missing. Regenerate with ALTTL_WRITE_GOLDEN=1 "
            + "python -m unittest worlds.alttl.test.test_slot_data");
        return SlotData.FromJson(File.ReadAllText(path));
    }
}
