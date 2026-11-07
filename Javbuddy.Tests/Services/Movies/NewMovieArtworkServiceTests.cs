using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public sealed class NewMovieArtworkServiceTests : IDisposable
{
    private readonly TestDbContextFactory db = new();
    private readonly INewMovieArtworkSettingsService settings = Substitute.For<INewMovieArtworkSettingsService>();
    private readonly ILocalLibraryClient library = Substitute.For<ILocalLibraryClient>();
    private readonly IMovieExtraFanartStillsService stills = Substitute.For<IMovieExtraFanartStillsService>();
    private readonly IMoviePosterGenerationService posters = Substitute.For<IMoviePosterGenerationService>();
    private readonly List<string> scratchDirs = [];

    public NewMovieArtworkServiceTests()
    {
        SetSettings(extraFanart: true, poster: true);
        library.AnyRootReachableAsync(Arg.Any<CancellationToken>()).Returns(true);
        library.ResolveMovieFolderPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("/media/x");
        posters.GenerateAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        stills.PreviewAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), Arg.Any<bool>(), Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ExtraFanartStillsPreviewResult(NewPreview()));
        stills.SaveAsync(Arg.Any<int>(), Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(2));
    }

    public void Dispose()
    {
        db.Dispose();
        foreach (var dir in scratchDirs.Where(Directory.Exists)) Directory.Delete(dir, recursive: true);
    }

    private void SetSettings(bool extraFanart, bool poster) =>
        settings.GetEffectiveAsync(Arg.Any<CancellationToken>())
            .Returns(new NewMovieArtworkSettings { GenerateExtraFanart = extraFanart, GeneratePosterAndFanart = poster });

    private ExtraFanartStillsPreview NewPreview()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "javbuddy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        scratchDirs.Add(scratch);
        return new ExtraFanartStillsPreview(scratch, [new ExtraFanartStill(1, 10, []), new ExtraFanartStill(2, 20, [])]);
    }

    private NewMovieArtworkService Service() =>
        new(db, settings, library, stills, posters, NullLogger<NewMovieArtworkService>.Instance);

    private async Task<int> AddMovieAsync(string? vrType = null, double? duration = 600, bool pending = true, bool probed = true)
    {
        await using var context = await db.CreateDbContextAsync();
        var movie = new Movie { Code = "ABC-" + Guid.NewGuid().ToString("N")[..6], VrType = vrType, MediaDurationSeconds = duration, NewArtworkPending = pending };
        if (probed) movie.MovieFiles.Add(new MovieFile { FileName = movie.Code + ".mp4", IsPrimary = true });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie.Id;
    }

    private async Task<bool> IsPendingAsync(int id)
    {
        await using var context = await db.CreateDbContextAsync();
        return (await context.Movies.AsNoTracking().SingleAsync(m => m.Id == id)).NewArtworkPending;
    }

    [Fact]
    public async Task GeneratesStillsAndPoster_ForAPendingMovie_AndClearsTheMarker()
    {
        var id = await AddMovieAsync();

        var generated = await Service().GeneratePendingAsync();

        Assert.Equal(1, generated);
        await posters.Received(1).GenerateAsync(id, true, Arg.Any<CancellationToken>());
        await stills.Received(1).PreviewAsync(id, MovieExtraFanartStillsService.DefaultCount, ExtraFanartStillsMode.OnlyIfNone, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>());
        await stills.Received(1).SaveAsync(id, Arg.Any<ExtraFanartStillsPreview>(), Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })),
            MovieExtraFanartStillsService.DefaultQuality, ExtraFanartStillsMode.OnlyIfNone, Arg.Any<CancellationToken>());
        Assert.False(await IsPendingAsync(id));
    }

    [Fact]
    public async Task LeavesAMovieWithoutAProbedDuration_PendingUntilItHasOne()
    {
        var noDuration = await AddMovieAsync(duration: null);
        var zero = await AddMovieAsync(duration: 0);

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await posters.DidNotReceiveWithAnyArgs().GenerateAsync(default, default, default);
        Assert.True(await IsPendingAsync(noDuration));
        Assert.True(await IsPendingAsync(zero));
    }

    [Fact]
    public async Task LeavesAMovieTheProbeStepHasNotFinished_Pending()
    {
        // Import already writes the duration; the file rows (and the VR format the crop depends on) come from the probe step.
        var id = await AddMovieAsync(probed: false);

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await posters.DidNotReceiveWithAnyArgs().GenerateAsync(default, default, default);
        Assert.True(await IsPendingAsync(id));
    }

    [Fact]
    public async Task WhenNoLibraryRootIsReachable_DoesNothing_AndKeepsEveryMarker()
    {
        library.AnyRootReachableAsync(Arg.Any<CancellationToken>()).Returns(false);
        var id = await AddMovieAsync();

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await posters.DidNotReceiveWithAnyArgs().GenerateAsync(default, default, default);
        Assert.True(await IsPendingAsync(id));
    }

    [Fact]
    public async Task AMovieWhoseFolderCantBeFound_KeepsItsMarker_AndTheOthersAreStillProcessed()
    {
        var lost = await AddMovieAsync();
        var found = await AddMovieAsync();
        string lostCode;
        await using (var context = await db.CreateDbContextAsync()) lostCode = (await context.Movies.SingleAsync(m => m.Id == lost)).Code!;
        library.ResolveMovieFolderPathAsync(lostCode, Arg.Any<CancellationToken>()).Returns((string?)null);

        var generated = await Service().GeneratePendingAsync();

        Assert.Equal(1, generated);
        await posters.DidNotReceive().GenerateAsync(lost, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.True(await IsPendingAsync(lost));
        Assert.False(await IsPendingAsync(found));
    }

    [Fact]
    public async Task TurningTheSettingsOffMidPass_StopsGeneratingForTheRest()
    {
        var first = await AddMovieAsync();
        var second = await AddMovieAsync();
        posters.GenerateAsync(first, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            SetSettings(extraFanart: false, poster: false);
            return OperationResult.Ok();
        });

        await Service().GeneratePendingAsync();

        await posters.DidNotReceive().GenerateAsync(second, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.False(await IsPendingAsync(second));
    }

    [Fact]
    public async Task IgnoresMoviesThatAreNotPending()
    {
        await AddMovieAsync(pending: false);

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await posters.DidNotReceiveWithAnyArgs().GenerateAsync(default, default, default);
    }

    [Fact]
    public async Task WithBothSettingsOff_DoesNothing_ButStillClearsTheMarker()
    {
        SetSettings(extraFanart: false, poster: false);
        var id = await AddMovieAsync();

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await posters.DidNotReceiveWithAnyArgs().GenerateAsync(default, default, default);
        await stills.DidNotReceiveWithAnyArgs().PreviewAsync(default, default, default, default, default, default);
        Assert.False(await IsPendingAsync(id));
    }

    [Fact]
    public async Task OnlyRunsTheEnabledGenerator()
    {
        SetSettings(extraFanart: false, poster: true);
        var id = await AddMovieAsync();

        await Service().GeneratePendingAsync();

        await posters.Received(1).GenerateAsync(id, true, Arg.Any<CancellationToken>());
        await stills.DidNotReceiveWithAnyArgs().PreviewAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task CropsToTheLeftEye_ForSideBySideVr_ButNotForHalfWidth()
    {
        var sbs = await AddMovieAsync(VrFormat.Vr180Sbs);
        var half = await AddMovieAsync(VrFormat.ThreeDHsbs);

        await Service().GeneratePendingAsync();

        await stills.Received(1).PreviewAsync(sbs, Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), true, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>());
        await stills.Received(1).PreviewAsync(half, Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACountOfZero_WhenNothingCouldBeGenerated_ButTheMarkerIsStillCleared()
    {
        posters.GenerateAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("already has both"));
        stills.PreviewAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), Arg.Any<bool>(), Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>())
            .Returns(new ExtraFanartStillsPreviewResult(null, "The movie already has extrafanart images."));
        var id = await AddMovieAsync();

        Assert.Equal(0, await Service().GeneratePendingAsync());

        await stills.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default!, default, default, default);
        Assert.False(await IsPendingAsync(id));
    }

    [Fact]
    public async Task AMovieThatThrows_DoesNotStopTheOthers()
    {
        var first = await AddMovieAsync();
        var second = await AddMovieAsync();
        posters.GenerateAsync(first, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns<Task<OperationResult>>(_ => throw new IOException("disk"));

        var generated = await Service().GeneratePendingAsync();

        Assert.Equal(1, generated);
        await posters.Received(1).GenerateAsync(second, true, Arg.Any<CancellationToken>());
        Assert.False(await IsPendingAsync(first));
        Assert.False(await IsPendingAsync(second));
    }

    [Fact]
    public async Task TheStillsScratchFolder_IsDeleted_WhetherOrNotTheSaveSucceeds()
    {
        await AddMovieAsync();
        await AddMovieAsync();
        stills.SaveAsync(Arg.Any<int>(), Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<int>(), Arg.Any<ExtraFanartStillsMode>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(2), ExtraFanartResult.Fail("disk full"));

        await Service().GeneratePendingAsync();

        Assert.Equal(2, scratchDirs.Count);
        Assert.All(scratchDirs, dir => Assert.False(Directory.Exists(dir)));
    }

    [Fact]
    public async Task Cancellation_StopsTheLoop_AndKeepsTheRemainingMarkers()
    {
        var first = await AddMovieAsync();
        var second = await AddMovieAsync();
        using var cts = new CancellationTokenSource();
        posters.GenerateAsync(first, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns<Task<OperationResult>>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GeneratePendingAsync(cts.Token));

        await posters.DidNotReceive().GenerateAsync(second, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.True(await IsPendingAsync(first));
        Assert.True(await IsPendingAsync(second));
    }
}
