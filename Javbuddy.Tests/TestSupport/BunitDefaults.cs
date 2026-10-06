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
