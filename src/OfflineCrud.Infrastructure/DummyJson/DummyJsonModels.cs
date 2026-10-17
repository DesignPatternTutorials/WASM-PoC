using System.Text.Json.Serialization;

namespace OfflineCrud.Infrastructure.DummyJson;

// Wire formats for DummyJSON. They stay internal to Infrastructure so the rest of the app never depends on
// the API's JSON shape. Property names bind case-insensitively using the System.Text.Json web defaults.

internal sealed record DummyJsonProduct(
    int Id,
    string? Title,
    string? Description,
    string? Category,
    string? Brand,
    decimal? Price,
    decimal? DiscountPercentage,
    double? Rating,
    int? Stock,
    string? Thumbnail,
    DummyJsonMeta? Meta);

// Only UpdatedAt is used: it's the closest thing DummyJSON has to a version. It's absent from write responses.
internal sealed record DummyJsonMeta(string? UpdatedAt);

internal sealed record DummyJsonProductPage(IReadOnlyList<DummyJsonProduct> Products, int Total, int Skip, int Limit);

// No Id property: DummyJSON echoes an id from the body over the one in the URL (see docs/api-notes.md).
internal sealed record DummyJsonProductWrite(
    string Title,
    string Description,
    string Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Brand,
    decimal Price,
    decimal DiscountPercentage,
    int Stock);

internal sealed record DummyJsonError(string? Message);
