using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Sync;
using OfflineCrud.Client.Layout;

namespace OfflineCrud.Client.Tests.Layout;

[TestFixture]
public class LayoutTests : ComponentTestBase
{
    private AppServices _services = null!;

    [SetUp]
    public void SetUpServices() => _services = new AppServices(Context);

    [TearDown]
    public async Task DisposeScheduler() => await _services.Scheduler.DisposeAsync();

    [Test]
    public void Banner_IsHiddenWhileOnline()
    {
        IRenderedComponent<ConnectivityBanner> banner = Context.Render<ConnectivityBanner>();

        Assert.That(banner.Markup.Trim(), Is.Empty);
    }

    [TestCase(ConnectivityStatus.Offline, "You're offline.")]
    [TestCase(ConnectivityStatus.Unreachable, "DummyJSON can't be reached.")]
    [TestCase(ConnectivityStatus.SimulatedOffline, "Simulated offline")]
    public void Banner_SaysWhichKindOfOffline_AndIsALiveRegion(ConnectivityStatus status, string text)
    {
        IRenderedComponent<ConnectivityBanner> banner = Context.Render<ConnectivityBanner>();

        _services.Connectivity.SetStatus(status);

        banner.WaitForAssertion(() =>
        {
            Assert.That(banner.Find("[role=status]").TextContent, Does.Contain(text));
        });
    }

    [Test]
    public void Banner_GoBackOnline_StopsSimulating()
    {
        _services.Connectivity.SetStatus(ConnectivityStatus.SimulatedOffline);
        IRenderedComponent<ConnectivityBanner> banner = Context.Render<ConnectivityBanner>();

        banner.Find("button").Click();

        Assert.Multiple(() =>
        {
            Assert.That(_services.Connectivity.SimulateRequests, Is.EqualTo(new[] { false }));
            Assert.That(banner.Markup.Trim(), Is.Empty);
        });
    }

    [Test]
    public void NavMenu_SimulateOfflineSwitch_DrivesTheConnectivityService()
    {
        IRenderedComponent<NavMenu> nav = Context.Render<NavMenu>();

        nav.Find("#simulate-offline").Change(true);

        Assert.Multiple(() =>
        {
            Assert.That(_services.Connectivity.SimulateRequests, Is.EqualTo(new[] { true }));
            Assert.That(nav.Find("label[for=simulate-offline]").TextContent, Is.EqualTo("Simulate offline"), "the switch has a label");
        });
    }

    [Test]
    public void NavMenu_MarksTheCurrentPage()
    {
        Context.Services.GetRequiredService<NavigationManager>().NavigateTo("/sync");

        IRenderedComponent<NavMenu> nav = Context.Render<NavMenu>();

        AngleSharp.Dom.IElement current = nav.Find("a.active");
        Assert.Multiple(() =>
        {
            Assert.That(current.TextContent, Does.Contain("Sync"));
            Assert.That(current.GetAttribute("aria-current"), Is.EqualTo("page"), "NavLink adds aria-current for screen readers");
        });
    }

    [Test]
    public void NavMenu_ShowsHowManyChangesAreWaiting()
    {
        OutboxEntry pending = OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Delete, Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        _services.Sync.Setup(sync => sync.GetOutboxAsync(It.IsAny<CancellationToken>())).ReturnsAsync([pending, pending with { Id = Guid.NewGuid() }]);
        IRenderedComponent<NavMenu> nav = Context.Render<NavMenu>();

        _services.Scheduler.Start();

        nav.WaitForAssertion(() =>
        {
            Assert.That(nav.Find("a[href=sync] .badge").TextContent, Does.StartWith("2").And.Contains("changes waiting to sync"));
        });
    }
}
