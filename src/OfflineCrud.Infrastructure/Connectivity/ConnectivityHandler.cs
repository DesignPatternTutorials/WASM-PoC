namespace OfflineCrud.Infrastructure.Connectivity;

/// <summary>
/// A step in the HttpClient pipeline that feeds request outcomes into <see cref="BrowserConnectivityService"/>, and
/// blocks every request while "Simulate offline" is on.
/// </summary>
internal sealed class ConnectivityHandler : DelegatingHandler
{
    private readonly BrowserConnectivityService _connectivity;

    public ConnectivityHandler(BrowserConnectivityService connectivity)
    {
        _connectivity = connectivity;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // LEARN: Simulating offline here, in the HTTP pipeline, means nothing above it can tell the difference. The
        // request never leaves the browser, and the caller gets the same HttpRequestException a real offline fetch()
        // produces. So the API client, sync engine and pages follow exactly the code paths they would offline.
        if (_connectivity.IsSimulatingOffline)
        {
            throw new HttpRequestException("Simulated offline: the request was blocked by the app (TypeError: Failed to fetch).");
        }

        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            // Any response, even a 404 or 500, proves the server can be reached.
            _connectivity.ReportRequestSucceeded();
            return response;
        }
        catch (HttpRequestException)
        {
            _connectivity.ReportRequestFailed();
            throw;
        }

        // Timeouts aren't reported here: HttpClient enforces Timeout by cancelling the token it passes down this
        // pipeline, so inside a handler a timeout looks exactly like the caller cancelling, which says nothing about the
        // network. The API client still turns a timeout into a Network failure for its caller.
    }
}
