namespace PresenceBeacon.Demo.Station;

using PresenceBeacon.Demo.Presence;

/// <summary>
/// A simulated weather-station node: it samples a temperature, then checks in.
/// </summary>
/// <remarks>
/// The ordering matters for the demo. The heartbeat follows the sample, so a node that
/// hangs mid-sample stops checking in and the dashboard notices on its own.
/// </remarks>
public interface ISensorNode
{
    /// <summary>The node's identity, which is also the name of its beacon file.</summary>
    NodeId Id { get; }

    /// <summary>
    /// Takes one invented reading and writes one heartbeat.
    /// </summary>
    /// <param name="cancellationToken">Cancels the cycle.</param>
    /// <returns>The reading taken during this cycle.</returns>
    Task<Reading> SampleOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Repeats <see cref="SampleOnceAsync(CancellationToken)"/> on the configured heartbeat
    /// interval until cancelled.
    /// </summary>
    /// <param name="cancellationToken">Stops the node, after which its beacon ages out.</param>
    Task RunAsync(CancellationToken cancellationToken = default);
}
