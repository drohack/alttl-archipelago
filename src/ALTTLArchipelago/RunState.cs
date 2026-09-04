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

        /// <summary>
        /// Traps that have already gone off.
        ///
        /// Archipelago replays the whole item list on every reconnect, so
        /// without this every cat you have ever been sent fires again the
        /// moment you log back in - and since that happens before a puzzle is
        /// open, they are all spent as misses and a genuinely new trap has
        /// nothing left to do.
        /// </summary>
        [JsonPropertyName("trapsSprung")]
        public int TrapsSprung { get; set; }

        /// <summary>
        /// Event locations we have collected - the Beaten tokens.
        ///
        /// The only checks nothing else can restore. They have no address, so
        /// the server never lists them back at login, and they are never owed
        /// because sending one can only ever be rejected. Both of the things
        /// that rebuild a ledger therefore miss them.
        ///
        /// Without this the beaten count returned to zero on every login, so
        /// the credits goal could only be reached inside one unbroken session -
        /// and the track's badges forgot which puzzles were finished.
        /// </summary>
        [JsonPropertyName("beaten")]
        public List<string> Beaten { get; set; } = new();
    }

    private static string? _path;
    private static Payload _state = new();

    /// <summary>Counts writes, so a test can assert persistence actually ran.</summary>
    internal static int Writes { get; private set; }

    internal static int SkipsUsed => _state.SkipsUsed;
    internal static int TrapsSprung => _state.TrapsSprung;

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
            if (_state.Owed.Count > 0 || _state.SkipsUsed > 0 || _state.TrapsSprung > 0
                || _state.Beaten.Count > 0)
            {
                Plugin.Logger.LogInfo(
                    $"run state: {_state.Owed.Count} check(s) still owed, "
                    + $"{_state.SkipsUsed} skip(s) used, "
                    + $"{_state.TrapsSprung} trap(s) already sprung, "
                    + $"{_state.Beaten.Count} puzzle(s) beaten");
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

    internal static IReadOnlyList<string> Beaten() => _state.Beaten;

    internal static void SetBeaten(IReadOnlyList<string> beaten)
    {
        if (Same(_state.Beaten, beaten)) return;

        _state.Beaten = new List<string>(beaten);
        Write();
    }

    internal static void SetOwed(IReadOnlyList<string> owed)
    {
        // Skip a write that would change nothing. The flush that calls this
        // runs on a timer, so an offline session sits re-saving the same list
        // every few seconds otherwise.
        if (Same(_state.Owed, owed)) return;

        _state.Owed = new List<string>(owed);
        Write();
    }

    private static bool Same(List<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    internal static void SpendSkip()
    {
        _state.SkipsUsed++;
        Write();
    }

    internal static void SpendTrap(int count)
    {
        if (count <= 0) return;
        _state.TrapsSprung += count;
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
