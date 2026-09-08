# Trade-offs

This section exists because every choice in the presence beacon pattern buys one property at the cost of another, and the costs are not obvious until a node dies at the wrong moment. The earlier sections showed you what the pattern does; this one shows you what it refuses to do, why each refusal was deliberate, and which of those refusals you should revisit when your own workload does not look like three weather sensors on one machine.

Read this before you copy the pattern into production. Most of the failure reports you would otherwise write yourself are already listed below.

---

## 1. Advisory liveness, not mutual exclusion

**The trade:** you get a cheap, lock-free answer to "who is working right now?" You do not get any guarantee that two nodes will avoid the same work.

A beacon says *I was alive at 14:32:07 UTC and I expect to still be alive for another 90 seconds.* That is a claim about the past plus a hint about the future. It is not a reservation. Two sensor nodes can both be live, both see each other as live, and both decide to sample the same rain gauge.

The temptation is to close that gap by making the beacon do double duty — write the task hint, then treat "I saw no other beacon naming this task" as permission to proceed. That is a lock built on a read-then-act race, and it fails exactly when you need it most: two nodes starting within the same polling interval each read a directory that does not yet contain the other's file.

If you need exclusion, you need a separate mechanism with a real atomic test-and-set — an exclusive-create file open, a database row with a unique constraint, a lease service. Keep it beside the beacon rather than inside it. Liveness and ownership answer different questions and expire on different clocks; merging them means every lock acquisition inherits the beacon's tolerance for stale reads.

**When to revisit:** never, for the beacon itself. Add the lock as a peer component.

---

## 2. TTL is a guess about the future, and both directions hurt

A beacon's TTL is the writer's promise about its own refresh cadence. Readers turn that promise into a boolean. The promise can be wrong in two ways.

**TTL too short.** A node that pauses for a long sample, a slow disk flush, or a garbage-collection pause misses its refresh window and is declared dead while it is still working. The dashboard shows a false absence. Anything that reacts to absence — reassigning work, alerting an operator, scaling up a replacement — now acts on a lie.

**TTL too long.** A node that crashes outright stays "live" for the remainder of its TTL. Coordinators wait on a corpse. The system's reaction time to a real failure is bounded below by the TTL, not by how fast you poll.

There is no correct value, only a ratio. The rule that survives contact with real systems is to refresh several times per TTL:

```
refreshInterval = TTL / 3
```

Three refreshes per TTL means a node must miss three consecutive heartbeats before a reader gives up on it, which absorbs a single slow cycle without a false death, while still keeping detection latency to roughly one TTL. Two is too tight — one hiccup and you are wrong. Ten is wasteful: you pay ten writes for detection latency you could have had with three.

Pick the TTL from the reaction you want, then derive the interval:

| You want to notice a dead node within… | TTL | Refresh every… |
|---|---|---|
| ~15 seconds | 15s | 5s |
| ~90 seconds | 90s | 30s |
| ~5 minutes | 5m | 100s |

**When to revisit:** when your work units are longer than your TTL. A node that runs a 10-minute computation between heartbeats cannot honor a 90-second TTL from its main loop. Either refresh from a background timer that is independent of the work, or raise the TTL to exceed your worst-case unit of work. Do not refresh a beacon from inside the loop you are trying to monitor.

---

## 3. TTL travels with the beacon, not with the reader

The beacon file carries its own TTL. A reader could instead impose one policy on everyone — "anything older than 60 seconds is dead" — and the code would be simpler.

Per-beacon TTL wins because heterogeneous nodes are the normal case, not the exception. A fast sensor polling every two seconds and a slow batch node checkpointing every five minutes are both legitimate, and a single reader-side threshold cannot be right for both. Push the policy to the writer, which is the only party that knows its own cadence.

The cost is that a writer can lie. A node that sets a one-hour TTL and then dies is unkillable in the reader's eyes for an hour. Since nothing authenticates the writer (see §7), nothing prevents this.

If you run untrusted or sloppy writers, clamp on read rather than trusting the file:

> This block exists to show the clamp — the reader honors the writer's TTL only within a range it is willing to accept.

```csharp
var effectiveTtl = beacon.Ttl < MinTtl ? MinTtl
                 : beacon.Ttl > MaxTtl ? MaxTtl
                 : beacon.Ttl;

var isLive = now - beacon.LastSeenUtc <= effectiveTtl;
```

**When to revisit:** as soon as beacons are written by code you do not control.

---

## 4. Clock skew is the failure mode you will not see in the demo

Every beacon comparison subtracts a timestamp written by one process from a timestamp read by another. On one machine those clocks are the same clock and the pattern looks flawless. Spread the nodes across hosts and the subtraction spans two clocks that disagree.

The asymmetry matters:

- A writer whose clock runs **fast** stamps beacons in the reader's future. `now - lastSeen` goes negative, the node looks permanently live, and a crash is invisible until the skew is exhausted.
- A writer whose clock runs **slow** stamps beacons in the reader's past. The node burns part of its TTL before the file is even written, and dies early under load.

Three mitigations, in order of how much you should like them:

1. **Run NTP.** Sub-second skew makes the problem disappear for any TTL measured in tens of seconds. This is the real answer.
2. **Size the TTL to absorb the skew.** If hosts can drift five seconds, a 90-second TTL is fine and a 6-second TTL is not.
3. **Use the file's own modification time instead of the embedded timestamp.** This moves the clock question to the filesystem, which is one clock rather than N — but it only helps if all nodes share that filesystem, and it breaks the moment a sync tool or a restore rewrites mtimes.

Keep the embedded timestamp regardless. It is what makes a beacon readable, debuggable, and portable across transports; mtime is at best a cross-check.

**When to revisit:** the first time a node moves off the machine that reads the beacons.

---

## 5. One file per harness, replaced atomically

Two structural choices, made together, and they are why concurrent writers do not corrupt each other.

**One file per harness id.** The alternative — a single shared registry file that every node updates — turns every heartbeat into a read-modify-write over shared state. Now you need a lock to write a liveness signal, which is precisely the dependency the pattern was supposed to remove. With one file per id, writers never touch the same bytes, so there is nothing to coordinate. The filesystem's own directory semantics do the partitioning for you.

The cost is directory scale. Reads are `O(nodes)`, and a directory that accumulates beacons from thousands of short-lived workers gets slow to enumerate long before it gets large on disk. Stale files also linger: nothing deletes them, because nothing is allowed to (see §6).

**Atomic replace, not in-place rewrite.** Truncating a beacon and rewriting it leaves a window in which a reader sees zero bytes or half a JSON object. That window is short and therefore easy to miss in testing and guaranteed to appear in production. Writing to a temporary file in the same directory and then replacing the target closes it: a reader sees either the old complete beacon or the new complete beacon, never a partial one.

> This block exists to show the shape of the write — same directory, then replace.

```csharp
var tempPath = $"{beaconPath}.{Guid.NewGuid():N}.tmp";
await File.WriteAllTextAsync(tempPath, json, ct);
File.Move(tempPath, beaconPath, overwrite: true);
```

The temporary file must live in the same directory as its target. Across a volume boundary the move degrades to copy-then-delete and stops being atomic — reintroducing the exact window you paid for the temp file to avoid.

**When to revisit:** at thousands of concurrent nodes, where per-file enumeration cost starts to dominate and a real registry earns its keep.

---

## 6. Writers never delete other writers' beacons

A reader that finds a beacon two hours past its TTL knows the file is garbage. It is still not allowed to remove it.

The reason is that "expired" is a conclusion drawn from a clock the reader does not own. A reader with a fast clock, or a reader that trips over the skew in §4, will confidently delete a beacon belonging to a node that is alive and working. The node's next refresh recreates the file, so the damage is transient — but during the gap the node is invisible, and any coordinator that reacts to absence has already acted.

Refusing to delete makes the pattern monotone in a useful way: the only process that can remove a harness from the directory is the harness itself. A wrong liveness verdict stays a wrong verdict and does not become a wrong mutation.

The cost is accumulation. The directory is append-mostly, and dead nodes leave permanent residue.

Sweep it from outside the pattern, on a much longer horizon than the TTL — a scheduled job that removes beacons older than, say, twenty times their TTL. At that age the "it might still be alive" argument no longer holds, and the sweeper is a single, auditable actor rather than every reader in the system.

**When to revisit:** when the beacon directory becomes an operational nuisance. Fix it with a sweeper, not by relaxing the rule.

---

## 7. The harness id is a claim, not an identity

Nothing authenticates a beacon. Any process that can write to the directory can write a file named for any harness id and assert anything about its own liveness.

This is a deliberate scope cut, and it is defensible for the case the pattern targets: cooperating processes on a shared filesystem inside one trust boundary. Adding signatures would mean key distribution, rotation, and verification cost on every read — a large amount of machinery to defend against a peer that, by construction, can already delete the entire directory.

Be explicit about what this means:

- Beacon data is **not** an authorization input. Never gate a privileged action on "harness `collector-3` says it is live."
- Filesystem permissions are the whole security model. If the directory is writable by something you do not trust, the beacons are worthless.
- A buggy node with a copy-pasted id silently impersonates its twin. Two processes sharing one harness id produce one file that both refresh, so the pair looks like a single healthy node — and stays "live" until both die. Derive ids from something naturally unique, and treat a duplicate as a configuration bug rather than something the reader can detect.

**When to revisit:** the moment beacons cross a trust boundary. At that point you do not want a harder beacon; you want a service with authentication.

---

## 8. Polling, with no push

Readers ask "who is live?" by enumerating the directory on demand. Nobody is notified when a beacon appears or expires.

Polling is the right default here because the interesting event — expiry — is not a filesystem event at all. No write occurs when a node dies; the beacon simply stops changing. A watcher would deliver notifications for every heartbeat you do not care about and stay silent for the one transition you do.

Polling also degrades honestly. A missed poll costs latency and nothing else, whereas filesystem watchers drop events under load, behave differently across platforms, and need a periodic reconciliation pass anyway — which is polling, arrived at by a longer route.

The cost is detection granularity: expiry is noticed at most one poll interval late, on top of the TTL itself. Worst-case detection latency is `TTL + pollInterval`. Budget for both.

**When to revisit:** when you want to react to a node *appearing* within milliseconds. Add a watcher for arrivals and keep polling for expiry. Do not replace the poll.

---

## 9. A missing directory reads as "nobody is live"

A reader pointed at a directory that does not exist returns an empty live set. It does not throw, and it does not create the directory.

This makes readers safe to start first. A dashboard that boots before any sensor node has ever run reports "no live nodes," which is both true and useful. The alternative — an exception that aborts the caller — turns a normal cold-start ordering into a crash, and pushes every caller into writing the same try/catch.

The cost is a real diagnostic hazard: a typo in the configured path is indistinguishable from a healthy, idle system. Both report zero live nodes, forever, with no error.

Do not fix this by throwing. Fix it by making the two cases distinguishable to a human — log the resolved absolute path once at startup, and surface it in whatever the reader renders. An operator who can see the path the reader is watching will spot the typo in seconds; one who sees only "0 live" will not.

**When to revisit:** never for the return value; always for the observability around it.

---

## 10. Files on a shared filesystem, and what that assumes

The pattern's transport is a directory. That buys a lot: no server to run, no port to open, no protocol version to negotiate, and a debugging story that consists of `cat`. It also inherits every property of the filesystem underneath it.

What the pattern relies on:

- **Atomic same-directory replace.** Holds on local NTFS and on local POSIX filesystems. Verify it before trusting it on anything else.
- **Reads that see completed writes promptly.** Local filesystems give you this. Network filesystems with client-side attribute caching may not: a reader can serve a beacon from cache and declare a live node dead. If your TTL is shorter than the cache lifetime, you have a bug you will diagnose as clock skew.
- **A directory that all participants agree on.** Not a per-host path that happens to have the same name on each host — that is N independent directories, and every node will report that it is the only one alive.

Object stores are a poor fit. They typically offer no atomic replace and no directory semantics, and listing is eventually consistent. Do not adapt this pattern to them; pick a transport that was designed for the job.

**When to revisit:** when nodes span hosts. At that point re-verify atomicity and cache behavior on the actual shared filesystem, or move to a coordination service.

---

## Summary

| Choice | You gain | You give up | Revisit when |
|---|---|---|---|
| Advisory only | No lock, no server, no deadlock | Any exclusion guarantee | Never — add a lock beside it |
| TTL in the beacon | Heterogeneous cadences coexist | A writer can lie about its TTL | Untrusted writers → clamp on read |
| Refresh at TTL/3 | Absorbs one slow cycle | 3 writes per detection window | Work units exceed the TTL |
| One file per harness | Writers never contend | `O(n)` reads, files accumulate | Thousands of nodes |
| Atomic replace | No torn reads | A temp file per write | Never |
| No peer deletion | A wrong verdict stays inert | Dead beacons linger | Add an external sweeper |
| Id is unauthenticated | No key management | Spoofing, silent id collisions | Beacons cross a trust boundary |
| Polling | Detects expiry, degrades honestly | `TTL + pollInterval` latency | Need instant arrival detection |
| Missing dir → empty | Readers start before writers | A typo looks like an idle system | Never — log the resolved path |
| Filesystem transport | No infrastructure | Inherits FS atomicity + caching | Nodes span hosts |

---

Next: [05-extending.md](05-extending.md) walks through the changes that are safe to make — new beacon fields, alternative stores, richer task hints — and the invariants that must survive all of them.
