---
name: nsubstitute-mocking
description: Use when mocking Javbuddy tests with NSubstitute. Covers substitutes, return values, argument matching, and call verification.
---

# NSubstitute Mocking (Javbuddy)

`AGENTS.md`'s Testing section specifies NSubstitute for mocking in `Javbuddy.Tests`. This skill documents the subset of NSubstitute actually used across the real test suite — not the full NSubstitute feature surface. If a test needs something not covered here (argument matchers beyond `Arg.Any<T>`, call ordering, throwing exceptions from a fake), it's fine to reach for NSubstitute's broader API, but check whether the simpler patterns below already cover the case first.

## When to Use This Skill

Use this skill when a test needs a fake for a service interface, integration client, or other dependency — in a plain xUnit service test or inside a bUnit component test (see the `bunit-component-testing` skill for wiring a fake into bUnit's `Services` container).

## Creating a Fake

```csharp
var javinizerClient = Substitute.For<IJavinizerClient>();
var dumpStore = Substitute.For<IR18DevDumpStore>();
var logger = Substitute.For<ILogger<NavMenu>>();
```

`Substitute.For<T>()` works for interfaces (the common case here — every integration client is behind an interface, per `docs/architecture.md`) and for framework types like `ILogger<T>`.

## Stubbing Return Values with `.Returns()`

```csharp
javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
    .Returns(new JavinizerScrapeResult(true, new MovieViewDto { Title = "Scraped Title" }, null));

localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>())
    .Returns(new LocalLookupResult(false, null, null, null));

pathMappingService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PathMapping>());
```

Note the return values are this codebase's own domain-specific result records (`JavinizerScrapeResult`, `LocalLookupResult`, etc. — see the `csharp-coding-standards` skill's error-handling section) — stub the same `Success`/data/`ErrorMessage` shape the real client returns, including the failure case:

```csharp
javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
    .Returns(new JavinizerScrapeResult(false, null, "javinizer-go is not configured."));
```

`.Returns()` also accepts a `Task<T>` directly for methods returning an in-flight operation to control timing (e.g. `localLibraryClient.ListMovieCodesAsync(Arg.Any<CancellationToken>()).Returns(codesTcs.Task)` with a `TaskCompletionSource` when a test needs to control exactly when an async call completes).

## `Arg.Any<T>()`

Used pervasively for parameters the test doesn't care about pinning to an exact value — most commonly `CancellationToken`:

```csharp
jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(["lib-1"]);
```

Pin the arguments that matter (a specific code, id, or path) as literal values in the same call, and use `Arg.Any<T>()` only for the ones that don't:

```csharp
sortService.ScanAsync(Arg.Any<int>(), "/scratch/ABC-123", Arg.Any<CancellationToken>())
    .Returns(new TorrentScanResult(true, files, null));
```

## Verifying Calls: `Received()` / `DidNotReceive()`

Use these to assert a fake *was* or *wasn't* called, when the observable effect isn't otherwise visible through a return value or DB state:

```csharp
// Called exactly twice
javinizer.Received(2).TestConnectionAsync(Arg.Any<CancellationToken>());

// Called exactly once, for one specific id, but not for another
await client.Received(1).RefreshMediaInfoOnlyAsync(gotId, Arg.Any<CancellationToken>());
await client.DidNotReceive().RefreshMediaInfoOnlyAsync(missingId, Arg.Any<CancellationToken>());
```

Real example — a safety-guard test verifying a task does nothing when it detects an unreachable library root, rather than wrongly treating it as an empty library (`Javbuddy.Tests/Services/LibraryRescanTaskTests.cs`):

```csharp
// If this were treated as "an empty library", the Got movie above would be wrongly reverted.
var client = CreateLocalLibraryClient(anyRootReachable: false, folderCodes: []);
var task = new LibraryRescanTask(factory, client, NullLogger<LibraryRescanTask>.Instance);
var summary = await task.RunAsync(CancellationToken.None, NoopProgress);

await using var verifyDb = await factory.CreateDbContextAsync();
Assert.Equal(MovieStatus.Got, (await verifyDb.Movies.SingleAsync()).Status);
Assert.Contains("no configured local library root is currently reachable", summary);
await client.DidNotReceive().ListMovieCodesAsync(Arg.Any<CancellationToken>());
```

Note the pattern here: the DB-state assertion (`Status` unchanged) and the message assertion prove the *outcome*; `DidNotReceive()` additionally proves *why* — the task bailed out before ever calling the client, not just that it happened to leave the DB alone. Reach for `Received`/`DidNotReceive` when a call's absence or count is itself the thing under test, not as a replacement for asserting observable state when that's available.

## Not Moq

Don't introduce Moq (`Mock<T>`) even in a new test file — NSubstitute is the established convention for this project, and mixing mocking libraries within the same test suite adds a second API to learn for no benefit.
