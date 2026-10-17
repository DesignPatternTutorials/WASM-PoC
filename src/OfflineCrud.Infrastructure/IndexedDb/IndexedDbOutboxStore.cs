using OfflineCrud.Application.Sync;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.IndexedDb;

/// <summary><see cref="IOutboxStore"/> over IndexedDB, in the same database (and transactions) as the products.</summary>
internal sealed class IndexedDbOutboxStore : IOutboxStore
{
    private readonly IndexedDbModule _module;

    public IndexedDbOutboxStore(IndexedDbModule module)
    {
        _module = module;
    }

    public async Task<IReadOnlyList<OutboxEntry>> GetAllAsync(CancellationToken cancellationToken)
    {
        OutboxRecord[] records = await _module.InvokeAsync<OutboxRecord[]>("getOutboxEntries", cancellationToken);
        return [.. OutboxPlanner.InSendOrder(records.Select(OutboxRecordMapper.ToEntry))];
    }

    public async Task<IReadOnlyList<OutboxEntry>> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken)
    {
        OutboxRecord[] records = await _module.InvokeAsync<OutboxRecord[]>("getOutboxEntriesForEntity", cancellationToken, entityId);
        return [.. OutboxPlanner.InSendOrder(records.Select(OutboxRecordMapper.ToEntry))];
    }

    public async Task CommitAsync(
        IReadOnlyCollection<Product> products,
        IReadOnlyCollection<OutboxEntry> entriesToSave,
        IReadOnlyCollection<Guid> entryIdsToDelete,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0 && entriesToSave.Count == 0 && entryIdsToDelete.Count == 0)
        {
            return;
        }

        ProductRecord[] productRecords = [.. products.Select(ProductRecordMapper.ToRecord)];
        OutboxRecord[] entryRecords = [.. entriesToSave.Select(OutboxRecordMapper.ToRecord)];
        Guid[] deletes = [.. entryIdsToDelete];

        // CancellationToken.None: once the decision to commit is made, the write must not be abandoned halfway on the
        // .NET side while the JS transaction carries on regardless. The caller then wouldn't know whether it happened.
        await _module.InvokeVoidAsync("commit", CancellationToken.None, productRecords, entryRecords, deletes);
    }
}
