using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

/// <summary>The player's apex countdown. Its number comes from a browser-side timer that must show the
/// right value for a paused video and only keep ticking while the video plays, which needs a real video element.</summary>
[Collection(E2ECollection.Name)]
public class ApexCountdownFlowTests
{
    private const string Video = "video.video-player-video";

    // Counts the setInterval timers ApexCountdown.razor.js has live, identified by the module in the call stack.
    private const string TimerTracker = """
        (() => {
            window.__apexTimers = new Set();
            const set = window.setInterval, clear = window.clearInterval;
            window.setInterval = function (fn, ms, ...rest) {
                const id = set.call(this, fn, ms, ...rest);
                if ((new Error().stack || '').includes('ApexCountdown')) window.__apexTimers.add(id);
                return id;
            };
            window.clearInterval = function (id) {
                window.__apexTimers.delete(id);
                return clear.call(this, id);
            };
        })();
        """;

    private readonly E2EFixture fixture;

    public ApexCountdownFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task Countdown_ShowsWhilePaused_AndItsTimerRunsOnlyWhilePlaying()
    {
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, "E2E-APEXCD-1", MovieStatus.Got);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var m = (await db.Movies.FindAsync(movie.Id))!;
            m.MediaDurationSeconds = 3; // the fixture video is 3 s
            await db.SaveChangesAsync();
            await DbSeeding.SeedPlayableVideoAsync(fixture.App.Services, fixture.DbFactory, m);
        }
        using (var scope = fixture.App.Services.CreateScope())
        {
            var added = await scope.ServiceProvider.GetRequiredService<IMovieApexService>().AddApexAsync(movie.Id, 2.5, []);
            Assert.True(added.Success, added.ErrorMessage);
        }

        var page = await fixture.NewPageAsync();
        await page.AddInitScriptAsync(TimerTracker);
        await page.GotoInteractiveAsync($"/movies/{movie.Code}");
        await page.Locator("button.movie-scenes-edit-btn").ClickAsync();
        await page.WaitForFunctionAsync($"() => {{ const v = document.querySelector('{Video}'); return v && v.readyState >= 2 && !v.error; }}");
        var countdown = page.Locator(".apex-countdown");
        Task<int> Timers() => page.EvaluateAsync<int>("() => window.__apexTimers.size");

        // Paused 0.5 s in, 2 s before the apex: the number shows, and no timer is running.
        await page.EvaluateAsync("""
            () => new Promise(resolve => {
                const v = document.querySelector('video.video-player-video');
                v.muted = true;
                v.pause();
                v.addEventListener('seeked', () => resolve(), { once: true });
                v.currentTime = 0.5;
            })
            """);
        await Expect(countdown).ToHaveTextAsync("2");
        await Expect(countdown).ToBeVisibleAsync();
        Assert.Equal(0, await Timers());

        // Playing: the timer runs and the countdown stays visible.
        await page.EvaluateAsync($"() => document.querySelector('{Video}').play()");
        await page.WaitForFunctionAsync("() => window.__apexTimers.size === 1");
        await Expect(countdown).ToBeVisibleAsync();

        // Paused again: the timer is gone and the number is kept.
        await page.EvaluateAsync($"() => document.querySelector('{Video}').pause()");
        await page.WaitForFunctionAsync("() => window.__apexTimers.size === 0");
        await Expect(countdown).ToBeVisibleAsync();
        await Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }
}
