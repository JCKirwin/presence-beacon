namespace PresenceBeacon.Demo.Station;

using System.Globalization;
using System.Text;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// The read-side of the demo: polls the beacon directory and prints who is reporting.
/// </summary>
/// <remarks>
/// The dashboard is a plain consumer of <see cref="IBeaconReader"/>. It has no channel to
/// the nodes, no registry of expected nodes, and no way to stop one. Everything it knows
/// comes from files the nodes left behind.
/// </remarks>
public sealed class StationDashboard
{
    private readonly IBeaconReader _reader;

    /// <summary>
    /// Creates a dashboard over a reader.
    /// </summary>
    /// <param name="reader">Source of presence snapshots.</param>
    public StationDashboard(IBeaconReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <summary>
    /// Takes one snapshot and renders it as a line-per-node report.
    /// </summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    /// <returns>Text ready to write to the console.</returns>
    public async Task<string> PollAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _reader.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        return Render(snapshot);
    }

    /// <summary>
    /// Formats an already-taken snapshot, kept separate from
    /// <see cref="PollAsync(CancellationToken)"/> so tests can assert on the text without
    /// touching a directory.
    /// </summary>
    /// <param name="snapshot">The snapshot to render.</param>
    /// <returns>Text ready to write to the console.</returns>
    public static string Render(PresenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var observed = snapshot.ObservedAt.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();

        if (snapshot.Nodes.Count == 0)
        {
            // An empty directory is a normal state, not an error worth shouting about.
            return $"{observed}Z  no beacons found";
        }

        var entries = snapshot.Nodes
            .OrderBy(entry => entry.Node.Value, StringComparer.Ordinal)
            .ToArray();

        var width = entries.Max(entry => entry.Node.Value.Length);

        builder.Append(CultureInfo.InvariantCulture, $"{observed}Z  {snapshot.Live.Count} live, {snapshot.Stale.Count} stale");

        foreach (var entry in entries)
        {
            var hint = entry.Beacon?.TaskHint;

            builder
                .AppendLine()
                .Append("  ")
                .Append(entry.Status.ToString().ToUpperInvariant().PadRight(6))
                .Append(entry.Node.Value.PadRight(width))
                .Append("  last seen ")
                .Append(FormatAge(snapshot.ObservedAt, entry.Beacon));

            if (!string.IsNullOrEmpty(hint))
            {
                builder.Append("  ").Append(hint);
            }
        }

        return builder.ToString();
    }

    private static string FormatAge(DateTimeOffset observedAt, Beacon? beacon)
    {
        if (beacon is null)
        {
            return "never";
        }

        var age = observedAt - beacon.LastSeen;

        // Clock skew can put a check-in in the future. Report it as fresh rather than
        // printing a negative age.
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age < TimeSpan.FromMinutes(1)
            ? string.Create(CultureInfo.InvariantCulture, $"{age.TotalSeconds,4:F1}s ago")
            : string.Create(CultureInfo.InvariantCulture, $"{age.TotalMinutes,4:F1}m ago");
    }
}
