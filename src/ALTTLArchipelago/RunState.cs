using System;
using System.Collections.Generic;
using System.IO;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago;

/// <summary>
/// The disk half of <see cref="RunStateData"/>: where the file lives, when it
/// is written, and refusing to let either fail loudly.
///
/// Deliberately thin. What the run state CONTAINS, and whether a given call
/// changed anything worth writing, are decided in Core, where they are tested
/// without a game. This file only knows a path and an atomic write.
///
/// A sidecar file rather than a field in the game's save. The save is the
/// game's own format behind a character cipher, and adding a key would mean
/// writing that format correctly forever, for data the game has no interest
/// in. It is named after the run's save so two runs cannot read each other's.
/// </summary>
internal static class RunState
{
    private static string? _path;
    private static RunStateData _state = new();

    internal static int SkipsUsed => _state.SkipsUsed;
    internal static int TrapsSprung => _state.TrapsSprung;

    /// <summary>Have the credits been played in this run, ever?</summary>
    internal static bool CreditsPlayed => _state.CreditsPlayed;

    /// <summary>How many Hint Pages have been spent.</summary>
    internal static int HintPagesOpened => _state.HintPagesOpened;

    /// <summary>Has this page already been paid for?</summary>
    internal static bool IsHintPageOpen(string key) => _state.IsHintPageOpen(key);

    internal static IReadOnlyList<string> Owed() => _state.Owed;

    internal static IReadOnlyList<string> Beaten() => _state.Beaten;

    /// <summary>Record that the credits were played. Idempotent.</summary>
    internal static void NoteCreditsPlayed() => WriteIf(_state.NoteCreditsPlayed());

    /// <summary>
    /// Pay for a page. Returns false if it was already open, so the caller
    /// cannot double charge by calling twice.
    /// </summary>
    internal static bool OpenHintPage(string key)
    {
        var opened = _state.OpenHintPage(key);
        WriteIf(opened);
        return opened;
    }

    internal static void SetBeaten(IReadOnlyList<string> beaten)
        => WriteIf(_state.SetBeaten(beaten));

    internal static void SetOwed(IReadOnlyList<string> owed)
        => WriteIf(_state.SetOwed(owed));

    internal static void SpendSkip() => WriteIf(_state.SpendSkip());

    internal static void SpendTrap(int count) => WriteIf(_state.SpendTrap(count));

    internal static void Begin(string saveName)
    {
        _path = null;
        _state = new RunStateData();

        try
        {
            var dir = SaveRedirect.SaveDirectory();
            if (dir == null) return;
            _path = Path.Combine(dir, saveName + ".run.json");

            if (!File.Exists(_path)) return;

            var loaded = RunStateData.FromJson(File.ReadAllText(_path));
            if (loaded == null) return;

            _state = loaded;
            if (_state.HasProgress)
            {
                Plugin.Logger.LogInfo($"run state: {_state.Summary()}");
            }
        }
        catch (Exception e)
        {
            // A corrupt file must not stop the game starting. What it held is
            // lost, which is bad; a crash loop is worse.
            Plugin.Logger.LogWarning($"run state: could not read it: {e.Message}");
            _state = new RunStateData();
        }
    }

    internal static void End()
    {
        _path = null;
        _state = new RunStateData();
    }

    /// <summary>
    /// Write only when Core says something actually changed.
    ///
    /// The flush that calls SetOwed runs on a timer, so an offline session
    /// would otherwise sit re-saving an identical list every few seconds.
    /// </summary>
    private static void WriteIf(bool changed)
    {
        if (changed) Write();
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
            File.WriteAllText(temp, _state.ToJson());
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"run state: could not write it: {e.Message}");
        }
    }
}
