using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MovieCleanupTests : BunitContext
{
    // The page queues movie IDs and loads each card's movie on demand; every movie
    // handed out through Queued resolves by ID from here.
    private readonly Dictionary<int, Movie> queuedMovies = [];

    /// <summary>Registers the movies for the page's on-demand card load and returns their IDs, as
    /// BuildQueueAsync / a session's Queue would hold them.</summary>
    private List<int> Queued(IEnumerable<Movie> movies)
    {
        var ids = new List<int>();
        foreach (var movie in movies)
        {
            queuedMovies[movie.Id] = movie;
            ids.Add(movie.Id);
        }
        return ids;
    }

    private TestDbContextFactory SetUpServices(
        IMovieCleanupService? cleanupService = null,
        IJellyfinClient? jellyfinClient = null,
        Javbuddy.Services.VideoRepair.IVideoRepairService? repairService = null,
        MovieChangeNotifier? movieChangeNotifier = null,
        IMovieService? movieService = null,
        IActorService? actorService = null,
        ITagService? tagService = null,
        IMovieStreamService? streamService = null,
        ITrickplayService? trickplayService = null)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IMovieSceneService>(new MovieSceneService(factory));
        Services.AddSingleton<IMovieDetailQueryService>(new MovieDetailQueryService(factory));
        Services.AddSingleton<IMovieHighlightService>(new MovieHighlightService(factory));
        Services.AddSingleton<IMovieApexService>(new MovieApexService(factory));
        Services.AddSingleton<IActorTagService>(new ActorTagService(factory));
        Services.AddSingleton(Substitute.For<ISceneChapterImportService>());
        Services.AddSingleton(Substitute.For<ISceneChapterWriteService>());
        Services.AddSingleton(Substitute.For<ISceneDetectionService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.ISceneMediaService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.IHighlightMediaService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.IApexMediaService>());
        Services.AddSingleton(new Javbuddy.Services.SceneMedia.SceneMediaQueue(Substitute.For<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Javbuddy.Services.SceneMedia.SceneMediaQueue>.Instance));
        cleanupService ??= Substitute.For<IMovieCleanupService>();
        cleanupService.GetQueueMovieAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(queuedMovies.GetValueOrDefault(call.ArgAt<int>(0))));
        Services.AddSingleton(cleanupService);
        Services.AddSingleton(jellyfinClient ?? Substitute.For<IJellyfinClient>());
        Services.AddSingleton(streamService ?? StreamServiceStubs.AllPlayable());
        Services.AddSingleton(trickplayService ?? Substitute.For<ITrickplayService>());
        Services.AddSingleton(repairService ?? Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairService>());
        Services.AddSingleton(movieChangeNotifier ?? new MovieChangeNotifier());
        Services.AddSingleton(movieService ?? Substitute.For<IMovieService>());
        Services.AddSingleton(actorService ?? Substitute.For<IActorService>());
        Services.AddSingleton(tagService ?? Substitute.For<ITagService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return factory;
    }

    [Fact]
    public void NoEligibleMovies_ShowsEmptyState()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([]);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Contains("No movies eligible for cleanup right now", cut.Markup);
    }

    [Fact]
    public void EligibleMovies_ShowsTheFirstCard()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var movies = new List<Movie>
        {
            new() { Id = 1, Code = "ABC-123", MetaTitle = "First Movie", Status = MovieStatus.Got },
            new() { Id = 2, Code = "DEF-456", MetaTitle = "Second Movie", Status = MovieStatus.Got },
        };
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Queued(movies));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Contains("ABC-123", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("First Movie", cut.Find(".cleanup-title").TextContent);
        Assert.DoesNotContain("DEF-456", cut.Markup);
    }

    [Fact]
    public void EligibleMovies_MovieCodeLinksToMovieDetailPageInNewTab()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var movies = new List<Movie>
        {
            new() { Id = 1, Code = "ABC-123", MetaTitle = "First Movie", Status = MovieStatus.Got },
        };
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Queued(movies));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var link = cut.Find(".cleanup-code a.cleanup-code-link");
        Assert.Equal("/movies/ABC-123", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("ABC-123", link.TextContent);
    }

    [Fact]
    public void ChangingOrder_RebuildsTheQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "RND-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.LargestFirst, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "LRG-1", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        Assert.Contains("RND-1", cut.Find(".cleanup-code").TextContent);

        cut.Find("#cleanup-order").Change("LargestFirst");

        Assert.Contains("LRG-1", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.LargestFirst, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EligibleLibraries_RendersLibraryDropdown()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns(["JAV", "VR"]);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var select = cut.Find("#cleanup-library");
        Assert.NotNull(select);
        var options = select.Children.Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal(["All libraries", "JAV", "VR"], options);
    }

    [Fact]
    public void NoEligibleLibraries_DoesNotRenderLibraryDropdown()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns([]);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ABC-123", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll("#cleanup-library"));
    }

    [Fact]
    public void ChangingLibrary_RebuildsTheQueueWithSelectedLibrary()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns(["JAV", "VR"]);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, null, Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ALL-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.Random, "VR", Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        Assert.Contains("ALL-1", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("1 eligible", cut.Find(".cleanup-eligible-pill").TextContent);

        cut.Find("#cleanup-library").Change("VR");

        Assert.Contains("VR-1", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.Random, "VR", Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SelectingAllLibraries_RebuildsTheQueueWithoutLibraryFilter()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns(["JAV", "VR"]);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, null, Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ALL-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.Random, "VR", Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("#cleanup-library").Change("VR");
        Assert.Contains("VR-1", cut.Find(".cleanup-code").TextContent);

        cut.Find("#cleanup-library").Change("");

        Assert.Contains("ALL-1", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.Received(2).BuildQueueAsync(CleanupOrder.Random, null, Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingSnooze_CallsServiceAndAdvancesToTheNextCard()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "SNZ-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "SNZ-2", Status = MovieStatus.Got },
        ]));
        cleanupService.SnoozeAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-snooze-btn").Click();

        Assert.Contains("SNZ-2", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("Session: 1 reviewed", cut.Markup);
        _ = cleanupService.Received(1).SnoozeAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingBlacklist_CallsServiceAndAdvancesToTheNextCard()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BLK-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "BLK-2", Status = MovieStatus.Got },
        ]));
        cleanupService.BlacklistAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-blacklist-btn").Click();

        Assert.Contains("BLK-2", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.Received(1).BlacklistAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingSkip_AdvancesWithoutCallingTheService()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "SKP-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "SKP-2", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();

        Assert.Contains("SKP-2", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        _ = cleanupService.DidNotReceive().SnoozeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        _ = cleanupService.DidNotReceive().BlacklistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingDelete_ShowsConfirmModal_AndDoesNotCallServiceUntilConfirmed()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "DEL-1", Status = MovieStatus.Got },
        ]));
        cleanupService.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-delete-btn").Click();

        Assert.Contains("Delete movie?", cut.Markup);
        _ = cleanupService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        cut.Find(".cleanup-modal-confirm-btn").Click();

        _ = cleanupService.Received(1).DeleteAsync(1, Arg.Any<CancellationToken>());
        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 1), CleanupOutcome.Deleted);
        Assert.NotEmpty(cut.FindAll(".cleanup-summary"));
    }

    [Fact]
    public void ConfirmingDelete_WhenServiceFails_ShowsTheErrorAndDoesNotAdvance()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "DEL-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "DEL-2", Status = MovieStatus.Got },
        ]));
        cleanupService.DeleteAsync(1, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Could not delete the movie's folder: permission denied"));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-delete-btn").Click();
        cut.Find(".cleanup-modal-confirm-btn").Click();

        Assert.Contains("Could not delete the movie's folder: permission denied", cut.Markup);
        Assert.DoesNotContain("Delete movie?", cut.Markup);
        Assert.Contains("DEL-1", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("Session: 0 reviewed", cut.Markup);
    }

    [Fact]
    public void ConfirmingDelete_ReleasesThePlayingVideoBeforeDeletingTheFolder()
    {
        // While the card's <video> still streams the file from Jellyfin, a network share
        // keeps the deleted file around (NFS .nfsXXXX / SMB delete-pending) and the folder delete fails
        // with "Directory not empty" — so the page must drop the stream first.
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
            new Movie { Id = 2, Code = "JF-2", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);
        var module = JSInterop.SetupModule("./Components/Pages/MovieCleanup.razor.js");
        module.SetupVoid("releaseVideo", "video.cleanup-video").SetVoidResult();
        var releasedBeforeDelete = false;
        cleanupService.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            releasedBeforeDelete = module.Invocations.Any(i => i.Identifier == "releaseVideo");
            return OperationResult.Fail("Could not delete the movie's folder: Directory not empty");
        });

        var cut = Render<MovieCleanup>();
        Assert.Single(cut.FindAll("video.cleanup-video"));
        cut.Find(".cleanup-delete-btn").Click();
        cut.Find(".cleanup-modal-confirm-btn").Click();

        Assert.True(releasedBeforeDelete);
        // The player stays stopped after a failed delete (poster + Play button again), so a retry of
        // Delete doesn't race a fresh stream.
        Assert.Empty(cut.FindAll("video.cleanup-video"));
        Assert.Single(cut.FindAll(".cleanup-play-btn"));
    }

    [Fact]
    public void ClickingCancelOnDeleteModal_DismissesItWithoutCallingTheService()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "DEL-1", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-delete-btn").Click();
        cut.Find(".cleanup-modal-cancel-btn").Click();

        Assert.DoesNotContain("Delete movie?", cut.Markup);
        Assert.Contains("DEL-1", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingStop_EndsTheSessionAndShowsTheSummaryInsteadOfLeaving()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "STP-1", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var startUri = nav.Uri;

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-stop-btn").Click();

        cleanupService.Received(1).EndSession();
        Assert.Equal(startUri, nav.Uri);
        Assert.NotEmpty(cut.FindAll(".cleanup-summary"));
        Assert.Empty(cut.FindAll(".cleanup-card"));
        Assert.Empty(cut.FindAll(".cleanup-controls"));
        Assert.Contains("No movies were reviewed in this session.", cut.Markup);
    }

    [Fact]
    public void ReachingTheEndOfTheQueue_EndsTheSessionAndShowsTheSummary()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "END-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "END-2", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();
        cleanupService.DidNotReceive().EndSession();
        Assert.Empty(cut.FindAll(".cleanup-summary"));

        cut.Find(".cleanup-skip-btn").Click();

        cleanupService.Received(1).EndSession();
        Assert.NotEmpty(cut.FindAll(".cleanup-summary"));
    }

    [Fact]
    public void QueuedMovieDeletedBeforeItsTurn_IsSkippedWithoutADecision()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var queue = Queued([
            new Movie { Id = 1, Code = "GONE-1", Status = MovieStatus.Got },
            new Movie { Id = 3, Code = "GONE-3", Status = MovieStatus.Got },
        ]);
        // Movie 2 is still queued but was deleted (e.g. from another tab) since the queue was built.
        queue.Insert(1, 2);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(queue);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();

        Assert.Contains("GONE-3", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("1 eligible", cut.Find(".cleanup-eligible-pill").TextContent);
        cleanupService.Received(1).AdvanceSession(Arg.Any<Movie>(), Arg.Any<CleanupOutcome>());
        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 1), CleanupOutcome.Kept);
        cleanupService.DidNotReceive().EndSession();
    }

    [Fact]
    public void OnlyDeletedMoviesLeftInTheQueue_EndsTheSessionOnAdvance()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var queue = Queued([new Movie { Id = 1, Code = "LAST-1", Status = MovieStatus.Got }]);
        queue.Add(2);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(queue);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();

        cleanupService.Received(1).EndSession();
        Assert.NotEmpty(cut.FindAll(".cleanup-summary"));
    }

    [Fact]
    public void Summary_ShowsStatisticsAndCategorizedMovies()
    {
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new MovieCleanupSession
        {
            Order = CleanupOrder.Random,
            Queue = [],
            StartedAt = start,
            EndedAt = start.AddSeconds(100)
        };
        session.Decisions.AddRange(
        [
            new CleanupDecision(1, "DEL-1", "Deleted Movie", true, 3L * 1024 * 1024 * 1024, false, CleanupOutcome.Deleted, start),
            new CleanupDecision(2, "SNZ-2", "Snoozed Movie", true, null, false, CleanupOutcome.Snoozed, start),
            new CleanupDecision(3, "BLK-3", "Blacklisted Movie", false, null, false, CleanupOutcome.Blacklisted, start),
            new CleanupDecision(4, "FLG-4", "Flagged Movie", true, null, true, CleanupOutcome.Kept, start),
            new CleanupDecision(5, "KPT-5", "Kept Movie", true, null, false, CleanupOutcome.Kept, start),
        ]);
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Equal("3 GB", cut.Find(".cleanup-stat-freed .cleanup-stat-value").TextContent);
        Assert.Equal("1m 40s", cut.Find(".cleanup-stat-duration .cleanup-stat-value").TextContent);
        Assert.Equal("20s", cut.Find(".cleanup-stat-average .cleanup-stat-value").TextContent);
        Assert.Equal("5", cut.Find(".cleanup-stat-reviewed .cleanup-stat-value").TextContent);

        Assert.Contains("DEL-1", cut.Find(".cleanup-summary-deleted").TextContent);
        Assert.Contains("3 GB freed", cut.Find(".cleanup-summary-deleted").TextContent);
        Assert.Equal("/image-cache/DEL-1/poster/thumb", cut.Find(".cleanup-summary-deleted img.cleanup-summary-cover").GetAttribute("src"));
        Assert.Empty(cut.Find(".cleanup-summary-deleted").QuerySelectorAll("a"));
        Assert.Contains("SNZ-2", cut.Find(".cleanup-summary-snoozed").TextContent);
        Assert.Equal("/movies/SNZ-2", cut.Find(".cleanup-summary-snoozed a.cleanup-summary-code").GetAttribute("href"));
        Assert.Contains("BLK-3", cut.Find(".cleanup-summary-blacklisted .cleanup-summary-cover-placeholder").TextContent);
        Assert.Contains("FLG-4", cut.Find(".cleanup-summary-flagged").TextContent);
        Assert.Contains("KPT-5", cut.Find(".cleanup-summary-kept").TextContent);
        _ = cleanupService.DidNotReceive().BuildQueueAsync(Arg.Any<CleanupOrder>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Summary_ShowsTagAndActorChangeCountsAndLists()
    {
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new MovieCleanupSession { Order = CleanupOrder.Random, Queue = [], StartedAt = start, EndedAt = start.AddSeconds(10) };
        session.Decisions.Add(new CleanupDecision(1, "EDT-1", "Edited Movie", true, null, false, CleanupOutcome.Kept, start));
        session.Edits.AddRange(
        [
            CleanupEdit.Tag(1, 5, "Drama", added: true),
            CleanupEdit.Tag(2, 5, "Drama", added: true),
            CleanupEdit.Tag(1, 6, "Genre › Solo", added: false),
            CleanupEdit.Actor(1, 42, "Mikami Yua", added: true),
        ]);
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Equal("2", cut.Find(".cleanup-stat-tags-added .cleanup-stat-value").TextContent);
        Assert.Equal("1", cut.Find(".cleanup-stat-tags-removed .cleanup-stat-value").TextContent);
        Assert.Equal("1", cut.Find(".cleanup-stat-actors-added .cleanup-stat-value").TextContent);
        Assert.Equal("0", cut.Find(".cleanup-stat-actors-removed .cleanup-stat-value").TextContent);

        var added = Assert.Single(cut.FindAll(".cleanup-summary-tags-added .cleanup-summary-edit"));
        Assert.Contains("Drama", added.TextContent);
        Assert.Equal("×2", added.QuerySelector(".cleanup-summary-edit-count")!.TextContent);
        Assert.Equal("added to 2 movies", added.GetAttribute("title"));
        Assert.Equal("Genre › Solo", cut.Find(".cleanup-summary-tags-removed .cleanup-summary-edit").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-summary-tags-removed .cleanup-summary-edit-count"));
        Assert.Equal("Mikami Yua", cut.Find(".cleanup-summary-actors-added .cleanup-summary-edit").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-summary-actors-removed"));
    }

    [Fact]
    public void Summary_OmitsCategoriesWithNoMovies()
    {
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new MovieCleanupSession { Order = CleanupOrder.Random, Queue = [], StartedAt = start, EndedAt = start.AddSeconds(10) };
        session.Decisions.Add(new CleanupDecision(1, "ONLY-1", "Only Movie", true, null, false, CleanupOutcome.Kept, start));
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Single(cut.FindAll(".cleanup-summary-section"));
        Assert.Empty(cut.FindAll(".cleanup-summary-deleted"));
    }

    [Fact]
    public void Summary_FinishButton_DiscardsSessionAndNavigatesToMovies()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "FIN-1", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);
        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-stop-btn").Click();

        cut.Find(".cleanup-finish-btn").Click();

        cleanupService.Received(1).DiscardSession();
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public void Summary_StartNewSessionButton_DiscardsSessionAndBuildsAFreshQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ =>
        Queued([
            new Movie { Id = 1, Code = "NEW-1", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);
        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-stop-btn").Click();

        cut.Find(".cleanup-new-session-btn").Click();

        cleanupService.Received(1).DiscardSession();
        _ = cleanupService.Received(2).BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".cleanup-summary"));
        Assert.Contains("NEW-1", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("Session: 0 reviewed", cut.Markup);
    }

    [Fact]
    public void MovieWithALocalFile_AutoplaysOnLoad()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();

        var video = cut.Find("video.cleanup-video");
        Assert.Equal("/api/movies/1/stream", video.GetAttribute("src"));
        Assert.True(video.HasAttribute("autoplay"));
        Assert.True(video.HasAttribute("controls"));
        Assert.Empty(cut.FindAll(".cleanup-play-btn"));
        Assert.Empty(cut.FindAll(".cleanup-not-linked-note"));
    }

    [Fact]
    public void VrToggle_ShowsTheViewerOverTheVideoAndTogglesBack()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();

        // Off by default: the plain video, with an un-pressed toggle.
        Assert.Empty(cut.FindAll(".vr-viewer"));
        Assert.DoesNotContain("video-player-vr", cut.Find("video.cleanup-video").ClassList);
        Assert.Equal("false", cut.Find(".cleanup-vr-toggle").GetAttribute("aria-pressed"));

        cut.Find(".cleanup-vr-toggle").Click();

        Assert.Single(cut.FindAll(".video-player-area .vr-viewer"));
        Assert.Contains("video-player-vr", cut.Find("video.cleanup-video").ClassList);
        Assert.Equal("true", cut.Find(".cleanup-vr-toggle").GetAttribute("aria-pressed"));

        cut.Find(".cleanup-vr-toggle").Click();

        Assert.Empty(cut.FindAll(".vr-viewer"));
        Assert.DoesNotContain("video-player-vr", cut.Find("video.cleanup-video").ClassList);
    }

    [Fact]
    public void AVrMovieTheViewerSupports_OpensInTheViewer_WithItsProjection()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000, VrType = VrFormat.FisheyeSbs },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();

        Assert.Single(cut.FindAll(".video-player-area .vr-viewer"));
        Assert.Equal("true", cut.Find(".cleanup-vr-toggle").GetAttribute("aria-pressed"));
        Assert.Equal("fisheye", cut.Find(".vr-viewer-projection option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void VrMode_DoesNotCarryOverToTheNextCard()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
            new Movie { Id = 2, Code = "JF-2", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-vr-toggle").Click();
        Assert.Single(cut.FindAll(".vr-viewer"));

        cut.Find(".cleanup-skip-btn").Click();

        // A flat movie must not inherit the previous VR movie's unwarping.
        Assert.Equal("/api/movies/2/stream", cut.Find("video.cleanup-video").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".vr-viewer"));
        Assert.Equal("false", cut.Find(".cleanup-vr-toggle").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void MovieWithoutALocalFile_HasNoVrToggle()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "NL-1", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService, Substitute.For<IJellyfinClient>());

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll(".cleanup-vr-toggle"));
    }

    private static readonly TrickplayLayout SampleTrickplay = new(
        Width: 320, Height: 180, TileWidth: 10, TileHeight: 10, ThumbnailCount: 18, IntervalMs: 10000,
        DurationSeconds: 180, TileUrlTemplate: "/trickplay/1/0123456789abcdef/{index}.webp");

    [Fact]
    public void MovieWithTrickplay_ShowsScrubBarBelowTheVideo()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        var trickplayService = Substitute.For<ITrickplayService>();
        trickplayService.GetAsync(1, Arg.Any<CancellationToken>()).Returns(SampleTrickplay);
        using var factory = SetUpServices(cleanupService, trickplayService: trickplayService);

        var cut = Render<MovieCleanup>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cleanup-player-column > .video-player-area + .scrub-bar")));
        Assert.Single(cut.FindAll(".video-player-area video.cleanup-video"));
        // The scrub bar replaces the browser's own seek bar, so the video is marked to hide it.
        Assert.Contains("video-player-scrub", cut.Find("video.cleanup-video").ClassList);
    }

    [Fact]
    public void MovieWithoutTrickplayOrAKnownLength_KeepsTheNativeSeekBarUntilTheVideoReportsItsLength()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        var trickplayService = Substitute.For<ITrickplayService>();
        trickplayService.GetAsync(1, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        using var factory = SetUpServices(cleanupService, trickplayService: trickplayService);

        var cut = Render<MovieCleanup>();

        Assert.Single(cut.FindAll("video.cleanup-video"));
        Assert.Empty(cut.FindAll(".scrub-bar"));
        // Without a scrub bar the native seek bar is the only one, so it must stay visible.
        Assert.DoesNotContain("video-player-scrub", cut.Find("video.cleanup-video").ClassList);
    }

    [Theory]
    [InlineData("/movies/review")]
    [InlineData("/movies/cleanup")]
    public void MovieWithoutTrickplay_ShowsTheScrubBarInPlaceOfTheNativeSeekBar(string route)
    {
        // The scrub bar without thumbnails, not the native seek bar with a timeline under it.
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000, MediaDurationSeconds = 180 },
        ]));
        cleanupService.StampReviewedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var trickplayService = Substitute.For<ITrickplayService>();
        trickplayService.GetAsync(1, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        using var factory = SetUpServices(cleanupService, trickplayService: trickplayService);
        NavigateTo(route);

        var cut = Render<MovieCleanup>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cleanup-player-column > .video-player-area + .scrub-bar")));
        var scrubBar = cut.FindComponent<ScrubBar>().Instance;
        Assert.Null(scrubBar.Trickplay);
        Assert.Equal(180d, scrubBar.DurationSeconds);
        var video = cut.Find("video.cleanup-video");
        Assert.Contains("video-player-scrub", video.ClassList);
        Assert.Equal("nofullscreen", video.GetAttribute("controlslist"));
        foreach (var info in cut.FindComponents<ShortcutsInfo>())
        {
            Assert.Contains("F", info.Instance.Shortcuts.Select(s => s.Keys[0]));
        }
    }

    [Fact]
    public void SlowTrickplayLookup_DoesNotDelayTheVideo()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        var pending = new TaskCompletionSource<TrickplayLayout?>();
        var trickplayService = Substitute.For<ITrickplayService>();
        trickplayService.GetAsync(1, Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        using var factory = SetUpServices(cleanupService, trickplayService: trickplayService);

        var cut = Render<MovieCleanup>();

        Assert.Single(cut.FindAll("video.cleanup-video"));
        Assert.Empty(cut.FindAll(".scrub-bar"));

        pending.SetResult(SampleTrickplay);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scrub-bar")));
    }

    [Fact]
    public async Task TrickplayArrivingAfterAdvancingToTheNextCard_IsDiscarded()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
            new Movie { Id = 2, Code = "JF-2", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 },
        ]));
        var firstCardTrickplay = new TaskCompletionSource<TrickplayLayout?>();
        var trickplayService = Substitute.For<ITrickplayService>();
        trickplayService.GetAsync(1, Arg.Any<CancellationToken>()).Returns(_ => firstCardTrickplay.Task);
        trickplayService.GetAsync(2, Arg.Any<CancellationToken>()).Returns((TrickplayLayout?)null);
        using var factory = SetUpServices(cleanupService, trickplayService: trickplayService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();
        Assert.Equal("/api/movies/2/stream", cut.Find("video.cleanup-video").GetAttribute("src"));

        firstCardTrickplay.SetResult(SampleTrickplay);
        // The late result's re-render is marshalled onto the renderer's dispatcher; flush it so the
        // assertion below can't pass merely because it ran before that render.
        await cut.InvokeAsync(() => { });

        Assert.Empty(cut.FindAll(".scrub-bar"));
    }

    [Fact]
    public void MovieWithoutALocalFile_ShowsTheNoFileNote_NotAVideoOrPlayButton()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "NOJF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1" },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll("video"));
        Assert.Empty(cut.FindAll(".cleanup-play-btn"));
        Assert.Contains("No local video file", cut.Find(".cleanup-not-linked-note").TextContent);
    }

    [Fact]
    public void LocalFileGoneAtPlayTime_ShowsPlayButtonFallback()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
        ]));
        // Recorded as having a file, but it's gone by the time it's played.
        var streams = Substitute.For<IMovieStreamService>();
        streams.GetMainFilePathAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);
        using var factory = SetUpServices(cleanupService, streamService: streams);

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll("video"));
        Assert.NotEmpty(cut.FindAll(".cleanup-play-btn"));
        Assert.Empty(cut.FindAll(".cleanup-not-linked-note"));
    }

    [Fact]
    public void AdvancingToTheNextCard_AutoplaysTheNextMovie()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
            new Movie { Id = 2, Code = "JF-2", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 },
        ]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();
        Assert.Equal("/api/movies/1/stream", cut.Find("video.cleanup-video").GetAttribute("src"));

        cut.Find(".cleanup-skip-btn").Click();

        Assert.Equal("/api/movies/2/stream", cut.Find("video.cleanup-video").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".cleanup-play-btn"));
    }

    [Fact]
    public void AdvancingPastACardWhileItsStreamUrlIsStillLoading_DiscardsTheStaleResult()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "JF-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 },
            new Movie { Id = 2, Code = "JF-2", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 },
        ]));
        var tcs1 = new TaskCompletionSource<string?>();
        var tcs2 = new TaskCompletionSource<string?>();
        var streams = Substitute.For<IMovieStreamService>();
        streams.GetMainFilePathAsync(1, Arg.Any<CancellationToken>()).Returns(_ => tcs1.Task);
        streams.GetMainFilePathAsync(2, Arg.Any<CancellationToken>()).Returns(_ => tcs2.Task);
        using var factory = SetUpServices(cleanupService, streamService: streams);

        var cut = Render<MovieCleanup>();
        Assert.Empty(cut.FindAll("video"));

        cut.Find(".cleanup-skip-btn").Click();
        Assert.Contains("JF-2", cut.Find(".cleanup-code").TextContent);
        Assert.Empty(cut.FindAll("video"));

        tcs1.SetResult("/library/JF-1/JF-1.mp4");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("video")));

        tcs2.SetResult("/library/JF-2/JF-2.mp4");

        cut.WaitForAssertion(() =>
        {
            var video = cut.Find("video.cleanup-video");
            Assert.Equal("/api/movies/2/stream", video.GetAttribute("src"));
        });
    }

    [Fact]
    public void ChangingOrder_AutoplaysFirstMovieOfNewOrder()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "RND-1", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 1_000_000 }]));
        cleanupService.BuildQueueAsync(CleanupOrder.LargestFirst, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "LRG-1", Status = MovieStatus.Got, JellyfinItemId = "item-2", LocalFileSizeBytes = 1_000_000 }]));
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();
        Assert.Equal("/api/movies/1/stream", cut.Find("video.cleanup-video").GetAttribute("src"));

        cut.Find("#cleanup-order").Change("LargestFirst");

        Assert.Equal("/api/movies/2/stream", cut.Find("video.cleanup-video").GetAttribute("src"));
    }

    [Fact]
    public void MovieCover_RendersOverTitle()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "CVR-1", MetaTitle = "Cover Movie", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var cover = cut.Find("img.cleanup-cover-img");
        Assert.Equal("/image-cache/CVR-1/poster/full", cover.GetAttribute("src"));
        Assert.Equal("Cover Movie", cover.GetAttribute("alt"));
    }

    [Fact]
    public void ClickingMovieCover_OpensImageLightboxWithFullJacketByDefault()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "CVR-1", MetaTitle = "Cover Movie", Status = MovieStatus.Got, MetaSourceName = "Local" }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var coverBtn = cut.Find(".cleanup-cover-clickable");
        Assert.Equal("Click to view full jacket image", coverBtn.GetAttribute("title"));
        Assert.Equal("View cover image", coverBtn.GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));

        coverBtn.Click();

        Assert.NotEmpty(cut.FindAll(".image-lightbox-backdrop"));
        var lightboxImg = cut.Find(".image-lightbox-image");
        Assert.Equal("/image-cache/CVR-1/fanart/full", lightboxImg.GetAttribute("src"));
        Assert.Equal("Cover Movie", cut.Find(".image-lightbox-name").TextContent.Trim());

        // Toggle to poster
        var posterBtn = cut.FindAll(".image-lightbox-actions button").First(b => b.TextContent.Trim() == "Poster");
        posterBtn.Click();
        Assert.Equal("/image-cache/CVR-1/poster/full", cut.Find(".image-lightbox-image").GetAttribute("src"));

        // Toggle back to full jacket
        var jacketBtn = cut.FindAll(".image-lightbox-actions button").First(b => b.TextContent.Trim() == "Full jacket");
        jacketBtn.Click();
        Assert.Equal("/image-cache/CVR-1/fanart/full", cut.Find(".image-lightbox-image").GetAttribute("src"));

        // Close lightbox
        cut.Find(".image-lightbox-close-btn").Click();
        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));
    }

    [Fact]
    public void ClickingMovieCover_WhenNoMetadata_OpensLightboxWithPosterOnly()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "CVR-2", MetaTitle = "Raw Movie", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        cut.Find(".cleanup-cover-clickable").Click();

        Assert.NotEmpty(cut.FindAll(".image-lightbox-backdrop"));
        Assert.Equal("/image-cache/CVR-2/poster/full", cut.Find(".image-lightbox-image").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".image-lightbox-actions [aria-label='Image variant']"));
    }

    [Fact]
    public void AdvancingQueue_ClosesCoverLightbox()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([
                new Movie { Id = 1, Code = "CVR-1", MetaTitle = "Movie 1", Status = MovieStatus.Got, MetaSourceName = "Local" },
                new Movie { Id = 2, Code = "CVR-2", MetaTitle = "Movie 2", Status = MovieStatus.Got, MetaSourceName = "Local" },
            ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        cut.Find(".cleanup-cover-clickable").Click();
        Assert.NotEmpty(cut.FindAll(".image-lightbox-backdrop"));

        cut.Find(".cleanup-skip-btn").Click();

        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));
        Assert.Contains("CVR-2", cut.Find(".cleanup-code").TextContent);
    }

    [Fact]
    public void MovieActors_RendersActorImagesAndInitials()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ACT-1", MetaTitle = "Movie With Cast", Status = MovieStatus.Got, MetaActresses = "Mikami Yua, Unknown Actress" }]));
        cleanupService.GetMovieActorsAsync(1, "Mikami Yua, Unknown Actress", Arg.Any<CancellationToken>())
            .Returns([
                new CleanupActorItem("Mikami Yua", 42, true),
                new CleanupActorItem("Unknown Actress", null, false)
            ]);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var actorImg = cut.Find("img.cleanup-actor-img");
        Assert.Equal("/actor-image/42/thumb", actorImg.GetAttribute("src"));
        Assert.Equal("Mikami Yua", actorImg.GetAttribute("alt"));

        var initials = cut.Find(".cleanup-actor-initials");
        Assert.Equal("UA", initials.TextContent.Trim());
    }

    private IMovieCleanupService CleanupServiceWithCast(string? metaActresses, params CleanupActorItem[] actors)
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([
                new Movie { Id = 1, Code = "CAST-1", MetaTitle = "First", Status = MovieStatus.Got, MetaActresses = metaActresses },
                new Movie { Id = 2, Code = "CAST-2", MetaTitle = "Second", Status = MovieStatus.Got }
            ]));
        cleanupService.GetMovieActorsAsync(1, metaActresses, Arg.Any<CancellationToken>()).Returns([.. actors]);
        return cleanupService;
    }

    [Fact]
    public void EditCast_IsCollapsedByDefault_AndTogglesRemoveButtonsAndSearch()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua, Unknown Actress",
            new CleanupActorItem("Mikami Yua", 42, false), new CleanupActorItem("Unknown Actress", null, false));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Equal("Edit Cast", cut.Find("button.cleanup-edit-cast-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-actor-remove-btn"));
        Assert.Empty(cut.FindAll("input.cast-search-input"));

        cut.Find("button.cleanup-edit-cast-btn").Click();

        Assert.Equal("Done", cut.Find("button.cleanup-edit-cast-btn").TextContent.Trim());
        Assert.Equal(2, cut.FindAll(".cleanup-actor-remove-btn").Count);
        Assert.Single(cut.FindAll("input.cast-search-input"));

        cut.Find("button.cleanup-edit-cast-btn").Click();

        Assert.Empty(cut.FindAll(".cleanup-actor-remove-btn"));
        Assert.Empty(cut.FindAll("input.cast-search-input"));
    }

    [Fact]
    public void EditCast_MovieWithNoCast_StillOffersEditingSoActressesCanBeAdded()
    {
        var cleanupService = CleanupServiceWithCast(null);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();

        Assert.Empty(cut.FindAll(".cleanup-actor-remove-btn"));
        Assert.Single(cut.FindAll("input.cast-search-input"));
    }

    [Fact]
    public void EditCast_RemovingTrackedActor_UnlinksByIdAndRefreshesTheCardFromTheDatabase()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua, Unknown Actress",
            new CleanupActorItem("Mikami Yua", 42, false), new CleanupActorItem("Unknown Actress", null, false));
        cleanupService.GetMetaActressesAsync(1, Arg.Any<CancellationToken>()).Returns("Unknown Actress");
        cleanupService.GetMovieActorsAsync(1, "Unknown Actress", Arg.Any<CancellationToken>())
            .Returns([new CleanupActorItem("Unknown Actress", null, false)]);
        var movieService = Substitute.For<IMovieService>();
        movieService.RemoveActorFromMovieAsync(1, 42, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService, movieService: movieService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();
        cut.FindAll(".cleanup-actor-remove-btn")[0].Click();

        movieService.Received(1).RemoveActorFromMovieAsync(1, 42, Arg.Any<CancellationToken>());
        cleanupService.Received(1).RecordActorEditAsync(1, 42, false, Arg.Any<CancellationToken>());
        Assert.DoesNotContain(cut.FindAll(".cleanup-actor-name"), n => n.TextContent.Trim() == "Mikami Yua");
        Assert.Contains(cut.FindAll(".cleanup-actor-name"), n => n.TextContent.Trim() == "Unknown Actress");
        // Edit mode survives the edit so several changes can be made in a row.
        Assert.Single(cut.FindAll(".cleanup-actor-remove-btn"));
    }

    [Fact]
    public void EditCast_RemovingUnmatchedName_UnlinksByName()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua, Unknown Actress",
            new CleanupActorItem("Mikami Yua", 42, false), new CleanupActorItem("Unknown Actress", null, false));
        cleanupService.GetMetaActressesAsync(1, Arg.Any<CancellationToken>()).Returns("Mikami Yua");
        cleanupService.GetMovieActorsAsync(1, "Mikami Yua", Arg.Any<CancellationToken>())
            .Returns([new CleanupActorItem("Mikami Yua", 42, false)]);
        var movieService = Substitute.For<IMovieService>();
        movieService.RemoveActorFromMovieAsync(1, "Unknown Actress", Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService, movieService: movieService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();
        cut.FindAll(".cleanup-actor-remove-btn")[1].Click();

        movieService.Received(1).RemoveActorFromMovieAsync(1, "Unknown Actress", Arg.Any<CancellationToken>());
        cleanupService.Received(1).RecordUntrackedActorRemoval(1, "Unknown Actress");
        Assert.DoesNotContain(cut.FindAll(".cleanup-actor-name"), n => n.TextContent.Trim() == "Unknown Actress");
    }

    [Fact]
    public void EditCast_SearchingAndPickingAnActor_AddsThemToTheMovie()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua", new CleanupActorItem("Mikami Yua", 42, false));
        cleanupService.GetMetaActressesAsync(1, Arg.Any<CancellationToken>()).Returns("Mikami Yua, Hatano Yui");
        cleanupService.GetMovieActorsAsync(1, "Mikami Yua, Hatano Yui", Arg.Any<CancellationToken>())
            .Returns([new CleanupActorItem("Mikami Yua", 42, false), new CleanupActorItem("Hatano Yui", 7, false)]);
        var actorService = Substitute.For<IActorService>();
        actorService.SearchActorsAsync("Hatano", Arg.Any<CancellationToken>())
            .Returns([new ActorSearchItem(42, "Mikami Yua", false), new ActorSearchItem(7, "Hatano Yui", false)]);
        var movieService = Substitute.For<IMovieService>();
        movieService.AddActorToMovieAsync(1, 7, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService, movieService: movieService, actorService: actorService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();
        cut.Find("input.cast-search-input").Input("Hatano");

        // Mikami Yua (already on the movie) is filtered out of the results.
        var candidate = Assert.Single(cut.FindAll(".cast-search-candidate"));
        candidate.Click();

        movieService.Received(1).AddActorToMovieAsync(1, 7, Arg.Any<CancellationToken>());
        cleanupService.Received(1).RecordActorEditAsync(1, 7, true, Arg.Any<CancellationToken>());
        Assert.Contains(cut.FindAll(".cleanup-actor-name"), n => n.TextContent.Trim() == "Hatano Yui");
    }

    [Fact]
    public void EditCast_WhenTheServiceFails_ShowsTheErrorAndKeepsTheCast()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua", new CleanupActorItem("Mikami Yua", 42, false));
        cleanupService.GetMetaActressesAsync(1, Arg.Any<CancellationToken>()).Returns("Mikami Yua");
        var movieService = Substitute.For<IMovieService>();
        movieService.RemoveActorFromMovieAsync(1, 42, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("Actor is not on this movie."));
        using var factory = SetUpServices(cleanupService, movieService: movieService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();
        cut.Find(".cleanup-actor-remove-btn").Click();

        Assert.Equal("Actor is not on this movie.", cut.Find(".cleanup-cast-error").TextContent.Trim());
        cleanupService.DidNotReceive().RecordActorEditAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.Contains(cut.FindAll(".cleanup-actor-name"), n => n.TextContent.Trim() == "Mikami Yua");
    }

    [Fact]
    public void EditCast_AdvancingToTheNextMovie_LeavesEditModeAndClearsTheError()
    {
        var cleanupService = CleanupServiceWithCast("Mikami Yua", new CleanupActorItem("Mikami Yua", 42, false));
        cleanupService.GetMetaActressesAsync(1, Arg.Any<CancellationToken>()).Returns("Mikami Yua");
        var movieService = Substitute.For<IMovieService>();
        movieService.RemoveActorFromMovieAsync(1, 42, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("boom"));
        using var factory = SetUpServices(cleanupService, movieService: movieService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-cast-btn").Click();
        cut.Find(".cleanup-actor-remove-btn").Click();
        Assert.Single(cut.FindAll(".cleanup-cast-error"));

        cut.Find("button.cleanup-skip-btn").Click();

        Assert.Contains("CAST-2", cut.Find(".cleanup-code").TextContent);
        Assert.Equal("Edit Cast", cut.Find("button.cleanup-edit-cast-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-cast-error"));
        Assert.Empty(cut.FindAll("input.cast-search-input"));
    }

    private IMovieCleanupService CleanupServiceWithTags(params CleanupTagItem[] tags)
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([
                new Movie { Id = 1, Code = "TAG-1", MetaTitle = "First", Status = MovieStatus.Got },
                new Movie { Id = 2, Code = "TAG-2", MetaTitle = "Second", Status = MovieStatus.Got }
            ]));
        cleanupService.GetMovieTagsAsync(1, Arg.Any<CancellationToken>()).Returns([.. tags]);
        return cleanupService;
    }

    [Fact]
    public void Tags_AreShownOnTheCard_WithParentBreadcrumbAndFlaggedWhenUnapproved()
    {
        var cleanupService = CleanupServiceWithTags(
            new CleanupTagItem(1, "Subtag", "Category", false),
            new CleanupTagItem(2, "Drama", null, false),
            new CleanupTagItem(3, "Pending", null, true));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        var chips = cut.FindAll(".cleanup-tag");
        Assert.Equal(["Category › Subtag", "Drama", "Pending"], chips.Select(c => c.TextContent.Trim()));
        Assert.DoesNotContain("cleanup-tag-flagged", chips[1].ClassName);
        Assert.Contains("cleanup-tag-flagged", chips[2].ClassName);
    }

    [Fact]
    public void EditTags_ATagOnlyTheClipsCarry_IsShownImplied_WithoutARemoveButton()
    {
        var cleanupService = CleanupServiceWithTags(
            new CleanupTagItem(1, "Drama", null, false), new CleanupTagItem(2, "Squirt", null, false, IsImplicit: true));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();

        var implied = cut.Find(".cleanup-tag-implied");
        Assert.Equal("Squirt", implied.TextContent.Trim());
        Assert.Equal("From scenes, highlights or apexes", implied.GetAttribute("title"));
        Assert.Single(cut.FindAll(".cleanup-tag-remove-btn"));
    }

    [Fact]
    public void EditTags_IsCollapsedByDefault_AndTogglesRemoveButtonsAndSearch()
    {
        var cleanupService = CleanupServiceWithTags(
            new CleanupTagItem(1, "Drama", null, false), new CleanupTagItem(2, "Solo", null, false));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Equal("Edit Tags", cut.Find("button.cleanup-edit-tags-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-tag-remove-btn"));
        Assert.Empty(cut.FindAll("input.tag-search-input"));

        cut.Find("button.cleanup-edit-tags-btn").Click();

        Assert.Equal("Done", cut.Find("button.cleanup-edit-tags-btn").TextContent.Trim());
        Assert.Equal(2, cut.FindAll(".cleanup-tag-remove-btn").Count);
        Assert.Single(cut.FindAll("input.tag-search-input"));

        cut.Find("button.cleanup-edit-tags-btn").Click();

        Assert.Empty(cut.FindAll(".cleanup-tag-remove-btn"));
        Assert.Empty(cut.FindAll("input.tag-search-input"));
    }

    [Fact]
    public void EditTags_MovieWithNoTags_StillOffersEditingSoTagsCanBeAdded()
    {
        var cleanupService = CleanupServiceWithTags();
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();

        Assert.Empty(cut.FindAll(".cleanup-tag-remove-btn"));
        Assert.Single(cut.FindAll("input.tag-search-input"));
    }

    [Fact]
    public void EditTags_RemovingATag_UnlinksItAndRefreshesTheCardFromTheDatabase()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "TAG-1", MetaTitle = "First", Status = MovieStatus.Got }]));
        cleanupService.GetMovieTagsAsync(1, Arg.Any<CancellationToken>()).Returns(
            [new CleanupTagItem(1, "Drama", null, false), new CleanupTagItem(2, "Solo", null, false)],
            [new CleanupTagItem(2, "Solo", null, false)]);
        var tagService = Substitute.For<ITagService>();
        tagService.RemoveTagFromMovieAsync(1, 1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService, tagService: tagService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();
        cut.FindAll(".cleanup-tag-remove-btn")[0].Click();

        tagService.Received(1).RemoveTagFromMovieAsync(1, 1, Arg.Any<CancellationToken>());
        cleanupService.Received(1).RecordTagEditAsync(1, 1, false, Arg.Any<CancellationToken>());
        Assert.Equal(["Solo"], cut.FindAll(".cleanup-tag").Select(c => c.TextContent.Trim()));
        // Edit mode survives the edit so several changes can be made in a row.
        Assert.Single(cut.FindAll(".cleanup-tag-remove-btn"));
    }

    [Fact]
    public void EditTags_SearchingAndPickingATag_AddsItToTheMovie()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "TAG-1", MetaTitle = "First", Status = MovieStatus.Got }]));
        cleanupService.GetMovieTagsAsync(1, Arg.Any<CancellationToken>()).Returns(
            [new CleanupTagItem(1, "Drama", null, false)],
            [new CleanupTagItem(1, "Drama", null, false), new CleanupTagItem(2, "Solo", null, false)]);
        var tagService = Substitute.For<ITagService>();
        tagService.GetTagsAsync(Arg.Any<string?>(), Arg.Any<TagSortOrder>(), Arg.Any<CancellationToken>())
            .Returns([
                new TagListItem(1, "Drama", 0, false, DateTime.UtcNow),
                new TagListItem(2, "Solo", 0, false, DateTime.UtcNow)]);
        tagService.AddTagToMovieAsync(1, 2, Arg.Any<CancellationToken>()).Returns(TagOperationResult.Ok(new Tag { Id = 2, Name = "Solo" }));
        using var factory = SetUpServices(cleanupService, tagService: tagService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();
        cut.Find("input.tag-search-input").Input("o");

        // Drama (already on the movie) is filtered out of the results.
        var candidate = Assert.Single(cut.FindAll(".tag-search-candidate"));
        candidate.Click();

        tagService.Received(1).AddTagToMovieAsync(1, 2, Arg.Any<CancellationToken>());
        cleanupService.Received(1).RecordTagEditAsync(1, 2, true, Arg.Any<CancellationToken>());
        Assert.Equal(["Drama", "Solo"], cut.FindAll(".cleanup-tag").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void EditTags_WhenTheServiceFails_ShowsTheErrorAndKeepsTheTags()
    {
        var cleanupService = CleanupServiceWithTags(new CleanupTagItem(1, "Drama", null, false));
        var tagService = Substitute.For<ITagService>();
        tagService.RemoveTagFromMovieAsync(1, 1, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("Tag is not on this movie."));
        using var factory = SetUpServices(cleanupService, tagService: tagService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();
        cut.Find(".cleanup-tag-remove-btn").Click();

        Assert.Equal("Tag is not on this movie.", cut.Find(".cleanup-tag-error").TextContent.Trim());
        Assert.Equal(["Drama"], cut.FindAll(".cleanup-tag").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void EditTags_AdvancingToTheNextMovie_LeavesEditModeAndClearsTheError()
    {
        var cleanupService = CleanupServiceWithTags(new CleanupTagItem(1, "Drama", null, false));
        var tagService = Substitute.For<ITagService>();
        tagService.RemoveTagFromMovieAsync(1, 1, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("boom"));
        using var factory = SetUpServices(cleanupService, tagService: tagService);

        var cut = Render<MovieCleanup>();
        cut.Find("button.cleanup-edit-tags-btn").Click();
        cut.Find(".cleanup-tag-remove-btn").Click();
        Assert.Single(cut.FindAll(".cleanup-tag-error"));

        cut.Find("button.cleanup-skip-btn").Click();

        Assert.Contains("TAG-2", cut.Find(".cleanup-code").TextContent);
        Assert.Equal("Edit Tags", cut.Find("button.cleanup-edit-tags-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-tag-error"));
        Assert.Empty(cut.FindAll("input.tag-search-input"));
    }

    [Fact]
    public void ClickingBrokenBFrame_MarksMovieDoesNotStartRepairAndStaysOnCurrentMovie()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BFR-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "BFR-2", Status = MovieStatus.Got },
        ]));
        cleanupService.MarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var repairService = Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairService>();
        using var factory = SetUpServices(cleanupService, repairService: repairService);

        var cut = Render<MovieCleanup>();
        var btn = cut.Find(".cleanup-bframe-btn");
        Assert.Equal("Broken B-Frame", btn.TextContent.Trim());
        btn.Click();

        // Stays on BFR-1
        Assert.Contains("BFR-1", cut.Find(".cleanup-code").TextContent);
        // Toggles button text and shows badge
        Assert.Equal("Unmark B-Frame", cut.Find(".cleanup-bframe-btn").TextContent.Trim());
        Assert.Contains("Broken B-Frames", cut.Find(".cleanup-bframe-badge").TextContent);
        _ = cleanupService.Received(1).MarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>());
        _ = repairService.DidNotReceive().StartRepairJobAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingUnmarkBrokenBFrame_UnmarksMovieAndRemovesBadge()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BFR-1", Status = MovieStatus.Got, HasBrokenBFrames = true },
            new Movie { Id = 2, Code = "BFR-2", Status = MovieStatus.Got },
        ]));
        cleanupService.UnmarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var repairService = Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairService>();
        using var factory = SetUpServices(cleanupService, repairService: repairService);

        var cut = Render<MovieCleanup>();
        var btn = cut.Find(".cleanup-bframe-btn");
        Assert.Equal("Unmark B-Frame", btn.TextContent.Trim());
        Assert.NotNull(cut.Find(".cleanup-bframe-badge"));

        btn.Click();

        // Stays on BFR-1
        Assert.Contains("BFR-1", cut.Find(".cleanup-code").TextContent);
        // Toggles button text and badge is removed
        Assert.Equal("Broken B-Frame", cut.Find(".cleanup-bframe-btn").TextContent.Trim());
        Assert.Empty(cut.FindAll(".cleanup-bframe-badge"));
        _ = cleanupService.Received(1).UnmarkBrokenBFrameAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BrokenBFrameCountGreaterThanZero_ShowsBatchRepairButton()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BFR-1", Status = MovieStatus.Got }
        ]));
        cleanupService.GetBrokenBFrameCountAsync(Arg.Any<CancellationToken>()).Returns(3);
        cleanupService.GetBrokenBFrameMovieIdsAsync(Arg.Any<CancellationToken>()).Returns([10, 20, 30]);
        var repairService = Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairService>();
        using var factory = SetUpServices(cleanupService, repairService: repairService);

        var cut = Render<MovieCleanup>();
        var batchBtn = cut.Find(".cleanup-batch-repair-btn");
        Assert.Contains("Repair Broken B-Frames (3)", batchBtn.TextContent);

        batchBtn.Click();
        _ = repairService.Received(1).StartBatchRepairAsync(Arg.Is<IReadOnlyList<int>>(ids => ids.Count == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MovieHasBrokenBFrames_ShowsBadge()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BFR-1", Status = MovieStatus.Got, HasBrokenBFrames = true }
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        Assert.Contains("Broken B-Frames", cut.Find(".cleanup-bframe-badge").TextContent);
    }

    [Fact]
    public void BatchRepairFinishing_RefreshesBrokenBFrameCount()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "BFR-1", Status = MovieStatus.Got }
        ]));
        cleanupService.GetBrokenBFrameCountAsync(Arg.Any<CancellationToken>()).Returns(2, 0);
        var notifier = new MovieChangeNotifier();
        using var factory = SetUpServices(cleanupService, movieChangeNotifier: notifier);

        var cut = Render<MovieCleanup>();
        Assert.Contains("(2)", cut.Find(".cleanup-batch-repair-btn").TextContent);

        // A repair job finishing raises the notifier after clearing the movie's flag.
        notifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cleanup-batch-repair-btn")));
    }

    [Fact]
    public void ActiveSessionExists_ResumesSessionWithoutRebuildingQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var activeSession = new MovieCleanupSession
        {
            Order = CleanupOrder.ByActress,
            Library = "VR",
            Queue = Queued([new Movie { Id = 2, Code = "RESUME-2", Status = MovieStatus.Got, JellyfinItemId = "item-res", LocalFileSizeBytes = 1_000_000 }]),
            ReviewedCount = 4,
            CurrentIndex = 4,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns(["JAV", "VR"]);
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(activeSession);
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        using var factory = SetUpServices(cleanupService, jellyfinClient);

        var cut = Render<MovieCleanup>();

        Assert.Contains("RESUME-2", cut.Find(".cleanup-code").TextContent);
        Assert.Contains("Session: 4 reviewed", cut.Find(".cleanup-session-count").TextContent);
        Assert.Contains("1 eligible", cut.Find(".cleanup-eligible-pill").TextContent);
        Assert.Equal("/api/movies/2/stream", cut.Find("video.cleanup-video").GetAttribute("src"));
        _ = cleanupService.DidNotReceive().BuildQueueAsync(Arg.Any<CleanupOrder>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickingRestart_DiscardsActiveSessionAndRebuildsQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ =>
        Queued([
            new Movie { Id = 1, Code = "RST-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "RST-2", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();
        Assert.Contains("Session: 1 reviewed", cut.Markup);

        var restartBtn = cut.Find(".cleanup-restart-btn");
        Assert.NotNull(restartBtn);
        restartBtn.Click();

        cleanupService.Received(1).DiscardSession();
        _ = cleanupService.Received(2).BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.Contains("Session: 0 reviewed", cut.Markup);
        Assert.Contains("RST-1", cut.Find(".cleanup-code").TextContent);
    }

    [Fact]
    public void ChangingOrder_DiscardsActiveSessionAndRebuildsQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "ORD-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.LargestFirst, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "ORD-2", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("#cleanup-order").Change("LargestFirst");

        cleanupService.Received(1).DiscardSession();
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.LargestFirst, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangingLibrary_DiscardsActiveSessionAndRebuildsQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.GetEligibleLibrariesAsync(Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>()).Returns(["JAV", "VR"]);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, null, Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "LIB-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.Random, "VR", Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "LIB-2", Status = MovieStatus.Got, JellyfinLibraryName = "VR" }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find("#cleanup-library").Change("VR");

        cleanupService.Received(1).DiscardSession();
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.Random, "VR", Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdvancingQueue_CallsAdvanceSessionOnCleanupService()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "ADV-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "ADV-2", Status = MovieStatus.Got },
        ]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-skip-btn").Click();

        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 1), CleanupOutcome.Kept);
    }

    [Fact]
    public void SnoozeAndBlacklist_RecordTheirOutcome()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        Queued([
            new Movie { Id = 1, Code = "OUT-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "OUT-2", Status = MovieStatus.Got },
            new Movie { Id = 3, Code = "OUT-3", Status = MovieStatus.Got },
        ]));
        cleanupService.SnoozeAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        cleanupService.BlacklistAsync(2, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-snooze-btn").Click();
        cut.Find(".cleanup-blacklist-btn").Click();

        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 1), CleanupOutcome.Snoozed);
        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 2), CleanupOutcome.Blacklisted);
    }

    private static Actor ScopeActor(int id = 5) => new() { Id = id, FirstName = "Arina", LastName = "Hashimoto" };

    [Fact]
    public void NoActorId_ShowsNoScopeBanner()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([]);
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll(".cleanup-scope"));
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), null, Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ActorIdQuery_BuildsScopedQueueStartsScopedSessionAndShowsBanner()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var movies = new List<Movie> { new() { Id = 1, Code = "ABC-123", MetaTitle = "First Movie", Status = MovieStatus.Got } };
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), 5, Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Queued(movies));
        var actorService = Substitute.For<IActorService>();
        actorService.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(ScopeActor());
        using var factory = SetUpServices(cleanupService, actorService: actorService);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/cleanup?actorId=5");

        var cut = Render<MovieCleanup>();

        Assert.Contains(ScopeActor().DisplayName, cut.Find(".cleanup-scope").TextContent);
        Assert.Equal($"/actors/{Uri.EscapeDataString(ScopeActor().DisplayName)}", cut.Find(".cleanup-scope a").GetAttribute("href"));
        Assert.Contains("ABC-123", cut.Markup);
        _ = cleanupService.Received().GetEligibleLibrariesAsync(5, Arg.Any<CleanupMode>(), Arg.Any<CancellationToken>());
        cleanupService.Received(1).StartSession(Arg.Is<List<int>>(ids => ids.SequenceEqual(new[] { 1 })), CleanupOrder.Random, null, 5);
    }

    [Fact]
    public void ActorIdQuery_ResumedSessionOfAnotherScope_IsDiscardedAndQueueRebuilt()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var unscoped = new MovieCleanupSession
        {
            Order = CleanupOrder.Random,
            Queue = Queued([new Movie { Id = 9, Code = "OLD-999", Status = MovieStatus.Got }]),
            StartedAt = DateTimeOffset.UtcNow
        };
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(unscoped);
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), 5, Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "NEW-001", Status = MovieStatus.Got }]));
        var actorService = Substitute.For<IActorService>();
        actorService.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(ScopeActor());
        using var factory = SetUpServices(cleanupService, actorService: actorService);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/cleanup?actorId=5");

        var cut = Render<MovieCleanup>();

        Assert.Contains("NEW-001", cut.Markup);
        Assert.DoesNotContain("OLD-999", cut.Markup);
        cleanupService.Received(1).DiscardSession();
    }

    [Fact]
    public void ActorIdQuery_ResumedSessionOfSameScope_IsResumed()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var scoped = new MovieCleanupSession
        {
            Order = CleanupOrder.Random,
            ActorId = 5,
            Queue = Queued([new Movie { Id = 2, Code = "RESUMED-2", Status = MovieStatus.Got }]),
            StartedAt = DateTimeOffset.UtcNow
        };
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(scoped);
        var actorService = Substitute.For<IActorService>();
        actorService.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(ScopeActor());
        using var factory = SetUpServices(cleanupService, actorService: actorService);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/cleanup?actorId=5");

        var cut = Render<MovieCleanup>();

        Assert.Contains("RESUMED-2", cut.Markup);
        cleanupService.DidNotReceive().DiscardSession();
        _ = cleanupService.DidNotReceive().BuildQueueAsync(Arg.Any<CleanupOrder>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NoActorId_ResumedScopedSession_KeepsItsScopeAndShowsBanner()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        var scoped = new MovieCleanupSession
        {
            Order = CleanupOrder.Random,
            ActorId = 5,
            Queue = Queued([new Movie { Id = 2, Code = "RESUMED-2", Status = MovieStatus.Got }]),
            StartedAt = DateTimeOffset.UtcNow
        };
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(scoped);
        var actorService = Substitute.For<IActorService>();
        actorService.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(ScopeActor());
        using var factory = SetUpServices(cleanupService, actorService: actorService);

        var cut = Render<MovieCleanup>();

        Assert.Contains("RESUMED-2", cut.Markup);
        Assert.Contains(ScopeActor().DisplayName, cut.Find(".cleanup-scope").TextContent);
        cleanupService.DidNotReceive().DiscardSession();
    }

    [Fact]
    public void ScopeBanner_AllMoviesButton_DiscardsSessionAndBuildsUnscopedQueue()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), 5, Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "SCOPED-1", Status = MovieStatus.Got }]));
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), null, Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 2, Code = "ALL-2", Status = MovieStatus.Got }]));
        var actorService = Substitute.For<IActorService>();
        actorService.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(ScopeActor());
        using var factory = SetUpServices(cleanupService, actorService: actorService);
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/movies/cleanup?actorId=5");
        var cut = Render<MovieCleanup>();

        cut.Find(".cleanup-scope-clear").Click();

        cut.WaitForAssertion(() => Assert.Contains("ALL-2", cut.Markup));
        Assert.Empty(cut.FindAll(".cleanup-scope"));
        Assert.EndsWith("/movies/cleanup", nav.Uri);
        cleanupService.Received(1).DiscardSession();
    }

    private void NavigateTo(string uri) => Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(uri);

    private IMovieCleanupService ReviewQueueService(params Movie[] movies)
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), CleanupMode.Review, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Queued([.. movies]));
        cleanupService.StampReviewedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        return cleanupService;
    }

    [Fact]
    public void ReviewRoute_BuildsAReviewQueueAndStartsAReviewSession()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        Assert.Contains("REV-1", cut.Find(".cleanup-code").TextContent);
        Assert.Equal("Review", cut.Find(".cleanup-page-title").TextContent);
        cleanupService.Received(1).StartSession(Arg.Any<List<int>>(), CleanupOrder.Random, null, null, CleanupMode.Review, true);
    }

    [Fact]
    public void ReviewMode_HidesDeleteAndSkip_ShowsNextAndKeepsTheOtherActions()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll(".cleanup-delete-btn"));
        Assert.Empty(cut.FindAll(".cleanup-skip-btn"));
        Assert.Empty(cut.FindAll(".cleanup-snooze-btn"));
        Assert.Empty(cut.FindAll(".cleanup-blacklist-btn"));
        Assert.NotNull(cut.Find(".cleanup-next-btn"));
        Assert.NotNull(cut.Find(".cleanup-stop-btn"));
        Assert.NotNull(cut.Find(".cleanup-bframe-btn"));
        Assert.NotNull(cut.Find(".cleanup-edit-cast-btn"));
        Assert.NotNull(cut.Find(".cleanup-edit-tags-btn"));
        Assert.NotNull(cut.Find(".cleanup-unreviewed-label"));
    }

    [Fact]
    public void ReviewMode_ShowsSceneEditorWithShortcuts_CleanupModeDoesNot()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        // One editor for scenes, highlights and apexes, with M / Shift+M, H / Shift+H and A.
        var editor = cut.FindComponent<ClipEditor>();
        Assert.Equal(1, editor.Instance.MovieId);
        Assert.Equal("video.cleanup-video", editor.Instance.VideoSelector);
        Assert.True(editor.Instance.EnableShortcuts);
        // The shortcuts are listed in an info icon; no player yet, so no F.
        Assert.NotNull(cut.Find(".cleanup-shortcuts .shortcuts-info"));
        var shortcuts = cut.FindComponent<ShortcutsInfo>().Instance.Shortcuts;
        Assert.Equal(["Space", "←", ",", "N", "M", "Shift+M", "H", "Shift+H", "A"], shortcuts.Select(s => s.Keys[0]));
    }

    [Fact]
    public void CleanupMode_HasNoSceneEditor()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), CleanupMode.Cleanup, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "CLN-1", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindComponents<ClipEditor>());
    }

    [Fact]
    public void CleanupRoute_KeepsDeleteAndSkip_AndHasNoNextButton()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), CleanupMode.Cleanup, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 1, Code = "CLN-1", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);

        var cut = Render<MovieCleanup>();

        Assert.NotNull(cut.Find(".cleanup-delete-btn"));
        Assert.NotNull(cut.Find(".cleanup-skip-btn"));
        Assert.NotNull(cut.Find(".cleanup-snooze-btn"));
        Assert.NotNull(cut.Find(".cleanup-blacklist-btn"));
        Assert.Empty(cut.FindAll(".cleanup-next-btn"));
        Assert.Empty(cut.FindAll(".cleanup-unreviewed-label"));
        Assert.Equal("Cleanup", cut.Find(".cleanup-page-title").TextContent);
    }

    [Fact]
    public void ReviewMode_ClickingNext_StampsReviewedAndAdvances()
    {
        var cleanupService = ReviewQueueService(
            new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got },
            new Movie { Id = 2, Code = "REV-2", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-next-btn").Click();

        Assert.Contains("REV-2", cut.Find(".cleanup-code").TextContent);
        _ = cleanupService.Received(1).StampReviewedAsync(1, Arg.Any<CancellationToken>());
        cleanupService.Received(1).AdvanceSession(Arg.Is<Movie>(m => m.Id == 1), CleanupOutcome.Kept);
        _ = cleanupService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        _ = cleanupService.DidNotReceive().SnoozeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        _ = cleanupService.DidNotReceive().BlacklistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReviewMode_ShowsUnreviewedOnlyCheckbox_AndTogglingRebuildsQueue()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        var checkbox = cut.Find("#cleanup-unreviewed");
        Assert.NotNull(checkbox);
        Assert.True(checkbox.HasAttribute("checked"));

        checkbox.Change(false);

        cleanupService.Received(1).DiscardSession();
        _ = cleanupService.Received(1).BuildQueueAsync(CleanupOrder.Random, null, null, CleanupMode.Review, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReviewMode_ClickingNext_WhenStampFails_ShowsErrorAndDoesNotAdvance()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        cleanupService.StampReviewedAsync(1, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("DB error"));
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();
        cut.Find(".cleanup-next-btn").Click();

        Assert.Contains("DB error", cut.Find(".cleanup-action-error").TextContent);
        Assert.Contains("REV-1", cut.Find(".cleanup-code").TextContent);
        cleanupService.DidNotReceive().AdvanceSession(Arg.Any<Movie>(), Arg.Any<CleanupOutcome>());
    }

    [Fact]
    public void ReviewMode_Summary_OmitsStorageFreedAndDeletedSection()
    {
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new MovieCleanupSession { Order = CleanupOrder.Random, Mode = CleanupMode.Review, Queue = [], StartedAt = start, EndedAt = start.AddSeconds(10) };
        session.Decisions.Add(new CleanupDecision(1, "REV-1", "Some Movie", true, null, false, CleanupOutcome.Kept, start));
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        Assert.Empty(cut.FindAll(".cleanup-stat-freed"));
        Assert.Empty(cut.FindAll(".cleanup-summary-deleted"));
        Assert.Contains("REV-1", cut.Find(".cleanup-summary-kept").TextContent);
        Assert.Equal("Reviewed", cut.Find(".cleanup-summary-kept .cleanup-summary-heading").ChildNodes[0].TextContent.Trim());
    }

    [Fact]
    public void ResumedSessionOfTheOtherMode_IsDiscardedAndAFreshQueueIsBuilt()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 2, Code = "REV-2", Status = MovieStatus.Got });
        cleanupService.ResumeSessionAsync(Arg.Any<CancellationToken>()).Returns(new MovieCleanupSession
        {
            Order = CleanupOrder.Random,
            Mode = CleanupMode.Cleanup,
            Queue = Queued([new Movie { Id = 1, Code = "OLD-1", Status = MovieStatus.Got }]),
            StartedAt = DateTimeOffset.UtcNow
        });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        cleanupService.Received().DiscardSession();
        Assert.Contains("REV-2", cut.Find(".cleanup-code").TextContent);
    }

    [Fact]
    public void ModeToggle_LinksToTheOtherModeAndMarksTheActiveOne()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();

        var cleanupLink = cut.Find("a.cleanup-mode-link[href='/movies/cleanup']");
        var reviewLink = cut.Find("a.cleanup-mode-link[href='/movies/review']");
        Assert.DoesNotContain("cleanup-mode-link-active", cleanupLink.ClassName);
        Assert.Contains("cleanup-mode-link-active", reviewLink.ClassName);
    }

    [Fact]
    public void NavigatingFromReviewToCleanup_DiscardsTheSessionAndRebuildsInCleanupMode()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        cleanupService.BuildQueueAsync(CleanupOrder.Random, Arg.Any<string?>(), Arg.Any<int?>(), CleanupMode.Cleanup, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Queued([new Movie { Id = 9, Code = "CLN-9", Status = MovieStatus.Got }]));
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();
        NavigateTo("/movies/cleanup");

        cut.WaitForAssertion(() => Assert.Contains("CLN-9", cut.Find(".cleanup-code").TextContent));
        Assert.NotNull(cut.Find(".cleanup-delete-btn"));
        cleanupService.Received(1).StartSession(Arg.Any<List<int>>(), CleanupOrder.Random, null, null, CleanupMode.Cleanup);
    }

    [Fact]
    public void NavigatingAwayFromTheCleanupPages_DoesNotRebuildTheQueue()
    {
        var cleanupService = ReviewQueueService(new Movie { Id = 1, Code = "REV-1", Status = MovieStatus.Got });
        using var factory = SetUpServices(cleanupService);
        NavigateTo("/movies/review");

        var cut = Render<MovieCleanup>();
        NavigateTo("/actors");

        _ = cleanupService.Received(1).BuildQueueAsync(Arg.Any<CleanupOrder>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<CleanupMode>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
