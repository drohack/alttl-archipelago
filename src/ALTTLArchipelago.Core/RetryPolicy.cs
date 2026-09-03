namespace ALTTLArchipelago.Core;

/// <summary>
/// How many times to retry a connection, and how long to wait between tries.
///
/// A fixed number of attempts and then a stop, deliberately: a server that is
/// genuinely gone should not be hammered, and a player staring at a menu should
/// be told what happened rather than left watching an endless spinner.
///
/// No timer and no threads here - it answers "should I try again, and when?"
/// and the caller supplies the clock. That is what makes the schedule testable
/// without a game or a two-minute test run.
/// </summary>
public sealed class RetryPolicy
{
    public const int DefaultMaxAttempts = 3;

    private readonly int _maxAttempts;
    private readonly double _firstDelaySeconds;
    private readonly double _maxDelaySeconds;

    /// <summary>
    /// Three attempts, first wait 3s, capped at 30s.
    ///
    /// Chosen from play rather than theory: five attempts starting a second
    /// apart hammered a dead server and read as thrashing. Three tries with a
    /// slower start looks like a considered reconnect and still covers a brief
    /// network blip.
    /// </summary>
    public RetryPolicy(int maxAttempts = DefaultMaxAttempts,
                       double firstDelaySeconds = 3.0,
                       double maxDelaySeconds = 30.0)
    {
        _maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        _firstDelaySeconds = firstDelaySeconds <= 0 ? 1.0 : firstDelaySeconds;
        // Clamped against the ALREADY-CLAMPED first delay, not the raw
        // argument: firstDelaySeconds of 0 becomes 1, and comparing against
        // the 0 left a max delay of 0 and every wait collapsing to nothing.
        _maxDelaySeconds = maxDelaySeconds < _firstDelaySeconds
            ? _firstDelaySeconds
            : maxDelaySeconds;
    }

    /// <summary>Attempts made since the last reset. 0 before the first try.</summary>
    public int Attempts { get; private set; }

    /// <summary>Why the last attempt failed, for the connection pane to show.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>True once the attempts are spent and the caller should stop.</summary>
    public bool GaveUp => Attempts >= _maxAttempts;

    public int MaxAttempts => _maxAttempts;

    /// <summary>
    /// Record a failure. Returns the seconds to wait before trying again, or
    /// null when the attempts are spent.
    /// </summary>
    public double? Failed(string error)
    {
        LastError = error ?? "";
        Attempts++;
        return GaveUp ? null : DelayForAttempt(Attempts);
    }

    /// <summary>
    /// Doubling, capped. Capped rather than unbounded because a growing wait
    /// eventually stops looking like "reconnecting" and starts looking like
    /// "hung", and there are only a handful of attempts anyway.
    /// </summary>
    public double DelayForAttempt(int attempt)
    {
        if (attempt < 1) attempt = 1;

        var delay = _firstDelaySeconds;
        for (int i = 1; i < attempt && delay < _maxDelaySeconds; i++)
        {
            delay *= 2;
        }
        return delay > _maxDelaySeconds ? _maxDelaySeconds : delay;
    }

    /// <summary>
    /// Back to the start. Called on a successful connect and whenever the
    /// player asks to connect by hand - pressing the button is a statement
    /// that the situation has changed, so it always gets a full set of tries.
    /// </summary>
    public void Reset()
    {
        Attempts = 0;
        LastError = "";
    }

    /// <summary>One line for the connection pane.</summary>
    public string Describe(bool connected, string slotName)
    {
        if (connected) return $"Connected as {slotName}";
        if (Attempts == 0) return "Not connected";
        if (GaveUp)
        {
            return LastError.Length > 0
                ? $"Gave up after {Attempts} attempts: {LastError}"
                : $"Gave up after {Attempts} attempts";
        }
        return $"Reconnecting, attempt {Attempts + 1} of {_maxAttempts}";
    }
}
