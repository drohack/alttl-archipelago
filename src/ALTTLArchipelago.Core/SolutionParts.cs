namespace ALTTLArchipelago.Core;

/// <summary>
/// Which of a level's controllers a completion's solution id names.
///
/// The game reports an ending as "&lt;controller&gt;_&lt;n&gt;" - Spoons ends as
/// "Size-(Elastic)_0" or "Stacked_0" - with the controller's name lightly
/// mangled (spaces become dashes, some punctuation goes). So the id's prefix
/// is compared with each controller name on letters and digits only,
/// case-folded. Measured 2026-09-27 over every id in
/// fixtures/solution-ids-observed.tsv: 198 of 219 name a controller this way.
/// The rest - MedicineCabinet's "Draggables_0", the DLC1 cabinets'
/// "Cupboard_0" - name none, and the caller keeps its whole-level rule.
///
/// WHY IT MATTERS: every solution location carries the level's WHOLE ability
/// set (rules.py), so Checks.Earned withheld droha's Spoons "Size (Elastic)"
/// ending for Stacking, which that ending never touches (0.4.2 playtest).
/// Judging an ending by the controller it names files it on the abilities it
/// actually used.
/// </summary>
public static class SolutionParts
{
    /// <summary>The controllers the id names, in the order given; empty when none.</summary>
    public static IReadOnlyList<string> ControllersFor(string? solutionId, IEnumerable<string> controllerNames)
    {
        var found = new List<string>();
        var key = Normalise(Prefix(solutionId));
        if (key.Length == 0 || controllerNames == null) return found;

        foreach (var name in controllerNames)
        {
            if (name != null && Normalise(name) == key) found.Add(name);
        }
        return found;
    }

    /// <summary>The id without its trailing "_&lt;n&gt;", if it has one.</summary>
    private static string Prefix(string? solutionId)
    {
        var id = solutionId ?? "";
        var cut = id.LastIndexOf('_');
        if (cut <= 0 || cut == id.Length - 1) return id;
        for (int i = cut + 1; i < id.Length; i++)
        {
            if (!char.IsDigit(id[i])) return id;
        }
        return id.Substring(0, cut);
    }

    private static string Normalise(string text)
    {
        var chars = new List<char>(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
