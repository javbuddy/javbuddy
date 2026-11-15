using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Statistics;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

/// <summary>How unidentified placeholders meet the rest of the app: what leaves them out, what cleans them up, and the
/// edges of merging one.</summary>
public class ActorPlaceholderIntegrationTests
{
    private static async Task<(int MovieId, int PlaceholderId)> SeedAsync(TestDbContextFactory factory, string code = "ABC-123", string? label = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        var placeholder = (await new ActorService(factory).CreatePlaceholderAsync(movie.Id, label)).Actor!;
        return (movie.Id, placeholder.Id);
    }

    [Fact]
    public async Task SearchAsync_NeverFindsAPlaceholder()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory, label: "Redhead");

        var result = await new SearchService(factory).SearchAsync("Redhead");

        Assert.Empty(result.Actors);
    }

    [Fact]
    public async Task Statistics_LeavePlaceholdersOutOfTheActorCountsAndTopActors()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var real = new Actor { FirstName = "Yui", LastName = "Hatano" };
            var movie = await db.Movies.SingleAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, Actor = real });
            await db.SaveChangesAsync();
        }
        var library = Substitute.For<ILocalLibraryClient>();
        library.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());

        var stats = await new LibraryStatisticsService(factory, library, new LibraryStatisticsCache()).GetAsync();

        Assert.Equal(1, stats.Actors);
        Assert.Equal(["Hatano Yui"], stats.TopActors.Select(a => a.Label));
    }

    [Fact]
    public async Task TheSceneWallsActorFilter_LeavesAPlaceholderOut_ButTheirClipStaysOnTheWall()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var scene = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 60 };
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();
            db.SceneActors.Add(new SceneActor { SceneId = scene.Id, MovieId = movieId, ActorId = placeholderId });
            await db.SaveChangesAsync();
        }
        var wall = new RefreshingSceneWall(factory, new SceneWallQueryService(factory, new MovieSceneService(factory)));

        var options = await wall.GetOptionsAsync(includeHidden: true);

        Assert.Empty(options.Actors);
    }

    [Fact]
    public async Task ASceneFormsCastChoices_IncludeThePlaceholder()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory, label: "Redhead");

        var cast = await new MovieSceneService(factory).GetCastOptionsAsync(movieId);

        var entry = Assert.Single(cast);
        Assert.Equal(placeholderId, entry.ActorId);
        Assert.Equal("Redhead (ABC-123)", entry.Name);
    }

    [Fact]
    public async Task DeletingAMovie_RemovesItsPlaceholdersPortraitsAndPhotos()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory);
        var images = Substitute.For<IActorImageCacheService>();
        var photos = Substitute.For<IActorPhotoService>();
        var service = new MovieService(factory, actorImageCache: images, actorPhotos: photos);

        var result = await service.DeleteAsync(movieId);

        Assert.True(result.Success);
        await images.Received(1).DeleteAllForActorAsync(placeholderId, Arg.Any<CancellationToken>());
        await photos.Received(1).DeleteAllForActorAsync(placeholderId, Arg.Any<CancellationToken>());
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Actors.ToListAsync());
    }

    [Fact]
    public async Task CreatePlaceholderAsync_CutsALongLabelTo100Characters()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            await db.SaveChangesAsync();
        }

        var result = await new ActorService(factory).CreatePlaceholderAsync(1, new string('x', 300));

        Assert.Equal(100, result.Actor!.FirstName!.Length);
    }

    [Fact]
    public async Task MergeAsync_IntoAnActorAlreadyOnTheMovie_KeepsOneLinkAndBothTagSets()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory);
        int targetId, tagA, tagB;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            var a = new Tag { Name = "Blonde", IsActorTag = true };
            var b = new Tag { Name = "Tall", IsActorTag = true };
            db.AddRange(target, a, b);
            await db.SaveChangesAsync();
            (targetId, tagA, tagB) = (target.Id, a.Id, b.Id);
            (await db.Movies.FindAsync(movieId))!.MetaActresses = "Hatano Yui";
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = targetId });
            db.Set<MovieActorTag>().AddRange(
                new MovieActorTag { MovieId = movieId, ActorId = targetId, TagId = tagA },
                new MovieActorTag { MovieId = movieId, ActorId = placeholderId, TagId = tagA },
                new MovieActorTag { MovieId = movieId, ActorId = placeholderId, TagId = tagB });
            await db.SaveChangesAsync();
        }

        var result = await new ActorService(factory).MergeAsync(placeholderId, targetId);

        Assert.True(result.Success);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(targetId, (await check.MovieActors.SingleAsync()).ActorId);
        Assert.Equal([tagA, tagB], (await check.Set<MovieActorTag>().Select(t => t.TagId).ToListAsync()).Order().ToList());
        Assert.Equal("Hatano Yui", (await check.Movies.SingleAsync()).MetaActresses);
    }

    [Fact]
    public async Task MergeAsync_DoesNotCopyAPlaceholdersOtherFieldsOntoTheTarget()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory);
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var placeholder = await db.Actors.FindAsync(placeholderId);
            placeholder!.IsFavorite = true;
            placeholder.HeightCm = 150;
            placeholder.JapaneseNameKanji = "素人";
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }

        await new ActorService(factory).MergeAsync(placeholderId, targetId);

        await using var check = await factory.CreateDbContextAsync();
        var merged = await check.Actors.SingleAsync();
        Assert.False(merged.IsFavorite);
        Assert.Null(merged.HeightCm);
        Assert.Null(merged.JapaneseNameKanji);
    }

    [Fact]
    public async Task MissingCastFilter_SkipsAMovieWhoseOnlyCastIsAPlaceholder_InTheDatabase()
    {
        using var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "NONE-001" });
            await db.SaveChangesAsync();
            foreach (var movie in db.Movies) movie.MetaFetchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var codes = await new MovieGridQueryService(factory).GetRangeAsync(
            new MovieGridFilter(Features: [MovieFeatureFilterOption.MissingCast]), new MovieGridSort("title", false, 1), 0, 10);

        Assert.Equal(["NONE-001"], codes.Select(m => m.Code));
    }

    [Fact]
    public async Task CreatingAndDeletingAPlaceholder_TellsOpenMovieGridsTheMoviesChanged()
    {
        using var factory = new TestDbContextFactory();
        var movieId = (await SeedAsync(factory)).MovieId;
        var notifier = new MovieChangeNotifier();
        var raised = 0;
        notifier.Changed += () => raised++;
        var service = new ActorService(factory, movieChangeNotifier: notifier);

        var created = await service.CreatePlaceholderAsync(movieId);
        await service.DeleteAsync(created.Actor!.Id);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void ThePersistedMovieKeepsWhetherAMovieHasAnUnidentifiedPerformer()
    {
        var movie = new Movie { Id = 1, Code = "ABC-123", HasPlaceholderActors = true };

        var restored = Javbuddy.Components.Pages.MoviesSections.MoviesPersistedState.PersistedMovie.FromMovie(movie).ToMovie();

        Assert.True(restored.HasPlaceholderActors);
    }

    [Fact]
    public async Task APlaceholdersLabel_NeverHoldsASlash_WhichWouldSplitItsPageRoute()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory, label: "Blonde/Tall");
        var service = new ActorService(factory);

        await service.UpdateAsync(new ActorUpdateModel { Id = placeholderId, FirstName = "Red/Head" });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("Red-Head (ABC-123)", (await db.Actors.SingleAsync()).DisplayName);
        Assert.NotNull(await service.GetByRouteNameAsync("Red-Head (ABC-123)"));
    }

    [Theory]
    [InlineData("Redhead (ABC-123)", "R")]
    [InlineData("Unidentified 1 (ABC-123)", "U1")]
    [InlineData("Yui Hatano", "YH")]
    [InlineData("Remu", "R")]
    public void Initials_LeaveTheBracketedMovieCodeOut(string name, string expected) =>
        Assert.Equal(expected, ActorMatching.Initials(name));

    [Fact]
    public async Task RemoveActorFromMovieAsync_RefusesAPlaceholder_ItIsDeletedFromTheCastSectionInstead()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory);

        var result = await new MovieService(factory).RemoveActorFromMovieAsync(movieId, placeholderId);

        Assert.False(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.Actors.ToListAsync());
    }

    [Fact]
    public async Task DeletingAMovieInCleanup_AlsoRemovesItsPlaceholdersPortraitsAndPhotos()
    {
        using var factory = new TestDbContextFactory();
        var (movieId, placeholderId) = await SeedAsync(factory);
        var images = Substitute.For<IActorImageCacheService>();
        var photos = Substitute.For<IActorPhotoService>();
        var service = new MovieCleanupService(factory, Substitute.For<ILocalLibraryClient>(), actorImageCache: images, actorPhotos: photos);

        await service.DeleteAsync(movieId);

        await images.Received(1).DeleteAllForActorAsync(placeholderId, Arg.Any<CancellationToken>());
        await photos.Received(1).DeleteAllForActorAsync(placeholderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleFavoriteAsync_IgnoresAPlaceholder()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory);

        Assert.False(await new ActorService(factory).ToggleFavoriteAsync(placeholderId));

        await using var db = await factory.CreateDbContextAsync();
        Assert.False((await db.Actors.SingleAsync()).IsFavorite);
    }

    [Fact]
    public async Task GetMergeCandidatesAsync_ForAPlaceholder_SuggestsNobody()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory, label: "Yui");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Yui", LastName = "Hatano" });
            await db.SaveChangesAsync();
        }

        var candidates = await new ActorService(factory).GetMergeCandidatesAsync(placeholderId);

        var candidate = Assert.Single(candidates);
        Assert.False(candidate.IsSuggested);
    }

    [Fact]
    public async Task MergeAsync_ForAPlaceholder_MovesTheirGalleryToTheTarget_MergingAlbumsThatShareAName()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory);
        int targetId, sharedAlbumId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
            var shared = new ActorAlbum { ActorId = targetId, Name = "Shoot" };
            var sharedFromPlaceholder = new ActorAlbum { ActorId = placeholderId, Name = "shoot" };
            var own = new ActorAlbum { ActorId = placeholderId, Name = "Beach" };
            db.ActorAlbums.AddRange(shared, sharedFromPlaceholder, own);
            await db.SaveChangesAsync();
            sharedAlbumId = shared.Id;
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = placeholderId, AlbumId = sharedFromPlaceholder.Id, ThumbStorageId = Guid.NewGuid(), FullStorageId = Guid.NewGuid() },
                new ActorPhoto { ActorId = placeholderId, AlbumId = own.Id, ThumbStorageId = Guid.NewGuid(), FullStorageId = Guid.NewGuid() },
                new ActorPhoto { ActorId = placeholderId, ThumbStorageId = Guid.NewGuid(), FullStorageId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        var result = await new ActorService(factory).MergeAsync(placeholderId, targetId);

        Assert.True(result.Success);
        await using var check = await factory.CreateDbContextAsync();
        Assert.All(await check.ActorPhotos.ToListAsync(), p => Assert.Equal(targetId, p.ActorId));
        Assert.Equal(3, await check.ActorPhotos.CountAsync());
        Assert.Equal(["Beach", "Shoot"], (await check.ActorAlbums.Select(a => a.Name).ToListAsync()).Order().ToList());
        Assert.Equal(2, await check.ActorPhotos.CountAsync(p => p.AlbumId != null));
        Assert.Equal(1, await check.ActorPhotos.CountAsync(p => p.AlbumId == sharedAlbumId));
    }

    [Fact]
    public async Task MergeAsync_ForAPlaceholder_KeepsTheirPortraitUntilTheMergeIsSaved()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory);
        var images = Substitute.For<IActorImageCacheService>();
        var service = new ActorService(factory, images);

        // The target doesn't exist, so the merge fails before anything is saved.
        var result = await service.MergeAsync(placeholderId, 9999);

        Assert.False(result.Success);
        await images.DidNotReceiveWithAnyArgs().DeleteAllForActorAsync(default, default);
    }

    [Fact]
    public async Task RenamingAPlaceholder_CapsItsLabelAt100Characters_LikeCreatingOne()
    {
        using var factory = new TestDbContextFactory();
        var (_, placeholderId) = await SeedAsync(factory);

        await new ActorService(factory).UpdateAsync(new ActorUpdateModel { Id = placeholderId, FirstName = new string('x', 150) });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(100, (await db.Actors.SingleAsync()).FirstName!.Length);
    }

    [Fact]
    public async Task CreatePlaceholderAsync_ANameARealActorAlreadyHolds_FailsCleanlyInsteadOfThrowing()
    {
        using var factory = new TestDbContextFactory();
        var movieId = (await SeedAsync(factory)).MovieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // The same first and last name the next placeholder of this movie would get.
            db.Actors.Add(new Actor { FirstName = "Unidentified 2", LastName = "ABC-123" });
            await db.SaveChangesAsync();
        }

        var result = await new ActorService(factory).CreatePlaceholderAsync(movieId);

        Assert.False(result.Success);
        Assert.Contains("already in use", result.ErrorMessage);
    }
}
