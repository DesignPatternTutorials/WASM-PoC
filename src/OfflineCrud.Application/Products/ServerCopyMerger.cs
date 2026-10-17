using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Decides how products fetched from the server combine with what this device already has.
/// </summary>
/// <remarks>
/// A pure function, so the rules can be unit tested without IndexedDB. The caller reads the existing records,
/// calls <see cref="Merge"/>, and writes back only <see cref="ServerCopyMergeResult.Changed"/>.
/// </remarks>
public static class ServerCopyMerger
{
    /// <summary>Merges server copies into the matching local products.</summary>
    /// <param name="existing">Local products (including tombstones) whose server ids appear in <paramref name="fromServer"/>.</param>
    /// <param name="fromServer">What the server returned.</param>
    /// <param name="newId">Creates a local id for a product this device hasn't seen before.</param>
    /// <returns>The products to save, and the local product for every server id.</returns>
    public static ServerCopyMergeResult Merge(
        IEnumerable<Product> existing,
        IEnumerable<ServerProduct> fromServer,
        Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(fromServer);
        ArgumentNullException.ThrowIfNull(newId);

        // Several local products can share a server id, because DummyJSON gives every created product the same one.
        // The server only ever returns real products, so the first local match is the one that came from it.
        Dictionary<int, Product> byServerId = existing
            .Where(product => product.ServerId is not null)
            .GroupBy(product => product.ServerId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        List<Product> changed = [];
        foreach (ServerProduct server in fromServer)
        {
            if (!byServerId.TryGetValue(server.ServerId, out Product? local))
            {
                local = Product.FromServer(newId(), server.ServerId, server.Details, server.Rating, server.ThumbnailUrl, server.Version);
                byServerId[server.ServerId] = local;
                changed.Add(local);
            }
            else if (local.ShouldAcceptServerCopy(server.Version))
            {
                local.ApplyServerCopy(server.ServerId, server.Details, server.Rating, server.ThumbnailUrl, server.Version);
                changed.Add(local);
            }
        }

        return new ServerCopyMergeResult(changed, byServerId);
    }
}

/// <summary>The outcome of <see cref="ServerCopyMerger.Merge"/>.</summary>
/// <param name="Changed">New or updated products that need saving.</param>
/// <param name="ByServerId">The local product (possibly a tombstone) for every server id that was merged.</param>
public sealed record ServerCopyMergeResult(IReadOnlyList<Product> Changed, IReadOnlyDictionary<int, Product> ByServerId);
