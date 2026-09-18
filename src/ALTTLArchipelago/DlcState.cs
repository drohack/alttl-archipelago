using System.Collections.Generic;

namespace ALTTLArchipelago;

/// <summary>
/// Which DLC the player owns, read on the MAIN THREAD and remembered.
///
/// WHY THIS IS NOT READ WHERE IT IS USED. The answer is wanted at connect, to
/// refuse a seed built with a DLC that is not installed - and Connection runs
/// its login inside a Task.Run, with its own docstring saying "never touch
/// Unity from here". Reading GameManager.Instance.DLCManager on that thread
/// threw NullReferenceException every time, and because the read fails open
/// the only symptom was one warning line and a guard that silently stopped
/// guarding:
///
///     could not read which DLC is installed (NullReferenceException);
///     the seed's DLC requirement will not be checked
///
/// So the value is collected here, from Update, and the connect path reads
/// what was collected.
///
/// POLLED RATHER THAN READ ONCE, because the manager is not ready at startup:
/// AuthenticateAllDLCs is a coroutine, and with AutoConnect the login begins
/// while the title screen is still coming up. Polling stops as soon as it gets
/// an answer, so the steady-state cost is one null check per frame.
/// </summary>
internal static class DlcState
{
    /// <summary>The DLC keys the game reports installed, or null if unknown.</summary>
    internal static IReadOnlyCollection<string>? Installed { get; private set; }

    /// <summary>Seconds between attempts while the answer is still unknown.</summary>
    private const float Interval = 1.0f;

    private static float _next;

    internal static void Tick(float deltaSeconds)
    {
        if (Installed != null) return;             // already answered

        _next -= deltaSeconds;
        if (_next > 0f) return;
        _next = Interval;

        var keys = Read();
        if (keys == null) return;                  // not ready; try again

        Installed = keys;
        Plugin.Logger.LogInfo(
            "DLC installed: " + (keys.Count == 0 ? "none" : string.Join(", ", keys)));
    }

    /// <summary>
    /// The installed keys, or null while the game cannot answer.
    ///
    /// Null is "ask again", never "the player owns none" - those mean opposite
    /// things to the connect guard, and returning a short or empty list early
    /// would refuse seeds the player can actually play.
    /// </summary>
    private static IReadOnlyCollection<string>? Read()
    {
        try
        {
            var manager = GameManager.Instance == null
                ? null : GameManager.Instance.DLCManager;
            if (manager == null) return null;

            var info = manager.DLCInfo;
            if (info == null || info.Count == 0) return null;

            var keys = new List<string>();
            for (int i = 0; i < info.Count; i++)
            {
                var d = info[i];
                if (d != null && d.Installed && !string.IsNullOrEmpty(d.key))
                    keys.Add(d.key);
            }
            return keys;
        }
        catch
        {
            // Quiet on purpose. This runs every second until it succeeds, and
            // a warning per attempt would bury the log in the ordinary case
            // where the manager simply is not up yet.
            return null;
        }
    }

    /// <summary>Forget the answer, so a reload asks the game again.</summary>
    internal static void Reset()
    {
        Installed = null;
        _next = 0f;
    }
}
