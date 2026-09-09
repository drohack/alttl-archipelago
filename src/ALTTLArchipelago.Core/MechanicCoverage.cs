namespace ALTTLArchipelago.Core;

/// <summary>
/// Which abilities the procedural generators simply cannot produce.
///
/// Four of the twelve have ZERO generator coverage in the base game:
///
///     Stacking     0 generators, 2 archive, 10 base
///     Containers   0 generators, 4 archive,  4 base
///     Furniture    0 generators, 3 archive,  1 base
///     Jigsaw       0 generators, 4 archive,  0 base
///
/// That matters because the default draw is 80% generators. Left to the
/// weights, a run can end up with one token Jigsaw level or - if someone sets
/// archive to zero - none at all, at which point the ability is pruned and an
/// entire family of puzzle disappears from the game.
///
/// Jigsaw is the most fragile: four levels, every one of them archive.
/// Furniture is next: four levels, of which only Workbench is base.
///
/// So the slot draw reserves a few slots up front to guarantee coverage before
/// the weights get a say. It is cheap because these levels overlap heavily -
/// NeatStreak_Paper Plane Supplies alone covers Containers, Furniture and
/// Jigsaw - so guaranteeing three of each costs about 8 of 79 slots.
///
/// This is derived from the level table rather than hardcoded, so adding DLC
/// (which does have stacking and container generators) corrects it by itself.
/// </summary>
public static class MechanicCoverage
{
    /// <summary>Abilities no generator can produce, given the level table.</summary>
    public static IReadOnlyList<string> WithoutGeneratorCoverage(LevelTable table)
    {
        var fromGenerators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in table.Levels.Where(l => l.Source == "generator"))
        {
            foreach (var a in ControllerGroups.AbilitiesTaughtBy(level)) fromGenerators.Add(a);
        }

        return Abilities.All
            .Where(a => !fromGenerators.Contains(a) && CountFor(table, a) > 0)
            .ToList();
    }

    /// <summary>How many levels in the table exercise an ability at all.</summary>
    public static int CountFor(LevelTable table, string ability)
        => table.Levels.Count(l => ControllerGroups.AbilitiesTaughtBy(l).Contains(ability));

    /// <summary>
    /// A small set of levels covering each named ability at least
    /// <paramref name="perAbility"/> times, chosen greedily so overlapping
    /// levels do the work of several.
    ///
    /// Returns fewer than asked if the table cannot supply it - Furniture only
    /// exists on four levels, so a demand of five is unmeetable. The caller
    /// takes what it gets rather than failing generation; a thin run beats no
    /// run.
    /// </summary>
    public static IReadOnlyList<LevelInfo> Reserve(
        LevelTable table, IEnumerable<string> abilities, int perAbility)
    {
        var need = abilities.ToDictionary(a => a, _ => perAbility, StringComparer.Ordinal);
        var remaining = table.Levels.ToList();
        var picked = new List<LevelInfo>();

        while (need.Values.Any(v => v > 0))
        {
            var wanted = need.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

            var best = remaining
                .Select(l => (Level: l, Gain: ControllerGroups.AbilitiesTaughtBy(l).Intersect(wanted).Count()))
                .Where(x => x.Gain > 0)
                // Most gap-abilities covered first; ties broken by fewest total
                // abilities, so a focused level is preferred over a sprawling
                // one that would over-serve things already covered.
                .OrderByDescending(x => x.Gain)
                .ThenBy(x => ControllerGroups.AbilitiesTaughtBy(x.Level).Count)
                .ThenBy(x => x.Level.LevelIndex)
                .FirstOrDefault();

            if (best.Level == null) break;   // nothing left can help

            foreach (var a in ControllerGroups.AbilitiesTaughtBy(best.Level).Intersect(wanted))
            {
                need[a]--;
            }
            picked.Add(best.Level);
            remaining.Remove(best.Level);
        }

        return picked;
    }
}
