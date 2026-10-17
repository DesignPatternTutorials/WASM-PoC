using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Sync;

/// <summary>The kind of change an outbox entry asks the server to make.</summary>
public enum OutboxOperation
{
    /// <summary>Create the product on the server.</summary>
    Create,

    /// <summary>Replace the product's details on the server.</summary>
    Update,

    /// <summary>Delete the product on the server.</summary>
    Delete,
}

/// <summary>Where an outbox entry is in its life.</summary>
public enum OutboxStatus
{
    /// <summary>Waiting to be sent, possibly after a backoff delay (see <see cref="OutboxEntry.NextAttemptAt"/>).</summary>
    Pending,

    /// <summary>Being sent right now.</summary>
    InFlight,

    /// <summary>The server permanently rejected it. It won't be retried automatically.</summary>
    Failed,
}

/// <summary>
/// One change made on this device that the server hasn't confirmed yet.
/// </summary>
/// <param name="Id">The entry's id. Also sent as the request's <c>Idempotency-Key</c>.</param>
/// <param name="Operation">What to ask the server to do.</param>
/// <param name="EntityId">The local id of the product the change applies to.</param>
/// <param name="Payload">The product's details to send, or <see langword="null"/> for a delete.</param>
/// <param name="CreatedAt">When the change was first queued. Entries for one product are sent in this order.</param>
/// <param name="Attempts">How many times sending has failed with a temporary error.</param>
/// <param name="NextAttemptAt">The earliest time to try again after a temporary error, or <see langword="null"/> to send as soon as possible.</param>
/// <param name="LastError">A readable description of the last failure, if any.</param>
/// <param name="Status">Where the entry is in its life.</param>
public sealed record OutboxEntry(
    Guid Id,
    OutboxOperation Operation,
    Guid EntityId,
    ProductDetails? Payload,
    DateTimeOffset CreatedAt,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    OutboxStatus Status)
{
    /// <summary>Creates a new pending entry.</summary>
    public static OutboxEntry New(Guid id, OutboxOperation operation, Guid entityId, ProductDetails? payload, DateTimeOffset now)
    {
        if (operation != OutboxOperation.Delete)
        {
            ArgumentNullException.ThrowIfNull(payload);
        }

        return new OutboxEntry(id, operation, entityId, payload, now, Attempts: 0, NextAttemptAt: null, LastError: null, OutboxStatus.Pending);
    }

    /// <summary>A copy that is pending and ready to send now, with its retry history cleared.</summary>
    public OutboxEntry ResetToPending() => this with { Status = OutboxStatus.Pending, Attempts = 0, NextAttemptAt = null, LastError = null };
}
