namespace PresenceBeacon.Demo.Presence;

/// <summary>
/// The production clock: real UTC time, no adjustment.
/// </summary>
/// <remarks>
/// Tests substitute their own <see cref="IClock"/> so they can jump past a time-to-live
/// instead of waiting for one.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static SystemClock Instance { get; } = new SystemClock();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
