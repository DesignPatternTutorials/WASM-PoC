using Microsoft.JSInterop;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Infrastructure.Connectivity;

namespace OfflineCrud.Infrastructure.Tests.Connectivity;

[TestFixture]
public class BrowserConnectivityServiceTests
{
    private BrowserConnectivityService _service = null!;
    private int _changes;

    [SetUp]
    public void SetUp()
    {
        // StartAsync isn't called, so no JS runs; the browser's events are simulated by calling the [JSInvokable] directly.
        _service = new BrowserConnectivityService(new NoJsRuntime());
        _changes = 0;
        _service.Changed += () => _changes++;
    }

    [TearDown]
    public async Task TearDown() => await _service.DisposeAsync();

    [Test]
    public void StartsOnline()
    {
        Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Online));
    }

    [Test]
    public void BrowserOffline_IsOffline()
    {
        _service.OnBrowserConnectivityChanged(online: false);

        Assert.Multiple(() =>
        {
            Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Offline));
            Assert.That(_changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void FailedRequest_WhileTheBrowserSaysOnline_IsUnreachable()
    {
        // navigator.onLine is true on a Wi-Fi with no internet; only a request can tell.
        _service.ReportRequestFailed();

        Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Unreachable));
    }

    [Test]
    public void SuccessfulRequest_ClearsUnreachable()
    {
        _service.ReportRequestFailed();
        _service.ReportRequestSucceeded();

        Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Online));
    }

    [Test]
    public void BrowserComingBackOnline_ClearsUnreachable()
    {
        _service.OnBrowserConnectivityChanged(online: false);
        _service.ReportRequestFailed();
        _service.OnBrowserConnectivityChanged(online: true);

        Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Online));
    }

    [Test]
    public void SimulatedOffline_WinsOverEverythingElse()
    {
        _service.OnBrowserConnectivityChanged(online: false);
        _service.SetSimulatedOffline(true);

        Assert.Multiple(() =>
        {
            Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.SimulatedOffline));
            Assert.That(_service.IsSimulatingOffline, Is.True);
        });

        _service.SetSimulatedOffline(false);
        Assert.That(_service.Status, Is.EqualTo(ConnectivityStatus.Offline), "back to what the browser says");
    }

    [Test]
    public void Changed_OnlyFiresWhenSomethingChanges()
    {
        _service.ReportRequestSucceeded();
        _service.OnBrowserConnectivityChanged(online: true);
        _service.ReportRequestFailed();
        _service.ReportRequestFailed();

        Assert.That(_changes, Is.EqualTo(1));
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("No JS in unit tests.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("No JS in unit tests.");
    }
}
