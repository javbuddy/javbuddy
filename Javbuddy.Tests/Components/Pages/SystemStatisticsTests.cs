using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Statistics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class SystemStatisticsTests : BunitContext
{
    private static LibraryStatistics Stats() => new(
        Movies: 7, MoviesGot: 5, MoviesMissing: 2, MoviesVr: 0, MoviesFavorite: 0, TotalBytes: 0, TotalRuntimeHours: 0,
        Actors: 0, ActorsFavorite: 0, Tags: 0, TagsNeedingReview: 0, Scenes: 0, Highlights: 0, Apexes: 0,
        [], [], [], [], [], []);

    [Fact]
    public void ShowsLoadingIndicatorUntilStatisticsResolve_ThenRendersThem()
    {
        var tcs = new TaskCompletionSource<LibraryStatistics>();
        var service = Substitute.For<ILibraryStatisticsService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(tcs.Task);
        Services.AddSingleton(service);

        var cut = Render<SystemStatistics>();

        Assert.DoesNotContain("stats-tiles", cut.Markup);
        Assert.Contains("loading", cut.Markup, StringComparison.OrdinalIgnoreCase);

        tcs.SetResult(Stats());

        cut.WaitForAssertion(() => Assert.Contains("stats-tiles", cut.Markup));
    }

    [Fact]
    public void ShowsAnErrorInsteadOfTheSpinner_WhenLoadingFails()
    {
        var service = Substitute.For<ILibraryStatisticsService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<LibraryStatistics>(new InvalidOperationException("boom")));
        Services.AddSingleton(service);

        var cut = Render<SystemStatistics>();

        cut.WaitForAssertion(() => Assert.Contains("Couldn't load the statistics", cut.Markup));
        Assert.DoesNotContain("loading", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
