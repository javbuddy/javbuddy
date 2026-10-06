using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class MoviesGridScrollTests(E2EFixture fixture)
{
    /// <summary>Regression test for a real bug: Movies.razor.js used to resize the grid's leading
    /// spacer via a JS-invokable call fired *after* the card list had already re-rendered — two
    /// separate SignalR round trips. For the frame in between, the cards already showed the new
    /// window but the spacer was still the old height, which broke the invariant this file's
    /// header comment describes ("the document's total scroll height is the same no matter where
    /// the window sits") and read to the user as a scroll-then-snap-back twitch on every window
    /// slide. bUnit can't catch this — it re-parses its DOM every render, so there's no real
    /// paint/timing gap to observe — which is why this needs a real browser against the real
    /// SignalR circuit.
    ///
    /// Asserting on the grid's own rendered height (rather than, say, window.scrollY) is
    /// deliberate: scrollY is something the test itself repeatedly sets while driving the scroll,
    /// so a transient correction can land in the same tick as — and get masked by — the test's own
    /// next write. The grid's height is never written by the test at all, only ever measured, so
    /// there's nothing to mask a violation.</summary>
    [Fact]
    public async Task ScrollingThroughManyWindowSlides_NeverChangesTheGridsTotalHeight()
    {
        // Enough rows that a long scroll forces the window to slide (not just grow) many times
        // over, maximizing the chance of reproducing the race if it still exists.
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 400, "E2E-SCROLL");
        var page = await fixture.NewPageAsync();
        // Pinned rather than relying on Playwright's default, so the row/column geometry this test
        // reasons about (see the seed count above) doesn't silently drift if that default changes.
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        // Scrolls in fixed steps, one per animation frame, using 'instant' (not the default, which
        // would inherit the app's `scroll-behavior: smooth` and make the browser itself animate
        // every step) — sampling the grid's rendered height every frame along the way. 175 frames
        // of 80px cover the same 14000px (so the same number of window slides) as the original
        // 350 of 40px in half the ~60fps wall-clock time. The height
        // is expected to be whatever it settles to almost immediately and then hold rock steady;
        // any later deviation from that is the bug, regardless of which direction it moves.
        var heightsJson = await page.EvaluateAsync<string>(
            """
            async () => {
                const grid = document.querySelector('.movies-grid');
                window.scrollTo({ top: 0, behavior: 'instant' });
                const heights = [];
                await new Promise(resolve => {
                    let y = 0;
                    let frames = 0;
                    const stepPx = 80;
                    const maxFrames = 175;
                    function tick() {
                        heights.push(grid.getBoundingClientRect().height);
                        y += stepPx;
                        window.scrollTo({ top: y, behavior: 'instant' });
                        frames++;
                        if (frames < maxFrames) requestAnimationFrame(tick); else resolve();
                    }
                    requestAnimationFrame(tick);
                });
                return JSON.stringify(heights);
            }
            """);
        var heights = System.Text.Json.JsonSerializer.Deserialize<List<double>>(heightsJson)!;

        // The very first few frames legitimately move: the initial window is smaller than what
        // the viewport needs, so it's still growing (spacer shrinking, grid growing, net height
        // unchanged) before anything has had a chance to *slide*. Settle on whatever height the
        // trace ends on and flag anything that doesn't match it, rather than assuming frame 0 is
        // already the steady state.
        var settled = heights[^1];
        var deviations = heights
            .Select((height, frame) => (frame, height))
            .Where(x => Math.Abs(x.height - settled) > 1)
            .ToList();

        Assert.True(deviations.Count == 0,
            $"Grid height should hold steady at {settled}px once settled, but deviated " +
            $"{deviations.Count} time(s): " +
            string.Join(", ", deviations.Take(5).Select(d => $"frame {d.frame}: {d.height}px")));

        // Confirms the scroll actually pushed the loaded window past its initial load — i.e. this
        // test exercised the window-slide code path rather than passing trivially because nothing
        // ever slid.
        var windowStart = await page.Locator(".movies-grid .virtualized-grid-spacer").First.GetAttributeAsync("data-window-start");
        Assert.True(int.Parse(windowStart!) > 0, $"Expected the window to have slid forward; data-window-start was '{windowStart}'.");
    }

    /// <summary>Regression test: a first fix attempt saved window.scrollY to
    /// sessionStorage unconditionally on every 'scroll'/'resize' event, but enhanced navigation
    /// patches the DOM to the destination page (and scrolls a forward navigation to top) well
    /// before this component's async DisposeAsync round-trips back to JS to detach those window
    /// listeners — so the detail page's own scroll-to-top was still reaching the stale Movies-page
    /// listener and clobbering the saved position with 0 right before it was needed for the
    /// back-navigation restore. bUnit can't catch this: it never tears down real DOM nodes or
    /// leaves stale listeners attached to `window` the way a real enhanced-nav swap does.</summary>
    [Fact]
    public async Task NavigatingToDetailAndBack_RestoresTheGridsScrollPosition()
    {
        // Enough rows that the grid's virtualized window has clearly slid (not just grown) once
        // scrolled deep in — restoring to the very top would otherwise look identical to a bug.
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 500, "E2E-BACKNAV");
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        await page.EvaluateAsync("window.scrollTo({ top: 6000, behavior: 'instant' })");
        // Waits for the window to actually have slid off the first page, i.e. for the SetVisibleRange
        // round trip triggered by that scroll to have landed and re-rendered the spacer — not just
        // for the scroll itself to have taken effect.
        await page.WaitForFunctionAsync(
            "document.querySelector('.movies-grid .virtualized-grid-spacer').dataset.windowStart !== '0'");
        var scrollYBeforeNav = await page.EvaluateAsync<double>("window.scrollY");

        // Picks a card whose bounding rect is entirely within the current viewport (not just any
        // rendered/":visible" one — the topmost *loaded* card sits above the viewport at scrollY
        // 6000 and Playwright's click would auto-scroll it into view first, itself moving scrollY
        // away from what this test just set and is trying to verify gets restored).
        var inViewCardIndex = await page.EvaluateAsync<int>(
            """
            () => Array.from(document.querySelectorAll('.poster-card')).findIndex(c => {
                const r = c.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            })
            """);
        Assert.True(inViewCardIndex >= 0, "Expected at least one poster card fully inside the viewport after scrolling.");

        await page.Locator(".poster-card").Nth(inViewCardIndex).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/movies/"));

        await page.GetByRole(AriaRole.Link, new() { Name = "All movies" }).First.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        // Polls rather than reading window.scrollY once: the restore itself is driven by an async
        // JS-interop round trip (Movies.razor's OnAfterRenderAsync -> module.InvokeVoidAsync("init"))
        // that "cards are visible" doesn't wait for, so a same-tick read can catch scrollY still at
        // its pre-restore value even though the restore is about to land a moment later. A little
        // slack in the target is for legitimate row-height rounding between the two windows'
        // geometry — what this guards against is the bug's actual failure mode, landing at/near 0.
        try
        {
            await page.WaitForFunctionAsync(
                $"window.scrollY > {scrollYBeforeNav - 500}",
                new PageWaitForFunctionOptions { Timeout = 3000 });
        }
        catch (TimeoutException)
        {
            var scrollYAfterBack = await page.EvaluateAsync<double>("window.scrollY");
            Assert.Fail(
                $"Expected scroll position to be restored near {scrollYBeforeNav}px, but was " +
                $"{scrollYAfterBack}px after waiting — navigating back reset the grid instead of " +
                "preserving where the user left it.");
        }
    }

    /// <summary>Regression test: the restore used to wait for the interactive
    /// circuit, the grid's JS module import and a requestAnimationFrame retry loop, so the user saw the
    /// top of the page for a moment before it snapped down. App.razor's enhancedload handler now
    /// restores from the spacer heights and scroll position the grid saved, so scrollY is already
    /// back by the time the navigation's DOM patch finishes — this reads it synchronously from
    /// another enhancedload listener (registered later, so it runs after that handler).</summary>
    [Fact]
    public async Task NavigatingBack_RestoresTheScrollPositionAtEnhancedLoad_NotAfterTheCircuitCatchesUp()
    {
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 500, "E2E-EARLYRESTORE");
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        await page.EvaluateAsync("window.scrollTo({ top: 6000, behavior: 'instant' })");
        await page.WaitForFunctionAsync(
            "document.querySelector('.movies-grid .virtualized-grid-spacer').dataset.windowStart !== '0'");
        var scrollYBeforeNav = await page.EvaluateAsync<double>("window.scrollY");

        var inViewCardIndex = await page.EvaluateAsync<int>(
            """
            () => Array.from(document.querySelectorAll('.poster-card')).findIndex(c => {
                const r = c.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            })
            """);
        Assert.True(inViewCardIndex >= 0, "Expected at least one poster card fully inside the viewport after scrolling.");

        await page.Locator(".poster-card").Nth(inViewCardIndex).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/movies/"));

        await page.EvaluateAsync(
            """
            window.__scrollAtEnhancedLoad = null;
            Blazor.addEventListener('enhancedload', () => {
                if (location.pathname === '/') window.__scrollAtEnhancedLoad = window.scrollY;
            });
            """);
        await page.GetByRole(AriaRole.Link, new() { Name = "All movies" }).First.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        await page.WaitForFunctionAsync("window.__scrollAtEnhancedLoad !== null");

        var scrollAtEnhancedLoad = await page.EvaluateAsync<double>("window.__scrollAtEnhancedLoad");
        Assert.True(scrollAtEnhancedLoad > scrollYBeforeNav - 500,
            $"Expected scrollY to already be near {scrollYBeforeNav}px when the navigation's DOM patch " +
            $"finished, but it was {scrollAtEnhancedLoad}px — the restore still waits for the circuit.");
    }

    /// <summary>Regression test: clicking a movie card while scrolled deep in the
    /// grid used to visibly animate the outgoing page scrolling back to the top before the detail
    /// page replaced it. Root cause confirmed live by instrumenting window.scrollTo: Blazor's
    /// enhanced navigation resets scroll via the two-argument `window.scrollTo(0, 0)`, which carries
    /// no explicit `behavior` and so inherits Bootstrap reboot's `:root { scroll-behavior: smooth; }`
    /// instead of jumping instantly — the same global setting Movies.razor.js's own scrollToTop()
    /// already has to work around explicitly. app.css now forces `scroll-behavior: auto` back on
    /// site-wide.
    ///
    /// A single 'scroll' event firing at y=0 is the *correct*, instant outcome (any scroll position
    /// change fires one, whether instant or animated) — what actually distinguishes an animation is
    /// multiple events spread out over real time as the position eases toward 0. So this asserts on
    /// the elapsed time between the first and last event, not the event count.</summary>
    [Fact]
    public async Task NavigatingToDetail_DoesNotAnimateScrollToTop()
    {
        await DbSeeding.SeedManyMoviesAsync(fixture.DbFactory, 500, "E2E-NAVSCROLL");
        var page = await fixture.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoInteractiveAsync("/");
        await Expect(page.Locator(".poster-card").First).ToBeVisibleAsync();

        await page.EvaluateAsync("window.scrollTo({ top: 3000, behavior: 'instant' })");
        await page.WaitForFunctionAsync(
            "document.querySelector('.movies-grid .virtualized-grid-spacer').dataset.windowStart !== '0'");

        // Fully in-viewport (not just ".First", which sits above the fold at this scroll position)
        // so Playwright's click doesn't itself scroll the page and pollute the event log below.
        var inViewCardIndex = await page.EvaluateAsync<int>(
            """
            () => Array.from(document.querySelectorAll('.poster-card')).findIndex(c => {
                const r = c.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            })
            """);
        Assert.True(inViewCardIndex >= 0, "Expected at least one poster card fully inside the viewport after scrolling.");

        // Armed right before the click, not earlier, so the setup's own scrollTo above doesn't
        // pollute the log this test actually cares about.
        await page.EvaluateAsync(
            """
            () => {
                window.__scrollEvents = [];
                window.addEventListener('scroll', () => window.__scrollEvents.push([performance.now(), window.scrollY]), { passive: true });
            }
            """);

        await page.Locator(".poster-card").Nth(inViewCardIndex).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/movies/"));
        // Gives any animated scroll a moment to play out before reading the log back — a wide-enough
        // margin below this that a genuine multi-frame smooth-scroll animation (Chrome's default
        // duration for a jump this size is in the hundreds of ms) can't finish inside it undetected.
        await page.WaitForTimeoutAsync(300);

        var scrollEvents = await page.EvaluateAsync<double[][]>("window.__scrollEvents");
        if (scrollEvents.Length > 1)
        {
            var span = scrollEvents[^1][0] - scrollEvents[0][0];
            Assert.True(span < 100,
                $"Expected navigating to the detail page to jump instantly, but {scrollEvents.Length} " +
                $"'scroll' events spanned {span}ms (values: [{string.Join(", ", scrollEvents.Select(s => s[1]))}]) " +
                "— the outgoing page's scroll-to-top is animating instead of jumping instantly.");
        }
    }
}
