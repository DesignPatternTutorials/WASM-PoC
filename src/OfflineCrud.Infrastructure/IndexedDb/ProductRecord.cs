using System.Globalization;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.IndexedDb;

// LEARN: These records are what crosses the C# ↔ JS boundary. Blazor serialises interop arguments and results with
// System.Text.Json (camelCase), so the JS module sees plain objects like { id, serverId, titleKey, ... }.
// The shape is chosen for JavaScript:
//   - Guid becomes a string, which works as an IndexedDB key.
//   - decimal becomes a JS number (a double). That's exact enough for prices with two decimal places, but it is a
//     lossy conversion, which is why the domain never relies on the stored value having more precision.
//   - The enum is stored by name, so the data stays readable in DevTools → Application → IndexedDB.
//   - `Deleted` is 0/1 and `TitleKey` is lower-case because they feed the byListOrder index.

internal sealed record ProductRecord(
    Guid Id,
    int? ServerId,
    string? ServerVersion,
    string SyncState,
    int Deleted,
    string TitleKey,
    string Title,
    string Description,
    string Category,
    string? Brand,
    decimal Price,
    decimal DiscountPercentage,
    int Stock,
    double? Rating,
    string? ThumbnailUrl);

internal sealed record ProductPageRecord(IReadOnlyList<ProductRecord> Items, int Total);

/// <summary>Converts between domain products and the plain records stored in IndexedDB.</summary>
internal static class ProductRecordMapper
{
    public static ProductRecord ToRecord(Product product)
    {
        ProductDetails details = product.Details;
        return new ProductRecord(
            product.Id,
            product.ServerId,
            product.ServerVersion,
            product.SyncState.ToString(),
            product.IsDeleted ? 1 : 0,
            details.Title.ToLower(CultureInfo.InvariantCulture),
            details.Title,
            details.Description,
            details.Category,
            details.Brand,
            details.Price,
            details.DiscountPercentage,
            details.Stock,
            product.Rating,
            product.ThumbnailUrl?.AbsoluteUri);
    }

    public static Product ToProduct(ProductRecord record)
    {
        ProductDetails details = new(
            record.Title,
            record.Description,
            record.Category,
            record.Brand,
            record.Price,
            record.DiscountPercentage,
            record.Stock);

        Uri? thumbnail = Uri.TryCreate(record.ThumbnailUrl, UriKind.Absolute, out Uri? uri) ? uri : null;

        return Product.Restore(
            record.Id,
            details,
            record.Rating,
            thumbnail,
            record.ServerId,
            record.ServerVersion,
            Enum.Parse<SyncState>(record.SyncState),
            isDeleted: record.Deleted != 0);
    }
}
