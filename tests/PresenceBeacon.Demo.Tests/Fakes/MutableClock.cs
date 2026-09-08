namespace PresenceBeacon.Demo.Tests.Fakes;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// A clock the test drives by hand. Liveness is a statement about elapsed time, so every
/// expiry test moves this forward instead of sleeping.
/// </summary>
internal sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }

    /// <summary>A fixed, arbitrary starting instant so failures print stable timestamps.</summary>
    public static MutableClock AtStart() =>
        new(new DateTimeOffset(2031, 3, 14, 9, 0, 0, TimeSpan.Zero));

    public void Advance(TimeSpan by) => UtcNow += by;
}
