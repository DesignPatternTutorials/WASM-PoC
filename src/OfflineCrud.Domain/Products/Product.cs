namespace OfflineCrud.Domain.Products;

/// <summary>
/// A product in the catalogue: an identity plus its editable <see cref="ProductDetails"/> and the
/// read-only values the server maintains.
/// </summary>
/// <remarks>
/// In Phase 1 the identity is the server's integer id, because the app talks only to the API.
/// Phase 2 adds a locally generated identity: DummyJSON gives every created product the same id,
/// so the server id can't be the key of a local store (see <c>docs/api-notes.md</c>).
/// </remarks>
public sealed class Product
{
    /// <summary>Creates a product.</summary>
    /// <param name="id">The server's id. Must be positive.</param>
    /// <param name="details">The editable fields.</param>
    /// <param name="rating">Average review rating, if the server provided one.</param>
    /// <param name="thumbnailUrl">Absolute URL of a thumbnail image, if any.</param>
    public Product(int id, ProductDetails details, double? rating = null, Uri? thumbnailUrl = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentNullException.ThrowIfNull(details);

        Id = id;
        Details = details;
        Rating = rating;
        ThumbnailUrl = thumbnailUrl;
    }

    /// <summary>The server's id.</summary>
    public int Id { get; }

    /// <summary>The editable fields.</summary>
    public ProductDetails Details { get; }

    /// <summary>Average review rating from 0 to 5, or <see langword="null"/> if unknown.</summary>
    public double? Rating { get; }

    /// <summary>Thumbnail image URL, or <see langword="null"/> if none.</summary>
    public Uri? ThumbnailUrl { get; }

    /// <summary>The price after applying <see cref="ProductDetails.DiscountPercentage"/>, rounded to two decimal places.</summary>
    public decimal DiscountedPrice =>
        Math.Round(Details.Price * (1m - (Details.DiscountPercentage / 100m)), 2, MidpointRounding.AwayFromZero);

    /// <summary>Whether at least one unit is in stock.</summary>
    public bool IsInStock => Details.Stock > 0;
}
