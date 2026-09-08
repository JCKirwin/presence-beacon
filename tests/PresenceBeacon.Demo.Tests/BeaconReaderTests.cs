namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// The reader turns stored beacons into verdicts. It must apply each beacon's own
/// time-to-live, judge every beacon against one instant, and never write anything.
/// </summary>
public sealed class BeaconReaderTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AFreshCheckInReadsAsLive()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        store.Seed(BeaconFor("ridge-01", clock.UtcNow));

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(NodeId.Parse("ridge-01")));
        Assert.Single(snapshot.Live);
        Assert.Empty(snapshot.Stale);
    }

    [Fact]
    public async Task ACheckInExactlyAtItsTtlStillReadsAsLive()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        store.Seed(BeaconFor("ridge-01", clock.UtcNow));

        clock.Advance(Ttl);

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(NodeId.Parse("ridge-01")));
    }

    [Fact]
    public async Task ACheckInPastItsTtlReadsAsStale()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        store.Seed(BeaconFor("ridge-01", clock.UtcNow));

        clock.Advance(Ttl + TimeSpan.FromTicks(1));

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BeaconStatus.Stale, snapshot.StatusOf(NodeId.Parse("ridge-01")));
        Assert.Empty(snapshot.Live);
        Assert.Single(snapshot.Stale);
    }

    [Fact]
    public async Task OneNodeGoingQuietDoesNotAffectTheOthers()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var token = TestContext.Current.CancellationToken;

        store.Seed(BeaconFor("meadow-02", clock.UtcNow));
        clock.Advance(Ttl + TimeSpan.FromSeconds(30));
        store.Seed(BeaconFor("ridge-01", clock.UtcNow));
        store.Seed(BeaconFor("summit-03", clock.UtcNow));

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(token);

        Assert.Equal(3, snapshot.Nodes.Count);
        Assert.Equal(2, snapshot.Live.Count);
        Assert.Equal(BeaconStatus.Stale, snapshot.StatusOf(NodeId.Parse("meadow-02")));
        Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(NodeId.Parse("ridge-01")));
        Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(NodeId.Parse("summit-03")));
    }

    [Fact]
    public async Task EachBeaconIsJudgedAgainstItsOwnTtl()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();

        store.Seed(new Beacon { Node = NodeId.Parse("patient-01"), LastSeen = clock.UtcNow, Ttl = TimeSpan.FromMinutes(10) });
        store.Seed(new Beacon { Node = NodeId.Parse("brisk-02"), LastSeen = clock.UtcNow, Ttl = TimeSpan.FromSeconds(5) });

        clock.Advance(TimeSpan.FromMinutes(1));

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(TestContext.Current.CancellationToken);

        // The verdict comes from the beacon, not from the reader's configuration. A node
        // that wants a longer grace period says so, and readers honour it unchanged.
        Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(NodeId.Parse("patient-01")));
        Assert.Equal(BeaconStatus.Stale, snapshot.StatusOf(NodeId.Parse("brisk-02")));
    }

    [Fact]
    public async Task TheWholeSnapshotIsJudgedAgainstOneInstant()
    {
        // Under a clock that moves on every read, sampling "now" per beacon would classify
        // two identical beacons differently. One read keeps the snapshot self-consistent.
        var clock = new TickingClock(MutableClock.AtStart().UtcNow, TimeSpan.FromMinutes(5));
        var store = new InMemoryBeaconStore();
        var start = MutableClock.AtStart().UtcNow;

        store.Seed(BeaconFor("ridge-01", start));
        store.Seed(BeaconFor("meadow-02", start));
        store.Seed(BeaconFor("summit-03", start));

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, clock.Reads);
        Assert.Equal(start, snapshot.ObservedAt);
        Assert.Equal(3, snapshot.Live.Count);
    }

    [Fact]
    public async Task AnUnknownNodeIsMissingRatherThanStale()
    {
        var clock = MutableClock.AtStart();
        var reader = new BeaconReader(new InMemoryBeaconStore(), clock);
        var token = TestContext.Current.CancellationToken;

        // "Never checked in" and "checked in and went quiet" are different facts, and a
        // coordinator that conflates them cannot tell a crash from a cold start.
        Assert.Equal(BeaconStatus.Missing, await reader.StatusOfAsync(NodeId.Parse("ridge-01"), token));
    }

    [Fact]
    public async Task StatusOfAgreesWithTheSnapshot()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        var token = TestContext.Current.CancellationToken;

        store.Seed(BeaconFor("ridge-01", clock.UtcNow));
        store.Seed(BeaconFor("meadow-02", clock.UtcNow - Ttl - TimeSpan.FromMinutes(1)));

        var reader = new BeaconReader(store, clock);
        var snapshot = await reader.SnapshotAsync(token);

        foreach (var node in new[] { "ridge-01", "meadow-02", "summit-03" })
        {
            var id = NodeId.Parse(node);
            Assert.Equal(snapshot.StatusOf(id), await reader.StatusOfAsync(id, token));
        }
    }

    [Fact]
    public async Task AMissingDirectoryReadsAsAnEmptySnapshot()
    {
        // The dossier's fifth invariant, exercised through the real file store: nobody has
        // checked in yet is a normal state, not an error that aborts the caller.
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var store = new FileBeaconStore(new BeaconOptions { DirectoryPath = directory.FullPath, Ttl = Ttl });
        var reader = new BeaconReader(store, clock);
        var token = TestContext.Current.CancellationToken;

        var snapshot = await reader.SnapshotAsync(token);

        Assert.Empty(snapshot.Nodes);
        Assert.Empty(snapshot.Live);
        Assert.Empty(snapshot.Stale);
        Assert.Equal(clock.UtcNow, snapshot.ObservedAt);
        Assert.Equal(BeaconStatus.Missing, await reader.StatusOfAsync(NodeId.Parse("ridge-01"), token));
        Assert.False(directory.Exists);
    }

    [Fact]
    public async Task ReadingLeavesTheStoreUntouched()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        store.Seed(BeaconFor("ridge-01", clock.UtcNow));

        var reader = new BeaconReader(store, clock);
        await reader.SnapshotAsync(TestContext.Current.CancellationToken);
        await reader.StatusOfAsync(NodeId.Parse("ridge-01"), TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void ConstructionRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new BeaconReader(null!, MutableClock.AtStart()));
        Assert.Throws<ArgumentNullException>(() => new BeaconReader(new InMemoryBeaconStore(), null!));
    }

    private static Beacon BeaconFor(string id, DateTimeOffset lastSeen) => new()
    {
        Node = NodeId.Parse(id),
        LastSeen = lastSeen,
        Ttl = Ttl,
    };
}
