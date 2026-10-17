namespace OfflineCrud.Application.Sync;

/// <summary>How long to wait before retrying an outbox entry after a temporary failure.</summary>
/// <remarks>
/// LEARN: Exponential backoff with jitter. Doubling the delay after each failure stops a struggling server from being
/// hammered. Randomising it ("jitter") stops every client that went offline together from retrying in the same instant
/// when the connection returns (the "thundering herd"). This uses "equal jitter": half the delay is fixed, half random.
/// </remarks>
public static class RetryPolicy
{
    /// <summary>The delay before the first retry, before jitter.</summary>
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>The longest delay between retries.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>Computes the delay before the next attempt.</summary>
    /// <param name="failedAttempts">How many attempts have failed so far, including the one that just failed. At least 1.</param>
    /// <param name="jitter">A random value in [0, 1).</param>
    public static TimeSpan DelayFor(int failedAttempts, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(jitter);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitter, 1d);

        // 2s, 4s, 8s, ... capped. The exponent is capped too, so a large attempt count can't overflow.
        double exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failedAttempts - 1, 20));
        double capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds((capped / 2) + (capped / 2 * jitter));
    }
}
