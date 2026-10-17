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
    /// <summary>Gets every product in the catalogue.</summary>
    Task<IReadOnlyList<ServerProduct>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Gets one page of products matching a free-text query.</summary>
    Task<PagedResult<ServerProduct>> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets a single product, or <see langword="null"/> if the server has no product with that id.</summary>
    Task<ServerProduct?> GetByIdAsync(int serverId, CancellationToken cancellationToken);

    /// <summary>Gets the category slugs the server knows about.</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a product and returns it as the server stored it, including its new id.</summary>
    Task<ServerProduct> CreateAsync(ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Replaces a product's details and returns it as the server stored it.</summary>
    Task<ServerProduct> UpdateAsync(int serverId, ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Deletes a product.</summary>
    Task DeleteAsync(int serverId, CancellationToken cancellationToken);
}
