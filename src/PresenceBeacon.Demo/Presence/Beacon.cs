namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// One heartbeat record: who wrote it, when they last checked in, how long that
/// check-in should be trusted, and an optional note about what they were doing.
/// </summary>
/// <remarks>
/// This is the entire on-disk payload. It carries its own time-to-live so a reader can
/// judge liveness without knowing how the writer was configured.
/// </remarks>
public sealed record Beacon
{
    /// <summary>The participant that wrote this beacon.</summary>
    public required NodeId Node { get; init; }

    /// <summary>UTC instant of the last check-in.</summary>
    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>How long after <see cref="LastSeen"/> this beacon still counts as live.</summary>
    public required TimeSpan Ttl { get; init; }

    /// <summary>Free-text hint about the work in flight, for example <c>sampling ridge sensor</c>.</summary>
    public string? TaskHint { get; init; }

    /// <summary>
    /// Decides whether this beacon is still within its time-to-live at the given instant.
    /// </summary>
    /// <param name="now">The instant to judge against, normally the reader's clock.</param>
    /// <returns><see langword="true"/> when <c>now - LastSeen</c> is within <see cref="Ttl"/>.</returns>
    /// <remarks>
    /// The comparison is inclusive, so a check-in exactly at its deadline still counts. It
    /// also reads a negative age as fresh: when two machines disagree about the clock, a
    /// beacon stamped slightly in the future belongs to a participant that is plainly
    /// working, and expiring it would be the wrong answer.
    /// </remarks>
    public bool IsLiveAt(DateTimeOffset now) => now - LastSeen <= Ttl;
}
