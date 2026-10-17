namespace OfflineCrud.Application.Connectivity;

/// <summary>Whether the app can reach the server, as far as it can tell.</summary>
public enum ConnectivityStatus
{
    /// <summary>The browser reports a connection and the last request got a response.</summary>
    Online,

    /// <summary>The browser reports no network connection at all.</summary>
    Offline,

    /// <summary>The browser reports a connection, but requests are failing (no internet, server down, captive portal).</summary>
    Unreachable,

    /// <summary>The user switched on "Simulate offline", so the app is blocking its own requests.</summary>
    SimulatedOffline,
}

/// <summary>
/// Port for connectivity. Implemented in Infrastructure from the browser's <c>navigator.onLine</c> and
/// <c>online</c>/<c>offline</c> events, plus the outcome of real requests.
/// </summary>
/// <remarks>
/// LEARN: <c>navigator.onLine</c> only means "connected to some network". It's <c>true</c> on a Wi-Fi with no internet,
/// behind a captive portal, or when the server itself is down. Its <c>false</c> is reliable; its <c>true</c> isn't.
/// So a request that fails at the network level also counts as offline (<see cref="ConnectivityStatus.Unreachable"/>)
/// until a request succeeds again.
/// </remarks>
public interface IConnectivityService
{
    /// <summary>The current status.</summary>
    ConnectivityStatus Status { get; }

    /// <summary>Whether the app currently believes requests can succeed.</summary>
    bool IsOnline => Status == ConnectivityStatus.Online;

    /// <summary>Whether "Simulate offline" is switched on.</summary>
    bool IsSimulatingOffline { get; }

    /// <summary>Raised whenever <see cref="Status"/> or <see cref="IsSimulatingOffline"/> changes.</summary>
    event Action? Changed;

    /// <summary>Starts listening to the browser's connectivity events. Call once at start-up.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Switches "Simulate offline" on or off. While on, every request fails without leaving the browser.</summary>
    void SetSimulatedOffline(bool simulate);
}
