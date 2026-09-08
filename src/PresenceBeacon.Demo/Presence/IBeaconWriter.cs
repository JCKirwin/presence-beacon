namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The write half of the pattern: one participant announcing that it is still working.
/// </summary>
/// <remarks>
/// A writer only ever touches its own record. It never deletes or rewrites another
/// participant's beacon, which is what keeps concurrent participants from interfering.
/// </remarks>
public interface IBeaconWriter
{
    /// <summary>The participant this writer speaks for.</summary>
    NodeId Node { get; }

    /// <summary>
    /// Stamps a fresh check-in for <see cref="Node"/>.
    /// </summary>
    /// <param name="taskHint">Optional note about the work in flight.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The beacon that was stored.</returns>
    Task<Beacon> HeartbeatAsync(string? taskHint = null, CancellationToken cancellationToken = default);
}
