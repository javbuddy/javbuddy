using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Smoke coverage for MovieDetail.razor post-Phase 2 (MovieService extraction) — renders
/// an existing movie and confirms the toolbar's status-toggle button drives the real MovieService
/// end to end (click → service → DB → reload).</summary>
public class MovieDetailTests : BunitContext
{
    private readonly ILocalLibraryClient localLibraryClient = Substitute.For<ILocalLibraryClient>();
    private readonly IMovieExtraFanartService extraFanartService = Substitute.For<IMovieExtraFanartService>();

    private TestDbContextFactory SetUpServices(
        IMovieCleanupService? cleanupService = null,
        IR18DevDumpStore? r18DevDumpStore = null,
        IImageServingService? imageServing = null)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(imageServing ?? Substitute.For<IImageServingService>());
        Services.AddSingleton<IMovieDetailQueryService>(new MovieDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IMovieSceneService>(new MovieSceneService(factory));
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
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Trickplay.IHighlightTrickplayService>());
        Services.AddSingleton(Substitute.For<IJavinizerClient>());
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        Services.AddSingleton(jellyfinClient);
        Services.AddSingleton(StreamServiceStubs.AllPlayable());
        Services.AddSingleton(Substitute.For<ITrickplayService>());
        Services.AddSingleton(new TrickplayGenerationTracker());
        Services.AddSingleton<IMediaServerClient>(jellyfinClient);
        var dumpStore = r18DevDumpStore ?? Substitute.For<IR18DevDumpStore>();
        Services.AddSingleton(dumpStore);
        Services.AddSingleton<IR18DevReleaseBrowseService>(new R18DevReleaseBrowseService(factory, dumpStore, TimeProvider.System));
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        var nfoSync = Substitute.For<INfoSyncService>();
        nfoSync.CheckMovieNfoConflictAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ActorNfoConflictCheckResult(ci.ArgAt<int>(0), "", false, null));
        Services.AddSingleton(nfoSync);
        Services.AddSingleton(Substitute.For<INfoHistoryService>());

        Services.AddSingleton<IMovieService>(new MovieService(factory, nfoSyncService: nfoSync, localLibraryClient: localLibraryClient));
        Services.AddSingleton<ITagService>(new TagService(factory, nfoSync));
        Services.AddSingleton<IActorService>(new ActorService(factory));
        Services.AddSingleton(Substitute.For<IMovieRescanService>());
        Services.AddSingleton(cleanupService ?? Substitute.For<IMovieCleanupService>());
        Services.AddSingleton<ScheduledTaskChangeNotifier>();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<TaskActivityTracker>();
        Services.AddSingleton<IJellyfinMovieScanService, JellyfinMovieScanService>();
        Services.AddSingleton(new MovieFilterNavigationState());
        Services.AddLogging();

        localLibraryClient.ListExtraFanartFileNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        Services.AddSingleton(localLibraryClient);
        Services.AddSingleton(extraFanartService);

        // MovieDetail.razor always renders a child ProwlarrSearchModal once a movie is loaded.
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        var cropService = Substitute.For<IMovieCoverCropService>();
        cropService.GetCropSourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MovieCropSources("", null, []));
        Services.AddSingleton(cropService);

        Services.AddSingleton(Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.VideoRepair.IVideoRepairJobTracker>());
        var ffmpegResolver = Substitute.For<Javbuddy.Services.Ffmpeg.IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(true);
        Services.AddSingleton(ffmpegResolver);

        JSInterop.Mode = JSRuntimeMode.Loose;
        return factory;
    }

    [Fact]
    public void UnknownCode_ShowsNotFoundMessage()
    {
        using var factory = SetUpServices();

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ZZZ-999"));

        Assert.Contains("Movie not found", cut.Markup);
    }

    [Fact]
    public void ExistingMovie_RendersItsCode()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Contains("ABC-123", cut.Markup);
        Assert.Contains("Mark as Got", cut.Markup);
        Assert.Empty(cut.FindAll(".movie-detail-poster-clickable"));
    }

    private string CreateLocalMovieFolder(string code)
    {
        var root = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, code);
        Directory.CreateDirectory(folder);
        localLibraryClient.ResolveMovieFolderPathAsync(code, Arg.Any<CancellationToken>()).Returns(folder);
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([root]);
        return folder;
    }

    [Fact]
    public void Delete_DeleteFilesToggle_DefaultsOff_AndLeavesFilesOnDisk()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        var folder = CreateLocalMovieFolder("ABC-123");
        File.WriteAllText(Path.Combine(folder, "ABC-123.mp4"), "video");
        try
        {
            var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
            cut.Find(".movie-toolbar-btn-danger").Click();

            Assert.False(cut.Find(".movie-delete-files-toggle input").HasAttribute("checked"));
            Assert.Contains("Video files on disk are left alone.", cut.Find(".delete-confirm-dialog").TextContent);
            Assert.Empty(cut.FindAll(".movie-delete-files-list"));

            cut.Find(".delete-confirm-confirm-btn").Click();

            cut.WaitForAssertion(() => Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri));
            Assert.True(File.Exists(Path.Combine(folder, "ABC-123.mp4")));
            Assert.False(factory.CreateDbContext().Movies.Any());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void Delete_DeleteFilesToggle_ListsTheFilesAndDeletesTheFolder()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        var folder = CreateLocalMovieFolder("ABC-123");
        File.WriteAllText(Path.Combine(folder, "ABC-123.mp4"), new string('x', 2048));
        File.WriteAllText(Path.Combine(folder, "ABC-123.nfo"), new string('x', 1024));
        try
        {
            var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
            cut.Find(".movie-toolbar-btn-danger").Click();
            cut.Find(".movie-delete-files-toggle input").Change(true);

            cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".movie-delete-files-list li").Count));
            var dialog = cut.Find(".delete-confirm-dialog").TextContent;
            Assert.Contains("ABC-123.mp4", dialog);
            Assert.Contains("ABC-123.nfo", dialog);
            Assert.Contains("2 files, 3 KB freed", dialog);
            Assert.Contains("can't be undone", cut.Find(".movie-delete-files-warning").TextContent);
            Assert.Contains("its folder is deleted from disk", dialog);

            cut.Find(".delete-confirm-confirm-btn").Click();

            cut.WaitForAssertion(() => Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri));
            Assert.False(Directory.Exists(folder));
            Assert.False(factory.CreateDbContext().Movies.Any());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void Delete_DeleteFilesFailure_ShowsTheErrorAndKeepsTheMovie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        var folder = CreateLocalMovieFolder("ABC-123");
        // The folder resolves outside every configured root, so the guard refuses to delete it.
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns(["/nonexistent-library-root"]);
        try
        {
            var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
            cut.Find(".movie-toolbar-btn-danger").Click();
            cut.Find(".movie-delete-files-toggle input").Change(true);
            cut.WaitForElement(".movie-delete-files-error");

            cut.Find(".delete-confirm-confirm-btn").Click();

            cut.WaitForAssertion(() => Assert.Contains("Refusing to delete", cut.Find(".delete-confirm-dialog .alert-danger").TextContent));
            Assert.True(Directory.Exists(folder));
            Assert.True(factory.CreateDbContext().Movies.Any());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void BackButton_DefaultsToAllMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var back = cut.Find(".movie-toolbar a.movie-toolbar-btn");
        Assert.Equal("/", back.GetAttribute("href"));
        Assert.Equal("All movies", back.TextContent.Trim());
    }

    [Fact]
    public void BackButton_ReturnsToTheActorPage_WhenOpenedFromActorDetail()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123?actor=Mikami%20Yua");

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var back = cut.Find(".movie-toolbar a.movie-toolbar-btn");
        Assert.Equal("/actors/Mikami%20Yua", back.GetAttribute("href"));
        Assert.Equal("Mikami Yua", back.TextContent.Trim());
    }

    [Fact]
    public void ClickingMainPoster_OpensImageLightbox()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaSourceName = "Local" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var poster = cut.Find(".movie-detail-poster-clickable");
        Assert.Equal("Click to view large poster", poster.GetAttribute("title"));

        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));

        poster.Click();
        Assert.NotEmpty(cut.FindAll(".image-lightbox-backdrop"));

        // Opens jacket image first instead of cover thumbnail
        var img = cut.Find(".image-lightbox-image");
        Assert.Equal("/image-cache/ABC-123/fanart/full", img.GetAttribute("src"));
        Assert.Equal("Full jacket", cut.Find(".image-lightbox-badge").TextContent);

        // Can toggle back to poster
        var buttons = cut.FindAll("[aria-label='Image variant'] button");
        Assert.Equal(2, buttons.Count);
        buttons[1].Click(); // "Poster" button

        img = cut.Find(".image-lightbox-image");
        Assert.StartsWith("/image-cache/ABC-123/poster/full", img.GetAttribute("src") ?? "");
        Assert.Equal("Poster", cut.Find(".image-lightbox-badge").TextContent);

        cut.Find(".image-lightbox-close-btn").Click();
        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));
    }

    // The poster URL carries the poster's own version, so a changed cover gets a new URL instead
    // of the browser's cached old image, while an unchanged one keeps its URL.
    [Fact]
    public async Task MainPoster_UrlFollowsPosterVersion()
    {
        var imageServing = Substitute.For<IImageServingService>();
        imageServing.GetPosterVersionAsync("ABC-123", Arg.Any<CancellationToken>()).Returns("v1");
        using var factory = SetUpServices(imageServing: imageServing);
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaSourceName = "Local" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Equal("/image-cache/ABC-123/poster/full?v=v1", cut.Find(".movie-detail-poster img").GetAttribute("src"));

        imageServing.GetPosterVersionAsync("ABC-123", Arg.Any<CancellationToken>()).Returns("v2");
        await cut.InvokeAsync(() => cut.FindComponent<MovieCoverCropModal>().Instance.OnCoverChanged.InvokeAsync(new MovieCoverCropSaveResult(true)));

        Assert.Equal("/image-cache/ABC-123/poster/full?v=v2", cut.Find(".movie-detail-poster img").GetAttribute("src"));
    }

    [Fact]
    public void InterlacedMovie_RendersHeightWithInterlacedSuffixInDetailBadge()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", MediaWidth = 1920, MediaHeight = 1080, MediaScanType = "Interlaced" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Contains(cut.FindAll(".movie-detail-badge"), badge => badge.TextContent.Trim() == "1080i");
    }

    [Fact]
    public void CastAvatars_LinkOnlyForTrackedActors()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Actor", LastName = "Tracked" };
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaActresses = "Tracked Actor, Untracked Actor",
            };
            db.AddRange(actor, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var linkedAvatar = cut.Find("a.movie-detail-cast-avatar");
        Assert.Equal("/actors/Tracked%20Actor", linkedAvatar.GetAttribute("href"));
        Assert.Equal("TA", linkedAvatar.TextContent.Trim());
        Assert.Single(cut.FindAll(".movie-detail-cast-item-tracked"));
        Assert.Single(cut.FindAll("div.movie-detail-cast-avatar"));
        Assert.DoesNotContain("movie-detail-cast-item-tracked", cut.Find("div.movie-detail-cast-avatar").ParentElement?.ClassName);
        Assert.Equal("UA", cut.Find("div.movie-detail-cast-avatar").TextContent.Trim());
    }

    [Fact]
    public void CastAge_ShowsAgeAtReleaseOnlyForTrackedActorsWithBirthDate()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var withBirthDate = new Actor { FirstName = "Actor", LastName = "Dated", BirthDate = new DateTime(1993, 8, 16) };
            var withoutBirthDate = new Actor { FirstName = "Actor", LastName = "Undated" };
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaReleaseDate = new DateTime(2017, 8, 15),
                MetaActresses = "Dated Actor, Undated Actor, Untracked Actor",
            };
            db.AddRange(withBirthDate, withoutBirthDate, movie);
            db.SaveChanges();
            db.MovieActors.AddRange(
                new MovieActor { MovieId = movie.Id, ActorId = withBirthDate.Id },
                new MovieActor { MovieId = movie.Id, ActorId = withoutBirthDate.Id });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var age = Assert.Single(cut.FindAll(".movie-detail-cast-age"));
        Assert.Equal("Age 23", age.TextContent.Trim());
        Assert.Contains("Dated Actor", age.ParentElement!.TextContent);
    }

    [Fact]
    public void CastAvatars_ShowsWarningBadgeOnlyForUntrackedActors()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Actor", LastName = "Tracked" };
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaActresses = "Tracked Actor, Untracked Actor",
            };
            db.AddRange(actor, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Single(cut.FindAll(".movie-detail-cast-warning-badge"));
        var badge = cut.Find(".movie-detail-cast-warning-badge");
        Assert.Equal("Not matched to a tracked actor", badge.GetAttribute("title"));
        Assert.Contains("Untracked Actor", badge.ParentElement?.NextElementSibling?.TextContent);
    }

    [Fact]
    public void CastAvatars_RendersImageWithVersion_WhenActorImageExists()
    {
        using var factory = SetUpServices();
        var updatedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Actor", LastName = "Tracked" };
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaActresses = "Tracked Actor",
            };
            db.AddRange(actor, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.ActorImages.Add(new ActorImage
            {
                ActorId = actor.Id,
                Variant = "thumb",
                SourceMovieCode = "ABC-123",
                StorageId = Guid.NewGuid(),
                UpdatedAt = updatedAt
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var img = cut.Find("a.movie-detail-cast-avatar img");
        Assert.NotNull(img);
        Assert.Contains($"/actor-image/", img.GetAttribute("src"));
        Assert.Contains($"?v={updatedAt.Ticks}", img.GetAttribute("src"));
    }

    [Fact]
    public void ClickingMarkAsGot_TogglesStatusViaMovieService_AndRerendersWithNewStatus()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        var toggleButton = cut.FindAll("button").Single(b => b.TextContent.Contains("Mark as Got"));
        toggleButton.Click();

        Assert.Contains("Mark as Missing", cut.Markup);

        using var readDb = factory.CreateDbContext();
        var movie = readDb.Movies.Single(m => m.Code == "ABC-123");
        Assert.Equal(MovieStatus.Got, movie.Status);
    }

    [Fact]
    public void ClickingFavorite_TogglesFavoriteViaMovieService()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.FindAll("button.movie-toolbar-btn").Single(b => b.TextContent.Trim() == "Favorite").Click();

        Assert.Contains("Favorited", cut.Find("button.movie-toolbar-btn-favorite.active").TextContent);
        using (var readDb = factory.CreateDbContext())
        {
            Assert.True(readDb.Movies.Single(m => m.Code == "ABC-123").IsFavorite);
        }

        cut.FindAll("button.movie-toolbar-btn").Single(b => b.TextContent.Trim() == "Favorited").Click();

        Assert.Empty(cut.FindAll("button.movie-toolbar-btn-favorite"));
        using var finalDb = factory.CreateDbContext();
        Assert.False(finalDb.Movies.Single(m => m.Code == "ABC-123").IsFavorite);
    }

    [Fact]
    public void GenresAndStudio_RenderAsCleanLinks_AndClickingSetsFilterNavigationState()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var drama = new Tag { Name = "Drama" };
            var bigTits = new Tag { Name = "Big Tits" };
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                MetaStudio = "Attackers",
                MetaGenres = "Drama, Big Tits",
            };
            db.AddRange(drama, bigTits, movie);
            db.SaveChanges();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = drama.Id });
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = bigTits.Id });
            db.SaveChanges();
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var buttons = cut.FindAll("button.movie-detail-badge");
        var studioButton = buttons.Single(b => b.TextContent.Trim() == "Attackers");
        var dramaButton = buttons.Single(b => b.TextContent.Trim() == "Drama");
        var bigTitsButton = buttons.Single(b => b.TextContent.Trim() == "Big Tits");

        Assert.NotNull(studioButton);
        Assert.NotNull(dramaButton);
        Assert.NotNull(bigTitsButton);

        studioButton.Click();
        Assert.Equal("Attackers", filterState.PeekPendingFilter()?.Studio);
        Assert.Null(filterState.PeekPendingFilter()?.Genre);
        Assert.Equal("http://localhost/", nav.Uri);

        dramaButton = cut.FindAll("button.movie-detail-badge").Single(b => b.TextContent.Trim() == "Drama");
        dramaButton.Click();
        Assert.Null(filterState.PeekPendingFilter()?.Studio);
        Assert.Equal("Drama", filterState.PeekPendingFilter()?.Genre);
        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void NeedsReviewTag_ShowsWarningBadge_ButNotForApprovedTag()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var pending = new Tag { Name = "Pending Discovery", NeedsReview = true };
            var approved = new Tag { Name = "Approved", NeedsReview = false };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaGenres = "Pending Discovery, Approved" };
            db.AddRange(pending, approved, movie);
            db.SaveChanges();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = pending.Id });
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = approved.Id });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Single(cut.FindAll(".movie-detail-badge-flagged"));
        var badge = cut.Find(".movie-detail-badge-flagged");
        Assert.Equal("Not yet approved in the tag library", badge.GetAttribute("title"));
        Assert.Contains("Pending Discovery", badge.TextContent);
    }

    [Fact]
    public void EditTags_TogglingRevealsRemoveButtons_AndSearchAddsExistingCatalogTag()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var drama = new Tag { Name = "Drama" };
            var solo = new Tag { Name = "Solo" };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaGenres = "Drama" };
            db.AddRange(drama, solo, movie);
            db.SaveChanges();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = drama.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".movie-detail-tag-remove-btn"));

        cut.Find("button.movie-detail-edit-tags-btn").Click();
        Assert.Single(cut.FindAll(".movie-detail-tag-remove-btn"));

        var searchInput = cut.Find("input.tag-search-input");
        searchInput.Input("Solo");

        var candidate = cut.Find(".tag-search-candidate");
        Assert.Equal("Solo", candidate.TextContent.Trim());
        candidate.Click();

        Assert.Contains(cut.FindAll("button.movie-detail-badge"), b => b.TextContent.Trim() == "Solo");

        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(2, verifyDb.MovieTags.Count(mt => mt.MovieId == movieId));
    }

    [Fact]
    public void EditTags_SearchResults_ExcludeTagsNotYetApproved()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var pending = new Tag { Name = "Solowork", NeedsReview = true };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got };
            db.AddRange(pending, movie);
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.Find("button.movie-detail-edit-tags-btn").Click();

        var searchInput = cut.Find("input.tag-search-input");
        searchInput.Input("Solo");

        Assert.Empty(cut.FindAll(".tag-search-candidate"));
    }

    [Fact]
    public void EditTags_RemoveButtonUnlinksTag()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var drama = new Tag { Name = "Drama" };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaGenres = "Drama" };
            db.AddRange(drama, movie);
            db.SaveChanges();
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = drama.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.Find("button.movie-detail-edit-tags-btn").Click();
        cut.Find(".movie-detail-tag-remove-btn").Click();

        Assert.DoesNotContain(cut.FindAll("button.movie-detail-badge"), b => b.TextContent.Trim() == "Drama");

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.MovieTags.Where(mt => mt.MovieId == movieId));
    }

    [Fact]
    public void EditCast_TogglingRevealsRemoveButtons_AndSearchAddsTrackedActor()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var mikami = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var hatano = new Actor { FirstName = "Yui", LastName = "Hatano" };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaActresses = "Mikami Yua" };
            db.AddRange(mikami, hatano, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = mikami.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".movie-detail-cast-remove-btn"));

        cut.Find("button.movie-detail-edit-cast-btn").Click();
        Assert.Single(cut.FindAll(".movie-detail-cast-remove-btn"));

        var searchInput = cut.Find("input.cast-search-input");
        searchInput.Input("Hatano");

        var candidate = cut.Find(".cast-search-candidate");
        Assert.Single(candidate.QuerySelectorAll(".cast-search-avatar"));
        Assert.Equal("Hatano Yui", candidate.QuerySelector("span:not(.cast-search-avatar)")!.TextContent.Trim());
        candidate.Click();

        Assert.Contains(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Hatano Yui");

        using var verifyDb = factory.CreateDbContext();
        Assert.Equal(2, verifyDb.MovieActors.Count(ma => ma.MovieId == movieId));
    }

    [Fact]
    public void EditCast_SearchResults_ExcludeActorsAlreadyOnTheMovie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var mikami = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaActresses = "Mikami Yua" };
            db.AddRange(mikami, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = mikami.Id });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.Find("button.movie-detail-edit-cast-btn").Click();

        var searchInput = cut.Find("input.cast-search-input");
        searchInput.Input("Mikami");

        Assert.Empty(cut.FindAll(".cast-search-candidate"));
    }

    [Fact]
    public void EditCast_UnmatchedCastEntry_HasRemoveButton_AndRemovesActor()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaActresses = "Unknown Person" };
            db.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.Empty(cut.FindAll(".movie-detail-cast-remove-btn"));
        Assert.Single(cut.FindAll(".movie-detail-cast-warning-badge"));

        cut.Find("button.movie-detail-edit-cast-btn").Click();

        Assert.Contains(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Unknown Person");
        var removeBtn = Assert.Single(cut.FindAll(".movie-detail-cast-remove-btn"));
        Assert.Equal("Remove Unknown Person", removeBtn.GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".movie-detail-cast-warning-badge"));

        removeBtn.Click();

        Assert.DoesNotContain(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Unknown Person");

        using var verifyDb = factory.CreateDbContext();
        var reloaded = verifyDb.Movies.Find(movieId);
        Assert.Null(reloaded!.MetaActresses);
        Assert.False(reloaded.HasUnmatchedActors);
    }

    [Fact]
    public void EditCast_RemoveButtonUnlinksActor()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var mikami = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaActresses = "Mikami Yua" };
            db.AddRange(mikami, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = mikami.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.Find("button.movie-detail-edit-cast-btn").Click();
        cut.Find(".movie-detail-cast-remove-btn").Click();

        Assert.DoesNotContain(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Mikami Yua");

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(verifyDb.MovieActors.Where(ma => ma.MovieId == movieId));
    }

    [Fact]
    public void CodePrefixBadge_ClickingFiltersMoviesByTheFullPrefix()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "FC2-PPV-123456", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "FC2-PPV-123456"));

        var prefixButton = cut.Find("button.movie-detail-code-prefix");
        Assert.Equal("FC2-PPV", prefixButton.TextContent.Trim());
        Assert.Contains("-123456", prefixButton.ParentElement?.TextContent);

        prefixButton.Click();

        Assert.Equal("FC2-PPV", filterState.PeekPendingFilter()?.CodePrefix);
        Assert.Null(filterState.PeekPendingFilter()?.Studio);
        Assert.Null(filterState.PeekPendingFilter()?.Genre);
        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public async Task NfoConflict_RendersWarningAffordances_AndReviewButtonDoesNotImmediatelySync()
    {
        using var factory = SetUpServices();
        var nfoSync = Services.GetRequiredService<INfoSyncService>();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                NfoDriftKind = NfoDriftKind.ExternalEdit,
                NfoConflictDetails = ".nfo has 'Mikami Yua', canonical is 'Yua Mikami'"
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        // Conflict detail text is shown only as a tooltip; it must NOT appear as visible rendered text.
        Assert.DoesNotContain("NFO Metadata Conflict:", cut.Markup);

        // The toolbar no longer repeats the badge with its own Resolve .nfo Conflict button.
        Assert.DoesNotContain(cut.FindAll(".movie-toolbar button"), b => b.TextContent.Contains("Resolve .nfo Conflict"));

        // The badge names the drift direction and opens the review.
        var conflictBadge = cut.FindAll("button.movie-detail-badge-warning").SingleOrDefault();
        Assert.NotNull(conflictBadge);
        Assert.Equal("NFO: External edit", conflictBadge.TextContent.Trim());
        Assert.Equal("NFO drift: External edit — .nfo has 'Mikami Yua', canonical is 'Yua Mikami'", conflictBadge.GetAttribute("title"));

        await cut.InvokeAsync(() => conflictBadge.Click());

        await nfoSync.DidNotReceive().SyncMovieNfoAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CastList_MatchesActorByAlias_AndRendersLink()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Momona Kito" });
            var movie = new Movie
            {
                Code = "ABC-456",
                Status = MovieStatus.Got,
                MetaActresses = "Momona Kito"
            };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            db.MovieActors.Add(new MovieActor { Actor = actor, Movie = movie });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-456"));

        var castLink = cut.Find("a.movie-detail-cast-name");
        Assert.NotNull(castLink);
        Assert.Contains("Momona%20Kito", castLink.GetAttribute("href"));
        Assert.Equal("Momona Kito", castLink.TextContent);
        Assert.Contains("movie-detail-cast-item-tracked", cut.Find(".movie-detail-cast-item").ClassName);
    }

    [Fact]
    public void ExistingMovie_RendersRescanButton()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Contains("Rescan", ToolbarMenuItems(cut, "Refresh"));
    }

    [Fact]
    public async Task ClickingRescan_InvokesMovieRescanService()
    {
        using var factory = SetUpServices();
        var rescanService = Services.GetRequiredService<IMovieRescanService>();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        await ClickToolbarMenuItemAsync(cut, "Refresh", "Rescan");

        await rescanService.Received(1).RescanAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ExistingMovie_RendersRefreshWithJavinizerButton()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Contains("Refresh with Javinizer", ToolbarMenuItems(cut, "Refresh"));
    }

    [Fact]
    public async Task ClickingRefreshWithJavinizer_OpensTheFieldPickerModal()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".metadata-refresh-backdrop"));

        await ClickToolbarMenuItemAsync(cut, "Refresh", "Refresh with Javinizer");

        var panel = cut.Find(".metadata-refresh-backdrop");
        Assert.Contains("ABC-123", panel.TextContent);
    }

    [Fact]
    public void MissingMovie_DoesNotDisplayEditNfoOrMediaInfoButtons()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Missing,
                NfoDriftKind = NfoDriftKind.ExternalEdit,
                NfoConflictDetails = ".nfo conflict"
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.DoesNotContain("Edit .nfo", ToolbarMenuItems(cut, "Tools"));
        Assert.DoesNotContain("Repair Video", ToolbarMenuItems(cut, "Tools"));
        Assert.DoesNotContain(cut.FindAll("button.movie-toolbar-btn"), b => b.TextContent.Contains("Media Info"));
        Assert.Empty(cut.FindAll("button.movie-detail-badge-warning"));
    }

    [Fact]
    public void GotMovie_DisplaysEditNfoAndMediaInfoButtons()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Equal(["Crop Cover", "Edit .nfo", "Repair Video"], ToolbarMenuItems(cut, "Tools"));
        Assert.Contains(cut.FindAll("button.movie-toolbar-btn"), b => b.TextContent.Contains("Media Info"));
    }

    [Fact]
    public void MarkingMissingMovieAsGot_RevealsEditNfoAndMediaInfoButtons()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.DoesNotContain("Edit .nfo", ToolbarMenuItems(cut, "Tools"));
        Assert.DoesNotContain(cut.FindAll("button.movie-toolbar-btn"), b => b.TextContent.Contains("Media Info"));

        var toggleButton = cut.FindAll("button.movie-toolbar-btn").Single(b => b.TextContent.Contains("Mark as Got"));
        toggleButton.Click();

        Assert.Contains("Edit .nfo", ToolbarMenuItems(cut, "Tools"));
        Assert.Contains(cut.FindAll("button.movie-toolbar-btn"), b => b.TextContent.Contains("Media Info"));
    }

    [Fact]
    public async Task ExistingMovie_RendersCropCoverButton_AndOpensModal()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".crop-modal-panel"));

        await ClickToolbarMenuItemAsync(cut, "Tools", "Crop Cover");

        Assert.NotEmpty(cut.FindAll(".crop-modal-panel"));
    }

    [Fact]
    public void BlacklistedMovie_ShowsIndicator_AndRemovingItCallsUnblacklistAsync()
    {
        var cleanupService = Substitute.For<IMovieCleanupService>();
        cleanupService.UnblacklistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        using var factory = SetUpServices(cleanupService);
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, CleanupBlacklisted = true };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.Contains("Blacklisted from Cleanup", cut.Markup);

        cut.Find("button.movie-detail-badge-warning").Click();

        _ = cleanupService.Received(1).UnblacklistAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NonBlacklistedMovie_ShowsNoIndicator()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, CleanupBlacklisted = false });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.DoesNotContain("Blacklisted from Cleanup", cut.Markup);
    }

    [Fact]
    public void PreviewMovie_RendersMovieGalleryWithThumbAndFullUrls()
    {
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetMovieByCodeAsync("MGMJ-077", Arg.Any<CancellationToken>())
            .Returns(new R18DevMovieDetail(
                "MGMJ-077",
                "Preview Title",
                null,
                "Preview Description",
                null,
                120,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                null,
                ["https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg"],
                ["https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-1.jpg"]));

        using var factory = SetUpServices(r18DevDumpStore: dumpStore);
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "MGMJ-077"));

        var gallery = cut.FindComponent<MovieGallery>();
        Assert.Equal(["https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077-1.jpg"], gallery.Instance.ThumbUrls);
        Assert.Equal(["https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg"], gallery.Instance.FullUrls);
        Assert.Equal(["https://pics.dmm.co.jp/digital/video/mgmj00077/mgmj00077jp-1.jpg"], gallery.Instance.OriginalUrls);
    }

    [Fact]
    public void LinkedToJellyfin_RendersPlayBadgeButtonBeforeOpenInJellyfin()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                JellyfinItemId = "jf-item-42",
                LocalFileSizeBytes = 1_000_000,
                JellyfinServerId = "jf-srv-1",
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var playBtn = Assert.Single(cut.FindAll("button.movie-detail-badge-play"));
        Assert.Contains("Play", playBtn.TextContent);

        var jellyfinBadge = Assert.Single(cut.FindAll("a.movie-detail-badge-jellyfin"));
        Assert.NotNull(jellyfinBadge);

        // Verify no play button in toolbar or on poster
        Assert.Empty(cut.FindAll(".movie-toolbar-btn-play"));
        Assert.Empty(cut.FindAll(".movie-detail-poster-play-btn"));

        var playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.False(playerModal.Instance.Show);
        Assert.NotNull(playerModal.Instance.MovieId);
    }

    /// <summary>Opens the toolbar menu labelled <paramref name="menu"/>, returns its item labels and closes it again.</summary>
    private static List<string> ToolbarMenuItems(IRenderedComponent<MovieDetail> cut, string menu)
    {
        ToolbarMenuTrigger(cut, menu).Click();
        var items = cut.FindAll(".toolbar-menu-item").Select(b => b.TextContent.Trim()).ToList();
        ToolbarMenuTrigger(cut, menu).Click();
        return items;
    }

    private static async Task ClickToolbarMenuItemAsync(IRenderedComponent<MovieDetail> cut, string menu, string item)
    {
        ToolbarMenuTrigger(cut, menu).Click();
        var button = cut.FindAll(".toolbar-menu-item").Single(b => b.TextContent.Trim() == item);
        await cut.InvokeAsync(() => button.Click());
    }

    private static AngleSharp.Dom.IElement ToolbarMenuTrigger(IRenderedComponent<MovieDetail> cut, string menu) =>
        cut.FindAll(".movie-toolbar button.toolbar-menu-btn").Single(b => b.TextContent.Trim() == menu);

    private static void AddMovieWithTwoVersions(TestDbContextFactory factory)
    {
        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, LocalFileSizeBytes = 3_000_000, MediaVideoFileName = "ABC-123.mp4" };
        movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123.mp4", VersionTag = "Original", IsPrimary = true, FileSizeBytes = 1_000_000 });
        movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123-RIFE.mkv", VersionTag = "RIFE", FrameRate = 60, FileSizeBytes = 2_000_000 });
        db.Movies.Add(movie);
        db.SaveChanges();
    }

    [Fact]
    public void SingleVersion_HasNoVersionPicker()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, LocalFileSizeBytes = 1_000_000 };
            movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123.mp4", IsPrimary = true });
            db.Movies.Add(movie);
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Single(cut.FindAll("button.movie-detail-badge-play"));
        Assert.Empty(cut.FindAll(".movie-detail-play-toggle"));
    }

    [Fact]
    public void SeveralVersions_PlayIsASingleButton_ThatPlaysThePrimary()
    {
        using var factory = SetUpServices();
        AddMovieWithTwoVersions(factory);
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".movie-detail-play-toggle"));
        cut.Find("button.movie-detail-badge-play").Click();

        var playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.True(playerModal.Instance.Show);
        Assert.Null(playerModal.Instance.FileId);
    }

    [Fact]
    public void TheVersionsBadge_PicksThePrimaryVersion()
    {
        using var factory = SetUpServices();
        AddMovieWithTwoVersions(factory);
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        cut.Find("button.movie-detail-badge-versions").Click();
        var choices = cut.FindAll(".movie-detail-version-choice");
        Assert.Equal(["true", "false"], choices.Select(c => c.GetAttribute("aria-checked")));
        choices[1].Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".movie-detail-version-menu")));
        cut.Find("button.movie-detail-badge-versions").Click();
        choices = cut.FindAll(".movie-detail-version-choice");
        Assert.StartsWith("RIFE", choices[0].TextContent.Trim().TrimStart('✓').Trim());
        Assert.Equal("true", choices[0].GetAttribute("aria-checked"));
        using var db = factory.CreateDbContext();
        var rife = db.MovieFiles.Single(f => f.FileName == "ABC-123-RIFE.mkv");
        Assert.True(rife.IsPrimary);
        Assert.True(rife.IsPrimaryPinned);
    }

    [Fact]
    public void LocalFileWithoutJellyfin_StillRendersThePlayBadge()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, LocalFileSizeBytes = 1_000_000 });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Single(cut.FindAll("button.movie-detail-badge-play"));
        Assert.Empty(cut.FindAll("a.movie-detail-badge-jellyfin"));
    }

    [Fact]
    public void LinkedToJellyfinWithoutALocalFile_HasNoPlayBadge()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", JellyfinServerId = "jf-srv-1" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll("button.movie-detail-badge-play"));
        Assert.Single(cut.FindAll("a.movie-detail-badge-jellyfin"));
    }

    [Fact]
    public async Task Scenes_SpanTheFullWidth_AndACardPlaysInTheClipPlayer_WhoseEditOpensTheFullPlayer()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                JellyfinItemId = "jf-item-42",
                LocalFileSizeBytes = 1_000_000,
                MediaDurationSeconds = 3600,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }
        var sceneService = new MovieSceneService(factory);
        await sceneService.AddSceneAsync(movieId, 0, 750, "Interview");
        await sceneService.AddSceneAsync(movieId, 750, null, null);

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        // Below the poster and details, not in the details column.
        Assert.Empty(cut.FindAll(".movie-detail-main .movie-scenes"));
        Assert.Equal("Scenes (2)", cut.Find(".movie-detail-wide .movie-scenes h2").TextContent);
        var cards = cut.FindAll(".movie-scenes-grid .movie-scenes-card");
        Assert.Equal(2, cards.Count);
        Assert.Contains("Interview", cards[0].TextContent);
        Assert.Contains("12:30–1:00:00", cards[1].TextContent);

        cards[1].QuerySelector(".movie-scenes-play")!.Click();

        var clip = cut.FindComponent<ClipPlayerModal>().Instance;
        Assert.Equal((PlayerClipKind.Scene, 750d, (double?)3600d), (clip.Clip!.Kind, clip.Clip.StartSeconds, clip.Clip.EndSeconds));
        Assert.Equal((2, 2), (clip.Position, clip.Count));
        Assert.False(cut.FindComponent<VideoPlayerModal>().Instance.Show);

        cut.Find("button.clip-player-edit-btn").Click();

        Assert.Null(cut.FindComponent<ClipPlayerModal>().Instance.Clip);
        var playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.True(playerModal.Instance.Show);
        Assert.Equal(750d, playerModal.Instance.StartAtSeconds);
        Assert.Equal(movieId, playerModal.Instance.MovieId);
        Assert.Null(playerModal.Instance.EndAtSeconds);
        Assert.True(playerModal.Instance.ShowEditor);
    }

    [Fact]
    public async Task Scenes_ShowHowManyHighlightsStartInThem_AndFollowPlayerEdits()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000, MediaDurationSeconds = 3600 };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }
        var sceneService = new MovieSceneService(factory);
        await sceneService.AddSceneAsync(movieId, 0, 750, "Interview");
        await sceneService.AddSceneAsync(movieId, 750, null, null);
        var highlightService = new MovieHighlightService(factory);
        await highlightService.AddHighlightAsync(movieId, 100, 200, null);
        // Crosses into the second scene but starts in the first.
        await highlightService.AddHighlightAsync(movieId, 700, 800, null);

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var cards = cut.FindAll(".movie-scenes-grid .movie-scenes-card");
        var badge = cards[0].QuerySelector(".movie-scenes-highlight-count")!;
        Assert.Equal("2", badge.TextContent.Trim());
        Assert.Equal("2 highlights", badge.GetAttribute("title"));
        Assert.Null(cards[1].QuerySelector(".movie-scenes-highlight-count"));

        // A highlight added in the player's editor shows up without a reload.
        await highlightService.AddHighlightAsync(movieId, 900, 950, null);
        var modal = cut.FindComponent<VideoPlayerModal>();
        await cut.InvokeAsync(async () => await modal.Instance.OnHighlightsChanged.InvokeAsync(await highlightService.GetHighlightsAsync(movieId)));

        Assert.Equal("1 highlight", cut.FindAll(".movie-scenes-grid .movie-scenes-card")[1].QuerySelector(".movie-scenes-highlight-count")!.GetAttribute("title"));
        Assert.Equal(3, cut.FindAll(".movie-scenes-shelf .movie-scenes-card").Count);
    }

    [Fact]
    public async Task Scenes_ANewSceneTag_ShowsUpInTheMovieTags()
    {
        using var factory = SetUpServices();
        int movieId;
        int tagId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 };
            var tag = new Tag { Name = "Bath" };
            db.AddRange(movie, tag);
            db.SaveChanges();
            movieId = movie.Id;
            tagId = tag.Id;
        }
        var sceneService = new MovieSceneService(factory, clipTags: new ClipTagSyncService(factory, Substitute.For<INfoSyncService>()));
        var sceneId = (await sceneService.AddSceneAsync(movieId, 0, null, null)).SceneId!.Value;

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.DoesNotContain(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));

        // What the player modal's scene editor reports after the tag is added to the scene.
        await sceneService.AddSceneTagAsync(sceneId, tagId);
        var scenes = await sceneService.GetScenesAsync(movieId);
        await cut.InvokeAsync(() => cut.FindComponent<VideoPlayerModal>().Instance.OnScenesChanged.InvokeAsync(scenes));

        Assert.Contains(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));
    }

    [Fact]
    public async Task Scenes_RemovingTheOnlySceneTag_DropsTheRolledUpMovieTag()
    {
        using var factory = SetUpServices();
        int movieId;
        int tagId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 };
            var tag = new Tag { Name = "Bath" };
            db.AddRange(movie, tag);
            db.SaveChanges();
            movieId = movie.Id;
            tagId = tag.Id;
        }
        var sceneService = new MovieSceneService(factory, clipTags: new ClipTagSyncService(factory, Substitute.For<INfoSyncService>()));
        var sceneId = (await sceneService.AddSceneAsync(movieId, 0, null, null)).SceneId!.Value;
        await sceneService.AddSceneTagAsync(sceneId, tagId);

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.Contains(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));

        // Clip-only movie tags follow the clips: removing the scene tag removes it.
        await sceneService.RemoveSceneTagAsync(sceneId, tagId);
        var scenes = await sceneService.GetScenesAsync(movieId);
        await cut.InvokeAsync(() => cut.FindComponent<VideoPlayerModal>().Instance.OnScenesChanged.InvokeAsync(scenes));

        Assert.DoesNotContain(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));
    }

    [Fact]
    public async Task ClosingThePlayer_ShowsTagsAddedInItsMovieTagsEditor()
    {
        using var factory = SetUpServices();
        int movieId;
        int tagId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 };
            var tag = new Tag { Name = "Bath" };
            db.AddRange(movie, tag);
            db.SaveChanges();
            movieId = movie.Id;
            tagId = tag.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.DoesNotContain(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));

        // The editor adds the tag straight to the movie; the page only learns of it when the player closes.
        await new TagService(factory, Substitute.For<INfoSyncService>()).AddTagToMovieAsync(movieId, tagId);
        await cut.InvokeAsync(() => cut.FindComponent<VideoPlayerModal>().Instance.OnClose.InvokeAsync());

        Assert.Contains(cut.FindAll(".movie-detail-badge"), b => b.TextContent.Contains("Bath"));
    }

    [Fact]
    public async Task ClosingThePlayer_ShowsCastRemovedInItsMovieActorsEditor()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000, MetaActresses = "Unknown Person" };
            db.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        Assert.Contains(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Unknown Person");

        // The editor changes the cast straight on the movie; the page only learns of it when the player closes.
        await new MovieService(factory).RemoveActorFromMovieAsync(movieId, "Unknown Person");
        await cut.InvokeAsync(() => cut.FindComponent<VideoPlayerModal>().Instance.OnClose.InvokeAsync());

        Assert.DoesNotContain(cut.FindAll(".movie-detail-cast-name"), n => n.TextContent.Trim() == "Unknown Person");
    }

    [Theory]
    [InlineData("?scene=SCENE", 750d)]
    [InlineData("?t=95.5", 95.5)]
    public async Task DeepLink_OpensThePlayerAtTheSceneOrTime(string query, double expectedStart)
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000, MediaDurationSeconds = 3600 };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }
        var sceneService = new MovieSceneService(factory);
        await sceneService.AddSceneAsync(movieId, 0, null, null);
        var sceneId = (await sceneService.AddSceneAsync(movieId, 750, null, null)).SceneId!.Value;
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/ABC-123" + query.Replace("SCENE", sceneId.ToString()));

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<VideoPlayerModal>().Instance.Show));
        Assert.Equal(expectedStart, cut.FindComponent<VideoPlayerModal>().Instance.StartAtSeconds);
        // The deep links are the Scenes wall's Edit links.
        Assert.True(cut.FindComponent<VideoPlayerModal>().Instance.ShowEditor);
    }

    [Fact]
    public async Task HighlightDeepLink_OpensThePlayerForJustTheClip()
    {
        using var factory = SetUpServices();
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000, MediaDurationSeconds = 3600 };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }
        var highlightId = (await new MovieHighlightService(factory).AddHighlightAsync(movieId, 700, 760, null)).HighlightId!.Value;
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/movies/ABC-123?highlight={highlightId}");

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<VideoPlayerModal>().Instance.Show));
        var modal = cut.FindComponent<VideoPlayerModal>().Instance;
        Assert.Equal((700d, (double?)760d), (modal.StartAtSeconds, modal.EndAtSeconds));
        Assert.True(modal.ShowEditor);
    }

    [Fact]
    public void HighlightDeepLink_UnknownId_DoesNotOpenThePlayer()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 });
            db.SaveChanges();
        }
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/ABC-123?highlight=999999");

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.False(cut.FindComponent<VideoPlayerModal>().Instance.Show);
    }

    [Fact]
    public void DeepLink_UnknownScene_DoesNotOpenThePlayer()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 });
            db.SaveChanges();
        }
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/movies/ABC-123?scene=999");

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.False(cut.FindComponent<VideoPlayerModal>().Instance.Show);
    }

    [Fact]
    public void Scenes_EditScenesOpensThePlayerFromTheStart()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, JellyfinItemId = "jf-item-42", LocalFileSizeBytes = 1_000_000 });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Equal("No scenes yet.", cut.Find(".movie-scenes-empty").TextContent);
        cut.Find(".movie-scenes-edit-btn").Click();

        var playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.True(playerModal.Instance.Show);
        Assert.Null(playerModal.Instance.StartAtSeconds);
        Assert.True(playerModal.Instance.ShowEditor);
    }

    [Fact]
    public void Scenes_SectionHidden_WhenNotPlayableAndNoScenes()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".movie-scenes"));
    }

    [Fact]
    public void NotLinkedToJellyfin_DoesNotRenderPlayBadgeButton()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                JellyfinItemId = null,
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".movie-detail-badge-play"));
        Assert.Empty(cut.FindAll(".movie-detail-badge-jellyfin"));
        Assert.Empty(cut.FindAll(".movie-toolbar-btn-play"));
        Assert.Empty(cut.FindAll(".movie-detail-poster-play-btn"));
    }

    [Fact]
    public void ClickingPlayBadgeButton_OpensVideoPlayerModal()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                JellyfinItemId = "jf-item-42",
                LocalFileSizeBytes = 1_000_000,
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.False(playerModal.Instance.Show);

        var playBtn = cut.Find("button.movie-detail-badge-play");
        playBtn.Click();

        playerModal = cut.FindComponent<VideoPlayerModal>();
        Assert.True(playerModal.Instance.Show);
        // Play is just the player; the editor is behind Edit scenes.
        Assert.False(playerModal.Instance.ShowEditor);
    }

    [Fact]
    public void ClickingCover_OpensPosterLightbox()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie
            {
                Code = "ABC-123",
                MetaSourceName = "javinizer",
                Status = MovieStatus.Got,
                JellyfinItemId = "jf-item-42",
                LocalFileSizeBytes = 1_000_000,
            });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        var lightbox = cut.FindComponent<ImageLightboxModal>();
        Assert.False(lightbox.Instance.Show);

        var cover = cut.Find(".movie-detail-poster-clickable");
        cover.Click();

        lightbox = cut.FindComponent<ImageLightboxModal>();
        Assert.True(lightbox.Instance.Show);
    }

    private IRenderedComponent<MovieDetail> RenderWithExtraFanart(List<string> files, bool hasLocalFolder)
    {
        var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "IPX-535", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        localLibraryClient.ListExtraFanartFileNamesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(_ => files.ToList());
        localLibraryClient.ResolveMovieFolderPathAsync("IPX-535", Arg.Any<CancellationToken>())
            .Returns(hasLocalFolder ? "/library/IPX-535" : null);

        return Render<MovieDetail>(p => p.Add(x => x.RouteCode, "IPX-535"));
    }

    [Fact]
    public void ExtraFanartUrls_CarryTheFileNameAsVersion()
    {
        var cut = RenderWithExtraFanart(["fanart1.jpg", "fanart3.jpg"], hasLocalFolder: true);

        var srcs = cut.FindAll("img.fanart-thumb").Select(i => i.GetAttribute("src")).ToList();
        Assert.Equal(
            ["/image-cache/IPX-535/extrafanart/0/thumb?v=fanart1.jpg", "/image-cache/IPX-535/extrafanart/1/thumb?v=fanart3.jpg"],
            srcs);
    }

    [Fact]
    public void LocalFolderWithoutImages_ShowsTheAddImagesPlaceholder()
    {
        var cut = RenderWithExtraFanart([], hasLocalFolder: true);

        cut.Find(".movie-images-empty").Click();

        Assert.NotEmpty(cut.FindAll(".movie-image-upload-backdrop"));
    }

    [Fact]
    public void NoLocalFolderAndNoImages_HidesTheImagesSection()
    {
        var cut = RenderWithExtraFanart([], hasLocalFolder: false);

        Assert.Empty(cut.FindAll(".movie-images-empty"));
        Assert.DoesNotContain("Add Images", cut.Markup);
    }

    [Fact]
    public void ImagesWithoutLocalFolder_HaveNoAddOrDeleteControls()
    {
        var cut = RenderWithExtraFanart(["fanart1.jpg"], hasLocalFolder: false);

        Assert.Single(cut.FindAll("img.fanart-thumb"));
        Assert.Empty(cut.FindAll(".movie-image-delete-btn"));
        Assert.DoesNotContain("Add Images", cut.Markup);
    }

    [Fact]
    public async Task DeletingFromAThumbnail_ConfirmsThenDeletesAndReloadsTheGallery()
    {
        var files = new List<string> { "fanart1.jpg", "fanart2.jpg" };
        extraFanartService.DeleteAsync(Arg.Any<int>(), "fanart2.jpg", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                files.Remove("fanart2.jpg");
                return ExtraFanartResult.Ok();
            });
        var cut = RenderWithExtraFanart(files, hasLocalFolder: true);

        await cut.FindAll(".movie-image-delete-btn")[1].ClickAsync(new());
        Assert.Contains("Delete \"fanart2.jpg\"?", cut.Find(".delete-confirm-dialog").TextContent);
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync(new());

        await extraFanartService.Received(1).DeleteAsync(Arg.Any<int>(), "fanart2.jpg", Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("img.fanart-thumb")));
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));
    }

    [Fact]
    public async Task ReusedFileNameAfterAChange_GetsANewUrl()
    {
        // Deleting the last fanartN frees its name for the next upload; the browser must not
        // reuse the deleted image it cached under the old URL.
        var files = new List<string> { "fanart1.jpg", "fanart2.jpg" };
        extraFanartService.DeleteAsync(Arg.Any<int>(), "fanart2.jpg", Arg.Any<CancellationToken>())
            .Returns(_ => ExtraFanartResult.Ok()); // listing still shows a (new) fanart2.jpg afterwards
        var cut = RenderWithExtraFanart(files, hasLocalFolder: true);
        var before = cut.FindAll("img.fanart-thumb")[1].GetAttribute("src");

        await cut.FindAll(".movie-image-delete-btn")[1].ClickAsync(new());
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotEqual(before, cut.FindAll("img.fanart-thumb")[1].GetAttribute("src")));
    }

    [Fact]
    public async Task DeleteFailure_ShowsTheErrorInTheImagesSection()
    {
        extraFanartService.DeleteAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Fail("Could not delete the image: access denied"));
        var cut = RenderWithExtraFanart(["fanart1.jpg"], hasLocalFolder: true);

        await cut.Find(".movie-image-delete-btn").ClickAsync(new());
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("access denied", cut.Find(".movie-detail-section .alert-danger").TextContent));
    }

    [Fact]
    public async Task DeleteFromTheViewer_CoversTheViewerWhileConfirming()
    {
        var cut = RenderWithExtraFanart(["fanart1.jpg", "fanart2.jpg"], hasLocalFolder: true);
        cut.FindAll("img.fanart-thumb")[1].Click();

        await cut.Find(".gallery-topbar-actions .movie-image-viewer-delete-btn").ClickAsync(new());
        Assert.True(cut.FindComponent<GalleryLightbox>().Instance.Covered);

        await cut.Find(".delete-confirm-cancel-btn").ClickAsync(new());
        Assert.False(cut.FindComponent<GalleryLightbox>().Instance.Covered);
    }

    [Fact]
    public async Task DeleteFailureFromTheViewer_ShowsTheErrorInsideTheViewer()
    {
        extraFanartService.DeleteAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Fail("Could not delete the image: access denied"));
        var cut = RenderWithExtraFanart(["fanart1.jpg", "fanart2.jpg"], hasLocalFolder: true);
        cut.FindAll("img.fanart-thumb")[1].Click();

        await cut.Find(".gallery-topbar-actions .movie-image-viewer-delete-btn").ClickAsync(new());
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("access denied", cut.Find(".gallery-lightbox .movie-image-viewer-error").TextContent));
    }

    private IR18DevDumpStore CatalogStore(string code, R18DevCatalogRef? series, R18DevCatalogRef? label)
    {
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetCatalogAvailabilityAsync(default).ReturnsForAnyArgs(R18DevCatalogAvailability.Ready);
        dumpStore.GetMovieByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(new R18DevMovieDetail(
            code, "Dump title", null, null, null, null, null, null, "Studio", label?.Name, series?.Name, null,
            [], [], null, [], null, series, label));
        dumpStore.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[]
        {
            new R18DevFilmographyEntry("REL-002", "Related two", null, new DateOnly(2021, 1, 1), null),
            new R18DevFilmographyEntry("REL-001", "Related one", null, new DateOnly(2020, 1, 1), null),
        });
        dumpStore.CountCatalogAsync(default!, default, default).ReturnsForAnyArgs(30);
        return dumpStore;
    }

    [Fact]
    public void TrackedMovie_InAnR18DevSeries_HasNoShelfButAClickableSeriesBadge()
    {
        var series = new R18DevCatalogRef(20, "Series A");
        var label = new R18DevCatalogRef(10, "Label B");
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("ABC-123", series, label));
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaStudio = "Studio", MetaLabel = "Label B" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button.movie-detail-badge"), b => b.TextContent == "Series A"));
        Assert.Empty(cut.FindAll(".related-releases"));
        Assert.DoesNotContain("More from this series", cut.Markup);

        cut.FindAll("button.movie-detail-badge").Single(b => b.TextContent == "Series A").Click();

        var pending = Services.GetRequiredService<MovieFilterNavigationState>().PeekPendingFilter();
        Assert.Equal(series, pending?.CatalogSeries);
        Assert.Null(pending?.CatalogLabel);
        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void TrackedMovie_LabelBadge_BrowsesTheLabel()
    {
        var series = new R18DevCatalogRef(20, "Series A");
        var label = new R18DevCatalogRef(10, "Label B");
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("ABC-123", series, label));
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaStudio = "Studio", MetaLabel = "Label B" });
            db.SaveChanges();
        }
        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button.movie-detail-badge"), b => b.TextContent == "Label B"));

        cut.FindAll("button.movie-detail-badge").Single(b => b.TextContent == "Label B").Click();
        Assert.Equal(label, filterState.PeekPendingFilter()?.CatalogLabel);
    }

    [Fact]
    public void TrackedMovie_R18DevDisabled_HasNoShelfAndAPlainLabel()
    {
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("ABC-123", new R18DevCatalogRef(20, "Series A"), new R18DevCatalogRef(10, "Label B")));
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaStudio = "Studio", MetaLabel = "Label B" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        Assert.Empty(cut.FindAll(".related-releases"));
        Assert.DoesNotContain(cut.FindAll("button.movie-detail-badge"), b => b.TextContent == "Label B");
        Assert.Contains(cut.FindAll("span.movie-detail-badge"), b => b.TextContent == "Label B");
    }

    [Fact]
    public void PreviewMovie_ShowsTheSeriesShelf_AndSeeAllBrowsesTheSeries()
    {
        var series = new R18DevCatalogRef(20, "Series A");
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("XYZ-001", series, null));
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "XYZ-001"));

        cut.WaitForAssertion(() => Assert.Contains("More from this series: Series A", cut.Markup));
        Assert.Equal(2, cut.FindAll(".related-releases a.poster-card").Count);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "See all 30").Click();

        Assert.Equal(series, Services.GetRequiredService<MovieFilterNavigationState>().PeekPendingFilter()?.CatalogSeries);
    }

    [Fact]
    public void PreviewMovie_SeriesBadge_BrowsesTheSeries()
    {
        var series = new R18DevCatalogRef(20, "Series A");
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("XYZ-001", series, null));
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "XYZ-001"));
        cut.WaitForAssertion(() => Assert.Contains("More from this series: Series A", cut.Markup));

        cut.FindAll("button.movie-hero-badge").Single(b => b.TextContent == "Series A").Click();

        Assert.Equal(series, Services.GetRequiredService<MovieFilterNavigationState>().PeekPendingFilter()?.CatalogSeries);
    }

    [Fact]
    public void TrackedMovie_WithALabelButNoSeries_HasNoShelfButAClickableLabelBadge()
    {
        var label = new R18DevCatalogRef(10, "S1 NO.1 STYLE");
        using var factory = SetUpServices(r18DevDumpStore: CatalogStore("ABC-123", null, label));
        using (var db = factory.CreateDbContext())
        {
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaStudio = "Studio", MetaLabel = "S1 NO.1 STYLE" });
            db.SaveChanges();
        }

        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button.movie-detail-badge"), b => b.TextContent == "S1 NO.1 STYLE"));
        Assert.Empty(cut.FindAll(".related-releases"));
        Assert.DoesNotContain("More from this label", cut.Markup);
    }

    [Fact]
    public void ChangingTheRouteCode_LoadsTheOtherMovie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Got, MetaTitle = "First movie" });
            db.Movies.Add(new Movie { Code = "DEF-456", Status = MovieStatus.Got, MetaTitle = "Second movie" });
            db.SaveChanges();
        }
        var cut = Render<MovieDetail>(p => p.Add(x => x.RouteCode, "ABC-123"));
        cut.WaitForAssertion(() => Assert.Contains("First movie", cut.Markup));

        cut.Render(p => p.Add(x => x.RouteCode, "DEF-456"));

        cut.WaitForAssertion(() => Assert.Contains("Second movie", cut.Markup));
        Assert.DoesNotContain("First movie", cut.Markup);
    }
}
