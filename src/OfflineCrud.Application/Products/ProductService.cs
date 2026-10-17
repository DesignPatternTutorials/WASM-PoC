using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Phase 1 implementation of <see cref="IProductService"/>: every call goes straight to the API.
/// </summary>
/// <remarks>
/// This is the "before" picture. With no local copy of anything, the app can only show what the
/// network returns. Go offline and every page fails.
/// </remarks>
public sealed class ProductService : IProductService
{
    private readonly IProductApiClient _apiClient;
    private IReadOnlyList<string>? _categories;

    /// <summary>Creates the service.</summary>
    public ProductService(IProductApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    /// <inheritdoc />
    public Task<PagedResult<Product>> GetPageAsync(string? search, int skip, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, IProductService.MinPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, IProductService.MaxPageSize);

        return string.IsNullOrWhiteSpace(search)
            ? _apiClient.GetPageAsync(skip, take, cancellationToken)
            : _apiClient.SearchAsync(search.Trim(), skip, take, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Product?> GetAsync(int id, CancellationToken cancellationToken)
    {
        return _apiClient.GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        // LEARN: In Blazor WebAssembly there is one DI scope for the whole app, so a scoped service lives
        // until the tab closes. This field therefore caches the category list for the whole session.
        // The same code in Blazor Server would cache per circuit (per user connection) instead.
        if (_categories is null)
        {
            IReadOnlyList<string> categories = await _apiClient.GetCategoriesAsync(cancellationToken);
            _categories = [.. categories.Order(StringComparer.OrdinalIgnoreCase)];
        }

        return _categories;
    }

    /// <inheritdoc />
    public Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        return _apiClient.CreateAsync(details, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Product> UpdateAsync(int id, ProductDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        return _apiClient.UpdateAsync(id, details, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        return _apiClient.DeleteAsync(id, cancellationToken);
    }
}
