# Presence Beacon

Three weather-station nodes are writing readings to a shared folder, and you need to know which ones are still alive right now. Presence Beacon is a small, dependency-free C# pattern where every worker drops a JSON heartbeat file and every reader decides liveness from a timestamp and a TTL — no server, no lock, no registry.

## What you'll learn

- How to model an advisory liveness signal as one JSON file per worker (harness id, last-seen UTC, task hint, TTL).
- Why `now - lastSeen <= ttl` is the whole liveness rule, and what that buys you over "the file exists".
- How to replace a beacon atomically so a reader never sees a half-written file.
- Why a writer must never delete a peer's beacon, and how expiry does that job instead.
- Where this pattern stops: it tells you who is live, not who owns what — mutual exclusion is a different problem.

## Quick Start

```bash
git clone https://github.com/JCKirwin/presence-beacon.git
cd presence-beacon

dotnet build
dotnet test

# Simulates three sensor nodes writing beacons while a dashboard polls them.
# One node stops writing partway through; watch it flip from live to stale.
dotnet run --project src/PresenceBeacon.Demo
```

You'll see the dashboard list all three nodes as live, then report the silent one as stale once its TTL elapses — while the other two beacon files are left untouched.

## Where to go next

| Doc | What it covers |
|---|---|
| [docs/01-the-pattern.md](docs/01-the-pattern.md) | The heartbeat idea and the problem it solves |
| [docs/02-architecture.md](docs/02-architecture.md) | Writer, reader, and the file layout between them |
| [docs/03-walkthrough.md](docs/03-walkthrough.md) | The demo run, line by line |
| [docs/04-tradeoffs.md](docs/04-tradeoffs.md) | Clock skew, TTL tuning, and what this pattern is not |
| [docs/05-extending.md](docs/05-extending.md) | Adding task hints, richer queries, and other transports |

## License

MIT — see [LICENSE](LICENSE).
