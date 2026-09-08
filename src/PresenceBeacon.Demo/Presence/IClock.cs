namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The one source of "now" in this pattern.
/// </summary>
/// <remarks>
/// Liveness is entirely a statement about elapsed time, so tests need to move the clock
/// instead of sleeping. Every type that compares timestamps takes this seam.
/// </remarks>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
