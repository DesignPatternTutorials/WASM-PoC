namespace OfflineCrud.Client.Services;

/// <summary>The look of a toast.</summary>
public enum ToastKind
{
    /// <summary>Neutral information.</summary>
    Info,

    /// <summary>Something worked.</summary>
    Success,

    /// <summary>Something needs attention but nothing is lost.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>A short-lived notification.</summary>
/// <param name="Id">Identifies the toast for dismissal.</param>
/// <param name="Message">The text to show.</param>
/// <param name="Kind">The look.</param>
public sealed record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>Holds the toasts currently on screen. Shown by the <c>ToastHost</c> component in the layout.</summary>
public sealed class ToastService
{
    /// <summary>How long a toast stays before it disappears by itself.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private const int MaxVisible = 4;

    private readonly TimeProvider _timeProvider;
    private readonly List<Toast> _toasts = [];

    /// <summary>Creates the service.</summary>
    public ToastService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Raised when a toast is added or removed.</summary>
    public event Action? Changed;

    /// <summary>The toasts on screen, oldest first.</summary>
    public IReadOnlyList<Toast> Toasts => _toasts;

    /// <summary>Shows a toast that dismisses itself after <see cref="Lifetime"/>.</summary>
    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        Toast toast = new(Guid.NewGuid(), message, kind);
        _toasts.Add(toast);
        if (_toasts.Count > MaxVisible)
        {
            _toasts.RemoveAt(0);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id);
    }

    /// <summary>Removes a toast now.</summary>
    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(toast => toast.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLaterAsync(Guid id)
    {
        await Task.Delay(Lifetime, _timeProvider);
        Dismiss(id);
    }
}
