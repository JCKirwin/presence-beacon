namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// What a reader can say about one participant at a point in time.
/// </summary>
public enum BeaconStatus
{
    /// <summary>No beacon file exists for this participant.</summary>
    Missing = 0,

    /// <summary>A beacon exists but its last check-in is older than its time-to-live.</summary>
    Stale = 1,

    /// <summary>A beacon exists and its last check-in is within its time-to-live.</summary>
    Live = 2,
}
