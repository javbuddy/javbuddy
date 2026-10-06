using Microsoft.Playwright;

namespace Javbuddy.E2ETests.Support;

public static class PlaywrightExtensions
{
    /// <summary>Clicks <paramref name="button"/> and waits for <paramref name="expected"/> to appear,
    /// clicking again if it doesn't. A click on a server-rendered Blazor Server page that lands before
    /// its circuit is interactive is silently dropped, and a slow runner can lose that race. It only
    /// re-clicks while the button is still there, so a slow-but-successful first click isn't undone by a
    /// second one. If nothing appears, returns so the caller's own assertion reports the failure.</summary>
    public static async Task ClickUntilVisibleAsync(this ILocator button, ILocator expected)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!await button.IsVisibleAsync()) break;
            await button.ClickAsync();
            try
            {
                await expected.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                return;
            }
            catch (TimeoutException)
            {
            }
        }
    }

    // blazor.web.js's per-element handler store, a `_blazorEvents_<n>` property it sets on an element
    // when it attaches an @on… handler (EventDelegator.eventsCollectionKey). Prerendered markup never
    // has it, so it marks DOM the circuit owns. Internal: recheck it after a framework upgrade if
    // these helpers start timing out.
    private const string HasBlazorHandler = "el => Object.keys(el).some(k => k.startsWith('_blazorEvents_'))";

    /// <summary>GotoAsync for an interactive Blazor page: returns once the circuit has attached the
    /// page, i.e. once anything in &lt;main&gt; carries a Blazor event handler. A server-rendered page
    /// shows its controls before its circuit is interactive, and a click or input landing in that
    /// window is silently dropped (or, for a link, followed with the circuit still on the old URL).
    /// Polled, so it survives the interactive render replacing the prerendered nodes. A page with no
    /// handler in &lt;main&gt; never satisfies it; use plain GotoAsync for one.</summary>
    public static async Task<IResponse?> GotoInteractiveAsync(this IPage page, string url)
    {
        var response = await page.GotoAsync(url);
        // Interval polling: some tests stub out requestAnimationFrame, Playwright's default.
        await page.WaitForFunctionAsync(
            $"() => [...document.querySelectorAll('main *')].some({HasBlazorHandler})",
            null,
            new() { PollingInterval = 50 });
        return response;
    }

    /// <summary>Waits until Blazor has attached an event handler to <paramref name="element"/>: for a
    /// control reached by in-app navigation, which has the same prerender window as a fresh load.
    /// Point it at an element that has an @on… handler; everything that component rendered is
    /// interactive by then.</summary>
    public static async Task WaitForInteractiveAsync(this ILocator element, int timeoutMs = 10000)
    {
        // Re-resolved on every check: the interactive render can replace the prerendered nodes.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!await element.First.EvaluateAsync<bool>(HasBlazorHandler))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The element never became interactive.");
            await Task.Delay(50);
        }
    }
}
