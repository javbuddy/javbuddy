using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

/// <summary>Unidentified placeholders: actors local to one movie that carry tags and a photo but have no name to
/// write to an .nfo or match a cast line by.</summary>
public class ActorPlaceholderTests
{
    private static async Task<int> AddMovieAsync(TestDbContextFactory factory, string code = "ABC-123", string? cast = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = code, MetaActresses = cast };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task CreatePlaceholderAsync_LinksAFlaggedActorToTheMovie_WithoutTouchingTheCastText()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, cast: "Hatano Yui");

        var result = await new ActorService(factory).CreatePlaceholderAsync(movieId);

        Assert.True(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        var actor = await db.Actors.SingleAsync();
        Assert.Equal(movieId, actor.PlaceholderMovieId);
        Assert.True(actor.IsPlaceholder);
        Assert.False(actor.HasName);
        // The code is the last name, brackets only added for display: the stored name is just the label.
        Assert.Equal("Unidentified 1", actor.FirstName);
        Assert.Equal("ABC-123", actor.LastName);
        Assert.Equal("Unidentified 1 (ABC-123)", actor.DisplayName);
        Assert.Equal(actor.Id, (await db.MovieActors.SingleAsync()).ActorId);
        Assert.Equal("Hatano Yui", (await db.Movies.SingleAsync()).MetaActresses);
    }

    [Fact]
    public async Task CreatePlaceholderAsync_NumbersEachUnlabelledOne_AndAppendsTheCodeToALabel()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        var other = await AddMovieAsync(factory, "XYZ-001");
        var service = new ActorService(factory);

        await service.CreatePlaceholderAsync(movieId);
        await service.CreatePlaceholderAsync(movieId);
        await service.CreatePlaceholderAsync(movieId, " Blonde ");
        await service.CreatePlaceholderAsync(movieId, "Blonde");
        await service.CreatePlaceholderAsync(other);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(
            ["Blonde (ABC-123)", "Blonde 1 (ABC-123)", "Unidentified 1 (ABC-123)", "Unidentified 1 (XYZ-001)", "Unidentified 2 (ABC-123)"],
            (await db.Actors.ToListAsync()).Select(a => a.DisplayName).Order().ToList());
    }

    [Fact]
    public async Task CreatePlaceholderAsync_Fails_ForAMissingMovie()
    {
        using var factory = new TestDbContextFactory();

        var result = await new ActorService(factory).CreatePlaceholderAsync(99);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_KeepsAPlaceholderLinkAndItsTags_WhenTheCastChanges()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, cast: "Hatano Yui");
        int placeholderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Yui", LastName = "Hatano" });
            await db.SaveChangesAsync();
        }
        placeholderId = (await new ActorService(factory).CreatePlaceholderAsync(movieId)).Actor!.Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Blonde", IsActorTag = true };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.Set<MovieActorTag>().Add(new MovieActorTag { MovieId = movieId, ActorId = placeholderId, TagId = tag.Id });
            await db.SaveChangesAsync();

            var movie = await db.Movies.SingleAsync();
            movie.MetaActresses = "Hatano Yui, Unidentified 1 (ABC-123), Someone Else";
            await MovieActorAssociation.SynchronizeAsync(db, movie);
            await db.SaveChangesAsync();
            await MovieActorAssociation.SynchronizeAllAsync(db);
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Contains(placeholderId, await check.MovieActors.Select(ma => ma.ActorId).ToListAsync());
        Assert.Single(await check.Set<MovieActorTag>().ToListAsync());
        // The placeholder's name in the text is only text: it doesn't resolve to the placeholder.
        Assert.Equal(2, (await check.Movies.SingleAsync()).UnmatchedActorNames!.Split(',').Length);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAPlaceholderWithItsTags_AndLeavesTheCastTextAlone()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, cast: "Hatano Yui");
        var service = new ActorService(factory);
        var placeholderId = (await service.CreatePlaceholderAsync(movieId)).Actor!.Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Blonde", IsActorTag = true };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.Set<MovieActorTag>().Add(new MovieActorTag { MovieId = movieId, ActorId = placeholderId, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        await service.DeleteAsync(placeholderId);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.Actors.ToListAsync());
        Assert.Empty(await check.MovieActors.ToListAsync());
        Assert.Empty(await check.Set<MovieActorTag>().ToListAsync());
        Assert.Equal("Hatano Yui", (await check.Movies.SingleAsync()).MetaActresses);
    }

    [Fact]
    public async Task DeletingTheMovie_TakesItsPlaceholdersWithIt()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        await new ActorService(factory).CreatePlaceholderAsync(movieId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Remove(await db.Movies.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.Empty(await check.Actors.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_RenamingAPlaceholder_KeepsTheNewNameOutOfTheCastTextAndAliases()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, cast: "Hatano Yui");
        var service = new ActorService(factory);
        var placeholder = (await service.CreatePlaceholderAsync(movieId)).Actor!;

        var result = await service.UpdateAsync(new ActorUpdateModel { Id = placeholder.Id, FirstName = "The redhead" });

        Assert.True(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ActorAliases.ToListAsync());
        Assert.Equal("Hatano Yui", (await db.Movies.SingleAsync()).MetaActresses);
        Assert.Equal(placeholder.Id, (await db.MovieActors.SingleAsync()).ActorId);
        Assert.True((await db.Actors.SingleAsync()).IsPlaceholder);
    }

    [Fact]
    public async Task UpdateAsync_ChangesOnlyAPlaceholdersLabel_ItsMovieCodeCantBeEditedAway()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        var service = new ActorService(factory);
        var placeholder = (await service.CreatePlaceholderAsync(movieId)).Actor!;

        var result = await service.UpdateAsync(new ActorUpdateModel { Id = placeholder.Id, FirstName = "Redhead", LastName = "somebody else" });

        Assert.True(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        var actor = await db.Actors.SingleAsync();
        Assert.Equal(("Redhead", "ABC-123"), (actor.FirstName, actor.LastName));
        Assert.Equal("Redhead (ABC-123)", actor.DisplayName);
    }

    [Fact]
    public async Task TwoMovies_CanEachHaveTheSameLabel_AndTheActorPageFindsEachByItsBracketedName()
    {
        using var factory = new TestDbContextFactory();
        var first = await AddMovieAsync(factory);
        var second = await AddMovieAsync(factory, "XYZ-001");
        var service = new ActorService(factory);
        await service.CreatePlaceholderAsync(first, "Redhead");
        await service.CreatePlaceholderAsync(second, "Redhead");

        var found = await service.GetByRouteNameAsync("redhead (xyz-001)");

        Assert.NotNull(found);
        Assert.Equal(second, found.PlaceholderMovieId);
        Assert.Null(await service.GetByRouteNameAsync("Redhead"));
    }

    [Fact]
    public async Task MergeAsync_MovesAPlaceholdersTagsToTheRealActor_AndNamesThemInTheCast_WithoutAnAlias()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }
        var service = new ActorService(factory);
        var placeholderId = (await service.CreatePlaceholderAsync(movieId)).Actor!.Id;
        int tagId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var tag = new Tag { Name = "Blonde", IsActorTag = true };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
            db.Set<MovieActorTag>().Add(new MovieActorTag { MovieId = movieId, ActorId = placeholderId, TagId = tag.Id });
            await db.SaveChangesAsync();
        }

        var result = await service.MergeAsync(placeholderId, targetId);

        Assert.True(result.Success);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(targetId, (await check.Actors.SingleAsync()).Id);
        // The placeholder's label (with the movie code) is not kept as an alias of the real actor.
        Assert.Empty(await check.ActorAliases.ToListAsync());
        Assert.Equal("Hatano Yui", (await check.Movies.SingleAsync()).MetaActresses);
        var link = await check.MovieActors.SingleAsync();
        Assert.Equal(targetId, link.ActorId);
        var actorTag = await check.Set<MovieActorTag>().SingleAsync();
        Assert.Equal((movieId, targetId, tagId), (actorTag.MovieId, actorTag.ActorId, actorTag.TagId));
    }

    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 0, 1)]
    public async Task MergeAsync_MovesAPlaceholdersPhotoOnlyWhenTheTargetHasNone(bool targetHasImage, int transfers, int discards)
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
            if (targetHasImage)
            {
                db.ActorImages.Add(new ActorImage { ActorId = targetId, Variant = "thumb", StorageId = Guid.NewGuid(), SourceMovieCode = "custom" });
                await db.SaveChangesAsync();
            }
        }
        var images = NSubstitute.Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>();
        var service = new ActorService(factory, images);
        var placeholderId = (await service.CreatePlaceholderAsync(movieId)).Actor!.Id;

        var result = await service.MergeAsync(placeholderId, targetId);

        Assert.True(result.Success);
        await images.Received(transfers).TransferImagesAsync(placeholderId, targetId, Arg.Any<CancellationToken>());
        await images.Received(discards).DeleteAllForActorAsync(placeholderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_RefusesToMergeIntoAPlaceholder()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        int realId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var real = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(real);
            await db.SaveChangesAsync();
            realId = real.Id;
        }
        var service = new ActorService(factory);
        var placeholderId = (await service.CreatePlaceholderAsync(movieId)).Actor!.Id;

        var result = await service.MergeAsync(realId, placeholderId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PlaceholdersAreLeftOutOfMergeCandidates_ActorSearch_AndActorMatching()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        int realId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var real = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(real);
            await db.SaveChangesAsync();
            realId = real.Id;
        }
        var service = new ActorService(factory);
        await service.CreatePlaceholderAsync(movieId);

        Assert.Empty(await service.GetMergeCandidatesAsync(realId));
        Assert.Empty(await service.SearchActorsAsync("Unidentified"));
        await using var db2 = await factory.CreateDbContextAsync();
        Assert.Single(await ActorMatching.LoadActorLookupsAsync(db2));
    }

    [Fact]
    public async Task GetActorCardsAsync_FlagsPlaceholders()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Yui", LastName = "Hatano" });
            await db.SaveChangesAsync();
        }
        var service = new ActorService(factory);
        await service.CreatePlaceholderAsync(movieId);

        var cards = await service.GetActorCardsAsync();

        Assert.Equal(["Unidentified 1 (ABC-123)"], cards.Where(c => c.IsPlaceholder).Select(c => c.DisplayName));
        Assert.Single(cards, c => !c.IsPlaceholder);
    }

    [Fact]
    public async Task GetCastAsync_ListsPlaceholdersAfterTheNamedCast()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, cast: "Hatano Yui");
        var placeholderId = (await new ActorService(factory).CreatePlaceholderAsync(movieId)).Actor!.Id;
        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movie);

        Assert.Equal(["Hatano Yui", "Unidentified 1 (ABC-123)"], cast.Select(c => c.Name));
        Assert.False(cast[0].IsPlaceholder);
        Assert.True(cast[1].IsPlaceholder);
        Assert.Equal(placeholderId, cast[1].ActorId);
    }

    [Fact]
    public async Task GetCastAsync_ListsAMoviesPlaceholders_EvenWithNoCastText()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        await new ActorService(factory).CreatePlaceholderAsync(movieId);

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movieId);

        Assert.Single(cast);
        Assert.True(cast[0].IsPlaceholder);
    }

    [Fact]
    public async Task AddActorToMovieAsync_RefusesAPlaceholder_OfAnyMovie()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        var otherId = await AddMovieAsync(factory, "XYZ-001");
        var placeholderId = (await new ActorService(factory).CreatePlaceholderAsync(movieId)).Actor!.Id;

        var result = await new MovieService(factory).AddActorToMovieAsync(otherId, placeholderId);

        Assert.False(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal([movieId], await db.MovieActors.Select(ma => ma.MovieId).ToListAsync());
        Assert.Null((await db.Movies.SingleAsync(m => m.Id == otherId)).MetaActresses);
    }

    [Fact]
    public void ThePlaceholderIsNotACastMismatch_ForTheNfoDriftCheck()
    {
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Mikami Yua" };
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Yua", LastName = "Mikami" } });
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Redhead", LastName = "ABC-123", PlaceholderMovieId = 1 } });
        // An .nfo written by hand, naming only the real actor, as one written by Javbuddy would.
        var root = System.Xml.Linq.XDocument.Parse("<movie><actor><name>Mikami Yua</name></actor></movie>").Root!;

        var outcome = NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null);

        Assert.DoesNotContain("Actors", outcome.Details ?? "");
    }

    [Fact]
    public async Task ACastWithAPlaceholder_HasNoUnderageOrUnmatchedEntry_EvenWithABirthdateThatWouldBeUnderage()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Movies.FindAsync(movieId))!.MetaReleaseDate = new DateTime(2020, 1, 1);
            var placeholder = new Actor { FirstName = "Redhead", LastName = "ABC-123", PlaceholderMovieId = movieId, BirthDate = new DateTime(2010, 1, 1) };
            db.MovieActors.Add(new MovieActor { MovieId = movieId, Actor = placeholder });
            await db.SaveChangesAsync();
        }

        var cast = await new MovieDetailQueryService(factory).GetCastAsync(movieId);

        var entry = Assert.Single(cast);
        Assert.NotNull(entry.ActorId);
        Assert.Null(entry.Age);
        Assert.False(entry.IsUnderage);
        Assert.Empty(MovieDetailText.UnderageCastTooltip(cast) ?? "");
    }

    [Fact]
    public void MovieNfoBuilder_NeverWritesAPlaceholder()
    {
        var movie = new Movie { Code = "abc-001" };
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Yua", LastName = "Mikami" } });
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Unidentified 1", LastName = "abc-001", PlaceholderMovieId = 1 } });

        var root = System.Xml.Linq.XDocument.Parse(MovieNfoBuilder.Build(movie)).Root!;

        Assert.Equal(["Mikami Yua"], root.Elements("actor").Select(a => a.Element("name")!.Value));
    }

    [Fact]
    public async Task CreatePlaceholderAsync_Fails_ForAMovieWithoutACode()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory, code: "");

        var result = await new ActorService(factory).CreatePlaceholderAsync(movieId);

        Assert.False(result.Success);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Actors.ToListAsync());
    }

    [Fact]
    public async Task MergeAsync_ForAPlaceholder_ChecksTheMoviesNfoDriftAndNeverSyncsActorNames()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }
        var nfo = Substitute.For<INfoSyncService>();
        var service = new ActorService(factory, nfoSyncService: nfo);
        var placeholderId = (await service.CreatePlaceholderAsync(movieId)).Actor!.Id;

        await service.MergeAsync(placeholderId, targetId, updateNfoOnDisk: true);

        await nfo.Received(1).CheckMovieNfoConflictAsync(movieId, Arg.Any<CancellationToken>());
        await nfo.DidNotReceiveWithAnyArgs().SyncActorAsync(default, default!, default);
    }

    [Fact]
    public async Task FindMatchingActorAsync_NeverReturnsAPlaceholder_EvenForItsKanjiOrLabel()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        var service = new ActorService(factory);
        var placeholder = (await service.CreatePlaceholderAsync(movieId, "Redhead")).Actor!;
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Actors.FindAsync(placeholder.Id))!.JapaneseNameKanji = "素人";
            await db.SaveChangesAsync();
        }

        Assert.Null(await service.FindMatchingActorAsync("Redhead", "素人"));
        Assert.Null(await service.FindMatchingActorAsync("ABC-123 Redhead"));
    }

    [Fact]
    public async Task TheCleanupReviewCast_NamesAPlaceholderLikeMovieDetailDoes_AndMarksIt()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        await new ActorService(factory).CreatePlaceholderAsync(movieId, "Redhead");

        var cast = await new MovieCleanupService(factory, Substitute.For<Javbuddy.Services.LocalLibrary.ILocalLibraryClient>()).GetMovieActorsAsync(movieId, null);

        var entry = Assert.Single(cast);
        Assert.Equal("Redhead (ABC-123)", entry.Name);
        Assert.True(entry.IsPlaceholder);
    }

    [Fact]
    public async Task ThePhotosPageNamesAPlaceholderWithItsBracketedCode()
    {
        using var factory = new TestDbContextFactory();
        var movieId = await AddMovieAsync(factory);
        var placeholderId = (await new ActorService(factory).CreatePlaceholderAsync(movieId, "Redhead")).Actor!.Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ActorPhotos.Add(new ActorPhoto { ActorId = placeholderId, ThumbStorageId = Guid.NewGuid(), FullStorageId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        var summary = await new ActorPhotoService(factory, Substitute.For<Javbuddy.Services.Images.ILocalImageCache>()).GetActorsWithPhotosAsync();

        Assert.Equal(["Redhead (ABC-123)"], summary.Select(a => a.DisplayName));
    }
}
