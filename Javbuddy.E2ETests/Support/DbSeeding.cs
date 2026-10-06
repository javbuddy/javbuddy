using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.E2ETests.Support;

/// <summary>Seeds rows directly via the same <see cref="IDbContextFactory{TContext}"/> pattern
/// the app itself uses everywhere (see CLAUDE.md), bypassing the UI for fast/deterministic setup
/// on tests that aren't exercising the seeded flow itself (e.g. a Movies grid test that just
/// needs some rows to render, not to prove Add Movie works).</summary>
public static class DbSeeding
{
    public static async Task<Movie> SeedMovieAsync(
        IDbContextFactory<AppDbContext> dbFactory,
        string code,
        MovieStatus status = MovieStatus.Got,
        string? metaTitle = null)
    {
        var movie = new Movie
        {
            Code = code,
            Status = status,
            MetaTitle = metaTitle ?? $"Test Movie {code}",
            MetaReleaseDate = new DateTime(2024, 1, 1),
            MetaRuntimeMinutes = 120,
            MetaActresses = "Test Actress",
            MetaGenres = "Test Genre",
            MetaFetchedAt = DateTime.UtcNow,
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie;
    }

    /// <summary>Seeds <paramref name="count"/> movies in one SaveChangesAsync round trip — for
    /// tests that need enough rows to force Movies.razor's virtualized window to actually *slide*
    /// (not just grow), where inserting one row at a time the way <see cref="SeedMovieAsync"/>
    /// does would be needlessly slow.</summary>
    public static async Task SeedManyMoviesAsync(IDbContextFactory<AppDbContext> dbFactory, int count, string codePrefix, MovieStatus status = MovieStatus.Got)
    {
        var movies = Enumerable.Range(0, count).Select(i => new Movie
        {
            Code = $"{codePrefix}-{i:D4}",
            Status = status,
            MetaTitle = $"{codePrefix} Test Movie {i:D4}",
            MetaReleaseDate = new DateTime(2024, 1, 1),
            MetaRuntimeMinutes = 120,
            MetaActresses = "Test Actress",
            MetaGenres = "Test Genre",
            MetaFetchedAt = DateTime.UtcNow,
        }).ToList();

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Movies.AddRange(movies);
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds <paramref name="movieCount"/> movies with <paramref name="scenesPerMovie"/> scenes
    /// each, in one SaveChangesAsync round trip — enough cards for the scene wall's virtualized window
    /// to slide.</summary>
    public static async Task SeedManyScenesAsync(IDbContextFactory<AppDbContext> dbFactory, int movieCount, int scenesPerMovie, string codePrefix)
    {
        var movies = Enumerable.Range(0, movieCount).Select(i =>
        {
            var movie = new Movie
            {
                Code = $"{codePrefix}-{i:D4}",
                Status = MovieStatus.Got,
                MetaTitle = $"{codePrefix} Scene Movie {i:D4}",
                // No release date, so they sort after every other test's scenes on the wall's default
                // newest-first order and don't push those out of its first window.
                MetaFetchedAt = DateTime.UtcNow,
            };
            for (var s = 0; s < scenesPerMovie; s++)
            {
                movie.Scenes.Add(new Scene { StartSeconds = s * 60, EndSeconds = s * 60 + 50, Title = $"Scene {s + 1}" });
            }
            return movie;
        }).ToList();

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Movies.AddRange(movies);
        await db.SaveChangesAsync();
    }

    public static async Task<Actor> SeedActorAsync(IDbContextFactory<AppDbContext> dbFactory, string name, bool isFavorite = false, bool isRetired = false)
    {
        var (firstName, lastName) = ActorDisplayName.Parse(name);
        var actor = new Actor { FirstName = firstName, LastName = lastName, IsFavorite = isFavorite, IsRetired = isRetired };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Actors.Add(actor);
        await db.SaveChangesAsync();
        return actor;
    }

    public static async Task SeedActorPhotosAsync(
        IDbContextFactory<AppDbContext> dbFactory,
        int actorId,
        int count,
        IReadOnlyList<(Guid StorageId, double AspectRatio)> images)
    {
        var uploadedAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var photos = Enumerable.Range(0, count).Select(i => new ActorPhoto
        {
            ActorId = actorId,
            ThumbStorageId = images[i % images.Count].StorageId,
            FullStorageId = images[i % images.Count].StorageId,
            AspectRatio = images[i % images.Count].AspectRatio,
            UploadedAt = uploadedAt.AddHours(-i),
        });
        await using var db = await dbFactory.CreateDbContextAsync();
        db.ActorPhotos.AddRange(photos);
        await db.SaveChangesAsync();
    }

    /// <summary>Prowlarr is deliberately left un-env-configured for the whole E2E suite (see
    /// E2EFixture) so its Settings &gt; Connections form stays editable — everything that needs
    /// Prowlarr actually working (without exercising the Connections form itself) seeds it here,
    /// going through the app's own <see cref="IConnectionSettingsSaveService"/> upsert rather than
    /// writing the row by hand, so this behaves identically to a user saving the form.</summary>
    public static async Task SeedProwlarrSettingsAsync(IServiceProvider appServices, string baseUrl, string apiKey)
    {
        using var scope = appServices.CreateScope();
        var saveService = scope.ServiceProvider.GetRequiredService<IConnectionSettingsSaveService>();
        await saveService.SaveProwlarrAsync(new ProwlarrSettings { BaseUrl = baseUrl, ApiKey = apiKey });
    }

    /// <summary>Like SeedProwlarrSettingsAsync — LocalLibrary:RootPaths is blanked suite-wide
    /// (see JavbuddyAppFactory) so no test accidentally scans a real developer's real library, so
    /// a test that needs actual discovery seeds a root here, pointed at an on-disk fixture folder.</summary>
    public static async Task SeedLocalLibraryRootPathAsync(IServiceProvider appServices, string rootPath)
    {
        using var scope = appServices.CreateScope();
        var saveService = scope.ServiceProvider.GetRequiredService<IConnectionSettingsSaveService>();
        await saveService.SaveLocalLibraryAsync(new LocalLibrarySettings { RootPaths = rootPath });
    }

    /// <summary>The throwaway library root SeedPlayableVideoAsync puts movie files under, one per
    /// test run; deleted when the fixture is disposed.</summary>
    public static readonly string PlayableLibraryRoot =
        Path.Combine(Path.GetTempPath(), $"javbuddy-e2e-library-{Environment.ProcessId}");

    /// <summary>Gives a seeded movie a real local video file — the 3 s side-by-side fisheye fixture
    /// clip, decodable so VR 2D mode has real frames — under <see cref="PlayableLibraryRoot"/>, and
    /// points the library at that root, so the player can stream it.</summary>
    public static async Task SeedPlayableVideoAsync(IServiceProvider appServices, IDbContextFactory<AppDbContext> dbFactory, Movie movie)
    {
        var fileName = movie.Code + ".webm";
        var folder = Directory.CreateDirectory(Path.Combine(PlayableLibraryRoot, movie.Code!)).FullName;
        var path = Path.Combine(folder, fileName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "VrVideo", "sbs-fisheye.webm"), path, overwrite: true);
        await SeedLocalLibraryRootPathAsync(appServices, PlayableLibraryRoot);

        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Movies.Where(m => m.Id == movie.Id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.LocalFileSizeBytes, new FileInfo(path).Length)
            .SetProperty(m => m.MediaVideoFileName, fileName));
    }

    /// <summary>The throwaway ObjectStore:Path the E2E app stores durable files (actor images,
    /// trickplay) in; deleted when the fixture is disposed.</summary>
    public static readonly string ObjectStoreRoot =
        Path.Combine(Path.GetTempPath(), $"javbuddy-e2e-objects-{Environment.ProcessId}");

    // A valid 1x1 lossy WebP, standing in for a real tile sheet.
    private static readonly byte[] TinyWebp = Convert.FromBase64String("UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAwA0JaQAA3AA/vuUAAA=");

    /// <summary>Gives a movie seeded with <see cref="SeedPlayableVideoAsync"/> a locally generated
    /// trickplay set: the probed MovieFile row its identity comes from, plus its
    /// TrickplaySet row and one tile sheet under <see cref="ObjectStoreRoot"/>. Returns the set's identity.</summary>
    public static async Task<string> SeedLocalTrickplayAsync(IDbContextFactory<AppDbContext> dbFactory, Movie movie)
    {
        var fileName = movie.Code + ".webm";
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            // The fixture clip: 512x256, 3 s.
            db.MovieFiles.Add(new MovieFile { MovieId = movie.Id, FileName = fileName, IsPrimary = true, DurationSeconds = 3, Width = 512, Height = 256 });
            await db.SaveChangesAsync();
        }

        var identity = TrickplayIdentity.For(fileName, 3, 512, 256)!;
        var codeFolder = TrickplayStore.CodeFolder(movie.Code!);
        var folder = Directory.CreateDirectory(Path.Combine(ObjectStoreRoot, "trickplay", codeFolder, identity)).FullName;
        await File.WriteAllBytesAsync(Path.Combine(folder, TrickplayStore.SheetName(0)), TinyWebp);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.TrickplaySets.Add(new TrickplaySet
            {
                CodeFolder = codeFolder,
                Identity = identity,
                Width = 320,
                Height = 160,
                TileWidth = 10,
                TileHeight = 10,
                ThumbnailCount = 3,
                IntervalMs = 1000,
                DurationSeconds = 3,
                LeftEyeOnly = true,
                GeneratedAt = DateTime.UtcNow,
                FileName = fileName,
            });
            await db.SaveChangesAsync();
        }
        return identity;
    }

    public static async Task SeedR18DevEnabledAsync(IDbContextFactory<AppDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
        await db.SaveChangesAsync();
    }

    public static async Task<TorrentDownload> SeedTorrentDownloadAsync(
        IDbContextFactory<AppDbContext> dbFactory,
        Movie movie,
        TorrentDownloadStatus status = TorrentDownloadStatus.Downloading,
        string? hash = null)
    {
        var download = new TorrentDownload
        {
            MovieId = movie.Id,
            MovieCode = movie.Code!,
            ReleaseTitle = $"{movie.Code} Test Release 1080p",
            Indexer = "Fake Indexer",
            Size = 2_000_000_000,
            Hash = hash,
            Status = status,
            Progress = status == TorrentDownloadStatus.Completed ? 1.0 : 0.4,
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.TorrentDownloads.Add(download);
        await db.SaveChangesAsync();
        return download;
    }

    public static async Task<ScheduledTaskRun> SeedScheduledTaskRunAsync(
        IDbContextFactory<AppDbContext> dbFactory,
        string taskName,
        DateTime? startedAt = null,
        DateTime? endedAt = null,
        bool success = false,
        string? progressStage = null,
        int? progressCurrent = null,
        int? progressTotal = null)
    {
        var run = new ScheduledTaskRun
        {
            TaskName = taskName,
            QueuedAt = DateTime.UtcNow,
            StartedAt = startedAt ?? DateTime.UtcNow,
            EndedAt = endedAt,
            Success = success,
            ProgressStage = progressStage,
            ProgressCurrent = progressCurrent,
            ProgressTotal = progressTotal,
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ScheduledTaskRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }
}
