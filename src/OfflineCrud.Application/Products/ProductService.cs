using OfflineCrud.Application.Common;
using OfflineCrud.Application.Sync;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// The product use cases. Reads come from local storage and are revalidated from the server. Writes are local-first:
/// the change and an outbox entry are saved together, and the sync engine sends the entry later.
/// </summary>
public sealed class ProductService : IProductService
{
    /// <summary>How long a catalogue refresh counts as fresh, so revisiting the list doesn't refetch every time.</summary>
    public static readonly TimeSpan CatalogueMaxAge = TimeSpan.FromMinutes(1);

    private readonly IProductApiClient _apiClient;
    private readonly IProductRepository _repository;
    private readonly IOutboxStore _outbox;
    private readonly LocalStoreLock _storeLock;
    private readonly ISyncTrigger _syncTrigger;
    private readonly TimeProvider _timeProvider;

    private IReadOnlyList<string>? _serverCategories;

    /// <summary>Creates the service.</summary>
    public ProductService(
        IProductApiClient apiClient,
        IProductRepository repository,
        IOutboxStore outbox,
        LocalStoreLock storeLock,
        ISyncTrigger syncTrigger,
        TimeProvider timeProvider)
    {
        _apiClient = apiClient;
        _repository = repository;
        _outbox = outbox;
        _storeLock = storeLock;
        _syncTrigger = syncTrigger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        ValidatePaging(skip, take);
        return _repository.GetPageAsync(skip, take, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RefreshResult> RefreshCatalogueAsync(bool force, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!force)
        {
            DateTimeOffset? lastRefreshed = await _repository.GetLastRefreshedAsync(cancellationToken);
            if (lastRefreshed is { } last && now - last < CatalogueMaxAge)
            {
                return RefreshResult.AlreadyFresh;
            }
        }

        IReadOnlyList<ServerProduct> serverProducts;
        try
        {
            serverProducts = await _apiClient.GetAllAsync(cancellationToken);
        }
        catch (ApiException ex)
        {
            return RefreshResult.Failed(ex);
        }

        ServerCopyMergeResult merge = await MergeAsync(serverProducts, cancellationToken);
        await _repository.SetLastRefreshedAsync(now, cancellationToken);
        return RefreshResult.Refreshed(merge.Changed.Count);
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken)
    {
        return _repository.GetLastRefreshedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProductSearchResult> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ValidatePaging(skip, take);
        string trimmed = query.Trim();

        PagedResult<ServerProduct> serverPage;
        try
        {
            serverPage = await _apiClient.SearchAsync(trimmed, skip, take, cancellationToken);
        }
        catch (ApiException ex)
        {
            return new ProductSearchResult(await SearchDeviceAsync(trimmed, skip, take, cancellationToken), ProductDataSource.Device, ex);
        }

        // Merge the results in, then show the local copies: they carry local ids and any changes made on this device.
        ServerCopyMergeResult merge = await MergeAsync(serverPage.Items, cancellationToken);
        Product[] items =
        [
            .. serverPage.Items
                .Select(server => merge.ByServerId[server.ServerId])
                .Where(product => !product.IsDeleted),
        ];

        return new ProductSearchResult(new PagedResult<Product>(items, serverPage.TotalCount, skip, take), ProductDataSource.Server);
    }

    /// <inheritdoc />
    public async Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetAsync(id, cancellationToken);
        return product is { IsDeleted: false } ? product : null;
    }

    /// <inheritdoc />
    public async Task<RefreshResult> RefreshAsync(Guid id, CancellationToken cancellationToken)
    {
        Product? product = await GetAsync(id, cancellationToken);
        if (product?.ServerId is not int serverId)
        {
            return RefreshResult.NotOnServer;
        }

        ServerProduct? serverProduct;
        try
        {
            serverProduct = await _apiClient.GetByIdAsync(serverId, cancellationToken);
        }
        catch (ApiException ex)
        {
            return RefreshResult.Failed(ex);
        }

        if (serverProduct is null)
        {
            return RefreshResult.NotOnServer;
        }

        ServerCopyMergeResult merge = await MergeAsync([serverProduct], cancellationToken);
        return RefreshResult.Refreshed(merge.Changed.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        if (_serverCategories is not null)
        {
            return _serverCategories;
        }

        try
        {
            IReadOnlyList<string> categories = await _apiClient.GetCategoriesAsync(cancellationToken);
            _serverCategories = [.. categories.Order(StringComparer.OrdinalIgnoreCase)];
            return _serverCategories;
        }
        catch (ApiException ex) when (ex.Kind == ApiFailureKind.Network)
        {
            // Offline: offer the categories of the products already on this device. Not cached, so the full list is
            // fetched once the connection is back.
            IReadOnlyList<Product> products = await _repository.GetAllAsync(cancellationToken);
            return [.. products.Select(product => product.Details.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <inheritdoc />
    public async Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        // LEARN: Local-first write. No network call here: the product exists on this device as soon as the transaction
        // commits, and works the same online or offline. The server id is filled in later, when the outbox entry is sent.
        Product product = Product.CreateLocal(Guid.NewGuid(), details);
        using (await _storeLock.AcquireAsync(cancellationToken))
        {
            OutboxPlan plan = OutboxPlanner.Plan([], OutboxOperation.Create, product.Id, details, _timeProvider.GetUtcNow(), Guid.NewGuid);
            await CommitAsync(product, plan, cancellationToken);
        }

        _syncTrigger.RequestSync();
        return product;
    }

    /// <inheritdoc />
    public async Task<Product> UpdateAsync(Guid id, ProductDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        Product product;
        using (await _storeLock.AcquireAsync(cancellationToken))
        {
            product = await GetRequiredAsync(id, cancellationToken);
            product.ApplyLocalEdit(details);

            IReadOnlyList<OutboxEntry> queued = await _outbox.GetForEntityAsync(id, cancellationToken);
            OutboxPlan plan = OutboxPlanner.Plan(queued, OutboxOperation.Update, id, details, _timeProvider.GetUtcNow(), Guid.NewGuid);
            await CommitAsync(product, plan, cancellationToken);
        }

        _syncTrigger.RequestSync();
        return product;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using (await _storeLock.AcquireAsync(cancellationToken))
        {
            Product product = await GetRequiredAsync(id, cancellationToken);
            product.ApplyLocalDelete();

            IReadOnlyList<OutboxEntry> queued = await _outbox.GetForEntityAsync(id, cancellationToken);
            OutboxPlan plan = OutboxPlanner.Plan(queued, OutboxOperation.Delete, id, payload: null, _timeProvider.GetUtcNow(), Guid.NewGuid);
            await CommitAsync(product, plan, cancellationToken);
        }

        _syncTrigger.RequestSync();
    }

    private static void ValidatePaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, IProductService.MinPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, IProductService.MaxPageSize);
    }

    /// <summary>Saves the product and its outbox changes in one transaction. The caller holds the store lock.</summary>
    private Task CommitAsync(Product product, OutboxPlan plan, CancellationToken cancellationToken)
    {
        product.SetSyncState(plan.SyncState);
        return _outbox.CommitAsync([product], plan.ToSave, plan.ToDelete, cancellationToken);
    }

    private async Task<PagedResult<Product>> SearchDeviceAsync(string query, int skip, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> all = await _repository.GetAllAsync(cancellationToken);
        Product[] matches = [.. all.Where(product => ProductSearch.Matches(product.Details, query))];
        return new PagedResult<Product>([.. matches.Skip(skip).Take(take)], matches.Length, skip, take);
    }

    private async Task<ServerCopyMergeResult> MergeAsync(IReadOnlyList<ServerProduct> serverProducts, CancellationToken cancellationToken)
    {
        using IDisposable _ = await _storeLock.AcquireAsync(cancellationToken);

        int[] serverIds = [.. serverProducts.Select(product => product.ServerId).Distinct()];
        IReadOnlyList<Product> existing = await _repository.GetByServerIdsAsync(serverIds, cancellationToken);

        // Products with queued changes aren't Synced, so ShouldAcceptServerCopy keeps the server from overwriting them.
        ServerCopyMergeResult merge = ServerCopyMerger.Merge(existing, serverProducts, Guid.NewGuid);
        if (merge.Changed.Count > 0)
        {
            await _repository.SaveAsync([.. merge.Changed], cancellationToken);
        }

        return merge;
    }

    private async Task<Product> GetRequiredAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Product {id} isn't stored on this device.");
    }
}
