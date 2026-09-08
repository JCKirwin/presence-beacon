namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

using IoPath = System.IO.Path;

/// <summary>
/// The file store is where four of the five invariants actually live: one path per node,
/// no cross-node deletion, no corruption under concurrency, and a missing directory that
/// reads as empty rather than as a failure.
/// </summary>
/// <remarks>
/// These tests touch a real temporary directory on purpose. An in-memory double would
/// prove nothing about atomic replace or directory enumeration.
/// </remarks>
public sealed class FileBeaconStoreTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    // --- Invariant: exactly one beacon file path per node id ---------------------------

    [Fact]
    public void PathForIsStableAcrossCalls()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var node = NodeId.Parse("ridge-01");

        Assert.Equal(store.PathFor(node), store.PathFor(node));
    }

    [Fact]
    public void DifferentNodesGetDifferentPaths()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        Assert.NotEqual(store.PathFor(NodeId.Parse("ridge-01")), store.PathFor(NodeId.Parse("ridge-02")));
    }

    [Fact]
    public void PathForStaysInsideTheConfiguredDirectory()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        var path = store.PathFor(NodeId.Parse("ridge-01"));

        Assert.Equal(IoPath.GetFullPath(directory.FullPath), IoPath.GetDirectoryName(path));
    }

    [Fact]
    public void PathForRejectsTheDefaultNodeId()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        Assert.Throws<ArgumentException>(() => store.PathFor(default));
    }

    [Fact]
    public async Task RepeatedCheckInsLeaveExactlyOneFile()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var store = StoreOver(directory);
        var node = NodeId.Parse("ridge-01");

        for (var i = 0; i < 12; i++)
        {
            await store.SaveAsync(BeaconFor(node, clock.UtcNow), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        // Heartbeats replace; they do not append. A directory that grew a file per
        // check-in would make "who is live" ambiguous within a single node.
        Assert.Single(directory.BeaconFiles);
        Assert.Equal(store.PathFor(node), directory.BeaconFiles[0]);
    }

    [Fact]
    public async Task WritesLeaveNoScratchFilesBehind()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(BeaconFor(NodeId.Parse("ridge-01"), MutableClock.AtStart().UtcNow), TestContext.Current.CancellationToken);
        }

        Assert.Single(directory.AllFiles);
    }

    // --- Invariant: writers never delete other nodes' beacon files ---------------------

    [Fact]
    public async Task SavingOneNodeLeavesOtherNodesFilesByteForByteUntouched()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var store = StoreOver(directory);
        var quiet = NodeId.Parse("meadow-02");
        var busy = NodeId.Parse("ridge-01");
        var token = TestContext.Current.CancellationToken;

        await store.SaveAsync(BeaconFor(quiet, clock.UtcNow), token);
        var before = await File.ReadAllBytesAsync(store.PathFor(quiet), token);

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            await store.SaveAsync(BeaconFor(busy, clock.UtcNow), token);
        }

        var after = await File.ReadAllBytesAsync(store.PathFor(quiet), token);

        Assert.Equal(before, after);
        Assert.Equal(2, directory.BeaconFiles.Count);
    }

    [Fact]
    public async Task AStaleBeaconIsNeverCleanedUpByAReader()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var store = StoreOver(directory);
        var node = NodeId.Parse("meadow-02");
        var token = TestContext.Current.CancellationToken;

        await store.SaveAsync(BeaconFor(node, clock.UtcNow), token);
        clock.Advance(Ttl + TimeSpan.FromMinutes(10));

        var reader = new BeaconReader(store, clock);
        var snapshot = await reader.SnapshotAsync(token);

        // Expiry is a verdict, not an eviction. The file has to survive so the node can
        // revive itself simply by checking in again.
        Assert.Equal(BeaconStatus.Stale, snapshot.StatusOf(node));
        Assert.True(File.Exists(store.PathFor(node)));
    }

    // --- Invariant: concurrent writes to different files do not corrupt each other -----

    [Fact]
    public async Task ConcurrentCheckInsFromManyNodesAllRoundTrip()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var start = MutableClock.AtStart().UtcNow;
        var token = TestContext.Current.CancellationToken;

        var nodes = Enumerable.Range(1, 8)
            .Select(index => NodeId.Parse($"node-{index:00}"))
            .ToArray();

        await Parallel.ForEachAsync(
            nodes,
            token,
            async (node, cancellationToken) =>
            {
                for (var beat = 0; beat < 25; beat++)
                {
                    await store.SaveAsync(BeaconFor(node, start.AddSeconds(beat)), cancellationToken);
                }
            });

        var loaded = await store.LoadAllAsync(token);

        Assert.Equal(nodes.Length, directory.BeaconFiles.Count);
        Assert.Equal(nodes.Length, loaded.Count);
        Assert.Equal(
            nodes.Select(node => node.Value).OrderBy(value => value, StringComparer.Ordinal),
            loaded.Select(beacon => beacon.Node.Value).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadingTheRosterWhileNodesCheckInNeverYieldsATornRecord()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var start = MutableClock.AtStart().UtcNow;
        var token = TestContext.Current.CancellationToken;

        var nodes = Enumerable.Range(1, 6)
            .Select(index => NodeId.Parse($"node-{index:00}"))
            .ToArray();

        foreach (var node in nodes)
        {
            await store.SaveAsync(BeaconFor(node, start), token);
        }

        var writing = Parallel.ForEachAsync(
            nodes,
            token,
            async (node, cancellationToken) =>
            {
                for (var beat = 0; beat < 40; beat++)
                {
                    await store.SaveAsync(BeaconFor(node, start.AddSeconds(beat)), cancellationToken);
                }
            });

        var observed = 0;
        do
        {
            // Writes land through a scratch file and an atomic move, so a reader sees the
            // old record or the new one. A torn read would come back as an unparseable
            // file -- dropped by the store and missing from this list.
            var beacons = await store.LoadAllAsync(token);

            foreach (var beacon in beacons)
            {
                Assert.Contains(beacon.Node, nodes);
                Assert.True(beacon.Ttl > TimeSpan.Zero);
                Assert.Equal(Ttl, beacon.Ttl);
                observed++;
            }
        }
        while (!writing.IsCompleted);

        await writing;

        Assert.True(observed > 0, "the reader never managed a read while the writers were running");
        Assert.Equal(nodes.Length, (await store.LoadAllAsync(token)).Count);
    }

    [Fact]
    public async Task AReaderHoldingAFileOpenDoesNotBlockThatNodesNextCheckIn()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var start = MutableClock.AtStart().UtcNow;
        var node = NodeId.Parse("ridge-01");
        var token = TestContext.Current.CancellationToken;

        await store.SaveAsync(BeaconFor(node, start), token);

        // A reader that opened the beacon a moment ago and has not closed it yet. The
        // store's own read path shares delete access precisely so this case works.
        await using var reading = new FileStream(
            store.PathFor(node),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        await store.SaveAsync(BeaconFor(node, start.AddSeconds(15)), token);

        var reloaded = await store.LoadAsync(node, token);

        Assert.NotNull(reloaded);
        Assert.Equal(start.AddSeconds(15), reloaded.LastSeen);
    }

    // --- Invariant: a missing directory reads as empty, not as an exception ------------

    [Fact]
    public async Task LoadAllOnAMissingDirectoryReturnsAnEmptyList()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        var beacons = await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.False(directory.Exists);
        Assert.Empty(beacons);
    }

    [Fact]
    public async Task LoadOnAMissingDirectoryReturnsNull()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        var beacon = await store.LoadAsync(NodeId.Parse("ridge-01"), TestContext.Current.CancellationToken);

        Assert.Null(beacon);
    }

    [Fact]
    public async Task ReadingDoesNotCreateTheDirectory()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        await store.LoadAllAsync(TestContext.Current.CancellationToken);

        Assert.False(directory.Exists);
    }

    [Fact]
    public async Task TheFirstWriteCreatesTheDirectory()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        Assert.False(directory.Exists);

        await store.SaveAsync(BeaconFor(NodeId.Parse("ridge-01"), MutableClock.AtStart().UtcNow), TestContext.Current.CancellationToken);

        Assert.True(directory.Exists);
    }

    // --- Enumeration and payload -------------------------------------------------------

    [Fact]
    public async Task ARoundTripPreservesEveryField()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var token = TestContext.Current.CancellationToken;
        var node = NodeId.Parse("ridge-01");

        var written = BeaconFor(node, MutableClock.AtStart().UtcNow) with { TaskHint = "sampled 12.4C" };
        await store.SaveAsync(written, token);

        var read = await store.LoadAsync(node, token);

        Assert.NotNull(read);
        Assert.Equal(written.Node, read.Node);
        Assert.Equal(written.LastSeen, read.LastSeen);
        Assert.Equal(written.Ttl, read.Ttl);
        Assert.Equal(written.TaskHint, read.TaskHint);
    }

    [Fact]
    public async Task UnrelatedFilesAndInFlightScratchFilesAreIgnored()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var token = TestContext.Current.CancellationToken;

        await store.SaveAsync(BeaconFor(NodeId.Parse("ridge-01"), MutableClock.AtStart().UtcNow), token);

        await File.WriteAllTextAsync(directory.Combine("notes.txt"), "not a beacon", token);
        await File.WriteAllTextAsync(directory.Combine("ridge-02.beacon.json.tmp-a1b2c3d4"), "{ half-writ", token);

        var beacons = await store.LoadAllAsync(token);

        Assert.Single(beacons);
        Assert.Equal("ridge-01", beacons[0].Node.Value);
    }

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("null")]
    [InlineData("{\"node\":\"ridge/02\",\"lastSeen\":\"2031-03-14T09:00:00+00:00\",\"ttlSeconds\":60}")]
    [InlineData("{\"node\":\"ridge-02\",\"lastSeen\":\"2031-03-14T09:00:00+00:00\",\"ttlSeconds\":0}")]
    [InlineData("{\"node\":\"ridge-02\",\"lastSeen\":\"2031-03-14T09:00:00+00:00\",\"ttlSeconds\":-5}")]
    public async Task OneUnreadableNeighbourDoesNotTakeDownTheRoster(string payload)
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var token = TestContext.Current.CancellationToken;

        await store.SaveAsync(BeaconFor(NodeId.Parse("ridge-01"), MutableClock.AtStart().UtcNow), token);
        await File.WriteAllTextAsync(directory.Combine("ridge-02.beacon.json"), payload, token);

        var beacons = await store.LoadAllAsync(token);

        // The healthy node still reports. Rejecting the whole directory because one
        // participant wrote nonsense would make the pattern less useful than no pattern.
        Assert.Single(beacons);
        Assert.Equal("ridge-01", beacons[0].Node.Value);
    }

    [Fact]
    public async Task LoadingAnUnreadableNodeReturnsNullRatherThanThrowing()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);
        var token = TestContext.Current.CancellationToken;

        Directory.CreateDirectory(directory.FullPath);
        await File.WriteAllTextAsync(directory.Combine("ridge-02.beacon.json"), "{ not json at all", token);

        Assert.Null(await store.LoadAsync(NodeId.Parse("ridge-02"), token));
    }

    [Fact]
    public async Task SaveRejectsANullBeacon()
    {
        using var directory = TempDirectory.Create();
        var store = StoreOver(directory);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.SaveAsync(null!, TestContext.Current.CancellationToken));
    }

    private static FileBeaconStore StoreOver(TempDirectory directory) =>
        new(new BeaconOptions
        {
            DirectoryPath = directory.FullPath,
            Ttl = Ttl,
            HeartbeatInterval = TimeSpan.FromSeconds(15),
        });

    private static Beacon BeaconFor(NodeId node, DateTimeOffset lastSeen) => new()
    {
        Node = node,
        LastSeen = lastSeen,
        Ttl = Ttl,
    };
}
