namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Station;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// The weather-station node is the demo's stand-in for "a worker doing real work". It
/// samples, then checks in, and it never touches anyone else's beacon.
/// </summary>
public sealed class SensorNodeTests
{
    [Fact]
    public async Task SamplingAlsoChecksIn()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var node = NodeFor("ridge-01", store, clock);
        var token = TestContext.Current.CancellationToken;

        var reading = await node.SampleOnceAsync(token);
        var beacon = await store.LoadAsync(node.Id, token);

        Assert.Equal(node.Id, reading.Node);
        Assert.Equal(clock.UtcNow, reading.TakenAt);
        Assert.NotNull(beacon);
        Assert.Equal(clock.UtcNow, beacon.LastSeen);
    }

    [Fact]
    public async Task TheCheckInCarriesAHintAboutTheWorkJustDone()
    {
        var store = new InMemoryBeaconStore();
        var node = NodeFor("ridge-01", store, MutableClock.AtStart());
        var token = TestContext.Current.CancellationToken;

        await node.SampleOnceAsync(token);
        var beacon = await store.LoadAsync(node.Id, token);

        Assert.NotNull(beacon);
        Assert.NotNull(beacon.TaskHint);
        Assert.StartsWith("sampled ", beacon.TaskHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadingsStayWithinPlausibleBounds()
    {
        var clock = MutableClock.AtStart();
        var node = NodeFor("ridge-01", new InMemoryBeaconStore(), clock);
        var token = TestContext.Current.CancellationToken;

        for (var i = 0; i < 200; i++)
        {
            var reading = await node.SampleOnceAsync(token);

            Assert.InRange(reading.Celsius, -20d, 45d);
            clock.Advance(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task TwoNodesSharingAStoreKeepSeparateRecords()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var ridge = NodeFor("ridge-01", store, clock);
        var meadow = NodeFor("meadow-02", store, clock);
        var token = TestContext.Current.CancellationToken;

        await ridge.SampleOnceAsync(token);
        await meadow.SampleOnceAsync(token);

        Assert.Equal(["meadow-02", "ridge-01"], store.Ids);
    }

    [Fact]
    public void ANodeCannotBorrowAnotherNodesWriter()
    {
        // Without this guard a misconfigured node would refresh a neighbour's beacon,
        // making a dead participant look permanently alive.
        var clock = MutableClock.AtStart();
        var options = Options();
        var writer = new BeaconWriter(NodeId.Parse("meadow-02"), new InMemoryBeaconStore(), options, clock);

        Assert.Throws<ArgumentException>(
            () => new SensorNode(NodeId.Parse("ridge-01"), writer, options, clock));
    }

    [Fact]
    public void ConstructionRejectsMissingCollaborators()
    {
        var clock = MutableClock.AtStart();
        var options = Options();
        var id = NodeId.Parse("ridge-01");
        var writer = new BeaconWriter(id, new InMemoryBeaconStore(), options, clock);

        Assert.Throws<ArgumentNullException>(() => new SensorNode(id, null!, options, clock));
        Assert.Throws<ArgumentNullException>(() => new SensorNode(id, writer, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new SensorNode(id, writer, options, null!));
    }

    [Fact]
    public async Task StoppingANodeIsQuietAndLeavesItsLastCheckInInPlace()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var node = NodeFor("ridge-01", store, clock);

        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        // Cancellation is how a participant leaves. Nothing is deleted and nothing is
        // announced -- the last beacon simply stops being refreshed.
        await node.RunAsync(stopped.Token);

        var beacon = await store.LoadAsync(node.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(beacon);
        Assert.Single(store.Ids);
    }

    private static SensorNode NodeFor(string id, IBeaconStore store, IClock clock)
    {
        var node = NodeId.Parse(id);
        var options = Options();

        return new SensorNode(node, new BeaconWriter(node, store, options, clock), options, clock);
    }

    private static BeaconOptions Options() => new()
    {
        DirectoryPath = "beacons",
        Ttl = TimeSpan.FromSeconds(90),
        HeartbeatInterval = TimeSpan.FromSeconds(15),
    };
}
