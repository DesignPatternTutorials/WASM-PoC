using Microsoft.JSInterop;
using OfflineCrud.Application.Connectivity;

namespace OfflineCrud.Infrastructure.Connectivity;

/// <summary>
/// <see cref="IConnectivityService"/> built from three signals: the browser's view (<c>navigator.onLine</c> and its
/// events), the outcome of real requests (reported by <see cref="ConnectivityHandler"/>), and the "Simulate offline"
/// switch.
/// </summary>
public sealed class BrowserConnectivityService : IConnectivityService, IAsyncDisposable
{
    private const string ModulePath = "./_content/OfflineCrud.Infrastructure/js/connectivity.js";

    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserConnectivityService>? _selfReference;

    // Until StartAsync reads navigator.onLine, assume online: requests will soon say otherwise if not.
    private bool _browserOnline = true;
    private bool _lastRequestFailed;
    private bool _simulatingOffline;

    /// <summary>Creates the service. Call <see cref="StartAsync"/> to begin listening to the browser.</summary>
    public BrowserConnectivityService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public ConnectivityStatus Status => (_simulatingOffline, _browserOnline, _lastRequestFailed) switch
    {
        (true, _, _) => ConnectivityStatus.SimulatedOffline,
        (_, false, _) => ConnectivityStatus.Offline,
        (_, _, true) => ConnectivityStatus.Unreachable,
        _ => ConnectivityStatus.Online,
    };

    /// <inheritdoc />
    public bool IsSimulatingOffline => _simulatingOffline;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_module is not null)
        {
            return;
        }

        _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);

        // LEARN: DotNetObjectReference pins this object so JS can call back into it. It must be disposed (see
        // DisposeAsync), or the object can never be garbage collected.
        _selfReference = DotNetObjectReference.Create(this);
        bool online = await _module.InvokeAsync<bool>("start", cancellationToken, _selfReference);
        OnBrowserConnectivityChanged(online);
    }

    /// <inheritdoc />
    public void SetSimulatedOffline(bool simulate)
    {
        Update(() => _simulatingOffline = simulate);
    }

    /// <summary>Called from JavaScript when the browser fires <c>online</c> or <c>offline</c>.</summary>
    /// <param name="online">The new value of <c>navigator.onLine</c>.</param>
    [JSInvokable]
    public void OnBrowserConnectivityChanged(bool online)
    {
        Update(() =>
        {
            _browserOnline = online;

            // The browser says the network is back: give requests the benefit of the doubt. The sync that follows
            // will find out quickly if the server still can't be reached.
            if (online)
            {
                _lastRequestFailed = false;
            }
        });
    }

    /// <summary>Records that a request got a response (any status), proving the server is reachable.</summary>
    public void ReportRequestSucceeded() => Update(() => _lastRequestFailed = false);

    /// <summary>Records that a request failed without a response.</summary>
    public void ReportRequestFailed() => Update(() => _lastRequestFailed = true);

    /// <summary>Stops listening and releases the JS module and the callback reference.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("stop");
            await _module.DisposeAsync();
            _module = null;
        }

        _selfReference?.Dispose();
        _selfReference = null;
    }

    private void Update(Action change)
    {
        ConnectivityStatus before = Status;
        bool simulatingBefore = _simulatingOffline;

        change();

        if (Status != before || _simulatingOffline != simulatingBefore)
        {
            Changed?.Invoke();
        }
    }
}
