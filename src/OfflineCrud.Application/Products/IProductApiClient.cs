using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Port for the remote products API. Implemented in Infrastructure over <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Every member throws <see cref="ApiException"/> on failure, never a raw HTTP exception.
/// </remarks>
public interface IProductApiClient
{
    /// <summary>Gets one page of all products.</summary>
    Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets one page of products matching a free-text query.</summary>
    Task<PagedResult<Product>> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets a single product, or <see langword="null"/> if the server has no product with that id.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets the category slugs the server knows about.</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a product and returns it as the server stored it, including its new id.</summary>
    Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Replaces a product's details and returns it as the server stored it.</summary>
    Task<Product> UpdateAsync(int id, ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Deletes a product.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
