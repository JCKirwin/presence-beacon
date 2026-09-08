namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The read half of the pattern: turning stored beacons into a live-or-not verdict.
/// </summary>
/// <remarks>
/// Readers never write and never clean up. A stale beacon is left exactly where it is so
/// that the participant can revive itself simply by checking in again.
/// </remarks>
public interface IBeaconReader
{
    /// <summary>
    /// Reads the whole beacon directory and classifies every participant found.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One immutable view of who was live at the moment of the read.</returns>
    Task<PresenceSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks about one participant without materializing the whole directory.
    /// </summary>
    /// <param name="node">The participant to check.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The participant's status at the moment of the read.</returns>
    Task<BeaconStatus> StatusOfAsync(NodeId node, CancellationToken cancellationToken = default);
}
