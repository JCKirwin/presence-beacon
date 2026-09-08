namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// Configuration that cannot produce sane liveness answers is rejected up front, at
/// construction, rather than at the first heartbeat.
/// </summary>
public sealed class BeaconOptionsTests
{
    [Fact]
    public void DefaultsLeaveRoomForSeveralMissedCheckIns()
    {
        var options = new BeaconOptions { DirectoryPath = "beacons" };

        options.Validate();

        Assert.True(options.HeartbeatInterval < options.Ttl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankDirectory(string directory)
    {
        var options = new BeaconOptions { DirectoryPath = directory };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void RejectsNonPositiveTtl()
    {
        // A beacon that expires on arrival is never live, so nobody would ever be seen.
        var zero = new BeaconOptions { DirectoryPath = "beacons", Ttl = TimeSpan.Zero, HeartbeatInterval = TimeSpan.FromSeconds(1) };
        var negative = zero with { Ttl = TimeSpan.FromSeconds(-1) };

        Assert.Throws<InvalidOperationException>(zero.Validate);
        Assert.Throws<InvalidOperationException>(negative.Validate);
    }

    [Fact]
    public void RejectsNonPositiveHeartbeatInterval()
    {
        var options = new BeaconOptions { DirectoryPath = "beacons", HeartbeatInterval = TimeSpan.Zero };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(45, 30)]
    public void RejectsHeartbeatThatIsNotShorterThanTtl(int intervalSeconds, int ttlSeconds)
    {
        // Otherwise a healthy participant is stale between every pair of check-ins.
        var options = new BeaconOptions
        {
            DirectoryPath = "beacons",
            Ttl = TimeSpan.FromSeconds(ttlSeconds),
            HeartbeatInterval = TimeSpan.FromSeconds(intervalSeconds),
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void FileStoreRejectsInvalidOptions()
    {
        var options = new BeaconOptions { DirectoryPath = "  " };

        Assert.Throws<InvalidOperationException>(() => new FileBeaconStore(options));
    }

    [Fact]
    public void WriterRejectsInvalidOptions()
    {
        var options = new BeaconOptions
        {
            DirectoryPath = "beacons",
            Ttl = TimeSpan.FromSeconds(10),
            HeartbeatInterval = TimeSpan.FromSeconds(10),
        };

        Assert.Throws<InvalidOperationException>(
            () => new BeaconWriter(NodeId.Parse("ridge-01"), new InMemoryBeaconStore(), options, MutableClock.AtStart()));
    }

    [Fact]
    public void FileStoreRejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new FileBeaconStore(null!));
    }
}
