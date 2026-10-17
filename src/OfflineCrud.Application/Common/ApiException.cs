namespace OfflineCrud.Application.Common;

/// <summary>
/// A failed call to the remote API, classified so callers don't need to know about HTTP.
/// </summary>
/// <remarks>
/// The classification is what the Phase 3 sync engine will use to decide between retrying
/// (<see cref="IsTransient"/>) and giving up with a readable reason.
/// </remarks>
public sealed class ApiException : Exception
{
    /// <summary>Creates an exception describing a failed API call.</summary>
    /// <param name="kind">The category of failure.</param>
    /// <param name="message">A human-readable description, ideally the server's own message.</param>
    /// <param name="statusCode">The HTTP status code, or <see langword="null"/> if no response was received.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public ApiException(ApiFailureKind kind, string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    /// <summary>The category of failure.</summary>
    public ApiFailureKind Kind { get; }

    /// <summary>The HTTP status code, or <see langword="null"/> if no response was received.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Whether trying the same request again later might succeed: true for network errors,
    /// 5xx responses and rate limiting.
    /// </summary>
    public bool IsTransient => Kind is ApiFailureKind.Network or ApiFailureKind.Server or ApiFailureKind.RateLimited;
}
