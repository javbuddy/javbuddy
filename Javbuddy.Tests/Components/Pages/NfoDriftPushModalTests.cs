using Bunit;
using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class NfoDriftPushModalTests : BunitContext
{
    private static readonly MovieGridFilter Filter = new(NfoDriftKinds: MovieFilterOptions.NfoDriftKinds);

    private readonly IMovieGridQueryService grid = Substitute.For<IMovieGridQueryService>();
    private readonly INfoSyncService nfoSync = Substitute.For<INfoSyncService>();
    private readonly NfoDriftPushLauncher launcher;
    // The push runs on BackgroundJobRunner's thread pool, not the render loop, so nothing re-renders
    // the modal when it lands — WaitForAssertion (which only re-checks on render) can't wait for it.
    private readonly TaskCompletionSource pushed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public NfoDriftPushModalTests()
    {
        grid.GetNfoDriftCountsAsync(Filter, Arg.Any<CancellationToken>()).Returns(new Dictionary<NfoDriftKind, int>
        {
            [NfoDriftKind.JavbuddyChanged] = 5,
            [NfoDriftKind.ExternalEdit] = 3,
            [NfoDriftKind.BothChanged] = 1,
            [NfoDriftKind.Unreadable] = 2,
        });
        grid.GetNfoDriftMovieIdsAsync(Filter, Arg.Any<IReadOnlyCollection<NfoDriftKind>>(), Arg.Any<CancellationToken>())
            .Returns([10, 11]);
        nfoSync.PushNfoDriftAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<bool>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pushed.TrySetResult();
                return new NfoDriftPushResult(2, 0, 0, 0);
            });

        var jobServices = new ServiceCollection().AddSingleton(nfoSync).BuildServiceProvider();
        var runner = new BackgroundJobRunner(jobServices.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        launcher = new NfoDriftPushLauncher(runner, new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System), new MovieChangeNotifier());

        Services.AddSingleton(grid);
        Services.AddSingleton(launcher);
    }

    [Fact]
    public void ShowsCountsPerDirection_JavbuddyChangedTickedAndOutsideEditsNot()
    {
        var cut = Render<NfoDriftPushModal>(p => p.Add(x => x.Filter, Filter));

        cut.WaitForAssertion(() => Assert.Contains("Javbuddy changed (5)", cut.Markup));
        Assert.Contains("External edit (3) / Both changed (1)", cut.Markup);
        Assert.True(cut.Find("#nfo-push-javbuddy").HasAttribute("checked"));
        Assert.False(cut.Find("#nfo-push-external").HasAttribute("checked"));
        Assert.Empty(cut.FindAll(".nfo-push-warning"));
        Assert.Equal("Write 5 .nfo file(s)", cut.Find("button.btn-primary").TextContent.Trim());
    }

    [Fact]
    public async Task Confirm_WithDefaults_PushesOnlyJavbuddyChangedMovies()
    {
        var closed = false;
        var cut = Render<NfoDriftPushModal>(p => p
            .Add(x => x.Filter, Filter)
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => closed = true)));
        cut.WaitForAssertion(() => Assert.Contains("Javbuddy changed (5)", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("button.btn-primary").Click());

        Assert.True(closed);
        await grid.Received(1).GetNfoDriftMovieIdsAsync(
            Filter, Arg.Is<IReadOnlyCollection<NfoDriftKind>>(k => k.SequenceEqual(new[] { NfoDriftKind.JavbuddyChanged })), Arg.Any<CancellationToken>());
        await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await nfoSync.Received(1).PushNfoDriftAsync(
            Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 10, 11 })), false, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TickingOutsideEdits_WarnsAndIncludesThem()
    {
        var cut = Render<NfoDriftPushModal>(p => p.Add(x => x.Filter, Filter));
        cut.WaitForAssertion(() => Assert.Contains("Javbuddy changed (5)", cut.Markup));

        cut.Find("#nfo-push-external").Change(true);

        Assert.Contains("overwrite", cut.Find(".nfo-push-warning").TextContent);
        Assert.Equal("Write 9 .nfo file(s)", cut.Find("button.btn-primary").TextContent.Trim());

        await cut.InvokeAsync(() => cut.Find("button.btn-primary").Click());

        await grid.Received(1).GetNfoDriftMovieIdsAsync(
            Filter,
            Arg.Is<IReadOnlyCollection<NfoDriftKind>>(k => k.Order().SequenceEqual(new[] { NfoDriftKind.JavbuddyChanged, NfoDriftKind.ExternalEdit, NfoDriftKind.BothChanged })),
            Arg.Any<CancellationToken>());
        await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await nfoSync.Received(1).PushNfoDriftAsync(
            Arg.Any<IReadOnlyList<int>>(), true, Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NothingTicked_DisablesTheWriteButton()
    {
        var cut = Render<NfoDriftPushModal>(p => p.Add(x => x.Filter, Filter));
        cut.WaitForAssertion(() => Assert.Contains("Javbuddy changed (5)", cut.Markup));

        cut.Find("#nfo-push-javbuddy").Change(false);

        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }
}
