using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// The whole handoff from the generator to the game.
///
/// The mod cannot recompute the draw - which puzzle sits in which slot, with
/// which procedural seed, is decided once at generation time and never again.
/// So anything absent from this payload does not exist as far as the game is
/// concerned, and every field here is something the mod dispatches on.
///
/// It is a mirror of the apworld's fill_slot_data(), and mirrors drift. The
/// guard is docs/data/slot-data-example.json: the Python side writes a real
/// payload there and SlotDataTests parses that exact file, so a field renamed
/// on one side fails a test rather than reading as null in someone's game.
///
/// DEFAULTS MATTER. A server that predates a field, or a hand-edited payload,
/// leaves it missing rather than wrong - so every property defaults to the
/// same value the corresponding yaml option defaults to, and the mod behaves
/// sanely instead of dividing by a zero pack size.
/// </summary>
public sealed class SlotData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The run, in track order. Index IS the position on the track.</summary>
    [JsonPropertyName("slots")]
    public List<SlotEntry> Slots { get; set; } = new();

    /// <summary>Puzzles the first packs open. Later packs open more.</summary>
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; } = 4;

    /// <summary>How many Progressive Puzzle Pack items exist in the seed.</summary>
    [JsonPropertyName("pack_total")]
    public int PackTotal { get; set; }

    /// <summary>
    /// Cumulative puzzles open after each pack; element 0 is the free opening.
    ///
    /// Sent by the generator rather than recomputed here. The ramp that
    /// decides it is a gentlest-fit acceleration under a cap proportional to
    /// run length, and a second implementation of that in C# would eventually
    /// disagree - at which point the game unlocks a different set of puzzles
    /// than the logic assumed reachable, and a seed that generated cleanly
    /// cannot be finished.
    /// </summary>
    [JsonPropertyName("pack_boundaries")]
    public List<int> PackBoundaries { get; set; } = new();

    /// <summary>Puzzles to beat before the credits card unlocks.</summary>
    [JsonPropertyName("levels_to_beat")]
    public int LevelsToBeat { get; set; } = 40;

    /// <summary>When false, every mechanic works from the start.</summary>
    [JsonPropertyName("ability_locks")]
    public bool AbilityLocks { get; set; } = true;

    /// <summary>Ability name -> the ObjectController classes it unlocks.</summary>
    [JsonPropertyName("abilities")]
    public Dictionary<string, List<string>> Abilities { get; set; } = new();

    /// <summary>Abilities the player holds before any item arrives.</summary>
    [JsonPropertyName("starting_abilities")]
    public List<string> StartingAbilities { get; set; } = new();

    /// <summary>
    /// Location name -> what it needs. Each entry is the COMPLETE requirement
    /// for that location; a consumer must not AND it with anything else. A
    /// controller group is often solvable long before its level is, and
    /// combining the two would hide that.
    /// </summary>
    [JsonPropertyName("requirements")]
    public Dictionary<string, Requirement> Requirements { get; set; } = new();

    /// <summary>Percentage of filler that is the cat knocking things over.</summary>
    /// <summary>
    /// Controller GameObject name -> group display name, per level.
    ///
    /// Sent by the generator rather than derived here. The grouping rule merges
    /// mutually-dependent controllers into a single checkable unit, and that
    /// decision is what a location IS - two implementations of it would
    /// eventually disagree about whether one event or two is a check.
    /// </summary>
    [JsonPropertyName("controller_groups")]
    public Dictionary<string, Dictionary<string, string>> ControllerGroups { get; set; } = new();

    [JsonPropertyName("cat_trap_chance")]
    public int CatTrapChance { get; set; } = 10;

    /// <summary>
    /// A short, stable identifier for THIS seed, derived from its contents.
    ///
    /// Exists because the server's own seed string could not be relied on:
    /// ArchipelagoSession.RoomState.Seed came back empty on every connection
    /// tested here, which left every multiworld played under one slot name
    /// sharing a single save file. The draw is unique per seed - which puzzles,
    /// in which order, with which procedural seeds - so hashing it identifies
    /// the run just as well and needs nothing from the server.
    ///
    /// Only fields fixed at generation are used, so the value does not change
    /// as the run is played.
    /// </summary>
    public string Fingerprint()
    {
        unchecked
        {
            uint hash = 2166136261;

            void Feed(string value)
            {
                foreach (var c in value) hash = (hash ^ c) * 16777619;
                hash = (hash ^ '|') * 16777619;
            }

            Feed(Slots.Count.ToString());
            Feed(PackTotal.ToString());
            Feed(LevelsToBeat.ToString());
            foreach (var slot in Slots)
            {
                Feed(slot.LevelId);
                Feed(slot.Instance.ToString());
                Feed(slot.Seed.ToString());
            }

            return hash.ToString("x8");
        }
    }

    public static SlotData FromJson(string json)
        => JsonSerializer.Deserialize<SlotData>(json, JsonOptions) ?? new SlotData();

    /// <summary>
    /// Whether the payload is coherent enough to start a run on.
    ///
    /// Deliberately narrow: it catches a payload that would make the mod
    /// misbehave silently, not one that is merely unusual. A run with no slots
    /// has nothing to show; a pack size of zero divides by zero; a level that
    /// needs more packs than the seed contains can never be reached.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (Slots.Count == 0) problems.Add("no slots");
        if (PackSize < 1) problems.Add($"pack_size is {PackSize}");

        if (PackBoundaries.Count == 0)
        {
            problems.Add("no pack_boundaries - the mod cannot tell which "
                + "puzzles a pack opens");
        }
        else
        {
            // Holding every pack must open every slot. One short strands the
            // end of the track behind an item that does not exist.
            if (PackBoundaries[PackBoundaries.Count - 1] != Slots.Count)
            {
                problems.Add(
                    $"pack_boundaries end at {PackBoundaries[PackBoundaries.Count - 1]}"
                    + $" but the run has {Slots.Count} slots");
            }
            if (PackBoundaries.Count - 1 != PackTotal)
            {
                problems.Add($"{PackBoundaries.Count - 1} pack steps but "
                    + $"pack_total is {PackTotal}");
            }
        }
        if (LevelsToBeat > Slots.Count)
        {
            problems.Add($"levels_to_beat {LevelsToBeat} exceeds {Slots.Count} slots");
        }

        // Every level in the run needs its controller map, or every group check
        // on it is silently unreportable: the solved event arrives, nothing
        // matches, and no location is ever sent. Reported here because that
        // failure is completely invisible while playing.
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var slot in Slots)
        {
            if (!string.IsNullOrEmpty(slot.LevelId)
                && !ControllerGroups.ContainsKey(slot.LevelId))
            {
                missing.Add(slot.LevelId);
            }
        }
        if (missing.Count > 0)
        {
            problems.Add($"{missing.Count} level(s) have no controller_groups entry: "
                + string.Join(", ", missing.Take(5)));
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            if (string.IsNullOrWhiteSpace(slot.LevelId)) problems.Add($"slot {i} has no levelId");
            if (slot.LevelIndex < 0) problems.Add($"slot {i} has levelIndex {slot.LevelIndex}");
        }

        foreach (var (name, requirement) in Requirements)
        {
            if (requirement.Packs > PackTotal)
            {
                problems.Add($"{name} needs {requirement.Packs} packs, seed has {PackTotal}");
            }
            foreach (var ability in requirement.Abilities)
            {
                // An ability gating a location but absent from the catalogue
                // would be unobtainable - the location could never be checked.
                if (AbilityLocks && !Abilities.ContainsKey(ability))
                {
                    problems.Add($"{name} needs unknown ability '{ability}'");
                }
            }
        }

        return problems;
    }
}

/// <summary>One card in the run.</summary>
public sealed class SlotEntry
{
    [JsonPropertyName("levelId")]
    public string LevelId { get; set; } = "";

    /// <summary>The game's own level index, for LevelManager.StartLevel.</summary>
    [JsonPropertyName("levelIndex")]
    public int LevelIndex { get; set; } = -1;

    /// <summary>1-based. Only generators ever appear more than once.</summary>
    [JsonPropertyName("instance")]
    public int Instance { get; set; } = 1;

    /// <summary>generator | archive | base.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Procedural seed for a generator slot, or -1 for a fixed level.
    ///
    /// -1 is not "no seed given", it is "do not force one" - passing it to
    /// StartLevel on a hand-made level would be meaningless.
    /// </summary>
    [JsonPropertyName("seed")]
    public long Seed { get; set; } = -1;

    [JsonIgnore]
    public bool IsGenerator => Source == "generator";
}

/// <summary>What one location needs, complete.</summary>
public sealed class Requirement
{
    /// <summary>Progressive Puzzle Packs held. 0 means the free opening.</summary>
    [JsonPropertyName("packs")]
    public int Packs { get; set; }

    [JsonPropertyName("abilities")]
    public List<string> Abilities { get; set; } = new();
}
