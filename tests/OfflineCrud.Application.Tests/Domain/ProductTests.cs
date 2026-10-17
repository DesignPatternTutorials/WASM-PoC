using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Domain;

[TestFixture]
public class ProductTests
{
    [TestCase(100, 10, 90)]
    [TestCase(9.99, 10.48, 8.94)]
    [TestCase(10, 0, 10)]
    [TestCase(10, 100, 0)]
    [TestCase(0.05, 50, 0.03)]
    public void DiscountedPrice_AppliesDiscountAndRoundsToCents(decimal price, decimal discount, decimal expected)
    {
        Product product = FromServer(details: ProductDetailsTests.Create(price: price, discountPercentage: discount));

        Assert.That(product.DiscountedPrice, Is.EqualTo(expected));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void IsInStock_ReflectsStock(int stock, bool expected)
    {
        Product product = FromServer(details: ProductDetailsTests.Create(stock: stock));

        Assert.That(product.IsInStock, Is.EqualTo(expected));
    }

    [Test]
    public void FromServer_IsSyncedAndNotDeleted()
    {
        Product product = FromServer(serverId: 7, version: "v1");

        Assert.Multiple(() =>
        {
            Assert.That(product.ServerId, Is.EqualTo(7));
            Assert.That(product.ServerVersion, Is.EqualTo("v1"));
            Assert.That(product.SyncState, Is.EqualTo(SyncState.Synced));
            Assert.That(product.IsDeleted, Is.False);
        });
    }

    [Test]
    public void FromServer_EmptyId_Throws()
    {
        Assert.That(
            () => Product.FromServer(Guid.Empty, 1, ProductDetailsTests.Create(), null, null, null),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("id"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void FromServer_NonPositiveServerId_Throws(int serverId)
    {
        Assert.That(() => FromServer(serverId: serverId), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Restore_KeepsEveryValue()
    {
        Guid id = Guid.NewGuid();
        ProductDetails details = ProductDetailsTests.Create();
        Uri thumbnail = new("https://example.test/t.webp");

        Product product = Product.Restore(id, details, 4.5, thumbnail, null, null, SyncState.PendingCreate, isDeleted: true);

        Assert.Multiple(() =>
        {
            Assert.That(product.Id, Is.EqualTo(id));
            Assert.That(product.Details, Is.EqualTo(details));
            Assert.That(product.Rating, Is.EqualTo(4.5));
            Assert.That(product.ThumbnailUrl, Is.EqualTo(thumbnail));
            Assert.That(product.ServerId, Is.Null);
            Assert.That(product.SyncState, Is.EqualTo(SyncState.PendingCreate));
            Assert.That(product.IsDeleted, Is.True);
        });
    }

    [TestCase("v1", "v2", true, TestName = "Server has a newer version")]
    [TestCase("v1", "v1", false, TestName = "Server version is the one already held")]
    [TestCase("v1", null, true, TestName = "Server copy has no version")]
    [TestCase(null, "v1", true, TestName = "Local copy has no version")]
    public void ShouldAcceptServerCopy_ComparesVersions(string? localVersion, string? serverVersion, bool expected)
    {
        Product product = FromServer(version: localVersion);

        Assert.That(product.ShouldAcceptServerCopy(serverVersion), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldAcceptServerCopy_Tombstone_IsFalse()
    {
        Product product = FromServer(version: "v1");
        product.MarkDeleted();

        Assert.That(product.ShouldAcceptServerCopy("v2"), Is.False);
    }

    [TestCase(SyncState.PendingCreate)]
    [TestCase(SyncState.PendingUpdate)]
    [TestCase(SyncState.PendingDelete)]
    [TestCase(SyncState.Failed)]
    public void ShouldAcceptServerCopy_UnsyncedLocalChange_IsFalse(SyncState state)
    {
        Product product = Product.Restore(Guid.NewGuid(), ProductDetailsTests.Create(), null, null, 1, "v1", state, isDeleted: false);

        Assert.That(product.ShouldAcceptServerCopy("v2"), Is.False);
    }

    [Test]
    public void ApplyServerCopy_ReplacesValuesAndKeepsLocalId()
    {
        Product product = FromServer(version: "v1");
        Guid id = product.Id;
        ProductDetails newDetails = ProductDetailsTests.Create(title: "Renamed");

        product.ApplyServerCopy(9, newDetails, 3.2, null, "v2");

        Assert.Multiple(() =>
        {
            Assert.That(product.Id, Is.EqualTo(id));
            Assert.That(product.ServerId, Is.EqualTo(9));
            Assert.That(product.Details, Is.EqualTo(newDetails));
            Assert.That(product.Rating, Is.EqualTo(3.2));
            Assert.That(product.ServerVersion, Is.EqualTo("v2"));
            Assert.That(product.SyncState, Is.EqualTo(SyncState.Synced));
        });
    }

    [Test]
    public void MarkDeleted_MakesATombstone()
    {
        Product product = FromServer();

        product.MarkDeleted();

        Assert.That(product.IsDeleted, Is.True);
    }

    internal static Product FromServer(int serverId = 1, string? version = "v1", ProductDetails? details = null)
    {
        return Product.FromServer(Guid.NewGuid(), serverId, details ?? ProductDetailsTests.Create(), null, null, version);
    }
}
