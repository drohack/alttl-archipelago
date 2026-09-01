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
    [JsonPropertyName("isDailyTidy")] public bool IsDailyTidy { get; set; }

    [JsonPropertyName("controllers")] public List<ControllerInfo> Controllers { get; set; } = new();
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
