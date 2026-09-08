namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// A snapshot is the answer to "who is live right now?", frozen at one instant.
/// </summary>
public sealed class PresenceSnapshotTests
{
    private static readonly DateTimeOffset Observed = MutableClock.AtStart().UtcNow;

    [Fact]
    public void LiveAndStalePartitionTheRoster()
    {
        var snapshot = SnapshotOf(
            Entry("ridge-01", BeaconStatus.Live),
            Entry("meadow-02", BeaconStatus.Stale),
            Entry("summit-03", BeaconStatus.Live));

        Assert.Equal(3, snapshot.Nodes.Count);
        Assert.Equal(2, snapshot.Live.Count);
        Assert.Single(snapshot.Stale);
        Assert.Equal(snapshot.Nodes.Count, snapshot.Live.Count + snapshot.Stale.Count);
    }

    [Fact]
    public void AnEmptySnapshotIsAValidAnswer()
    {
        var snapshot = SnapshotOf();

        Assert.Empty(snapshot.Nodes);
        Assert.Empty(snapshot.Live);
        Assert.Empty(snapshot.Stale);
        Assert.Equal(Observed, snapshot.ObservedAt);
    }

    [Fact]
    public void StatusOfReportsARecordedNode()
    {
        var snapshot = SnapshotOf(Entry("ridge-01", BeaconStatus.Stale));

        Assert.Equal(BeaconStatus.Stale, snapshot.StatusOf(NodeId.Parse("ridge-01")));
    }

    [Fact]
    public void StatusOfReportsAnAbsentNodeAsMissing()
    {
        var snapshot = SnapshotOf(Entry("ridge-01", BeaconStatus.Live));

        Assert.Equal(BeaconStatus.Missing, snapshot.StatusOf(NodeId.Parse("summit-03")));
    }

    [Fact]
    public void StatusOfIsCaseSensitive()
    {
        var snapshot = SnapshotOf(Entry("ridge-01", BeaconStatus.Live));

        Assert.Equal(BeaconStatus.Missing, snapshot.StatusOf(NodeId.Parse("RIDGE-01")));
    }

    private static PresenceSnapshot SnapshotOf(params NodePresence[] nodes) =>
        new() { ObservedAt = Observed, Nodes = nodes };

    private static NodePresence Entry(string id, BeaconStatus status)
    {
        var node = NodeId.Parse(id);

        return new NodePresence
        {
            Node = node,
            Status = status,
            Beacon = new Beacon { Node = node, LastSeen = Observed, Ttl = TimeSpan.FromSeconds(60) },
        };
    }
}
