# Database Workflow

## Current state

- SQLite by default, with PostgreSQL as an opt-in provider (see below), via EF Core. Always inject `IDbContextFactory<AppDbContext>`, never a scoped `DbContext` — see `docs/architecture.md` for why.
- No migrate-the-live-DB guarantee: recreating the database (SQLite or Postgres) from scratch is an acceptable, expected fallback. Don't build workarounds to preserve data across a schema change unless specifically asked.
- A second, independent SQLite database (`Javbuddy.r18dev.db`) holds an imported r18.dev reference dump, queried directly with raw SQL rather than through EF Core — see `docs/architecture.md`'s Data layer section. This is a separate concern from the app's own schema below.

## EF Core migrations

- Generate with `dotnet ef migrations add <Name> --context Javbuddy.Data.AppDbContext` from `Javbuddy/`, and add the matching Postgres migration (see "PostgreSQL" below).
- To squash again, delete every file in `Javbuddy/Migrations/` (the model snapshot too, or EF generates an empty migration), run `dotnet ef migrations add Baseline`, compare its schema with a database built by the old migrations.
- Always inspect a freshly generated migration for spurious column renames before treating it as done — EF Core's migration diff can misread an add+drop as a rename when column order/type shifts, which silently loses data.

## PostgreSQL (opt-in)

SQLite is the default; `Database__Provider=Postgres` plus a Postgres `ConnectionStrings__Default` switches the app to PostgreSQL 18 (see `docs/configuration.md`). Nothing migrates data between the providers: switching starts from an empty database.

- **Two contexts, one model.** `AppDbContext` is the SQLite context (migrations in `Javbuddy/Migrations/`). `PostgresAppDbContext : AppDbContext` has its own migrations in `Javbuddy/Migrations/Postgres/`. The few provider differences are gated on `Database.IsNpgsql()` inside `AppDbContext` (the case-insensitive ICU collation on `Movie.Code` and `CachedImage.Code`, the `pg_trgm` search indexes, the stored `DateAdded` generated column, and the `DateTime` mapping). Call sites keep injecting `IDbContextFactory<AppDbContext>`; Program.cs registers an adapter over the Postgres factory.
- **Every schema change needs a migration for both contexts**, generated from `Javbuddy/`. Always pass the full context name, because EF treats the Postgres design-time factory as a match for the base type too (that is why `SqliteAppDbContextDesignTimeFactory` exists):

  ```bash
  dotnet ef migrations add <Name> --context Javbuddy.Data.AppDbContext
  dotnet ef migrations add <Name> --context PostgresAppDbContext --output-dir Migrations/Postgres
  ```

  A change SQLite doesn't care about (a max length, a column type annotation) gives an empty SQLite migration; keep it so the two histories stay in step. Inspect both for spurious renames. `PostgresModel_HasNoPendingChanges` and `SqliteModel_HasNoPendingChanges` fail when a model change lacks its migration.
- **Behaviour that differs and is handled in code.** Declared string max lengths are enforced on Postgres only (SQLite ignores them), so size a new limit for real data, or leave long JSON text unbounded. `NULL` sorts first ascending on SQLite and last on Postgres, so sort on `col != null` first where it matters. `DateTime` is `timestamp` at microsecond precision (a file's 100 ns time truncates: compare through `FileTimestamps`). `EF.Functions.Like` is case-sensitive on Postgres: use `CaseInsensitiveSearch.ContainsAny/StartsWithAny`, which emits `Like` on SQLite (its prefix seek on `IX_Movies_Code` survives) and `ILIKE` on Postgres, served by the `gin_trgm_ops` indexes (`pg_trgm`). `ILIKE` refuses a nondeterministic collation anywhere in its operand (`Movie.Code`, `CachedImage.Code`, or an expression over them), so the helper always casts the searched expression with `COLLATE "default"` (a no-op on other columns), and the `Movie.Code` trigram index is declared with that collation. A text filter that ORs a row's own column with its movie's columns goes through `ContainsAnyOrMovie`: SQLite keeps the flat OR over the join, Postgres uses `own ILIKE … OR MovieId IN (SELECT Id FROM Movies WHERE …)`, which lets the movie side use its trigram indexes. A Postgres-only index goes inside `if (IsPostgres)` in `AppDbContext`; the SQLite migration for it is empty. Any column compared with `Movie.Code` in a query (today `CachedImages.Code`) needs the same collation on Postgres, or the comparison cannot use its index. Text ordering follows the database collation on Postgres and byte order on SQLite. Case-insensitive equality on a column needs `AppDbContext.CaseInsensitiveCollation` rather than a literal `NOCASE`.
- **Duplicate keys:** use `DbConstraintErrors.IsDuplicateKey`, which knows both providers' error codes.
- **Raw SQL:** the image-cache upserts use `INSERT ... ON CONFLICT DO UPDATE`, valid on both. Their `DateTime` parameters bypass the model's converter, so on Postgres they are sent as explicit `timestamp` parameters (a UTC value would go as `timestamptz` and be shifted by the session time zone). Migrations may contain raw SQL (the `DeoVrGroups` identity sequence is advanced after its seed row).

### Running the tests on Postgres

The whole `Javbuddy.Tests` suite also runs on Postgres. Start a server and point the suite at it with an admin connection string:

```bash
docker run -d --name javbuddy-pg -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:18.4
JAVBUDDY_TEST_POSTGRES="Host=localhost;Username=postgres;Password=dev" dotnet test Javbuddy.Tests/Javbuddy.Tests.csproj
```

(use `sudo docker` if your user isn't in the `docker` group). Unset, the suite uses in-memory SQLite as before. Once per run a template database is created and migrated with the Postgres migrations (so they are tested too); each `TestDbContextFactory` clones it with `CREATE DATABASE ... TEMPLATE` and drops the clone on dispose. Cloning is I/O bound: for a disposable test server, `-c fsync=off -c synchronous_commit=off -c full_page_writes=off` on the `docker run` line makes it about three times faster. `SynchronousCompletionInterceptor` makes Postgres calls complete synchronously the way SQLite's do, because many component tests assert right after a render. Some tests never dispose their factory, so a full run can leave a few dozen `test_*` databases behind; the next run drops any older than 30 minutes (or drop them yourself: `DROP DATABASE ... WITH (FORCE)`). Tests that only make sense on Postgres use `[PostgresFact]` and are skipped otherwise. The E2E suite runs on SQLite only.

## SQLite-specific notes

- SQLite's dynamic typing is more permissive than Postgres: columns relying on type affinity rather than an explicit EF Core column type need care when a model change is made.
- The `Javbuddy.r18dev.db` raw-SQL reader (`R18DevDumpStore.cs`) is SQLite-only by design (a small embedded side database, independent of the app's main provider).
