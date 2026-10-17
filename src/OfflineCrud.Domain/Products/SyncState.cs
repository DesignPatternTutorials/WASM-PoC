namespace OfflineCrud.Domain.Products;

/// <summary>
/// Where a locally stored product stands relative to the server.
/// </summary>
/// <remarks>
/// Phase 2 only produces <see cref="Synced"/>, because writes still go to the API before they're stored.
/// The other states arrive with the outbox in Phase 3, when a change can exist on this device before
/// the server has seen it.
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
