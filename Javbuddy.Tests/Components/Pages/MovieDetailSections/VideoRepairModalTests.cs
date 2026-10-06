using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Javbuddy.Services.VideoRepair;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class VideoRepairModalTests : BunitContext
{
    private readonly IVideoRepairService repairService = Substitute.For<IVideoRepairService>();
    private readonly IVideoRepairJobTracker jobTracker = Substitute.For<IVideoRepairJobTracker>();
    private readonly IFfmpegBinaryResolver ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
    private readonly IMovieCleanupService cleanupService = Substitute.For<IMovieCleanupService>();

    public VideoRepairModalTests()
    {
        ffmpegResolver.IsAvailable.Returns(true);
        Services.AddSingleton(repairService);
        Services.AddSingleton(jobTracker);
        Services.AddSingleton(ffmpegResolver);
        Services.AddSingleton(cleanupService);
    }

    [Fact]
    public void WhenClosed_RendersNothing()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got };
        var cut = Render<VideoRepairModal>(p => p.Add(x => x.Movie, movie));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task WhenOpened_LoadsCandidatesAndRendersTargetFile()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate
                {
                    MovieId = 1,
                    MovieCode = "ABC-123",
                    FileName = "ABC-123.mp4",
                    VersionTag = "Original",
                    FileSizeBytes = 4_000_000_000,
                    ResolutionDisplay = "1080p",
                    DurationSeconds = 7200,
                    IsPrimary = true,
                }
            ]);

        var cut = Render<VideoRepairModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Contains("Repair Video", cut.Find(".videorepair-modal-title").TextContent);
        Assert.Contains("ABC-123.mp4", cut.Find(".target-file-name").TextContent);
        Assert.Contains("1080p", cut.Find(".videorepair-target-card").TextContent);
        Assert.Contains("Start Repair", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public async Task WhenFfmpegNotAvailable_ShowsWarningAndDisablesStart()
    {
        ffmpegResolver.IsAvailable.Returns(false);
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate
                {
                    MovieId = 1,
                    MovieCode = "ABC-123",
                    FileName = "ABC-123.mp4",
                    IsPrimary = true,
                }
            ]);

        var cut = Render<VideoRepairModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Contains("FFmpeg not found", cut.Markup);
        var startBtn = cut.Find("button.btn-primary");
        Assert.True(startBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task ClickingStartRepair_StartsJob()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate
                {
                    MovieId = 1,
                    MovieFileId = 10,
                    MovieCode = "ABC-123",
                    FileName = "ABC-123.mp4",
                    IsPrimary = true,
                }
            ]);

        var tcs = new TaskCompletionSource<VideoRepairResult>();
        var job = new VideoRepairJob
        {
            MovieId = 1,
            MovieFileId = 10,
            MovieCode = "ABC-123",
            FileName = "ABC-123.mp4",
            Task = tcs.Task,
        };
        repairService.StartRepairJobAsync(1, 10, Arg.Any<CancellationToken>()).Returns(job);

        var cut = Render<VideoRepairModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        cut.Find("button.btn-primary").Click();

        await repairService.Received(1).StartRepairJobAsync(1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpeningWhileRepairAlreadyRunning_FollowsJobToCompletion()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate { MovieId = 1, MovieFileId = 10, MovieCode = "ABC-123", FileName = "ABC-123.mp4", IsPrimary = true },
                new VideoRepairCandidate { MovieId = 1, MovieFileId = 11, MovieCode = "ABC-123", FileName = "ABC-123-4K.mp4", VersionTag = "4K" },
            ]);

        // Started elsewhere (e.g. Cleanup page) for the non-primary file, so this component never saw it begin.
        var tcs = new TaskCompletionSource<VideoRepairResult>();
        var job = new VideoRepairJob { MovieId = 1, MovieFileId = 11, MovieCode = "ABC-123", FileName = "ABC-123-4K.mp4", Task = tcs.Task };
        jobTracker.Get(1).Returns(job);
        jobTracker.IsRunning(1).Returns(true);

        var repaired = 0;
        var cut = Render<VideoRepairModal>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.OnRepaired, () => repaired++));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Contains("ABC-123-4K.mp4", cut.Find(".target-file-name").TextContent);
        Assert.Contains("Repairing in progress", cut.Markup);

        jobTracker.IsRunning(1).Returns(false);
        tcs.SetResult(VideoRepairResult.Ok("/x/ABC-123-4K.mp4"));

        cut.WaitForAssertion(() => Assert.Contains("repaired successfully", cut.Find(".alert-success").TextContent));
        Assert.Equal(1, repaired);
    }

    [Fact]
    public async Task WhenMovieHasBrokenBFrames_ShowsUnmarkButton_AndClickingUnmarksAndCloses()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got, HasBrokenBFrames = true };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate { MovieId = 1, MovieCode = "ABC-123", FileName = "ABC-123.mp4", IsPrimary = true }
            ]);
        cleanupService.UnmarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        var repairedInvoked = 0;
        var cut = Render<VideoRepairModal>(p => p
            .Add(x => x.Movie, movie)
            .Add(x => x.OnRepaired, () => repairedInvoked++));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        var unmarkBtn = cut.Find("button.btn-outline-warning");
        Assert.Equal("Unmark Broken Flag", unmarkBtn.TextContent.Trim());

        await cut.InvokeAsync(() => unmarkBtn.Click());

        _ = cleanupService.Received(1).UnmarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>());
        Assert.False(movie.HasBrokenBFrames);
        Assert.Equal(1, repairedInvoked);
        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task WhenMovieDoesNotHaveBrokenBFrames_DoesNotShowUnmarkButton()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got, HasBrokenBFrames = false };
        repairService.GetRepairCandidatesAsync(1, Arg.Any<CancellationToken>())
            .Returns([
                new VideoRepairCandidate { MovieId = 1, MovieCode = "ABC-123", FileName = "ABC-123.mp4", IsPrimary = true }
            ]);

        var cut = Render<VideoRepairModal>(p => p.Add(x => x.Movie, movie));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.Empty(cut.FindAll("button.btn-outline-warning"));
    }
}
