using Microsoft.JSInterop;

namespace OfflineCrud.Infrastructure.IndexedDb;

/// <summary>
/// The <c>indexedDb.js</c> module, imported once and shared by the product repository and the outbox store.
/// </summary>
internal sealed class IndexedDbModule : IAsyncDisposable
{
    // Files in a Razor class library's wwwroot are served under _content/{assembly name}/.
    private const string ModulePath = "./_content/OfflineCrud.Infrastructure/js/indexedDb.js";

    private readonly IJSRuntime _jsRuntime;

    // LEARN: The module is imported on first use, not at start-up, and only once. `import()` returns a reference to the
    // module object living in JS. .NET holds it as an IJSObjectReference and calls its exported functions by name.
    // A Lazy<Task> would also cache a *failed* import forever, so one transient failure (a 404 right after a rebuild,
    // say) would break all storage until the tab was reloaded. A failed import is retried on the next call instead.
    private Task<IJSObjectReference>? _module;

    public IndexedDbModule(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    // LEARN: The CancellationToken only stops .NET waiting. The JS function keeps running, and an IndexedDB
    // transaction that has started will still commit. Cancellation across the interop boundary is cooperative at best.
    public async Task<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        IJSObjectReference module = await GetModuleAsync();
        return await module.InvokeAsync<T>(identifier, cancellationToken, args);
    }

    public async Task InvokeVoidAsync(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        IJSObjectReference module = await GetModuleAsync();
        await module.InvokeVoidAsync(identifier, cancellationToken, args);
    }

    public async ValueTask DisposeAsync()
    {
        // LEARN: An IJSObjectReference keeps the JS object alive until it's disposed. Disposing tells the JS side it
        // can release its handle. (In Blazor Server this can throw JSDisconnectedException if the circuit has gone;
        // in WebAssembly the JS runtime is always in the same tab.)
        if (_module is { IsCompletedSuccessfully: true })
        {
            await _module.Result.DisposeAsync();
        }
    }

    private Task<IJSObjectReference> GetModuleAsync()
    {
        // Single-threaded, so no lock is needed: nothing can run between the check and the assignment.
        if (_module is null || _module.IsFaulted || _module.IsCanceled)
        {
            _module = _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
        }

        return _module;
    }
}
