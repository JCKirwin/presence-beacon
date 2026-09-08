namespace PresenceBeacon.Demo.Presence;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Stores each beacon as one JSON file named after its participant, inside the configured
/// directory.
/// </summary>
/// <remarks>
/// Two rules make this safe for concurrent participants. Writes go to a temporary file in
/// the same directory and are then moved into place, so a reader sees either the old
/// record or the new one and never a half-written file. And because the participant id is
/// the file name, two participants can never target the same file.
/// </remarks>
public sealed class FileBeaconStore : IBeaconStore
{
    private const string FileSuffix = ".beacon.json";
    private const int ReplaceAttempts = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string _directory;

    /// <summary>
    /// Creates a store over the directory named in <paramref name="options"/>. The directory
    /// is not created until the first write.
    /// </summary>
    /// <param name="options">Directory and timing settings.</param>
    public FileBeaconStore(BeaconOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _directory = Path.GetFullPath(options.DirectoryPath);
    }

    /// <summary>
    /// Resolves the file path a given participant's beacon occupies.
    /// </summary>
    /// <param name="node">The participant.</param>
    /// <returns>An absolute path inside the configured directory.</returns>
    public string PathFor(NodeId node)
    {
        if (node.Value.Length == 0)
        {
            throw new ArgumentException("A beacon path needs an id built through NodeId.Parse.", nameof(node));
        }

        return Path.Combine(_directory, node.Value + FileSuffix);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Beacon beacon, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beacon);

        Directory.CreateDirectory(_directory);

        var destination = PathFor(beacon.Node);

        // The scratch path carries a random suffix. Two writers for the same id should not
        // exist, but if a participant is started twice they still get separate temp files
        // and the last move simply wins.
        var scratch = destination + ".tmp-" + Guid.NewGuid().ToString("n")[..8];
        var payload = JsonSerializer.SerializeToUtf8Bytes(BeaconDocument.From(beacon), SerializerOptions);

        try
        {
            await File.WriteAllBytesAsync(scratch, payload, cancellationToken).ConfigureAwait(false);
            await ReplaceAsync(scratch, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Beacon>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var beacons = new List<Beacon>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Match the suffix here rather than through a search pattern: on Windows a
                // pattern can also match longer extensions, which would sweep up the temp
                // files of an in-flight write.
                if (!path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var beacon = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (beacon is not null)
                {
                    beacons.Add(beacon);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // The directory went away underneath us. Report what was already read.
        }

        return beacons;
    }

    /// <inheritdoc />
    public Task<Beacon?> LoadAsync(NodeId node, CancellationToken cancellationToken = default) =>
        ReadAsync(PathFor(node), cancellationToken);

    private static async Task ReplaceAsync(string scratch, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    // Replace, not move. On Windows an overwriting move refuses to run
                    // while any handle on the destination is open, even one that shared
                    // delete access -- so a dashboard mid-poll would fail a heartbeat.
                    // Replace honors that shared handle, which is what keeps readers and
                    // writers out of each other's way.
                    File.Replace(scratch, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(scratch, destination);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < ReplaceAttempts)
            {
                // The destination can appear between the check and the call, or a reader
                // can hold it for a moment. Backing off is cheaper than failing a heartbeat.
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Beacon?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // FileShare.Delete lets a writer move a new payload onto this path while the
            // handle is open, so readers never block heartbeats.
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var document = await JsonSerializer
                .DeserializeAsync<BeaconDocument>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return document?.ToBeacon();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // A participant that never started, or a file that vanished mid-read. Both are
            // ordinary states, not failures.
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // One unreadable neighbor must not take down the whole roster.
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch file is untidy, not incorrect. It is never enumerated.
        }
    }

    /// <summary>
    /// The wire shape, kept separate from <see cref="Beacon"/> so the domain type can use a
    /// validated id and a <see cref="TimeSpan"/> while the file stays plain JSON.
    /// </summary>
    private sealed record BeaconDocument
    {
        public string? Node { get; init; }

        public DateTimeOffset LastSeen { get; init; }

        public double TtlSeconds { get; init; }

        public string? TaskHint { get; init; }

        public static BeaconDocument From(Beacon beacon) => new()
        {
            Node = beacon.Node.Value,
            LastSeen = beacon.LastSeen,
            TtlSeconds = beacon.Ttl.TotalSeconds,
            TaskHint = beacon.TaskHint,
        };

        public Beacon? ToBeacon()
        {
            if (!NodeId.TryParse(Node, out var node))
            {
                return null;
            }

            if (!double.IsFinite(TtlSeconds) || TtlSeconds <= 0)
            {
                return null;
            }

            return new Beacon
            {
                Node = node,
                LastSeen = LastSeen,
                Ttl = TimeSpan.FromSeconds(TtlSeconds),
                TaskHint = TaskHint,
            };
        }
    }
}
