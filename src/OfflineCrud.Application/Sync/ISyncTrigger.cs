namespace OfflineCrud.Application.Sync;

/// <summary>Asks for a sync run soon, without waiting for it.</summary>
/// <remarks>Implemented by the client's scheduler. Several requests in quick succession become one run.</remarks>
public interface ISyncTrigger
{
    /// <summary>Requests a sync run. Returns immediately.</summary>
    void RequestSync();
}
