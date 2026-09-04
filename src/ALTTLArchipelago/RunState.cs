using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALTTLArchipelago;

/// <summary>
/// The little that has to outlive the process but does not belong to the game.
///
/// Two things so far:
///
/// - checks earned but not yet accepted by the server. Without them, playing
///   offline and quitting loses everything earned in that session: the ledger
///   is in memory, and the server never heard about any of it.
/// - how many Skips have been spent. Archipelago replays the ITEMS on
///   reconnect, so the number held is always recoverable - but nothing tells
///   us how many were used, and without that every reconnect would hand them
///   all back.
///
/// A sidecar file rather than a field in the game's save. The save is the
/// game's own format behind a character cipher, and adding a key would mean
/// writing that format correctly forever, for data the game has no interest
/// in. It is named after the run's save so two runs cannot read each other's.
/// </summary>
internal static class RunState
{
    private sealed class Payload
    {
        [JsonPropertyName("owed")]
        public List<string> Owed { get; set; } = new();

        [JsonPropertyName("skipsUsed")]
        public int SkipsUsed { get; set; }
    }

    private static string? _path;
    private static Payload _state = new();

    /// <summary>Counts writes, so a test can assert persistence actually ran.</summary>
    internal static int Writes { get; private set; }

    internal static int SkipsUsed => _state.SkipsUsed;

    internal static void Begin(string saveName)
    {
        _path = null;
        _state = new Payload();

        try
        {
            var dir = SaveRedirect.SaveDirectory();
            if (dir == null) return;
            _path = Path.Combine(dir, saveName + ".run.json");

            if (!File.Exists(_path)) return;

            var loaded = JsonSerializer.Deserialize<Payload>(File.ReadAllText(_path));
            if (loaded == null) return;

            _state = loaded;
            if (_state.Owed.Count > 0 || _state.SkipsUsed > 0)
            {
                Plugin.Logger.LogInfo(
                    $"run state: {_state.Owed.Count} check(s) still owed, "
                    + $"{_state.SkipsUsed} skip(s) used");
            }
        }
        catch (Exception e)
        {
            // A corrupt file must not stop the game starting. What it held is
            // lost, which is bad; a crash loop is worse.
            Plugin.Logger.LogWarning($"run state: could not read it: {e.Message}");
            _state = new Payload();
        }
    }

    internal static void End()
    {
        _path = null;
        _state = new Payload();
    }

    internal static IReadOnlyList<string> Owed() => _state.Owed;

    internal static void SetOwed(IReadOnlyList<string> owed)
    {
        _state.Owed = new List<string>(owed);
        Write();
    }

    internal static void SpendSkip()
    {
        _state.SkipsUsed++;
        Write();
    }

    /// <summary>
    /// Written whenever it changes, not on a timer: the moment worth surviving
    /// is the one just before an unexpected quit.
    /// </summary>
    private static void Write()
    {
        if (_path == null) return;
        try
        {
            // Through a temp file and moved into place, so a quit midway leaves
            // the previous state intact rather than half a file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state));
            File.Move(temp, _path, overwrite: true);
            Writes++;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"run state: could not write it: {e.Message}");
        }
    }
}
