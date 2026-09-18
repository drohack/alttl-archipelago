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
    public const string Drawer = "Drawer";
    public const string Sticking = "Sticking";
    public const string Symmetry = "Symmetry";
    public const string Jigsaw = "Jigsaw";

    // ---- DLC abilities -----------------------------------------------------
    //
    // Kept separate from the twelve above, and listed per DLC, because the
    // twelve are a closed set that item ids hang off: ABILITY_ITEMS sits in
    // the middle of the item name list, so inserting a thirteenth there would
    // renumber Cat Trap, Background Change Trap and Hint Page and silently
    // repoint every seed in flight. DLC abilities are appended after all of
    // them instead.
    //
    // Only a mechanic with no base-game equivalent earns one. Of the five
    // controller classes the DLCs add, four already had a home:
    // DrawerExpandableController is a drawer, DLC2NanopetsShuffleables is a
    // Shuffleables subtype, GridPuzzleBase is what GridPuzzle derives from,
    // and CatEyesController is a one-level bespoke gadget - which is exactly
    // what Gadgets already collects for Candles, Hourglass, Matchboxes and
    // Record Player.

    /// <summary>DLC2's pizza: 48 objects spread across slices, no base analogue.</summary>
    public const string Distributing = "Distributing";

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
        ["DLC2NanopetsShuffleables"] = Swapping,
        ["ShuffleablesRelative"] = Swapping,
        ["ShuffleablesRepeatingPattern"] = Swapping,
        ["CrackersShuffleables"] = Swapping,

        ["StackablesY"] = Stacking,
        ["StackablesZ"] = Stacking,
        ["DraggablesStacked"] = Stacking,
        ["TupperwareTower"] = Stacking,

        ["DraggablesOrdered"] = Ordering,
        ["Indexables"] = Ordering,

        // DLC2 Pizza. The only controller on its level, and the only DLC class
        // that is not a variation on something the base game already does.
        ["Distributables"] = Distributing,

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
        // DLC2 Cat Eyes. A bespoke one-level mechanic, which is what this
        // bucket is for.
        ["CatEyesController"] = Gadgets,

        ["Rotateables"] = Rotating,
        ["Frame_Rotateables"] = Rotating,
        ["RadialDance"] = Rotating,

        ["GridPuzzle"] = Grids,
        ["GridPuzzleBase"] = Grids,
        ["StackableGrid"] = Grids,

        ["Removables"] = Tidying,
        ["Pluckables"] = Tidying,
        ["Dirtyables"] = Tidying,
        ["Clearables"] = Tidying,
        ["CleanablesController"] = Tidying,

        ["Containables"] = Containers,
        ["TupperwareLids"] = Containers,
        ["TupperwareNesting"] = Containers,

        ["DrawerController"] = Drawer,
        ["DrawerExpandableController"] = Drawer,
        ["Cupboard"] = Drawer,
        ["HangingToolsController"] = Drawer,

        ["Stickables"] = Sticking,
        ["SortingItems_Draggables"] = Sticking,

        ["SymmetricalPlaceables"] = Symmetry,

        ["DraggablesJigsaw"] = Jigsaw,
    };

    /// <summary>
    /// The base game's twelve, in a stable order. MUST NOT be reordered or
    /// extended: item ids are positional and hang off exactly this sequence.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Swapping, Stacking, Ordering, Gadgets, Rotating, Grids,
        Tidying, Containers, Drawer, Sticking, Symmetry, Jigsaw,
    };

    /// <summary>
    /// Abilities each DLC introduces, appended after the twelve. A DLC with
    /// no new mechanic is listed with an empty set rather than left out, so
    /// the answer to "what does this DLC add" is always stated.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Dlc =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // Cupboards and Drawers adds DrawerExpandableController, which is
            // a drawer. Nothing new to unlock.
            ["DLC1"] = new string[0],
            ["DLC2"] = new[] { Distributing },
        };

    /// <summary>
    /// Every ability, base twelve first and DLC ones after, in the order item
    /// ids are allocated.
    /// </summary>
    public static readonly IReadOnlyList<string> AllWithDlc =
        All.Concat(Dlc.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                      .SelectMany(kv => kv.Value)).ToList();

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
