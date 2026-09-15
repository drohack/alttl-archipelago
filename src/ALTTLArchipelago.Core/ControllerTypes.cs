using System;
using System.Collections.Generic;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Controller types that never carry a location.
///
/// The level table lists every ObjectController a level registers, and the
/// name table turns most of them into part locations. Most - not all. A
/// controller that is scenery or a camera aid registers like any other and can
/// even report itself solved, but there is nothing to check.
///
/// MEASURED ACROSS THE WHOLE TABLE rather than judged by name. Counting every
/// controller type against whether any level makes a location of it gives
/// exactly one type that never does:
///
///     Pannables        12 occurrences, 0 locations
///
/// Every other type of the thirty-nine scores at least once, so this list is
/// one entry long on purpose and should stay that way unless the same count
/// says otherwise. Re-run it before adding to this:
///
///     for each level, for each controller, is controller.name in names.parts?
///
/// WHAT GOES WRONG WITHOUT IT. The mod reported a CONTROLLER MISMATCH on every
/// level holding one, which reads as "the logic and the game disagree about
/// what is solvable" - the loudest warning this codebase has, permanently on,
/// for a type that was never meant to be solvable. And the release harness
/// dutifully tried to force-solve it: SetSolved succeeded, dispatching the
/// solved event through the game's own handler threw ArgumentOutOfRange, the
/// controller stayed unsolved, and the level burned every retry and was
/// declared unfinishable. Desktop Computer stalled the 0.3.2 gate at 7 of 8
/// that way.
/// </summary>
public static class ControllerTypes
{
    /// <summary>
    /// Runtime type names, as GetIl2CppType().Name reports them.
    /// </summary>
    public static readonly IReadOnlyCollection<string> NeverScored =
        new HashSet<string>(StringComparer.Ordinal) { "Pannables" };

    /// <summary>
    /// Can a controller of this runtime type ever carry a location?
    ///
    /// Null and empty are SCORED, deliberately: an unknown type is worth a
    /// mismatch warning, and silence is the failure mode this exists to stop.
    /// </summary>
    public static bool Scores(string? runtimeType)
        => string.IsNullOrEmpty(runtimeType)
           || !NeverScored.Contains(runtimeType!);
}
