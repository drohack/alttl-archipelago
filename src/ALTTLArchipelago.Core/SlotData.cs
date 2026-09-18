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
/// guard is fixtures/slot-data-example.json: the Python side writes a real
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

    /// <summary>
    /// The apworld version that generated this seed, as "major.minor.build".
    ///
    /// THE ONLY THING THAT CAN CATCH A MISMATCHED PAIR. The mod and the
    /// apworld ship together and must agree about the item table - location
    /// ids move between releases, so a 0.3.1 mod playing a 0.3.2 seed sends
    /// the wrong checks and reads the wrong names, quietly. check-version.py
    /// enforces the pact INSIDE THE REPO, between three files in one commit;
    /// nothing enforced it between two installs on a player's disk, and the
    /// README said as much: "nothing detects that at runtime".
    ///
    /// Empty for a seed generated before this field existed. That is not a
    /// mismatch - it is an unknown - so it is reported and allowed. Refusing
    /// would strand runs that are very probably fine.
    /// </summary>
    [JsonPropertyName("world_version")]
    public string WorldVersion { get; set; } = "";

    /// <summary>The run, in track order. Index IS the position on the track.</summary>
    [JsonPropertyName("slots")]
    public List<SlotEntry> Slots { get; set; } = new();

    /// <summary>Puzzles each pack opens. Every pack opens the same number.</summary>
    /// <remarks>
    /// 5, matching PackSize.default in options.py - and 4 is not merely stale,
    /// it is unreachable: items.MIN_OPENING floors a real payload at 5. This
    /// default only applies when the key is MISSING, which is exactly the
    /// degraded case the class exists to survive, and it feeds TrackState's
    /// no-boundaries fallback - so it decides how much of the track opens in
    /// the one situation where nothing else can.
    /// </remarks>
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; } = 5;

    /// <summary>How many Progressive Puzzle Pack items exist in the seed.</summary>
    [JsonPropertyName("pack_total")]
    public int PackTotal { get; set; }

    /// <summary>
    /// Cumulative puzzles open after each pack; element 0 is the free opening.
    ///
    /// Sent by the generator rather than recomputed here. Every block is the
    /// same width, but WHICH width depends on a cap proportional to run length
    /// that can raise the requested size, and the last block is whatever
    /// remainder is left - so a second implementation of that in C# would
    /// eventually disagree, at which point the game unlocks a different set of
    /// puzzles than the logic assumed reachable, and a seed that generated
    /// cleanly cannot be finished.
    /// </summary>
    [JsonPropertyName("pack_boundaries")]
    public List<int> PackBoundaries { get; set; } = new();

    /// <summary>
    /// What unlocks the credits: "beat_levels" or "star_levels".
    ///
    /// A STRING, matching the payload, rather than the option's 0 and 1.
    /// The mod dispatches on it and so does a human reading a slot_data
    /// dump; a name that reads the same on both sides is one fewer mapping
    /// to keep in step.
    ///
    /// Defaults to beating, which is both the option default and what every
    /// seed generated before this existed meant - an older server's payload
    /// carries no "goal" key at all and must degrade to the old behaviour.
    /// </summary>
    [JsonPropertyName("goal")]
    public string Goal { get; set; } = "beat_levels";

    /// <summary>True when the run's goal is starring rather than beating.</summary>
    [JsonIgnore]
    public bool GoalIsStars
        => string.Equals(Goal, "star_levels", StringComparison.Ordinal);

    /// <summary>Puzzles to beat before the credits card unlocks.</summary>
    [JsonPropertyName("levels_to_beat")]
    public int LevelsToBeat { get; set; } = 40;

    /// <summary>
    /// Puzzles to STAR before the credits card unlocks - every check on the
    /// puzzle, not just finishing it. Only used when Goal is star_levels.
    /// </summary>
    [JsonPropertyName("levels_to_star")]
    public int LevelsToStar { get; set; } = 20;

    /// <summary>
    /// How many puzzles the goal actually wants, whichever goal it is.
    ///
    /// Here rather than at each call site because three places used to read
    /// LevelsToBeat directly - the credits gate, the beaten toast and the
    /// offline summary - and a fourth would have been written before anyone
    /// noticed the first three disagreed with the goal.
    /// </summary>
    [JsonIgnore]
    public int GoalTarget => GoalIsStars ? LevelsToStar : LevelsToBeat;

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

    /// <summary>Percentage of filler that is the cat knocking things over.</summary>
    [JsonPropertyName("cat_trap_chance")]
    public int CatTrapChance { get; set; } = 25;   // CatTrapChance.default

    /// <summary>
    /// Whether this seed contains Cupboards and Drawers puzzles.
    ///
    /// Named after its yaml option rather than after the DLC key, like every
    /// other field here - pack_size is PackSize, cat_trap_chance is
    /// CatTrapChance. check-slot-defaults.py pairs the two BY NAME, so a field
    /// called dlc1 would be a default that nothing compares.
    /// </summary>
    [JsonPropertyName("cupboards_and_drawers")]
    public bool CupboardsAndDrawers { get; set; } = false;

    /// <summary>Whether this seed contains Seeing Stars puzzles.</summary>
    [JsonPropertyName("seeing_stars")]
    public bool SeeingStars { get; set; } = false;

    /// <summary>
    /// The DLC keys this seed needs, from the two flags above.
    ///
    /// Read the FLAGS rather than scanning the slots, deliberately. A seed
    /// generated with a DLC enabled can draw none of its levels by chance, and
    /// it is still a seed built for a player who owns it - refusing to say so
    /// would let the same yaml connect on one machine and not another
    /// depending on the roll.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> RequiredDlc
    {
        get
        {
            var keys = new List<string>();
            if (CupboardsAndDrawers) keys.Add("DLC1");
            if (SeeingStars) keys.Add("DLC2");
            return keys;
        }
    }

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
    ///
    /// GOAL AND LEVELS_TO_STAR ARE DELIBERATELY NOT FED, and an audit flagged
    /// their absence, so the reasoning is here rather than waiting to be
    /// rediscovered. Two seeds with an identical draw and different goals do
    /// collide on one save file. That is close to harmless - the draw is the
    /// same run, and switching goal mode keeps your progress rather than
    /// starting you over, which is arguably the better behaviour. Against
    /// that, feeding them would change the hash for EVERY existing seed and
    /// orphan the save of every run in progress.
    ///
    /// If a future change makes the collision actually harmful, the cost of
    /// fixing it is that one-time orphaning, and it should be taken at a
    /// release boundary with a note in the changelog.
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
    /// Whether this seed was generated by a different apworld than the mod
    /// playing it. Null when they agree, or when the seed cannot say.
    /// </summary>
    /// <param name="modVersion">
    /// The mod's own version, "major.minor.build". The caller reads it from
    /// the running assembly rather than a constant, so it cannot drift from
    /// what was actually built.
    /// </param>
    /// <remarks>
    /// EXACT MATCH, deliberately. It is tempting to allow a patch-level
    /// difference, but this project moves location ids in patch releases -
    /// 0.3.1 to 0.3.2 moved them - so "same major.minor" would wave through
    /// precisely the pairing that breaks. The release notes already tell
    /// players a seed does not survive a version change; this makes the game
    /// say it instead of misbehaving quietly.
    /// </remarks>
    public string? VersionMismatch(string modVersion)
    {
        if (string.IsNullOrEmpty(WorldVersion)) return null;
        if (string.IsNullOrEmpty(modVersion)) return null;
        if (string.Equals(WorldVersion, modVersion, StringComparison.Ordinal))
        {
            return null;
        }

        return $"this seed was generated by apworld {WorldVersion} and this "
             + $"mod is {modVersion}. They disagree about the item table, so "
             + $"the run would send the wrong checks. Install the matching "
             + $"pair - both ship in the same release.";
    }

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
        if (LevelsToStar > Slots.Count)
        {
            problems.Add($"levels_to_star {LevelsToStar} exceeds {Slots.Count} slots");
        }
        // Checked even though only one goal is in use: the generator clamps
        // both, so a count over the slot total means the payload did not come
        // from a generator that agrees with this build.
        if (Goal != "beat_levels" && Goal != "star_levels")
        {
            problems.Add($"goal '{Goal}' is not one this build knows");
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

    /// <summary>generator | archive | base | dlc1 | dlc2.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// The DLC this level needs ("DLC1", "DLC2"), or empty for base content.
    ///
    /// NOT derivable from Source: four DLC levels carry the game's own
    /// randomizer flag, so their source is "generator" while they still need
    /// the DLC installed. Empty by default, so a payload generated before
    /// this field existed reads as base content - which is what it was.
    /// </summary>
    [JsonPropertyName("dlc")]
    public string Dlc { get; set; } = "";

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
