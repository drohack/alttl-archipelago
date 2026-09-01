namespace ALTTLArchipelago.Core;

/// <summary>
/// The 12 ability items and the ObjectController classes each one unlocks.
///
/// The grouping is not cosmetic. A raw class-per-ability fails badly in two
/// directions at once: Draggables touches 48 of the 111 eligible levels, so
/// gating it would lock almost the whole game, while 23 of the 41 classes
/// touch exactly one level each and would be dead items. Grouped this way
/// every ability gates between 4 and 19 levels, which is what makes "all
/// abilities matter" true.
///
/// Draggables is deliberately absent. It is the baseline verb - pick up and
/// put down, taught in level 1 - and is never an item.
/// </summary>
public static class Abilities
{
    public const string Swapping = "Swapping";
    public const string Stacking = "Stacking";
    public const string Ordering = "Ordering";
    public const string Gadgets = "Gadgets";
    public const string Rotating = "Rotating";
    public const string Grids = "Grids";
    public const string Tidying = "Tidying";
    public const string Containers = "Containers";
    public const string Furniture = "Furniture";
    public const string Sticking = "Sticking";
    public const string Symmetry = "Symmetry";
    public const string Jigsaw = "Jigsaw";

    /// <summary>Controller classes that need no ability. Never items.</summary>
    public static readonly IReadOnlySet<string> Baseline = new HashSet<string>
    {
        // Pick up and put down, taught in level 1.
        "Draggables",
        // A catch-all bucket for objects with no mechanic of their own - it
        // appears once, on Record Player, alongside that level's real
        // controllers. Gating it would lock a level behind an item that
        // describes nothing.
        "GenericLevelObjects",
    };

    /// <summary>
    /// Controller classes that are not puzzles and never become locations:
    /// camera-pan helpers and the like. Filtering these stops the location
    /// table minting checks a player cannot perceive, let alone complete.
    /// </summary>
    public static readonly IReadOnlySet<string> NotPuzzles = new HashSet<string>
    {
        "Pannables",
    };

    private static readonly Dictionary<string, string> ClassToAbility = new()
    {
        ["Shuffleables"] = Swapping,
        ["ShuffleablesRelative"] = Swapping,
        ["ShuffleablesRepeatingPattern"] = Swapping,
        ["CrackersShuffleables"] = Swapping,

        ["StackablesY"] = Stacking,
        ["StackablesZ"] = Stacking,
        ["DraggablesStacked"] = Stacking,
        ["TupperwareTower"] = Stacking,

        ["DraggablesOrdered"] = Ordering,
        ["Indexables"] = Ordering,

        ["RecordPlayer"] = Gadgets,
        ["ComputerErrorsController"] = Gadgets,
        ["HourglassController"] = Gadgets,
        ["MatchboxesObjectController"] = Gadgets,
        ["Toggleables"] = Gadgets,
        ["AnimScrubbables"] = Gadgets,
        ["Groupables"] = Gadgets,
        ["ScrollFieldGroupsController"] = Gadgets,
        ["TelescopeDraggables"] = Gadgets,
        ["CandlesObjectController"] = Gadgets,

        ["Rotateables"] = Rotating,
        ["Frame_Rotateables"] = Rotating,
        ["RadialDance"] = Rotating,

        ["GridPuzzle"] = Grids,
        ["StackableGrid"] = Grids,

        ["Removables"] = Tidying,
        ["Pluckables"] = Tidying,
        ["Dirtyables"] = Tidying,
        ["Clearables"] = Tidying,
        ["CleanablesController"] = Tidying,

        ["Containables"] = Containers,
        ["TupperwareLids"] = Containers,
        ["TupperwareNesting"] = Containers,

        ["DrawerController"] = Furniture,
        ["Cupboard"] = Furniture,
        ["HangingToolsController"] = Furniture,

        ["Stickables"] = Sticking,
        ["SortingItems_Draggables"] = Sticking,

        ["SymmetricalPlaceables"] = Symmetry,

        ["DraggablesJigsaw"] = Jigsaw,
    };

    /// <summary>Every ability name, in a stable order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Swapping, Stacking, Ordering, Gadgets, Rotating, Grids,
        Tidying, Containers, Furniture, Sticking, Symmetry, Jigsaw,
    };

    /// <summary>
    /// The ability a controller class needs, or null when it needs none -
    /// either because it is the baseline verb or because it is not a puzzle.
    /// An unknown class also returns null: a game update that adds a class we
    /// have never seen should leave it playable rather than permanently
    /// locked behind an item that does not exist.
    /// </summary>
    public static string? ForClass(string controllerClass)
        => ClassToAbility.TryGetValue(controllerClass, out var a) ? a : null;

    /// <summary>Controller classes an ability unlocks, for slot_data and docs.</summary>
    public static IReadOnlyList<string> ClassesFor(string ability)
        => ClassToAbility.Where(kv => kv.Value == ability)
                         .Select(kv => kv.Key)
                         .OrderBy(k => k, StringComparer.Ordinal)
                         .ToList();

    public static bool IsPuzzleController(ControllerInfo c) => !NotPuzzles.Contains(c.Type);
}
