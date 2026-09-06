using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Everything the server told us last time, so the run can start without it.
///
/// The problem this solves: an unreachable server at launch used to mean no
/// run at all. Ready never fired, so the track never went up, and the player
/// got the vanilla game with a finished campaign sitting in a save they could
/// not reach. A mid-session drop, meanwhile, keeps playing perfectly well. The
/// two cases disagreed for no reason.
///
/// TWO THINGS ARE CACHED, and the second is easy to miss. slot_data alone
/// brings the run up with zero packs and no abilities - a locked track, which
/// is worse than no track. The received ITEM LIST is what makes an offline
/// start the same run the player left.
///
/// Caching items is safe because of a property the mod already relies on:
/// Inventory counts the whole list from scratch on every change rather than
/// reacting to arrivals, and the session boundary clears the list before the
/// socket opens. So when a server is finally reached it REPLACES this list
/// rather than adding to it - the same property that fixed pack doubling.
///
/// Only the last session is kept. An offline start has to choose a run before
/// it can know a seed, and "the one you were just playing" is the only answer
/// that needs no interface. Playing a second seed replaces this file; going
/// back to the first means connecting to its server once.
/// </summary>
public sealed class CachedSession
{
    /// <summary>
    /// Trailing commas and unknown fields tolerated on read, because this file
    /// outlives the version that wrote it. A cache written by an older mod
    /// must degrade to "cannot use it" at worst, never to a crash on launch.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false,
    };

    /// <summary>The slot this run was played under.</summary>
    [JsonPropertyName("slot_name")]
    public string SlotName { get; set; } = "";

    /// <summary>
    /// The seed, exactly as <c>SaveRedirect</c> used it.
    ///
    /// Stored rather than recomputed, and that is the whole safety argument
    /// for this file. The save is named save_ap_(slot)_(seed), so replaying
    /// this seed reopens THAT save and no other. A seed regenerated under the
    /// same slot name draws differently, so its fingerprint differs, so it
    /// gets its own save - a stale cache can therefore be wrong about the
    /// CONTENT of a run, but it cannot write into a run it does not belong to.
    /// </summary>
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "";

    /// <summary>When this was written, for the log and the pane. Not logic.</summary>
    [JsonPropertyName("saved_at")]
    public string SavedAt { get; set; } = "";

    /// <summary>
    /// Every item name received, duplicates included.
    ///
    /// A list rather than counts: Inventory derives counts from names, and
    /// storing the derived form here would mean a second implementation of
    /// that derivation, free to drift from the tested one in Core.
    /// </summary>
    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = new();

    /// <summary>The draw itself.</summary>
    [JsonPropertyName("slot_data")]
    public SlotData Slot { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Null when the text is not a cache at all, rather than throwing.</summary>
    public static CachedSession? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedSession>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Why this cache cannot be played, or empty if it can.
    ///
    /// Checked before a run is started from it, never after: an offline start
    /// that half-succeeds is the failure mode worth avoiding, because the
    /// player cannot tell a broken cache from a broken game.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(SlotName)) problems.Add("no slot name");
        if (string.IsNullOrWhiteSpace(Seed)) problems.Add("no seed");
        foreach (var p in Slot.Problems()) problems.Add(p);
        return problems;
    }

    /// <summary>
    /// Is this cache the run the player is currently configured for?
    ///
    /// The slot name is the only thing that can be compared before connecting:
    /// the seed is not knowable until a server answers. So a player who edits
    /// SlotName gets no offline start rather than someone else's run - and
    /// changing the name is exactly how a player says "different multiworld".
    ///
    /// Ordinal and case-sensitive, matching how Archipelago itself treats slot
    /// names. A cache that will not match reads as "no cached run", which is
    /// the honest answer.
    /// </summary>
    public bool IsFor(string slotName)
        => !string.IsNullOrWhiteSpace(slotName)
           && string.Equals(SlotName, slotName, StringComparison.Ordinal);

    /// <summary>
    /// A cache of what is held right now. Copies the list: the caller's is
    /// live and will keep changing.
    /// </summary>
    public static CachedSession Of(string slotName, string seed, SlotData slot,
                                   IReadOnlyList<string> items, DateTime now)
        => new()
        {
            SlotName = slotName,
            Seed = seed,
            Slot = slot,
            Items = new List<string>(items),
            SavedAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
}
