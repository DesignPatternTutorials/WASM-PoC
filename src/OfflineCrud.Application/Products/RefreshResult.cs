using OfflineCrud.Application.Common;

namespace OfflineCrud.Application.Products;

/// <summary>What happened when the app tried to refresh local data from the server.</summary>
public enum RefreshStatus
{
    /// <summary>The server answered and its data was merged in.</summary>
    Refreshed,

    /// <summary>The local data was refreshed recently enough that no request was made.</summary>
    AlreadyFresh,

    /// <summary>The server doesn't have this product, e.g. because DummyJSON didn't persist it when it was created.</summary>
    NotOnServer,

    /// <summary>The request failed. The local data is unchanged and still usable.</summary>
    Failed,
}

/// <summary>The outcome of a refresh. Refreshing never throws <see cref="ApiException"/>; failures are reported here.</summary>
/// <param name="Status">What happened.</param>
/// <param name="ChangedCount">How many local products were added or updated.</param>
/// <param name="Error">The failure, when <paramref name="Status"/> is <see cref="RefreshStatus.Failed"/>.</param>
public sealed record RefreshResult(RefreshStatus Status, int ChangedCount = 0, ApiException? Error = null)
{
    /// <summary>A refresh that didn't need to run.</summary>
    public static RefreshResult AlreadyFresh { get; } = new(RefreshStatus.AlreadyFresh);

    /// <summary>A refresh for a product the server doesn't have.</summary>
    public static RefreshResult NotOnServer { get; } = new(RefreshStatus.NotOnServer);

    /// <summary>Creates a successful result.</summary>
    public static RefreshResult Refreshed(int changedCount) => new(RefreshStatus.Refreshed, changedCount);

    /// <summary>Creates a failed result.</summary>
    public static RefreshResult Failed(ApiException error) => new(RefreshStatus.Failed, Error: error);
}
