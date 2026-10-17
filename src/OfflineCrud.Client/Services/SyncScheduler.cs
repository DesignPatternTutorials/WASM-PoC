using System.Threading.Channels;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Sync;

namespace OfflineCrud.Client.Services;

/// <summary>
/// Decides when the sync engine runs, and tells the UI how it's going.
/// </summary>
/// <remarks>
/// <para>Runs happen at start-up, after every local write (via <see cref="RequestSync"/>), when "Sync now" is pressed,
/// when the connection comes back,
/// every <see cref="Interval"/> while the app is open, and when the earliest backed-off entry falls due. One loop does
/// all the running, so runs never overlap.</para>
/// <para>
/// LEARN: Why only while the app is open? The browser's Background Sync API lets a service worker finish work after the
/// tab closes, but a service worker runs JavaScript, not .NET. The .NET runtime and this C# code only exist inside an
/// open tab. A JS-only version is possible as a stretch: the service worker would read the IndexedDB outbox itself and
/// replay it with fetch() in a `sync` event, duplicating the engine's rules in JS. Background Sync is also not available
/// in every browser (notably Safari and Firefox).
/// </para>
/// </remarks>
public sealed class SyncScheduler : ISyncTrigger, IAsyncDisposable
{
    /// <summary>How often to run while the app is open, even without a trigger (e.g. to retry after a backoff).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ISyncService _syncService;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SyncScheduler> _logger;
    private readonly CancellationTokenSource _stopping = new();

    // Capacity 1, extra writes dropped: ten requests in a burst become one run.
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private Task? _loop;
    private bool _wasOnline;

    /// <summary>Creates the scheduler. Call <see cref="Start"/> once the app has started.</summary>
    public SyncScheduler(ISyncService syncService, IConnectivityService connectivity, TimeProvider timeProvider, ILogger<SyncScheduler> logger)
    {
        _syncService = syncService;
        _connectivity = connectivity;
        _timeProvider = timeProvider;
        _logger = logger;

        _wasOnline = connectivity.IsOnline;
        connectivity.Changed += OnConnectivityChanged;
    }

    /// <summary>Raised whenever <see cref="IsRunning"/>, <see cref="LastResult"/> or <see cref="Outbox"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Raised after every completed run that actually ran, with its result. For notifications.</summary>
    public event Action<SyncRunResult>? RunCompleted;

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Whether the last run was skipped because the app is offline. Nothing is sent while offline: the requests would
    /// only fail, and each failure would push the entry's next retry further away.
    /// </summary>
    public bool PausedOffline { get; private set; }

    /// <summary>What the last completed run did, or <see langword="null"/> before the first one.</summary>
    public SyncRunResult? LastResult { get; private set; }

    /// <summary>When the last run completed.</summary>
    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>The unexpected error that ended the last run, if any (not an API failure, which the engine handles).</summary>
    public Exception? LastError { get; private set; }

    /// <summary>The outbox as of the last run.</summary>
    public IReadOnlyList<OutboxEntry> Outbox { get; private set; } = [];

    /// <summary>Starts the background loop. Safe to call once.</summary>
    public void Start()
    {
        _loop ??= RunLoopAsync(_stopping.Token);
    }

    /// <inheritdoc />
    public void RequestSync()
    {
        _requests.Writer.TryWrite(true);
    }

    /// <summary>Stops the loop.</summary>
    public async ValueTask DisposeAsync()
    {
        _connectivity.Changed -= OnConnectivityChanged;
        await _stopping.CancelAsync();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stopping.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        // Yield first, so Start() returns before any IndexedDB work begins.
        await Task.Yield();

        try
        {
            await _syncService.RecoverAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Recovering in-flight outbox entries failed.");
        }

        RequestSync();
        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForRequestOrIntervalAsync(stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task WaitForRequestOrIntervalAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task request = _requests.Reader.WaitToReadAsync(waitCancellation.Token).AsTask();
        Task interval = Task.Delay(NextWait(), _timeProvider, waitCancellation.Token);

        await Task.WhenAny(request, interval);
        await waitCancellation.CancelAsync();
        _requests.Reader.TryRead(out _);
        stoppingToken.ThrowIfCancellationRequested();
    }

    // Wake for the earliest retry that falls due before the next regular tick, so a 2-second backoff really is
    // 2 seconds rather than "whenever the 30-second timer fires".
    private TimeSpan NextWait()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset? nextRetry = Outbox
            .Where(entry => entry.Status == OutboxStatus.Pending && entry.NextAttemptAt is not null)
            .Min(entry => entry.NextAttemptAt);

        if (nextRetry is not { } due)
        {
            return Interval;
        }

        TimeSpan untilDue = due - now;
        return untilDue < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : untilDue > Interval ? Interval : untilDue;
    }

    // Back online (or "Simulate offline" switched off): send whatever queued up meanwhile, straight away.
    private void OnConnectivityChanged()
    {
        bool online = _connectivity.IsOnline;
        if (online && !_wasOnline)
        {
            RequestSync();
        }

        _wasOnline = online;
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        // Offline for certain (the browser says so, or the user is simulating it): don't send anything. "Unreachable"
        // still runs, because only a real request can tell when the server is back.
        PausedOffline = _connectivity.Status is ConnectivityStatus.Offline or ConnectivityStatus.SimulatedOffline;

        IsRunning = !PausedOffline;
        Changed?.Invoke();
        try
        {
            if (!PausedOffline)
            {
                LastResult = await _syncService.RunAsync(stoppingToken);
                LastError = null;
                RunCompleted?.Invoke(LastResult);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Something unexpected, e.g. IndexedDB unavailable in a private window. Keep the loop alive: the next run
            // might work, and a dead loop would silently stop all syncing.
            LastError = ex;
            _logger.LogError(ex, "Sync run failed.");
        }
        finally
        {
            IsRunning = false;
        }

        try
        {
            Outbox = await _syncService.GetOutboxAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reading the outbox failed.");
        }

        LastRunAt = _timeProvider.GetUtcNow();
        Changed?.Invoke();
    }
}
