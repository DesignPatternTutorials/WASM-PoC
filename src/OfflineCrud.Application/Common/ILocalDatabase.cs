namespace OfflineCrud.Application.Common;

/// <summary>Port for operations on the whole local database rather than one store.</summary>
public interface ILocalDatabase
{
    /// <summary>Deletes every product, outbox entry and metadata value stored on this device.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
