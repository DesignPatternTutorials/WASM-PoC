using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Sync;

/// <summary>
/// Decides how a new local change joins the outbox entries already queued for the same product (coalescing),
/// and what sync state that leaves the product in.
/// </summary>
/// <remarks>
/// <para>A pure function, so the rules are unit tested without IndexedDB.</para>
/// <para>
/// LEARN: Coalescing trade-offs. Merging a new change into the last queued one means fewer requests and fewer chances to
/// fail. Create-then-delete never touches the server, and ten edits made offline become one PUT. The cost is that the
/// server never sees the intermediate states. That's right for state-based sync like this, where only the latest
/// details matter, but wrong if the server needs every change (an audit log, or event sourcing). An entry that is
/// already in flight is never changed, because part of it may have reached the server, so a new change queues behind it.
/// </para>
/// </remarks>
public static class OutboxPlanner
{
    /// <summary>Works out the outbox changes for a new local change to one product.</summary>
    /// <param name="existing">Every outbox entry currently queued for the product.</param>
    /// <param name="operation">The new change.</param>
    /// <param name="entityId">The product's local id.</param>
    /// <param name="payload">The product's details after the change, or <see langword="null"/> for a delete.</param>
    /// <param name="now">The current time, used for a new entry's <see cref="OutboxEntry.CreatedAt"/>.</param>
    /// <param name="newId">Creates the id of a new entry.</param>
    /// <exception cref="InvalidOperationException">The change doesn't make sense, e.g. editing a product that is being deleted.</exception>
    public static OutboxPlan Plan(
        IReadOnlyList<OutboxEntry> existing,
        OutboxOperation operation,
        Guid entityId,
        ProductDetails? payload,
        DateTimeOffset now,
        Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(newId);

        List<OutboxEntry> queue = [.. InSendOrder(existing)];
        bool anyInFlight = queue.Any(entry => entry.Status == OutboxStatus.InFlight);

        // Only the last entry can absorb the new change, and only if it isn't being sent right now. A Failed entry can
        // absorb it too: editing again is how the user fixes a change the server rejected, so it becomes pending again.
        OutboxEntry? tail = queue.Count > 0 && queue[^1].Status != OutboxStatus.InFlight ? queue[^1] : null;

        switch (operation, tail?.Operation)
        {
            case (OutboxOperation.Create, _):
                if (queue.Count > 0)
                {
                    throw new InvalidOperationException("A product can only be created once.");
                }

                return Append(queue, OutboxEntry.New(newId(), OutboxOperation.Create, entityId, payload, now));

            case (OutboxOperation.Update, OutboxOperation.Create or OutboxOperation.Update):
                // Create + update → one create with the latest details. Update + update → only the latest update.
                return Replace(queue, (tail! with { Payload = payload }).ResetToPending());

            case (OutboxOperation.Update, OutboxOperation.Delete):
                throw new InvalidOperationException("A product that is being deleted can't be edited.");

            case (OutboxOperation.Update, null):
                return Append(queue, OutboxEntry.New(newId(), OutboxOperation.Update, entityId, payload, now));

            case (OutboxOperation.Delete, OutboxOperation.Create) when !anyInFlight:
                // Create + delete before anything was sent → the server never needs to hear about it. Drop both.
                return Remove(queue, tail!);

            case (OutboxOperation.Delete, OutboxOperation.Update):
                // Update + delete → just the delete. The update would be overwritten anyway.
                return Replace(queue, (tail! with { Operation = OutboxOperation.Delete, Payload = null }).ResetToPending());

            case (OutboxOperation.Delete, OutboxOperation.Delete):
                return new OutboxPlan([], [], queue);

            case (OutboxOperation.Delete, _):
                return Append(queue, OutboxEntry.New(newId(), OutboxOperation.Delete, entityId, payload: null, now));

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown outbox operation.");
        }
    }

    /// <summary>
    /// The sync state implied by the entries still queued for a product: <see cref="SyncState.Synced"/> when there are
    /// none, <see cref="SyncState.Failed"/> if any failed, otherwise the pending state of the most recent one.
    /// </summary>
    public static SyncState DeriveSyncState(IReadOnlyCollection<OutboxEntry> queued)
    {
        ArgumentNullException.ThrowIfNull(queued);
        if (queued.Count == 0)
        {
            return SyncState.Synced;
        }

        if (queued.Any(entry => entry.Status == OutboxStatus.Failed))
        {
            return SyncState.Failed;
        }

        return InSendOrder(queued).Last().Operation switch
        {
            OutboxOperation.Create => SyncState.PendingCreate,
            OutboxOperation.Update => SyncState.PendingUpdate,
            _ => SyncState.PendingDelete,
        };
    }

    /// <summary>Orders entries the way the sync engine sends them: oldest first.</summary>
    public static IEnumerable<OutboxEntry> InSendOrder(IEnumerable<OutboxEntry> entries)
    {
        // CreatedAt alone could tie for changes made within the same millisecond; the id breaks the tie consistently.
        return entries.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id);
    }

    private static OutboxPlan Append(List<OutboxEntry> queue, OutboxEntry added)
    {
        return new OutboxPlan([added], [], [.. queue, added]);
    }

    private static OutboxPlan Replace(List<OutboxEntry> queue, OutboxEntry replacement)
    {
        return new OutboxPlan([replacement], [], [.. queue.Select(entry => entry.Id == replacement.Id ? replacement : entry)]);
    }

    private static OutboxPlan Remove(List<OutboxEntry> queue, OutboxEntry removed)
    {
        return new OutboxPlan([], [removed.Id], [.. queue.Where(entry => entry.Id != removed.Id)]);
    }
}

/// <summary>The outcome of <see cref="OutboxPlanner.Plan"/>.</summary>
/// <param name="ToSave">Entries to insert or replace.</param>
/// <param name="ToDelete">Ids of entries to remove.</param>
/// <param name="Queue">Every entry queued for the product once the plan is applied, in send order.</param>
public sealed record OutboxPlan(IReadOnlyList<OutboxEntry> ToSave, IReadOnlyList<Guid> ToDelete, IReadOnlyList<OutboxEntry> Queue)
{
    /// <summary>The product's sync state once the plan is applied.</summary>
    public SyncState SyncState => OutboxPlanner.DeriveSyncState(Queue);
}
