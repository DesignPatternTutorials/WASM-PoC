namespace OfflineCrud.Domain.Products;

/// <summary>
/// Where a locally stored product stands relative to the server.
/// </summary>
/// <remarks>
/// A change is saved on this device first and sent later by the outbox, so the local copy can be ahead of the
/// server. The state is derived from the outbox entries still queued for the product.
/// </remarks>
public enum SyncState
{
    /// <summary>The local copy matches the last version the server confirmed.</summary>
    Synced,

    /// <summary>Created on this device; the server doesn't know about it yet.</summary>
    PendingCreate,

    /// <summary>Changed on this device; the change hasn't been sent yet.</summary>
    PendingUpdate,

    /// <summary>Deleted on this device; the delete hasn't been sent yet.</summary>
    PendingDelete,

    /// <summary>The server permanently rejected the last change.</summary>
    Failed,
}
