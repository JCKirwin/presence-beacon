namespace PresenceBeacon.Demo.Station;

using System.Globalization;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// Default <see cref="ISensorNode"/>: invents a plausible temperature, then heartbeats.
/// </summary>
/// <remarks>
/// The node owns its own writer and never reads other nodes' beacons. Stopping this node
/// is the whole demo -- no cleanup runs, and the dashboard flips it to stale purely because
/// its last check-in aged past the time-to-live.
/// </remarks>
public sealed class SensorNode : ISensorNode
{
    private const double MinimumCelsius = -20d;
    private const double MaximumCelsius = 45d;

    private readonly IBeaconWriter _writer;
    private readonly IClock _clock;
    private readonly TimeSpan _interval;
    private readonly Random _drift;

    private double _celsius;

    /// <summary>
    /// Creates a node that reports under <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The node's identity.</param>
    /// <param name="writer">The beacon writer bound to that same identity.</param>
    /// <param name="options">Timing settings, including the heartbeat interval.</param>
    /// <param name="clock">Source of reading timestamps.</param>
    public SensorNode(NodeId id, IBeaconWriter writer, BeaconOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();

        if (!writer.Node.Equals(id))
        {
            throw new ArgumentException(
                $"Writer speaks for '{writer.Node}', so it cannot heartbeat for '{id}'.",
                nameof(writer));
        }

        Id = id;
        _writer = writer;
        _clock = clock;
        _interval = options.HeartbeatInterval;

        // Seeded from the id so each node follows its own curve and a demo run is
        // repeatable. The readings are invented; only the heartbeat matters.
        _drift = new Random(SeedFor(id));
        _celsius = Math.Round(5d + (_drift.NextDouble() * 20d), 1);
    }

    /// <inheritdoc />
    public NodeId Id { get; }

    /// <inheritdoc />
    public async Task<Reading> SampleOnceAsync(CancellationToken cancellationToken = default)
    {
        _celsius = Math.Round(
            Math.Clamp(_celsius + ((_drift.NextDouble() - 0.5d) * 0.8d), MinimumCelsius, MaximumCelsius),
            1);

        var reading = new Reading
        {
            Node = Id,
            Celsius = _celsius,
            TakenAt = _clock.UtcNow,
        };

        // Sample first, check in second. A node that hangs while sampling never reaches
        // this line, which is exactly the signal the dashboard needs.
        await _writer
            .HeartbeatAsync(
                string.Create(CultureInfo.InvariantCulture, $"sampled {reading.Celsius:F1}C"),
                cancellationToken)
            .ConfigureAwait(false);

        return reading;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            await SampleOnceAsync(cancellationToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SampleOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Going quiet is the whole point. Nothing is deleted, nothing is announced --
            // the last beacon simply stops being refreshed.
        }
    }

    private static int SeedFor(NodeId id)
    {
        // String hashing is randomized per process, so derive the seed by hand to keep
        // runs comparable.
        var seed = 17;

        foreach (var character in id.Value)
        {
            seed = unchecked((seed * 31) + character);
        }

        return seed;
    }
}
