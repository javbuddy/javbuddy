# Database Workflow

## Current state

- SQLite only, via EF Core. Always inject `IDbContextFactory<AppDbContext>`, never a scoped `DbContext` — see `docs/architecture.md` for why.
- No migrate-the-live-DB guarantee: recreating the SQLite database from scratch is an acceptable, expected fallback. Don't build workarounds to preserve data across a schema change unless specifically asked.
- A second, independent SQLite database (`Javbuddy.r18dev.db`) holds an imported r18.dev reference dump, queried directly with raw SQL rather than through EF Core — see `docs/architecture.md`'s Data layer section. This is a separate concern from the app's own schema below.

## EF Core migrations

- Generate with `dotnet ef migrations add <Name>` from `Javbuddy/`.
- To squash again, delete every file in `Javbuddy/Migrations/` (the model snapshot too, or EF generates an empty migration), run `dotnet ef migrations add Baseline`, compare its schema with a database built by the old migrations.
- Always inspect a freshly generated migration for spurious column renames before treating it as done — EF Core's migration diff can misread an add+drop as a rename when column order/type shifts, which silently loses data.

## If/when Postgres is considered later

Not scheduled. Don't start this without an explicit request; the notes below exist only so a future decision has a starting point, not as a plan to execute.

- Provider swap: `Microsoft.EntityFrameworkCore.Sqlite` → `Npgsql.EntityFrameworkCore.PostgreSQL`, plus a connection-string/env-var convention change (settings-via-ENV is the established pattern to extend).
- SQLite-specific type/constraint behavior to re-check: SQLite's dynamic typing is more permissive than Postgres — columns relying on SQLite's type affinity rather than an explicit EF Core column type would need auditing.
- The `Javbuddy.r18dev.db` raw-SQL reader (`R18DevDumpStore.cs`) is SQLite-specific (imports a SQLite dump) — it would either need to stay SQLite-only (a small embedded side database, independent of the app's main provider) or be redesigned; not a reason to block a Postgres migration on its own.
- Whether the "recreate rather than migrate" policy still holds: it's a reasonable default for a self-hosted single-user app on SQLite; worth re-confirming intentionally rather than assuming it carries over once a real production dataset exists on Postgres.
- Local dev ergonomics: SQLite's zero-setup, single-file nature is presumably part of why it was chosen — a Postgres move likely means adding a docker-compose dependency for local development.
