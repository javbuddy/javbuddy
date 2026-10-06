using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class MovieCleanupServiceTests
{
    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-cleanup-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public string AddMovieFolder(string folderName)
        {
            var folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static (MovieCleanupService Service, TempRoot Root) CreateService(TestDbContextFactory factory)
    {
        var root = new TempRoot();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalLibrary:RootPaths:0"] = root.Path })
            .Build();
        var localLibraryClient = new LocalLibraryClient(
            factory, config, new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IMediaInfoProber>(), NullLogger<LocalLibraryClient>.Instance);
        return (new MovieCleanupService(factory, localLibraryClient), root);
    }

    /// <summary>Loads the movies behind a queue of IDs, in queue order, so tests can
    /// assert on their codes.</summary>
    private static async Task<List<Movie>> LoadQueueAsync(TestDbContextFactory factory, List<int> ids)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movies = await db.Movies.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        return ids.Select(id => movies[id]).ToList();
    }

    [Fact]
    public async Task BuildQueueAsync_OnlyIncludesGotMoviesNotBlacklistedOrCurrentlySnoozed()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "GOT-1", Status = MovieStatus.Got },
                new Movie { Code = "MISSING-1", Status = MovieStatus.Missing },
                new Movie { Code = "BLACKLISTED-1", Status = MovieStatus.Got, CleanupBlacklisted = true },
                new Movie { Code = "SNOOZED-ACTIVE-1", Status = MovieStatus.Got, CleanupSnoozedUntil = DateTime.UtcNow.AddDays(10) },
                new Movie { Code = "SNOOZED-EXPIRED-1", Status = MovieStatus.Got, CleanupSnoozedUntil = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random));

        var codes = queue.Select(m => m.Code).ToHashSet();
        Assert.Contains("GOT-1", codes);
        Assert.Contains("SNOOZED-EXPIRED-1", codes);
        Assert.DoesNotContain("MISSING-1", codes);
        Assert.DoesNotContain("BLACKLISTED-1", codes);
        Assert.DoesNotContain("SNOOZED-ACTIVE-1", codes);
    }

    [Fact]
    public async Task BuildQueueAsync_WithJellyfinLibrary_FiltersQueueToMatchingLibrary()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "JAV-1", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" },
                new Movie { Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" },
                new Movie { Code = "NOLIB-1", Status = MovieStatus.Got, JellyfinLibraryName = null });
            await db.SaveChangesAsync();
        }

        var javQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, "JAV"));
        var vrQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, "VR"));

        Assert.Single(javQueue);
        Assert.Equal("JAV-1", javQueue[0].Code);

        Assert.Single(vrQueue);
        Assert.Equal("VR-1", vrQueue[0].Code);
    }

    [Fact]
    public async Task BuildQueueAsync_WithoutJellyfinLibrary_IncludesAllLibraries()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "JAV-1", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" },
                new Movie { Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" },
                new Movie { Code = "NOLIB-1", Status = MovieStatus.Got, JellyfinLibraryName = null });
            await db.SaveChangesAsync();
        }

        var queueNull = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, null));
        var queueEmpty = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, ""));

        Assert.Equal(3, queueNull.Count);
        Assert.Equal(3, queueEmpty.Count);
    }

    [Fact]
    public async Task GetEligibleLibrariesAsync_ReturnsDistinctSortedLibrariesOfEligibleMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var favActor = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            db.Actors.Add(favActor);
            await db.SaveChangesAsync();

            var favMovie = new Movie { Code = "FAV-1", Status = MovieStatus.Got, JellyfinLibraryName = "FavOnlyLib" };
            db.Movies.Add(favMovie);
            await db.SaveChangesAsync();

            db.MovieActors.Add(new MovieActor { MovieId = favMovie.Id, ActorId = favActor.Id });

            db.Movies.AddRange(
                new Movie { Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" },
                new Movie { Code = "JAV-1", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" },
                new Movie { Code = "JAV-2", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" },
                new Movie { Code = "MISSING-1", Status = MovieStatus.Missing, JellyfinLibraryName = "Anime" },
                new Movie { Code = "BL-1", Status = MovieStatus.Got, CleanupBlacklisted = true, JellyfinLibraryName = "Hidden" },
                new Movie { Code = "SNOOZE-1", Status = MovieStatus.Got, CleanupSnoozedUntil = DateTime.UtcNow.AddDays(5), JellyfinLibraryName = "Snoozed" },
                new Movie { Code = "NOLIB-1", Status = MovieStatus.Got, JellyfinLibraryName = null },
                new Movie { Code = "EMPTY-1", Status = MovieStatus.Got, JellyfinLibraryName = "" });
            await db.SaveChangesAsync();
        }

        var libraries = await service.GetEligibleLibrariesAsync();

        Assert.Equal(["JAV", "VR"], libraries);
    }

    [Fact]
    public async Task BuildQueueAsync_ExcludesMoviesFromFavoriteActors()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var favActor = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            var normalActor = new Actor { FirstName = "Arina", LastName = "Hashimoto", IsFavorite = false };
            db.Actors.AddRange(favActor, normalActor);
            await db.SaveChangesAsync();

            var m1 = new Movie { Code = "NORMAL-1", Status = MovieStatus.Got };
            var m2 = new Movie { Code = "FAV-LINKED-1", Status = MovieStatus.Got };
            var m3 = new Movie { Code = "FAV-META-1", Status = MovieStatus.Got, MetaActresses = "Mikami Yua" };
            db.Movies.AddRange(m1, m2, m3);
            await db.SaveChangesAsync();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = m1.Id, ActorId = normalActor.Id },
                new MovieActor { MovieId = m2.Id, ActorId = favActor.Id });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random));

        var codes = queue.Select(m => m.Code).ToHashSet();
        Assert.Contains("NORMAL-1", codes);
        Assert.DoesNotContain("FAV-LINKED-1", codes);
        Assert.DoesNotContain("FAV-META-1", codes);
    }

    [Fact]
    public async Task BuildQueueAsync_OldestAdded_OrdersByFileAddedAtAscending()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "NEWER", Status = MovieStatus.Got, FileAddedAt = new DateTime(2026, 6, 1) },
                new Movie { Code = "OLDEST", Status = MovieStatus.Got, FileAddedAt = new DateTime(2024, 1, 1) },
                new Movie { Code = "MIDDLE", Status = MovieStatus.Got, FileAddedAt = new DateTime(2025, 3, 1) });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.OldestAdded));

        Assert.Equal(["OLDEST", "MIDDLE", "NEWER"], queue.Select(m => m.Code));
    }

    [Fact]
    public async Task BuildQueueAsync_LargestFirst_OrdersByLocalFileSizeBytesDescending()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "SMALL", Status = MovieStatus.Got, LocalFileSizeBytes = 100 },
                new Movie { Code = "LARGE", Status = MovieStatus.Got, LocalFileSizeBytes = 900 },
                new Movie { Code = "MEDIUM", Status = MovieStatus.Got, LocalFileSizeBytes = 500 });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.LargestFirst));

        Assert.Equal(["LARGE", "MEDIUM", "SMALL"], queue.Select(m => m.Code));
    }

    [Fact]
    public async Task BuildQueueAsync_ByActress_GroupsConsecutivelyByFirstActressAlphabetically()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "ZOE-1", Status = MovieStatus.Got, MetaActresses = "Zoe" },
                new Movie { Code = "AMY-1", Status = MovieStatus.Got, MetaActresses = "Amy" },
                new Movie { Code = "AMY-2", Status = MovieStatus.Got, MetaActresses = "Amy, Co-Star" });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.ByActress));

        Assert.Equal(["AMY-1", "AMY-2", "ZOE-1"], queue.Select(m => m.Code));
    }

    [Fact]
    public async Task BuildQueueAsync_Random_ReturnsEveryEligibleMovieRegardlessOfOrder()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(Enumerable.Range(0, 10).Select(i => new Movie { Code = $"RND-{i}", Status = MovieStatus.Got }));
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random));

        Assert.Equal(10, queue.Count);
        Assert.Equal(10, queue.Select(m => m.Code).Distinct().Count());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheOnDiskFolderAndTheDbRow()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        var folder = root.AddMovieFolder("DEL-123");
        await File.WriteAllTextAsync(Path.Combine(folder, "DEL-123.mp4"), "video bytes");
        await File.WriteAllTextAsync(Path.Combine(folder, "poster.jpg"), "poster bytes");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "DEL-123", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var result = await service.DeleteAsync(movieId);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(folder));
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync(m => m.Id == movieId));
        var history = await readDb.DeletedMovies.SingleAsync();
        Assert.Equal("DEL-123", history.Code);
        Assert.Equal(MovieStatus.Got, history.PreviousStatus);
    }

    [Fact]
    public async Task DeleteAsync_FolderDeleteThrows_ReturnsFailureAndKeepsTheDbRow()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        var folder = root.AddMovieFolder("PERM-1");
        var filePath = Path.Combine(folder, "PERM-1.mp4");
        await File.WriteAllTextAsync(filePath, "video bytes");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "PERM-1", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        OperationResult result;
        if (OperatingSystem.IsWindows())
        {
            // An exclusively-locked file inside the folder makes Directory.Delete throw
            // IOException on Windows regardless of privilege level.
            await using var lockHandle = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            result = await service.DeleteAsync(movieId);
        }
        else
        {
            if (Environment.IsPrivilegedProcess)
            {
                // Root (or any process with CAP_DAC_OVERRIDE) bypasses Unix permission checks
                // entirely, so the chmod-based restriction below can't force the failure branch
                // here — there's no reliable OS-agnostic way to trigger it under root without
                // extra setup (e.g. the immutable file attribute, which containers also drop by
                // default). This repo's CI image (mcr.microsoft.com/dotnet/sdk) runs as root, so
                // this assertion is a deliberate no-op there; it does exercise the real
                // IOException/UnauthorizedAccessException catch branch in DeleteAsync when run
                // locally as a non-root user (verified manually before writing this test).
                return;
            }

            // Removing write+execute from the folder itself (not the file) blocks File.Delete on
            // the file inside it: on Unix, deleting a directory entry needs write access to the
            // *parent* directory, not the file being removed.
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            try
            {
                result = await service.DeleteAsync(movieId);
            }
            finally
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Assert.False(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await readDb.Movies.AnyAsync(m => m.Id == movieId));
        Assert.Empty(await readDb.DeletedMovies.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_FolderOutsideTheLibraryRoots_IsRefusedAndKeepsTheDbRow()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        using var elsewhere = new TempRoot();
        var folder = elsewhere.AddMovieFolder("OUT-1");
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.ResolveMovieFolderPathAsync("OUT-1", Arg.Any<CancellationToken>()).Returns(folder);
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([root.Path]);
        var service = new MovieCleanupService(factory, localLibraryClient);

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "OUT-1", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var result = await service.DeleteAsync(movieId);

        Assert.False(result.Success);
        Assert.Contains("isn't a folder directly inside a configured local library root", result.ErrorMessage);
        Assert.True(Directory.Exists(folder));
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await readDb.Movies.AnyAsync(m => m.Id == movieId));
        Assert.Empty(await readDb.DeletedMovies.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_MovieNotFound_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        var result = await service.DeleteAsync(999);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DeleteAsync_NoResolvableFolder_StillRemovesTheDbRow()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "NEVER-ON-DISK", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var result = await service.DeleteAsync(movieId);

        Assert.True(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync(m => m.Id == movieId));
    }

    [Fact]
    public async Task SnoozeAsync_SetsSnoozedUntilNinetyDaysOut()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "SNZ-1", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var before = DateTime.UtcNow;
        var result = await service.SnoozeAsync(movieId);
        var after = DateTime.UtcNow;

        Assert.True(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        var reloaded = await readDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.NotNull(reloaded.CleanupSnoozedUntil);
        Assert.InRange(reloaded.CleanupSnoozedUntil!.Value, before.AddDays(90), after.AddDays(90));
    }

    [Fact]
    public async Task BlacklistAsync_ThenUnblacklistAsync_RoundTripsTheFlag()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "BLK-1", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var blacklistResult = await service.BlacklistAsync(movieId);
        Assert.True(blacklistResult.Success);
        await using (var readDb = await factory.CreateDbContextAsync())
        {
            Assert.True((await readDb.Movies.SingleAsync(m => m.Id == movieId)).CleanupBlacklisted);
        }

        var unblacklistResult = await service.UnblacklistAsync(movieId);
        Assert.True(unblacklistResult.Success);
        await using var readDb2 = await factory.CreateDbContextAsync();
        Assert.False((await readDb2.Movies.SingleAsync(m => m.Id == movieId)).CleanupBlacklisted);
    }

    [Fact]
    public async Task SnoozeAsync_MovieNotFound_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        Assert.False((await service.SnoozeAsync(999)).Success);
        Assert.False((await service.BlacklistAsync(999)).Success);
        Assert.False((await service.UnblacklistAsync(999)).Success);
    }

    [Fact]
    public async Task GetMovieActorsAsync_ResolvesLinkedActorsAndThumbnailStatus()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        int actorWithImageId;
        int actorWithoutImageId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor1 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var actor2 = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.AddRange(actor1, actor2);
            await db.SaveChangesAsync();

            actorWithImageId = actor1.Id;
            actorWithoutImageId = actor2.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorWithImageId,
                Variant = "thumb",
                StorageId = Guid.NewGuid()
            });

            var movie = new Movie
            {
                Code = "ACT-1",
                Status = MovieStatus.Got,
                MetaActresses = "Mikami Yua, Hashimoto Arina, Unknown Actress"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;

            db.MovieActors.AddRange(
                new MovieActor { MovieId = movieId, ActorId = actorWithImageId },
                new MovieActor { MovieId = movieId, ActorId = actorWithoutImageId });
            await db.SaveChangesAsync();
        }

        var actors = await service.GetMovieActorsAsync(movieId, "Mikami Yua, Hashimoto Arina, Unknown Actress");

        Assert.Equal(3, actors.Count);

        var first = actors[0];
        Assert.Equal("Mikami Yua", first.Name);
        Assert.Equal(actorWithImageId, first.ActorId);
        Assert.True(first.HasImage);

        var second = actors[1];
        Assert.Equal("Hashimoto Arina", second.Name);
        Assert.Equal(actorWithoutImageId, second.ActorId);
        Assert.False(second.HasImage);

        var third = actors[2];
        Assert.Equal("Unknown Actress", third.Name);
        Assert.Null(third.ActorId);
        Assert.False(third.HasImage);
    }

    [Fact]
    public async Task GetQueueMovieAsync_LoadsTheMovieOrNullWhenItNoLongerExists()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "CARD-1", MetaTitle = "Card Movie", Status = MovieStatus.Got, JellyfinItemId = "item-1", LocalFileSizeBytes = 42 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var loaded = await service.GetQueueMovieAsync(movieId);

        Assert.NotNull(loaded);
        Assert.Equal("CARD-1", loaded.Code);
        Assert.Equal("Card Movie", loaded.DisplayName);
        Assert.Equal("item-1", loaded.JellyfinItemId);
        Assert.Equal(42, loaded.LocalFileSizeBytes);
        Assert.Null(await service.GetQueueMovieAsync(movieId + 999));
    }

    [Fact]
    public async Task GetMetaActressesAsync_ReturnsTheMoviesCurrentCastText()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "CAST-1", Status = MovieStatus.Got, MetaActresses = "Mikami Yua, Unknown Actress" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        // Simulates an edit made after the queue's Movie instance was loaded.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tracked = await db.Movies.FindAsync(movieId);
            tracked!.MetaActresses = "Mikami Yua";
            await db.SaveChangesAsync();
        }

        Assert.Equal("Mikami Yua", await service.GetMetaActressesAsync(movieId));
    }

    [Fact]
    public async Task GetMetaActressesAsync_ReturnsNullWhenMovieHasNoCastOrDoesNotExist()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "CAST-2", Status = MovieStatus.Got, MetaActresses = null };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        Assert.Null(await service.GetMetaActressesAsync(movieId));
        Assert.Null(await service.GetMetaActressesAsync(movieId + 999));
    }

    [Fact]
    public async Task GetMovieTagsAsync_ReturnsOnlyThisMoviesTags_WithParentAndReviewFlag_InDetailOrder()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        int childId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var category = new Tag { Name = "Category" };
            db.Tags.Add(category);
            await db.SaveChangesAsync();

            var child = new Tag { Name = "Subtag", ParentTagId = category.Id };
            var plain = new Tag { Name = "Drama" };
            var pending = new Tag { Name = "Zeta", NeedsReview = true };
            var other = new Tag { Name = "OtherMovieTag" };
            db.Tags.AddRange(child, plain, pending, other);

            var movie = new Movie { Code = "TAG-1", Status = MovieStatus.Got };
            var otherMovie = new Movie { Code = "TAG-2", Status = MovieStatus.Got };
            db.Movies.AddRange(movie, otherMovie);
            await db.SaveChangesAsync();

            db.MovieTags.AddRange(
                new MovieTag { MovieId = movie.Id, TagId = pending.Id },
                new MovieTag { MovieId = movie.Id, TagId = child.Id },
                new MovieTag { MovieId = movie.Id, TagId = plain.Id },
                new MovieTag { MovieId = otherMovie.Id, TagId = other.Id });
            await db.SaveChangesAsync();
            movieId = movie.Id;
            childId = child.Id;
        }

        var tags = await service.GetMovieTagsAsync(movieId);

        // Ordered by parent name (or own name when top-level), then name — the same order Movie
        // Detail's Genres section uses: Category › Subtag, Drama, Zeta.
        Assert.Equal(["Subtag", "Drama", "Zeta"], tags.Select(t => t.Name));
        var child0 = tags[0];
        Assert.Equal(childId, child0.Id);
        Assert.Equal("Category", child0.ParentName);
        Assert.False(child0.NeedsReview);
        Assert.Null(tags[1].ParentName);
        Assert.True(tags[2].NeedsReview);
    }

    [Fact]
    public async Task GetMovieTagsAsync_ReturnsEmptyForMovieWithoutTags()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "TAG-3", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        Assert.Empty(await service.GetMovieTagsAsync(movieId));
    }

    [Fact]
    public async Task MarkBrokenBFrameAsync_SetsFlagOnMovie()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "BFR-1", Status = MovieStatus.Got, HasBrokenBFrames = false };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var result = await service.MarkBrokenBFrameAsync(movieId);

        Assert.True(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        var updated = await readDb.Movies.FindAsync(movieId);
        Assert.True(updated!.HasBrokenBFrames);
    }

    [Fact]
    public async Task UnmarkBrokenBFrameAsync_ClearsFlagOnMovie()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "BFR-CLEAR", Status = MovieStatus.Got, HasBrokenBFrames = true };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var result = await service.UnmarkBrokenBFrameAsync(movieId);

        Assert.True(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        var updated = await readDb.Movies.FindAsync(movieId);
        Assert.False(updated!.HasBrokenBFrames);
    }

    [Fact]
    public async Task GetBrokenBFrameCountAndIds_ReturnsOnlyGotMoviesWithFlag()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Id = 10, Code = "GOT-BFR", Status = MovieStatus.Got, HasBrokenBFrames = true },
                new Movie { Id = 11, Code = "GOT-OK", Status = MovieStatus.Got, HasBrokenBFrames = false },
                new Movie { Id = 12, Code = "MISSING-BFR", Status = MovieStatus.Missing, HasBrokenBFrames = true });
            await db.SaveChangesAsync();
        }

        var count = await service.GetBrokenBFrameCountAsync();
        var ids = await service.GetBrokenBFrameMovieIdsAsync();

        Assert.Equal(1, count);
        Assert.Single(ids);
        Assert.Equal(10, ids[0]);
    }

    [Fact]
    public void StartSession_SetsActiveSessionWithCorrectProperties()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        var movies = new List<Movie>
        {
            new() { Id = 1, Code = "SESS-1", Status = MovieStatus.Got },
            new() { Id = 2, Code = "SESS-2", Status = MovieStatus.Got }
        };

        service.StartSession([.. movies.Select(m => m.Id)], CleanupOrder.ByActress, "JAV");

        var session = service.ActiveSession;
        Assert.NotNull(session);
        Assert.Equal(CleanupOrder.ByActress, session.Order);
        Assert.Equal("JAV", session.Library);
        Assert.Equal(2, session.Queue.Count);
        Assert.Equal(0, session.CurrentIndex);
        Assert.Equal(0, session.ReviewedCount);
        Assert.InRange(session.StartedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void AdvanceSession_IncrementsCurrentIndexAndReviewedCount()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        var movies = new List<Movie> { new() { Id = 1, Code = "ADV-1", Status = MovieStatus.Got } };
        service.StartSession([.. movies.Select(m => m.Id)], CleanupOrder.Random, null);

        service.AdvanceSession(movies[0], CleanupOutcome.Snoozed);

        var session = service.ActiveSession;
        Assert.NotNull(session);
        Assert.Equal(1, session.CurrentIndex);
        Assert.Equal(1, session.ReviewedCount);
        var decision = Assert.Single(session.Decisions);
        Assert.Equal(1, decision.MovieId);
        Assert.Equal(CleanupOutcome.Snoozed, decision.Outcome);
    }

    [Fact]
    public void EndSession_MarksActiveSessionEndedAndKeepsItTracked()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        service.StartSession([1], CleanupOrder.Random, null);

        var ended = service.EndSession();

        Assert.NotNull(ended);
        Assert.NotNull(ended.EndedAt);
        Assert.Same(ended, service.ActiveSession);
    }

    [Fact]
    public void EndSession_WhenNoActiveSession_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        Assert.Null(service.EndSession());
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenSessionEnded_ReturnsItUnprunedEvenIfQueueEmpty()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        service.StartSession([], CleanupOrder.Random, null);
        service.EndSession();

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.NotNull(resumed.EndedAt);
        Assert.NotNull(service.ActiveSession);
    }

    [Fact]
    public void DiscardSession_ClearsActiveSession()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        service.StartSession([1], CleanupOrder.Random, null);
        Assert.NotNull(service.ActiveSession);

        service.DiscardSession();

        Assert.Null(service.ActiveSession);
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenNoActiveSession_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        var resumed = await service.ResumeSessionAsync();
        Assert.Null(resumed);
    }

    [Fact]
    public async Task ResumeSessionAsync_PrunesMoviesThatAreNoLongerEligible()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int m1Id, m2Id, m3Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1 = new Movie { Code = "ELIGIBLE-1", Status = MovieStatus.Got };
            var m2 = new Movie { Code = "DELETED-WHILE-AWAY", Status = MovieStatus.Got };
            var m3 = new Movie { Code = "SNOOZED-WHILE-AWAY", Status = MovieStatus.Got };
            db.Movies.AddRange(m1, m2, m3);
            await db.SaveChangesAsync();
            m1Id = m1.Id;
            m2Id = m2.Id;
            m3Id = m3.Id;
        }

        var queue = new List<Movie>
        {
            new() { Id = m1Id, Code = "ELIGIBLE-1", Status = MovieStatus.Got },
            new() { Id = m2Id, Code = "DELETED-WHILE-AWAY", Status = MovieStatus.Got },
            new() { Id = m3Id, Code = "SNOOZED-WHILE-AWAY", Status = MovieStatus.Got }
        };
        service.StartSession([.. queue.Select(m => m.Id)], CleanupOrder.Random, null);

        // While away, m2 is deleted and m3 is snoozed
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m2Db = await db.Movies.FindAsync(m2Id);
            db.Movies.Remove(m2Db!);
            var m3Db = await db.Movies.FindAsync(m3Id);
            m3Db!.CleanupSnoozedUntil = DateTime.UtcNow.AddDays(90);
            await db.SaveChangesAsync();
        }

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.Single(resumed.Queue);
        Assert.Equal(m1Id, resumed.Queue[0]);
    }

    [Fact]
    public async Task ResumeSessionAsync_PrunesMoviesWithNewlyFavoritedActors()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int m1Id, m2Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1 = new Movie { Code = "FAV-NEW", Status = MovieStatus.Got, MetaActresses = "Yua Mikami" };
            var m2 = new Movie { Code = "FAV-KEEP", Status = MovieStatus.Got, MetaActresses = "Someone Else" };
            db.Movies.AddRange(m1, m2);
            await db.SaveChangesAsync();
            m1Id = m1.Id;
            m2Id = m2.Id;
        }

        var queue = new List<Movie>
        {
            new() { Id = m1Id, Code = "FAV-NEW", Status = MovieStatus.Got, MetaActresses = "Yua Mikami" },
            new() { Id = m2Id, Code = "FAV-KEEP", Status = MovieStatus.Got, MetaActresses = "Someone Else" }
        };
        service.StartSession([.. queue.Select(m => m.Id)], CleanupOrder.Random, null);

        // While away, Mikami Yua is marked as favorite
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true });
            await db.SaveChangesAsync();
        }

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.Single(resumed.Queue);
        Assert.Equal(m2Id, resumed.Queue[0]);
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenNothingLeftToReview_DiscardsSessionAndReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int m1Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1 = new Movie { Code = "GONE-1", Status = MovieStatus.Got };
            db.Movies.Add(m1);
            await db.SaveChangesAsync();
            m1Id = m1.Id;
        }

        service.StartSession([m1Id], CleanupOrder.Random, null);

        // While away, the last remaining movie is blacklisted from cleanup
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1Db = await db.Movies.FindAsync(m1Id);
            m1Db!.CleanupBlacklisted = true;
            await db.SaveChangesAsync();
        }

        Assert.Null(await service.ResumeSessionAsync());
        Assert.Null(service.ActiveSession);
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenSessionQueueAlreadyEmpty_DiscardsSessionAndReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        service.StartSession([], CleanupOrder.Random, null);

        Assert.Null(await service.ResumeSessionAsync());
        Assert.Null(service.ActiveSession);
    }

    [Fact]
    public async Task RecordTagEditAsync_SnapshotsTheTagNameWithItsParent()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        int childId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var parent = new Tag { Name = "Genre" };
            var child = new Tag { Name = "Solo", ParentTag = parent };
            db.Tags.AddRange(parent, child);
            await db.SaveChangesAsync();
            childId = child.Id;
        }
        service.StartSession([1], CleanupOrder.Random, null);

        await service.RecordTagEditAsync(1, childId, added: true);

        var edit = Assert.Single(service.ActiveSession!.Edits);
        Assert.Equal(CleanupEdit.Tag(1, childId, "Genre › Solo", added: true), edit);
    }

    [Fact]
    public async Task RecordActorEditAsync_SnapshotsTheActorDisplayName()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }
        service.StartSession([1], CleanupOrder.Random, null);

        await service.RecordActorEditAsync(1, actorId, added: false);

        var edit = Assert.Single(service.ActiveSession!.Edits);
        Assert.Equal(CleanupEdit.Actor(1, actorId, ActorDisplayName.Format("Yua", "Mikami"), added: false), edit);
    }

    [Fact]
    public void SessionTracker_RecordEdit_KeepsOnlyTheNetChange()
    {
        var tracker = new MovieCleanupSessionTracker();
        tracker.StartSession([1, 2], CleanupOrder.Random, null);

        tracker.RecordEdit(CleanupEdit.Tag(1, 5, "Drama", added: true));
        tracker.RecordEdit(CleanupEdit.Tag(1, 5, "Drama", added: false));
        tracker.RecordEdit(CleanupEdit.Tag(2, 5, "Drama", added: true));
        tracker.RecordEdit(CleanupEdit.Actor(1, 42, "Mikami Yua", added: false));
        tracker.RecordEdit(CleanupEdit.Actor(1, 42, "Mikami Yua", added: false));

        Assert.Equal(
            [CleanupEdit.Tag(2, 5, "Drama", added: true), CleanupEdit.Actor(1, 42, "Mikami Yua", added: false)],
            tracker.ActiveSession!.Edits);
    }

    [Fact]
    public void SessionTracker_RecordEdit_IgnoredWithoutAnOpenSession()
    {
        var tracker = new MovieCleanupSessionTracker();
        tracker.RecordEdit(CleanupEdit.Tag(1, 5, "Drama", added: true));

        tracker.StartSession([1], CleanupOrder.Random, null);
        tracker.EndSession();
        tracker.RecordEdit(CleanupEdit.Tag(1, 5, "Drama", added: true));

        Assert.Empty(tracker.ActiveSession!.Edits);
    }

    [Fact]
    public void SessionTracker_DirectUsage_PreservesStateThreadSafely()
    {
        var tracker = new MovieCleanupSessionTracker();
        Assert.False(tracker.HasActiveSession);
        Assert.Null(tracker.ActiveSession);

        var movies = new List<Movie>
        {
            new() { Id = 1, Code = "TRK-1" },
            new() { Id = 2, Code = "TRK-2" }
        };

        var session = tracker.StartSession([.. movies.Select(m => m.Id)], CleanupOrder.OldestAdded, "TestLib");
        Assert.True(tracker.HasActiveSession);
        Assert.Same(session, tracker.ActiveSession);
        Assert.Equal(CleanupOrder.OldestAdded, session.Order);
        Assert.Equal("TestLib", session.Library);

        tracker.AdvanceSession(movies[0], CleanupOutcome.Kept);
        Assert.Equal(1, tracker.ActiveSession!.CurrentIndex);
        Assert.Equal(1, tracker.ActiveSession!.ReviewedCount);

        tracker.ClearSession();
        Assert.False(tracker.HasActiveSession);
        Assert.Null(tracker.ActiveSession);
    }

    [Fact]
    public void SessionTracker_AdvanceSession_SnapshotsTheMovieAtDecisionTime()
    {
        var clock = new StepClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        var tracker = new MovieCleanupSessionTracker(clock);
        var movie = new Movie
        {
            Id = 7,
            Code = "SNAP-7",
            MetaTitle = "Snapshot Movie",
            MetaSourceName = "r18dev",
            LocalFileSizeBytes = 5_000,
            HasBrokenBFrames = true
        };
        tracker.StartSession([movie.Id], CleanupOrder.Random, null);
        clock.Now = clock.Now.AddSeconds(30);

        tracker.AdvanceSession(movie, CleanupOutcome.Deleted);

        var decision = Assert.Single(tracker.ActiveSession!.Decisions);
        Assert.Equal(new CleanupDecision(7, "SNAP-7", "Snapshot Movie", true, 5_000, true, CleanupOutcome.Deleted, clock.Now), decision);
    }

    [Fact]
    public void SessionTracker_EndSession_KeepsTheFirstEndTime()
    {
        var clock = new StepClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        var tracker = new MovieCleanupSessionTracker(clock);
        tracker.StartSession([], CleanupOrder.Random, null);

        clock.Now = clock.Now.AddMinutes(2);
        tracker.EndSession();
        var firstEnd = tracker.ActiveSession!.EndedAt;
        clock.Now = clock.Now.AddMinutes(5);
        tracker.EndSession();

        Assert.Equal(clock.Now.AddMinutes(-5), firstEnd);
        Assert.Equal(firstEnd, tracker.ActiveSession!.EndedAt);
    }

    private sealed class StepClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task BuildQueueAsync_WithActorId_ReturnsOnlyThatActorsEligibleMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            var other = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.AddRange(actor, other);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var solo = new Movie { Code = "SOLO-1", Status = MovieStatus.Got };
            var collab = new Movie { Code = "COLLAB-1", Status = MovieStatus.Got };
            var missing = new Movie { Code = "MISSING-1", Status = MovieStatus.Missing };
            var snoozed = new Movie { Code = "SNOOZED-1", Status = MovieStatus.Got, CleanupSnoozedUntil = DateTime.UtcNow.AddDays(30) };
            var blacklisted = new Movie { Code = "BLACKLISTED-1", Status = MovieStatus.Got, CleanupBlacklisted = true };
            var otherOnly = new Movie { Code = "OTHER-1", Status = MovieStatus.Got };
            db.Movies.AddRange(solo, collab, missing, snoozed, blacklisted, otherOnly);
            await db.SaveChangesAsync();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = solo.Id, ActorId = actor.Id },
                new MovieActor { MovieId = collab.Id, ActorId = actor.Id },
                new MovieActor { MovieId = collab.Id, ActorId = other.Id },
                new MovieActor { MovieId = missing.Id, ActorId = actor.Id },
                new MovieActor { MovieId = snoozed.Id, ActorId = actor.Id },
                new MovieActor { MovieId = blacklisted.Id, ActorId = actor.Id },
                new MovieActor { MovieId = otherOnly.Id, ActorId = other.Id });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, null, actorId));

        Assert.Equal(["COLLAB-1", "SOLO-1"], queue.Select(m => m.Code).Order().ToList());
    }

    [Fact]
    public async Task BuildQueueAsync_WithActorId_IgnoresTheFavoritesExclusion()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int favoriteId, normalId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var favorite = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            var normal = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.AddRange(favorite, normal);
            await db.SaveChangesAsync();
            favoriteId = favorite.Id;
            normalId = normal.Id;

            var favoriteSolo = new Movie { Code = "FAV-SOLO", Status = MovieStatus.Got };
            var normalWithFavorite = new Movie { Code = "NORMAL-WITH-FAV", Status = MovieStatus.Got };
            var normalMetaFavorite = new Movie { Code = "NORMAL-META-FAV", Status = MovieStatus.Got, MetaActresses = "Mikami Yua" };
            db.Movies.AddRange(favoriteSolo, normalWithFavorite, normalMetaFavorite);
            await db.SaveChangesAsync();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = favoriteSolo.Id, ActorId = favorite.Id },
                new MovieActor { MovieId = normalWithFavorite.Id, ActorId = normal.Id },
                new MovieActor { MovieId = normalWithFavorite.Id, ActorId = favorite.Id },
                new MovieActor { MovieId = normalMetaFavorite.Id, ActorId = normal.Id });
            await db.SaveChangesAsync();
        }

        var favoriteQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, null, favoriteId));
        var normalQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, null, normalId));

        Assert.Equal(["FAV-SOLO", "NORMAL-WITH-FAV"], favoriteQueue.Select(m => m.Code).Order().ToList());
        Assert.Equal(["NORMAL-META-FAV", "NORMAL-WITH-FAV"], normalQueue.Select(m => m.Code).Order().ToList());
    }

    [Fact]
    public async Task BuildQueueAsync_WithActorIdAndLibrary_AppliesBothFilters()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var jav = new Movie { Code = "JAV-1", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" };
            var vr = new Movie { Code = "VR-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" };
            var otherActorJav = new Movie { Code = "JAV-OTHER", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" };
            db.Movies.AddRange(jav, vr, otherActorJav);
            await db.SaveChangesAsync();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = jav.Id, ActorId = actor.Id },
                new MovieActor { MovieId = vr.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, "JAV", actorId));

        Assert.Equal(["JAV-1"], queue.Select(m => m.Code).ToList());
    }

    [Fact]
    public async Task GetEligibleLibrariesAsync_WithActorId_OnlyListsLibrariesOfThatActorsMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var mine = new Movie { Code = "MINE-1", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" };
            var notMine = new Movie { Code = "NOT-MINE-1", Status = MovieStatus.Got, JellyfinLibraryName = "VR" };
            db.Movies.AddRange(mine, notMine);
            await db.SaveChangesAsync();

            db.MovieActors.Add(new MovieActor { MovieId = mine.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        Assert.Equal(["JAV"], await service.GetEligibleLibrariesAsync(actorId));
        Assert.Equal(["JAV", "VR"], await service.GetEligibleLibrariesAsync());
    }

    [Fact]
    public void StartSession_WithActorId_StoresTheScopeOnTheSession()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        service.StartSession([], CleanupOrder.Random, null, 42);

        Assert.Equal(42, service.ActiveSession!.ActorId);
    }

    [Fact]
    public async Task ResumeSessionAsync_ScopedSession_PrunesMoviesNoLongerLinkedButKeepsFavoriteActorMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int actorId, keptId, unlinkedId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            var other = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.AddRange(actor, other);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var kept = new Movie { Code = "KEPT-1", Status = MovieStatus.Got };
            var unlinked = new Movie { Code = "UNLINKED-1", Status = MovieStatus.Got };
            db.Movies.AddRange(kept, unlinked);
            await db.SaveChangesAsync();
            keptId = kept.Id;
            unlinkedId = unlinked.Id;

            db.MovieActors.AddRange(
                new MovieActor { MovieId = kept.Id, ActorId = actor.Id },
                new MovieActor { MovieId = unlinked.Id, ActorId = other.Id });
            await db.SaveChangesAsync();
        }

        service.StartSession(
            [keptId, unlinkedId],
            CleanupOrder.Random, null, actorId);

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.Equal(actorId, resumed.ActorId);
        Assert.Equal([keptId], resumed.Queue);
    }

    private static async Task<(int FavoriteId, int FavoriteMovieId, int PlainMovieId)> SeedFavoriteAndPlainMovieAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var favorite = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
        db.Actors.Add(favorite);
        var favoriteMovie = new Movie { Code = "REV-FAV", Status = MovieStatus.Got, JellyfinLibraryName = "JAV" };
        var plainMovie = new Movie { Code = "REV-PLAIN", Status = MovieStatus.Got };
        db.Movies.AddRange(favoriteMovie, plainMovie);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = favoriteMovie.Id, ActorId = favorite.Id });
        await db.SaveChangesAsync();
        return (favorite.Id, favoriteMovie.Id, plainMovie.Id);
    }

    [Fact]
    public async Task BuildQueueAsync_ReviewMode_IncludesMoviesOfFavoriteActors()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        await SeedFavoriteAndPlainMovieAsync(factory);

        var cleanupQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random));
        var reviewQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, mode: CleanupMode.Review));

        Assert.Equal(["REV-PLAIN"], cleanupQueue.Select(m => m.Code).ToList());
        Assert.Equal(["REV-FAV", "REV-PLAIN"], reviewQueue.Select(m => m.Code).Order().ToList());
    }

    [Fact]
    public async Task BuildQueueAsync_ReviewMode_StillExcludesBlacklistedSnoozedAndMissingMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "REV-OK", Status = MovieStatus.Got },
                new Movie { Code = "REV-BLACK", Status = MovieStatus.Got, CleanupBlacklisted = true },
                new Movie { Code = "REV-SNOOZED", Status = MovieStatus.Got, CleanupSnoozedUntil = DateTime.UtcNow.AddDays(10) },
                new Movie { Code = "REV-MISSING", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
        }

        var queue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, mode: CleanupMode.Review));

        Assert.Equal(["REV-OK"], queue.Select(m => m.Code).ToList());
    }

    [Fact]
    public async Task GetEligibleLibrariesAsync_ReviewMode_IncludesLibrariesOfFavoriteActorsMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        await SeedFavoriteAndPlainMovieAsync(factory);

        Assert.Empty(await service.GetEligibleLibrariesAsync());
        Assert.Equal(["JAV"], await service.GetEligibleLibrariesAsync(mode: CleanupMode.Review));
    }

    [Fact]
    public async Task ResumeSessionAsync_ReviewSession_KeepsMoviesOfFavoriteActors()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;
        var (_, favoriteMovieId, plainMovieId) = await SeedFavoriteAndPlainMovieAsync(factory);

        service.StartSession(
            [favoriteMovieId, plainMovieId],
            CleanupOrder.Random, null, mode: CleanupMode.Review);

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.Equal(CleanupMode.Review, resumed.Mode);
        Assert.Equal([favoriteMovieId, plainMovieId], resumed.Queue.Order().ToList());
    }

    [Fact]
    public void StartSession_DefaultsToCleanupMode()
    {
        var tracker = new MovieCleanupSessionTracker();

        Assert.Equal(CleanupMode.Cleanup, tracker.StartSession([], CleanupOrder.Random, null).Mode);
        Assert.Equal(CleanupMode.Review, tracker.StartSession([], CleanupOrder.Random, null, mode: CleanupMode.Review).Mode);
    }

    [Fact]
    public async Task StampReviewedAsync_SetsLastReviewedAtToCurrentUtcTime()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "REV-STAMP-1", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var before = DateTime.UtcNow;
        var result = await service.StampReviewedAsync(movieId);
        var after = DateTime.UtcNow;

        Assert.True(result.Success);
        await using (var readDb = await factory.CreateDbContextAsync())
        {
            var reloaded = await readDb.Movies.SingleAsync(m => m.Id == movieId);
            Assert.NotNull(reloaded.LastReviewedAt);
            Assert.InRange(reloaded.LastReviewedAt!.Value, before, after);
        }
    }

    [Fact]
    public async Task StampReviewedAsync_WhenMovieNotFound_ReturnsFailure()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        var result = await service.StampReviewedAsync(99999);

        Assert.False(result.Success);
        Assert.Equal("Movie not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task BuildQueueAsync_ReviewMode_WithUnreviewedOnly_FiltersOutReviewedMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "REV-UNREVIEWED", Status = MovieStatus.Got, LastReviewedAt = null },
                new Movie { Code = "REV-REVIEWED", Status = MovieStatus.Got, LastReviewedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var unreviewedQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, mode: CleanupMode.Review, unreviewedOnly: true));
        var allQueue = await LoadQueueAsync(factory, await service.BuildQueueAsync(CleanupOrder.Random, mode: CleanupMode.Review, unreviewedOnly: false));

        Assert.Equal(["REV-UNREVIEWED"], unreviewedQueue.Select(m => m.Code).ToList());
        Assert.Equal(["REV-REVIEWED", "REV-UNREVIEWED"], allQueue.Select(m => m.Code).Order().ToList());
    }

    [Fact]
    public void StartSession_StoresUnreviewedOnlyFlag()
    {
        var tracker = new MovieCleanupSessionTracker();

        Assert.False(tracker.StartSession([], CleanupOrder.Random, null, unreviewedOnly: false).UnreviewedOnly);
        Assert.True(tracker.StartSession([], CleanupOrder.Random, null, unreviewedOnly: true).UnreviewedOnly);
    }

    [Fact]
    public async Task ResumeSessionAsync_ReviewSessionWithUnreviewedOnly_PrunesReviewedMovies()
    {
        using var factory = new TestDbContextFactory();
        var (service, root) = CreateService(factory);
        using var _ = root;

        int m1Id;
        int m2Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1 = new Movie { Code = "RES-1", Status = MovieStatus.Got };
            var m2 = new Movie { Code = "RES-2", Status = MovieStatus.Got };
            db.Movies.AddRange(m1, m2);
            await db.SaveChangesAsync();
            m1Id = m1.Id;
            m2Id = m2.Id;
        }

        service.StartSession(
            [m1Id, m2Id],
            CleanupOrder.Random, null, mode: CleanupMode.Review, unreviewedOnly: true);

        // Another tab or action reviewed m1 in the meantime
        await using (var db = await factory.CreateDbContextAsync())
        {
            var m1 = await db.Movies.SingleAsync(m => m.Id == m1Id);
            m1.LastReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var resumed = await service.ResumeSessionAsync();

        Assert.NotNull(resumed);
        Assert.True(resumed.UnreviewedOnly);
        Assert.Equal([m2Id], resumed.Queue);
    }
}
