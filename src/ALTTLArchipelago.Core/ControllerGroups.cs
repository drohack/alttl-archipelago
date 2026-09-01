namespace ALTTLArchipelago.Core;

/// <summary>
/// One checkable unit of a level: either a lone controller, or several
/// controllers that cannot be solved apart from one another.
/// </summary>
public sealed class ControllerGroup
{
    /// <summary>Member controller names, sorted, so the group is stable.</summary>
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The group's stable identity, used in location names. The alphabetically
    /// first member rather than a join of all of them, so a location name does
    /// not balloon and does not change if the game ever reorders controllers.
    /// </summary>
    public string Name => Members.Count > 0 ? Members[0] : "";

    /// <summary>
    /// Abilities needed to solve this group: its own members' abilities, plus
    /// those of everything it depends on, transitively. A one-way dependency
    /// does not merge two groups, but it does mean you cannot finish the
    /// dependent one without the ability for the thing it waits on.
    /// </summary>
    public IReadOnlySet<string> Abilities { get; init; } = new HashSet<string>();
}

/// <summary>
/// Turns a level's controllers into checkable groups.
///
/// The rule comes from measurement, not taste. Only 9 of 380 controllers
/// declare a dependency, across 5 levels - and 4 of those 5 are MUTUAL pairs
/// (matchDependencySolutions: two controllers whose solutions must agree, like
/// the pieces and shadows in Chess Shadows). A mutual pair is one puzzle
/// wearing two hats, so minting two locations for it would create two checks
/// that can only ever be collected together.
///
/// A one-way dependency is different. Desktop Computer's "Computer Errors"
/// waits on "Computer Desktop", but they are genuinely solved one after the
/// other, so they stay two locations - the dependent one simply carries the
/// other's abilities in its requirement.
/// </summary>
public static class ControllerGroups
{
    public static IReadOnlyList<ControllerGroup> For(LevelInfo level)
    {
        var puzzles = level.Controllers.Where(Abilities.IsPuzzleController).ToList();

        // Controllers sharing a GameObject name are one visual group wearing
        // two components (Radial Dance Party lists "Radial Cat Toys 1" as both
        // a RadialDance and a Draggables). Collapse by name, unioning types.
        var byName = new Dictionary<string, List<ControllerInfo>>(StringComparer.Ordinal);
        foreach (var c in puzzles)
        {
            if (!byName.TryGetValue(c.Name, out var list))
            {
                list = new List<ControllerInfo>();
                byName[c.Name] = list;
            }
            list.Add(c);
        }

        var names = byName.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        // Merge only MUTUAL dependencies. Union-find over the pairs where each
        // side names the other.
        var parent = names.ToDictionary(n => n, n => n, StringComparer.Ordinal);

        string Find(string x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(string a, string b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra != rb) parent[rb] = ra;
        }

        bool DependsOn(string from, string to)
            => byName.TryGetValue(from, out var cs)
               && cs.Any(c => c.DependsOn.Contains(to, StringComparer.Ordinal));

        foreach (var a in names)
        {
            foreach (var b in names)
            {
                if (a == b) continue;
                if (DependsOn(a, b) && DependsOn(b, a)) Union(a, b);
            }
        }

        // Abilities a single named controller needs, ignoring dependencies.
        IEnumerable<string> Own(string name)
            => byName[name]
                .Select(c => Abilities.ForClass(c.Type))
                .Where(a => a != null)!
                .Cast<string>();

        // Plus everything reachable through dependencies, transitively.
        IReadOnlySet<string> WithDependencies(IEnumerable<string> members)
        {
            var need = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(members, StringComparer.Ordinal);
            var queue = new Queue<string>(seen);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (!byName.ContainsKey(n)) continue;   // a dependency we filtered out
                foreach (var a in Own(n)) need.Add(a);
                foreach (var c in byName[n])
                {
                    foreach (var d in c.DependsOn)
                    {
                        if (seen.Add(d)) queue.Enqueue(d);
                    }
                }
            }
            return need;
        }

        return names
            .GroupBy(Find, StringComparer.Ordinal)
            .Select(g =>
            {
                var members = g.OrderBy(n => n, StringComparer.Ordinal).ToList();
                return new ControllerGroup
                {
                    Members = members,
                    Abilities = WithDependencies(members),
                };
            })
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every ability the level needs to be finished outright - the union over
    /// all its groups. This is a solution location's requirement, because a
    /// solution is an arrangement of the whole level.
    /// </summary>
    public static IReadOnlySet<string> AbilitiesForLevel(LevelInfo level)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in For(level))
        {
            foreach (var a in g.Abilities) all.Add(a);
        }
        return all;
    }
}
