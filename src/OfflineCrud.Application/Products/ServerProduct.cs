using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>A product as the server returned it, before it is merged into local storage.</summary>
/// <param name="ServerId">The server's id.</param>
/// <param name="Details">The editable fields.</param>
/// <param name="Rating">Average review rating, if provided.</param>
/// <param name="ThumbnailUrl">Thumbnail URL, if provided.</param>
/// <param name="Version">
/// An opaque token that changes whenever the server's copy changes, or <see langword="null"/> if the response
/// didn't include one. For DummyJSON this is <c>meta.updatedAt</c>; a production API would typically use an ETag
/// or row version.
/// </param>
public sealed record ServerProduct(int ServerId, ProductDetails Details, double? Rating, Uri? ThumbnailUrl, string? Version);
