using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentSortWizardStateTests
{
    private static (TorrentSortWizardState State, ITorrentSortService TorrentSortService, IPathMappingService PathMappingService, ILocalLibraryClient LocalLibraryClient)
        CreateState(TestDbContextFactory factory, IConfiguration? config = null, TaskActivityTracker? activities = null, ISortEditorLookupService? lookup = null, TimeProvider? timeProvider = null)
    {
        var torrentSortService = Substitute.For<ITorrentSortService>();
        var pathMappingService = Substitute.For<IPathMappingService>();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegBinaryResolver = Substitute.For<IFfmpegBinaryResolver>();
        config ??= new ConfigurationBuilder().Build();

        var state = new TorrentSortWizardState(torrentSortService, pathMappingService, localLibraryClient, factory, config, vrMergeService, vrMergeJobTracker, ffmpegBinaryResolver, activities,
            timeProvider, lookup ?? Substitute.For<ISortEditorLookupService>());
        return (state, torrentSortService, pathMappingService, localLibraryClient);
    }

    private static (TorrentSortWizardState State, IPathMappingService PathMappingService, IVrMergeService VrMergeService, IVrMergeJobTracker VrMergeJobTracker, IFfmpegBinaryResolver FfmpegBinaryResolver)
        CreateStateForMergeStep(TestDbContextFactory factory)
    {
        var torrentSortService = Substitute.For<ITorrentSortService>();
        var pathMappingService = Substitute.For<IPathMappingService>();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegBinaryResolver = Substitute.For<IFfmpegBinaryResolver>();

        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegBinaryResolver);
        return (state, pathMappingService, vrMergeService, vrMergeJobTracker, ffmpegBinaryResolver);
    }

    [Fact]
    public async Task InitializeAsync_LoadsTorrentAndTranslatesPath()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/ABC-123" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Javinizer:DestinationAliases:0:Name"] = "jav",
            ["Javinizer:DestinationAliases:0:Path"] = "/media/jav",
            ["Javinizer:DestinationAliases:1:Name"] = "vr",
            ["Javinizer:DestinationAliases:1:Path"] = "/media/vr"
        }).Build();

        var (state, sortService, pathMapping, localLib) = CreateState(factory, config);
        pathMapping.TranslateAsync("/downloads/ABC-123", Arg.Any<CancellationToken>())
            .Returns("/scratch/ABC-123");
        localLib.ResolveRootForCodeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns("D:\\Movies");

        bool notified = false;
        state.OnChange += () => notified = true;

        await state.InitializeAsync(downloadId);

        Assert.True(notified);
        Assert.NotNull(state.Torrent);
        Assert.Equal("ABC-123", state.Torrent.MovieCode);
        Assert.Equal("/scratch/ABC-123", state.Scan.ScanPath);
        Assert.True(state.Scan.ScanPathMapped);
        Assert.Equal("D:\\Movies", state.LocalRootHint);
        Assert.Equal(2, state.Scrape.DestinationAliases.Count);
        Assert.Equal("jav", state.Scrape.SelectedDestinationAliasName);
        Assert.Equal("/media/jav", state.Destination);
        Assert.False(state.Scrape.NoDestinationAliasesConfigured);
        Assert.Equal(WizardStep.Scan, state.Step);
    }

    [Fact]
    public async Task InitializeAsync_NoDestinationAliasesConfigured_BlocksStep()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/ABC-123" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var (state, _, pathMapping, _) = CreateState(factory);
        pathMapping.TranslateAsync("/downloads/ABC-123", Arg.Any<CancellationToken>())
            .Returns("/downloads/ABC-123");

        await state.InitializeAsync(downloadId);

        Assert.True(state.Scrape.NoDestinationAliasesConfigured);
        Assert.Null(state.Scrape.SelectedDestinationAliasName);
        Assert.Equal("", state.Destination);
    }

    [Fact]
    public async Task SelectDestinationAlias_UpdatesDestinationFromMatchingAlias()
    {
        using var factory = new TestDbContextFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Javinizer:DestinationAliases:0:Name"] = "jav",
            ["Javinizer:DestinationAliases:0:Path"] = "/media/jav",
            ["Javinizer:DestinationAliases:1:Name"] = "vr",
            ["Javinizer:DestinationAliases:1:Path"] = "/media/vr"
        }).Build();
        var (state, _, _, _) = CreateState(factory, config);
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/ABC-123" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        await state.InitializeAsync(downloadId);
        state.Scrape.SelectDestinationAlias("vr");

        Assert.Equal("vr", state.Scrape.SelectedDestinationAliasName);
        Assert.Equal("/media/vr", state.Destination);
    }

    [Fact]
    public async Task InitializeAsync_PrefersContentPathOverSavePath_WhenBothPresent()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads", ContentPath = "/downloads/ABC-123" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var (state, sortService, pathMapping, _) = CreateState(factory);
        pathMapping.TranslateAsync("/downloads/ABC-123", Arg.Any<CancellationToken>())
            .Returns("/scratch/ABC-123");

        await state.InitializeAsync(downloadId);

        Assert.Equal("/scratch/ABC-123", state.Scan.ScanPath);
        await pathMapping.DidNotReceive().TranslateAsync("/downloads", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToSavePath_WhenContentPathIsBlank()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/ABC-123", ContentPath = null };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var (state, sortService, pathMapping, _) = CreateState(factory);
        pathMapping.TranslateAsync("/downloads/ABC-123", Arg.Any<CancellationToken>())
            .Returns("/downloads/ABC-123");

        await state.InitializeAsync(downloadId);

        Assert.Equal("/downloads/ABC-123", state.Scan.ScanPath);
    }

    [Fact]
    public async Task ScanAsync_Success_SelectsMatchedFiles()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Scan.ScanPath = "/scratch/ABC-123";

        var files = new List<FileInfoDto>
        {
            new() { Name = "abc-123.mp4", Path = "/scratch/ABC-123/abc-123.mp4", Matched = true, IsDir = false },
            new() { Name = "cover.jpg", Path = "/scratch/ABC-123/cover.jpg", Matched = false, IsDir = false },
            new() { Name = "extra", Path = "/scratch/ABC-123/extra", Matched = false, IsDir = true }
        };

        sortService.ScanAsync(Arg.Any<int>(), "/scratch/ABC-123", Arg.Any<CancellationToken>())
            .Returns(new TorrentScanResult(true, files, null));

        await state.Scan.ScanAsync();

        Assert.NotNull(state.Scan.ScannedFiles);
        Assert.Equal(3, state.Scan.ScannedFiles.Count);
        Assert.Single(state.Scan.SelectedFilePaths);
        Assert.Contains("/scratch/ABC-123/abc-123.mp4", state.Scan.SelectedFilePaths);
    }

    [Fact]
    public async Task SelectionHelpers_OperateCorrectly()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Scan.ScanPath = "/scratch/test";

        var files = new List<FileInfoDto>
        {
            new() { Name = "video1.mp4", Path = "/scratch/test/video1.mp4", Matched = true, IsDir = false },
            new() { Name = "video2.mkv", Path = "/scratch/test/video2.mkv", Matched = false, IsDir = false },
            new() { Name = "readme.txt", Path = "/scratch/test/readme.txt", Matched = false, IsDir = false },
        };

        sortService.ScanAsync(Arg.Any<int>(), "/scratch/test", Arg.Any<CancellationToken>())
            .Returns(new TorrentScanResult(true, files, null));

        await state.Scan.ScanAsync();

        state.Scan.SelectAllFiles();
        Assert.Equal(3, state.Scan.SelectedFilePaths.Count);

        state.Scan.DeselectAllFiles();
        Assert.Empty(state.Scan.SelectedFilePaths);

        state.Scan.SelectVideoFiles();
        Assert.Equal(2, state.Scan.SelectedFilePaths.Count);
        Assert.Contains("/scratch/test/video1.mp4", state.Scan.SelectedFilePaths);
        Assert.Contains("/scratch/test/video2.mkv", state.Scan.SelectedFilePaths);

        state.Scan.ToggleFileSelection("/scratch/test/video1.mp4", false);
        Assert.Single(state.Scan.SelectedFilePaths);
    }

    [Fact]
    public async Task StartBatchScrapeAsync_AdvancesToScraping()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Scan.SelectedFilePaths.Add("/scratch/test/movie.mp4");
        state.Destination = "/media/jav";

        sortService.StartBatchScrapeAsync(Arg.Any<int>(), Arg.Any<List<string>>(), "/media/jav", Arg.Any<CancellationToken>())
            .Returns(new TorrentBatchScrapeStartResult(true, "job-123", null));

        sortService.PollJobAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Status = JavinizerJobStatus.Running }, null));

        await state.StartBatchScrapeAsync();

        Assert.Equal(WizardStep.Scraping, state.Step);
        Assert.Null(state.Scrape.StartError);

        await state.DisposeAsync();
    }

    [Fact]
    public async Task StartBatchScrapeAsync_TracksAndCompletesActivityWhenPollingStops()
    {
        using var factory = new TestDbContextFactory();
        var tracker = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var (state, sortService, _, _) = CreateState(factory, activities: tracker);
        state.Scan.SelectedFilePaths.Add("/scratch/test/movie.mp4");
        state.Destination = "/media/jav";
        var pollGate = new TaskCompletionSource<JavinizerBatchJobResult>();

        sortService.StartBatchScrapeAsync(Arg.Any<int>(), Arg.Any<List<string>>(), "/media/jav", Arg.Any<CancellationToken>())
            .Returns(new TorrentBatchScrapeStartResult(true, "job-123", null));
        sortService.PollJobAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => pollGate.Task.WaitAsync(call.Arg<CancellationToken>()));

        await state.StartBatchScrapeAsync();

        Assert.Equal(new TaskActivity("Sorting · movie sort", "Scraping metadata…", true), tracker.Current);

        await state.DisposeAsync();

        Assert.Equal(new TaskActivity("Sorting · movie sort", "Stopped monitoring metadata scrape.", false), tracker.Current);
    }

    [Fact]
    public async Task ExcludeAndFixMatch_UpdatesJobData()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Review.SetFixMatchInput("res-1", "ABC-123");

        sortService.RescrapeAsync(Arg.Any<int>(), "res-1", "ABC-123", Arg.Any<CancellationToken>())
            .Returns(new JavinizerRescrapeResult(true, null));
        sortService.ExcludeResultAsync(Arg.Any<int>(), "res-2", Arg.Any<CancellationToken>())
            .Returns(new JavinizerExcludeResult(true, null));

        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Id = "job-123",
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Completed, Movie = new MovieViewDto { Id = "ABC-123" } }
                }
            }, null));

        await state.Review.FixMatchAsync("res-1");
        Assert.NotNull(state.JobData);

        await state.Review.ExcludeAsync("res-2");
        Assert.Null(state.ReviewError);
    }

    [Fact]
    public async Task RefreshReviewAsync_UpdatesJobDataAndClearsError()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Id = "job-123",
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Completed, Movie = new MovieViewDto { Id = "ABC-123" } }
                }
            }, null));

        await state.Review.RefreshReviewAsync();

        Assert.False(state.Review.RefreshingJobData);
        Assert.Null(state.ReviewError);
        Assert.NotNull(state.JobData);
        Assert.Equal("job-123", state.JobData!.Id);
    }

    [Fact]
    public async Task RefreshReviewAsync_SetsReviewErrorOnFailure()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(false, null, "javinizer-go unreachable"));

        await state.Review.RefreshReviewAsync();

        Assert.False(state.Review.RefreshingJobData);
        Assert.Equal("javinizer-go unreachable", state.ReviewError);
    }

    [Fact]
    public async Task EditModal_ChipEdits_KeepStructuredFieldsAndSaveOnce()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        var kept = new ActressViewDto { Id = 5, DmmId = 99, FirstName = "Noa", LastName = "Araki", ThumbUrl = "t.jpg", Aliases = "A|B" };
        state.EditModal.OpenEditModal("res-1", new MovieViewDto
        {
            Id = "ABC-123",
            Actresses = [kept, new ActressViewDto { FirstName = "Gone" }],
            Genres = [new GenreViewDto { Name = "Nasty, hardcore" }]
        });

        state.EditModal.RemoveActressAt(1);
        Assert.False(state.EditModal.AddActress(new ActressViewDto { Id = 5, FirstName = "Noa", LastName = "Araki" }));
        Assert.True(state.EditModal.AddGenre("VR"));
        Assert.False(state.EditModal.AddGenre("vr"));
        Assert.False(state.EditModal.AddGenre("   "));
        state.EditModal.EditingReleaseDate = new DateOnly(2024, 5, 6);
        Assert.True(state.EditModal.IsDirty);

        MovieViewDto? sent = null;
        var gate = new TaskCompletionSource<JavinizerUpdateResult>();
        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Do<MovieViewDto>(m => sent = m), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-123" }, null));

        var first = state.EditModal.SaveEditedMovieAsync();
        await state.EditModal.SaveEditedMovieAsync(); // Ctrl+Enter while the first save is in flight
        gate.SetResult(new JavinizerUpdateResult(true, null));
        await first;

        await sortService.Received(1).UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>());
        var actress = Assert.Single(sent!.Actresses!);
        Assert.Same(kept, actress);
        Assert.Equal(["Nasty, hardcore", "VR"], sent.Genres!.Select(g => g.Name));
        Assert.Equal(new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc), sent.ReleaseDate);
        Assert.Equal(2024, sent.ReleaseYear);
        Assert.Null(state.EditModal.EditingMovie);
    }

    [Fact]
    public async Task EditModal_AddTrackedActor_UsesLookupAndRejectsDuplicates()
    {
        using var factory = new TestDbContextFactory();
        var lookup = Substitute.For<ISortEditorLookupService>();
        lookup.ToActressDtoAsync(3, Arg.Any<CancellationToken>()).Returns(new ActressViewDto { Id = 11, FirstName = "Yua", LastName = "Mikami" });
        var (state, _, _, _) = CreateState(factory, lookup: lookup);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });

        Assert.True(await state.EditModal.AddTrackedActorAsync(3));
        Assert.False(await state.EditModal.AddTrackedActorAsync(3));
        Assert.Equal("Yua", Assert.Single(state.EditModal.EditingMovie!.Actresses!).FirstName);
    }

    [Fact]
    public void EditModal_AddActress_JapaneseOnlyDuplicateIsRejected()
    {
        using var factory = new TestDbContextFactory();
        var (state, _, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Actresses = [new ActressViewDto { JapaneseName = "新木希空" }] });

        Assert.False(state.EditModal.AddActress(new ActressViewDto { JapaneseName = "新木 希空" }));
        Assert.False(state.EditModal.IsDirty);
    }

    [Fact]
    public async Task EditModal_SaveFailure_KeepsDrawerOpenWithError()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        sortService.UpdateResultAsync(default, default!, default!, default).ReturnsForAnyArgs(new JavinizerUpdateResult(false, "javinizer-go returned 409: busy"));

        await state.EditModal.SaveEditedMovieAsync();

        Assert.NotNull(state.EditModal.EditingMovie);
        Assert.Equal("javinizer-go returned 409: busy", state.EditModal.EditModalError);
        Assert.False(state.EditModal.SavingMovie);
    }

    [Fact]
    public async Task EditModal_SaveFailure_KeepsTheTypedGenresNotTheNormalizedOnes()
    {
        using var factory = new TestDbContextFactory();
        await new Javbuddy.Services.Tags.TagRuleService(factory).AddIgnoredTagAsync("Sample", TagMatchMode.CaseInsensitive);
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Genres = [new GenreViewDto { Name = "Sample" }, new GenreViewDto { Name = "VR" }] });
        MovieViewDto? sent = null;
        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Do<MovieViewDto>(m => sent = m), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(false, "javinizer-go returned 409: busy"));

        await state.EditModal.SaveEditedMovieAsync();

        Assert.Equal(["VR"], sent!.Genres!.Select(g => g.Name));
        Assert.Equal(["Sample", "VR"], state.EditModal.EditingMovie!.Genres!.Select(g => g.Name));
    }

    [Fact]
    public async Task EditModal_Save_IsANoOpWhileAnOverrideIsInFlight()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        var gate = new TaskCompletionSource<JavinizerFieldOverrideResult>();
        sortService.OverrideFieldAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(_ => gate.Task);

        var overriding = state.EditModal.ApplyFieldOverrideAsync("maker", "dmm");
        Assert.True(state.EditModal.SourcesBusy);
        await state.EditModal.SaveEditedMovieAsync();
        gate.SetResult(new JavinizerFieldOverrideResult(false, null, "nope"));
        await overriding;

        await sortService.DidNotReceiveWithAnyArgs().UpdateResultAsync(default, default!, default!, default);
        Assert.False(state.EditModal.SourcesBusy);
    }

    [Fact]
    public async Task EditModal_LateOverride_DoesNotLandInTheNextOpenedEditor()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));
        var gate = new TaskCompletionSource<JavinizerFieldOverrideResult>();
        sortService.OverrideFieldAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(_ => gate.Task);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "A", Maker = "Old" });

        var overriding = state.EditModal.ApplyFieldOverrideAsync("maker", "dmm");
        state.EditModal.CloseEditModal();
        state.EditModal.OpenEditModal("res-2", new MovieViewDto { Id = "B", Maker = "Other" });
        gate.SetResult(new JavinizerFieldOverrideResult(true, new FieldOverrideResponseDto { Movie = new MovieViewDto { Maker = "S1" }, FieldSources = new() { ["maker"] = "dmm" } }, null));

        Assert.False(await overriding);
        Assert.Equal("res-2", state.EditModal.EditingResultId);
        Assert.Equal("Other", state.EditModal.EditingMovie!.Maker);
        Assert.Empty(state.EditModal.FieldSources);
        // The override itself is saved in javinizer-go, so the Review card still refreshes.
        await sortService.Received().PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditModal_LateRescrape_DoesNotReopenAClosedEditor()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));
        var gate = new TaskCompletionSource<JavinizerRescrapeResult>();
        sortService.RescrapeWithScrapersAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(_ => gate.Task);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "A" });

        var rescraping = state.EditModal.RescrapeWithAsync(["dmm"]);
        state.EditModal.CloseEditModal();
        gate.SetResult(new JavinizerRescrapeResult(true, null));

        Assert.False(await rescraping);
        Assert.Null(state.EditModal.EditingResultId);
        Assert.Null(state.EditModal.EditingMovie);
    }

    [Fact]
    public async Task EditModal_LateSources_DoNotLandInTheNextOpenedEditor()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        var gate = new TaskCompletionSource<JavinizerSourcesResult>();
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(_ => gate.Task);
        sortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true, [], null));
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "A" });

        var loading = state.EditModal.LoadSourcesAsync(null);
        state.EditModal.OpenEditModal("res-2", new MovieViewDto { Id = "B" });
        gate.SetResult(new JavinizerSourcesResult(false, null, "javinizer-go timed out"));
        await loading;

        Assert.Empty(state.EditModal.Sources);
        Assert.Null(state.EditModal.SourcesError);
    }

    [Fact]
    public void EditModal_Open_ClearsAnOlderReviewErrorSoTheDrawerOnlyShowsItsOwn()
    {
        using var factory = new TestDbContextFactory();
        var (state, _, _, _) = CreateState(factory);
        state.Session.ReviewError = "Exclude failed.";

        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "A" });

        Assert.Null(state.EditModal.MediaError);
    }

    [Fact]
    public async Task EditModal_SchedulePreview_DebouncesAndOnlyLatestResultWins()
    {
        using var factory = new TestDbContextFactory();
        var time = new FakeTimeProvider();
        var (state, sortService, _, _) = CreateState(factory, timeProvider: time);
        state.Destination = "/out";
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Title = "One" });

        var slow = new TaskCompletionSource<JavinizerOrganizePreviewResult>();
        sortService.PreviewWithEditsAsync(Arg.Any<int>(), "res-1", "/out", Arg.Is<MovieViewDto>(m => m.Title == "One"), Arg.Any<CancellationToken>())
            .Returns(slow.Task);
        sortService.PreviewWithEditsAsync(Arg.Any<int>(), "res-1", "/out", Arg.Is<MovieViewDto>(m => m.Title == "Two"), Arg.Any<CancellationToken>())
            .Returns(new JavinizerOrganizePreviewResult(true, new OrganizePreviewResponseDto { FolderName = "Two" }, null));

        var first = state.EditModal.SchedulePreviewAsync();
        time.Advance(TimeSpan.FromMilliseconds(500)); // first request is now in flight (slow)
        state.EditModal.EditingMovie!.Title = "Two";
        var second = state.EditModal.SchedulePreviewAsync();
        time.Advance(TimeSpan.FromMilliseconds(500));
        await second;
        slow.SetResult(new JavinizerOrganizePreviewResult(true, new OrganizePreviewResponseDto { FolderName = "One" }, null));
        await first;

        Assert.Equal("Two", state.EditModal.LivePreview!.FolderName);
        Assert.False(state.EditModal.LivePreviewLoading);
    }

    [Fact]
    public async Task EditModal_SchedulePreview_BurstSendsOneRequest()
    {
        using var factory = new TestDbContextFactory();
        var time = new FakeTimeProvider();
        var (state, sortService, _, _) = CreateState(factory, timeProvider: time);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        sortService.PreviewWithEditsAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new JavinizerOrganizePreviewResult(true, new OrganizePreviewResponseDto(), null));

        var calls = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            calls.Add(state.EditModal.SchedulePreviewAsync());
            time.Advance(TimeSpan.FromMilliseconds(100));
        }
        time.Advance(TimeSpan.FromMilliseconds(500));
        await Task.WhenAll(calls);

        await sortService.ReceivedWithAnyArgs(1).PreviewWithEditsAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task EditModal_SchedulePreview_FailureSetsErrorButSaveStillWorks()
    {
        using var factory = new TestDbContextFactory();
        var time = new FakeTimeProvider();
        var (state, sortService, _, _) = CreateState(factory, timeProvider: time);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        sortService.PreviewWithEditsAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new JavinizerOrganizePreviewResult(false, null, "javinizer-go returned 502: down"));
        sortService.UpdateResultAsync(default, default!, default!, default).ReturnsForAnyArgs(new JavinizerUpdateResult(true, null));
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));

        var preview = state.EditModal.SchedulePreviewAsync();
        time.Advance(TimeSpan.FromMilliseconds(500));
        await preview;
        Assert.Equal("javinizer-go returned 502: down", state.EditModal.LivePreviewError);

        await state.EditModal.SaveEditedMovieAsync();
        Assert.Null(state.EditModal.EditingMovie);
    }

    [Fact]
    public async Task EditModal_Close_DiscardsPendingPreview()
    {
        using var factory = new TestDbContextFactory();
        var time = new FakeTimeProvider();
        var (state, sortService, _, _) = CreateState(factory, timeProvider: time);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });

        var pending = state.EditModal.SchedulePreviewAsync();
        state.EditModal.CloseEditModal();
        time.Advance(TimeSpan.FromMilliseconds(500));
        await pending;

        await sortService.DidNotReceiveWithAnyArgs().PreviewWithEditsAsync(default, default!, default!, default!, default);
        Assert.Null(state.EditModal.LivePreview);
    }

    [Fact]
    public async Task EditModal_ApplyFieldOverride_ReplacesOnlyThatFieldKeepingOtherUnsavedEdits()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Maker = "Old", Title = "Scraped" });
        state.EditModal.EditingMovie!.Title = "My unsaved title";
        sortService.OverrideFieldAsync(Arg.Any<int>(), "res-1", "maker", "dmm", Arg.Any<CancellationToken>())
            .Returns(new JavinizerFieldOverrideResult(true, new FieldOverrideResponseDto
            {
                Movie = new MovieViewDto { Maker = "S1", Title = "Server title" },
                FieldSources = new() { ["maker"] = "dmm" }
            }, null));
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));

        Assert.True(await state.EditModal.ApplyFieldOverrideAsync("maker", "dmm"));

        Assert.Equal("S1", state.EditModal.EditingMovie.Maker);
        Assert.Equal("My unsaved title", state.EditModal.EditingMovie.Title);
        Assert.Equal("dmm", state.EditModal.FieldSources["maker"]);
        Assert.False(state.EditModal.SourcesBusy);
    }

    [Fact]
    public async Task EditModal_ApplyFieldOverride_FailureSetsErrorAndKeepsBuffer()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Maker = "Old" });
        sortService.OverrideFieldAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new JavinizerFieldOverrideResult(false, null, "javinizer-go returned 409: busy"));

        Assert.False(await state.EditModal.ApplyFieldOverrideAsync("maker", "dmm"));

        Assert.Equal("Old", state.EditModal.EditingMovie!.Maker);
        Assert.Equal("javinizer-go returned 409: busy", state.EditModal.SourcesError);
    }

    [Fact]
    public async Task EditModal_RescrapeWith_ReloadsBufferFromRefreshedJob()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Session.JobData = new BatchJobResponseDto { Results = new() { ["res-1"] = new() { ResultId = "res-1", Movie = new MovieViewDto { Id = "ABC-123", Maker = "Old" } } } };
        state.EditModal.OpenEditModal("res-1", state.Session.JobData.Results["res-1"].Movie);
        state.EditModal.MarkDirty();
        sortService.RescrapeWithScrapersAsync(Arg.Any<int>(), "res-1", "ABC-123", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerRescrapeResult(true, null));
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Results = new() { ["res-1"] = new() { ResultId = "res-1", Movie = new MovieViewDto { Id = "ABC-123", Maker = "Fresh" } } } }, null));
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [], null));
        sortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true, [], null));

        Assert.True(await state.EditModal.RescrapeWithAsync(["javdb"]));

        Assert.Equal("Fresh", state.EditModal.EditingMovie!.Maker);
        Assert.False(state.EditModal.IsDirty);
        await sortService.Received().RescrapeWithScrapersAsync(Arg.Any<int>(), "res-1", "ABC-123",
            Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "javdb" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditModal_LoadSources_KeepsEnabledScrapers_FailureOnlySetsError()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(false, null, "javinizer-go returned 404: not found"));
        sortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true,
            [new ScraperInfoDto { Name = "dmm", Enabled = true }, new ScraperInfoDto { Name = "javdb", Enabled = false }], null));
        var result = new BatchFileResultDto { ResultId = "res-1", FieldSources = System.Text.Json.JsonDocument.Parse("""{"maker":"dmm"}""").RootElement };

        await state.EditModal.LoadSourcesAsync(result);

        Assert.Equal("javinizer-go returned 404: not found", state.EditModal.SourcesError);
        Assert.Empty(state.EditModal.Sources);
        Assert.Equal(["dmm"], state.EditModal.Scrapers.Select(s => s.Name));
        Assert.Equal("dmm", state.EditModal.FieldSources["maker"]);
        Assert.NotNull(state.EditModal.EditingMovie);
    }

    [Fact]
    public async Task EditModal_Save_KeepsMediaChangesMadeInTheDrawer_AndSendsCastCasTokens()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        var original = new MovieViewDto { Id = "ABC-123", Title = "Scraped", PosterUrl = "default.jpg", CastVersion = "cv1", Actresses = [new() { FirstName = "Yui", NameKey = "yui" }] };
        state.Session.JobData = new BatchJobResponseDto
        {
            Results = new() { ["/in/a.mp4"] = new() { ResultId = "res-1", Revision = 3, Movie = original } }
        };
        state.EditModal.OpenEditModal("res-1", original);
        state.EditModal.EditingMovie!.Title = "Edited";
        state.EditModal.AddActress(new ActressViewDto { FirstName = "Noa" });

        // A media action in the drawer (crop, poster pick, screenshot delete) refreshed JobData.
        state.Session.JobData = new BatchJobResponseDto
        {
            Results = new()
            {
                ["/in/a.mp4"] = new()
                {
                    ResultId = "res-1",
                    Revision = 5,
                    Movie = new MovieViewDto
                    {
                        Id = "ABC-123",
                        Title = "Scraped",
                        PosterUrl = "shot2.jpg",
                        CroppedPosterUrl = "crop.jpg",
                        ShouldCropPoster = false,
                        PosterCropBounds = new CropBoundsDto { X = 1, Y = 2, Width = 3, Height = 4 },
                        CoverUrl = "cover2.jpg",
                        ScreenshotUrls = ["shot2.jpg"],
                        CastVersion = "cv1"
                    }
                }
            }
        };

        MovieViewDto? sent = null;
        ulong? revision = null;
        sortService.UpdateResultAsync(default, default!, default!, default, default).ReturnsForAnyArgs(ci =>
        {
            sent = ci.ArgAt<MovieViewDto>(2);
            revision = ci.ArgAt<ulong?>(4);
            return new JavinizerUpdateResult(true, null);
        });
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));

        await state.EditModal.SaveEditedMovieAsync();

        Assert.Equal("Edited", sent!.Title);
        Assert.Equal(("shot2.jpg", "crop.jpg", false, "cover2.jpg"), (sent.PosterUrl, sent.CroppedPosterUrl, sent.ShouldCropPoster, sent.CoverUrl));
        Assert.Equal(3, sent.PosterCropBounds!.Width);
        Assert.Equal(["shot2.jpg"], sent.ScreenshotUrls);
        Assert.Equal("cv1", sent.CastVersion);
        Assert.Equal("yui", sent.Actresses![0].NameKey);
        Assert.Equal((ulong)5, revision);
    }

    [Fact]
    public async Task EditModal_FieldOverride_RefreshesJobData()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123", Maker = "Old" });
        sortService.OverrideFieldAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(new JavinizerFieldOverrideResult(true,
            new FieldOverrideResponseDto { Movie = new MovieViewDto { Maker = "S1" } }, null));
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "fresh" }, null));

        await state.EditModal.ApplyFieldOverrideAsync("maker", "dmm");

        Assert.Equal("fresh", state.Session.JobData!.Id);
    }

    [Fact]
    public void EditModal_RequestClose_WhenDirtyAsksFirst()
    {
        using var factory = new TestDbContextFactory();
        var (state, _, _, _) = CreateState(factory);
        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });

        state.EditModal.RequestClose();
        Assert.Null(state.EditModal.EditingMovie); // clean: closes at once

        state.EditModal.OpenEditModal("res-1", new MovieViewDto { Id = "ABC-123" });
        state.EditModal.MarkDirty();
        state.EditModal.RequestClose();
        Assert.NotNull(state.EditModal.EditingMovie);
        Assert.True(state.EditModal.ConfirmingDiscard);

        state.EditModal.RequestClose(); // Esc again = discard
        Assert.Null(state.EditModal.EditingMovie);
    }

    [Fact]
    public async Task GoToPreviewAsync_FailsIfUnmatchedFileExists()
    {
        using var factory = new TestDbContextFactory();
        var (state, _, _, _) = CreateState(factory);

        state.Session.JobData = new BatchJobResponseDto
        {
            Results = new Dictionary<string, BatchFileResultDto>
            {
                ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Failed, Movie = null }
            }
        };

        await state.Preview.GoToPreviewAsync();

        Assert.NotNull(state.ReviewError);
        Assert.Contains("could not be matched", state.ReviewError);
        Assert.Equal(WizardStep.Scan, state.Step);
    }

    [Fact]
    public async Task OrganizeAsync_Success_TransitionsToDone()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Destination = "/media/jav";

        sortService.OrganizeAsync(Arg.Any<int>(), "/media/jav", Arg.Any<CancellationToken>())
            .Returns(new TorrentOrganizeResult(true, null));

        await state.Organize.OrganizeAsync();

        Assert.Equal(WizardStep.Done, state.Step);
        Assert.Null(state.Organize.OrganizeError);
    }

    [Fact]
    public async Task OrganizeAsync_TracksElapsedTimeWhileInFlightThenClearsIt()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Destination = "/media/jav";

        var tcs = new TaskCompletionSource<TorrentOrganizeResult>();
        sortService.OrganizeAsync(Arg.Any<int>(), "/media/jav", Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var organizeTask = state.Organize.OrganizeAsync();

        Assert.True(state.Organize.Organizing);
        Assert.NotNull(state.Organize.OrganizeStartedAt);
        Assert.NotNull(state.Organize.OrganizeElapsed);

        tcs.SetResult(new TorrentOrganizeResult(true, null));
        await organizeTask;

        Assert.False(state.Organize.Organizing);
        Assert.Null(state.Organize.OrganizeStartedAt);
        Assert.Null(state.Organize.OrganizeElapsed);
    }

    [Fact]
    public async Task OrganizeAsync_TracksAndCompletesActivity()
    {
        using var factory = new TestDbContextFactory();
        var tracker = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        var (state, sortService, _, _) = CreateState(factory, activities: tracker);
        state.Destination = "/media/jav";
        var gate = new TaskCompletionSource<TorrentOrganizeResult>();
        sortService.OrganizeAsync(Arg.Any<int>(), "/media/jav", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var organizeTask = state.Organize.OrganizeAsync();

        Assert.Equal(new TaskActivity("Sorting · movie sort", "Organizing files…", true), tracker.Current);
        gate.SetResult(new TorrentOrganizeResult(true, null));
        await organizeTask;

        Assert.Equal(new TaskActivity("Sorting · movie sort", "Files organized.", false), tracker.Current);
    }

    [Fact]
    public async Task StepClass_WhileOrganizing_ShowsPreviewDoneAndOrganizeActive()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);
        state.Destination = "/media/jav";
        state.Step = WizardStep.Preview;

        var tcs = new TaskCompletionSource<TorrentOrganizeResult>();
        sortService.OrganizeAsync(Arg.Any<int>(), "/media/jav", Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var organizeTask = state.Organize.OrganizeAsync();

        Assert.Equal("sort-step-done", state.StepClass(WizardStep.Preview));
        Assert.Equal("sort-step-active", state.StepClass(WizardStep.Done));

        tcs.SetResult(new TorrentOrganizeResult(true, null));
        await organizeTask;

        Assert.Equal("sort-step-active", state.StepClass(WizardStep.Done));
    }

    [Fact]
    public async Task StartCleanupPreviewAsync_Success_PopulatesPreview()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PreviewCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/scratch/ABC-123", ["movie.mp4", "ad.txt"], null));

        await state.Cleanup.StartCleanupPreviewAsync();

        Assert.False(state.Cleanup.PreviewingCleanup);
        Assert.Null(state.Cleanup.CleanupPreviewError);
        Assert.Equal("/scratch/ABC-123", state.Cleanup.CleanupPreviewResolvedPath);
        Assert.Equal(["movie.mp4", "ad.txt"], state.Cleanup.CleanupPreviewEntries);
        Assert.True(state.Cleanup.CleanupPreviewActive);
    }

    [Fact]
    public async Task StartCleanupPreviewAsync_Failure_SetsError()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PreviewCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(false, null, null, "No source path is known for this torrent."));

        await state.Cleanup.StartCleanupPreviewAsync();

        Assert.Equal("No source path is known for this torrent.", state.Cleanup.CleanupPreviewError);
        Assert.Null(state.Cleanup.CleanupPreviewEntries);
    }

    [Fact]
    public void CancelCleanupPreview_ClearsPreviewState()
    {
        using var factory = new TestDbContextFactory();
        var (state, _, _, _) = CreateState(factory);

        typeof(CleanupStep).GetProperty(nameof(state.Cleanup.CleanupPreviewActive))!.SetValue(state.Cleanup, true);
        typeof(CleanupStep).GetProperty(nameof(state.Cleanup.CleanupPreviewEntries))!.SetValue(state.Cleanup, new List<string> { "movie.mp4" });

        state.Cleanup.CancelCleanupPreview();

        Assert.False(state.Cleanup.CleanupPreviewActive);
        Assert.Null(state.Cleanup.CleanupPreviewEntries);
        Assert.Null(state.Cleanup.CleanupPreviewError);
    }

    [Fact]
    public async Task ConfirmCleanupAsync_Success_SetsCleanupSucceeded_AndClearsPreview()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PreviewCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/scratch/ABC-123", ["movie.mp4"], null));
        await state.Cleanup.StartCleanupPreviewAsync();

        sortService.CleanupSourceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupResult(true, null));

        await state.Cleanup.ConfirmCleanupAsync();

        Assert.True(state.Cleanup.CleanupSucceeded);
        Assert.False(state.Cleanup.CleaningUp);
        Assert.False(state.Cleanup.CleanupPreviewActive);
        Assert.Null(state.Cleanup.CleanupPreviewEntries);
    }

    [Fact]
    public async Task ConfirmCleanupAsync_Failure_SetsError_KeepsPreview()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        sortService.PreviewCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/scratch/ABC-123", ["movie.mp4"], null));
        await state.Cleanup.StartCleanupPreviewAsync();

        sortService.CleanupSourceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupResult(false, "qBittorrent failed to delete the torrent."));

        await state.Cleanup.ConfirmCleanupAsync();

        Assert.False(state.Cleanup.CleanupSucceeded);
        Assert.Equal("qBittorrent failed to delete the torrent.", state.Cleanup.CleanupError);
        Assert.True(state.Cleanup.CleanupPreviewActive);
    }

    private static void SeedJobDataWithMovie(TorrentSortWizardState state, MovieViewDto movie)
    {
        state.Session.JobData = new BatchJobResponseDto
        {
            Id = "job-123",
            Results = new Dictionary<string, BatchFileResultDto>
            {
                ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Completed, Movie = movie }
            }
        };
    }

    [Fact]
    public async Task SetPosterFromScreenshotAsync_Success_UpdatesPosterAndRefreshes()
    {
        // follow-up: SetPosterFromScreenshotAsync now calls javinizer-go's dedicated
        // poster-from-url endpoint (not the generic whole-movie PATCH) — confirmed against a real
        // javinizer-go instance that the generic PATCH leaves CroppedPosterUrl stale, which is what
        // javinizer-go's own review UI actually displays.
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        SeedJobDataWithMovie(state, original);

        sortService.SetPosterFromUrlAsync(Arg.Any<int>(), "res-1", "shot2.jpg", Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterFromUrlResult(true, null));
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        await state.Review.SetPosterFromScreenshotAsync("res-1", "shot2.jpg");

        await sortService.Received(1).SetPosterFromUrlAsync(state.TorrentDownloadId, "res-1", "shot2.jpg", Arg.Any<CancellationToken>());
        Assert.Empty(state.Review.UpdatingScreenshots);
        Assert.Null(state.ReviewError);
        Assert.Equal("job-456", state.JobData!.Id); // RefreshJobDataAsync re-polled and replaced JobData
        await sortService.Received(1).PollJobAsync(state.TorrentDownloadId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCoverFromScreenshotAsync_Success_UpdatesCoverAndRefreshes()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        SeedJobDataWithMovie(state, original);

        MovieViewDto? sentClone = null;
        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sentClone = callInfo.Arg<MovieViewDto>();
                return new JavinizerUpdateResult(true, null);
            });
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        await state.Review.SetCoverFromScreenshotAsync("res-1", "shot1.jpg");

        Assert.Equal("shot1.jpg", sentClone!.CoverUrl);
        Assert.Null(original.CoverUrl);
        Assert.Empty(state.Review.UpdatingScreenshots);
        Assert.Null(state.ReviewError);
    }

    [Fact]
    public async Task SetPosterFromScreenshotAsync_UsesDedicatedEndpoint_NotGenericMoviePatch()
    {
        // follow-up: confirmed directly against a real javinizer-go instance that the
        // generic whole-movie PATCH (UpdateResultAsync) leaves CroppedPosterUrl stale even after
        // clearing ShouldCropPoster — javinizer-go's own review UI displays CroppedPosterUrl in
        // preference to PosterUrl, so the pick looked like it "didn't work" there. javinizer-go's own
        // poster-from-url endpoint downloads the image server-side and regenerates CroppedPosterUrl
        // (and clears ShouldCropPoster/crop geometry itself), so Javbuddy no longer needs to — or
        // even can — clear ShouldCropPoster client-side for a poster pick.
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"], ShouldCropPoster = true };
        SeedJobDataWithMovie(state, original);

        sortService.SetPosterFromUrlAsync(Arg.Any<int>(), "res-1", "shot2.jpg", Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterFromUrlResult(true, null));
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        await state.Review.SetPosterFromScreenshotAsync("res-1", "shot2.jpg");

        await sortService.Received(1).SetPosterFromUrlAsync(state.TorrentDownloadId, "res-1", "shot2.jpg", Arg.Any<CancellationToken>());
        await sortService.DidNotReceive().UpdateResultAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCoverFromScreenshotAsync_LeavesShouldCropPosterUntouched()
    {
        // Unlike SetPosterFromScreenshotAsync, cover/fanart has no crop concept in javinizer-go, so
        // this action must not touch ShouldCropPoster at all.
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"], ShouldCropPoster = true };
        SeedJobDataWithMovie(state, original);

        MovieViewDto? sentClone = null;
        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sentClone = callInfo.Arg<MovieViewDto>();
                return new JavinizerUpdateResult(true, null);
            });
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        await state.Review.SetCoverFromScreenshotAsync("res-1", "shot1.jpg");

        Assert.NotNull(sentClone);
        Assert.True(sentClone!.ShouldCropPoster);
    }

    [Fact]
    public async Task RemoveScreenshotAsync_Success_RemovesUrlFromCloneAndRefreshes()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        SeedJobDataWithMovie(state, original);

        MovieViewDto? sentClone = null;
        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sentClone = callInfo.Arg<MovieViewDto>();
                return new JavinizerUpdateResult(true, null);
            });
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        await state.Review.RemoveScreenshotAsync("res-1", "shot1.jpg");

        Assert.NotNull(sentClone);
        Assert.DoesNotContain("shot1.jpg", sentClone!.ScreenshotUrls!);
        Assert.Contains("shot2.jpg", sentClone.ScreenshotUrls!);
        Assert.Equal(2, original.ScreenshotUrls!.Count); // original list untouched
        Assert.Empty(state.Review.UpdatingScreenshots);
    }

    [Fact]
    public async Task SetPosterFromScreenshotAsync_Failure_SetsReviewErrorAndClearsBusyKey_WithoutRefreshing()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg"] };
        SeedJobDataWithMovie(state, original);

        sortService.SetPosterFromUrlAsync(Arg.Any<int>(), "res-1", "shot1.jpg", Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterFromUrlResult(false, "javinizer-go rejected the update"));

        await state.Review.SetPosterFromScreenshotAsync("res-1", "shot1.jpg");

        Assert.Equal("javinizer-go rejected the update", state.ReviewError);
        Assert.Empty(state.Review.UpdatingScreenshots);
        Assert.Equal("job-123", state.JobData!.Id); // unchanged — RefreshJobDataAsync was never called
        await sortService.DidNotReceive().PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveScreenshotAsync_Failure_SetsReviewErrorAndClearsBusyKey()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg"] };
        SeedJobDataWithMovie(state, original);

        sortService.UpdateResultAsync(Arg.Any<int>(), "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(false, null));

        await state.Review.RemoveScreenshotAsync("res-1", "shot1.jpg");

        Assert.Equal("Failed to update screenshot.", state.ReviewError);
        Assert.Empty(state.Review.UpdatingScreenshots);
    }

    [Fact]
    public async Task SetPosterFromScreenshotAsync_TracksBusyKeyWhileInFlight()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg"] };
        SeedJobDataWithMovie(state, original);

        var tcs = new TaskCompletionSource<JavinizerPosterFromUrlResult>();
        sortService.SetPosterFromUrlAsync(Arg.Any<int>(), "res-1", "shot1.jpg", Arg.Any<CancellationToken>())
            .Returns(tcs.Task);
        sortService.PollJobAsync(Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new JavinizerBatchJobResult(true, new BatchJobResponseDto { Id = "job-456" }, null));

        var actionTask = state.Review.SetPosterFromScreenshotAsync("res-1", "shot1.jpg");

        Assert.Contains("res-1:shot1.jpg", state.Review.UpdatingScreenshots);

        tcs.SetResult(new JavinizerPosterFromUrlResult(true, null));
        await actionTask;

        Assert.Empty(state.Review.UpdatingScreenshots);
    }

    [Fact]
    public async Task ScreenshotAction_UnknownResultId_NoOps()
    {
        using var factory = new TestDbContextFactory();
        var (state, sortService, _, _) = CreateState(factory);

        var original = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg"] };
        SeedJobDataWithMovie(state, original);

        await state.Review.SetPosterFromScreenshotAsync("missing-result", "shot1.jpg");

        await sortService.DidNotReceive().SetPosterFromUrlAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(state.Review.UpdatingScreenshots);
        Assert.Null(state.ReviewError);
    }

    [Theory]
    [InlineData("video.mp4", true)]
    [InlineData("movie.mkv", true)]
    [InlineData("image.jpg", false)]
    [InlineData("sub.srt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVideoFile_ClassifiesExtensions(string? filename, bool expected)
    {
        Assert.Equal(expected, ScanStep.IsVideoFile(filename));
    }

    [Theory]
    [InlineData("/path/to/movie.mp4", "movie.mp4")]
    [InlineData("C:\\path\\to\\movie.mp4", "movie.mp4")]
    [InlineData("single.mkv", "single.mkv")]
    [InlineData("", "Unknown file")]
    [InlineData(null, "Unknown file")]
    public void GetFileName_ExtractsName(string? path, string expected)
    {
        Assert.Equal(expected, TorrentSortPreviewHelper.GetFileName(path));
    }

    [Fact]
    public async Task InitializeAsync_ReattachesToRunningMergeJob_SetsFfmpegAvailable()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "DEVR-041" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "DEVR-041", SavePath = "/downloads/devr-041" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var ffmpegBinaryResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegBinaryResolver.IsAvailable.Returns(true);

        // A real tracker (not a substitute) with a merge job that never completes on its own,
        // simulating "still running" — reproduces navigating away from an active merge and back.
        // A substitute tracker wouldn't actually record the job GetOrStart is given here, so
        // InitializeAsync's own vrMergeJobTracker.Get(...) call would just see nothing running.
        var vrMergeJobTracker = new VrMergeJobTracker(new JavbuddyMetrics());
        var neverCompletes = new TaskCompletionSource<VrMergeResult>();
        vrMergeJobTracker.GetOrStart(downloadId, (progress, ct) => neverCompletes.Task);

        var state = new TorrentSortWizardState(
            Substitute.For<ITorrentSortService>(), Substitute.For<IPathMappingService>(), Substitute.For<ILocalLibraryClient>(),
            factory, new ConfigurationBuilder().Build(), Substitute.For<IVrMergeService>(), vrMergeJobTracker, ffmpegBinaryResolver);

        await state.InitializeAsync(downloadId);

        Assert.Equal(WizardStep.Merge, state.Step);
        Assert.True(state.Merge.Merging);
        // Regression: FfmpegAvailable used to only be set *after* the reattach branch's early
        // return, so it silently stayed at its false default and the UI showed "ffmpeg not
        // available" instead of the in-progress merge.
        Assert.True(state.Merge.FfmpegAvailable);

        await state.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_FfmpegAvailableButFewerThanTwoPartsDetected_SkipsMergeStep()
    {
        using var factory = new TestDbContextFactory();
        int downloadId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/abc-123" };
            db.TorrentDownloads.Add(torrent);
            await db.SaveChangesAsync();
            downloadId = torrent.Id;
        }

        var (state, pathMappingService, vrMergeService, _, ffmpegBinaryResolver) = CreateStateForMergeStep(factory);
        ffmpegBinaryResolver.IsAvailable.Returns(true);

        var realFolder = Directory.CreateTempSubdirectory("javbuddy-mergestep-skip-test-").FullName;
        try
        {
            pathMappingService.TranslateToAppPathAsync("/downloads/abc-123", Arg.Any<CancellationToken>())
                .Returns(realFolder);
            vrMergeService.DetectAsync(realFolder, Arg.Any<CancellationToken>()).Returns((VrMergeCandidate?)null);

            await state.InitializeAsync(downloadId);

            Assert.Equal(WizardStep.Scan, state.Step);
            Assert.False(state.Merge.Merging);
        }
        finally
        {
            Directory.Delete(realFolder, recursive: true);
        }
    }
}
