namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Station;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// The demo story end to end: three nodes on a real directory, one of them goes quiet, and
/// the dashboard notices on its own without anyone touching the other two.
/// </summary>
/// <remarks>
/// Every invariant in the dossier meets here at once, which is why this runs against the
/// file store and the real dashboard rather than doubles.
/// </remarks>
public sealed class PresenceScenarioTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task AQuietNodeFlipsToStaleWhileItsPeersKeepReporting()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var options = OptionsOver(directory);
        var store = new FileBeaconStore(options);
        var reader = new BeaconReader(store, clock);
        var token = TestContext.Current.CancellationToken;

        var ridge = NodeOver("ridge-01", store, options, clock);
        var meadow = NodeOver("meadow-02", store, options, clock);
        var summit = NodeOver("summit-03", store, options, clock);

        // Round one: everybody reports.
        foreach (var node in new[] { ridge, meadow, summit })
        {
            await node.SampleOnceAsync(token);
        }

        var warmUp = await reader.SnapshotAsync(token);
        Assert.Equal(3, warmUp.Live.Count);
        Assert.Empty(warmUp.Stale);

        // Round two onward: summit-03 has stopped. Nothing deletes its file, nothing
        // announces its departure -- it just never writes again.
        for (var beat = 0; beat < 8; beat++)
        {
            clock.Advance(Heartbeat);
            await ridge.SampleOnceAsync(token);
            await meadow.SampleOnceAsync(token);
        }

        var afterExpiry = await reader.SnapshotAsync(token);

        Assert.Equal(BeaconStatus.Live, afterExpiry.StatusOf(ridge.Id));
        Assert.Equal(BeaconStatus.Live, afterExpiry.StatusOf(meadow.Id));
        Assert.Equal(BeaconStatus.Stale, afterExpiry.StatusOf(summit.Id));

        // Three files, still, including the quiet node's.
        Assert.Equal(3, directory.BeaconFiles.Count);
        Assert.True(File.Exists(store.PathFor(summit.Id)));

        var text = StationDashboard.Render(afterExpiry);
        Assert.Contains("2 live, 1 stale", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReturningNodeRevivesByCheckingInAgain()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var options = OptionsOver(directory);
        var store = new FileBeaconStore(options);
        var reader = new BeaconReader(store, clock);
        var token = TestContext.Current.CancellationToken;

        var summit = NodeOver("summit-03", store, options, clock);

        await summit.SampleOnceAsync(token);
        clock.Advance(Ttl + TimeSpan.FromMinutes(5));

        Assert.Equal(BeaconStatus.Stale, await reader.StatusOfAsync(summit.Id, token));

        // Nothing had to be repaired or re-registered. Liveness is derived from the file,
        // so writing a fresh one is the whole recovery path.
        await summit.SampleOnceAsync(token);

        Assert.Equal(BeaconStatus.Live, await reader.StatusOfAsync(summit.Id, token));
        Assert.Single(directory.BeaconFiles);
    }

    [Fact]
    public async Task ManyNodesHeartbeatingAtOnceProduceOneFileEachAndOneCoherentRoster()
    {
        using var directory = TempDirectory.Create();
        var clock = MutableClock.AtStart();
        var options = OptionsOver(directory);
        var store = new FileBeaconStore(options);
        var token = TestContext.Current.CancellationToken;

        var nodes = Enumerable.Range(1, 6)
            .Select(index => NodeOver($"node-{index:00}", store, options, clock))
            .ToArray();

        await Parallel.ForEachAsync(
            nodes,
            token,
            async (node, cancellationToken) =>
            {
                for (var beat = 0; beat < 15; beat++)
                {
                    await node.SampleOnceAsync(cancellationToken);
                }
            });

        var snapshot = await new BeaconReader(store, clock).SnapshotAsync(token);

        Assert.Equal(nodes.Length, directory.BeaconFiles.Count);
        Assert.Equal(nodes.Length, snapshot.Live.Count);
        Assert.Empty(snapshot.Stale);

        foreach (var node in nodes)
        {
            Assert.Equal(BeaconStatus.Live, snapshot.StatusOf(node.Id));
        }
    }

    private static BeaconOptions OptionsOver(TempDirectory directory) => new()
    {
        DirectoryPath = directory.FullPath,
        Ttl = Ttl,
        HeartbeatInterval = Heartbeat,
    };

    private static SensorNode NodeOver(string id, IBeaconStore store, BeaconOptions options, IClock clock)
    {
        var node = NodeId.Parse(id);

        return new SensorNode(node, new BeaconWriter(node, store, options, clock), options, clock);
    }
}
