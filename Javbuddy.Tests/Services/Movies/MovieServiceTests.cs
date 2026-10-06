using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class MovieServiceTests
{
    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public string AddMovieFolder(string folderName)
        {
            var folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static (MovieService Service, NfoSyncService NfoSync) CreateServiceWithRealNfoSync(
        TestDbContextFactory factory, TempRoot root, IJavinizerClient? javinizerClient = null, MovieChangeNotifier? movieChangeNotifier = null)
    {
        var inMemory = new Dictionary<string, string?> { ["LocalLibrary:RootPaths:0"] = root.Path };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var localLibraryClient = new LocalLibraryClient(
            factory,
            config,
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IMediaInfoProber>(),
            NullLogger<LocalLibraryClient>.Instance);
        var nfoSync = new NfoSyncService(factory, localLibraryClient, NullLogger<NfoSyncService>.Instance);
        return (new MovieService(factory, movieChangeNotifier, nfoSync, javinizerClient), nfoSync);
    }

    private static MovieViewDto CreateScrapedMovie() => new()
    {
        Id = "ABC-100",
        Title = "New Title",
        OriginalTitle = "New Original Title",
        Description = "New overview.",
        ReleaseDate = new DateTime(2024, 6, 1),
        Director = "New Director",
        Maker = "New Studio",
        Label = "New Label",
        Series = "New Series",
        Runtime = 130,
        CoverUrl = "https://example.com/new-cover.jpg",
        Actresses = [new ActressViewDto { FirstName = "Yua", LastName = "Mikami" }],
        Genres = [new GenreViewDto { Name = "Drama" }]
    };
    [Theory]
    [InlineData(MovieStatus.Missing, MovieStatus.Got)]
    [InlineData(MovieStatus.Got, MovieStatus.Missing)]
    public async Task ToggleStatusAsync_FlipsStatus(MovieStatus initial, MovieStatus expected)
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = initial };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        await service.ToggleStatusAsync(movieId);

        await using var readDb = await factory.CreateDbContextAsync();
        var reloaded = await readDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(expected, reloaded.Status);
    }

    [Fact]
    public async Task SetPrimaryFileAsync_PinsTheVersion_AndSyncsItOntoTheMovie()
    {
        using var factory = new TestDbContextFactory();
        int movieId, originalId, rifeId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got, MediaVideoFileName = "ABC-123.mp4", MediaFrameRate = 30 };
            var original = new MovieFile { FileName = "ABC-123.mp4", IsPrimary = true, FrameRate = 30, FileSizeBytes = 100 };
            var rife = new MovieFile { FileName = "ABC-123-RIFE.mkv", VersionTag = "RIFE", FrameRate = 60, FileSizeBytes = 300 };
            movie.MovieFiles.Add(original);
            movie.MovieFiles.Add(rife);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            (movieId, originalId, rifeId) = (movie.Id, original.Id, rife.Id);
        }

        var trickplayTrigger = Substitute.For<Javbuddy.Services.Trickplay.ITrickplayTrigger>();
        var result = await new MovieService(factory, trickplayTrigger: trickplayTrigger).SetPrimaryFileAsync(movieId, rifeId);

        Assert.True(result.Success);
        await trickplayTrigger.Received(1).OnVideoFilesChangedAsync(movieId, Arg.Any<CancellationToken>());
        await using var readDb = await factory.CreateDbContextAsync();
        var reloaded = await readDb.Movies.Include(m => m.MovieFiles).SingleAsync(m => m.Id == movieId);
        var files = reloaded.MovieFiles.ToDictionary(f => f.Id);
        Assert.True(files[rifeId].IsPrimary);
        Assert.True(files[rifeId].IsPrimaryPinned);
        Assert.False(files[originalId].IsPrimary);
        Assert.False(files[originalId].IsPrimaryPinned);
        Assert.Equal("ABC-123-RIFE.mkv", reloaded.MediaVideoFileName);
        Assert.Equal(60, reloaded.MediaFrameRate);
        Assert.Equal(400, reloaded.LocalFileSizeBytes);
    }

    [Fact]
    public async Task SetPrimaryFileAsync_Fails_ForAnotherMoviesFile()
    {
        using var factory = new TestDbContextFactory();
        int movieId, otherFileId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Got };
            movie.MovieFiles.Add(new MovieFile { FileName = "ABC-123.mp4", IsPrimary = true });
            var other = new Movie { Code = "XYZ-999", Status = MovieStatus.Got };
            var otherFile = new MovieFile { FileName = "XYZ-999.mp4", IsPrimary = true };
            other.MovieFiles.Add(otherFile);
            db.Movies.AddRange(movie, other);
            await db.SaveChangesAsync();
            (movieId, otherFileId) = (movie.Id, otherFile.Id);
        }

        var result = await new MovieService(factory).SetPrimaryFileAsync(movieId, otherFileId);

        Assert.False(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await readDb.MovieFiles.Where(f => f.Id == otherFileId).Select(f => f.IsPrimary).SingleAsync());
        Assert.False(await readDb.MovieFiles.AnyAsync(f => f.IsPrimaryPinned));
    }

    [Fact]
    public async Task ToggleStatusAsync_UnknownId_DoesNothing()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieService(factory);

        await service.ToggleStatusAsync(999);
        // No exception is the assertion — nothing to reload.
    }

    [Fact]
    public async Task ToggleFavoriteAsync_FavoritesThenUnfavorites()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notifications = 0;
        notifier.Changed += () => notifications++;
        var service = new MovieService(factory, notifier);

        Assert.True(await service.ToggleFavoriteAsync(movieId));
        await using (var readDb = await factory.CreateDbContextAsync())
        {
            var favorited = await readDb.Movies.SingleAsync(m => m.Id == movieId);
            Assert.True(favorited.IsFavorite);
            Assert.NotNull(favorited.FavoritedAt);
        }

        Assert.False(await service.ToggleFavoriteAsync(movieId));
        await using (var readDb = await factory.CreateDbContextAsync())
        {
            var unfavorited = await readDb.Movies.SingleAsync(m => m.Id == movieId);
            Assert.False(unfavorited.IsFavorite);
            Assert.Null(unfavorited.FavoritedAt);
        }

        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_UnknownId_ReturnsFalse()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieService(factory);

        Assert.False(await service.ToggleFavoriteAsync(999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheMovie()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        await service.DeleteAsync(movieId);

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync(m => m.Id == movieId));
    }

    [Fact]
    public async Task DeleteAsync_PreservesHistoryAndRepeatedDeletionUpdatesOneRow()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieService(factory);
        var before = DateTime.UtcNow;
        for (var i = 0; i < 2; i++)
        {
            var movie = new Movie { Code = "abc-123", Title = $"Title {i}", MetaTitle = $"Metadata {i}", Status = MovieStatus.Got };
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Movies.Add(movie);
                await db.SaveChangesAsync();
            }
            await service.DeleteAsync(movie.Id);
            await service.DeleteAsync(movie.Id);
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.Empty(await readDb.Movies.ToListAsync());
        var history = await readDb.DeletedMovies.SingleAsync();
        Assert.Equal("abc-123", history.Code);
        Assert.Equal("ABC123", history.NormalizedCode);
        Assert.Equal(CodeNormalization.GetCanonicalKey(history.Code), history.CanonicalKey);
        Assert.Equal("Title 1", history.Title);
        Assert.Equal("Metadata 1", history.MetaTitle);
        Assert.Equal(MovieStatus.Got, history.PreviousStatus);
        Assert.InRange(history.DeletedAt, before, DateTime.UtcNow);
    }

    private static MovieService CreateServiceWithLocalLibrary(TestDbContextFactory factory, string rootPath)
    {
        var inMemory = new Dictionary<string, string?> { ["LocalLibrary:RootPaths:0"] = rootPath };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var localLibraryClient = new LocalLibraryClient(
            factory,
            config,
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IMediaInfoProber>(),
            NullLogger<LocalLibraryClient>.Instance);
        return new MovieService(factory, localLibraryClient: localLibraryClient);
    }

    private static async Task<int> AddMovieAsync(TestDbContextFactory factory, string code)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, Status = MovieStatus.Got };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task DeleteAsync_WithoutDeleteFiles_LeavesTheFolderOnDisk()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        File.WriteAllText(Path.Combine(folder, "ABC-123.mp4"), "video");
        var movieId = await AddMovieAsync(factory, "ABC-123");

        var result = await CreateServiceWithLocalLibrary(factory, root.Path).DeleteAsync(movieId);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(folder, "ABC-123.mp4")));
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithDeleteFiles_DeletesTheFolderAndTheRecord()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        File.WriteAllText(Path.Combine(folder, "ABC-123.mp4"), "video");
        Directory.CreateDirectory(Path.Combine(folder, "extrafanart"));
        File.WriteAllText(Path.Combine(folder, "extrafanart", "fanart1.jpg"), "img");
        var otherFolder = root.AddMovieFolder("ABC-124");
        var movieId = await AddMovieAsync(factory, "ABC-123");

        var result = await CreateServiceWithLocalLibrary(factory, root.Path).DeleteAsync(movieId, deleteFiles: true);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(otherFolder));
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync());
        Assert.Equal("ABC-123", (await readDb.DeletedMovies.SingleAsync()).Code);
    }

    [Fact]
    public async Task DeleteAsync_WithDeleteFiles_AndNoLocalFolder_StillDeletesTheRecord()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var movieId = await AddMovieAsync(factory, "ABC-123");

        var result = await CreateServiceWithLocalLibrary(factory, root.Path).DeleteAsync(movieId, deleteFiles: true);

        Assert.True(result.Success);
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await readDb.Movies.AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithDeleteFiles_RefusesAFolderOutsideTheLibraryRoots()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        using var elsewhere = new TempRoot();
        var folder = elsewhere.AddMovieFolder("ABC-123");
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(folder);
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([root.Path]);
        var movieId = await AddMovieAsync(factory, "ABC-123");
        var service = new MovieService(factory, localLibraryClient: localLibraryClient);

        var result = await service.DeleteAsync(movieId, deleteFiles: true);
        var preview = await service.GetFolderDeletePreviewAsync(movieId);

        Assert.False(result.Success);
        Assert.Contains("isn't a folder directly inside a configured local library root", result.ErrorMessage);
        Assert.True(Directory.Exists(folder));
        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await readDb.Movies.AnyAsync(m => m.Id == movieId));
        Assert.Null(preview.FolderPath);
        Assert.NotNull(preview.ErrorMessage);
    }

    [Fact]
    public async Task GetFolderDeletePreviewAsync_ListsEveryFileRecursivelyWithTotalSize()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var folder = root.AddMovieFolder("ABC-123");
        File.WriteAllText(Path.Combine(folder, "ABC-123.mp4"), "12345");
        File.WriteAllText(Path.Combine(folder, "ABC-123.nfo"), "123");
        Directory.CreateDirectory(Path.Combine(folder, ".actors"));
        File.WriteAllText(Path.Combine(folder, ".actors", "Yua_Mikami.jpg"), "12");
        var movieId = await AddMovieAsync(factory, "ABC-123");

        var preview = await CreateServiceWithLocalLibrary(factory, root.Path).GetFolderDeletePreviewAsync(movieId);

        Assert.Null(preview.ErrorMessage);
        Assert.Equal(folder, preview.FolderPath);
        Assert.Equal(
            [Path.Combine(".actors", "Yua_Mikami.jpg"), "ABC-123.mp4", "ABC-123.nfo"],
            preview.Files.Select(f => f.RelativePath));
        Assert.Equal(10, preview.TotalBytes);
    }

    [Fact]
    public async Task GetFolderDeletePreviewAsync_NoLocalFolder_ReturnsEmptyPreview()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var movieId = await AddMovieAsync(factory, "ABC-123");

        var preview = await CreateServiceWithLocalLibrary(factory, root.Path).GetFolderDeletePreviewAsync(movieId);

        Assert.Null(preview.FolderPath);
        Assert.Null(preview.ErrorMessage);
        Assert.Empty(preview.Files);
    }

    [Fact]
    public async Task LinkJellyfinItemAsync_AppliesMatchAndMarksGot()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var item = new JellyfinItemDto { Id = "item-1", ServerId = "server-1", LibraryId = "lib-1", LibraryName = "Movies" };
        await service.LinkJellyfinItemAsync(movieId, item);

        await using var readDb = await factory.CreateDbContextAsync();
        var reloaded = await readDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(MovieStatus.Got, reloaded.Status);
        Assert.Equal("item-1", reloaded.JellyfinItemId);
        Assert.Equal("server-1", reloaded.JellyfinServerId);
        Assert.Equal("lib-1", reloaded.JellyfinLibraryId);
        Assert.Equal("Movies", reloaded.JellyfinLibraryName);
    }

    [Fact]
    public async Task ToggleStatusAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        await service.ToggleStatusAsync(movieId);

        Assert.True(notified);
    }

    [Fact]
    public async Task DeleteAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        await service.DeleteAsync(movieId);

        Assert.True(notified);
    }

    [Fact]
    public async Task LinkJellyfinItemAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        var item = new JellyfinItemDto { Id = "item-1", ServerId = "server-1", LibraryId = "lib-1", LibraryName = "Movies" };
        await service.LinkJellyfinItemAsync(movieId, item);

        Assert.True(notified);
    }

    [Fact]
    public async Task AddActorToMovieAsync_LinksActorAndUpdatesMetaActresses()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.AddActorToMovieAsync(movieId, actorId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.True(await verifyDb.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId));
        var reloaded = await verifyDb.Movies.FindAsync(movieId);
        Assert.Equal("Mikami Yua", reloaded!.MetaActresses);
        Assert.False(reloaded.HasUnmatchedActors);
    }

    [Fact]
    public async Task AddActorToMovieAsync_RejectsAlreadyLinkedActor()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Mikami Yua" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.AddActorToMovieAsync(movieId, actorId);

        Assert.False(result.Success);
        Assert.Contains("already on this movie", result.ErrorMessage);
    }

    [Fact]
    public async Task AddActorToMovieAsync_CorrectsAnUnmatchedNameAlreadyInMetaActresses()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Mikami Yua", HasUnmatchedActors = true, UnmatchedActorNames = "Mikami Yua" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.AddActorToMovieAsync(movieId, actorId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.FindAsync(movieId);
        Assert.False(reloaded!.HasUnmatchedActors);
        Assert.Null(reloaded.UnmatchedActorNames);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_UnlinksActorAndUpdatesMetaActresses()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var mikami = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var hashimoto = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Mikami Yua, Hashimoto Arina" };
            db.AddRange(mikami, hashimoto, movie);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = mikami.Id });
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = hashimoto.Id });
            await db.SaveChangesAsync();
            actorId = mikami.Id;
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, actorId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.False(await verifyDb.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId));
        var reloaded = await verifyDb.Movies.FindAsync(movieId);
        Assert.Equal("Hashimoto Arina", reloaded!.MetaActresses);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_RejectsWhenNotLinked()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, actorId);

        Assert.False(result.Success);
        Assert.Contains("not on this movie", result.ErrorMessage);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_ByName_RemovesUnmatchedActorAndUpdatesMetaActresses()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-100",
                MetaActresses = "Untracked One, Untracked Two",
                HasUnmatchedActors = true,
                UnmatchedActorNames = "Untracked One, Untracked Two"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, "Untracked One");

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.FindAsync(movieId);
        Assert.Equal("Untracked Two", reloaded!.MetaActresses);
        Assert.True(reloaded.HasUnmatchedActors);
        Assert.Equal("Untracked Two", reloaded.UnmatchedActorNames);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_ByName_RemovesLastActorAndClearsMetaActressesAndUnmatched()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "ABC-100",
                MetaActresses = "Untracked One",
                HasUnmatchedActors = true,
                UnmatchedActorNames = "Untracked One"
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, "Untracked One");

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.FindAsync(movieId);
        Assert.Null(reloaded!.MetaActresses);
        Assert.False(reloaded.HasUnmatchedActors);
        Assert.Null(reloaded.UnmatchedActorNames);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_ByName_RejectsWhenActorNotOnMovie()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Untracked One" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, "Nonexistent");

        Assert.False(result.Success);
        Assert.Contains("not on this movie", result.ErrorMessage);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_ByName_RejectsWhenNameNullOrWhitespace()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Untracked One" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.RemoveActorFromMovieAsync(movieId, "   ");

        Assert.False(result.Success);
        Assert.Contains("required", result.ErrorMessage);
    }

    [Fact]
    public async Task RemoveActorFromMovieAsync_ByName_NotifiesChangeAndChecksNfoConflict()
    {
        using var factory = new TestDbContextFactory();
        var notifier = new MovieChangeNotifier();
        var nfoSync = Substitute.For<INfoSyncService>();
        bool notified = false;
        notifier.Changed += () => notified = true;

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", MetaActresses = "Untracked One" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory, movieChangeNotifier: notifier, nfoSyncService: nfoSync);
        var result = await service.RemoveActorFromMovieAsync(movieId, "Untracked One");

        Assert.True(result.Success);
        Assert.True(notified);
        await nfoSync.Received(1).CheckMovieNfoConflictAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddActorToMovieAsync_ImmediatelyFlagsNfoConflict_WithoutWaitingForRescan()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("ACT-001");
        await File.WriteAllTextAsync(Path.Combine(folder, "ACT-001.nfo"), "<movie><actor><name>Hashimoto Arina</name></actor></movie>");

        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ACT-001", Status = MovieStatus.Got };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var result = await service.AddActorToMovieAsync(movieId, actorId);
        Assert.True(result.Success);

        // Linking Yua Mikami makes the canonical cast ["Yua Mikami"] vs. the .nfo's
        // ["Hashimoto Arina"] — a real mismatch, expected to be flagged immediately rather than on
        // the next rescan.
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloadedMovie = await verifyDb.Movies.FindAsync(movieId);
        Assert.NotNull(reloadedMovie);
        Assert.NotEqual(NfoDriftKind.None, reloadedMovie!.NfoDriftKind);
    }

    [Fact]
    public async Task AddActorToMovieAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        await service.AddActorToMovieAsync(movieId, actorId);

        Assert.True(notified);
    }

    [Fact]
    public async Task UpdateMetadataAsync_UpdatesAllFields()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var releaseDate = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var update = new MovieMetadataUpdate(
            Title: " Manual Title ",
            MetaTitle: "Display Title",
            MetaOriginalTitle: "Original Title",
            MetaReleaseDate: releaseDate,
            MetaStudio: "Studio",
            MetaLabel: "Label",
            MetaSeries: "Series",
            MetaDirector: "Director",
            MetaRuntimeMinutes: 120,
            MetaDescription: "Overview text.");

        var service = new MovieService(factory);
        var result = await service.UpdateMetadataAsync(movieId, update);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Manual Title", reloaded.Title);
        Assert.Equal("Display Title", reloaded.MetaTitle);
        Assert.Equal("Original Title", reloaded.MetaOriginalTitle);
        Assert.Equal(releaseDate, reloaded.MetaReleaseDate);
        Assert.Equal("Studio", reloaded.MetaStudio);
        Assert.Equal("Label", reloaded.MetaLabel);
        Assert.Equal("Series", reloaded.MetaSeries);
        Assert.Equal("Director", reloaded.MetaDirector);
        Assert.Equal(120, reloaded.MetaRuntimeMinutes);
        Assert.Equal("Overview text.", reloaded.MetaDescription);
    }

    [Fact]
    public async Task UpdateMetadataAsync_BlankStringsAreStoredAsNull()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", MetaStudio = "Old Studio", Notes = "Old notes" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var update = new MovieMetadataUpdate(null, null, null, null, "   ", null, null, null, null, null);

        var service = new MovieService(factory);
        var result = await service.UpdateMetadataAsync(movieId, update);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Null(reloaded.MetaStudio);
        // Notes isn't part of the metadata editor — saving leaves it as it was.
        Assert.Equal("Old notes", reloaded.Notes);
    }

    [Fact]
    public async Task UpdateMetadataAsync_RejectsNegativeRuntime()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var update = new MovieMetadataUpdate(null, null, null, null, null, null, null, null, -5, null);

        var service = new MovieService(factory);
        var result = await service.UpdateMetadataAsync(movieId, update);

        Assert.False(result.Success);
        Assert.Contains("Runtime", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateMetadataAsync_UnknownMovie_Fails()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieService(factory);

        var update = new MovieMetadataUpdate(null, null, null, null, null, null, null, null, null, null);
        var result = await service.UpdateMetadataAsync(999, update);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task UpdateMetadataAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        var update = new MovieMetadataUpdate(null, null, null, null, null, null, null, null, null, null);
        await service.UpdateMetadataAsync(movieId, update);

        Assert.True(notified);
    }

    [Fact]
    public async Task UpdateMetadataAsync_NeverWritesToDisk_ButFlagsConflictForAnyFieldThatNowDiffers()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        // The metadata editor modal only ever updates the DB — Save must never
        // touch the .nfo file itself, even for a field whose element already has a value.
        var folder = root.AddMovieFolder("MET-001");
        var nfoPath = Path.Combine(folder, "MET-001.nfo");
        await File.WriteAllTextAsync(nfoPath, """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Old Title</title>
              <runtime>90</runtime>
              <releasedate>2020-01-01</releasedate>
            </movie>
            """);
        var originalXml = await File.ReadAllTextAsync(nfoPath);

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "MET-001", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var update = new MovieMetadataUpdate(
            Title: "Javbuddy-only manual title",
            MetaTitle: "New Title",
            MetaOriginalTitle: null,
            MetaReleaseDate: new DateTime(2024, 6, 1),
            MetaStudio: null,
            MetaLabel: null,
            MetaSeries: null,
            MetaDirector: null,
            MetaRuntimeMinutes: 105,
            MetaDescription: null);

        var result = await service.UpdateMetadataAsync(movieId, update);
        Assert.True(result.Success);

        Assert.Equal(originalXml, await File.ReadAllTextAsync(nfoPath));

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Null(reloaded.MediaNfoLastWriteUtc);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Title: .nfo has 'Old Title', canonical is 'New Title'", reloaded.NfoConflictDetails);
        Assert.Contains("Release date: .nfo has '2020-01-01', canonical is '2024-06-01'", reloaded.NfoConflictDetails);
        Assert.Contains("Runtime: .nfo has '90' minutes, canonical is '105' minutes", reloaded.NfoConflictDetails);
        // Title has no .nfo counterpart at all — never mentioned in a conflict message.
        Assert.DoesNotContain("Javbuddy-only manual title", reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task UpdateMetadataAsync_NoLocalNfo_StillUpdatesDatabase()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "MET-002", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var update = new MovieMetadataUpdate(null, "New Title", null, null, null, null, null, null, null, null);

        var result = await service.UpdateMetadataAsync(movieId, update);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("New Title", reloaded.MetaTitle);
    }

    [Fact]
    public async Task UpdateMetadataAsync_EditedFieldHasNoExistingNfoElementValue_FlagsNfoConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        // <director></director> exists but is blank. The modal never writes to the .nfo, so
        // this must surface as a conflict rather than silently vanish.
        var folder = root.AddMovieFolder("MET-003");
        await File.WriteAllTextAsync(Path.Combine(folder, "MET-003.nfo"), "<movie><director></director></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "MET-003", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var update = new MovieMetadataUpdate(null, null, null, null, null, null, null, "New Director", null, null);

        var result = await service.UpdateMetadataAsync(movieId, update);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("New Director", reloaded.MetaDirector);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Director", reloaded.NfoConflictDetails);

        // The .nfo itself was never touched.
        var nfoXml = await File.ReadAllTextAsync(Path.Combine(folder, "MET-003.nfo"));
        Assert.Equal("<movie><director></director></movie>", nfoXml);
    }

    [Fact]
    public async Task UpdateMetadataAsync_FieldWithNoNfoElementAtAll_FlagsNfoConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("MET-004");
        await File.WriteAllTextAsync(Path.Combine(folder, "MET-004.nfo"), "<movie><title>T</title></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "MET-004", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var update = new MovieMetadataUpdate(null, null, null, null, null, "New Label", null, null, null, null);

        var result = await service.UpdateMetadataAsync(movieId, update);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Label", reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task UpdateMetadataAsync_FieldNowMatchesNfo_ClearsPriorConflictForThatField()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("MET-005");
        await File.WriteAllTextAsync(Path.Combine(folder, "MET-005.nfo"), "<movie><label>Matching</label></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie
            {
                Code = "MET-005",
                Status = MovieStatus.Got,
                MetaLabel = "Stale",
                NfoDriftKind = NfoDriftKind.ExternalEdit,
                NfoConflictDetails = "Label: .nfo has 'Matching', canonical is 'Stale'",
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var update = new MovieMetadataUpdate(null, null, null, null, null, "Matching", null, null, null, null);

        var result = await service.UpdateMetadataAsync(movieId, update);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Null(reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task UpdateMetadataAsync_PreservesUnrelatedActorConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("MET-006");
        await File.WriteAllTextAsync(Path.Combine(folder, "MET-006.nfo"), "<movie><title>Old</title><actor><name>Someone Else</name></actor></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie
            {
                Code = "MET-006",
                Status = MovieStatus.Got,
                MetaActresses = "Mikami Yua",
                NfoDriftKind = NfoDriftKind.ExternalEdit,
                NfoConflictDetails = "pre-existing actor drift",
            };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        // Title now matches the .nfo's existing (non-blank) value, so it contributes no new
        // conflict — but the real, pre-existing actor mismatch (Yua Mikami vs. "Someone Else")
        // must still be recomputed and reported, not silently dropped.
        var update = new MovieMetadataUpdate(null, "Old", null, null, null, null, null, null, null, null);

        var result = await service.UpdateMetadataAsync(movieId, update);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Someone Else", reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task FetchJavinizerMetadataAsync_NotConfigured_Fails()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var service = new MovieService(factory);
        var result = await service.FetchJavinizerMetadataAsync(movieId);

        Assert.False(result.Success);
        Assert.Null(result.Scraped);
    }

    [Fact]
    public async Task FetchJavinizerMetadataAsync_UnknownMovie_Fails()
    {
        using var factory = new TestDbContextFactory();
        var javinizerClient = Substitute.For<IJavinizerClient>();
        var service = new MovieService(factory, javinizerClient: javinizerClient);

        var result = await service.FetchJavinizerMetadataAsync(999);

        Assert.False(result.Success);
        Assert.Equal("Movie not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchJavinizerMetadataAsync_ScrapeSucceeds_ReturnsScrapedMetadataWithoutTouchingTheDb()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = CreateScrapedMovie();
        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.ScrapeAsync("ABC-100", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(true, scraped, null));

        var service = new MovieService(factory, javinizerClient: javinizerClient);
        var result = await service.FetchJavinizerMetadataAsync(movieId);

        Assert.True(result.Success);
        Assert.Same(scraped, result.Scraped);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Null(reloaded.MetaTitle);
    }

    [Fact]
    public async Task FetchJavinizerMetadataAsync_ScrapeFails_ReturnsError()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.ScrapeAsync("ABC-100", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(false, null, "code not found"));

        var service = new MovieService(factory, javinizerClient: javinizerClient);
        var result = await service.FetchJavinizerMetadataAsync(movieId);

        Assert.False(result.Success);
        Assert.Equal("code not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_AppliesOnlySelectedFields()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", MetaStudio = "Old Studio", MetaDirector = "Old Director" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = CreateScrapedMovie();
        var options = new MovieMetadataImportOptions { ImportTitle = true, ImportStudio = true };

        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, options);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("ABC-100 New Title", reloaded.MetaTitle);
        Assert.Equal("New Studio", reloaded.MetaStudio);
        // Not selected — left untouched even though the scrape carried a different value.
        Assert.Equal("Old Director", reloaded.MetaDirector);
        Assert.Null(reloaded.MetaOriginalTitle);
        Assert.Null(reloaded.MetaGenres);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_ImportActresses_SynchronizesMovieActorAssociation()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = CreateScrapedMovie();
        var options = new MovieMetadataImportOptions { ImportActresses = true };

        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, options);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("Mikami Yua", reloaded.MetaActresses);
        Assert.True(await verifyDb.MovieActors.AnyAsync(ma => ma.MovieId == movieId));
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_ActressesNotSelected_DoesNotSynchronize()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "ABC-100" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = CreateScrapedMovie();
        var options = new MovieMetadataImportOptions { ImportTitle = true };

        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, options);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Null(reloaded.MetaActresses);
        Assert.False(await verifyDb.MovieActors.AnyAsync(ma => ma.MovieId == movieId));
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_ImportCover_SetsCoverAndBackdrop()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = CreateScrapedMovie();
        var options = new MovieMetadataImportOptions { ImportCover = true };

        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, options);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("https://example.com/new-cover.jpg", reloaded.MetaCoverUrl);
        Assert.Equal("https://example.com/new-cover.jpg", reloaded.MetaBackdropUrl);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_UnknownMovie_Fails()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieService(factory);

        var result = await service.ApplyJavinizerMetadataAsync(999, CreateScrapedMovie(), new MovieMetadataImportOptions());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var service = new MovieService(factory, notifier);
        await service.ApplyJavinizerMetadataAsync(movieId, CreateScrapedMovie(), new MovieMetadataImportOptions { ImportTitle = true });

        Assert.True(notified);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_ExistingNfoDiffers_FlagsNfoConflictWithoutModifyingDisk()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("ABC-100");
        var nfoPath = Path.Combine(folder, "ABC-100.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>Old Title</title></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var options = new MovieMetadataImportOptions { ImportTitle = true };

        var result = await service.ApplyJavinizerMetadataAsync(movieId, CreateScrapedMovie(), options);
        Assert.True(result.Success);

        // The .nfo file on disk was never touched.
        var nfoXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Equal("<movie><title>Old Title</title></movie>", nfoXml);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("ABC-100 New Title", reloaded.MetaTitle);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Title: .nfo has 'Old Title', canonical is 'ABC-100 New Title'", reloaded.NfoConflictDetails);
        Assert.Null(reloaded.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_FieldMissingFromNfo_FlagsStrictConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("ABC-100");
        var nfoPath = Path.Combine(folder, "ABC-100.nfo");
        // NFO only has title matching the canonical title; director element is absent entirely.
        await File.WriteAllTextAsync(nfoPath, "<movie><title>ABC-100 New Title</title></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var options = new MovieMetadataImportOptions { ImportTitle = true, ImportDirector = true };

        var result = await service.ApplyJavinizerMetadataAsync(movieId, CreateScrapedMovie(), options);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Director: .nfo has '(none)', canonical is 'New Director'", reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_MatchesExistingNfo_DoesNotFlagConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        var folder = root.AddMovieFolder("ABC-100");
        var nfoPath = Path.Combine(folder, "ABC-100.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>ABC-100 New Title</title></movie>");

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var options = new MovieMetadataImportOptions { ImportTitle = true };

        var result = await service.ApplyJavinizerMetadataAsync(movieId, CreateScrapedMovie(), options);
        Assert.True(result.Success);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Null(reloaded.NfoConflictDetails);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_NoLocalNfo_StillUpdatesDatabase()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-100", Status = MovieStatus.Missing };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var (service, _) = CreateServiceWithRealNfoSync(factory, root);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, CreateScrapedMovie(), new MovieMetadataImportOptions { ImportTitle = true });

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("ABC-100 New Title", reloaded.MetaTitle);
        Assert.Null(reloaded.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_PrependsDvdIdToTitle()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "XYZ-999" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = new MovieViewDto { Id = "XYZ-999", Title = "Clean Movie Title" };
        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, new MovieMetadataImportOptions { ImportTitle = true });

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("XYZ-999 Clean Movie Title", reloaded.MetaTitle);
    }

    [Fact]
    public async Task ApplyJavinizerMetadataAsync_WhenScrapedTitleAlreadyHasDvdId_AvoidsDuplicatePrepending()
    {
        using var factory = new TestDbContextFactory();
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "XYZ-999" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var scraped = new MovieViewDto { Id = "XYZ-999", Title = "XYZ-999 Already Prefixed Title" };
        var service = new MovieService(factory);
        var result = await service.ApplyJavinizerMetadataAsync(movieId, scraped, new MovieMetadataImportOptions { ImportTitle = true });

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal("XYZ-999 Already Prefixed Title", reloaded.MetaTitle);
    }

    private static async Task<(int MovieId, int PrimaryId, int OtherId)> SeedVrMovieAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "SIVR-059", Status = MovieStatus.Got, VrType = "VR180 SBS" };
        var primary = new MovieFile { FileName = "SIVR-059.mp4", IsPrimary = true, Width = 3840, Height = 1920, VrType = "VR180 SBS" };
        var other = new MovieFile { FileName = "SIVR-059-2D.mp4", VersionTag = "2D", Width = 1920, Height = 1080 };
        movie.MovieFiles.Add(primary);
        movie.MovieFiles.Add(other);
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return (movie.Id, primary.Id, other.Id);
    }

    [Fact]
    public async Task SetVrTypeAsync_PinsTheChosenFormat_AndThePrimarysBecomesTheMovies()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, primaryId, otherId) = await SeedVrMovieAsync(factory);
        var service = new MovieService(factory);

        Assert.True((await service.SetVrTypeAsync(movieId, primaryId, "VR180 TB")).Success);
        Assert.True((await service.SetVrTypeAsync(movieId, otherId, null)).Success);

        await using var readDb = await factory.CreateDbContextAsync();
        var movie = await readDb.Movies.Include(m => m.MovieFiles).SingleAsync(m => m.Id == movieId);
        var files = movie.MovieFiles.ToDictionary(f => f.Id);
        Assert.Equal(("VR180 TB", true), (files[primaryId].VrType, files[primaryId].VrTypePinned));
        Assert.Equal((null, true), (files[otherId].VrType, files[otherId].VrTypePinned));
        Assert.Equal("VR180 TB", movie.VrType);
    }

    [Fact]
    public async Task SetVrTypeAsync_Detect_UnpinsAndDetectsAgain()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, primaryId, _) = await SeedVrMovieAsync(factory);
        var service = new MovieService(factory);
        await service.SetVrTypeAsync(movieId, primaryId, null);

        Assert.True((await service.SetVrTypeAsync(movieId, primaryId, "ignored", detect: true)).Success);

        await using var readDb = await factory.CreateDbContextAsync();
        var movie = await readDb.Movies.Include(m => m.MovieFiles).SingleAsync(m => m.Id == movieId);
        var primary = movie.MovieFiles.Single(f => f.Id == primaryId);
        Assert.Equal(("VR180 SBS", false), (primary.VrType, primary.VrTypePinned));
        Assert.Equal("VR180 SBS", movie.VrType);
    }

    [Fact]
    public async Task SetVrTypeAsync_RejectsAnUnknownFormat_AndAnotherMoviesFile()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, primaryId, _) = await SeedVrMovieAsync(factory);
        var service = new MovieService(factory);

        Assert.False((await service.SetVrTypeAsync(movieId, primaryId, "VR720")).Success);
        Assert.False((await service.SetVrTypeAsync(movieId + 1, primaryId, "VR")).Success);
    }
}
