namespace PresenceBeacon.Demo.Tests.Fakes;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// A dictionary standing in for the beacon directory, keyed by node id.
/// </summary>
/// <remarks>
/// Keying by id mirrors the one-file-per-node rule of the real store, so a writer that
/// tried to stamp under a neighbor's name would show up here as a changed entry. The
/// cancellation token is deliberately ignored: tests that want a cancelled write use the
/// file store, and ignoring it here keeps loop tests deterministic.
/// </remarks>
internal sealed class InMemoryBeaconStore : IBeaconStore
{
    private readonly Dictionary<string, Beacon> _beacons = new(StringComparer.Ordinal);

    public int SaveCount { get; private set; }

    /// <summary>Every id that currently has a stored beacon, in sorted order.</summary>
    public IReadOnlyList<string> Ids =>
        _beacons.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public void Seed(Beacon beacon) => _beacons[beacon.Node.Value] = beacon;

    public Task SaveAsync(Beacon beacon, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beacon);

        _beacons[beacon.Node.Value] = beacon;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Beacon>> LoadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Beacon>>(_beacons.Values.ToArray());

    public Task<Beacon?> LoadAsync(NodeId node, CancellationToken cancellationToken = default) =>
        Task.FromResult(_beacons.TryGetValue(node.Value, out var beacon) ? beacon : null);
}
