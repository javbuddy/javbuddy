---
name: bunit-component-testing
description: Use when testing Javbuddy Razor components with bUnit. Covers project fixtures, dependency setup, interaction, and async rendering.
---

# bUnit Component Testing (Javbuddy)

`AGENTS.md`'s Testing section requires a bUnit test for every non-trivial new shared component (`Components/Shared/`) and is the default for component-level testing generally — E2E/Playwright is reserved for the handful of flows that need a real browser/SignalR circuit (see `docs/feature-workflow.md` step 5). This skill covers the real patterns already used across `Javbuddy.Tests/Components/`, not generic bUnit documentation — see `Javbuddy.Tests/Components/Layout/NavMenuTests.cs` and `Javbuddy.Tests/Components/Pages/ActorMissingFilterTests.cs` for full worked examples.

## When to Use This Skill

Use this skill when writing a test for a Razor component or page — anything under `Components/Shared/` or `Components/Pages/`.

## Base Class and Setup

Test classes derive from bUnit's `BunitContext` (this project uses bUnit v2's `BunitContext`, not the older `TestContext` name). Register fakes/test doubles into `Services` (bUnit's built-in DI container) before rendering:

```csharp
using Bunit;
using Javbuddy.Data;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

public class NavMenuTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(Substitute.For<ILogger<NavMenu>>());
        return factory;
    }
}
```

## Database-Backed Components

Reuse `Javbuddy.Tests/TestSupport/TestDbContextFactory.cs` (an `IDbContextFactory<AppDbContext>` backed by an in-memory SQLite connection — exercises real provider behavior, unlike EF Core's `InMemory` provider) exactly as `AGENTS.md` requires. Its underlying connection must stay open for the test's lifetime, so `using`/`await using` the factory and dispose it when the test is done:

```csharp
[Fact]
public async Task QueuedAndDownloadingTorrents_ShowInTheBadge()
{
    using var factory = SetUpServices();
    await using (var db = await factory.CreateDbContextAsync())
    {
        db.Movies.Add(new Movie { Code = "AAA-001" });
        await db.SaveChangesAsync();
    }

    var cut = Render<NavMenu>();

    Assert.Equal("1", cut.Find(".nav-badge").TextContent);
}
```

For services with a fakeable dependency (an integration client, `IR18DevDumpStore`, etc.), register an `NSubstitute` fake into `Services` alongside the DB factory — see the `nsubstitute-mocking` skill for the mocking conventions themselves.

## Rendering and Passing Parameters

```csharp
private IRenderedComponent<ActorMissing> RenderPage() =>
    Render<ActorMissing>(p => p.Add(x => x.RouteName, ActorName));
```

`Render<T>()` returns an `IRenderedComponent<T>` — use `cut` (component-under-test) as the conventional variable name, matching the rest of the suite.

## Finding and Interacting with Elements

Prefer CSS-class selectors that already exist in the component's markup (this codebase doesn't use `data-test`/`data-testid` attributes — don't introduce them for bUnit tests unless the component already has one for another reason):

```csharp
cut.Find(".nav-badge").TextContent;
cut.FindAll(".poster-title").Select(e => e.TextContent.Trim()).ToList();
cut.FindAll("button").First(b => b.TextContent.TrimStart().StartsWith("Filter")).Click();
```

`.Click()` on a found element triggers the component's `@onclick` handler synchronously within the test.

## Async Re-Renders: `WaitForAssertion` and `InvokeAsync`

A state change triggered from outside the component's own render cycle — a `ChangeNotifier` event (see the `blazor-server-components` skill), a background task, `NavigationManager.NavigateTo` — needs to be driven through the component the same way it would happen in the real circuit, then waited on:

```csharp
// Triggering a cross-circuit notification (must marshal via InvokeAsync, matching the
// real component's own handler — see blazor-server-components skill)
await cut.InvokeAsync(notifier.NotifyChanged);
cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".nav-badge").TextContent));

// Triggering real navigation
var nav = Services.GetRequiredService<NavigationManager>();
nav.NavigateTo("actors");
cut.WaitForAssertion(() => Assert.Contains("actors/add/new", cut.Markup));
```

`WaitForAssertion` retries the assertion until it passes or times out — don't `Assert` immediately after an action that triggers an async re-render; it will race the render and flake.

## When There's No Pure-Function Seam

Some component logic (private filter/sort/dedup pipelines, for example) has no static-method seam to unit test directly. In that case, drive the real component end-to-end instead of trying to extract the logic just to make it testable: seed the DB/fakes with curated data, render, interact with the actual controls, and assert on rendered output — see `ActorMissingFilterTests.cs`'s `ApplyFiltersAndSort` coverage for the full pattern. Don't force a refactor purely to create a testing seam if the component's actual structure doesn't call for one otherwise.
