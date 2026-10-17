using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the IndexedDB stores, implementing both ports over one "database".
/// </summary>
/// <remarks>
/// Products are stored as copies, like IndexedDB's structured clones. Code that mutates a product but never commits it
/// therefore doesn't appear to have saved it, which is the bug these tests need to catch.
/// </remarks>
internal sealed class InMemoryLocalStore : IProductRepository, IOutboxStore, ILocalDatabase
{
    private readonly Dictionary<Guid, Product> _products = [];
    private readonly Dictionary<Guid, OutboxEntry> _outbox = [];

    /// <summary>Runs inside every <see cref="CommitAsync"/>, before it applies, so tests can see each commit.</summary>
    public Action? OnCommit { get; set; }

    public int CommitCount { get; private set; }

    public IReadOnlyCollection<OutboxEntry> Entries => _outbox.Values;

    public void Seed(Product product, params OutboxEntry[] entries)
    {
        _products[product.Id] = Copy(product);
        foreach (OutboxEntry entry in entries)
        {
            _outbox[entry.Id] = entry;
        }
    }

    public void SeedEntry(OutboxEntry entry) => _outbox[entry.Id] = entry;

    public Product? Product(Guid id) => _products.TryGetValue(id, out Product? product) ? Copy(product) : null;

    public OutboxEntry? Entry(Guid id) => _outbox.GetValueOrDefault(id);

    // IProductRepository

    public Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        Product[] active = [.. Active()];
        return Task.FromResult(new PagedResult<Product>([.. active.Skip(skip).Take(take)], active.Length, skip, take));
    }

    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>([.. Active()]);

    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Product(id));

    public Task<IReadOnlyList<Product>> GetByServerIdsAsync(IReadOnlyCollection<int> serverIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(
            [.. _products.Values.Where(product => product.ServerId is int id && serverIds.Contains(id)).Select(Copy)]);

    public Task SaveAsync(IReadOnlyCollection<Product> products, CancellationToken cancellationToken) =>
        CommitAsync(products, [], [], cancellationToken);

    public Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(null);

    public Task SetLastRefreshedAsync(DateTimeOffset refreshedAt, CancellationToken cancellationToken) => Task.CompletedTask;

    // IOutboxStore

    Task<IReadOnlyList<OutboxEntry>> IOutboxStore.GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutboxEntry>>([.. OutboxPlanner.InSendOrder(_outbox.Values)]);

    public Task<IReadOnlyList<OutboxEntry>> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutboxEntry>>([.. OutboxPlanner.InSendOrder(_outbox.Values.Where(entry => entry.EntityId == entityId))]);

    public Task CommitAsync(
        IReadOnlyCollection<Product> products,
        IReadOnlyCollection<OutboxEntry> entriesToSave,
        IReadOnlyCollection<Guid> entryIdsToDelete,
        CancellationToken cancellationToken)
    {
        OnCommit?.Invoke();
        CommitCount++;

        foreach (Product product in products)
        {
            _products[product.Id] = Copy(product);
        }

        foreach (OutboxEntry entry in entriesToSave)
        {
            _outbox[entry.Id] = entry;
        }

        foreach (Guid id in entryIdsToDelete)
        {
            _outbox.Remove(id);
        }

        return Task.CompletedTask;
    }

    // ILocalDatabase

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _products.Clear();
        _outbox.Clear();
        return Task.CompletedTask;
    }

    private IEnumerable<Product> Active() =>
        _products.Values.Where(product => !product.IsDeleted).OrderBy(product => product.Details.Title, StringComparer.OrdinalIgnoreCase).Select(Copy);

    private static Product Copy(Product product) => global::OfflineCrud.Domain.Products.Product.Restore(
        product.Id, product.Details, product.Rating, product.ThumbnailUrl, product.ServerId, product.ServerVersion, product.SyncState, product.IsDeleted);
}
