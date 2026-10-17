using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Sync;

/// <summary>
/// Port for the outbox stored on this device. Implemented in Infrastructure over IndexedDB, in the same database as the
/// products, so a product change and its outbox entry can be written in one transaction.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Gets every outbox entry, oldest first.</summary>
    Task<IReadOnlyList<OutboxEntry>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Gets the outbox entries queued for one product, oldest first.</summary>
    Task<IReadOnlyList<OutboxEntry>> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes products and outbox changes in one transaction: either all of them are applied or none are.
    /// </summary>
    /// <remarks>
    /// LEARN: This is the heart of the pattern. If the product were saved but the entry wasn't, the UI would show a
    /// change that is never sent. If the entry were saved but the product wasn't, a change would be sent that the user
    /// can't see. One transaction makes both impossible, even if the tab is closed halfway through.
    /// </remarks>
    Task CommitAsync(
        IReadOnlyCollection<Product> products,
        IReadOnlyCollection<OutboxEntry> entriesToSave,
        IReadOnlyCollection<Guid> entryIdsToDelete,
        CancellationToken cancellationToken);
}
