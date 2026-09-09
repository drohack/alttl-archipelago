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
    /// The group's stable identity - the alphabetically first member, so it
    /// does not change if the game ever reorders controllers. This is what the
    /// mod matches incoming solved events against.
    /// </summary>
    public string Name => Members.Count > 0 ? Members[0] : "";

    /// <summary>
    /// What a player is shown, in location names and hints: the same group
    /// with its redundant component-type suffix stripped. See PartNames.
    /// </summary>
    public string DisplayName { get; init; } = "";

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
/// The rule comes from measurement, not taste. Dependencies fall into two
/// shapes, and the difference decides whether they merge.
///
/// A MUTUAL pair (matchDependencySolutions: two controllers whose solutions
/// must agree, like the pieces and shadows in Chess Shadows) is one puzzle
/// wearing two hats, so minting two locations for it would create two checks
/// that can only ever be collected together. Four such pairs exist.
///
/// THIS COMMENT USED TO SAY "only 9 of 380 controllers declare a dependency,
/// across 5 levels", and someone reading it to decide whether the dependency
/// graph mattered would have concluded it barely does. Those figures describe
/// docs/data/controller-survey.tsv - a PREFAB walk. This class reads
/// levels.json, which is a RUNTIME sweep, and dependencies are wired up at
/// registration rather than serialised on the prefab: the survey sees 9 edges
/// where the runtime sees 23, and hand-authored containment and phase edges
/// push it further still. The graph is not a curiosity; it is the difference
/// between a finishable seed and a dead card.
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

        // EVERY controller by name, puzzles and non-puzzles alike, so a chain
        // can be followed THROUGH something that carries no ability of its own.
        //
        // The traversal below used to `continue` on any name missing from the
        // filtered map, which dropped the onward edges as well as the
        // abilities. A chain A -> Pannables -> B would therefore lose B
        // entirely, and lose it silently: the group would simply require less
        // than it should, which is the one direction that makes a seed
        // unfinishable. No level has that shape today - this is a trap being
        // closed before something walks into it, not a bug being fixed.
        var allByName = new Dictionary<string, List<ControllerInfo>>(StringComparer.Ordinal);
        foreach (var c in level.Controllers)
        {
            if (!allByName.TryGetValue(c.Name, out var list))
            {
                list = new List<ControllerInfo>();
                allByName[c.Name] = list;
            }
            list.Add(c);
        }

        // Plus everything reachable through dependencies, transitively.
        IReadOnlySet<string> WithDependencies(IEnumerable<string> members)
        {
            var need = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(members, StringComparer.Ordinal);
            var queue = new Queue<string>(seen);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();

                // Abilities only from puzzle controllers...
                if (byName.ContainsKey(n))
                {
                    foreach (var a in Own(n)) need.Add(a);
                }

                // ...but keep walking regardless of what this one was.
                if (!allByName.TryGetValue(n, out var cs)) continue;
                foreach (var c in cs)
                {
                    foreach (var d in c.DependsOn)
                    {
                        if (seen.Add(d)) queue.Enqueue(d);
                    }
                }
            }
            return need;
        }

        var display = PartNames.ForLevel(level);

        return names
            .GroupBy(Find, StringComparer.Ordinal)
            .Select(g =>
            {
                var members = g.OrderBy(n => n, StringComparer.Ordinal).ToList();
                var lead = members[0];
                return new ControllerGroup
                {
                    Members = members,
                    Abilities = WithDependencies(members),
                    DisplayName = display.TryGetValue(lead, out var d) ? d : lead,
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
        var all = new HashSet<string>(AbilitiesTaughtBy(level), StringComparer.Ordinal);

        // Plus what the registered controllers cannot reveal - see
        // LevelInfo.ExtraAbilities. Deliberately NOT folded into the groups:
        // these abilities belong to the level as a whole, and giving them to a
        // group would put them on a part location that does not need them.
        foreach (var a in level.ExtraAbilities) all.Add(a);

        return all;
    }

    /// <summary>
    /// The abilities this level demonstrably EXERCISES, from its registered
    /// controllers alone.
    ///
    /// Separate from AbilitiesForLevel, and the difference matters in exactly
    /// one place. Requirements should err towards demanding too much: an extra
    /// ability makes a seed tighter, a missing one can make it unwinnable, so
    /// AbilitiesForLevel includes ExtraAbilities.
    ///
    /// Mechanic COVERAGE is the opposite. It exists to guarantee a run
    /// contains real puzzles for the mechanics no generator can make, and it
    /// should err towards demanding too little - counting a level that might
    /// only need an ability in a phase nobody has confirmed would let coverage
    /// tick a box with a puzzle that never teaches the thing.
    ///
    /// Measured when this split was made: folding ExtraAbilities into coverage
    /// took Furniture from 4 levels to 5, the fifth being MedicineCabinet on
    /// the strength of a prefab Cupboard that does not register at load.
    /// </summary>
    public static IReadOnlySet<string> AbilitiesTaughtBy(LevelInfo level)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in For(level))
        {
            foreach (var a in g.Abilities) all.Add(a);
        }
        return all;
    }
}
