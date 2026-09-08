namespace PresenceBeacon.Demo.Tests;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Station;
using PresenceBeacon.Demo.Tests.Fakes;

using Xunit;

/// <summary>
/// The dashboard only ever reads. It has no channel to the nodes and no registry of
/// expected nodes, so everything it prints comes from files the nodes left behind.
/// </summary>
public sealed class StationDashboardTests
{
    private static readonly DateTimeOffset Observed = MutableClock.AtStart().UtcNow;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    [Fact]
    public void AnEmptyDirectoryRendersAsAPlainStatement()
    {
        var text = StationDashboard.Render(new PresenceSnapshot { ObservedAt = Observed, Nodes = [] });

        Assert.Contains("no beacons found", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EachNodeGetsItsOwnLineWithAStatus()
    {
        var snapshot = SnapshotWith(
            Entry("ridge-01", Observed, BeaconStatus.Live, "sampled 12.4C"),
            Entry("meadow-02", Observed - TimeSpan.FromMinutes(3), BeaconStatus.Stale, null));

        var lines = StationDashboard.Render(snapshot).Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains("1 live, 1 stale", lines[0], StringComparison.Ordinal);
        Assert.Contains("STALE", lines[1], StringComparison.Ordinal);
        Assert.Contains("LIVE", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void NodesAreListedInAStableOrder()
    {
        // The dashboard is polled repeatedly. Rows that reshuffle between polls make it
        // impossible to see at a glance which node changed.
        var snapshot = SnapshotWith(
            Entry("summit-03", Observed, BeaconStatus.Live, null),
            Entry("ridge-01", Observed, BeaconStatus.Live, null),
            Entry("meadow-02", Observed, BeaconStatus.Live, null));

        var text = StationDashboard.Render(snapshot);

        Assert.True(text.IndexOf("meadow-02", StringComparison.Ordinal) < text.IndexOf("ridge-01", StringComparison.Ordinal));
        Assert.True(text.IndexOf("ridge-01", StringComparison.Ordinal) < text.IndexOf("summit-03", StringComparison.Ordinal));
    }

    [Fact]
    public void ATaskHintIsShownWhenOneWasRecorded()
    {
        var snapshot = SnapshotWith(Entry("ridge-01", Observed, BeaconStatus.Live, "sampled 12.4C"));

        Assert.Contains("sampled 12.4C", StationDashboard.Render(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void AClockSkewedCheckInNeverPrintsANegativeAge()
    {
        var snapshot = SnapshotWith(Entry("ridge-01", Observed + TimeSpan.FromMinutes(5), BeaconStatus.Live, null));

        var text = StationDashboard.Render(snapshot);

        Assert.DoesNotContain("-", text[text.IndexOf("last seen", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void AgeIsShownInSecondsUnderAMinuteAndMinutesAbove()
    {
        var recent = StationDashboard.Render(SnapshotWith(Entry("ridge-01", Observed - TimeSpan.FromSeconds(9), BeaconStatus.Live, null)));
        var older = StationDashboard.Render(SnapshotWith(Entry("ridge-01", Observed - TimeSpan.FromMinutes(4), BeaconStatus.Stale, null)));

        Assert.Contains("9.0s ago", recent, StringComparison.Ordinal);
        Assert.Contains("4.0m ago", older, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollingGoesThroughTheReader()
    {
        var clock = MutableClock.AtStart();
        var store = new InMemoryBeaconStore();
        store.Seed(new Beacon { Node = NodeId.Parse("ridge-01"), LastSeen = clock.UtcNow, Ttl = Ttl });

        var dashboard = new StationDashboard(new BeaconReader(store, clock));
        var text = await dashboard.PollAsync(TestContext.Current.CancellationToken);

        Assert.Contains("ridge-01", text, StringComparison.Ordinal);
        Assert.Contains("LIVE", text, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void ConstructionRejectsAMissingReader()
    {
        Assert.Throws<ArgumentNullException>(() => new StationDashboard(null!));
    }

    [Fact]
    public void RenderRejectsAMissingSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => StationDashboard.Render(null!));
    }

    private static PresenceSnapshot SnapshotWith(params NodePresence[] nodes) =>
        new() { ObservedAt = Observed, Nodes = nodes };

    private static NodePresence Entry(string id, DateTimeOffset lastSeen, BeaconStatus status, string? hint)
    {
        var node = NodeId.Parse(id);

        return new NodePresence
        {
            Node = node,
            Status = status,
            Beacon = new Beacon { Node = node, LastSeen = lastSeen, Ttl = Ttl, TaskHint = hint },
        };
    }
}
