namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// A single participant paired with the verdict a reader reached about it.
/// </summary>
/// <remarks>
/// <see cref="Beacon"/> is <see langword="null"/> when <see cref="Status"/> is
/// <see cref="BeaconStatus.Missing"/>, and present otherwise.
/// </remarks>
public sealed record NodePresence
{
    /// <summary>The participant this verdict is about.</summary>
    public required NodeId Node { get; init; }

    /// <summary>The verdict.</summary>
    public required BeaconStatus Status { get; init; }

    /// <summary>The beacon the verdict was derived from, when one was found.</summary>
    public Beacon? Beacon { get; init; }
}
