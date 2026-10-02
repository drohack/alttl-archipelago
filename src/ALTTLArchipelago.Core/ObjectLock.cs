namespace ALTTLArchipelago.Core;

/// <summary>
/// Whether one object is movable, decided from every controller that holds it.
///
/// UNLOCKED WINS among the groups that act on the object. Coins 1 (Shape) holds
/// its six coins in an Ordered group and a Stacked group, and holding either
/// verb must free them; locking inside the controller loop let the last group
/// to touch a coin decide, and left a level with nothing to interact with.
///
/// A DRAWER OR CUPBOARD IS NOT ONE OF THOSE GROUPS. It holds what sits inside
/// it; it does not move it. Paper Plane Supplies' 14 holder pieces (seven
/// blades, two knife parts, five pencils) are held by Containables and the
/// Drawer Controller and by nothing else (DevTools sharing:, 2026-09-23), so
/// while the drawer counted, holding Drawer freed every one of them and the
/// Containers lock did nothing. droha, 2026-09-25: "i don't think the container
/// should count as a drawer." An enclosure decides only an object that no other
/// controller holds - the drawer itself.
///
/// By class, not by ability: HangingToolsController also maps to Drawer, but it
/// moves the tools it holds, so it votes like any other group.
/// </summary>
public sealed class ObjectLock
{
    /// <summary>Controller classes that enclose objects rather than act on them.</summary>
    public static readonly IReadOnlySet<string> Enclosures =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DrawerController",
            "DrawerExpandableController",
            "Cupboard",
        };

    /// <summary>
    /// Is this holder a cover: something sitting in front of other objects,
    /// that a locked one must keep blocking clicks (IsCover)? With its
    /// collider switched off, Tea Cabinet's locked doors let the items behind
    /// them be grabbed (droha, 2026-09-25).
    ///
    /// Drawers and cupboards always are. AnimScrubbables only on
    /// <see cref="DoorLevels"/>: on Wilting Flowers it is the puzzle.
    /// </summary>
    public static bool IsCoverClass(string controllerClass, string levelId)
    {
        if (Enclosures.Contains(controllerClass ?? "")) return true;
        return controllerClass == "AnimScrubbables" && DoorLevels.Contains(levelId ?? "");
    }

    /// <summary>
    /// Levels where AnimScrubbables is a DOOR: scrubbed open to reach what is
    /// behind it, so a locked one must keep blocking clicks.
    /// </summary>
    public static readonly IReadOnlySet<string> DoorLevels =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DLC1 Clock Cupboard",
            "DLC1 Tea Cabinet",
            "DLC1 Trophy Cabinet",
        };

    /// <summary>
    /// Levels where AnimScrubbables is the PUZZLE itself (Wilting Flowers'
    /// "Upright" flowers), so it locks like any other group.
    /// </summary>
    public static readonly IReadOnlySet<string> ScrubPuzzleLevels =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Wilting Flowers",
        };

    /// <summary>
    /// Levels reloaded, not unlocked in place, when their ability arrives
    /// mid-level. The game marks their covered pieces fixed while the lock
    /// holds them, and unlocking in place gives those back pickable but
    /// impossible to put down (droha, Cat Food Cans, 2026-09-27: "they don't
    /// drop"). Every piece in them is locked without the ability, so the
    /// reload loses nothing. The probe (tools/probe-lock-roundtrip.py) finds
    /// this "can be picked up where the game did not allow it" on these
    /// three and on Wilting Flowers' Dirt, which cannot be dragged anyway.
    ///
    /// Seed Pods and Clover for their colour: the game tints their pieces to
    /// 0.9 once, after the intro, and only the ones not locked then, so a
    /// piece unlocked in place came back at 1.0, brighter than the rest
    /// (probe-lock-roundtrip.py, 2026-09-30). The reload replays the intro.
    /// Their one group is Symmetry's, so it too loses nothing.
    /// </summary>
    public static readonly IReadOnlySet<string> ResetOnUnlockLevels =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Cat Food Cans",
            "Boxes (Stacked)",
            "GoodTidings_Presents (Stacked)",
            "Seed Pods",
            "Clover",
        };

    /// <summary>
    /// Levels whose Indexables states are held while their group is locked,
    /// even on a piece another, free group holds: the buttons that change them
    /// (the game's IndexIncrementTrigger) have their colliders off.
    ///
    /// Robots: its Indexables group (Ordering) shares Robots 2, 3 and 7 with
    /// IndexedDraggables, which needs nothing, so the lock freed them and
    /// their states changed without Ordering. Robot 9's antennas and Robot
    /// 1's arm buttons are locked but take clicks through colliders under
    /// their free robot (DevTools objects:, 2026-10-01). droha: what an
    /// ability is withheld for must not move, "not just the antenna guy".
    /// The robots still drag: IndexedDraggables needs no ability.
    ///
    /// Math Set the same: its CirclePart and Compass are Indexables pieces its
    /// Items Placements Draggables also holds, and droha extended and
    /// retracted both with Ordering withheld (2026-10-01). Both its Solutions
    /// ask for Ordering.
    ///
    /// NOT every level with that pairing. Junk Drawer Transforming was
    /// finished with Ordering locked (proven-requirements.json), and its logic
    /// asks only for Drawer, as Combs' does, so holding their states would
    /// leave a seed that cannot be won. Water Glasses and Music Box share no
    /// state piece, so the lock already holds theirs (droha's hand test,
    /// 2026-10-01).
    /// </summary>
    public static readonly IReadOnlySet<string> IndexHoldLevels =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DLC2 Robots",
            "DLC2 Math Set",
        };

    /// <summary>
    /// Art that is grey while an indexed state of a level in
    /// <see cref="IndexHoldLevels"/> is held: a child of the state's owner, by
    /// name. Robot 3's heart is its chest door, drawn on the chest-and-arms
    /// layer over its body, so with Ordering withheld that layer is grey and
    /// the body stays lit, as it still drags (droha, 2026-10-01). Robots 2
    /// and 7 draw their buttons into their one body layer, so none of theirs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> IndexHoldArt =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["DLC2 Robots"] = new[] { "Robot3 Chest and Arms" },
        };

    /// <summary>Hold this level's indexed states: it is listed and its Indexables group is locked.</summary>
    public static bool HoldsIndexes(string levelId, bool indexablesLocked) =>
        indexablesLocked && IndexHoldLevels.Contains(levelId ?? "");

    /// <summary>
    /// Reload the level: it is one of <see cref="ResetOnUnlockLevels"/> and the
    /// pass that just ran freed pieces the lock was holding.
    /// </summary>
    public static bool ResetOnUnlock(string levelId, int lockedBefore, int lockedAfter) =>
        ResetOnUnlockLevels.Contains(levelId ?? "") && lockedBefore > 0 && lockedAfter < lockedBefore;

    /// <summary>
    /// Controllers the lock treats as another class, on one level: (level id,
    /// controller GameObject name) to the class it locks as.
    ///
    /// Books (Randomized): a seed with a *_SYMMETRIC rule adds a second
    /// controller, Draggables, over the same 11 books Shuffle holds
    /// (21 of 40 seeds, DevTools gensweep). The lock goes by class, and
    /// Draggables needs nothing, so it freed every book: a player without
    /// Swapping finished the puzzle (the 0.4.3 run, "abilities: 1 locked, 1
    /// open, 11 objects, waiting on Swapping"). Its ending is one of Shuffle's
    /// two (Endings.Canonical) and the table gives both Swapping, so it locks
    /// as Shuffle does. Books 3 and Chocolate Bars have the same pair of
    /// groups and bypass Swapping in the table instead: there the drag rule
    /// is a real way out.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Level, string Controller), string> LockedAs =
        new Dictionary<(string Level, string Controller), string>
        {
            [("Books (Randomized)", "Draggables")] = "Shuffleables",
        };

    /// <summary>The class a controller locks as: its own, unless <see cref="LockedAs"/> names another.</summary>
    public static string LockClass(string levelId, string controllerName, string controllerClass) =>
        LockedAs.TryGetValue((levelId ?? "", controllerName ?? ""), out var cls) ? cls : controllerClass;

    private readonly string _levelId;

    /// <param name="levelId">The running level, which decides whether an
    /// AnimScrubbables holder is a door (<see cref="DoorLevels"/>).</param>
    public ObjectLock(string levelId = "")
    {
        _levelId = levelId ?? "";
    }

    private bool _heldByGroup;
    private bool _groupUnlocked;
    private bool _enclosureUnlocked;
    private bool _heldByCover;
    private bool _heldByOther;
    private bool _heldByDrawerSet;
    private bool _drawerSetLocked;

    /// <summary>Drawer controllers: they hold a drawer SET, box and drawers together.</summary>
    private static readonly IReadOnlySet<string> DrawerSets =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DrawerController",
            "DrawerExpandableController",
        };

    /// <summary>
    /// True for a cover held by a drawer controller: a drawer, or the box the
    /// drawers slide into. Only the drawers themselves should lock - the box
    /// cannot move, and greying it greyed everything sitting in it (droha,
    /// 2026-09-26, Sewing Box: "the whole box and drawers are greyed out").
    /// Which one an object is, the runtime answers (AbilityLocks.GameHoldsStill:
    /// no travel, and not a tray the player can pick up).
    /// </summary>
    public bool IsDrawerSetPart => IsCover && _heldByDrawerSet;

    /// <summary>
    /// A drawer controller holding the object voted it locked. A drawer that
    /// slides follows this even when another group frees it (AbilityLocks):
    /// on Daggers a Draggables group holds the four drawers, and they slid
    /// open while their Box stayed locked - droha, 2026-09-26: "i would expect
    /// the box and drawers to be tied together".
    /// </summary>
    public bool DrawerSetLocked => _drawerSetLocked;

    /// <summary>
    /// A drawer controller holds the object: a drawer, or something in one.
    /// The body state recorded when the lock first froze one of these reads
    /// "stopped" while the level plays it simulated: putting it back left
    /// Sewing Box's pieces dead (tools/probe-lock-roundtrip.py, 2026-09-27),
    /// so AbilityLocks.Freeze gives them their physics back. What stops them
    /// first is not known.
    /// </summary>
    public bool HeldByDrawerSet => _heldByDrawerSet;

    /// <summary>
    /// True for the cover itself - the drawer, its handle, the door - and not
    /// for what it holds: every holder is a cover class. A drawer controller
    /// also holds its contents (all 40 of Jewelry Box's pieces, DevTools
    /// sharing:), and those must go on locking like any other object.
    /// </summary>
    public bool IsCover => _heldByCover && !_heldByOther;

    /// <summary>Note one controller that holds the object.</summary>
    public void Add(string controllerClass, bool isLocked)
    {
        if (IsCoverClass(controllerClass, _levelId)) _heldByCover = true;
        else _heldByOther = true;
        if (DrawerSets.Contains(controllerClass ?? ""))
        {
            _heldByDrawerSet = true;
            _drawerSetLocked |= isLocked;
        }

        if (Enclosures.Contains(controllerClass ?? ""))
        {
            _enclosureUnlocked |= !isLocked;
            return;
        }

        _heldByGroup = true;
        _groupUnlocked |= !isLocked;
    }

    /// <summary>True when the object may be moved.</summary>
    public bool Unlocked => _heldByGroup ? _groupUnlocked : _enclosureUnlocked;
}
