using System.Globalization;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Sync;

/// <summary>
/// The sync engine: replays outbox entries against the server, in order per product, with retries.
/// </summary>
/// <remarks>
/// <para>For each entry that is due, oldest first:</para>
/// <list type="number">
/// <item>Mark it in flight, so a crash mid-request is visible on the next start.</item>
/// <item>Send it, with the entry's id as the <c>Idempotency-Key</c>.</item>
/// <item>
/// On success, remove the entry and update the product (server id, sync state) in one transaction. On a temporary
/// failure (network, 5xx, 429), schedule a retry with backoff. On a permanent failure (other 4xx, unreadable response),
/// mark it <see cref="OutboxStatus.Failed"/> with a readable reason and stop retrying.
/// </item>
/// </list>
/// <para>
/// Products are independent, so a failure only holds back later entries for the same product. Their order matters
/// (an update can't be sent before its create), but other products carry on.
/// </para>
/// </remarks>
public sealed class SyncService : ISyncService, IDisposable
{
    private readonly IOutboxStore _outbox;
    private readonly IProductRepository _products;
    private readonly IProductApiClient _apiClient;
    private readonly LocalStoreLock _storeLock;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;

    // Guards against overlapping runs (a timer tick and a "Sync now" click at the same time, say). Two runs could
    // otherwise both pick up the same entry and send it twice.
    private readonly SemaphoreSlim _runGate = new(1, 1);

    /// <summary>Creates the sync engine.</summary>
    public SyncService(
        IOutboxStore outbox,
        IProductRepository products,
        IProductApiClient apiClient,
        LocalStoreLock storeLock,
        TimeProvider timeProvider,
        Random random)
    {
        _outbox = outbox;
        _products = products;
        _apiClient = apiClient;
        _storeLock = storeLock;
        _timeProvider = timeProvider;
        _random = random;
    }

    private enum SendOutcome
    {
        Sent,
        Retrying,
        Failed,

        /// <summary>The entry was removed or changed state after the run started, so it wasn't sent.</summary>
        Gone,
    }

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using IDisposable _ = await _storeLock.AcquireAsync(cancellationToken);

        // LEARN: An entry still InFlight at start-up means the tab closed while its request was out. The server may or
        // may not have applied it, and there's no way to know. Sending it again is only safe because it carries the same
        // Idempotency-Key, which a real server uses to recognise the repeat and not apply it twice. DummyJSON ignores the
        // header, but it doesn't persist anything either.
        OutboxEntry[] stuck = [.. (await _outbox.GetAllAsync(cancellationToken)).Where(entry => entry.Status == OutboxStatus.InFlight)];
        if (stuck.Length > 0)
        {
            await _outbox.CommitAsync([], [.. stuck.Select(entry => entry with { Status = OutboxStatus.Pending })], [], cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken))
        {
            return SyncRunResult.AlreadyRunning;
        }

        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync(CancellationToken cancellationToken)
    {
        return _outbox.GetAllAsync(cancellationToken);
    }

    /// <summary>Releases the run gate.</summary>
    public void Dispose() => _runGate.Dispose();

    private async Task<SyncRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<OutboxEntry> entries = await _outbox.GetAllAsync(cancellationToken);

        // Products whose remaining entries must wait, because an earlier one for the same product didn't go through.
        HashSet<Guid> heldBack = [];
        int sent = 0, retrying = 0, failed = 0;

        foreach (OutboxEntry entry in OutboxPlanner.InSendOrder(entries))
        {
            if (heldBack.Contains(entry.EntityId)
                || entry.Status == OutboxStatus.Failed
                || entry.NextAttemptAt > now)
            {
                heldBack.Add(entry.EntityId);
                continue;
            }

            (SendOutcome outcome, ApiException? error) = await SendAsync(entry, cancellationToken);
            switch (outcome)
            {
                case SendOutcome.Sent:
                    sent++;
                    break;
                case SendOutcome.Retrying:
                    retrying++;
                    heldBack.Add(entry.EntityId);
                    break;
                case SendOutcome.Failed:
                    failed++;
                    heldBack.Add(entry.EntityId);
                    break;
                default:
                    // Gone: whatever replaced it will be picked up by the next run.
                    heldBack.Add(entry.EntityId);
                    break;
            }

            // Offline (or the server is unreachable): every other request would fail the same way, so stop here rather
            // than burn through the queue adding attempts to each entry. The next trigger will try again.
            if (error?.Kind == ApiFailureKind.Network)
            {
                break;
            }
        }

        // Everything not sent, retried or failed in this run is still waiting: held back, in backoff, or not reached.
        return new SyncRunResult(sent, retrying, failed, entries.Count - sent - retrying - failed);
    }

    private async Task<(SendOutcome Outcome, ApiException? Error)> SendAsync(OutboxEntry snapshot, CancellationToken cancellationToken)
    {
        OutboxEntry entry;
        Product? product;
        using (await _storeLock.AcquireAsync(cancellationToken))
        {
            // Re-read the entry: since the run took its snapshot, the user may have merged a newer edit into it, or
            // removed it entirely (create + delete). Sending the snapshot would send stale data.
            OutboxEntry? current = (await _outbox.GetForEntityAsync(snapshot.EntityId, cancellationToken))
                .FirstOrDefault(other => other.Id == snapshot.Id);
            if (current is not { Status: OutboxStatus.Pending })
            {
                return (SendOutcome.Gone, null);
            }

            product = await _products.GetAsync(current.EntityId, cancellationToken);
            entry = current with { Status = OutboxStatus.InFlight };
            await _outbox.CommitAsync([], [entry], [], cancellationToken);
        }

        try
        {
            int? serverId = await SendToServerAsync(entry, product, cancellationToken);
            await CompleteAsync(entry, serverId, cancellationToken);
            return (SendOutcome.Sent, null);
        }
        catch (ApiException ex) when (ex.IsTransient)
        {
            await RescheduleAsync(entry, ex, cancellationToken);
            return (SendOutcome.Retrying, ex);
        }
        catch (ApiException ex)
        {
            await FailAsync(entry, Describe(entry, product, ex), cancellationToken);
            return (SendOutcome.Failed, ex);
        }
        catch (OperationCanceledException)
        {
            // Cancelled mid-request (e.g. the app is shutting down). Put the entry back so it's sent next time.
            // CancellationToken.None: the caller's token is already cancelled, and this write must still happen.
            await CommitEntryAsync(entry with { Status = OutboxStatus.Pending }, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Sends one entry. Returns the server id assigned by a create, otherwise <see langword="null"/>.</summary>
    private async Task<int?> SendToServerAsync(OutboxEntry entry, Product? product, CancellationToken cancellationToken)
    {
        switch (entry.Operation)
        {
            case OutboxOperation.Create:
                ServerProduct created = await _apiClient.CreateAsync(entry.Payload!, entry.Id, cancellationToken);
                return created.ServerId;

            case OutboxOperation.Update:
                int updateId = product?.ServerId
                    ?? throw new ApiException(ApiFailureKind.Rejected, "This product has no server id, so the server can't be asked to update it.");
                await _apiClient.UpdateAsync(updateId, entry.Payload!, entry.Id, cancellationToken);
                return null;

            default:
                if (product?.ServerId is not int deleteId)
                {
                    // The server never had it, so there's nothing to delete there.
                    return null;
                }

                try
                {
                    await _apiClient.DeleteAsync(deleteId, entry.Id, cancellationToken);
                }
                catch (ApiException ex) when (ex.Kind == ApiFailureKind.NotFound)
                {
                    // DELETE is idempotent: the goal is "the server doesn't have it", and a 404 says exactly that.
                    // (This is also what happens with products created through DummyJSON, which it never stored.)
                }

                return null;
        }
    }

    private async Task CompleteAsync(OutboxEntry entry, int? serverId, CancellationToken cancellationToken)
    {
        using IDisposable _ = await _storeLock.AcquireAsync(cancellationToken);

        // Re-read both inside the lock: the user may have changed the product while the request was in flight. That
        // change queued a new entry, which is why the state is derived from what's left rather than set to Synced.
        Product? product = await _products.GetAsync(entry.EntityId, cancellationToken);
        IReadOnlyList<OutboxEntry> remaining =
            [.. (await _outbox.GetForEntityAsync(entry.EntityId, cancellationToken)).Where(other => other.Id != entry.Id)];

        if (product is null)
        {
            await _outbox.CommitAsync([], [], [entry.Id], cancellationToken);
            return;
        }

        // Only the server id is taken from a create's response. The local details may already be newer than what was
        // sent, and DummyJSON's response has no version to keep.
        if (serverId is int id)
        {
            product.RecordServerId(id);
        }

        product.SetSyncState(OutboxPlanner.DeriveSyncState(remaining));
        await _outbox.CommitAsync([product], [], [entry.Id], cancellationToken);
    }

    private async Task RescheduleAsync(OutboxEntry entry, ApiException error, CancellationToken cancellationToken)
    {
        int attempts = entry.Attempts + 1;
        TimeSpan delay = RetryPolicy.DelayFor(attempts, _random.NextDouble());
        OutboxEntry rescheduled = entry with
        {
            Status = OutboxStatus.Pending,
            Attempts = attempts,
            NextAttemptAt = _timeProvider.GetUtcNow() + delay,
            LastError = string.Create(CultureInfo.InvariantCulture, $"{error.Message} Retrying in {delay.TotalSeconds:0} s (attempt {attempts})."),
        };

        await CommitEntryAsync(rescheduled, cancellationToken);
    }

    private async Task FailAsync(OutboxEntry entry, string reason, CancellationToken cancellationToken)
    {
        using IDisposable _ = await _storeLock.AcquireAsync(cancellationToken);

        OutboxEntry failed = entry with { Status = OutboxStatus.Failed, LastError = reason };
        Product? product = await _products.GetAsync(entry.EntityId, cancellationToken);
        product?.SetSyncState(SyncState.Failed);
        await _outbox.CommitAsync(product is null ? [] : [product], [failed], [], cancellationToken);
    }

    private async Task CommitEntryAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        using IDisposable _ = await _storeLock.AcquireAsync(cancellationToken);
        await _outbox.CommitAsync([], [entry], [], cancellationToken);
    }

    /// <summary>Turns a permanent failure into a reason the user can act on.</summary>
    private static string Describe(OutboxEntry entry, Product? product, ApiException error)
    {
        string operation = entry.Operation.ToString().ToLowerInvariant();
        return error.Kind switch
        {
            ApiFailureKind.NotFound => string.Create(
                CultureInfo.InvariantCulture,
                $"The server has no product #{product?.ServerId}, so it can't apply this {operation}. DummyJSON doesn't store products created through its API, so this is expected for those."),
            ApiFailureKind.InvalidResponse => $"The server accepted the {operation} but sent a response that couldn't be read: {error.Message}",
            _ => string.Create(CultureInfo.InvariantCulture, $"The server rejected the {operation} ({error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "no status"}): {error.Message}"),
        };
    }
}
