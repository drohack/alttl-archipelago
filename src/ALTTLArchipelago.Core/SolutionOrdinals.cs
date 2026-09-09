using System.Collections.Generic;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Which solution number a completion is, per slot.
///
/// The game reports a completion with an internal SolutionId - a string the
/// generator has never seen and cannot be asked about. So solutions are counted
/// ORDINALLY: the Nth DISTINCT id found on a slot is that slot's Nth solution
/// location. That mapping is the whole of this class.
///
/// Distinct matters. A player can complete the same arrangement repeatedly -
/// leave the level, come back, solve it the same way - and that is not a new
/// check. Counting completions instead of distinct ids would hand out a slot's
/// second and third solution locations for solving its first one three times.
/// </summary>
public sealed class SolutionOrdinals
{
    private readonly Dictionary<int, List<string>> _found = new();

    /// <summary>
    /// Record a completion and return which solution number it is, or 0 if this
    /// arrangement has already been seen on this slot.
    ///
    /// Zero rather than the existing ordinal, because the caller's question is
    /// "is there a new check here" and a repeat is not one. An empty id still
    /// counts: a level that reports no id at all has exactly one solution, and
    /// refusing it would mean that level could never be checked.
    /// </summary>
    public int Record(int slot, string solutionId)
    {
        if (slot < 0) return 0;

        if (!_found.TryGetValue(slot, out var seen))
        {
            seen = new List<string>();
            _found[slot] = seen;
        }

        var id = solutionId ?? "";
        if (seen.Contains(id)) return 0;

        seen.Add(id);
        return seen.Count;
    }

    /// <summary>
    /// Put back the arrangements a slot had already found before this session.
    ///
    /// WITHOUT THIS THE ORDINALS RESTART AT 1 EVERY LAUNCH, AND CHECKS ARE LOST.
    ///
    /// The ordinal is what picks the location: the first new arrangement of a
    /// slot files "Solution 1", the second "Solution 2". That counter lived
    /// only in memory, so closing the game reset it. droha found all three
    /// arrangements of Snow Globes, the game recorded 3 of 3 - and the server
    /// had exactly one check, because each session re-filed "Solution 1", a
    /// location already collected. Two checks silently went nowhere and the
    /// card sat green with nothing obvious left to do.
    ///
    /// The game's own save is the right source: it stores the solutionId of
    /// every arrangement found, per level, which is precisely the list this
    /// class keeps. Seeding from it makes the ordinal a property of the RUN
    /// rather than of the session.
    ///
    /// Ids already present are not added twice, so calling this more than once
    /// for a slot is harmless - which matters, because the slot is re-entered
    /// every time the player opens the card.
    /// </summary>
    public int Seed(int slot, IEnumerable<string> solutionIds)
    {
        if (slot < 0 || solutionIds == null) return 0;

        if (!_found.TryGetValue(slot, out var seen))
        {
            seen = new List<string>();
            _found[slot] = seen;
        }

        var added = 0;
        foreach (var id in solutionIds)
        {
            var key = id ?? "";
            if (seen.Contains(key)) continue;
            seen.Add(key);
            added++;
        }
        return added;
    }

    /// <summary>How many distinct solutions have been found on a slot.</summary>
    public int CountFor(int slot)
        => _found.TryGetValue(slot, out var seen) ? seen.Count : 0;

    public void Clear() => _found.Clear();
}
