using Moq;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Products;
using OfflineCrud.Client.Pages.Products;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Client.Tests.Pages;

[TestFixture]
public class ProductListTests : ComponentTestBase
{
    private AppServices _services = null!;

    [SetUp]
    public void SetUpServices() => _services = new AppServices(Context);

    [TearDown]
    public async Task DisposeScheduler() => await _services.Scheduler.DisposeAsync();

    [Test]
    public void ShowsTheSavedCopy_WithSyncBadges()
    {
        SetLocalPage(Synced("Lamp"), Pending("Chair"));
        SetRefresh(RefreshResult.AlreadyFresh);

        IRenderedComponent<ProductList> page = Context.Render<ProductList>();

        page.WaitForAssertion(() =>
        {
            Assert.That(page.FindAll("tbody tr"), Has.Count.EqualTo(2));
            Assert.That(page.FindAll("tbody .badge").Select(badge => badge.TextContent.Trim()), Does.Contain("Edited, not synced"));
        });
    }

    [Test]
    public void Offline_StillShowsTheSavedCopy_AndSaysSo()
    {
        SetLocalPage(Synced("Lamp"));
        SetRefresh(RefreshResult.Failed(new ApiException(ApiFailureKind.Network, "Couldn't reach the server.")));

        IRenderedComponent<ProductList> page = Context.Render<ProductList>();

        page.WaitForAssertion(() =>
        {
            Assert.That(page.Find("tbody tr").TextContent, Does.Contain("Lamp"));
            Assert.That(page.Markup, Does.Contain("Offline.").And.Contains("Showing the copy saved on this device"));
        });
    }

    [Test]
    public void SuccessfulRefresh_ReReadsTheLocalPage()
    {
        _services.Products.SetupSequence(products => products.GetPageAsync(0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(Synced("Old title")))
            .ReturnsAsync(Page(Synced("New title")));
        SetRefresh(RefreshResult.Refreshed(1));

        IRenderedComponent<ProductList> page = Context.Render<ProductList>();

        page.WaitForAssertion(() => Assert.That(page.Find("tbody tr").TextContent, Does.Contain("New title")));
    }

    [Test]
    public void NothingSavedAndOffline_ExplainsHowToGetData()
    {
        SetLocalPage();
        SetRefresh(RefreshResult.Failed(new ApiException(ApiFailureKind.Network, "offline")));

        IRenderedComponent<ProductList> page = Context.Render<ProductList>();

        page.WaitForAssertion(() => Assert.That(page.Markup, Does.Contain("Nothing is saved on this device yet")));
    }

    [Test]
    public void ComingBackOnline_RevalidatesTheCatalogue()
    {
        SetLocalPage(Synced("Lamp"));
        SetRefresh(RefreshResult.AlreadyFresh);
        _services.Connectivity.SetStatus(ConnectivityStatus.Offline);
        IRenderedComponent<ProductList> page = Context.Render<ProductList>();

        _services.Connectivity.SetStatus(ConnectivityStatus.Online);

        page.WaitForAssertion(() =>
            _services.Products.Verify(products => products.RefreshCatalogueAsync(true, It.IsAny<CancellationToken>()), Times.Once));
    }

    [Test]
    public void Delete_AsksFirst_ThenDeletesLocallyAndConfirmsWithAToast()
    {
        Product lamp = Synced("Lamp");
        SetLocalPage(lamp);
        SetRefresh(RefreshResult.AlreadyFresh);
        IRenderedComponent<ProductList> page = Context.Render<ProductList>();
        page.WaitForAssertion(() => Assert.That(page.FindAll("tbody tr"), Has.Count.EqualTo(1)));

        page.Find("button[title=Delete]").Click();
        page.Find("[role=dialog] .btn-danger").Click();

        page.WaitForAssertion(() =>
        {
            _services.Products.Verify(products => products.DeleteAsync(lamp.Id, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(_services.Toasts.Toasts.Single().Message, Does.StartWith("Deleted “Lamp” on this device"));
            Assert.That(page.FindAll("[role=dialog]"), Is.Empty);
        });
    }

    private void SetLocalPage(params Product[] products)
    {
        _services.Products.Setup(service => service.GetPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(products));
    }

    private void SetRefresh(RefreshResult result)
    {
        _services.Products.Setup(service => service.RefreshCatalogueAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static PagedResult<Product> Page(params Product[] products) => new(products, products.Length, 0, 10);

    private static Product Synced(string title) =>
        Product.FromServer(Guid.NewGuid(), 1, Details(title), null, null, "v1");

    private static Product Pending(string title) =>
        Product.Restore(Guid.NewGuid(), Details(title), null, null, 2, "v1", SyncState.PendingUpdate, isDeleted: false);

    private static ProductDetails Details(string title) => new(title, "Description", "furniture", null, 10m, 0m, 3);
}
