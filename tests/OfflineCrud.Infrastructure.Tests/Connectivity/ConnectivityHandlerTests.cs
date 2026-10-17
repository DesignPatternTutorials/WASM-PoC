using Microsoft.JSInterop;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Infrastructure.Connectivity;

namespace OfflineCrud.Infrastructure.Tests.Connectivity;

[TestFixture]
public class ConnectivityHandlerTests
{
    private static readonly Uri Address = new("https://dummyjson.example/products");

    private BrowserConnectivityService _connectivity = null!;

    [SetUp]
    public void SetUp()
    {
        _connectivity = new BrowserConnectivityService(new UnusedJsRuntime());
    }

    [TearDown]
    public async Task TearDown() => await _connectivity.DisposeAsync();

    [Test]
    public async Task Response_OfAnyStatus_MarksTheServerReachable()
    {
        _connectivity.ReportRequestFailed();
        using HttpClient client = Client(StubHttpMessageHandler.Returning(HttpStatusCode.InternalServerError));

        using HttpResponseMessage response = await client.GetAsync(Address);

        Assert.That(_connectivity.Status, Is.EqualTo(ConnectivityStatus.Online));
    }

    [Test]
    public void NetworkFailure_MarksTheServerUnreachable()
    {
        using HttpClient client = Client(StubHttpMessageHandler.Throwing(new HttpRequestException("TypeError: Failed to fetch")));

        Assert.Multiple(() =>
        {
            Assert.That(() => client.GetAsync(Address), Throws.InstanceOf<HttpRequestException>());
            Assert.That(_connectivity.Status, Is.EqualTo(ConnectivityStatus.Unreachable));
        });
    }

    [Test]
    public void SimulatedOffline_FailsLikeARealOfflineFetch_WithoutSendingAnything()
    {
        _connectivity.SetSimulatedOffline(true);
        StubHttpMessageHandler inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        using HttpClient client = Client(inner);

        Assert.Multiple(() =>
        {
            Assert.That(() => client.GetAsync(Address), Throws.InstanceOf<HttpRequestException>().With.Message.Contains("Simulated offline"));
            Assert.That(inner.Requests, Is.Empty, "the request never left the app");
        });
    }

    private HttpClient Client(HttpMessageHandler inner)
    {
        return new HttpClient(new ConnectivityHandler(_connectivity) { InnerHandler = inner });
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();
    }
}
