using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALTTLArchipelago.Core;

/// <summary>
/// The little that has to outlive the process but does not belong to the game.
///
/// Four things so far:
///
/// - checks earned but not yet accepted by the server. Without them, playing
///   offline and quitting loses everything earned in that session: the ledger
///   is in memory, and the server never heard about any of it.
/// - how many Skips have been spent. Archipelago replays the ITEMS on
///   reconnect, so the number held is always recoverable - but nothing tells
///   us how many were used, and without that every reconnect would hand them
///   all back.
/// - which hint pages have been paid for, and which puzzles are beaten.
/// - whether the credits have been played, which is what lets a run finished
///   offline still report its goal.
///
/// THE DECISIONS, NOT THE DISK. <c>RunState</c> in the plugin owns the path,
/// the atomic write and the logging; everything about what this file CONTAINS
/// and when it has changed is decided here, where it is tested without a game.
/// That split is the same one <c>SlotCache</c> makes against
/// <see cref="CachedSession"/>, and it exists because the Core csproj records
/// what the alternative cost: the run state was one of four Unity-free files
/// left stranded in the plugin with no tests, and two of the five bugs a
/// previous audit found were in exactly that untested code. One of them was
/// <see cref="CreditsPlayed"/>.
///
/// EVERY MUTATOR RETURNS WHETHER ANYTHING CHANGED, so the caller writes only
/// when there is something to write. That is not a micro-optimisation: the
/// flush that calls <see cref="SetOwed"/> runs on a timer, so without it an
/// offline session sits re-saving an identical list every few seconds.
/// </summary>
public sealed class RunStateData
{
    /// <summary>
    /// Trailing commas and unknown fields tolerated on read, because this file
    /// outlives the version that wrote it. A run file written by an older mod
    /// must degrade to "lost what it held" at worst, never to a crash.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false,
    };

    /// <summary>Checks earned but not yet accepted by the server.</summary>
    [JsonPropertyName("owed")]
    public List<string> Owed { get; set; } = new();

    [JsonPropertyName("skipsUsed")]
    public int SkipsUsed { get; set; }

    /// <summary>
    /// Traps that have already gone off.
    ///
    /// Archipelago replays the whole item list on every reconnect, so without
    /// this every cat you have ever been sent fires again the moment you log
    /// back in - and since that happens before a puzzle is open, they are all
    /// spent as misses and a genuinely new trap has nothing left to do.
    /// </summary>
    [JsonPropertyName("trapsSprung")]
    public int TrapsSprung { get; set; }

    /// <summary>
    /// Event locations we have collected - the Beaten tokens.
    ///
    /// The only checks nothing else can restore. They have no address, so the
    /// server never lists them back at login, and they are never owed because
    /// sending one can only ever be rejected. Both of the things that rebuild
    /// a ledger therefore miss them.
    ///
    /// Without this the beaten count returned to zero on every login, so the
    /// credits goal could only be reached inside one unbroken session - and
    /// the track's badges forgot which puzzles were finished.
    /// </summary>
    [JsonPropertyName("beaten")]
    public List<string> Beaten { get; set; } = new();

    /// <summary>
    /// Hint pages already paid for, as "slot:page".
    ///
    /// A set of keys rather than a spent COUNT, and the difference is the
    /// whole point: a page you have opened must stay open. With a counter,
    /// leaving a puzzle and coming back would charge a second Hint Page for a
    /// page you had already read, and re-reading your own hint is not
    /// something a player should pay for twice. The length of this list is
    /// what has been spent, so one field answers both questions.
    ///
    /// Keyed by SLOT, not level index: a generator can be drawn several times
    /// into one run, and each instance has its own notepad.
    ///
    /// A missing key deserialises to an empty list, so run files written
    /// before this existed stay valid.
    /// </summary>
    [JsonPropertyName("hintPages")]
    public List<string> HintPages { get; set; } = new();

    /// <summary>
    /// Whether the player has played the credits through to the end.
    ///
    /// THE GOAL CANNOT BE REPORTED WITHOUT THIS, which is why it has to
    /// outlive the process. Reporting requires the credits to have been
    /// PLAYED, and that flag lived only in GoalLatch, which Credits.Reset()
    /// replaces wholesale on every reconnect and every offline start. So a
    /// player who finished the run offline, played the credits and then
    /// reconnected had the fact wiped, and the goal was never sent - the
    /// multiworld waiting forever on a slot that had genuinely finished.
    /// Relaunching between playing the credits and reporting did the same.
    ///
    /// It also does the work an acknowledgement would. The client library
    /// offers SetGoalAchieved and no async or callback form, so a send that
    /// left is the strongest signal available and the report latch cannot
    /// honestly wait for more. Because this survives, every reconnect re-arms
    /// the report and sends again until one lands, and the server takes a
    /// repeated goal as idempotent.
    ///
    /// A missing key deserialises to false, so run files written before this
    /// existed stay valid - such a player re-plays the credits card, which is
    /// a click.
    /// </summary>
    [JsonPropertyName("creditsPlayed")]
    public bool CreditsPlayed { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Null when the text is not a run file at all, rather than throwing.
    ///
    /// A corrupt file must not stop the game starting. What it held is lost,
    /// which is bad; a crash loop is worse.
    /// </summary>
    public static RunStateData? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RunStateData>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How many Hint Pages have been spent.</summary>
    [JsonIgnore]
    public int HintPagesOpened => HintPages.Count;

    /// <summary>Whether anything here is worth mentioning in the log.</summary>
    [JsonIgnore]
    public bool HasProgress
        => Owed.Count > 0 || SkipsUsed > 0 || TrapsSprung > 0
           || Beaten.Count > 0 || HintPages.Count > 0;

    /// <summary>One line for the log, when <see cref="HasProgress"/>.</summary>
    public string Summary()
        => $"{Owed.Count} check(s) still owed, "
           + $"{SkipsUsed} skip(s) used, "
           + $"{TrapsSprung} trap(s) already sprung, "
           + $"{HintPages.Count} hint page(s) opened, "
           + $"{Beaten.Count} puzzle(s) beaten";

    /// <summary>Record that the credits were played. Idempotent.</summary>
    /// <returns>True when this changed something.</returns>
    public bool NoteCreditsPlayed()
    {
        if (CreditsPlayed) return false;
        CreditsPlayed = true;
        return true;
    }

    /// <summary>Has this page already been paid for?</summary>
    public bool IsHintPageOpen(string key) => HintPages.Contains(key);

    /// <summary>
    /// Pay for a page. Returns false if it was already open - or if the key is
    /// empty - so the caller cannot double charge by calling twice.
    /// </summary>
    public bool OpenHintPage(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (HintPages.Contains(key)) return false;
        HintPages.Add(key);
        return true;
    }

    /// <returns>True when the list differs from the one already held.</returns>
    public bool SetOwed(IReadOnlyList<string> owed)
    {
        if (Same(Owed, owed)) return false;
        Owed = new List<string>(owed);
        return true;
    }

    /// <returns>True when the list differs from the one already held.</returns>
    public bool SetBeaten(IReadOnlyList<string> beaten)
    {
        if (Same(Beaten, beaten)) return false;
        Beaten = new List<string>(beaten);
        return true;
    }

    /// <returns>Always true - a spent Skip always changes the count.</returns>
    public bool SpendSkip()
    {
        SkipsUsed++;
        return true;
    }

    /// <returns>False for a count of zero or less, which changes nothing.</returns>
    public bool SpendTrap(int count)
    {
        if (count <= 0) return false;
        TrapsSprung += count;
        return true;
    }

    /// <summary>
    /// ORDER MATTERS, deliberately. These lists are written and read back as
    /// sequences, and a reordering is a real difference worth persisting -
    /// treating them as sets here would make a reordered ledger compare equal
    /// and silently skip the write that records it.
    /// </summary>
    private static bool Same(List<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
