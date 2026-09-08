namespace PresenceBeacon.Demo.Tests.Fakes;

using IoPath = System.IO.Path;

/// <summary>
/// A scratch directory path for file-store tests. The directory is deliberately not
/// created, because "the directory does not exist yet" is one of the states under test.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    private const string BeaconSuffix = ".beacon.json";

    private TempDirectory(string fullPath) => FullPath = fullPath;

    public string FullPath { get; }

    public bool Exists => Directory.Exists(FullPath);

    /// <summary>Files the store would enumerate as beacons, sorted for stable assertions.</summary>
    public IReadOnlyList<string> BeaconFiles => Filter(name => name.EndsWith(BeaconSuffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Everything on disk, including scratch files a write should have cleaned up.</summary>
    public IReadOnlyList<string> AllFiles => Filter(_ => true);

    public static TempDirectory Create() =>
        new(IoPath.Combine(IoPath.GetTempPath(), "presence-beacon-tests", Guid.NewGuid().ToString("n")));

    public string Combine(string fileName) => IoPath.Combine(FullPath, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch directory must not fail an otherwise passing test run.
        }
    }

    private IReadOnlyList<string> Filter(Func<string, bool> predicate) =>
        Directory.Exists(FullPath)
            ? Directory.GetFiles(FullPath)
                .Where(path => predicate(IoPath.GetFileName(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];
}
