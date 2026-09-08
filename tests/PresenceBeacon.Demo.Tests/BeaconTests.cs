namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// Invariant: a beacon is live if and only if <c>now - lastSeen &lt;= ttl</c>.
/// </summary>
/// <remarks>
/// Everything else in the pattern is plumbing around this one comparison, so it is tested
/// at the boundary tick rather than with comfortable margins.
/// </remarks>
public sealed class BeaconTests
{
    private static readonly DateTimeOffset Start = MutableClock.AtStart().UtcNow;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Fact]
    public void FreshCheckIn_IsLive()
    {
        var beacon = BeaconAt(Start);

        Assert.True(beacon.IsLiveAt(Start));
    }

    [Fact]
    public void OneTickBeforeExpiry_IsLive()
    {
        var beacon = BeaconAt(Start);

        Assert.True(beacon.IsLiveAt(Start + Ttl - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void ExactlyAtExpiry_IsStillLive()
    {
        // The rule is "<=", not "<". Pinning the boundary keeps a later refactor from
        // silently shortening every participant's grace period by one tick.
        var beacon = BeaconAt(Start);

        Assert.True(beacon.IsLiveAt(Start + Ttl));
    }

    [Fact]
    public void OneTickAfterExpiry_IsNotLive()
    {
        var beacon = BeaconAt(Start);

        Assert.False(beacon.IsLiveAt(Start + Ttl + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void LongAfterExpiry_IsNotLive()
    {
        var beacon = BeaconAt(Start);

        Assert.False(beacon.IsLiveAt(Start + TimeSpan.FromHours(4)));
    }

    [Fact]
    public void CheckInStampedInTheFuture_IsLive()
    {
        // Two machines whose clocks disagree produce a negative age. Skew must read as
        // fresh; reporting it as expiry would evict a participant that is plainly working.
        var beacon = BeaconAt(Start + TimeSpan.FromMinutes(5));

        Assert.True(beacon.IsLiveAt(Start));
    }

    [Fact]
    public void EachBeaconCarriesItsOwnTtl()
    {
        // A reader honors the writer's stated grace period, so two beacons stamped at the
        // same instant can disagree about liveness without reconfiguring anyone.
        var patient = new Beacon
        {
            Node = NodeId.Parse("ridge-01"),
            LastSeen = Start,
            Ttl = TimeSpan.FromMinutes(10),
        };

        var impatient = BeaconAt(Start);
        var now = Start + TimeSpan.FromMinutes(1);

        Assert.True(patient.IsLiveAt(now));
        Assert.False(impatient.IsLiveAt(now));
    }

    [Fact]
    public void TaskHintIsOptionalAndDoesNotAffectLiveness()
    {
        var withHint = BeaconAt(Start) with { TaskHint = "sampled 12.4C" };

        Assert.Equal("sampled 12.4C", withHint.TaskHint);
        Assert.True(withHint.IsLiveAt(Start + Ttl));
        Assert.False(withHint.IsLiveAt(Start + Ttl + TimeSpan.FromTicks(1)));
    }

    private static Beacon BeaconAt(DateTimeOffset lastSeen) => new()
    {
        Node = NodeId.Parse("ridge-01"),
        LastSeen = lastSeen,
        Ttl = Ttl,
    };
}
