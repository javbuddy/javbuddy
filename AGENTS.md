# Javbuddy

A .NET Blazor Server app for tracking wanted/owned JAV movies, modeled on Sonarr/Radarr's UX.
- Metadata provider: javinizer-go (real API contract: https://github.com/javinizer/javinizer-go — see "Verify against the real thing" below)
- User-facing overview and setup: **README.md**.

## Feature development

- Implement what the user asked for — no more, no less. For anything beyond a trivial fix, follow the full request → plan → verify → implement → verify workflow in **docs/feature-workflow.md**.
- If the request is ambiguous or silent on a detail you need, ask rather than guess.
- README.md is ordinary user-facing documentation (features, setup, docs index): when a change makes something it states wrong (a renamed route, setting, or doc), fix it in the same change.
- Don't build ahead of the request. No speculative features, no gold-plating an in-scope feature with things that weren't asked for — pulling in an unrequested idea while implementing a scoped change is scope creep even when it seems like a natural next step.

## Tech stack & conventions

- .NET Blazor Server, EF Core + SQLite.
- Always inject and use `IDbContextFactory<AppDbContext>`, never a scoped `DbContext` — Blazor Server circuits are long-lived, and a scoped context goes stale/thread-unsafe across renders and background tasks.
- Settings are configured via environment variables.
- Don't try to preserve or migrate the SQLite DB with workarounds — recreating it from scratch is an acceptable, expected fallback.
- Blazor CSS isolation: `.razor.css` files are scoped per component. Identical class names in two different `.razor.css` files do **not** share styles — copy the needed rules into the new component's own `.razor.css` rather than assuming reuse across components.
- EF Core migrations: always inspect a freshly generated migration for spurious column renames before applying it.
- Verify against the real thing, not assumptions: when a change touches an external API (javinizer-go, Prowlarr, Jellyfin) or the UI, check the real contract (read the actual source, or run the app and exercise the feature) rather than guessing field names or claiming a UI works from reading the code alone.
- Database roadmap and migration workflow: **docs/database-workflow.md**.

## Do's and Don'ts

- Don't write raw SQL against the app's own database (`AppDbContext`) — use EF Core. The only permitted exceptions are encapsulated atomic upsert helpers for image cache metadata (`Data/AppDbContextImageCacheExtensions.cs`), where EF Core lacks native upsert and SQLite's atomic `ON CONFLICT DO UPDATE` prevents unique constraint concurrency races without noisy error logging. Raw SQL is also fine against the separate r18.dev dump DB (`Services/R18Dev/R18DevDumpStore.cs`, backed by `Javbuddy.r18dev.db`), since it's a read-only imported reference dataset, not app data.
- Don't create, edit, or delete anything in a real media library (a configured library root outside the repo).
- Run `dotnet format Javbuddy.slnx --verify-no-changes` before committing/pushing.

## Structure & conventions

- Solution file is `Javbuddy.slnx`, covering the web project (`Javbuddy/Javbuddy.csproj`), the unit/component test project (`Javbuddy.Tests/Javbuddy.Tests.csproj`), and the E2E test project (`Javbuddy.E2ETests/Javbuddy.E2ETests.csproj`) as sibling project folders. `dotnet build Javbuddy.slnx` builds all three. For tests, run the two test projects separately (see "Testing") rather than `dotnet test Javbuddy.slnx` — that would also run the slow, browser-driven E2E suite.
- Razor pages/components should not inject `IDbContextFactory<AppDbContext>` or an integration client (`IJavinizerClient`, `IJellyfinClient`, etc.) directly for anything beyond a single read-only display query. Data mutation and any non-trivial or cross-cutting query belongs in a service, called from the component.
- `Services/Infrastructure/` holds generic, domain-agnostic infrastructure (`ApiClientBase<TSettings>`, `EffectiveSettingsResolver<TSettings>`, `SingleRowSettingsRepository<TSettings>`, `IHasConnectionUrls`) — extend it for a new integration rather than hand-rolling HTTP/settings boilerplate again. Concrete services and per-integration clients (`Services/<Integration>/*Client.cs`) stay in `Services/` itself. Full layering detail: **docs/architecture.md**.
- A UI pattern (markup + logic) used in 2+ places belongs in `Components/Shared/`, not copy-pasted between pages. Full UI/reuse guidelines: **docs/ui-guidelines.md**.
- `.editorconfig` at the repo root encodes the project's actual formatting conventions (verified against the existing codebase, not aspirational) plus the built-in .NET analyzers (`EnableNETAnalyzers`/`AnalysisLevel` in `Javbuddy.csproj`).
- Primary constructors (C# 12) are the enforced style (`csharp_style_prefer_primary_constructors`) for classes/records whose constructor body is trivial (field assignment or a pure base-call forward) — don't add a traditional constructor+field pair where one would do. A constructor with real logic in its body (e.g. building a field from a lambda) is correctly left traditional.

## Testing

- Test project: `Javbuddy.Tests` (xUnit + bUnit for Razor component rendering + NSubstitute for mocking). Run with `dotnet test Javbuddy.Tests/Javbuddy.Tests.csproj`.
- DB-backed tests use `Javbuddy.Tests/TestSupport/TestDbContextFactory.cs`, an `IDbContextFactory<AppDbContext>` backed by an in-memory SQLite connection — this exercises the real provider (constraints, indexes, conversions), unlike EF Core's `InMemory` provider. Reuse it rather than adding a second DB test double.
- Tests never depend on files on a developer's machine. `test-jav/`, `test-jav-nfo/` and the dev databases exist only for running the app by hand; a test builds its own fixture in a temp dir (or uses `TestDbContextFactory`) instead of reading a real local path.
- New non-trivial service method → add an xUnit test. New non-trivial shared component (`Components/Shared/`) → add a bUnit test. Pure-function static helpers (mappers, normalizers, formatters) are the cheapest to test — no mocking needed — so don't skip them.
- E2E test project: `Javbuddy.E2ETests` (xUnit + Playwright, real headless Chromium against a real Kestrel socket — not bUnit's in-process render). Run with `dotnet test Javbuddy.E2ETests/Javbuddy.E2ETests.csproj`. Reuses one shared app+browser fixture (`Javbuddy.E2ETests/Fixtures/E2EFixture.cs`) with an isolated, throwaway SQLite file per run, never a developer's real database — see the `playwright-blazor` skill for the fixture details and first-run browser-download notes.
- E2E tests are for the handful of flows that need a real browser/SignalR circuit to catch (page actually renders and hydrates, a component's real CSS/JS, a WebSocket reconnect) — bUnit stays the default for component-level testing; don't duplicate bUnit coverage here.

## Versioning

- `CHANGELOG.md` (Keep a Changelog format) is the source of truth for shipped history. Every user-facing change adds an entry under `## [Unreleased]` as part of the same change, not a follow-up step — see `docs/feature-workflow.md` step 6.
- A version bump = move `[Unreleased]`'s entries under a new dated `## [x.y.z]` heading, and update `<Version>` in `Directory.Build.props` (shared by all three projects) to match.

## Git hygiene

- Don't bundle an unrelated README edit into a feature commit.
- Don't commit unless explicitly asked.
- Commit message convention (observed and consistent across this repo's history):
  - Subject line: imperative, capitalized, no type-prefix (`Add`/`Fix`/`Refactor`/`Remove`/`Consolidate`/`Enforce`/`Restyle`/`Extract`/`Support` + what changed), roughly 50-70 characters.
  - Body: prose explaining the root cause/why and how the change was verified — not a Conventional Commits-style bullet list.
  - Trailers: `Co-Authored-By: <current model name> <noreply@example.com>`, naming whichever model actually made the change — not a hardcoded name. No agent-specific session metadata line.
  - Full request-to-commit workflow: **docs/feature-workflow.md**.

## Parallel work / worktrees

- Use a subagent for independent research, testing, or review work that would flood the main context. Every brief must include the repository path, relevant context, constraints, expected evidence, and required output; subagents do not inherit the conversation and cannot ask the user questions.
- For read-only codebase research, ask the child to read `AGENTS.md`, consult `docs/codebase-map.md`, verify findings against real files, avoid all writes, and cite `path:line` evidence. Keep small single-file lookups in the main session.
- Worktrees live as sibling directories to the repo, e.g. `../javbuddy-worktrees/<branch-name>/`, not nested inside it — keeps `bin`/`obj` and any running app instance isolated per worktree.
- Worktree directory name matches the branch name.
- Use a worktree when independent parallel agent sessions each need their own working build; a plain branch + `git switch` is enough for normal sequential work.
