using System.Text.Json;
using OfflineCrud.Domain.Products;
using OfflineCrud.Infrastructure.IndexedDb;

namespace OfflineCrud.Infrastructure.Tests.IndexedDb;

[TestFixture]
public class ProductRecordMapperTests
{
    // Blazor's JS interop serialises with the web defaults (camelCase), so the tests do the same.
    private static readonly JsonSerializerOptions InteropOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void Product_RoundTripsThroughTheJsShape()
    {
        Product original = Product.Restore(
            Guid.NewGuid(),
            new ProductDetails("iPhone 13", "A phone", "smartphones", "Apple", 999.99m, 12.5m, 4),
            4.6,
            new Uri("https://cdn.example.test/1.webp"),
            serverId: 123,
            serverVersion: "2026-05-23T11:27:41.868Z",
            SyncState.PendingUpdate,
            isDeleted: true);

        string json = JsonSerializer.Serialize(ProductRecordMapper.ToRecord(original), InteropOptions);
        Product restored = ProductRecordMapper.ToProduct(JsonSerializer.Deserialize<ProductRecord>(json, InteropOptions)!);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(original.Id));
            Assert.That(restored.Details, Is.EqualTo(original.Details));
            Assert.That(restored.Rating, Is.EqualTo(original.Rating));
            Assert.That(restored.ThumbnailUrl, Is.EqualTo(original.ThumbnailUrl));
            Assert.That(restored.ServerId, Is.EqualTo(original.ServerId));
            Assert.That(restored.ServerVersion, Is.EqualTo(original.ServerVersion));
            Assert.That(restored.SyncState, Is.EqualTo(original.SyncState));
            Assert.That(restored.IsDeleted, Is.True);
        });
    }

    [Test]
    public void ToRecord_ProducesTheFieldsTheIndexesUse()
    {
        Product product = Product.FromServer(Guid.NewGuid(), 1, new ProductDetails("Zebra MUG", "d", "c", null, 1m, 0m, 1), null, null, null);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(ProductRecordMapper.ToRecord(product), InteropOptions));
        JsonElement root = json.RootElement;

        Assert.Multiple(() =>
        {
            // These names and values must match the key paths in indexedDb.js.
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo(product.Id.ToString()));
            Assert.That(root.GetProperty("serverId").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("deleted").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("titleKey").GetString(), Is.EqualTo("zebra mug"));
            Assert.That(root.GetProperty("syncState").GetString(), Is.EqualTo("Synced"));
        });
    }

    [Test]
    public void ToRecord_NoServerId_SerialisesAsNull()
    {
        Product product = Product.Restore(
            Guid.NewGuid(), new ProductDetails("T", "D", "c", null, 1m, 0m, 1), null, null, null, null, SyncState.PendingCreate, false);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(ProductRecordMapper.ToRecord(product), InteropOptions));

        // IndexedDB leaves records whose index key is null out of that index instead of failing.
        Assert.That(json.RootElement.GetProperty("serverId").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
}
