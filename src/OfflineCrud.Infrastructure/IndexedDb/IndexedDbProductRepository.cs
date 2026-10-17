using System.Globalization;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.IndexedDb;

/// <summary>
/// <see cref="IProductRepository"/> over IndexedDB, through the JS module in <c>wwwroot/js/indexedDb.js</c>.
/// </summary>
internal sealed class IndexedDbProductRepository : IProductRepository
{
    private const string LastRefreshedKey = "catalogueLastRefreshed";

    private readonly IndexedDbModule _module;

    public IndexedDbProductRepository(IndexedDbModule module)
    {
        _module = module;
    }

    public async Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        ProductPageRecord page = await _module.InvokeAsync<ProductPageRecord>("getProductPage", cancellationToken, skip, take);
        return new PagedResult<Product>([.. page.Items.Select(ProductRecordMapper.ToProduct)], page.Total, skip, take);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        ProductRecord[] records = await _module.InvokeAsync<ProductRecord[]>("getAllProducts", cancellationToken);
        return [.. records.Select(ProductRecordMapper.ToProduct)];
    }

    public async Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ProductRecord? record = await _module.InvokeAsync<ProductRecord?>("getProduct", cancellationToken, id);
        return record is null ? null : ProductRecordMapper.ToProduct(record);
    }

    public async Task<IReadOnlyList<Product>> GetByServerIdsAsync(IReadOnlyCollection<int> serverIds, CancellationToken cancellationToken)
    {
        if (serverIds.Count == 0)
        {
            return [];
        }

        ProductRecord[] records = await _module.InvokeAsync<ProductRecord[]>("getProductsByServerIds", cancellationToken, serverIds);
        return [.. records.Select(ProductRecordMapper.ToProduct)];
    }

    public async Task SaveAsync(IReadOnlyCollection<Product> products, CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return;
        }

        ProductRecord[] records = [.. products.Select(ProductRecordMapper.ToRecord)];

        // Wrapped in [ ] on purpose: an array passed straight to a params object?[] parameter would be spread into
        // one JS argument per record, instead of arriving as a single array argument.
        await _module.InvokeVoidAsync("putProducts", cancellationToken, [records]);
    }

    public async Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken)
    {
        string? value = await _module.InvokeAsync<string?>("getMeta", cancellationToken, LastRefreshedKey);
        return value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public Task SetLastRefreshedAsync(DateTimeOffset refreshedAt, CancellationToken cancellationToken)
    {
        // Stored as an ISO 8601 string: JS has no DateTimeOffset, and a string round-trips exactly.
        return _module.InvokeVoidAsync("setMeta", cancellationToken, LastRefreshedKey, refreshedAt.ToString("O", CultureInfo.InvariantCulture));
    }
}
