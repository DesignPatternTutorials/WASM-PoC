namespace OfflineCrud.Application.Common;

/// <summary>
/// Serialises read-modify-write sequences against local storage, so two of them can't interleave at an <c>await</c>.
/// </summary>
/// <remarks>
/// LEARN: WebAssembly runs .NET on a single thread, but that doesn't make read-modify-write safe. Every <c>await</c> lets
/// other work run. A background refresh or sync could read a product, a user save could then change it, and the first
/// sequence would write back its stale copy. Every sequence that reads, decides and writes takes this lock. It is shared
/// by <c>ProductService</c> and <c>SyncService</c>, and only covers this tab: two tabs share one IndexedDB database but
/// not this lock.
/// </remarks>
public sealed class LocalStoreLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for the lock. Dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    /// <summary>Releases the underlying semaphore.</summary>
    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
