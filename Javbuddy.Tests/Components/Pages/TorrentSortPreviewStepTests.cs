using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers TorrentSort.razor's Preview step step-indicator/organize UI: while
/// State.Organize.Organizing is true, "6. Preview" should show as done and "7. Organize" as active instead of
/// the reverse, and "Back to Review" should be disabled so users can't navigate away mid-move.</summary>
public class TorrentSortPreviewStepTests : BunitContext
{
    private (TestDbContextFactory Factory, int TorrentDownloadId, ITorrentSortService TorrentSortService)
        SetUpServices()
    {
        var factory = new TestDbContextFactory();
        int torrentDownloadId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", ContentPath = "/media/x" };
            db.TorrentDownloads.Add(torrent);
            db.SaveChanges();
            torrentDownloadId = torrent.Id;
        }

        var torrentSortService = Substitute.For<ITorrentSortService>();
        torrentSortService.GetJavinizerBaseUrlAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(false);

        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegResolver);
        Services.AddSingleton(state);

        return (factory, torrentDownloadId, torrentSortService);
    }

    private async Task<IRenderedComponent<TorrentSort>> RenderAtPreviewStepAsync(int torrentDownloadId)
    {
        var state = Services.GetRequiredService<TorrentSortWizardState>();
        await state.InitializeAsync(torrentDownloadId);
        state.Step = WizardStep.Preview;

        // TorrentSort's own OnInitializedAsync calls State.InitializeAsync again on render, which
        // resets Destination to "" (no destination aliases configured in these tests) — so this must
        // be set after Render, not before, or OrganizeAsync would be invoked with an empty destination.
        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        state.Destination = "/media/jav";
        return cut;
    }

    [Fact]
    public async Task WhileOrganizing_StepIndicatorMarksPreviewDoneAndOrganizeActive()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices();
        using var f = factory;
        var state = Services.GetRequiredService<TorrentSortWizardState>();

        var gate = new TaskCompletionSource<TorrentOrganizeResult>();
        sortService.OrganizeAsync(torrentDownloadId, "/media/jav", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = await RenderAtPreviewStepAsync(torrentDownloadId);
        cut.Find("button:contains('Organize')").Click();
        cut.WaitForState(() => state.Organize.Organizing);

        var steps = cut.FindAll("ol.sort-steps li");
        Assert.Equal("sort-step-done", steps[5].ClassName);
        Assert.Equal("sort-step-active", steps[6].ClassName);

        gate.SetResult(new TorrentOrganizeResult(true, null));
        cut.WaitForState(() => !state.Organize.Organizing);
    }

    [Fact]
    public async Task WhileOrganizing_BackToReviewButtonIsDisabled()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices();
        using var f = factory;
        var state = Services.GetRequiredService<TorrentSortWizardState>();

        var gate = new TaskCompletionSource<TorrentOrganizeResult>();
        sortService.OrganizeAsync(torrentDownloadId, "/media/jav", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = await RenderAtPreviewStepAsync(torrentDownloadId);
        cut.Find("button:contains('Organize')").Click();
        cut.WaitForState(() => state.Organize.Organizing);

        var backButton = cut.Find("button:contains('Back to Review')");
        Assert.True(backButton.HasAttribute("disabled"));

        gate.SetResult(new TorrentOrganizeResult(true, null));
        cut.WaitForState(() => !state.Organize.Organizing);
    }

    [Fact]
    public async Task AfterOrganizeSucceeds_OrganizeStepStaysActive()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices();
        using var f = factory;
        var state = Services.GetRequiredService<TorrentSortWizardState>();

        sortService.OrganizeAsync(torrentDownloadId, "/media/jav", Arg.Any<CancellationToken>())
            .Returns(new TorrentOrganizeResult(true, null));

        var cut = await RenderAtPreviewStepAsync(torrentDownloadId);
        cut.Find("button:contains('Organize')").Click();
        cut.WaitForState(() => state.Step == WizardStep.Done);

        var steps = cut.FindAll("ol.sort-steps li");
        Assert.Equal("sort-step-active", steps[6].ClassName);
    }
}
