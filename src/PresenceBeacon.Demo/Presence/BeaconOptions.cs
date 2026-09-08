namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The three knobs that decide where beacons live and how long they are trusted.
/// </summary>
/// <remarks>
/// Keep <see cref="HeartbeatInterval"/> comfortably shorter than <see cref="Ttl"/>. If they
/// are close, one slow write turns a healthy participant stale.
/// </remarks>
public sealed record BeaconOptions
{
    /// <summary>Directory that holds one beacon file per participant.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>How long a beacon stays live after its last check-in.</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often a writer is expected to refresh its beacon.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Throws when the options cannot produce sane liveness answers — a blank directory, a
    /// non-positive interval, or a heartbeat that is not shorter than the time-to-live.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
        {
            throw new InvalidOperationException(
                "A beacon directory path is required. Without one there is nowhere to publish and nothing to read.");
        }

        if (Ttl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Ttl must be positive, but was {Ttl}. A beacon that expires on arrival is never live, so nobody would ever be seen.");
        }

        if (HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"HeartbeatInterval must be positive, but was {HeartbeatInterval}. A participant has to check in on some cadence.");
        }

        if (HeartbeatInterval >= Ttl)
        {
            // Caught here rather than at the first heartbeat, because the symptom -- a
            // participant that flickers between live and stale while working normally --
            // looks like a bug anywhere except in the configuration that caused it.
            throw new InvalidOperationException(
                $"HeartbeatInterval ({HeartbeatInterval}) must be shorter than Ttl ({Ttl}), otherwise a healthy participant is stale between every pair of check-ins.");
        }
    }
}
