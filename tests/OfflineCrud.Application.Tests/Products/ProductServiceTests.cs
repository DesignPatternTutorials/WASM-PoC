using Moq;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Application.Tests.Fakes;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Products;

[TestFixture]
public class ProductServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private Mock<IProductApiClient> _apiClient = null!;
    private Mock<IProductRepository> _repository = null!;
    private Mock<IOutboxStore> _outbox = null!;
    private Mock<ISyncTrigger> _syncTrigger = null!;
    private ManualTimeProvider _time = null!;
    private LocalStoreLock _lock = null!;
    private ProductService _service = null!;
    private List<Product> _saved = null!;

    [SetUp]
    public void SetUp()
    {
        _apiClient = new Mock<IProductApiClient>(MockBehavior.Strict);
        _repository = new Mock<IProductRepository>();
        _outbox = new Mock<IOutboxStore>(MockBehavior.Strict);
        _syncTrigger = new Mock<ISyncTrigger>();
        _time = new ManualTimeProvider(Now);
        _lock = new LocalStoreLock();
        _saved = [];

        _repository.Setup(repo => repo.GetByServerIdsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(repo => repo.SaveAsync(It.IsAny<IReadOnlyCollection<Product>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Product>, CancellationToken>((products, _) => _saved.AddRange(products))
            .Returns(Task.CompletedTask);

        _service = CreateService(_repository.Object, _outbox.Object);
    }

    [TearDown]
    public void TearDown() => _lock.Dispose();

    [Test]
    public async Task GetPageAsync_ReadsLocalStorageOnly()
    {
        PagedResult<Product> page = new([], 0, 10, 10);
        _repository.Setup(repo => repo.GetPageAsync(10, 10, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        PagedResult<Product> result = await _service.GetPageAsync(10, 10, CancellationToken.None);

        Assert.That(result, Is.SameAs(page));
        _apiClient.VerifyNoOtherCalls();
    }

    [TestCase(-1, 10)]
    [TestCase(0, IProductService.MinPageSize - 1)]
    [TestCase(0, IProductService.MaxPageSize + 1)]
    public void GetPageAsync_InvalidPaging_Throws(int skip, int take)
    {
        Assert.That(() => _service.GetPageAsync(skip, take, CancellationToken.None), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task RefreshCatalogueAsync_RecentlyRefreshed_DoesNotCallTheServer()
    {
        SetLastRefreshed(Now - ProductService.CatalogueMaxAge + TimeSpan.FromSeconds(1));

        RefreshResult result = await _service.RefreshCatalogueAsync(force: false, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(RefreshStatus.AlreadyFresh));
        _apiClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RefreshCatalogueAsync_Stale_MergesEverythingAndRecordsTheTime()
    {
        SetLastRefreshed(Now - ProductService.CatalogueMaxAge);
        _apiClient.Setup(api => api.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Server(1), Server(2)]);

        RefreshResult result = await _service.RefreshCatalogueAsync(force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RefreshStatus.Refreshed));
            Assert.That(result.ChangedCount, Is.EqualTo(2));
            Assert.That(_saved.Select(product => product.ServerId), Is.EquivalentTo(new int?[] { 1, 2 }));
        });
        _repository.Verify(repo => repo.GetByServerIdsAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.Order().SequenceEqual(new[] { 1, 2 })), It.IsAny<CancellationToken>()));
        _repository.Verify(repo => repo.SetLastRefreshedAsync(Now, It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task RefreshCatalogueAsync_Force_IgnoresFreshness()
    {
        SetLastRefreshed(Now);
        _apiClient.Setup(api => api.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        RefreshResult result = await _service.RefreshCatalogueAsync(force: true, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(RefreshStatus.Refreshed));
    }

    [Test]
    public async Task RefreshCatalogueAsync_ServerFails_ReportsItAndChangesNothing()
    {
        ApiException offline = new(ApiFailureKind.Network, "offline");
        _apiClient.Setup(api => api.GetAllAsync(It.IsAny<CancellationToken>())).ThrowsAsync(offline);

        RefreshResult result = await _service.RefreshCatalogueAsync(force: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RefreshStatus.Failed));
            Assert.That(result.Error, Is.SameAs(offline));
            Assert.That(_saved, Is.Empty);
        });
        _repository.Verify(repo => repo.SetLastRefreshedAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SearchAsync_ServerAnswers_ReturnsTheLocalCopiesWithoutTombstones()
    {
        Product edited = ProductTests.FromServer(serverId: 1, version: "v1", details: ProductDetailsTests.Create(title: "Edited here"));
        Product deleted = ProductTests.FromServer(serverId: 2, version: "v1");
        deleted.ApplyLocalDelete();
        SetExisting(edited, deleted);
        _apiClient.Setup(api => api.SearchAsync("phone", 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ServerProduct>([Server(1, "v1"), Server(2, "v1"), Server(3, "v1")], 3, 0, 10));

        ProductSearchResult result = await _service.SearchAsync(" phone ", 0, 10, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProductDataSource.Server));
            Assert.That(result.Page.Items.Select(product => product.ServerId), Is.EqualTo(new int?[] { 1, 3 }));
            Assert.That(result.Page.Items[0], Is.SameAs(edited), "the local copy, with its local edit");
            Assert.That(result.Page.TotalCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task SearchAsync_ServerUnreachable_SearchesThisDevice()
    {
        ApiException offline = new(ApiFailureKind.Network, "offline");
        _apiClient.Setup(api => api.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(offline);
        _repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            ProductTests.FromServer(serverId: 1, details: ProductDetailsTests.Create(title: "Apple phone")),
            ProductTests.FromServer(serverId: 2, details: ProductDetailsTests.Create(title: "Garden chair", description: "Wooden", category: "furniture")),
            ProductTests.FromServer(serverId: 3, details: ProductDetailsTests.Create(title: "Another phone")),
        ]);

        ProductSearchResult result = await _service.SearchAsync("phone", 1, 10, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProductDataSource.Device));
            Assert.That(result.ServerError, Is.SameAs(offline));
            Assert.That(result.Page.TotalCount, Is.EqualTo(2));
            Assert.That(result.Page.Items.Select(product => product.ServerId), Is.EqualTo(new int?[] { 3 }), "skip applies to the matches");
        });
    }

    [Test]
    public async Task GetAsync_HidesTombstones()
    {
        Product deleted = ProductTests.FromServer();
        deleted.ApplyLocalDelete();
        _repository.Setup(repo => repo.GetAsync(deleted.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);

        Assert.That(await _service.GetAsync(deleted.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task RefreshAsync_ProductWithoutServerId_IsNotOnServer()
    {
        Product localOnly = Product.Restore(Guid.NewGuid(), ProductDetailsTests.Create(), null, null, null, null, SyncState.PendingCreate, false);
        SetStored(localOnly);

        RefreshResult result = await _service.RefreshAsync(localOnly.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(RefreshStatus.NotOnServer));
        _apiClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RefreshAsync_ServerReturns404_IsNotOnServer()
    {
        Product created = ProductTests.FromServer(serverId: 195, version: null);
        SetStored(created);
        _apiClient.Setup(api => api.GetByIdAsync(195, It.IsAny<CancellationToken>())).ReturnsAsync((ServerProduct?)null);

        RefreshResult result = await _service.RefreshAsync(created.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(RefreshStatus.NotOnServer));
    }

    [Test]
    public async Task RefreshAsync_NewerOnServer_MergesIt()
    {
        Product product = ProductTests.FromServer(serverId: 4, version: "v1");
        SetStored(product);
        SetExisting(product);
        _apiClient.Setup(api => api.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(Server(4, "v2"));

        RefreshResult result = await _service.RefreshAsync(product.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(RefreshResult.Refreshed(1)));
            Assert.That(_saved, Is.EqualTo(new[] { product }));
        });
    }

    [Test]
    public async Task GetCategoriesAsync_SortsAndCachesForTheSession()
    {
        _apiClient.Setup(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["tablets", "Beauty", "laptops"]);

        IReadOnlyList<string> first = await _service.GetCategoriesAsync(CancellationToken.None);
        IReadOnlyList<string> second = await _service.GetCategoriesAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { "Beauty", "laptops", "tablets" }));
            Assert.That(second, Is.SameAs(first));
        });
        _apiClient.Verify(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetCategoriesAsync_Offline_UsesTheCategoriesOfStoredProducts()
    {
        _apiClient.Setup(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Network, "offline"));
        _repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            ProductTests.FromServer(details: ProductDetailsTests.Create(category: "tablets")),
            ProductTests.FromServer(details: ProductDetailsTests.Create(category: "beauty")),
            ProductTests.FromServer(details: ProductDetailsTests.Create(category: "tablets")),
        ]);

        IReadOnlyList<string> categories = await _service.GetCategoriesAsync(CancellationToken.None);

        Assert.That(categories, Is.EqualTo(new[] { "beauty", "tablets" }));
    }


    [Test]
    public async Task CreateAsync_SavesLocallyWithAnOutboxEntry_AndNeverCallsTheServer()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        ProductDetails details = ProductDetailsTests.Create();

        Product created = await service.CreateAsync(details, CancellationToken.None);

        OutboxEntry entry = store.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.ServerId, Is.Null);
            Assert.That(store.Product(created.Id)!.SyncState, Is.EqualTo(SyncState.PendingCreate));
            Assert.That(entry.Operation, Is.EqualTo(OutboxOperation.Create));
            Assert.That(entry.EntityId, Is.EqualTo(created.Id));
            Assert.That(entry.Payload, Is.EqualTo(details));
            Assert.That(entry.CreatedAt, Is.EqualTo(Now));
            Assert.That(store.CommitCount, Is.EqualTo(1), "product and entry in one transaction");
        });
        _apiClient.VerifyNoOtherCalls();
        _syncTrigger.Verify(trigger => trigger.RequestSync(), Times.Once);
    }

    [Test]
    public async Task UpdateAsync_SyncedProduct_QueuesAnUpdate()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        Product product = ProductTests.FromServer(serverId: 4);
        store.Seed(product);
        ProductDetails edited = ProductDetailsTests.Create(title: "Edited");

        Product updated = await service.UpdateAsync(product.Id, edited, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Details, Is.EqualTo(edited));
            Assert.That(store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.PendingUpdate));
            Assert.That(store.Entries.Single().Operation, Is.EqualTo(OutboxOperation.Update));
            Assert.That(store.Entries.Single().Payload, Is.EqualTo(edited));
        });
        _syncTrigger.Verify(trigger => trigger.RequestSync(), Times.Once);
    }

    [Test]
    public async Task UpdateAsync_BeforeTheCreateIsSent_FoldsIntoTheCreate()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        Product created = await service.CreateAsync(ProductDetailsTests.Create(title: "First"), CancellationToken.None);
        ProductDetails edited = ProductDetailsTests.Create(title: "Second");

        await service.UpdateAsync(created.Id, edited, CancellationToken.None);

        OutboxEntry entry = store.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Operation, Is.EqualTo(OutboxOperation.Create));
            Assert.That(entry.Payload, Is.EqualTo(edited));
            Assert.That(store.Product(created.Id)!.SyncState, Is.EqualTo(SyncState.PendingCreate));
        });
    }

    [Test]
    public async Task DeleteAsync_BeforeTheCreateIsSent_DropsBoth()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        Product created = await service.CreateAsync(ProductDetailsTests.Create(), CancellationToken.None);

        await service.DeleteAsync(created.Id, CancellationToken.None);

        Product tombstone = store.Product(created.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(store.Entries, Is.Empty);
            Assert.That(tombstone.IsDeleted, Is.True);
            Assert.That(tombstone.SyncState, Is.EqualTo(SyncState.Synced), "nothing to tell the server");
        });
    }

    [Test]
    public async Task DeleteAsync_SyncedProduct_QueuesADeleteAndHidesIt()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        Product product = ProductTests.FromServer(serverId: 4);
        store.Seed(product);

        await service.DeleteAsync(product.Id, CancellationToken.None);
        Product? visible = await service.GetAsync(product.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(store.Entries.Single().Operation, Is.EqualTo(OutboxOperation.Delete));
            Assert.That(store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.PendingDelete));
            Assert.That(visible, Is.Null);
        });
    }

    [Test]
    public async Task RefreshCatalogueAsync_DoesNotOverwriteAProductWithAQueuedEdit()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);
        Product product = ProductTests.FromServer(serverId: 4, version: "v1");
        store.Seed(product);
        await service.UpdateAsync(product.Id, ProductDetailsTests.Create(title: "Edited offline"), CancellationToken.None);
        _apiClient.Setup(api => api.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ServerProduct(4, ProductDetailsTests.Create(title: "Changed on server"), null, null, "v2")]);

        RefreshResult result = await service.RefreshCatalogueAsync(force: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ChangedCount, Is.Zero);
            Assert.That(store.Product(product.Id)!.Details.Title, Is.EqualTo("Edited offline"));
        });
    }

    [Test]
    public void UpdateAsync_UnknownProduct_Throws()
    {
        InMemoryLocalStore store = new();
        ProductService service = CreateService(store, store);

        Assert.That(
            () => service.UpdateAsync(Guid.NewGuid(), ProductDetailsTests.Create(), CancellationToken.None),
            Throws.InvalidOperationException);
    }

    private ProductService CreateService(IProductRepository repository, IOutboxStore outbox)
    {
        return new ProductService(_apiClient.Object, repository, outbox, _lock, _syncTrigger.Object, _time);
    }

    private static ServerProduct Server(int serverId, string? version = "v1")
    {
        return new ServerProduct(serverId, ProductDetailsTests.Create(), 4.0, null, version);
    }

    private void SetLastRefreshed(DateTimeOffset when)
    {
        _repository.Setup(repo => repo.GetLastRefreshedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(when);
    }

    private void SetExisting(params Product[] products)
    {
        _repository.Setup(repo => repo.GetByServerIdsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                [.. products.Where(product => product.ServerId is int id && ids.Contains(id))]);
    }

    private void SetStored(Product product)
    {
        _repository.Setup(repo => repo.GetAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
    }
}
