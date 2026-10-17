using OfflineCrud.Application.Common;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>Where a set of results came from.</summary>
public enum ProductDataSource
{
    /// <summary>The server answered.</summary>
    Server,

    /// <summary>The server couldn't be reached, so the products stored on this device were searched instead.</summary>
    Device,
}

/// <summary>A page of search results, and which source answered.</summary>
/// <param name="Page">The matching products, as stored on this device.</param>
/// <param name="Source">Which source answered.</param>
/// <param name="ServerError">Why the server wasn't used, when <paramref name="Source"/> is <see cref="ProductDataSource.Device"/>.</param>
public sealed record ProductSearchResult(PagedResult<Product> Page, ProductDataSource Source, ApiException? ServerError = null);
