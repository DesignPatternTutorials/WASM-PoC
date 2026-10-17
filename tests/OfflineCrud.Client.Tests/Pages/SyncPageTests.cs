using Moq;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Sync;
using OfflineCrud.Client.Pages;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Client.Tests.Pages;

[TestFixture]
public class SyncPageTests : ComponentTestBase
{
    private static readonly ProductDetails Lamp = new("Lamp", "A lamp", "furniture", null, 10m, 0m, 3);

    private AppServices _services = null!;

    [SetUp]
    public void SetUpServices() => _services = new AppServices(Context);

    [TearDown]
    public async Task DisposeScheduler() => await _services.Scheduler.DisposeAsync();

    [Test]
    public void EmptyOutbox_SaysEverythingIsSynced()
    {
        IRenderedComponent<Sync> page = Context.Render<Sync>();

        page.WaitForAssertion(() => Assert.That(page.Markup, Does.Contain("Nothing waiting")));
    }

    [Test]
    public void FailedEntry_ShowsItsReason_WithRetryAndDiscard()
    {
        OutboxEntry failed = Entry(OutboxStatus.Failed, "The server has no product #195");
        SetOutbox(failed);

        IRenderedComponent<Sync> page = Context.Render<Sync>();

        page.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement row = page.Find("tbody tr");
            Assert.That(row.TextContent, Does.Contain("Update").And.Contains("“Lamp”").And.Contains("The server has no product #195"));
            Assert.That(row.QuerySelectorAll("button").Select(button => button.TextContent.Trim()), Is.EqualTo(new[] { "Retry", "Discard" }));
        });
    }

    [Test]
    public void PendingEntry_HasNoRetryOrDiscard()
    {
        SetOutbox(Entry(OutboxStatus.Pending, lastError: null));

        IRenderedComponent<Sync> page = Context.Render<Sync>();

        page.WaitForAssertion(() => Assert.That(page.Find("tbody tr").QuerySelectorAll("button"), Is.Empty));
    }

    [Test]
    public void Retry_AsksTheEngineToRetryTheEntry()
    {
        OutboxEntry failed = Entry(OutboxStatus.Failed, "rejected");
        SetOutbox(failed);
        IRenderedComponent<Sync> page = Context.Render<Sync>();
        page.WaitForAssertion(() => Assert.That(page.FindAll("tbody tr"), Has.Count.EqualTo(1)));

        page.FindAll("tbody button").Single(button => button.TextContent.Contains("Retry")).Click();

        _services.Sync.Verify(sync => sync.RetryAsync(failed.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void Discard_ConfirmsFirst_ThenFetchesTheServerCopy()
    {
        OutboxEntry failed = Entry(OutboxStatus.Failed, "rejected");
        SetOutbox(failed);
        _services.Sync.Setup(sync => sync.DiscardAsync(failed.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failed.EntityId);
        IRenderedComponent<Sync> page = Context.Render<Sync>();
        page.WaitForAssertion(() => Assert.That(page.FindAll("tbody tr"), Has.Count.EqualTo(1)));

        page.FindAll("tbody button").Single(button => button.TextContent.Contains("Discard")).Click();
        _services.Sync.Verify(sync => sync.DiscardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        page.Find("[role=dialog] .btn-danger").Click();

        page.WaitForAssertion(() =>
        {
            _services.Sync.Verify(sync => sync.DiscardAsync(failed.Id, It.IsAny<CancellationToken>()), Times.Once);
            _services.Products.Verify(products => products.RefreshAsync(failed.EntityId, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public void SyncNow_IsDisabledWhileCertainlyOffline()
    {
        _services.Connectivity.SetStatus(ConnectivityStatus.SimulatedOffline);

        IRenderedComponent<Sync> page = Context.Render<Sync>();

        Assert.That(page.Find(".page-header .btn-primary").HasAttribute("disabled"), Is.True);
    }

    private void SetOutbox(params OutboxEntry[] entries)
    {
        _services.Sync.Setup(sync => sync.GetOutboxItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. entries.Select(entry => new OutboxItem(entry, "Lamp", 195))]);
    }

    private static OutboxEntry Entry(OutboxStatus status, string? lastError)
    {
        return OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Update, Guid.NewGuid(), Lamp, DateTimeOffset.UtcNow.AddMinutes(-1))
            with { Status = status, LastError = lastError };
    }
}
