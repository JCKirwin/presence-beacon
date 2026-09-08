namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// Storage for beacon records, with no opinion about what "live" means.
/// </summary>
/// <remarks>
/// Splitting storage from the liveness rule is what lets the unit tests run against an
/// in-memory store while the demo runs against a directory on disk. Implementations own
/// two guarantees: a replace is atomic from a reader's point of view, and a write only
/// ever touches the calling participant's own record.
/// </remarks>
public interface IBeaconStore
{
    /// <summary>
    /// Replaces one participant's stored beacon, creating the backing directory if needed.
    /// </summary>
    /// <param name="beacon">The record to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveAsync(Beacon beacon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every stored beacon. A missing directory yields an empty sequence rather than
    /// an error, because "nobody has checked in yet" is a normal state.
    /// </summary>
    /// <param name="cancellationToken">Cancels enumeration.</param>
    /// <returns>Every beacon currently stored, in no guaranteed order.</returns>
    Task<IReadOnlyList<Beacon>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one participant's beacon.
    /// </summary>
    /// <param name="node">The participant to look up.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored beacon, or <see langword="null"/> when none exists.</returns>
    Task<Beacon?> LoadAsync(NodeId node, CancellationToken cancellationToken = default);
}
