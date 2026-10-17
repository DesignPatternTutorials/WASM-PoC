using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// The product use cases the UI calls. Pages depend on this interface only.
/// </summary>
/// <remarks>
/// In Phase 1 the implementation goes straight to the API. Later phases change the implementation to
/// read from and write to IndexedDB first, without the pages having to change.
/// </remarks>
public interface IProductService
{
    /// <summary>Smallest allowed page size.</summary>
    public const int MinPageSize = 1;

    /// <summary>Largest allowed page size.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Gets one page of products, filtered by <paramref name="search"/> when it isn't blank.</summary>
    Task<PagedResult<Product>> GetPageAsync(string? search, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets a single product, or <see langword="null"/> if it doesn't exist.</summary>
    Task<Product?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets the available category slugs, sorted.</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a product.</summary>
    Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Updates a product's details.</summary>
    Task<Product> UpdateAsync(int id, ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Deletes a product.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
