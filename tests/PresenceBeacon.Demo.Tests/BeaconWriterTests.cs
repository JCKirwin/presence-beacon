namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// Invariant: a writer touches its own record and nothing else.
/// </summary>
/// <remarks>
/// The design enforces this structurally -- the id is captured in the constructor and no
/// method takes one -- so these tests pin the structure as much as the behaviour.
/// </remarks>
public sealed class BeaconWriterTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task HeartbeatStampsTheClockAndTheConfiguredTtl()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var writer = WriterFor("ridge-01", store, clock);

        var beacon = await writer.HeartbeatAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(NodeId.Parse("ridge-01"), beacon.Node);
        Assert.Equal(clock.UtcNow, beacon.LastSeen);
        Assert.Equal(Ttl, beacon.Ttl);
    }

    [Fact]
    public async Task HeartbeatReturnsExactlyWhatWasStored()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var writer = WriterFor("ridge-01", store, clock);
        var token = TestContext.Current.CancellationToken;

        var returned = await writer.HeartbeatAsync("sampled 12.4C", token);
        var stored = await store.LoadAsync(writer.Node, token);

        Assert.Equal(returned, stored);
    }

    [Fact]
    public async Task RepeatedHeartbeatsAdvanceTheSameRecord()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var writer = WriterFor("ridge-01", store, clock);
        var token = TestContext.Current.CancellationToken;

        var first = await writer.HeartbeatAsync(cancellationToken: token);
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = await writer.HeartbeatAsync(cancellationToken: token);

        Assert.Equal(second.LastSeen, first.LastSeen + TimeSpan.FromSeconds(30));
        Assert.Single(store.Ids);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task AWriterCannotStampUnderANeighboursName()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var ridge = WriterFor("ridge-01", store, clock);
        var meadow = WriterFor("meadow-02", store, clock);
        var token = TestContext.Current.CancellationToken;

        await meadow.HeartbeatAsync(cancellationToken: token);
        var meadowBefore = await store.LoadAsync(meadow.Node, token);

        for (var beat = 0; beat < 10; beat++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await ridge.HeartbeatAsync(cancellationToken: token);
        }

        var meadowAfter = await store.LoadAsync(meadow.Node, token);

        // Two entries, and the quiet node's entry is the one it wrote itself.
        Assert.Equal(["meadow-02", "ridge-01"], store.Ids);
        Assert.Equal(meadowBefore, meadowAfter);
    }

    [Fact]
    public async Task TheNodeIdIsFixedForTheLifetimeOfTheWriter()
    {
        var clock = MutableClock.AtStart();
        var writer = WriterFor("ridge-01", new InMemoryBeaconStore(), clock);
        var token = TestContext.Current.CancellationToken;

        var first = await writer.HeartbeatAsync("one", token);
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await writer.HeartbeatAsync("two", token);

        Assert.Equal(writer.Node, first.Node);
        Assert.Equal(writer.Node, second.Node);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTaskHintIsStoredAsNothingAtAll(string? hint)
    {
        var writer = WriterFor("ridge-01", new InMemoryBeaconStore(), MutableClock.AtStart());

        var beacon = await writer.HeartbeatAsync(hint, TestContext.Current.CancellationToken);

        Assert.Null(beacon.TaskHint);
    }

    [Fact]
    public async Task ATaskHintIsTrimmed()
    {
        var writer = WriterFor("ridge-01", new InMemoryBeaconStore(), MutableClock.AtStart());

        var beacon = await writer.HeartbeatAsync("  sampled 12.4C  ", TestContext.Current.CancellationToken);

        Assert.Equal("sampled 12.4C", beacon.TaskHint);
    }

    [Fact]
    public void ConstructionRejectsTheDefaultNodeId()
    {
        Assert.Throws<ArgumentException>(
            () => new BeaconWriter(default, new InMemoryBeaconStore(), OptionsFor("beacons"), MutableClock.AtStart()));
    }

    [Fact]
    public void ConstructionRejectsMissingCollaborators()
    {
        var node = NodeId.Parse("ridge-01");
        var options = OptionsFor("beacons");

        Assert.Throws<ArgumentNullException>(() => new BeaconWriter(node, null!, options, MutableClock.AtStart()));
        Assert.Throws<ArgumentNullException>(() => new BeaconWriter(node, new InMemoryBeaconStore(), null!, MutableClock.AtStart()));
        Assert.Throws<ArgumentNullException>(() => new BeaconWriter(node, new InMemoryBeaconStore(), options, null!));
    }

    private static BeaconWriter WriterFor(string id, IBeaconStore store, IClock clock) =>
        new(NodeId.Parse(id), store, OptionsFor("beacons"), clock);

    private static BeaconOptions OptionsFor(string directory) => new()
    {
        DirectoryPath = directory,
        Ttl = Ttl,
        HeartbeatInterval = TimeSpan.FromSeconds(15),
    };
}
