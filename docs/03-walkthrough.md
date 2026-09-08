# 03 — Walkthrough

This section exists to walk you through every file in the repository in an order that builds on itself. `01-the-pattern.md` told you what a presence beacon is and `02-architecture.md` named the seams; this page opens each file, says why it exists, and shows the two or three lines in it that actually carry the pattern. Read it top to bottom the first time. After that it works as a reference — each heading is a path, so you can jump to the file you are looking at in your editor.

## Run it first

The demo is a console app. It needs nothing installed beyond the .NET 10 SDK and it writes only to your temp directory.

```bash
dotnet run --project src/PresenceBeacon.Demo
```

Three sensor nodes start sampling invented temperatures. Every two seconds a dashboard polls the beacon directory and prints a line per node. After the third poll, one node stops. Nothing deletes its file and nothing tells the dashboard — you watch the age column climb past the six-second time-to-live and the verdict flip on its own.

Abridged output:

```text
Presence Beacon -- weather-station demo
  beacons    C:\Users\you\AppData\Local\Temp\presence-beacon-demo
  ttl        6s
  heartbeat  2s

3 sensor nodes sample invented temperatures and check in after each sample.
After poll 3, summit-03 stops. Its beacon file is left exactly where it is;
watch it age past the ttl and flip to STALE while its peers stay LIVE.

09:14:22Z  3 live, 0 stale
  LIVE  meadow-02  last seen  0.3s ago  sampled 18.9C
  LIVE  ridge-01   last seen  0.3s ago  sampled 12.4C
  LIVE  summit-03  last seen  0.3s ago  sampled 11.2C

-- summit-03 stopped sampling. Nothing was deleted and nobody was told.

09:14:30Z  2 live, 1 stale
  LIVE  meadow-02  last seen  0.2s ago  sampled 19.1C
  LIVE  ridge-01   last seen  0.2s ago  sampled 12.1C
  STALE summit-03  last seen  8.1s ago  sampled 11.2C
```

Note the last line. `summit-03` still has a task hint, still has a timestamp, and still has a file on disk. The only thing that changed is how old that timestamp is relative to its own TTL.

## The file map

| Path | What it is |
|---|---|
| `src/PresenceBeacon.Demo/Program.cs` | Composition root and the demo script |
| `src/PresenceBeacon.Demo/Presence/NodeId.cs` | Validated participant identity, which is also the file name |
| `src/PresenceBeacon.Demo/Presence/Beacon.cs` | The heartbeat record and the liveness rule |
| `src/PresenceBeacon.Demo/Presence/BeaconOptions.cs` | Directory, TTL, heartbeat interval, and their validation |
| `src/PresenceBeacon.Demo/Presence/IClock.cs` | The "now" seam |
| `src/PresenceBeacon.Demo/Presence/SystemClock.cs` | The production clock |
| `src/PresenceBeacon.Demo/Presence/IBeaconStore.cs` | Storage abstraction, no opinion on liveness |
| `src/PresenceBeacon.Demo/Presence/FileBeaconStore.cs` | One JSON file per participant, replaced atomically |
| `src/PresenceBeacon.Demo/Presence/IBeaconWriter.cs` | Write half of the pattern |
| `src/PresenceBeacon.Demo/Presence/BeaconWriter.cs` | Stamps the clock and hands the record to the store |
| `src/PresenceBeacon.Demo/Presence/BeaconStatus.cs` | `Missing` / `Stale` / `Live` |
| `src/PresenceBeacon.Demo/Presence/NodePresence.cs` | One participant paired with its verdict |
| `src/PresenceBeacon.Demo/Presence/PresenceSnapshot.cs` | One immutable reading of the whole directory |
| `src/PresenceBeacon.Demo/Presence/IBeaconReader.cs` | Read half of the pattern |
| `src/PresenceBeacon.Demo/Presence/BeaconReader.cs` | Applies each beacon's TTL against one instant |
| `src/PresenceBeacon.Demo/Station/Reading.cs` | An invented temperature sample |
| `src/PresenceBeacon.Demo/Station/ISensorNode.cs` | A simulated worker |
| `src/PresenceBeacon.Demo/Station/SensorNode.cs` | Samples, then checks in |
| `src/PresenceBeacon.Demo/Station/StationDashboard.cs` | Polls and renders the roster |
| `tests/PresenceBeacon.Demo.Tests/**` | Ten test classes and four fakes |

Two namespaces, and the split is deliberate. `Presence` is the pattern and would move into a library unchanged. `Station` is the weather-station story wrapped around it. Nothing in `Presence` knows what a temperature is.

---

## `src/PresenceBeacon.Demo/Program.cs`

Top-level statements, and the only file in the project that names a concrete type. Everything else takes its collaborators through an interface.

The file opens by declaring the two numbers that matter, because a reader's first question about any heartbeat system is "how often, and how long until it counts as gone?"

```csharp
var options = new BeaconOptions
{
    DirectoryPath = Path.Combine(Path.GetTempPath(), "presence-beacon-demo"),
    Ttl = TimeSpan.FromSeconds(6),
    HeartbeatInterval = TimeSpan.FromSeconds(2),
};

options.Validate();
```

A two-second heartbeat against a six-second TTL is the three-to-one ratio `01-the-pattern.md` recommends, compressed so the whole run finishes in well under a minute. `Validate()` runs here rather than being trusted to the store, so a bad configuration fails on the first line instead of on the first write.

The demo then deletes the directory if it survived a previous run. That is a concession to readability, and the comment in the source says so: a stale beacon from yesterday showing up as a ghost node is faithful behavior, just confusing on your first read.

Wiring is four lines. Note that the dashboard receives a reader and nothing else.

```csharp
var clock = SystemClock.Instance;
var store = new FileBeaconStore(options);

var dashboard = new StationDashboard(new BeaconReader(store, clock));
```

There is no list of expected nodes, no registration call, and no handle on any node object. The dashboard cannot ask a node how it is doing. It can only look at files.

Each node then gets its own linked cancellation source, which is what makes the demo's central move possible.

```csharp
var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
running.Add((node, stop, node.RunAsync(stop.Token)));
```

Cancelling one of those sources stops exactly one participant while its peers keep working. That happens after poll three:

```csharp
await quiet.Stop.CancelAsync();
```

That is the entire "node goes away" event. No file is removed, no peer is notified, and the dashboard is not told to expect a change. Everything you see afterward is derived from a timestamp aging.

The `finally` block cancels the rest, awaits every loop, disposes the sources, and unhooks the Ctrl+C handler. Worth reading once: it is the shape you want in any process that owns background loops, and it is also the reason the demo exits cleanly instead of leaving orphaned tasks writing to a temp directory.

---

## `src/PresenceBeacon.Demo/Presence/NodeId.cs`

A `readonly record struct` wrapping a string. It looks like ceremony until you remember that this value becomes a file name.

The invariant "exactly one beacon file path per participant" only holds if an id cannot contain a path separator, a dot, or anything else that would let it climb out of its directory or impersonate a scratch file. So the allowed set is narrow and explicit:

```csharp
private static bool IsAllowed(char character) =>
    character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';
```

That is narrower than the filesystem allows, on purpose. The source comment names the reason: dots, spaces and separators are all legal in a file name somewhere, and every one of them is a way for an id to escape or collide.

Validation lives in `Parse` and `TryParse` rather than in a constructor, which gives you one obvious place to look when an id is rejected. `Parse` throws with a message that states the rule; `TryParse` is the non-throwing counterpart and is what the JSON read path uses, since a malformed id on disk should drop one beacon rather than throw.

The struct's `Value` property returns `string.Empty` for a defaulted instance instead of `null`. That matters because a `record struct` can always be defaulted — `default(NodeId)` compiles — and downstream code that does `node.Value.Length` should not have to null-check. Instead, the places where a defaulted id would be harmful check for it explicitly:

```csharp
if (node.Value.Length == 0)
{
    throw new ArgumentException("A beacon path needs an id built through NodeId.Parse.", nameof(node));
}
```

That guard appears in `FileBeaconStore.PathFor` and again in the `BeaconWriter` constructor.

---

## `src/PresenceBeacon.Demo/Presence/Beacon.cs`

The heartbeat itself: a sealed record with four members and one method. This is the entire on-disk payload.

```csharp
public required NodeId Node { get; init; }
public required DateTimeOffset LastSeen { get; init; }
public required TimeSpan Ttl { get; init; }
public string? TaskHint { get; init; }
```

Three are `required` and one is not, which encodes what is load-bearing. `TaskHint` is free text for humans and nothing reads it to make a decision.

`Ttl` is a `TimeSpan` on the record and travels with it. A participant that checks in every minute can declare a five-minute TTL while a chatty one declares thirty seconds, and a reader honors both without being reconfigured.

Then the one rule, as a single expression on the record that owns the data:

```csharp
public bool IsLiveAt(DateTimeOffset now) => now - LastSeen <= Ttl;
```

Two properties of that line are easy to miss and both are tested at the boundary.

It is inclusive. A check-in landing exactly on its deadline still counts as live, so a beacon does not expire in the same tick it was judged fresh.

It reads a negative age as live. When two machines disagree about the clock, a beacon stamped slightly in the future belongs to a participant that is plainly working. Expiring it would be the wrong answer, and the subtraction gives you the right one for free.

---

## `src/PresenceBeacon.Demo/Presence/BeaconOptions.cs`

Three settings — directory, TTL, heartbeat interval — with defaults of two minutes and fifteen seconds. The defaults keep the recommended ratio without you thinking about it.

The interesting part is `Validate()`, and specifically the last check:

```csharp
if (HeartbeatInterval >= Ttl)
{
    throw new InvalidOperationException(
        $"HeartbeatInterval ({HeartbeatInterval}) must be shorter than Ttl ({Ttl}), otherwise a healthy participant is stale between every pair of check-ins.");
}
```

This is a configuration error caught at configuration time. The alternative is finding out at runtime, and the runtime symptom is nasty: a participant that flickers between live and stale while doing nothing wrong. That symptom looks like a bug in the reader, or in the store, or in the clock — it looks like a bug anywhere except in the setting that caused it. `FileBeaconStore` and `BeaconWriter` both call `Validate()` in their constructors so there is no way to build a working system around bad options.

The other three checks reject a blank directory, a non-positive TTL, and a non-positive interval, each with a message that says what would go wrong rather than just what was wrong.

---

## `src/PresenceBeacon.Demo/Presence/IClock.cs`

One property.

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

Liveness is entirely a statement about elapsed time. Without this seam, every expiry test has to sleep, and a test suite that sleeps is a test suite that is slow and flaky in the same breath. Every type that compares timestamps takes an `IClock`.

## `src/PresenceBeacon.Demo/Presence/SystemClock.cs`

Real UTC, no adjustment, exposed as a shared `Instance` because the type holds no state. Six lines. It exists so that `IClock` has a production implementation and `Program.cs` has something to pass.

---

## `src/PresenceBeacon.Demo/Presence/IBeaconStore.cs`

Storage with no opinion about what "live" means. Three methods: `SaveAsync` for one beacon, `LoadAllAsync` for every beacon, `LoadAsync` for one participant by id.

Two guarantees are documented on the interface rather than left to each implementation: a replace is atomic from a reader's point of view, and a write only ever touches the calling participant's own record.

The doc comment on `LoadAllAsync` is worth quoting, because it makes a behavior part of the contract instead of an accident of the file implementation:

```csharp
/// Reads every stored beacon. A missing directory yields an empty sequence rather than
/// an error, because "nobody has checked in yet" is a normal state.
```

Notice what is absent: there is no `Delete`. The invariant "writers never delete another participant's beacon" is not a rule someone has to remember, because the abstraction offers no way to break it.

---

## `src/PresenceBeacon.Demo/Presence/FileBeaconStore.cs`

The longest file in the project, and the one where the concurrency claims are actually cashed. Everything here is about two participants writing at the same time as a third reads.

**One path per participant.** The map from id to path is a pure function: validated id, plus a fixed suffix, combined with the configured directory.

```csharp
private const string FileSuffix = ".beacon.json";

public string PathFor(NodeId node) =>
    Path.Combine(_directory, node.Value + FileSuffix);
```

`ridge-01` becomes `ridge-01.beacon.json`. Two participants cannot collide because two distinct ids cannot produce one path, and an id cannot contain a separator because `NodeId` refused it.

**Writes are a two-step replace.** Serialize to a scratch file beside the target, then move it into place.

```csharp
var scratch = destination + ".tmp-" + Guid.NewGuid().ToString("n")[..8];
var payload = JsonSerializer.SerializeToUtf8Bytes(BeaconDocument.From(beacon), SerializerOptions);

await File.WriteAllBytesAsync(scratch, payload, cancellationToken).ConfigureAwait(false);
await ReplaceAsync(scratch, destination, cancellationToken).ConfigureAwait(false);
```

The random suffix means two processes started under the same id still get separate scratch files and the last move simply wins. Writing in place with a truncating stream would give a reader a real chance of parsing an empty or partial file, and that failure is rare enough to survive code review and then find you in production.

**Replace, not move.** This is the sharpest detail in the file, and the comment explains why:

```csharp
if (File.Exists(destination))
{
    File.Replace(scratch, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
}
else
{
    File.Move(scratch, destination);
}
```

On Windows an overwriting move refuses to run while any handle on the destination is open, even a handle that shared delete access. A dashboard mid-poll would fail somebody's heartbeat. `File.Replace` honors that shared handle. The call is wrapped in a small retry — five attempts with a growing delay — because the destination can appear between the `Exists` check and the call, and backing off twenty milliseconds is cheaper than failing a check-in.

**Reads share aggressively.** The read side opens with `FileShare.ReadWrite | FileShare.Delete` so a reader that is parsing a file never blocks the writer that wants to move a new payload over it.

**Reads fail soft, per file.** Three `catch` filters, each with a different reason:

- `FileNotFoundException` / `DirectoryNotFoundException` → `null`. A participant that never started, or a file that vanished mid-read. Both are ordinary states.
- `IOException` / `UnauthorizedAccessException` / `JsonException` → `null`. One unreadable neighbor must not take down the whole roster.
- A missing directory in `LoadAllAsync` → `[]`, checked before enumeration and caught again during it in case the directory disappears underneath the loop.

**Enumeration filters by suffix in code, not by search pattern.** A small thing with a real bug behind it:

```csharp
if (!path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
{
    continue;
}
```

A `*.beacon.json` search pattern on Windows can also match longer extensions, which would sweep up the `.tmp-xxxxxxxx` scratch file of an in-flight write. Filtering after enumeration avoids that and also means unrelated files in the directory are ignored — the directory is not assumed to be exclusively yours.

**The wire shape is separate from the domain type.** `BeaconDocument` is a private nested record with `Node` as a plain `string?` and the TTL as `TtlSeconds`, a `double`.

```csharp
public Beacon? ToBeacon()
{
    if (!NodeId.TryParse(Node, out var node)) return null;
    if (!double.IsFinite(TtlSeconds) || TtlSeconds <= 0) return null;
    ...
}
```

That is the parse boundary. The file on disk is plain JSON that anything can read; the in-memory `Beacon` always has a validated id and a positive TTL. A hand-edited file with a nonsense id or a TTL of `NaN` becomes `null` here and is dropped by the caller, rather than becoming a `Beacon` that no code downstream expects.

---

## `src/PresenceBeacon.Demo/Presence/IBeaconWriter.cs`

The write half, and its shape is the invariant.

```csharp
public interface IBeaconWriter
{
    NodeId Node { get; }
    Task<Beacon> HeartbeatAsync(string? taskHint = null, CancellationToken cancellationToken = default);
}
```

The id is a property you read, not a parameter you pass. No call site can stamp a heartbeat under someone else's name, because no method accepts a name.

## `src/PresenceBeacon.Demo/Presence/BeaconWriter.cs`

The default implementation, and it is short. The constructor validates the options, rejects a defaulted id, and captures the identity once. `HeartbeatAsync` builds a record and saves it:

```csharp
var beacon = new Beacon
{
    Node = Node,
    LastSeen = _clock.UtcNow,
    Ttl = _ttl,
    TaskHint = string.IsNullOrWhiteSpace(taskHint) ? null : taskHint.Trim(),
};

await _store.SaveAsync(beacon, cancellationToken).ConfigureAwait(false);
return beacon;
```

Two decisions worth naming.

A blank or whitespace hint is stored as `null` rather than as an empty string, so "no hint" has exactly one representation on disk and the dashboard needs one check rather than two.

The writer holds no timer. Whoever owns the work loop decides when to call `HeartbeatAsync`. That keeps the heartbeat honest — it fires only while the participant is actually running, rather than from a background timer that would happily keep announcing a worker that had wedged. It also makes the writer trivially testable: call it, advance a fake clock, call it again.

The writer never reads. It cannot answer "who else is live?", and giving it that ability would invite callers to treat a beacon as a lock.

---

## `src/PresenceBeacon.Demo/Presence/BeaconStatus.cs`

Three values: `Missing`, `Stale`, `Live`.

`Missing` earns its place. "Never checked in" and "checked in, then went quiet" are different facts, and a coordinator that folded them together could not tell a crash from a cold start.

## `src/PresenceBeacon.Demo/Presence/NodePresence.cs`

One participant paired with its verdict, plus the beacon the verdict came from. `Beacon` is `null` exactly when `Status` is `Missing`, and the doc comment says so.

## `src/PresenceBeacon.Demo/Presence/PresenceSnapshot.cs`

The answer to "who is live right now?", frozen at one instant.

```csharp
public required DateTimeOffset ObservedAt { get; init; }
public required IReadOnlyList<NodePresence> Nodes { get; init; }

public IReadOnlyList<NodePresence> Live => WithStatus(BeaconStatus.Live);
public IReadOnlyList<NodePresence> Stale => WithStatus(BeaconStatus.Stale);
```

`ObservedAt` is on the snapshot rather than implied by "when you called it". That is what lets the dashboard print an age for every node against a single reference point, and what lets a test render a snapshot without owning a clock.

`Live` and `Stale` are computed views over `Nodes`, not separate collections. There is one list of facts and two filters over it, so the two can never disagree about who is in the roster.

`StatusOf` is the lookup, and its fall-through is the whole reason `Missing` exists:

```csharp
public BeaconStatus StatusOf(NodeId node)
{
    foreach (var entry in Nodes)
    {
        if (entry.Node.Equals(node)) return entry.Status;
    }

    return BeaconStatus.Missing;
}
```

Ask about a participant nobody has ever heard of and you get `Missing`, not `Stale` and not an exception.

## `src/PresenceBeacon.Demo/Presence/IBeaconReader.cs`

Two methods: `SnapshotAsync` for the whole directory, `StatusOfAsync` for one participant without materializing the rest.

The remarks carry a rule that has no code to enforce it, so it is stated plainly: readers never write and never clean up. A stale beacon is left where it is so the participant can revive itself by checking in again.

## `src/PresenceBeacon.Demo/Presence/BeaconReader.cs`

The default implementation, and the first line of `SnapshotAsync` is the one to read twice.

```csharp
var now = _clock.UtcNow;
var beacons = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
```

The clock is read once, before the load, and every beacon is judged against that single instant. Sampling `UtcNow` inside the loop would let two beacons with identical timestamps get different verdicts, which is the kind of defect that reproduces once a week and never under a debugger. There is a fake clock in the test project built specifically to catch it.

The classification itself is one ternary:

```csharp
Status = beacon.IsLiveAt(now) ? BeaconStatus.Live : BeaconStatus.Stale,
```

The rule lives on `Beacon`, not here. The reader applies each beacon's own TTL, so a participant that wants a longer grace period says so in its own record and every reader honors it without being reconfigured.

`StatusOfAsync` is the same logic for one id, with `null` from the store mapping to `Missing`.

---

## `src/PresenceBeacon.Demo/Station/Reading.cs`

An invented temperature sample: node, degrees Celsius, and a UTC timestamp. Readings are the work the demo pretends to do. They are never written to the beacon directory — a beacon says a node is alive, not what it measured.

## `src/PresenceBeacon.Demo/Station/ISensorNode.cs`

A simulated worker. `SampleOnceAsync` does one cycle; `RunAsync` repeats on the configured interval until cancelled.

The ordering in the doc comment is the design: the heartbeat follows the sample. A node that hangs mid-sample never reaches its check-in, which is exactly the signal the dashboard needs.

## `src/PresenceBeacon.Demo/Station/SensorNode.cs`

The demo's stand-in for a real worker. It owns a writer, a clock, an interval, and a seeded `Random`.

The constructor refuses a writer that speaks for somebody else:

```csharp
if (!writer.Node.Equals(id))
{
    throw new ArgumentException(
        $"Writer speaks for '{writer.Node}', so it cannot heartbeat for '{id}'.",
        nameof(writer));
}
```

Belt and braces on top of the writer's own captured id, and cheap.

The random seed is derived by hand from the id rather than taken from `string.GetHashCode`, because string hashing is randomized per process and the demo should look the same on two consecutive runs.

`SampleOnceAsync` does the two steps in the order that matters:

```csharp
await _writer
    .HeartbeatAsync(
        string.Create(CultureInfo.InvariantCulture, $"sampled {reading.Celsius:F1}C"),
        cancellationToken)
    .ConfigureAwait(false);
```

Sample first, check in second. The temperature goes into the task hint, which is how the dashboard ends up printing `sampled 12.4C` next to a live node.

`RunAsync` wraps a `PeriodicTimer` and swallows `OperationCanceledException`:

```csharp
catch (OperationCanceledException)
{
    // Going quiet is the whole point. Nothing is deleted, nothing is announced --
    // the last beacon simply stops being refreshed.
}
```

There is no shutdown beacon, no tombstone, and no farewell write. Stopping is the absence of an action. That is what makes the pattern work for a node that crashes as well as for one that exits cleanly — neither gets a chance to announce anything, and neither needs one.

## `src/PresenceBeacon.Demo/Station/StationDashboard.cs`

The read side of the demo, and a plain consumer of `IBeaconReader`. It has no channel to the nodes, no registry of expected nodes, and no way to stop one.

The class splits into two methods on purpose:

```csharp
public async Task<string> PollAsync(CancellationToken cancellationToken = default)
{
    var snapshot = await _reader.SnapshotAsync(cancellationToken).ConfigureAwait(false);
    return Render(snapshot);
}

public static string Render(PresenceSnapshot snapshot) { ... }
```

`Render` is static and takes a snapshot, so the formatting tests build a snapshot in memory and assert on the text without touching a directory. `PollAsync` is the thin part that goes to storage, and it has one test that only checks it went through the reader.

An empty directory renders as a sentence, not a warning:

```csharp
if (snapshot.Nodes.Count == 0)
{
    return $"{observed}Z  no beacons found";
}
```

Nodes are sorted by id with `StringComparer.Ordinal` so the output does not shuffle between polls, and the id column is padded to the widest id so the ages line up.

`FormatAge` clamps a negative age to zero:

```csharp
if (age < TimeSpan.Zero)
{
    age = TimeSpan.Zero;
}
```

Same clock-skew case `Beacon.IsLiveAt` handles by treating a future timestamp as live. Here it is a display concern: `-0.4s ago` would make a reader think something is broken when nothing is.

---

## The test project

`tests/PresenceBeacon.Demo.Tests` runs on xUnit v3, targets `net10.0`, and shares the demo's `TreatWarningsAsErrors` and `Nullable` settings. Ten test classes and four fakes.

```bash
dotnet test
```

### `Fakes/MutableClock.cs`

An `IClock` the test drives by hand, with an `Advance(TimeSpan)` method. Every expiry test moves this instead of sleeping, which is why the whole suite runs in seconds and why none of it is timing-dependent.

`AtStart()` returns a fixed arbitrary instant — 2031-03-14 09:00:00Z — so a failing assertion prints the same timestamp on every machine.

### `Fakes/TickingClock.cs`

A clock that jumps forward on every read and counts the reads. It exists to catch one specific bug: a reader that samples "now" per beacon rather than once per snapshot. Under this clock, that bug classifies two identical beacons differently, and `BeaconReaderTests.TheWholeSnapshotIsJudgedAgainstOneInstant` fails.

This is worth studying as a technique. Rather than asserting on the implementation, the test supplies a collaborator whose behavior makes the defect observable.

### `Fakes/InMemoryBeaconStore.cs`

A `Dictionary<string, Beacon>` behind `IBeaconStore`, keyed by id. Keying by id mirrors the real store's one-file-per-participant rule, so a writer that tried to stamp under a neighbor's name shows up here as a changed entry rather than a new one. It also exposes `SaveCount` and a sorted `Ids`, which is how the writer tests assert that repeated heartbeats update one record instead of accumulating.

The cancellation token is ignored deliberately, and the remarks say so: tests that want a cancelled write use the file store.

### `Fakes/TempDirectory.cs`

A scratch path under the system temp directory, disposed at the end of the test. The important line is in the doc comment: the directory is deliberately *not* created, because "the directory does not exist yet" is one of the states under test.

It exposes `BeaconFiles` (what the store would enumerate) separately from `AllFiles` (everything, including scratch files a write should have cleaned up). That split is what lets `WritesLeaveNoScratchFilesBehind` assert something real.

### `NodeIdTests.cs`

Nine tests, and the theory of rejected inputs reads like a threat model: `ridge 01`, `ridge.01`, `ridge/01`, `ridge\01`, `..`, `../../secrets`, `ridge:01`, `ridge*`, and `ridge-01.beacon.json`.

That last one is the subtle case. An id that already looks like a beacon file name would produce `ridge-01.beacon.json.beacon.json` — or, with a less careful path scheme, would let one participant write over another's file.

### `BeaconTests.cs`

The liveness rule, tested at the boundary tick rather than with comfortable margins: one tick before expiry is live, exactly at expiry is live, one tick after is not. Plus the two cases that are easy to get wrong — a check-in stamped in the future reads as live, and two beacons with different TTLs are each judged against their own.

### `BeaconOptionsTests.cs`

Rejection of blank directories, non-positive TTLs and intervals, and a heartbeat that is not shorter than the TTL — including the equal case, since `HeartbeatInterval == Ttl` means stale between every pair of check-ins. Two tests confirm that `FileBeaconStore` and `BeaconWriter` both validate in their constructors, so bad options cannot reach a running system.

### `BeaconWriterTests.cs`

Nine tests around one claim: a writer touches its own record and nothing else. `AWriterCannotStampUnderANeighboursName` and `TheNodeIdIsFixedForTheLifetimeOfTheWriter` pin the structural guarantee, not just the behavior — if someone later adds an id parameter to `HeartbeatAsync`, these are the tests that should stop them.

The rest cover the small decisions: the stamped time comes from the clock, the stamped TTL comes from the options, the returned beacon is exactly what was stored, a blank hint becomes `null`, and a padded hint is trimmed.

### `BeaconReaderTests.cs`

Eleven tests. The TTL boundary again, this time through the reader. `EachBeaconIsJudgedAgainstItsOwnTtl` proves the reader is not applying its own configuration. `TheWholeSnapshotIsJudgedAgainstOneInstant` is the `TickingClock` test. `AMissingDirectoryReadsAsAnEmptySnapshot` and `AnUnknownNodeIsMissingRatherThanStale` pin the fail-soft contract, and `ReadingLeavesTheStoreUntouched` pins the no-cleanup rule.

`OneNodeGoingQuietDoesNotAffectTheOthers` is the pattern's headline claim at unit scale.

### `PresenceSnapshotTests.cs`

Five short tests: `Live` and `Stale` partition the roster, an empty snapshot is a valid answer rather than an error, `StatusOf` finds a recorded node, returns `Missing` for an absent one, and is case-sensitive — matching `NodeId`, where `Ridge-01` and `ridge-01` are two different participants.

### `FileBeaconStoreTests.cs`

Twenty tests, and the densest file in the suite. These touch a real temporary directory on purpose; an in-memory double would prove nothing about atomic replace or directory enumeration.

The groups map onto the invariants:

- **One path per participant.** `PathForIsStableAcrossCalls`, `DifferentNodesGetDifferentPaths`, `PathForStaysInsideTheConfiguredDirectory`, and `RepeatedCheckInsLeaveExactlyOneFile`.
- **No cross-participant interference.** `SavingOneNodeLeavesOtherNodesFilesByteForByteUntouched` reads the neighbors' bytes before and after and compares them. `AStaleBeaconIsNeverCleanedUpByAReader` confirms nothing gets swept.
- **No corruption under concurrency.** `ReadingTheRosterWhileNodesCheckInNeverYieldsATornRecord` runs six participants writing forty beacons each while a loop reads the roster continuously, asserting every beacon that comes back is well-formed. It also asserts it managed at least one read during the writes, so the test cannot quietly pass by never overlapping.
- **Readers do not block writers.** `AReaderHoldingAFileOpenDoesNotBlockThatNodesNextCheckIn` holds an open `FileStream` on a beacon and then writes it. This is the test that fails if `File.Replace` is swapped back to an overwriting `File.Move`.
- **Missing directory reads as empty.** Four tests, including `ReadingDoesNotCreateTheDirectory` — a read is not allowed to have side effects — and `TheFirstWriteCreatesTheDirectory`.
- **Bad input drops one beacon, not the roster.** `UnrelatedFilesAndInFlightScratchFilesAreIgnored` and `OneUnreadableNeighbourDoesNotTakeDownTheRoster`, the latter a theory over several malformed payloads.

`ARoundTripPreservesEveryField` is the plain serialization test that makes the rest meaningful.

### `SensorNodeTests.cs`

Seven tests over the demo worker: sampling also checks in, the check-in carries a hint about the work just done, readings stay in plausible bounds, two nodes sharing a store keep separate records, and a node cannot borrow another node's writer.

`StoppingANodeIsQuietAndLeavesItsLastCheckInInPlace` is the one that matters for the story. Stopping produces no write, no delete, and no announcement.

### `StationDashboardTests.cs`

Nine tests on rendering. An empty directory is a plain statement, each node gets a line with a status, the order is stable, a hint is shown when recorded, a skewed timestamp never prints a negative age, and the age unit switches from seconds to minutes at the one-minute mark. Only `PollingGoesThroughTheReader` is async — everything else calls the static `Render`.

### `PresenceScenarioTests.cs`

Three end-to-end tests against the real file store, the real dashboard, and a temp directory. This is where every invariant meets at once.

`AQuietNodeFlipsToStaleWhileItsPeersKeepReporting` is the dossier's demo story as an assertion. Three nodes report, then one stops and the clock advances eight heartbeats while its peers keep sampling. The checks at the end are the whole point:

```csharp
Assert.Equal(BeaconStatus.Live, afterExpiry.StatusOf(ridge.Id));
Assert.Equal(BeaconStatus.Live, afterExpiry.StatusOf(meadow.Id));
Assert.Equal(BeaconStatus.Stale, afterExpiry.StatusOf(summit.Id));

Assert.Equal(3, directory.BeaconFiles.Count);
Assert.True(File.Exists(store.PathFor(summit.Id)));
```

Three files on disk, still, including the quiet node's. The verdict changed; the directory did not.

`AReturningNodeRevivesByCheckingInAgain` covers the `Stale → Live` edge. Nothing has to be repaired or re-registered — writing a fresh beacon is the entire recovery path, and there is still exactly one file afterward.

`ManyNodesHeartbeatingAtOnceProduceOneFileEachAndOneCoherentRoster` runs six nodes through `Parallel.ForEachAsync`, fifteen beats each, and asserts one file per node and one coherent roster at the end.

---

## Project and solution files

`src/PresenceBeacon.Demo/PresenceBeacon.Demo.csproj` targets `net10.0` with `Nullable` enabled, `ImplicitUsings` on, `TreatWarningsAsErrors` on, and `InvariantGlobalization` on. No package references at all — the pattern needs `System.Text.Json` and `System.IO`, both in the shared framework.

`tests/PresenceBeacon.Demo.Tests/PresenceBeacon.Demo.Tests.csproj` carries the same settings plus `xunit.v3`, `xunit.runner.visualstudio`, and `Microsoft.NET.Test.Sdk`, and a project reference to the demo.

`PresenceBeacon.sln` ties the two together so `dotnet build` and `dotnet test` at the root do the obvious thing.

`samples/` is reserved for invented data files. The demo generates its own readings at runtime and the tests build their own fixtures, so nothing there is required to run either.

## Where to go next

[04-tradeoffs.md](04-tradeoffs.md) is the honest accounting: what a TTL costs you, where clock skew bites, and the cases where this pattern is the wrong tool.

[05-extending.md](05-extending.md) takes the seams you just read — `IBeaconStore`, `IClock`, `IBeaconWriter`, `IBeaconReader` — and shows what you can swap behind them without touching the liveness rule.
