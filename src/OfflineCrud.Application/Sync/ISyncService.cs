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
}

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
