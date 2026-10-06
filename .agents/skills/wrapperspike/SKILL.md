---
name: wrapperspike
description: Use when validating Javbuddy logic against real local data. Runs disposable probes against the real database and media library.
---

# The wrapperspike scratch-test workflow

Unit tests (in-memory SQLite via `TestDbContextFactory`, a `TempRoot` temp
folder) prove logic is correct in the abstract. They don't prove it survives
contact with the real `Javbuddy.db` and the real `D:\media\movies\jav` /
`D:\media\movies\vr` libraries — real file permission quirks, real `.nfo`
encodings, a real corrupted file, thousands of real rows. This skill is a
disposable console project ("wrapperspike") that references the actual
`Javbuddy.csproj` and lets you call real service methods directly against
real data, without going through the Blazor app or a browser.

Use it to expose real-data behavior that synthetic tests miss, such as corrupt
media hanging a native parser, inaccessible directories, unusual encodings,
or library-wide metadata gaps.

## When to reach for this vs. other verification

- **Pure logic, no real file-system/DB quirks involved** → unit test is
  enough, skip this.
- **New/changed code that reads or writes real files, or needs to be proven
  safe against the real `Javbuddy.db` before it runs unattended (a scheduled
  task, a bulk backfill)** → wrapperspike first, live browser run second.
- **UI rendering/interaction** → the `debug-ui` skill instead; wrapperspike
  has no browser involved at all.

## 1. Scaffold the project

The scratch project is disposable. Recreate it for each investigation, or
reuse it only while the current investigation is active. Put it at
`<repo-root>/.scratch/wrapperspike/`; the directory is ignored.

`WrapperSpike.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../Javbuddy/Javbuddy.csproj" />
  </ItemGroup>
</Project>
```

## 2. `Program.cs` template

```csharp
using Javbuddy.Data;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection; // needed for ServiceCollection

var services = new ServiceCollection();
services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=../../../Javbuddy/Javbuddy.db"));
var provider = services.BuildServiceProvider();
var dbFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

// Apply any pending migration — this bypasses Program.cs's own startup
// MigrateAsync() call, so the DB may be behind if you just added one.
await using (var migrateDb = await dbFactory.CreateDbContextAsync())
{
    await migrateDb.Database.MigrateAsync();
}

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["LocalLibrary:RootPaths:0"] = @"D:\media\movies\jav",
        ["LocalLibrary:RootPaths:1"] = @"D:\media\movies\vr",
    })
    .Build();

var prober = new MediaInfoProber(dbFactory);
var client = new LocalLibraryClient(dbFactory, config, new MemoryCache(new MemoryCacheOptions()), prober, /* ILogger<LocalLibraryClient> */ Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalLibraryClient>.Instance);

// ... call the real method(s) under test, print what happened ...
```

Adjust the constructed service(s) to whatever you're verifying — construct
the concrete class directly with its real dependencies (a real `IMediaInfoProber`,
a real `IMemoryCache`, `NullLogger<T>.Instance` for any `ILogger<T>`
parameter) rather than mocking, since the whole point is exercising real
behavior. Check the class's current constructor signature first — it drifts
as the codebase changes (e.g. an `ILogger<T>` parameter was added mid-session
here).

Run it with `dotnet run` from the wrapperspike directory.

## 2a. Fix the "type not found" build error

The scratch project's `dotnet run` builds standalone from a template — if it
fails with `error CS0246: The type or namespace name 'ServiceCollection'
could not be found`, add the missing `using
Microsoft.Extensions.DependencyInjection;` shown above. This is the single
most common first-run error.

## 3. Verification discipline — cheapest and safest first

1. **Read-only dry run before any mutation.** Before calling a method that
   writes to `Javbuddy.db`, first write a version that only *computes* what
   it would do (e.g. compare the real folder listing against tracked DB rows)
   and prints the result. Confirm the numbers look sane against a known
   quantity (e.g. "3627 folders, 3627 tracked Got movies, 0 drift") before
   trusting the real mutation.
2. **One known row before the whole library.** Once the dry run looks safe,
   call the real mutating method against a single, specific, already-understood
   row chosen and understood for the current investigation, and print its
   before/after state. Only after
   that looks correct do you trust a full-library run through the actual app.
3. **Idempotency check.** Call the same method a second time immediately
   after the first and confirm the state doesn't change further — this catches
   "first observation establishes a baseline" logic silently re-triggering
   every run instead of only once.

## 4. Hazards specific to touching the real DB

- **Never revert an applied migration on the real DB once code depends on
  the data it wrote.** `dotnet ef database update <earlier-migration>` runs
  every migration's `Down()` between the current state and the target — which
  drops columns/tables, destroying whatever was just backfilled into them.
  Reverting can also drop unrelated columns populated by expensive probes.
  If a migration needs to change, prefer `dotnet ef migrations remove`
  **before** it's ever applied anywhere, or a new additive migration —
  never a revert once real data depends on it.
- **Stop the running dev server before `dotnet build`/`dotnet run --project
  Javbuddy`** — same file-lock issue as the `debug-ui` skill
  (`taskkill //F //IM Javbuddy.exe 2>/dev/null || true`). The
  wrapperspike project itself doesn't lock anything, but if a live task
  triggered through the app (e.g. a scheduled rescan) is still running,
  wait for it to finish before rebuilding — killing the process mid-run
  loses its progress.
- **A long-running real backfill is normal, not a hang.** Native probes
  (MediaInfo) or thousands-of-rows loops can legitimately take many minutes
  against a real network-share library. Poll for completion (`ScheduledTaskRuns`
  table, or the specific row's state) with a background `terminal` process and
  bounded polling rather than blocking synchronously or assuming something's
  stuck.
- **SQLite has no separate read-only mode enforced here** — a `python -c
  "import sqlite3; ..."` one-liner is the fastest way to just *inspect* rows
  without spinning up wrapperspike at all; reach for wrapperspike specifically
  when you need to invoke real C# business logic, not just read data.

## 5. Cleanup

Wrapperspike's `Program.cs` is throwaway — overwrite it freely for the next
verification task, no need to preserve history between checks. If a check
inserted scratch rows into the real DB, delete them the same way the
`debug-ui` skill recommends (tag with an identifiable value, clean up after).
