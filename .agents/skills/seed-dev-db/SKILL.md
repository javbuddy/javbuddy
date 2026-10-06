---
name: seed-dev-db
description: Use when seeding Javbuddy's development database. Creates disposable EF Core tooling for realistic manual UI testing.
---

# Seeding the local dev DB for manual testing

Some UI behavior only shows up with enough rows — virtualized-window sliding,
scroll position, filter debounce, pagination. Adding that many movies by hand
through Add Movie is impractical. This skill is a disposable console project
(same shape as the `wrapperspike` skill) that inserts synthetic `Movie`/`Actor`
rows straight into the developer's local `Javbuddy.db` via EF Core, so you can
open the running app in a real browser and see the behavior for yourself.

## When to use this vs. alternatives

- **Writing an automated Playwright test that needs seeded rows** →
  `Javbuddy.E2ETests/Support/DbSeeding.cs` instead. It seeds the E2E fixture's
  isolated, throwaway per-run SQLite DB (see `E2EFixture.DbFactory` and the
  `playwright-blazor` skill) — nothing there touches the dev DB, and this
  skill's tool has no place in a committed test. Reuse or extend `DbSeeding`'s
  existing helpers (`SeedMovieAsync`, `SeedManyMoviesAsync`, etc.) rather than
  duplicating them.
- **Verifying service/data logic against real files or real production data**
  → the `wrapperspike` skill instead (points at the real `Javbuddy.db` and
  `D:\media\...` library to catch real-world quirks, not to add rows for a
  UI check).
- **Just want to look at what's currently in the dev DB** → the `debug-ui`
  skill's read-only `python`/`sqlite3` one-liner is faster than scaffolding
  a project. Reach for *this* skill specifically when you need to insert rows.

## 1. Scaffold the project

Disposable, like `wrapperspike` — recreate it each time, don't commit it.
Put it at `<repo-root>/.scratch/seed-dev-db/`; the directory is ignored.

`SeedDevDb.csproj`:

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

Seeds via `IDbContextFactory<AppDbContext>` — the same pattern the app itself
uses everywhere (see `AGENTS.md`) — never raw SQL against `AppDbContext`.
Every row gets a **distinctive `Code` prefix** so it can be found and deleted
later without guessing which rows were seeded vs. real data, mirroring the
`debug-ui` skill's tag-and-cleanup convention.

```csharp
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

const string prefix = "SEEDTEST"; // change per session so runs don't collide
const int count = 500; // enough to force Movies.razor's virtualized window to slide

var services = new ServiceCollection();
services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=../../../Javbuddy/Javbuddy.db"));
var dbFactory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();

await using var db = await dbFactory.CreateDbContextAsync();

var movies = Enumerable.Range(0, count).Select(i => new Movie
{
    Code = $"{prefix}-{i:D4}",
    Status = MovieStatus.Got,
    MetaTitle = $"{prefix} Test Movie {i:D4}",
    MetaReleaseDate = new DateTime(2024, 1, 1),
    MetaRuntimeMinutes = 120,
    MetaActresses = "Test Actress",
    MetaGenres = "Test Genre",
    MetaFetchedAt = DateTime.UtcNow,
}).ToList();

db.Movies.AddRange(movies);
await db.SaveChangesAsync();
Console.WriteLine($"Seeded {movies.Count} movies with prefix '{prefix}'.");
```

Run with `dotnet run` from the project directory. If it fails with
`error CS0246: The type or namespace name 'ServiceCollection' could not be
found`, add `using Microsoft.Extensions.DependencyInjection;` — the most
common first-run error (same as `wrapperspike`).

The app doesn't need to be stopped to run this — it's a separate process
just writing to the SQLite file. The running app's Blazor circuit won't pick
up the new rows on its own; reload the browser tab (or navigate to the page
again) to see them.

## 3. Cleanup — always, via EF Core

Never leave seeded rows in the dev DB once you're done. Delete by the same
prefix, through the same `DbContext` — not a raw `DELETE` statement:

```csharp
var toRemove = await db.Movies.Where(m => m.Code!.StartsWith(prefix)).ToListAsync();
db.Movies.RemoveRange(toRemove);
await db.SaveChangesAsync();
Console.WriteLine($"Removed {toRemove.Count} seeded movies.");
```

Swap the seed block for the cleanup block in the same `Program.cs` once
you're finished testing, run it once, and confirm the count back to 0 before
moving on — don't leave the dev DB in a state different from how you found it.
