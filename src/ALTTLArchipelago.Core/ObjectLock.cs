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

    private bool _heldByGroup;
    private bool _groupUnlocked;
    private bool _enclosureUnlocked;

    /// <summary>Note one controller that holds the object.</summary>
    public void Add(string controllerClass, bool isLocked)
    {
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
