namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// Default <see cref="IBeaconWriter"/>: stamps the clock, attaches the configured
/// time-to-live, and hands the record to the store.
/// </summary>
/// <remarks>
/// The writer holds no timer of its own. Whoever owns the work loop decides when to call
/// <see cref="HeartbeatAsync(string?, CancellationToken)"/>, which keeps the heartbeat
/// honest -- it fires only while the participant is actually running.
/// </remarks>
public sealed class BeaconWriter : IBeaconWriter
{
    private readonly IBeaconStore _store;
    private readonly IClock _clock;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Creates a writer bound to one participant.
    /// </summary>
    /// <param name="node">The participant this writer speaks for.</param>
    /// <param name="store">Where beacons are stored.</param>
    /// <param name="options">Timing settings, including the time-to-live to stamp.</param>
    /// <param name="clock">Source of the check-in timestamp.</param>
    public BeaconWriter(NodeId node, IBeaconStore store, BeaconOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();

        if (node.Value.Length == 0)
        {
            throw new ArgumentException("A writer needs an id built through NodeId.Parse.", nameof(node));
        }

        // The id is captured once, here. No later call site can stamp a heartbeat under a
        // neighbor's name, because no method takes an id.
        Node = node;
        _store = store;
        _clock = clock;
        _ttl = options.Ttl;
    }

    /// <inheritdoc />
    public NodeId Node { get; }

    /// <inheritdoc />
    public async Task<Beacon> HeartbeatAsync(string? taskHint = null, CancellationToken cancellationToken = default)
    {
        var beacon = new Beacon
        {
            Node = Node,
            LastSeen = _clock.UtcNow,
            Ttl = _ttl,
            TaskHint = string.IsNullOrWhiteSpace(taskHint) ? null : taskHint.Trim(),
        };

        await _store.SaveAsync(beacon, cancellationToken).ConfigureAwait(false);
        return beacon;
    }
}
