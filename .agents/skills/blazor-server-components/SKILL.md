---
name: blazor-server-components
description: Use when changing Javbuddy Blazor components. Covers render modes, circuit lifecycle, live updates, and enhanced navigation.
---

# Blazor Server Components (Javbuddy)

Grounded in this project's actual render-mode setup and live-update patterns — not generic Blazor Server advice. See `docs/architecture.md` for the broader circuit/`IDbContextFactory` reasoning and `docs/ui-guidelines.md` for CSS isolation and component reuse; this skill covers render modes, circuit lifecycle, and cross-circuit updates specifically.

## When to Use This Skill

Use this skill when:
- Adding a new page/component and deciding whether it needs `@rendermode InteractiveServer`
- A component needs to reflect a change made from a different browser tab/circuit
- Debugging a component that "doesn't update" after a background task or another circuit changes data
- Debugging something that works on first load but breaks after Blazor's enhanced (in-app) navigation

## Render Modes

`Program.cs` calls `.AddInteractiveServerRenderMode()` on `MapRazorComponents<App>()`, which registers interactive-server as an available render mode for the app — it does **not** make every page interactive by default. Each page/component that needs interactivity (`@onclick`, `@bind`, live updates, anything beyond static server-rendered HTML) declares `@rendermode InteractiveServer` explicitly at the top of its `.razor` file, matching the rest of this codebase's pages (`ActivityQueue.razor`, `MovieDetail.razor`, `ActorAdd.razor`, etc. all do this).

```razor
@page "/example"
@rendermode InteractiveServer
```

A page with no `@rendermode` renders once as static HTML and never reconnects to a circuit — fine for genuinely static content, wrong for anything with `@onclick`/`@bind`/live state.

## Circuit Lifecycle: Long-Lived Instances Need Explicit Re-Render Triggers

Once a component is `@rendermode InteractiveServer`, its instance persists for the life of the circuit (the browser tab's SignalR connection) — it is **not** freshly re-created on every in-app navigation the way a static SSR page would be. Real example from this codebase: `NavMenu.razor` became `@rendermode InteractiveServer` to support a live sidebar badge, and as a direct consequence stopped auto-refreshing its "expanded" state on navigation, because nothing was subscribed to `NavigationManager.LocationChanged` — the component simply never re-ran the code that would decide to expand a section (see `Javbuddy.Tests/Components/Layout/NavMenuTests.cs`'s `NavigatingToActorsSection_ExpandsItsSubItems` test and its comment for the full story).

The lesson: once a component stops being freshly rendered per navigation, anything it needs to notice — a route change, a background task completing, another circuit's edit — needs an explicit subscription and re-render call. Don't assume "it worked before `@rendermode InteractiveServer` was added" implies it still will.

## Cross-Circuit Live Updates: the ChangeNotifier Pattern

Each Blazor Server circuit is a separate component-state world — a change made in one browser tab doesn't automatically appear in another tab's already-rendered components. This codebase's pattern for "one open page should notice a change made elsewhere" is a small singleton pub/sub type, e.g. `Services/MovieChangeNotifier.cs` and `Services/TorrentChangeNotifier.cs`:

```csharp
// Singleton: each circuit is a separate component-state world, so this is the one thing
// every circuit shares to signal across that boundary.
public class TorrentChangeNotifier
{
    // Handlers run on whatever thread called NotifyChanged — a component handling this
    // must marshal back onto its own circuit via InvokeAsync before touching state.
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
```

A component that wants to react subscribes in `OnInitialized`/`OnInitializedAsync`, marshals back onto its own circuit with `InvokeAsync`, and unsubscribes in `Dispose`:

```csharp
protected override void OnInitialized()
{
    ChangeNotifier.Changed += OnChanged;
}

private void OnChanged() => InvokeAsync(StateHasChanged);

public void Dispose() => ChangeNotifier.Changed -= OnChanged;
```

The publishing side just calls `ChangeNotifier.NotifyChanged()` after the mutation (see `ActivityQueue.razor` calling it after a grab). Register the notifier as a singleton in `Program.cs`, not scoped — a scoped instance would be per-circuit and defeat the point.

## Enhanced Navigation Drops Runtime-Injected DOM

Blazor's enhanced navigation replaces `<head>` (and body content outside the interactive root) with the *server-rendered* markup of the page being navigated to. Anything a script injected into the DOM at runtime — a `<style>` or `<link>` added by JS after page load, not present in the server-rendered markup — is silently dropped on the next in-app navigation, even though nothing "removed" it explicitly.

Two real examples in this codebase:
- Monaco's editor stylesheet is injected by its own JS on first use; `Components/App.razor` declares a static `<link>` for it in `<head>` instead of relying on Monaco's runtime injection, specifically so it survives enhanced navigation (see the in-file comment above that `<link>`).
- Monaco also generates a `<style class="monaco-colors">` element at runtime (the active theme's token colors) that has no static equivalent — `Components/App.razor`'s `restoreMonacoHead()` re-attaches the same node after enhanced navigation removes it, rather than trying to regenerate it.

If a feature seems to work on first load (full page request) but breaks after clicking around the app, suspect this before anything else — check whether something relies on JS having injected DOM/head content that a fresh server render won't include.

## Blazor CSS Isolation

Covered in `docs/ui-guidelines.md` — `.razor.css` files are scoped per component, and identical class names in two different `.razor.css` files do not share styles.
