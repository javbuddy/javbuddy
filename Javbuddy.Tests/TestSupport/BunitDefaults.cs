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
