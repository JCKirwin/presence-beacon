# Contributing

Thanks for looking at Presence Beacon. This repository is a teaching reference: it exists so that a
.NET engineer can read one small, complete implementation of the presence-beacon pattern and walk
away able to build their own. That goal shapes what a good contribution looks like here, so please
read this page before you open a pull request.

## What this project is (and is not)

- It **is** a single self-contained demo: a weather-station console app whose sensor nodes write
  heartbeat files, plus a dashboard that reports which nodes are live.
- It **is** a place for clearer explanations, sharper tests, and small illustrative extensions.
- It is **not** a NuGet library. There is no published package and no API stability promise.
- It is **not** a distributed-locking system. Beacons are advisory liveness only. Pull requests that
  add claim, lease, or mutual-exclusion semantics are out of scope — see `docs/04-tradeoffs.md`.

If you want the pattern to do something it deliberately does not do, open an issue describing the
use case. The answer may be "fork it", and that is a fine answer for a reference project.

## Prerequisites

- .NET SDK 10.0 or newer (`dotnet --version`).
- Any editor. The solution file `PresenceBeacon.sln` opens in Visual Studio, Rider, or VS Code.

No database, container, or cloud account is needed. The demo writes files to a temporary directory
and cleans up after itself.

## Build and test

Run these from the repository root before you open a pull request:

```bash
dotnet build PresenceBeacon.sln
dotnet test PresenceBeacon.sln
dotnet run --project src/PresenceBeacon.Demo
```

The build treats warnings as errors and has nullable reference types enabled. If the compiler
complains, fix the code rather than suppressing the diagnostic — a suppression in a teaching
repository teaches the wrong thing. If you believe a suppression is genuinely correct, add it with a
one-line comment explaining why.

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/PresenceBeacon.Demo/Presence/` | The pattern itself: beacon record, options, store, writer, reader, clock abstraction. |
| `src/PresenceBeacon.Demo/Station/` | The weather-station demo domain: sensor nodes, readings, dashboard. |
| `src/PresenceBeacon.Demo/Program.cs` | The runnable story that ties the two together. |
| `tests/PresenceBeacon.Demo.Tests/` | xUnit v3 tests, including fakes for the clock and the store. |
| `docs/` | Numbered guides, read in order, plus `docs/adr/` for decision records. |
| `samples/demo-data.json` | Invented sample data. Tests may read it, but unit tests must not require it. |

Keep the split between `Presence/` and `Station/` intact. The point of the layout is that a reader
can see at a glance which types are the pattern and which types are the story wrapped around it.

## Invariants a change must preserve

These are the rules the tests encode. If your change breaks one, it changes the pattern rather than
improving the demo, and it needs an issue and an ADR first:

1. Each node id maps to exactly one beacon file path inside the configured directory.
2. A beacon is live if and only if `now - lastSeen <= ttl`.
3. A writer never deletes or rewrites another id's beacon file.
4. Concurrent writes to different ids do not corrupt each other, and a single beacon file is
   replaced atomically rather than half-written.
5. Reading a directory that does not exist yields an empty live set, not an exception that kills the
   caller.

## Code style

- Target `net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Follow the conventions already in the files you touch: file-scoped namespaces, `sealed` by
  default, explicit accessibility on every type.
- Time comes from `IClock`. Never call `DateTimeOffset.UtcNow` or `DateTime.Now` outside
  `SystemClock`, or the tests lose their ability to move the clock.
- File-system access goes through `IBeaconStore`. That indirection is what lets tests run in memory.
- Comments explain *why*, not *what*. A comment restating the line below it is noise. A comment
  explaining a race, a rounding rule, or a deliberate omission earns its place.
- Public types in `Presence/` carry XML doc comments, because they are the part readers copy.

## Tests

- xUnit v3. Name tests `MethodOrScenario_Condition_ExpectedResult`.
- Prefer the existing fakes (`InMemoryBeaconStore`, `MutableClock`, `TickingClock`, `TempDirectory`)
  over new infrastructure.
- No `Thread.Sleep`, no wall-clock waits, no tests that depend on machine speed. TTL expiry is
  tested by advancing `MutableClock`, not by waiting.
- A bug fix starts with a failing test that demonstrates the bug, then the fix. Both go in the same
  pull request, and the test alone should fail on `main`.

## Documentation

Docs are a deliverable here, not an afterthought.

- Each `docs/0N-*.md` file opens with one paragraph explaining why that section exists.
- Concept first, code second. Every code block gets a one-sentence lead-in saying why it is there.
- Write plainly and in the second person. Skip adjectives that sell rather than describe.
- If your change alters observable behavior, update the guide that describes it in the same pull
  request.

## Architecture decision records

Anything that changes the shape of the pattern — the file format, the TTL rule, the store
abstraction, the set of non-goals — needs a record in `docs/adr/`. Use the next free number and keep
the standard triad:

```markdown
# NNNN. Short title

## Context
What forced a decision, in a paragraph.

## Decision
What was chosen, stated as a rule.

## Consequences
What this makes easy, what it makes hard, and what it rules out.
```

Superseded ADRs stay in the repository. Mark them superseded and link forward. Deleting them erases
the reasoning a reader came for.

## Pull requests

1. Open an issue first for anything larger than a typo or a one-file fix. It is cheaper to agree on
   scope in an issue than to unwind a large branch.
2. Branch from `main`. Use a short descriptive name, for example `fix-ttl-boundary` or
   `docs-clarify-atomic-write`.
3. Keep commits focused. One commit that refactors and one that changes behavior beats a single
   commit that does both, because a reviewer can then read the behavior change on its own.
4. Write commit messages that say why the change is needed, not what lines moved. The diff already
   shows what moved.
5. Make sure `dotnet build` and `dotnet test` are green locally. CI runs the same two commands on
   every push to `main` and every pull request, so a red pipeline means a red machine.
6. Say in the pull request description what a reader learns that they could not learn before. That
   is the review criterion for this repository.

## Reporting problems

- **Bugs:** open an issue with the smallest program that shows the behavior, what you expected, and
  what happened. Include your `dotnet --version` and operating system, because file-system atomicity
  guarantees differ between platforms.
- **Documentation gaps:** open an issue that quotes the sentence that confused you. A confusing
  sentence is a real defect in a teaching repository.
- **Security:** this demo takes no untrusted input and holds no credentials, so ordinary issues are
  the right channel. If you find something you would rather not post publicly, use GitHub's private
  vulnerability reporting on this repository instead.

## Licensing of contributions

This project is released under the MIT License. By contributing, you agree that your contribution is
licensed under the same terms.

## Conduct

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md). Read it once. It is short,
and it is enforced.
