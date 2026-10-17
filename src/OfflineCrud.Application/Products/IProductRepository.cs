using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Port for the products stored on this device. Implemented in Infrastructure over IndexedDB.
/// </summary>
/// <remarks>
/// This store is the source of truth for what the UI shows. The server is only where changes are synced to.
/// </remarks>
public interface IProductRepository
{
    /// <summary>Gets one page of products that aren't deleted, ordered by title.</summary>
    Task<PagedResult<Product>> GetPageAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>Gets every product that isn't deleted, ordered by title.</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Gets a product by local id, including tombstones, or <see langword="null"/> if there is none.</summary>
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Gets every stored product, including tombstones, whose server id is in <paramref name="serverIds"/>.</summary>
    Task<IReadOnlyList<Product>> GetByServerIdsAsync(IReadOnlyCollection<int> serverIds, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces products. All of them are written in one transaction, or none are.</summary>
    Task SaveAsync(IReadOnlyCollection<Product> products, CancellationToken cancellationToken);

    /// <summary>Gets when the catalogue was last refreshed from the server, or <see langword="null"/> if never.</summary>
    Task<DateTimeOffset?> GetLastRefreshedAsync(CancellationToken cancellationToken);

    /// <summary>Records when the catalogue was last refreshed from the server.</summary>
    Task SetLastRefreshedAsync(DateTimeOffset refreshedAt, CancellationToken cancellationToken);
}
