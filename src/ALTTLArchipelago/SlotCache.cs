using System;
using System.IO;
using ALTTLArchipelago.Core;

namespace ALTTLArchipelago;

/// <summary>
/// The disk half of <see cref="CachedSession"/>: where the file lives, when it
/// is written, and refusing to let either fail loudly.
///
/// Deliberately thin. What a cache CONTAINS, whether it is usable and whether
/// it belongs to the configured slot are all decided in Core, where they are
/// tested without a game. This file only knows a path and a debounce.
///
/// It sits beside the run files, in the game's own save folder, because that
/// is where everything else belonging to a run already is - and because a
/// player moving their saves to another machine should carry their offline
/// run with them without being told to.
/// </summary>
internal static class SlotCache
{
    /// <summary>
    /// One file, not one per seed: an offline start has to pick a run before
    /// it can know a seed, and "the last one" is the only answer that needs no
    /// interface. The name is not per-slot for the same reason.
    /// </summary>
    private const string FileName = "alttl-last-session.json";

    /// <summary>
    /// Seconds between writes at most.
    ///
    /// The write is triggered by the item list changing, and a reconnect
    /// replays the entire item list one item at a time - a couple of hundred
    /// of them, in a burst, every single login. Writing per item would put
    /// hundreds of serialisations of the whole draw on the disk to end up at
    /// the state it already had.
    /// </summary>
    private const float WriteEvery = 2f;

    private static bool _dirty;
    private static float _cooldown;

    /// <summary>What to write, supplied by Plugin so this file holds no state.</summary>
    internal static Func<CachedSession?>? Source;

    private static string? Path_()
    {
        var dir = SaveRedirect.SaveDirectory();
        return dir == null ? null : System.IO.Path.Combine(dir, FileName);
    }

    /// <summary>Something changed; write soon. Cheap enough to call per item.</summary>
    internal static void MarkDirty() => _dirty = true;

    /// <summary>
    /// Write now, ignoring the debounce. For the moments that are worth a
    /// write on their own - a run starting, a session ending.
    /// </summary>
    internal static void Flush()
    {
        _dirty = false;
        _cooldown = WriteEvery;

        var cache = Source?.Invoke();
        if (cache == null) return;

        // A cache that cannot be played is not worth keeping, and writing one
        // would overwrite a good cache with a useless one - which is how a
        // player loses offline access to a run that was working.
        var problems = cache.Problems();
        if (problems.Count > 0)
        {
            Plugin.Logger.LogWarning(
                $"session cache: not saving, {string.Join("; ", problems)}");
            return;
        }

        var path = Path_();
        if (path == null) return;

        try
        {
            // Temp file then move, as RunState does: a quit midway leaves the
            // previous cache intact rather than half a file. Half a file is
            // survivable here - CachedSession.FromJson returns null - but the
            // player still loses a run they could have kept playing.
            var temp = path + ".tmp";
            File.WriteAllText(temp, cache.ToJson());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"session cache: could not write it: {e.Message}");
        }
    }

    /// <summary>Called once a frame. Writes at most every WriteEvery seconds.</summary>
    internal static void Tick(float dt)
    {
        if (_cooldown > 0f) _cooldown -= dt;
        if (!_dirty || _cooldown > 0f) return;
        Flush();
    }

    /// <summary>
    /// The last session, or null if there is not one worth playing.
    ///
    /// Every failure here is a null and a log line. A cache is a convenience;
    /// no possible state of this file justifies keeping the player out of
    /// their game.
    /// </summary>
    internal static CachedSession? Load()
    {
        var path = Path_();
        if (path == null || !File.Exists(path)) return null;

        try
        {
            var cache = CachedSession.FromJson(File.ReadAllText(path));
            if (cache == null)
            {
                Plugin.Logger.LogWarning("session cache: unreadable, ignoring it");
                return null;
            }
            return cache;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"session cache: could not read it: {e.Message}");
            return null;
        }
    }

    /// <summary>Stop writing. The file stays: it is the next launch's run.</summary>
    internal static void End()
    {
        _dirty = false;
        _cooldown = 0f;
    }

    // There is deliberately no Forget(). Deleting the cache on Disconnect was
    // written first and was wrong twice over: Unload() calls DisconnectNow(),
    // so every normal game exit would have deleted the file this feature
    // exists to write - and even without that, Disconnect is not a durable
    // "leave the multiworld". AutoConnect rejoins on the next launch anyway,
    // so an offline start that also comes back is the consistent behaviour,
    // not a surprise.
}
