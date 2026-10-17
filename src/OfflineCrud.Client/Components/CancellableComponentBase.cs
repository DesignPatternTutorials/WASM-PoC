using Microsoft.AspNetCore.Components;

namespace OfflineCrud.Client.Components;

/// <summary>
/// Base class for components that make async calls which should stop when the user navigates away.
/// </summary>
public abstract class CancellableComponentBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _disposalSource;

    // Captured once, because reading Token from a disposed CancellationTokenSource throws.
    private readonly CancellationToken _componentToken;

    /// <summary>Initialises the cancellation source.</summary>
    protected CancellableComponentBase()
    {
        _disposalSource = new CancellationTokenSource();
        _componentToken = _disposalSource.Token;
    }

    /// <summary>Cancelled when the component is removed from the page.</summary>
    /// <remarks>
    /// LEARN: Navigating away doesn't stop a component's in-flight HttpClient call by itself. Passing this token
    /// cancels the browser's fetch() through its AbortController, so the response doesn't arrive at, and try to
    /// re-render, a page that no longer exists.
    /// </remarks>
    protected CancellationToken ComponentToken => _componentToken;

    /// <summary>Cancels outstanding work.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cancels outstanding work. Override to release more resources, calling the base method.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposalSource.Cancel();
            _disposalSource.Dispose();
        }
    }
}
