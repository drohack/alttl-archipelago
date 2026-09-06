namespace ALTTLArchipelago.Core;

/// <summary>
/// How many times to retry a connection, and how long to wait between tries.
///
/// Unbounded by default, backing off. Archipelago's own reference client
/// retries forever, and a bounded policy turned out to be worse than the
/// endless spinner it was avoiding: three tries against a server that comes
/// back a minute later leaves the player disconnected with no indication, and
/// the fix - press Connect - was itself broken while an attempt was in flight.
///
/// A bounded policy is still available for tests and for anyone who wants one;
/// pass a positive maxAttempts.
///
/// No timer and no threads here - it answers "should I try again, and when?"
/// and the caller supplies the clock. That is what makes the schedule testable
/// without a game or a two-minute test run.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>maxAttempts value meaning "keep going until told to stop".</summary>
    public const int Unlimited = 0;

    public const int DefaultMaxAttempts = Unlimited;

    private readonly int _maxAttempts;
    private readonly double _firstDelaySeconds;
    private readonly double _maxDelaySeconds;

    /// <summary>
    /// Unlimited attempts, first wait 5s, doubling to a 60s ceiling: 5, 10,
    /// 20, 40, 60, 60, ... which is the schedule cw4-archipelago settled on
    /// for the same reasons. Pure doubling without the cap reaches an hour by
    /// the twelfth attempt, which is indistinguishable from having given up.
    /// </summary>
    public RetryPolicy(int maxAttempts = DefaultMaxAttempts,
                       double firstDelaySeconds = 5.0,
                       double maxDelaySeconds = 60.0)
    {
        // 0 (Unlimited) is meaningful and must survive; only a negative is
        // nonsense. This used to clamp anything below 1 up to 1, which would
        // silently turn "forever" into "once".
        _maxAttempts = maxAttempts < 0 ? 0 : maxAttempts;
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

    /// <summary>
    /// True once the attempts are spent and the caller should stop. Never true
    /// for an unlimited policy - only a deliberate cancel or disconnect stops
    /// that one.
    /// </summary>
    public bool GaveUp => _maxAttempts != Unlimited && Attempts >= _maxAttempts;

    /// <summary>True when this policy will keep trying indefinitely.</summary>
    public bool IsUnlimited => _maxAttempts == Unlimited;

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
    /// Doubling, capped. The cap is what keeps an unlimited policy usable: a
    /// wait that keeps growing eventually stops looking like "reconnecting"
    /// and starts looking like "hung".
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
