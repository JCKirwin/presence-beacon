namespace PresenceBeacon.Demo.Tests.Fakes;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// A clock that jumps forward on every read and counts how many times it was asked.
/// </summary>
/// <remarks>
/// This exists to catch one specific bug: a reader that samples "now" per beacon rather
/// than once per snapshot. Under this clock, that bug classifies two identical beacons
/// differently.
/// </remarks>
internal sealed class TickingClock : IClock
{
    private readonly TimeSpan _step;
    private DateTimeOffset _current;

    public TickingClock(DateTimeOffset start, TimeSpan step)
    {
        _current = start;
        _step = step;
    }

    public int Reads { get; private set; }

    public DateTimeOffset UtcNow
    {
        get
        {
            Reads++;
            var value = _current;
            _current += _step;
            return value;
        }
    }
}
