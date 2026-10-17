using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// The product use cases the UI calls. Pages depend on this interface only.
/// </summary>
/// <remarks>
/// Reads come from the products stored on this device, and the <c>Refresh…</c> members bring them up to date from
/// the server (stale-while-revalidate). Writes are local-first: they never touch the network, work offline, and are
/// sent to the server later by the sync engine.
/// </remarks>
public interface IProductService
{
    /// <summary>Smallest allowed page size.</summary>
    public const int MinPageSize = 1;

    /// <summary>Largest allowed page size.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Gets one page of the products stored on this device, ordered by title. Never touches the network.</summary>
    Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the whole catalogue from the server and merges it into local storage, unless that was done recently
    /// and <paramref name="force"/> is <see langword="false"/>.
    /// </summary>
    Task<RefreshResult> RefreshCatalogueAsync(bool force, CancellationToken cancellationToken);

    /// <summary>Gets when the catalogue was last refreshed from the server, or <see langword="null"/> if never.</summary>
    Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken);

    /// <summary>Searches the server, or the products stored on this device if the server can't be reached.</summary>
    Task<ProductSearchResult> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets a product stored on this device, or <see langword="null"/> if there is none or it was deleted.</summary>
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Fetches one product from the server and merges it into local storage.</summary>
    Task<RefreshResult> RefreshAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Gets the category slugs, sorted. Falls back to the categories of stored products when offline.</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a product on this device and queues it to be sent to the server.</summary>
    Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Updates a product on this device and queues the change to be sent to the server.</summary>
    Task<Product> UpdateAsync(Guid id, ProductDetails details, CancellationToken cancellationToken);

    /// <summary>Marks a product deleted on this device and queues the delete to be sent to the server.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
