using OfflineCrud.Application.Connectivity;

namespace OfflineCrud.Client.Tests.Fakes;

/// <summary>A connectivity service whose status the test sets directly.</summary>
internal sealed class FakeConnectivityService : IConnectivityService
{
    private ConnectivityStatus _status = ConnectivityStatus.Online;

    public event Action? Changed;

    public ConnectivityStatus Status => _status;

    public bool IsSimulatingOffline => _status == ConnectivityStatus.SimulatedOffline;

    public List<bool> SimulateRequests { get; } = [];

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void SetSimulatedOffline(bool simulate)
    {
        SimulateRequests.Add(simulate);
        SetStatus(simulate ? ConnectivityStatus.SimulatedOffline : ConnectivityStatus.Online);
    }

    public void SetStatus(ConnectivityStatus status)
    {
        _status = status;
        Changed?.Invoke();
    }
}
