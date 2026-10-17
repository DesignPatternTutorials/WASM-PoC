using OfflineCrud.Application.Products;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Products;

[TestFixture]
public class ServerCopyMergerTests
{
    private static readonly Guid NewId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Test]
    public void UnknownServerProduct_IsAddedWithANewLocalId()
    {
        ServerProduct server = Server(5, "v1");

        ServerCopyMergeResult result = ServerCopyMerger.Merge([], [server], () => NewId);

        Product added = result.Changed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(added.Id, Is.EqualTo(NewId));
            Assert.That(added.ServerId, Is.EqualTo(5));
            Assert.That(added.ServerVersion, Is.EqualTo("v1"));
            Assert.That(result.ByServerId[5], Is.SameAs(added));
        });
    }

    [Test]
    public void SameVersion_IsLeftAlone_SoLocalEditsSurvive()
    {
        Product local = ProductTests.FromServer(serverId: 5, version: "v1", details: ProductDetailsTests.Create(title: "Edited here"));

        ServerCopyMergeResult result = ServerCopyMerger.Merge([local], [Server(5, "v1", title: "Original")], () => NewId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.Empty);
            Assert.That(local.Details.Title, Is.EqualTo("Edited here"));
            Assert.That(result.ByServerId[5], Is.SameAs(local));
        });
    }

    [Test]
    public void NewerVersion_ReplacesTheLocalCopy()
    {
        Product local = ProductTests.FromServer(serverId: 5, version: "v1");
        Guid localId = local.Id;

        ServerCopyMergeResult result = ServerCopyMerger.Merge([local], [Server(5, "v2", title: "Changed on server")], () => NewId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.EqualTo(new[] { local }));
            Assert.That(local.Id, Is.EqualTo(localId));
            Assert.That(local.Details.Title, Is.EqualTo("Changed on server"));
            Assert.That(local.ServerVersion, Is.EqualTo("v2"));
        });
    }

    [Test]
    public void Tombstone_IsNotResurrected()
    {
        Product local = ProductTests.FromServer(serverId: 5, version: "v1");
        local.ApplyLocalDelete();

        ServerCopyMergeResult result = ServerCopyMerger.Merge([local], [Server(5, "v2")], () => NewId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.Empty);
            Assert.That(local.IsDeleted, Is.True);
            Assert.That(result.ByServerId[5].IsDeleted, Is.True);
        });
    }

    [Test]
    public void PendingLocalChange_IsNotOverwritten()
    {
        Product local = Product.Restore(
            Guid.NewGuid(), ProductDetailsTests.Create(title: "Not sent yet"), null, null, 5, "v1", SyncState.PendingUpdate, isDeleted: false);

        ServerCopyMergeResult result = ServerCopyMerger.Merge([local], [Server(5, "v2")], () => NewId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.Empty);
            Assert.That(local.Details.Title, Is.EqualTo("Not sent yet"));
        });
    }

    [Test]
    public void SeveralLocalsWithTheSameServerId_OnlyTheFirstIsMatched()
    {
        // DummyJSON gives every created product server id 195.
        Product first = ProductTests.FromServer(serverId: 195, version: null);
        Product second = ProductTests.FromServer(serverId: 195, version: null);

        ServerCopyMergeResult result = ServerCopyMerger.Merge([first, second], [Server(195, "v1")], () => NewId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.EqualTo(new[] { first }));
            Assert.That(result.ByServerId[195], Is.SameAs(first));
        });
    }

    [Test]
    public void MixedBatch_ReportsOnlyWhatChanged()
    {
        Product unchanged = ProductTests.FromServer(serverId: 1, version: "v1");
        Product stale = ProductTests.FromServer(serverId: 2, version: "v1");
        Guid[] ids = [Guid.NewGuid()];
        int next = 0;

        ServerCopyMergeResult result = ServerCopyMerger.Merge(
            [unchanged, stale],
            [Server(1, "v1"), Server(2, "v2"), Server(3, "v1")],
            () => ids[next++]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed.Select(product => product.ServerId), Is.EquivalentTo(new int?[] { 2, 3 }));
            Assert.That(result.ByServerId.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }));
        });
    }

    private static ServerProduct Server(int serverId, string? version, string title = "Phone")
    {
        return new ServerProduct(serverId, ProductDetailsTests.Create(title: title), 4.1, null, version);
    }
}
