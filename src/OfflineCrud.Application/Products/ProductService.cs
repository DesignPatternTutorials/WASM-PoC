using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Phase 2 implementation of <see cref="IProductService"/>: reads come from local storage and are revalidated from
/// the server; writes go to the server, then to local storage.
/// </summary>
/// <remarks>
/// What still breaks offline: creating, editing and deleting, because each write needs the server to answer first.
/// Phase 3 replaces that with a local write plus an outbox entry.
/// </remarks>
public sealed class ProductService : IProductService, IDisposable
{
    /// <summary>How long a catalogue refresh counts as fresh, so revisiting the list doesn't refetch every time.</summary>
    public static readonly TimeSpan CatalogueMaxAge = TimeSpan.FromMinutes(1);

    private readonly IProductApiClient _apiClient;
    private readonly IProductRepository _repository;
    private readonly TimeProvider _timeProvider;

    // LEARN: WebAssembly runs .NET on a single thread, but that doesn't make read-modify-write safe. Every `await`
    // lets other work run, so a catalogue refresh could read a product, then a save could change it, then the refresh
    // writes its stale copy back. This lock makes each read-merge-write sequence finish before the next one starts.
    // (It only covers this tab. Two tabs share one IndexedDB database but not this lock.)
    private readonly SemaphoreSlim _storeLock = new(1, 1);

    private IReadOnlyList<string>? _serverCategories;

    /// <summary>Creates the service.</summary>
    public ProductService(IProductApiClient apiClient, IProductRepository repository, TimeProvider timeProvider)
    {
        _apiClient = apiClient;
        _repository = repository;
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

        ServerProduct created = await _apiClient.CreateAsync(details, cancellationToken);

        // A fresh local id, never the server's: DummyJSON returns the same id for every create.
        Product product = Product.FromServer(Guid.NewGuid(), created.ServerId, created.Details, created.Rating, created.ThumbnailUrl, created.Version);
        await SaveLockedAsync(product, cancellationToken);
        return product;
    }

    /// <inheritdoc />
    public async Task<Product> UpdateAsync(Guid id, ProductDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        int serverId = await GetServerIdAsync(id, cancellationToken);

        ServerProduct updated = await _apiClient.UpdateAsync(serverId, details, cancellationToken);

        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            // Re-read inside the lock, in case a refresh changed the record while the request was in flight.
            Product product = await GetRequiredAsync(id, cancellationToken);

            // DummyJSON's PUT response omits meta, so keep the version this copy was based on rather than forgetting it.
            // Forgetting it would let the next refresh overwrite the edit with the server's unchanged copy.
            product.ApplyServerCopy(
                serverId,
                updated.Details,
                updated.Rating ?? product.Rating,
                updated.ThumbnailUrl ?? product.ThumbnailUrl,
                updated.Version ?? product.ServerVersion);
            await _repository.SaveAsync([product], cancellationToken);
            return product;
        }
        finally
        {
            _storeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        int serverId = await GetServerIdAsync(id, cancellationToken);

        await _apiClient.DeleteAsync(serverId, cancellationToken);

        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            Product product = await GetRequiredAsync(id, cancellationToken);
            product.MarkDeleted();
            await _repository.SaveAsync([product], cancellationToken);
        }
        finally
        {
            _storeLock.Release();
        }
    }

    /// <summary>Releases the store lock.</summary>
    public void Dispose()
    {
        _storeLock.Dispose();
    }

    private static void ValidatePaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, IProductService.MinPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, IProductService.MaxPageSize);
    }

    private async Task<PagedResult<Product>> SearchDeviceAsync(string query, int skip, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> all = await _repository.GetAllAsync(cancellationToken);
        Product[] matches = [.. all.Where(product => ProductSearch.Matches(product.Details, query))];
        return new PagedResult<Product>([.. matches.Skip(skip).Take(take)], matches.Length, skip, take);
    }

    private async Task<ServerCopyMergeResult> MergeAsync(IReadOnlyList<ServerProduct> serverProducts, CancellationToken cancellationToken)
    {
        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            int[] serverIds = [.. serverProducts.Select(product => product.ServerId).Distinct()];
            IReadOnlyList<Product> existing = await _repository.GetByServerIdsAsync(serverIds, cancellationToken);

            ServerCopyMergeResult merge = ServerCopyMerger.Merge(existing, serverProducts, Guid.NewGuid);
            if (merge.Changed.Count > 0)
            {
                await _repository.SaveAsync([.. merge.Changed], cancellationToken);
            }

            return merge;
        }
        finally
        {
            _storeLock.Release();
        }
    }

    private async Task SaveLockedAsync(Product product, CancellationToken cancellationToken)
    {
        await _storeLock.WaitAsync(cancellationToken);
        try
        {
            await _repository.SaveAsync([product], cancellationToken);
        }
        finally
        {
            _storeLock.Release();
        }
    }

    private async Task<Product> GetRequiredAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Product {id} isn't stored on this device.");
    }

    private async Task<int> GetServerIdAsync(Guid id, CancellationToken cancellationToken)
    {
        Product product = await GetRequiredAsync(id, cancellationToken);
        return product.ServerId
            ?? throw new InvalidOperationException($"Product {id} has no server id, so the server can't be asked to change it.");
    }
}
