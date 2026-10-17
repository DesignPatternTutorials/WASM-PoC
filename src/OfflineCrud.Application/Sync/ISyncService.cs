namespace OfflineCrud.Application.Sync;

/// <summary>Sends queued local changes to the server.</summary>
public interface ISyncService
{
    /// <summary>
    /// Returns entries left <see cref="OutboxStatus.InFlight"/> by a previous session (the tab closed mid-request)
    /// to <see cref="OutboxStatus.Pending"/>, so they are sent again. Call once at start-up, before the first run.
    /// </summary>
    Task RecoverAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends every entry that is due, oldest first per product. If a run is already in progress, returns at once
    /// with <see cref="SyncRunResult.Skipped"/> set.
    /// </summary>
    Task<SyncRunResult> RunAsync(CancellationToken cancellationToken);

    /// <summary>Gets every queued entry, oldest first.</summary>
    Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync(CancellationToken cancellationToken);

    /// <summary>Gets every queued entry with the product it belongs to, oldest first. For the sync page.</summary>
    Task<IReadOnlyList<OutboxItem>> GetOutboxItemsAsync(CancellationToken cancellationToken);

    /// <summary>Makes a <see cref="OutboxStatus.Failed"/> entry pending again, so the next run sends it.</summary>
    /// <exception cref="InvalidOperationException">The entry doesn't exist or hasn't failed.</exception>
    Task RetryAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives up on a <see cref="OutboxStatus.Failed"/> change and removes it from the outbox. A discarded create removes
    /// the product from this device; a discarded update or delete lets the server's copy replace the local one on the
    /// next refresh.
    /// </summary>
    /// <returns>The local id of the product the entry belonged to.</returns>
    /// <exception cref="InvalidOperationException">The entry doesn't exist or hasn't failed.</exception>
    Task<Guid> DiscardAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes everything stored on this device, including unsent changes. Waits for any run in progress to finish first.
    /// </summary>
    Task ClearLocalDataAsync(CancellationToken cancellationToken);
}

/// <summary>An outbox entry together with what the sync page needs to describe it.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="ProductTitle">The product's title on this device, or the title being sent if the product is gone.</param>
/// <param name="ServerId">The product's server id, if it has one.</param>
public sealed record OutboxItem(OutboxEntry Entry, string ProductTitle, int? ServerId);

/// <summary>What a sync run did.</summary>
/// <param name="Sent">Entries the server accepted.</param>
/// <param name="Retrying">Entries that hit a temporary error and are scheduled for a retry.</param>
/// <param name="Failed">Entries the server permanently rejected in this run.</param>
/// <param name="Waiting">Entries not sent because they are in backoff, already failed, or queued behind one that is.</param>
/// <param name="Skipped">Whether the run didn't happen because another was already in progress.</param>
public sealed record SyncRunResult(int Sent, int Retrying, int Failed, int Waiting, bool Skipped = false)
{
    /// <summary>A run that didn't happen because another was in progress.</summary>
    public static SyncRunResult AlreadyRunning { get; } = new(0, 0, 0, 0, Skipped: true);
}
