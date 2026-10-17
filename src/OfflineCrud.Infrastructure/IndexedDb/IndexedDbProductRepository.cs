using System.Globalization;
using Microsoft.JSInterop;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.IndexedDb;

/// <summary>
/// <see cref="IProductRepository"/> over IndexedDB, through the JS module in <c>wwwroot/js/indexedDb.js</c>.
/// </summary>
internal sealed class IndexedDbProductRepository : IProductRepository, IAsyncDisposable
{
    // Files in a Razor class library's wwwroot are served under _content/{assembly name}/.
    private const string ModulePath = "./_content/OfflineCrud.Infrastructure/js/indexedDb.js";
    private const string LastRefreshedKey = "catalogueLastRefreshed";

    // LEARN: The module is imported on first use, not at start-up, and only once. `import()` returns a reference to the
    // module object living in JS. .NET holds it as an IJSObjectReference and calls its exported functions by name.
    private readonly Lazy<Task<IJSObjectReference>> _module;

    public IndexedDbProductRepository(IJSRuntime jsRuntime)
    {
        _module = new Lazy<Task<IJSObjectReference>>(
            () => jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());
    }

    public async Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        ProductPageRecord page = await InvokeAsync<ProductPageRecord>("getProductPage", cancellationToken, skip, take);
        return new PagedResult<Product>([.. page.Items.Select(ProductRecordMapper.ToProduct)], page.Total, skip, take);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        ProductRecord[] records = await InvokeAsync<ProductRecord[]>("getAllProducts", cancellationToken);
        return [.. records.Select(ProductRecordMapper.ToProduct)];
    }

    public async Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ProductRecord? record = await InvokeAsync<ProductRecord?>("getProduct", cancellationToken, id);
        return record is null ? null : ProductRecordMapper.ToProduct(record);
    }

    public async Task<IReadOnlyList<Product>> GetByServerIdsAsync(IReadOnlyCollection<int> serverIds, CancellationToken cancellationToken)
    {
        if (serverIds.Count == 0)
        {
            return [];
        }

        ProductRecord[] records = await InvokeAsync<ProductRecord[]>("getProductsByServerIds", cancellationToken, serverIds);
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
        await InvokeVoidAsync("putProducts", cancellationToken, [records]);
    }

    public async Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken)
    {
        string? value = await InvokeAsync<string?>("getMeta", cancellationToken, LastRefreshedKey);
        return value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public Task SetLastRefreshedAsync(DateTimeOffset refreshedAt, CancellationToken cancellationToken)
    {
        // Stored as an ISO 8601 string: JS has no DateTimeOffset, and a string round-trips exactly.
        return InvokeVoidAsync("setMeta", cancellationToken, LastRefreshedKey, refreshedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public async ValueTask DisposeAsync()
    {
        // LEARN: An IJSObjectReference keeps the JS object alive until it's disposed. Disposing tells the JS side it
        // can release its handle. (In Blazor Server this can throw JSDisconnectedException if the circuit has gone;
        // in WebAssembly the JS runtime is always in the same tab.)
        if (_module.IsValueCreated)
        {
            IJSObjectReference module = await _module.Value;
            await module.DisposeAsync();
        }
    }

    // LEARN: The CancellationToken only stops .NET waiting. The JS function keeps running, and an IndexedDB
    // transaction that has started will still commit. Cancellation across the interop boundary is cooperative at best.
    private async Task<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        IJSObjectReference module = await _module.Value;
        return await module.InvokeAsync<T>(identifier, cancellationToken, args);
    }

    private async Task InvokeVoidAsync(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        IJSObjectReference module = await _module.Value;
        await module.InvokeVoidAsync(identifier, cancellationToken, args);
    }
}
