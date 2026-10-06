# Contributing to Javbuddy

Thanks for taking an interest. Bug reports, feature ideas and pull requests are welcome.

## Before you start

- Check the existing issues first. For anything beyond a small fix, open an issue to talk it through before writing code.
- Javbuddy covers adult content by nature; see the [content note in the README](README.md#disclaimers). Keep screenshots, test data and fixtures fictional and non-explicit.

## Building and testing

Requires the .NET 11 SDK, plus `ffmpeg` and `ffprobe` on the `PATH` for the media features.

```bash
dotnet build Javbuddy.slnx
dotnet test Javbuddy.Tests/Javbuddy.Tests.csproj
dotnet format Javbuddy.slnx --verify-no-changes
```

CI runs the format check, the unit tests and the E2E tests. The E2E suite drives a real headless Chromium and is slower, so run it separately and only when your change touches something that needs a real browser:

```bash
dotnet test Javbuddy.E2ETests/Javbuddy.E2ETests.csproj
```

Tests never read files from your machine: they build their own fixtures in a temp directory or use `Javbuddy.Tests/TestSupport/TestDbContextFactory.cs`. A new non-trivial service method gets an xUnit test, and a new shared component gets a bUnit test.

## Conventions

- Layering, UI patterns, database migrations and the codebase layout are written down in [Architecture](docs/architecture.md), [UI guidelines](docs/ui-guidelines.md), [Database workflow](docs/database-workflow.md) and the [Codebase map](docs/codebase-map.md). Read the ones your change touches.
- Inject `IDbContextFactory<AppDbContext>`, never a scoped `DbContext`.
- Formatting follows `.editorconfig`; `dotnet format` enforces it.
- Don't build ahead of the issue: no extra features beyond what it asks for.
- Check anything that touches an external API (javinizer-go, Prowlarr, Jellyfin) against the real thing, and run the app to check UI changes rather than reading the code.

## Pull requests

- Add an entry under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for every user-facing change.
- Update the README or docs if your change makes something they say wrong.
- Commit subjects are imperative and capitalized, with no type prefix (`Add …`, `Fix …`, `Refactor …`). Use the body to explain why the change was needed and how you verified it.
- Keep each pull request to one change.

The full workflow is in [Feature workflow](docs/feature-workflow.md).

## Reporting security issues

Don't open a public issue for a vulnerability. See [SECURITY.md](SECURITY.md).
