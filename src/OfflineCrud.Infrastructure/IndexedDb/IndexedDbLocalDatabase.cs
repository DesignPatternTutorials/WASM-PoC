using OfflineCrud.Application.Common;

namespace OfflineCrud.Infrastructure.IndexedDb;

/// <summary><see cref="ILocalDatabase"/> over the IndexedDB database used by the products and the outbox.</summary>
internal sealed class IndexedDbLocalDatabase : ILocalDatabase
{
    private readonly IndexedDbModule _module;

    public IndexedDbLocalDatabase(IndexedDbModule module)
    {
        _module = module;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        return _module.InvokeVoidAsync("clearAll", CancellationToken.None);
    }
}
