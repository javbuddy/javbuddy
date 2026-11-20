using System.Runtime.CompilerServices;
using Bunit;

namespace Javbuddy.Tests.TestSupport;

internal static class BunitDefaults
{
    /// <summary>bUnit's WaitFor helpers give up after 1 second by default, which a busy CI runner
    /// running the whole suite in parallel overshoots (a 250ms search debounce plus the reload it
    /// triggers, or a background job's continuation). A passing wait returns as soon as its
    /// condition holds, so the longer ceiling only slows down a test that fails anyway.</summary>
    [ModuleInitializer]
    internal static void Initialize() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
}

internal static class BunitJSInteropExtensions
{
    /// <summary>ModalShell imports its focus-on-open module whenever a dialog opens; bUnit's default Strict
    /// JSInterop throws on that unconfigured import, so a test that renders any ModalShell host allows it here
    /// (a test of the focus call itself sets the module up explicitly instead).</summary>
    internal static void AllowModalShell(this BunitJSInterop jsInterop) =>
        jsInterop.SetupModule("./Components/Shared/ModalShell.razor.js").Mode = JSRuntimeMode.Loose;
}

/// <summary>A BunitContext with its <c>RendererInfo</c> set, which bUnit leaves unset (a component reading
/// <c>RendererInfo.IsInteractive</c> then throws). <see cref="Interactive"/> (true by default) is applied before
/// the first render of any kind, so every render path sees it; a test of the prerender sets it to false first.
/// It can't be set in the constructor: bUnit builds its service provider when the renderer info is set, and tests
/// still register their services after construction. Because Render is not virtual, each public render entry
/// point is hidden here to apply it.</summary>
public abstract class RendererInfoBunitContext : BunitContext
{
    private bool rendererInfoApplied;

    protected bool Interactive { get; set; } = true;

    private void ApplyRendererInfo()
    {
        if (rendererInfoApplied) return;
        rendererInfoApplied = true;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo(Interactive ? "Server" : "Static", Interactive));
    }

    public new IRenderedComponent<TComponent> Render<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        ApplyRendererInfo();
        return base.Render(parameterBuilder);
    }

    public new IRenderedComponent<TComponent> Render<TComponent>(Microsoft.AspNetCore.Components.RenderFragment renderFragment)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        ApplyRendererInfo();
        return base.Render<TComponent>(renderFragment);
    }

    public new IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> Render(Microsoft.AspNetCore.Components.RenderFragment renderFragment)
    {
        ApplyRendererInfo();
        return base.Render(renderFragment);
    }
}

/// <summary>Renders as the interactive circuit does (<c>RendererInfo.IsInteractive</c> is true).</summary>
public abstract class InteractiveBunitContext : RendererInfoBunitContext;

/// <summary>Renders as the prerender does (<c>RendererInfo.IsInteractive</c> is false).</summary>
public abstract class PrerenderBunitContext : RendererInfoBunitContext
{
    protected PrerenderBunitContext() => Interactive = false;
}
