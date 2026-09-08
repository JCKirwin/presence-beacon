// Entry point for the weather-station demo. Three sensor nodes publish beacons into one
// directory, a dashboard polls that directory, and partway through the run one node stops.
// Nothing tells the dashboard about the departure -- it works the change out from the
// timestamps alone, which is the entire claim of the pattern.
using System.Globalization;

using PresenceBeacon.Demo.Presence;
using PresenceBeacon.Demo.Station;

// Demo-scale timings. A heartbeat every two seconds against a six-second time-to-live
// leaves room for two missed check-ins before a node is written off, and makes the whole
// run finish in under half a minute.
var options = new BeaconOptions
{
    DirectoryPath = Path.Combine(Path.GetTempPath(), "presence-beacon-demo"),
    Ttl = TimeSpan.FromSeconds(6),
    HeartbeatInterval = TimeSpan.FromSeconds(2),
};

options.Validate();

// A previous run's beacons would show up as ghosts on the first poll. That is faithful
// behavior -- the directory is not owned by any one run -- but it makes the demo harder to
// read, so this run starts from an empty directory.
if (Directory.Exists(options.DirectoryPath))
{
    Directory.Delete(options.DirectoryPath, recursive: true);
}

const int TotalPolls = 10;
const int StopOneNodeAfterPoll = 3;

var clock = SystemClock.Instance;
var store = new FileBeaconStore(options);

// The dashboard gets a reader and nothing else. It has no handle on the nodes, no list of
// who is expected, and no way to ask a node how it is doing.
var dashboard = new StationDashboard(new BeaconReader(store, clock));

var shutdown = new CancellationTokenSource();

ConsoleCancelEventHandler stopOnCtrlC = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.CancelKeyPress += stopOnCtrlC;

// Each node carries its own cancellation source, linked to the shared one. Stopping
// exactly one participant while its peers keep working is the whole demo.
var running = new List<(SensorNode Node, CancellationTokenSource Stop, Task Loop)>();

foreach (var name in new[] { "ridge-01", "meadow-02", "summit-03" })
{
    var id = NodeId.Parse(name);
    var writer = new BeaconWriter(id, store, options, clock);
    var node = new SensorNode(id, writer, options, clock);
    var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);

    running.Add((node, stop, node.RunAsync(stop.Token)));
}

var quiet = running[^1];

Console.WriteLine("Presence Beacon -- weather-station demo");
Console.WriteLine($"  beacons    {options.DirectoryPath}");
Console.WriteLine($"  ttl        {Seconds(options.Ttl)}");
Console.WriteLine($"  heartbeat  {Seconds(options.HeartbeatInterval)}");
Console.WriteLine();
Console.WriteLine($"{running.Count} sensor nodes sample invented temperatures and check in after each sample.");
Console.WriteLine($"After poll {StopOneNodeAfterPoll}, {quiet.Node.Id} stops. Its beacon file is left exactly where it is;");
Console.WriteLine("watch it age past the ttl and flip to STALE while its peers stay LIVE.");
Console.WriteLine();

try
{
    for (var poll = 1; poll <= TotalPolls; poll++)
    {
        await Task.Delay(options.HeartbeatInterval, shutdown.Token);

        Console.WriteLine(await dashboard.PollAsync(shutdown.Token));
        Console.WriteLine();

        if (poll == StopOneNodeAfterPoll)
        {
            // Going quiet, in full. No file is removed, no peer is notified, and the
            // dashboard is not told to expect anything.
            await quiet.Stop.CancelAsync();

            Console.WriteLine($"-- {quiet.Node.Id} stopped sampling. Nothing was deleted and nobody was told.");
            Console.WriteLine();
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("-- interrupted.");
    Console.WriteLine();
}
finally
{
    foreach (var entry in running)
    {
        await entry.Stop.CancelAsync();
    }

    await Task.WhenAll(running.Select(entry => entry.Loop));

    foreach (var entry in running)
    {
        entry.Stop.Dispose();
    }

    Console.CancelKeyPress -= stopOnCtrlC;
    shutdown.Dispose();
}

Console.WriteLine($"Every node's last check-in is still on disk under {options.DirectoryPath}.");
Console.WriteLine("Expiry is a verdict, not an eviction: any of them revives by writing one more beacon.");

static string Seconds(TimeSpan span) =>
    string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:F0}s");
