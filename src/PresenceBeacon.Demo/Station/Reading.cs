namespace PresenceBeacon.Demo.Station;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// One invented temperature sample from one sensor node.
/// </summary>
/// <remarks>
/// Readings are the work the demo pretends to do. They are never written to the beacon
/// directory -- the beacon says a node is alive, not what it measured.
/// </remarks>
public sealed record Reading
{
    /// <summary>The node that took the sample.</summary>
    public required NodeId Node { get; init; }

    /// <summary>Temperature in degrees Celsius.</summary>
    public required double Celsius { get; init; }

    /// <summary>UTC instant the sample was taken.</summary>
    public required DateTimeOffset TakenAt { get; init; }
}
