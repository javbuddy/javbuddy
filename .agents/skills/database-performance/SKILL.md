---
name: database-performance
description: Use when writing EF Core queries in Javbuddy. Covers DbContext factories, tracking, limits, joins, and N+1 avoidance.
tags: [efcore, performance, patterns]
---

# Database Performance Patterns

Rewritten around this project's actual data access shape: EF Core + SQLite via `IDbContextFactory<AppDbContext>`, no Dapper, no CQRS-style per-entity read/write store split. See `docs/architecture.md`'s Data layer section and `docs/database-workflow.md` for the broader picture; this skill is the query-performance detail underneath that.

## When to Use This Skill

Use this skill when:
- Writing a new service method that queries or mutates `AppDbContext`
- Reviewing a query for performance issues (N+1, unbounded results, unnecessary tracking)
- Deciding whether a query belongs in a service at all (see `docs/architecture.md`'s page/component/service boundary)

---

## Core Principles

1. **`IDbContextFactory<AppDbContext>`, always** — never a directly-injected scoped `DbContext`. See below for why.
2. **`AsNoTracking` for reads** — EF Core change tracking is expensive and unneeded for anything you won't `SaveChangesAsync`.
3. **Apply row limits** — don't return unbounded result sets from a query that could grow large.
4. **Avoid N+1 queries** — use `Include`/`Select` projections instead of looping and querying per item.
5. **Do joins in EF Core, not in application code** — let `Include`/navigation properties or a LINQ join generate the SQL join.
6. **Constrain column sizes** — set `[StringLength]`/`HasMaxLength` explicitly rather than leaving strings unbounded.

---

## `IDbContextFactory<AppDbContext>`, Not a Scoped `DbContext`

A Blazor Server circuit is long-lived — it survives across many renders and background work, unlike a typical web request. A scoped `DbContext` captured once at circuit start goes stale and isn't thread-safe to use from background tasks (timers, SignalR callbacks) running alongside the UI. Every service method that touches the database creates its own short-lived context instead:

```csharp
public class MovieService(IDbContextFactory<AppDbContext> dbFactory) : IMovieService
{
    public async Task<Movie?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == code, ct);
    }
}
```

Razor pages/components should not inject `IDbContextFactory<AppDbContext>` directly beyond a single read-only display query — mutation and any non-trivial or cross-cutting query belongs in a service like the one above (`docs/architecture.md`).

---

## AsNoTracking for Read Queries

```csharp
// DO: Disable tracking for reads
var movies = await db.Movies
    .AsNoTracking()
    .Where(m => m.Status == MovieStatus.Missing)
    .ToListAsync(ct);

// Only when you intend to mutate and SaveChangesAsync in the same context:
var movie = await db.Movies
    .FirstOrDefaultAsync(m => m.Id == id, ct);   // tracked, on purpose
movie.Status = MovieStatus.Owned;
await db.SaveChangesAsync(ct);
```

---

## Always Apply Row Limits

Don't return an unbounded result set from a query whose row count can grow with the library. Prefer an explicit `Take`/page size, or a query shaped to the UI's actual need (e.g. filtered by actor, status) rather than "fetch everything and filter in memory."

```csharp
public async Task<IReadOnlyList<MovieSummary>> GetRecentAsync(int limit, CancellationToken ct = default)
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    return await db.Movies
        .AsNoTracking()
        .OrderByDescending(m => m.AddedAt)
        .Take(limit)
        .Select(m => new MovieSummary(m.Id, m.Code, m.Title))
        .ToListAsync(ct);
}
```

---

## Avoid N+1 Queries

```csharp
// BAD: N+1 — one query per movie
var movies = await db.Movies.ToListAsync(ct);
foreach (var movie in movies)
{
    var actors = await db.MovieActors.Where(a => a.MovieId == movie.Id).ToListAsync(ct);
}

// GOOD: single query with Include
var movies = await db.Movies
    .AsNoTracking()
    .Include(m => m.Actors)
    .ToListAsync(ct);

// GOOD: projection avoids loading full navigation graphs you don't need
var summaries = await db.Movies
    .AsNoTracking()
    .Select(m => new MovieWithActorNames(m.Id, m.Code, m.Actors.Select(a => a.Name).ToList()))
    .ToListAsync(ct);
```

---

## Never Do Application-Side Joins

Let EF Core generate the SQL join via navigation properties or a LINQ `join`, instead of fetching two lists and matching them in C#.

```csharp
// BAD: application join — two full-table queries, O(n*m) in memory
var movies = await db.Movies.ToListAsync(ct);
var actors = await db.Actors.ToListAsync(ct);
var result = movies.Select(m => new { Movie = m, Owner = actors.FirstOrDefault(a => a.Id == m.PrimaryActorId) });

// GOOD: EF Core join via navigation property
var result = await db.Movies
    .AsNoTracking()
    .Include(m => m.PrimaryActor)
    .ToListAsync(ct);
```

---

## Avoid Cartesian Explosions

Multiple `Include` calls on collection navigations can multiply row counts. Use `AsSplitQuery()` or an explicit projection when including more than one collection navigation on the same query.

```csharp
// Can multiply rows: N images * M genres per movie
var movie = await db.Movies
    .AsSplitQuery()
    .Include(m => m.Images)
    .Include(m => m.Genres)
    .FirstOrDefaultAsync(m => m.Id == id, ct);
```

---

## Constrain Column Sizes

Set `[StringLength]` on new model properties rather than leaving them unbounded — matches this codebase's existing convention (e.g. `Models/TorrentDownload.cs`).

```csharp
public class TorrentDownload
{
    [StringLength(1000)]
    public string? SavePath { get; set; }
}
```

---

## Don't Build a Generic Repository

A generic `IRepository<T>` hides query complexity, can't enforce row limits, and hides N+1 problems. Write purpose-built service methods instead (this is already how `MovieService`, `SearchService`, etc. are structured) — each method's signature says exactly what it fetches and how much.

---

## Quick Reference

| Anti-Pattern | Solution |
|--------------|----------|
| Scoped `DbContext` in a service/component | `IDbContextFactory<AppDbContext>.CreateDbContextAsync` per operation |
| No row limit | Add a `limit`/`Take` to any query whose result can grow unbounded |
| N+1 queries | Use `Include` or a `Select` projection |
| Application joins | Use navigation properties / EF Core `join` |
| Cartesian explosion | Use `AsSplitQuery` or projection when including multiple collections |
| Tracking read-only data | Use `AsNoTracking` |
| Generic repository | Purpose-built service methods |
| Unbounded strings | `[StringLength]`/`HasMaxLength` in the model |

---

## Resources

- **EF Core Performance**: https://learn.microsoft.com/en-us/ef/core/performance/
- **AsSplitQuery**: https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries
