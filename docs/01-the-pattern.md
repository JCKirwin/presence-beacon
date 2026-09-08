# 01 — The Pattern

This section exists to explain the presence beacon before you look at any code. By the end of it you should be able to describe the pattern to a colleague, state the one rule that decides whether a peer is live, and know the three or four ways people get it wrong. Everything else in this repository (the architecture notes, the walkthrough, the demo app) assumes you have read this page.

## The situation

You have several processes doing the same kind of work at the same time. They share a filesystem. They do not share a database, a message broker, or a coordination service.

In this repository those processes are weather sensor nodes. Each node collects temperature readings on its own schedule and writes them somewhere. A dashboard process wants to answer one question:

> Which nodes are collecting right now?

That question is harder than it looks. A node that has published nothing in the last minute might be:

- still running, just between readings
- crashed
- finished and shut down cleanly
- never started at all

From the outside, silence looks the same in all four cases. The dashboard cannot tell "quiet" from "gone". So it does one of two unhelpful things: it waits forever for a node that will never report, or it assumes the node is dead and hands its work to someone else while the original node is still busy.

## The idea

Each worker periodically writes a small file that says "I am alive, and here is when I said so." Each file carries an expiry. Readers treat any file older than its expiry as if it were not there.

That is the whole pattern. It is a heartbeat, written to disk instead of sent over a wire.

The important move is putting the expiry *in the data*. The writer decides how long its own claim of liveness should be believed. The reader does not need a configuration file, a shared constant, or an agreement negotiated in advance. It reads the beacon, reads the TTL the beacon carries, compares against its own clock, and decides.

## The shape of a beacon

A beacon is a JSON document with four fields. This is the record the demo writes, and it is deliberately small.

```csharp
public sealed record Beacon(
    string HarnessId,       // who is reporting: "sensor-north-ridge"
    DateTimeOffset LastSeen,// when they last reported, in UTC
    string? TaskHint,       // optional: what they were doing
    int TtlMinutes);        // how long this report should be believed
```

`HarnessId` is the identity of the worker. In the demo it is a sensor node name. It is also the filename, which is what makes the "one file per worker" invariant fall out for free rather than needing enforcement.

`LastSeen` is a UTC timestamp. Not local time. A beacon written in one timezone and read in another must still compare correctly, and daylight-saving transitions must not create an hour where every worker looks either dead or immortal.

`TaskHint` is free text and purely informational. The dashboard can print "collecting" or "calibrating" next to a live node. Nothing in the liveness decision reads this field.

`TtlMinutes` is the writer's own statement about how stale its report is allowed to get. A node that reports every five seconds can set a short TTL. A node that reports every two minutes needs a longer one. Because the value travels with the beacon, nodes with different rhythms can share a directory without agreeing on anything.

## The one rule

Everything about reading beacons reduces to a single comparison. This is the function the dashboard calls, and it is the only place liveness is defined.

```csharp
public static bool IsLive(Beacon beacon, DateTimeOffset now) =>
    now - beacon.LastSeen <= TimeSpan.FromMinutes(beacon.TtlMinutes);
```

Read it as: a beacon is live if and only if the elapsed time since its last report is within its own TTL.

Note what is *not* in that expression. There is no process id. No handle. No lock. No check that the file is still open. The reader never asks the operating system whether the writing process exists. It only asks whether the writer said something recently enough.

This is the pattern's core trade. You give up certainty and you get a check that costs one file read and one subtraction, works across process boundaries, survives the reader restarting, and cannot deadlock.

## Two operations, and only two

The pattern has exactly two verbs.

**Write.** A worker replaces its own beacon file with a fresh one, on a timer, for as long as it is doing work. The replacement is atomic: write to a temporary file, then move it over the target. A reader that arrives mid-write sees either the old complete beacon or the new complete beacon, never half of either.

**Read.** A caller lists the beacon directory, parses each file, and partitions the results into live and stale using the rule above.

The read side needs one behavior spelled out, because it is the difference between a liveness check and an outage.

```csharp
// A missing directory means "nobody has reported yet",
// which is an empty result, not a failure.
if (!Directory.Exists(_beaconDirectory))
    return new PresenceSnapshot(Live: [], Stale: []);
```

The first time this system runs, the directory does not exist. If reading throws there, every caller has to wrap the check in a try/catch, and one of them will forget. An empty snapshot is the honest answer: no beacons, therefore no live workers.

Malformed files deserve the same treatment. A beacon that fails to parse is one worker's problem. If it becomes an exception that aborts the whole scan, one corrupt file makes the other nine nodes invisible. Skip it, count it as stale, keep going.

## What the pattern promises

These are the invariants. The demo's test suite exists to hold them.

1. **One beacon file per worker.** The id maps to exactly one path in the directory.
2. **Liveness is TTL-relative.** `now - lastSeen <= ttl` decides, and nothing else does.
3. **Writers touch only their own file.** A worker never deletes, moves, or rewrites another worker's beacon, including beacons it believes to be stale.
4. **Concurrent writes to different files do not interfere.** Two nodes writing at the same instant produce two intact beacons.
5. **A missing directory reads as empty.** No exception escapes to the caller.

Invariant 3 is the one that gets violated first, usually with good intentions. Someone notices stale beacons accumulating and adds cleanup: while scanning, delete anything past its TTL. Then a node comes back from a long garbage-collection pause, writes its next beacon, and discovers a reader deleted the file it was about to update. Worse, two readers race on the delete and one gets a `FileNotFoundException` from a path it just enumerated. Let stale beacons sit. They are a few hundred bytes, and they are also a record of who was here.

## What the pattern does not promise

Be precise about this, because the failure mode of a presence beacon is someone treating it as a lock.

**It is not mutual exclusion.** A live beacon says a worker reported recently. It does not say that worker owns anything. If you need "only one process may do X," you need a claim or lease with compare-and-swap semantics on the acquisition. That is a different pattern with different, harder guarantees. Presence answers "who is around"; a claim answers "who may proceed."

**It is not authenticated.** The harness id is a string a process wrote about itself. Any process that can write to the directory can write any id. Filesystem permissions are the security boundary. The beacon format is not.

**It is not networked.** Beacons live on a filesystem all participants can see. That can be a local directory or a share, but there is no broadcast, no discovery protocol, and no gossip. If your workers cannot see a common path, this pattern does not reach them.

**Liveness is approximate, and knowingly so.** A worker can die one second after writing a beacon with a five-minute TTL, and for the rest of those five minutes readers will call it live. Shortening the TTL narrows that window and increases write traffic. There is no setting that closes it. The window is the price of not having a coordinator.

## Choosing a TTL

The one number you have to pick. The rule of thumb is to set the TTL to a small multiple of the write interval, typically three.

Write every 10 seconds, expire after 30. That tolerates two consecutive missed writes, which covers a slow disk, a scheduling hiccup, or a brief pause, without stretching the false-live window past half a minute.

Pushing the multiple to 1 means any single delayed write makes a healthy node look dead, and you will chase phantom failures. Pushing it to 20 means a crashed node stays "live" for several minutes and whatever depends on that answer stays wrong for just as long. Three is a starting point, not a law. Move it based on which error costs you more: believing a dead node is alive, or believing a live node is dead.

## When to reach for this

Use a presence beacon when all of these hold:

- Several processes work in parallel and share a filesystem.
- You want to know who is currently participating.
- An occasionally wrong answer is acceptable, and the cost of being wrong is a retry or a duplicated effort, not corruption.
- Standing up a coordination service would cost more than the problem is worth.

Look elsewhere when any of these hold:

- Correctness depends on exactly one process acting. Use a lease or a lock.
- The processes cannot see a shared path. Use something with a network in it.
- You need an audit trail of who did what. Beacons are overwritten by design; they are current state, not history.

## Where to go next

[02-architecture.md](02-architecture.md) turns this into types and boundaries: the writer, the reader, the clock seam that makes TTL expiry testable without waiting for real time to pass.

[03-walkthrough.md](03-walkthrough.md) runs the demo. Three weather nodes report, one stops, and you watch it flip from live to stale while the other two carry on untouched.
