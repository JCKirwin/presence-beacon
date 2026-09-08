namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The answer to "who is live right now?" — one immutable reading of the beacon
/// directory, taken at <see cref="ObservedAt"/>.
/// </summary>
/// <remarks>
/// A snapshot is advisory. It describes the directory as it looked when it was read, and
/// a participant may write or stop writing a moment later.
/// </remarks>
public sealed record PresenceSnapshot
{
    /// <summary>The instant the directory was read.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Every participant found, live and stale alike, in no guaranteed order.</summary>
    public required IReadOnlyList<NodePresence> Nodes { get; init; }

    /// <summary>The participants whose beacons were within their time-to-live.</summary>
    public IReadOnlyList<NodePresence> Live => WithStatus(BeaconStatus.Live);

    /// <summary>The participants whose beacons had aged out.</summary>
    public IReadOnlyList<NodePresence> Stale => WithStatus(BeaconStatus.Stale);

    /// <summary>
    /// Reports the status of one participant, including participants absent from
    /// <see cref="Nodes"/>.
    /// </summary>
    /// <param name="node">The participant to look up.</param>
    /// <returns>
    /// The recorded status, or <see cref="BeaconStatus.Missing"/> when no beacon was found.
    /// </returns>
    public BeaconStatus StatusOf(NodeId node)
    {
        foreach (var entry in Nodes)
        {
            if (entry.Node.Equals(node))
            {
                return entry.Status;
            }
        }

        // "Never checked in" and "checked in, then went quiet" are different facts. A
        // coordinator that folded them together could not tell a crash from a cold start.
        return BeaconStatus.Missing;
    }

    private IReadOnlyList<NodePresence> WithStatus(BeaconStatus status) =>
        Nodes.Where(entry => entry.Status == status).ToArray();
}
