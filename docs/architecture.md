# Architecture

Consolidates the layering rules already stated piecemeal in `AGENTS.md` — no new architecture invented here, just gathered in one place with more room to explain the "why." For a concrete map of what's actually in each directory today, see **docs/codebase-map.md**.

## Solution layout

`Javbuddy.slnx` covers three sibling project folders:
- `Javbuddy/` — the Blazor Server app itself.
- `Javbuddy.Tests/` — xUnit + bUnit (component rendering) + NSubstitute (mocking) unit/component tests.
- `Javbuddy.E2ETests/` — xUnit + Playwright, driving a real headless Chromium browser against a real Kestrel instance.

`dotnet build Javbuddy.slnx` builds all three; test projects are run separately (`dotnet test <project>.csproj`), not via `dotnet test Javbuddy.slnx`, since that would also run the slow E2E suite.

## Blazor Server constraints

A Blazor Server circuit is long-lived — it survives across many renders and background work, unlike a typical web request. A scoped `DbContext` captured once at circuit start would go stale and isn't safe to use concurrently from background tasks (timers, SignalR callbacks) that can run alongside the UI thread. That's why every service and component injects `IDbContextFactory<AppDbContext>` and creates a fresh, short-lived context per operation, never a directly-injected scoped `DbContext`.

The same long-lived-circuit reasoning is why Blazor CSS isolation (`.razor.css`) matters here more than in a typical SPA: styles are scoped per component file, and identical class names in two different `.razor.css` files do not share rules. Copy the CSS you need into the new component's own file rather than assuming reuse — see `docs/ui-guidelines.md` for the concrete pattern.

## Service layering

`Services/` holds concrete app services and pure-function helpers — anything with knowledge of Javbuddy's domain model. For the current, concrete list of what's in `Services/` (and `Components/Pages/`, `Components/Shared/`, and where each area's tests live), see **docs/codebase-map.md** — kept separate from this file specifically so an added/renamed service doesn't require touching the architectural narrative below.

`Services/Infrastructure/` holds generic, reusable infrastructure with **no** app-domain knowledge:
- `ApiClientBase<TSettings>` — shared HTTP timeout/error handling for integration clients.
- `EffectiveSettingsResolver<TSettings>` — env-var-or-DB settings resolution.
- `SingleRowSettingsRepository<TSettings>` — persistence for the single-row settings pattern.
- `IHasConnectionUrls` — a small contract those settings types implement.
- `IObjectStore` / `IObjectStoreProvider` (`ObjectStore.cs`) — durable storage addressed by string keys and streams, never paths. There's one store, under `ObjectStore:Path`; each `ObjectStoreArea` (actor images, trickplay) is a key prefix in it (`PrefixedObjectStore`), the way one S3 bucket would hold them, so a non-filesystem backend is one more `IObjectStore`. `FileSystemObjectStore` is the only implementation today: keys are relative paths, writes are atomic (temp file + rename). The domain wrappers (`ActorImageDataStore`, `TrickplayStore`) keep their layout and indexes (DB rows) on top; tools that need a real file (ffmpeg, upload staging) use local temp and write the result to the store. The disposable image cache is not an object store.
- `BackgroundJobRunner` — application-owned execution for user-triggered work that must outlive the page that queued it (own DI scope per job, exceptions logged, cancelled and awaited on shutdown). Components queue work through a domain launcher (`ScheduledTaskLauncher`, `ActorBatchEnrichmentLauncher`) instead of a detached `Task.Run`, and pass immutable inputs so a job never retains the component.

`Services/<Integration>/*Client.cs` — one subfolder per external integration, each client extending `ApiClientBase<TSettings>` rather than hand-rolling timeout/error/settings boilerplate again. A few per-subject subfolders (local filesystem scanning, a native library wrapper, a local SQLite dump reader) aren't HTTP integrations and don't extend `ApiClientBase`.

`Services/Tasks/` holds the background task scheduler and individual task implementations (System > Tasks).

Dependency direction: Razor pages/components → concrete services (`Services/<Group>/*Service.cs`) → integration clients / `Infrastructure/` / `IDbContextFactory<AppDbContext>`. Components should not inject `IDbContextFactory<AppDbContext>` or an integration client directly for anything beyond a single read-only display query — mutation and non-trivial or cross-cutting queries belong in a service.

## Data layer

EF Core + SQLite, via `AppDbContext` and `IDbContextFactory<AppDbContext>` (see above). No migration workaround policy: the app is expected to support recreating the SQLite DB from scratch rather than guaranteeing in-place migration of old data.

Always inspect a freshly generated EF Core migration for spurious column renames before applying it — see `docs/database-workflow.md` for the full migration workflow and current-vs-future (Postgres) notes.

There's also a second, independent SQLite database — `Javbuddy.r18dev.db` — imported from a downloaded r18.dev SQL dump (`Services/R18Dev/R18DevDumpImporter.cs`). It's a read-only reference dataset queried directly with raw SQL (`R18DevDumpStore.cs`), not through `AppDbContext`. Aside from encapsulated image-cache atomic upsert helpers (`Data/AppDbContextImageCacheExtensions.cs`), raw SQL is not used against `AppDbContext` (see `AGENTS.md`'s Do's and Don'ts).

## Search/tooling note

This repo does not use RAG (retrieval-augmented generation) for code search, and shouldn't need to: it's one small-to-medium .NET solution, and Claude's native file search (Glob/Grep) plus Explore-agent-driven investigation already cover it at zero setup or maintenance cost. Reconsider only if the codebase grows by an order of magnitude or gains many more sibling repos that need cross-repo search.
