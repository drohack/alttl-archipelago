using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// One ObjectController as the game registers it at runtime.
///
/// Note "as the game registers it at runtime" - the set of controllers on a
/// loaded prefab is not the same as the set in Level.objectControllers once
/// the level is running (MedicineCabinet: 14 vs 13), and only the registered
/// ones raise GameEvent_ObjectControllerSolved. The data table is generated
/// from the runtime set for exactly that reason.
/// </summary>
public sealed class ControllerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("objects")] public int Objects { get; set; }

    /// <summary>Names of controllers that must be solved before this one.</summary>
    [JsonPropertyName("dependsOn")] public List<string> DependsOn { get; set; } = new();
}

public sealed class LevelInfo
{
    [JsonPropertyName("levelIndex")] public int LevelIndex { get; set; }
    [JsonPropertyName("levelId")] public string LevelId { get; set; } = "";

    /// <summary>"generator", "archive" or "base".</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    [JsonPropertyName("solutionCount")] public int SolutionCount { get; set; }
    [JsonPropertyName("isRandomizable")] public bool IsRandomizable { get; set; }
    [JsonPropertyName("isArchived")] public bool IsArchived { get; set; }

    /// <summary>In the game's everyday daily-tidy rotation. 16 levels.</summary>
    [JsonPropertyName("isDailyTidy")] public bool IsDailyTidy { get; set; }

    /// <summary>
    /// A seasonal daily, pinned to calendar dates. 20 levels.
    ///
    /// Separate from <see cref="IsDailyTidy"/> because the game keeps them
    /// apart, and anything asking "will finishing this drop the player on the
    /// Daily page" has to ask BOTH - see <see cref="InDailyPool"/>.
    /// </summary>
    [JsonPropertyName("isHolidayDaily")] public bool IsHolidayDaily { get; set; }

    /// <summary>
    /// The question that actually matters: 36 of the 111 levels.
    ///
    /// Exists so no caller has to remember that there are two flags. Getting
    /// that wrong is not hypothetical - the mod's daily guard was written
    /// against a believed six levels, shipped, and let the player out of the
    /// run three times before anyone counted.
    /// </summary>
    [JsonIgnore] public bool InDailyPool => IsDailyTidy || IsHolidayDaily;

    /// <summary>
    /// The game's own C# class for this level - "Level" for the ordinary ones,
    /// "RadialDanceParty", "TupperwareNestingLevel", "PawPrintsPhaseLevel" and
    /// so on for the bespoke ones.
    ///
    /// One string that names every unusual level for free. Nothing recorded it
    /// until 2026-09-09, which is why "is this level phased" was answered three
    /// times by counting controllers and guessing.
    /// </summary>
    [JsonPropertyName("levelClass")] public string LevelClass { get; set; } = "Level";

    /// <summary>
    /// The controllers this level reveals in order, as the game declares them.
    ///
    /// Empty for all but three levels in the game. Read from
    /// PhasedLevel.phases, TupperwareNesting.GetPhaseControllers() and
    /// RadialDanceParty.dances - authored data, not inference. A level with a
    /// non-empty list hands out its groups one at a time, so a group late in
    /// the list cannot be reached until the earlier ones are solved.
    /// </summary>
    [JsonPropertyName("phases")] public List<string> Phases { get; set; } = new();

    [JsonPropertyName("controllers")] public List<ControllerInfo> Controllers { get; set; } = new();

    /// <summary>
    /// Abilities the level needs that its REGISTERED controllers do not reveal.
    ///
    /// The sweep that builds this table boots a level and reads the controllers
    /// that have registered. A level which reveals its controllers PHASE BY
    /// PHASE therefore records only its first phase - TupperwareNesting lists
    /// two and reaches seven in play - and the ability union taken from that
    /// list is short by whatever the later phases need.
    ///
    /// That is not a cosmetic gap. A solution location requires the level's
    /// whole union (rules.py), so an under-recorded level looks finishable
    /// without an ability it genuinely needs, and the generator may place
    /// progression behind it. TupperwareNesting really does need Grids: its
    /// Layout (Grid) controller was seen registering in a later phase.
    ///
    /// Listed here rather than by adding the controllers themselves, because
    /// the two questions are different. Adding a controller also mints a PART
    /// location, and a part location for a group that never registers can
    /// never be earned - the prefab survey cannot tell a phased controller
    /// from one that exists only in the prefab. Recording the ability alone
    /// fixes the lockout, changes no location name, and so does not move a
    /// single location id.
    ///
    /// The direction of the risk decides the doubt: an ability listed here in
    /// error makes a seed slightly tighter, while one missing can make it
    /// unwinnable.
    /// </summary>
    [JsonPropertyName("extraAbilities")] public List<string> ExtraAbilities { get; set; } = new();
}

public sealed class LevelTable
{
    [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
    [JsonPropertyName("sweepSeed")] public int SweepSeed { get; set; }
    [JsonPropertyName("levels")] public List<LevelInfo> Levels { get; set; } = new();

    public static LevelTable FromJson(string json)
        => JsonSerializer.Deserialize<LevelTable>(json) ?? new LevelTable();

    public LevelInfo? ByIndex(int levelIndex)
        => Levels.FirstOrDefault(l => l.LevelIndex == levelIndex);

    public LevelInfo? ById(string levelId)
        => Levels.FirstOrDefault(l => l.LevelId == levelId);
}
