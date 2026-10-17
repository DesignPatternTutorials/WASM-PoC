namespace OfflineCrud.Domain.Products;

/// <summary>
/// A product as this device knows it: a local identity, its editable <see cref="ProductDetails"/>, and the
/// bookkeeping needed to keep it in step with the server.
/// </summary>
/// <remarks>
/// <para>
/// The identity is a locally generated <see cref="Guid"/>, not the server's id. A product can exist here before
/// the server has assigned an id, and DummyJSON gives every created product the same id anyway
/// (see <c>docs/api-notes.md</c>). <see cref="ServerId"/> records the server's id when there is one.
/// </para>
/// <para>
/// Deleting marks the product as a tombstone (<see cref="IsDeleted"/>) instead of removing it. The record then
/// remembers that this device deleted it, so a later refresh from the server doesn't bring it back.
/// </para>
/// </remarks>
public sealed class Product
{
    private Product(
        Guid id,
        ProductDetails details,
        double? rating,
        Uri? thumbnailUrl,
        int? serverId,
        string? serverVersion,
        SyncState syncState,
        bool isDeleted)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A product needs a non-empty id.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(details);
        if (serverId is int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(serverId));
        }

        Id = id;
        Details = details;
        Rating = rating;
        ThumbnailUrl = thumbnailUrl;
        ServerId = serverId;
        ServerVersion = serverVersion;
        SyncState = syncState;
        IsDeleted = isDeleted;
    }

    /// <summary>The local identity. Stable for the lifetime of the record on this device.</summary>
    public Guid Id { get; }

    /// <summary>The editable fields.</summary>
    public ProductDetails Details { get; private set; }

    /// <summary>Average review rating from 0 to 5, or <see langword="null"/> if unknown.</summary>
    public double? Rating { get; private set; }

    /// <summary>Thumbnail image URL, or <see langword="null"/> if none.</summary>
    public Uri? ThumbnailUrl { get; private set; }

    /// <summary>The server's id, or <see langword="null"/> if the server hasn't assigned one.</summary>
    public int? ServerId { get; private set; }

    /// <summary>
    /// An opaque token identifying the server version this copy is based on, or <see langword="null"/> if unknown.
    /// Only ever compared for equality, like an HTTP ETag.
    /// </summary>
    public string? ServerVersion { get; private set; }

    /// <summary>Where this copy stands relative to the server.</summary>
    public SyncState SyncState { get; private set; }

    /// <summary>Whether this device has deleted the product. Deleted products are hidden but kept as tombstones.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>The price after applying <see cref="ProductDetails.DiscountPercentage"/>, rounded to two decimal places.</summary>
    public decimal DiscountedPrice =>
        Math.Round(Details.Price * (1m - (Details.DiscountPercentage / 100m)), 2, MidpointRounding.AwayFromZero);

    /// <summary>Whether at least one unit is in stock.</summary>
    public bool IsInStock => Details.Stock > 0;

    /// <summary>Creates a product on this device. The server doesn't know about it until the outbox sends it.</summary>
    public static Product CreateLocal(Guid id, ProductDetails details)
    {
        return new Product(id, details, rating: null, thumbnailUrl: null, serverId: null, serverVersion: null, SyncState.PendingCreate, isDeleted: false);
    }

    /// <summary>Creates a local copy of a product the server returned.</summary>
    public static Product FromServer(
        Guid id,
        int serverId,
        ProductDetails details,
        double? rating,
        Uri? thumbnailUrl,
        string? serverVersion)
    {
        return new Product(id, details, rating, thumbnailUrl, serverId, serverVersion, SyncState.Synced, isDeleted: false);
    }

    /// <summary>Recreates a product exactly as it was stored. Used when loading from local storage.</summary>
    public static Product Restore(
        Guid id,
        ProductDetails details,
        double? rating,
        Uri? thumbnailUrl,
        int? serverId,
        string? serverVersion,
        SyncState syncState,
        bool isDeleted)
    {
        return new Product(id, details, rating, thumbnailUrl, serverId, serverVersion, syncState, isDeleted);
    }

    /// <summary>
    /// Whether a copy of this product just fetched from the server should replace this one.
    /// </summary>
    /// <param name="serverVersion">The version token of the fetched copy, or <see langword="null"/> if it has none.</param>
    /// <returns>
    /// <see langword="false"/> if this device has a change the server hasn't confirmed, if the product is a local
    /// tombstone, or if the server's version is the one this copy is already based on. Otherwise <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// Comparing versions is what lets local changes survive a refresh: the server hasn't produced a new version since
    /// this copy was taken, so there's nothing new to apply. With no version on either side there's nothing to compare,
    /// so the server's copy wins.
    /// </remarks>
    public bool ShouldAcceptServerCopy(string? serverVersion)
    {
        if (IsDeleted || SyncState != SyncState.Synced)
        {
            return false;
        }

        return serverVersion is null || ServerVersion is null || !string.Equals(serverVersion, ServerVersion, StringComparison.Ordinal);
    }

    /// <summary>Replaces this copy with values the server returned, leaving it <see cref="SyncState.Synced"/>.</summary>
    public void ApplyServerCopy(int serverId, ProductDetails details, double? rating, Uri? thumbnailUrl, string? serverVersion)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serverId);

        ServerId = serverId;
        Details = details;
        Rating = rating;
        ThumbnailUrl = thumbnailUrl;
        ServerVersion = serverVersion;
        SyncState = SyncState.Synced;
    }

    /// <summary>Applies an edit made on this device. <see cref="SyncState"/> is set separately, from the outbox.</summary>
    /// <exception cref="InvalidOperationException">The product has been deleted.</exception>
    public void ApplyLocalEdit(ProductDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted product can't be edited.");
        }

        Details = details;
    }

    /// <summary>Marks the product as deleted on this device. It is kept as a tombstone and hidden from lists.</summary>
    public void ApplyLocalDelete()
    {
        IsDeleted = true;
    }

    /// <summary>Reverses <see cref="ApplyLocalDelete"/>, e.g. when the user discards a delete the server rejected.</summary>
    public void UndoLocalDelete()
    {
        IsDeleted = false;
    }

    /// <summary>
    /// Forgets which server version this copy is based on, so the next copy fetched from the server replaces it
    /// (see <see cref="ShouldAcceptServerCopy"/>). Used to throw away a local change in favour of the server's version.
    /// </summary>
    public void ForgetServerVersion()
    {
        ServerVersion = null;
    }

    /// <summary>Records the id the server assigned when it accepted this product's creation.</summary>
    public void RecordServerId(int serverId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serverId);
        ServerId = serverId;
    }

    /// <summary>
    /// Sets where this product stands relative to the server. The outbox decides this: a product is
    /// <see cref="SyncState.Synced"/> exactly when no outbox entries remain for it.
    /// </summary>
    public void SetSyncState(SyncState syncState)
    {
        SyncState = syncState;
    }
}
