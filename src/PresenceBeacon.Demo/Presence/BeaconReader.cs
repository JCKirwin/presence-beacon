namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// Default <see cref="IBeaconReader"/>: loads what the store has and applies each beacon's
/// own time-to-live against the current clock.
/// </summary>
/// <remarks>
/// The verdict comes from the beacon, not from this reader's configuration. A participant
/// that wants a longer grace period says so in its own record, and readers honor it
/// without being reconfigured.
/// </remarks>
public sealed class BeaconReader : IBeaconReader
{
    private readonly IBeaconStore _store;
    private readonly IClock _clock;

    /// <summary>
    /// Creates a reader over a store.
    /// </summary>
    /// <param name="store">Where beacons are stored.</param>
    /// <param name="clock">Source of the instant each beacon is judged against.</param>
    public BeaconReader(IBeaconStore store, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PresenceSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        // Read the clock once. Classifying each beacon against a slightly different "now"
        // would let two participants with identical timestamps disagree.
        var now = _clock.UtcNow;
        var beacons = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        var nodes = new List<NodePresence>(beacons.Count);
        foreach (var beacon in beacons)
        {
            nodes.Add(new NodePresence
            {
                Node = beacon.Node,
                Status = beacon.IsLiveAt(now) ? BeaconStatus.Live : BeaconStatus.Stale,
                Beacon = beacon,
            });
        }

        return new PresenceSnapshot { ObservedAt = now, Nodes = nodes };
    }

    /// <inheritdoc />
    public async Task<BeaconStatus> StatusOfAsync(NodeId node, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var beacon = await _store.LoadAsync(node, cancellationToken).ConfigureAwait(false);

        if (beacon is null)
        {
            return BeaconStatus.Missing;
        }

        return beacon.IsLiveAt(now) ? BeaconStatus.Live : BeaconStatus.Stale;
    }
}
