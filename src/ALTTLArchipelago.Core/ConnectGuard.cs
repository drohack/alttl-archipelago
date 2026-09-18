namespace ALTTLArchipelago.Core;

/// <summary>
/// What to do with a payload the server just handed us: play it, refuse it, or
/// play it with a caveat.
///
/// <see cref="Refusal"/> non-null means STOP - it is the reason, already
/// written for a player to read in the connection pane.
/// </summary>
public sealed class ConnectVerdict
{
    private ConnectVerdict(string? refusal, string? warning)
    {
        Refusal = refusal;
        Warning = warning;
    }

    /// <summary>Why this seed cannot be played, or null when it can.</summary>
    public string? Refusal { get; }

    /// <summary>Something worth logging on a seed that is otherwise fine.</summary>
    public string? Warning { get; }

    public bool Accepted => Refusal == null;

    internal static ConnectVerdict Refuse(string why) => new(why, null);
    internal static ConnectVerdict Allow(string? warning = null) => new(null, warning);
}

/// <summary>
/// The checks a login still has to pass after the server says yes.
///
/// WHY THIS IS IN CORE, AND WHY IT IS A SEPARATE THING AT ALL. The sequence
/// below used to live inline in <c>Connection.Connect</c>, between a socket
/// handshake and an event wiring - untestable in practice, because reaching it
/// needs a live <c>ArchipelagoSession</c>, which is a concrete class whose
/// helper properties would all have to be stubbed to be non-null before a
/// single assertion could run. So the most consequential decision the mod
/// makes had no test, and its two halves were added in different releases for
/// the same reason each time: the guard existed, and was skipped exactly where
/// it was authoritative.
///
/// Moving the socket itself here would buy compilation, not coverage. This
/// buys coverage: everything below is a pure function of the payload and the
/// mod's own version.
///
/// ORDER IS PART OF THE CONTRACT. The version pair is checked FIRST, because a
/// mismatched apworld can make every other field look wrong in ways that would
/// be reported as a broken seed rather than as the mismatch it is - and
/// "install the matching pair" is an answer the player can act on, where
/// "pack_size is 0" is not.
/// </summary>
public static class ConnectGuard
{
    /// <summary>
    /// Written for a player, not a log: it names the fix rather than the
    /// symptom, because the symptom is invisible until the run misbehaves.
    /// </summary>
    public const string UnknownVersionWarning =
        "this seed predates the version check, so the mod cannot tell whether "
        + "it matches. If the run misbehaves, regenerate it with the apworld "
        + "from this release.";

    /// <summary>
    /// Whether to start a run on this payload.
    /// </summary>
    /// <param name="slot">The payload the server sent.</param>
    /// <param name="modVersion">
    /// The mod's own version, or "" when it cannot be read. Empty disables the
    /// pair check rather than failing the connection: an unreadable assembly
    /// version is a packaging oddity, and refusing every seed over it would be
    /// a far worse failure than the one being guarded.
    /// </param>
    public static ConnectVerdict Evaluate(SlotData slot, string modVersion)
        => Evaluate(slot, modVersion, null);

    /// <param name="installedDlc">
    /// The DLC keys the player actually has, or null to skip the check. Passed
    /// in because Core cannot reference the game - the same reason modVersion
    /// is a parameter.
    ///
    /// NULL SKIPS RATHER THAN REFUSES. A caller that cannot read the game's
    /// DLC list should not turn that into a refused connection; the failure it
    /// would cause is worse than the one it guards.
    /// </param>
    public static ConnectVerdict Evaluate(SlotData slot, string modVersion,
                                          IReadOnlyCollection<string>? installedDlc)
    {
        // THE PAIR CHECK, BEFORE ANYTHING ELSE.
        //
        // The mod and the apworld ship together and must agree about the item
        // table. check-version.py binds them at BUILD time, across three files
        // in one commit - and nothing checked the two things a player actually
        // installed. A 0.3.1 mod would connect to a 0.3.2 seed without
        // complaint and play a subtly wrong game: location ids move between
        // releases, so it sends the wrong checks under the right names.
        //
        // Refused rather than warned. Everything downstream builds a run on
        // this payload, and a run built on the wrong table cannot be walked
        // back - checks are sent to other people's worlds.
        var mismatch = slot.VersionMismatch(modVersion);
        if (mismatch != null) return ConnectVerdict.Refuse(mismatch);

        // AND THE REST OF THE PAYLOAD, on the same terms.
        //
        // Problems() is documented as "whether the payload is coherent enough
        // to start a run on", and two of its three callers treated it that way
        // - the offline start and the cache write both REFUSE. The live
        // connect path logged a warning and started the run anyway, so the
        // guard was enforced where a bad payload is merely inconvenient and
        // skipped where it is authoritative.
        //
        // What got through is not cosmetic. pack_size 0 with no boundaries
        // leaves OpenSlots at 0 forever, which is a track that never opens a
        // single card; boundaries that stop short strand the tail of the run
        // behind an item that does not exist; a level with no controller_groups
        // entry can never send a group check. Each is a run the player cannot
        // finish, behind one warning line in a log nobody reads during a game.
        var problems = slot.Problems();
        if (problems.Count > 0)
        {
            return ConnectVerdict.Refuse(
                "this seed cannot be played: " + string.Join("; ", problems));
        }

        // DLC THE PLAYER DOES NOT OWN, refused rather than warned.
        //
        // This one has no soft failure available. A level belonging to an
        // uninstalled DLC does not throw when the mod launches it - measured
        // 2026-09-17, LevelManager.SetActiveLevel quietly redirects to the DLC
        // level select instead - so a run containing one would look like the
        // mod failing to open a puzzle, repeatedly, with nothing in the log
        // saying why. Better to say so at connect, once, in words.
        if (installedDlc != null)
        {
            var missing = slot.RequiredDlc
                .Where(key => !installedDlc.Contains(key))
                .Select(DlcName)
                .ToList();
            if (missing.Count > 0)
            {
                return ConnectVerdict.Refuse(
                    "this seed was built with " + string.Join(" and ", missing)
                    + ", which is not installed. Install it, or ask for a seed "
                    + "generated without it.");
            }
        }

        // Allowed, not refused: a seed too old to say which version made it is
        // an UNKNOWN rather than a mismatch, and refusing would strand runs
        // that are very probably fine.
        return string.IsNullOrEmpty(slot.WorldVersion)
            ? ConnectVerdict.Allow(UnknownVersionWarning)
            : ConnectVerdict.Allow();
    }

    /// <summary>The name a player would recognise, not the internal key.</summary>
    private static string DlcName(string key) => key switch
    {
        "DLC1" => "the Cupboards and Drawers DLC",
        "DLC2" => "the Seeing Stars DLC",
        _ => key,
    };
}
