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

    /// <summary>
    /// True for a controller the game registers but never reports solved, so a
    /// check on it could never be sent. It still gates: its ability counts for
    /// every group that depends on it. Set only from play (DLC1 Kitchen Utensils
    /// Drawers: the level ends before both drawers can be shut).
    /// </summary>
    [JsonPropertyName("notALocation")] public bool NotALocation { get; set; }
}

/// <summary>
/// One drawer or cupboard, and what it gates.
///
/// WHY THIS IS NOT JUST ANOTHER dependsOn EDGE. A drawer's contents are not an
/// ObjectController.dependencies relationship - the game authors them on the
/// Drawer component instead, as UnlockOnSolvedControllers and
/// OpenOnSolvedControllers. The sweep has always harvested that, and
/// tools/merge-levels.py threw it away as a field "nothing downstream reads".
/// It was not read because it was never kept, and the cost was a dead run on
/// 2026-09-21: every group inside every drawer was recorded as needing
/// nothing, so the generator put a Progressive Puzzle Pack behind a shut
/// drawer.
///
/// Empty on a level with no drawers, and empty on a row swept before this
/// field existed - so absent means "not measured", not "no drawers".
/// </summary>
public sealed class DrawerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>How many objects sit inside. A count, for sanity-checking
    /// <see cref="ContainsControllers"/> against.</summary>
    [JsonPropertyName("contains")] public int Contains { get; set; }

    /// <summary>
    /// The controllers whose objects are inside this drawer.
    ///
    /// Resolved from instance ids to names inside DevTools, while both lists
    /// are live - see DataTable.ContainedControllersOf. Ids would not survive
    /// the trip out of the process.
    /// </summary>
    [JsonPropertyName("containsControllers")]
    public List<string> ContainsControllers { get; set; } = new();

    /// <summary>
    /// Solve these and the drawer unlocks, as "controller#solutionId".
    ///
    /// The solution id is part of it: a multi-solution controller can open a
    /// drawer on one arrangement and not another, so the name alone would lose
    /// that. Anything wanting the controller takes the part before the '#'.
    /// </summary>
    [JsonPropertyName("unlockOn")] public List<string> UnlockOn { get; set; } = new();

    /// <summary>Solve these and the drawer opens. Plain controller names.</summary>
    [JsonPropertyName("openOn")] public List<string> OpenOn { get; set; } = new();

    [JsonPropertyName("subDrawers")] public int SubDrawers { get; set; }
}

public sealed class LevelInfo
{
    [JsonPropertyName("levelIndex")] public int LevelIndex { get; set; }
    [JsonPropertyName("levelId")] public string LevelId { get; set; } = "";

    /// <summary>
    /// Which pool the level is drawn from: "generator", "archive", "base",
    /// "dlc1" or "dlc2".
    /// </summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    /// <summary>
    /// The DLC this level needs ("DLC1", "DLC2"), or empty for base content.
    ///
    /// SEPARATE FROM Source ON PURPOSE, and the four levels where they
    /// disagree are the reason. DLC1 Trophy Cabinet, DLC2 Water Glasses,
    /// Figurines and Bread Crusts all carry the game's randomizer flag, so
    /// their source is "generator" - they repeat with a fresh seed like any
    /// other generator. Source alone would therefore say nothing about
    /// whether a player needs to own Seeing Stars to play them, and a seed
    /// built on that would hand someone a level that cannot load.
    ///
    /// Empty rather than null so a table written before this field existed
    /// reads as base content, which is what it was.
    /// </summary>
    [JsonPropertyName("dlc")] public string Dlc { get; set; } = "";

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
    /// The level's drawers and cupboards, and what each one gates.
    ///
    /// See <see cref="DrawerInfo"/>. An empty list means either "no drawers"
    /// or "swept before this was kept", and the two are not distinguishable
    /// from here - which is why the generator treats a level holding a drawer
    /// with no recorded contents as suspect rather than as settled.
    /// </summary>
    [JsonPropertyName("drawers")] public List<DrawerInfo> Drawers { get; set; } = new();

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
