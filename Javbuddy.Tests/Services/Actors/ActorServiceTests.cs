using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

public class ActorServiceTests
{
    [Fact]
    public async Task GetByRouteNameAsync_ReturnsNull_ForEmptyOrWhitespace()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.GetByRouteNameAsync("   ");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByRouteNameAsync_FindsSingleAndTwoWordActors_CaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Yua", LastName = "Mikami" },
                new Actor { FirstName = "Remu", LastName = null }
            );
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);

        var actor1 = await service.GetByRouteNameAsync("mikami yua");
        Assert.NotNull(actor1);
        Assert.Equal("Yua", actor1.FirstName);
        Assert.Equal("Mikami", actor1.LastName);

        var actor2 = await service.GetByRouteNameAsync("REMU");
        Assert.NotNull(actor2);
        Assert.Equal("Remu", actor2.FirstName);
        Assert.Null(actor2.LastName);

        var notFound = await service.GetByRouteNameAsync("Nonexistent Actor");
        Assert.Null(notFound);
    }

    [Fact]
    public async Task GetByRouteNameAsync_FindsActorsWithNonAsciiNames()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Ichiiyūka", LastName = null },
                new Actor { FirstName = "Yūka", LastName = "Kōno" },
                new Actor { FirstName = "Mei", LastName = "Satō", Aliases = { new ActorAlias { Name = "Satō Meī" } } }
            );
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);

        Assert.Equal("Ichiiyūka", (await service.GetByRouteNameAsync("Ichiiyūka"))?.FirstName);
        Assert.Equal("Ichiiyūka", (await service.GetByRouteNameAsync("ichiiyūka"))?.FirstName);
        Assert.Equal("Kōno", (await service.GetByRouteNameAsync("Kōno Yūka"))?.LastName);
        Assert.Equal("Satō", (await service.GetByRouteNameAsync("satō meī"))?.LastName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsActor_WhenExists()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Ichika", LastName = "Matsumoto" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);

        var found = await service.GetByIdAsync(actorId);
        Assert.NotNull(found);
        Assert.Equal("Ichika", found.FirstName);

        var notFound = await service.GetByIdAsync(99999);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task UpdateAsync_RejectsEmptyFirstName()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = 1,
            FirstName = "   "
        });

        Assert.False(result.Success);
        Assert.Equal("First name is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDuplicateActorName()
    {
        using var factory = new TestDbContextFactory();
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var existing = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var target = new Actor { FirstName = "Ai", LastName = "Uehara" };
            db.Actors.AddRange(existing, target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }

        var service = new ActorService(factory);

        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = targetId,
            FirstName = "Yua",
            LastName = "Mikami"
        });

        Assert.False(result.Success);
        Assert.Equal("An actor named \"Mikami Yua\" already exists.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_AllowsSavingUnchangedName_OnSameActor()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);

        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yua",
            LastName = "Mikami",
            R18DevId = 12345
        });

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(12345, result.Actor?.R18DevId);
    }

    [Fact]
    public async Task UpdateAsync_AddsUpdatesAndRemovesCupSizePeriods_ByDate()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Cup" };
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2020, 1, 1), CupSize = "C" });
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = "E" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }
        var service = new ActorService(factory);

        // 2020 kept with a new cup, 2022 dropped, 2024 added.
        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Cup",
            CupSizePeriods = [new() { EffectiveFrom = new DateTime(2024, 1, 1), CupSize = " g " }, new() { EffectiveFrom = new DateTime(2020, 1, 1), CupSize = "D" }],
        });

        Assert.True(result.Success, result.ErrorMessage);
        var periods = (await service.GetByRouteNameAsync("Cup"))!.CupSizePeriods.OrderBy(p => p.EffectiveFrom).Select(p => (p.EffectiveFrom, p.CupSize));
        Assert.Equal([(new DateTime(2020, 1, 1), "D"), (new DateTime(2024, 1, 1), "G")], periods);
    }

    [Theory]
    [InlineData("2020-01-01", null, "2021-01-01", "E", "needs both a date and a cup size")]
    [InlineData(null, "C", "2021-01-01", "E", "needs both a date and a cup size")]
    [InlineData("2020-01-01", "Z", "2021-01-01", "E", "must be one of")]
    [InlineData("2020-01-01", "C", "2020-01-01", "E", "same date")]
    public void ValidateCupSizePeriods_RejectsIncompleteNonStandardAndDuplicateRows(string? from1, string? cup1, string? from2, string? cup2, string expected)
    {
        var error = ActorService.ValidateCupSizePeriods(
        [
            new() { EffectiveFrom = from1 is null ? null : DateTime.Parse(from1), CupSize = cup1 },
            new() { EffectiveFrom = from2 is null ? null : DateTime.Parse(from2), CupSize = cup2 },
        ], birthDate: null, ValidationToday);

        Assert.Contains(expected, error);
        Assert.Null(ActorService.ValidateCupSizePeriods([new() { EffectiveFrom = new DateTime(2020, 1, 1), CupSize = "c" }], null, ValidationToday));
    }

    private static readonly DateOnly ValidationToday = new(2026, 6, 15);

    [Theory]
    [InlineData("2018-03-01", null)]      // her 18th birthday
    [InlineData("2018-02-28", "turned 18")]
    [InlineData("2026-06-15", null)]      // today
    [InlineData("2026-06-16", "future")]
    public void ValidateCupSizePeriods_AllowsDatesFromThe18thBirthdayToToday(string from, string? expected)
    {
        var error = ActorService.ValidateCupSizePeriods([new() { EffectiveFrom = DateTime.Parse(from), CupSize = "C" }], new DateTime(2000, 3, 1), ValidationToday);

        if (expected is null) Assert.Null(error);
        else Assert.Contains(expected, error);
    }

    [Fact]
    public void ValidateCupSizePeriods_WithoutABirthdate_OnlyBoundsByToday()
    {
        Assert.Null(ActorService.ValidateCupSizePeriods([new() { EffectiveFrom = new DateTime(1990, 1, 1), CupSize = "C" }], null, ValidationToday));
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidCupSizePeriods_WithoutSaving()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Cup" });
            await db.SaveChangesAsync();
        }
        var service = new ActorService(factory);
        var id = (await service.GetByRouteNameAsync("Cup"))!.Id;

        var result = await service.UpdateAsync(new ActorUpdateModel { Id = id, FirstName = "Cup", CupSizePeriods = [new() { CupSize = "C" }] });

        Assert.False(result.Success);
        Assert.Empty((await service.GetByRouteNameAsync("Cup"))!.CupSizePeriods);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAllSettledFields_AndSynchronizesMovieAssociations()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "TEST-001", MetaActresses = "Mikami Yua, 新木希空" };
            var actor = new Actor { FirstName = "Old", LastName = "Name" };
            db.Movies.Add(movie);
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);

        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yua",
            LastName = "Mikami",
            JapaneseNameKanji = "三上悠亜",
            JapaneseNameKana = "みかみ ゆあ",
            R18DevName = "Yua Mikami",
            JellyfinPersonId = "guid-1234",
            R18DevId = 200
        });

        Assert.True(result.Success);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var updated = await db.Actors.Include(a => a.MovieActors).SingleAsync(a => a.Id == actorId);
            Assert.Equal("Yua", updated.FirstName);
            Assert.Equal("Mikami", updated.LastName);
            Assert.Equal("三上悠亜", updated.JapaneseNameKanji);
            Assert.Equal("みかみ ゆあ", updated.JapaneseNameKana);
            Assert.Equal("Yua Mikami", updated.R18DevName);
            Assert.Equal("guid-1234", updated.JellyfinPersonId);
            Assert.Equal(200, updated.R18DevId);

            // Verified that MovieActor association was synchronized
            var link = Assert.Single(updated.MovieActors);
            var linkedMovie = await db.Movies.FindAsync(link.MovieId);
            Assert.Equal("TEST-001", linkedMovie?.Code);
        }
    }

    [Fact]
    public async Task UpdateAsync_RenamingActor_PreservesOldNameAsAlias_AndUpdatesLinkedMovieMetaActresses()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var movie = new Movie
            {
                Code = "ABC-123",
                MetaActresses = "Hatano Yui, Julia",
                Status = MovieStatus.Got
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;

            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actorId });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yui",
            LastName = "UpdatedHatano"
        });

        Assert.True(result.Success);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var updatedActor = await db.Actors.Include(a => a.Aliases).SingleAsync(a => a.Id == actorId);
            Assert.Equal("UpdatedHatano Yui", updatedActor.DisplayName);
            Assert.Contains(updatedActor.Aliases, a => a.Name == "Hatano Yui");

            var updatedMovie = await db.Movies.SingleAsync(m => m.Id == movieId);
            // Movie MetaActresses was updated to the new canonical name
            Assert.Equal("UpdatedHatano Yui, Julia", updatedMovie.MetaActresses);

            // MovieActor link is preserved
            var link = await db.MovieActors.SingleOrDefaultAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId);
            Assert.NotNull(link);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesActorFromDatabase()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Delete", LastName = "Me" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        await service.DeleteAsync(actorId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var exists = await db.Actors.AnyAsync(a => a.Id == actorId);
            Assert.False(exists);
        }
    }

    [Fact]
    public async Task DeleteAsync_FlagsMovieAsHavingUnmatchedActorsWhenItsLinkedActorIsDeleted()
    {
        using var factory = new TestDbContextFactory();
        int actorId, movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Delete", LastName = "Me" };
            var movie = new Movie { Code = "ABC-123", MetaActresses = "Me Delete" };
            db.AddRange(actor, movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;

            await MovieActorAssociation.SynchronizeAsync(db, movie);
            await db.SaveChangesAsync();
            Assert.False(movie.HasUnmatchedActors);
        }

        var service = new ActorService(factory);
        await service.DeleteAsync(actorId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = await db.Movies.SingleAsync(m => m.Id == movieId);
            Assert.True(movie.HasUnmatchedActors);
            Assert.Equal("Me Delete", movie.UnmatchedActorNames);
            Assert.False(await db.MovieActors.AnyAsync(l => l.MovieId == movieId));
        }
    }

    [Fact]
    public async Task GetByRouteNameAsync_MatchesActorByAlias()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Alternate Stage Name" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var found = await service.GetByRouteNameAsync("alternate stage name");

        Assert.NotNull(found);
        Assert.Equal("Yua", found.FirstName);
        Assert.Equal("Mikami", found.LastName);
    }

    [Fact]
    public async Task GetMergeCandidatesAsync_ExcludesSource_AndRanksSuggestedFirst()
    {
        using var factory = new TestDbContextFactory();
        int sourceId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Yua", LastName = "Mikami", JapaneseNameKanji = "三上悠亜" };
            var candidate1 = new Actor { FirstName = "Mikami", LastName = "Yua" };
            var candidate2 = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.AddRange(source, candidate1, candidate2);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        var service = new ActorService(factory);
        var candidates = await service.GetMergeCandidatesAsync(sourceId);

        Assert.Equal(2, candidates.Count);
        Assert.DoesNotContain(candidates, c => c.Id == sourceId);
        Assert.True(candidates[0].IsSuggested);
        Assert.Equal("Yua Mikami", candidates[0].DisplayName);
        Assert.False(candidates[1].IsSuggested);
    }

    [Fact]
    public async Task SearchActorsAsync_ReturnsEmpty_ForBlankSearch()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var results = await service.SearchActorsAsync("   ");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchActorsAsync_MatchesNameOrAlias_CaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var mikami = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var withAlias = new Actor { FirstName = "Someone", LastName = "Else" };
            withAlias.Aliases.Add(new ActorAlias { Name = "Yua Alt Name" });
            var unrelated = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.AddRange(mikami, withAlias, unrelated);
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var results = await service.SearchActorsAsync("yua");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.DisplayName == "Mikami Yua");
        Assert.Contains(results, r => r.DisplayName == "Else Someone");
    }

    [Fact]
    public async Task SearchActorsAsync_ReportsHasImage_OnlyForActorsWithAThumbCached()
    {
        using var factory = new TestDbContextFactory();
        int withImageId, withoutImageId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var withImage = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var withoutImage = new Actor { FirstName = "Yui", LastName = "Mikamoto" };
            db.Actors.AddRange(withImage, withoutImage);
            await db.SaveChangesAsync();
            withImageId = withImage.Id;
            withoutImageId = withoutImage.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = withImageId,
                Variant = "thumb",
                SourceMovieCode = "custom",
                StorageId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var results = await service.SearchActorsAsync("mikam");

        Assert.True(results.Single(r => r.Id == withImageId).HasImage);
        Assert.False(results.Single(r => r.Id == withoutImageId).HasImage);
    }

    [Fact]
    public async Task MergeAsync_Fails_WhenMergingIntoSelf()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.MergeAsync(1, 1);
        Assert.False(result.Success);
        Assert.Equal("Cannot merge an actor into themselves.", result.ErrorMessage);
    }

    [Fact]
    public async Task MergeAsync_CopiesCupSizePeriods_OnlyWhenTheTargetHasNone()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, emptyTargetId, datedTargetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            static Actor Dated(string name, string cup)
            {
                var actor = new Actor { FirstName = name };
                actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = cup });
                return actor;
            }
            var source = Dated("Source", "F");
            var source2 = Dated("Source2", "H");
            var emptyTarget = new Actor { FirstName = "Empty" };
            var datedTarget = Dated("Dated", "D");
            db.Actors.AddRange(source, source2, emptyTarget, datedTarget);
            await db.SaveChangesAsync();
            (sourceId, emptyTargetId, datedTargetId) = (source.Id, emptyTarget.Id, datedTarget.Id);
        }
        var service = new ActorService(factory);

        Assert.True((await service.MergeAsync(sourceId, emptyTargetId)).Success);
        var source2Id = (await service.GetByRouteNameAsync("Source2"))!.Id;
        Assert.True((await service.MergeAsync(source2Id, datedTargetId)).Success);

        Assert.Equal(["F"], (await service.GetByRouteNameAsync("Empty"))!.CupSizePeriods.Select(p => p.CupSize));
        Assert.Equal(["D"], (await service.GetByRouteNameAsync("Dated"))!.CupSizePeriods.Select(p => p.CupSize));
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(2, await check.ActorCupSizePeriods.CountAsync());
    }

    [Fact]
    public async Task MergeAsync_ConsolidatesMetadata_Aliases_Relations_AndDeletesSource()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId, movie1Id, movie2Id;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie1 = new Movie { Code = "MOV-001", MetaActresses = "Mikami Yua" };
            var movie2 = new Movie { Code = "MOV-002", MetaActresses = "Yua Mikami" };
            db.Movies.AddRange(movie1, movie2);

            var source = new Actor
            {
                FirstName = "Momona",
                LastName = "Koizumi",
                JapaneseNameKanji = "三上悠亜"
            };
            source.Aliases.Add(new ActorAlias { Name = "Old Alias" });

            var target = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                R18DevId = 999
            };

            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();

            sourceId = source.Id;
            targetId = target.Id;
            movie1Id = movie1.Id;
            movie2Id = movie2.Id;

            // Link movie1 to both source and target (duplicate association)
            db.MovieActors.Add(new MovieActor { MovieId = movie1Id, ActorId = sourceId });
            db.MovieActors.Add(new MovieActor { MovieId = movie1Id, ActorId = targetId });
            // Link movie2 to source only
            db.MovieActors.Add(new MovieActor { MovieId = movie2Id, ActorId = sourceId });
            await db.SaveChangesAsync();
        }

        var imageCacheService = NSubstitute.Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>();
        var service = new ActorService(factory, imageCacheService);

        var result = await service.MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        Assert.NotNull(result.Actor);
        Assert.Equal(targetId, result.Actor.Id);

        // Verify image cache transfer was invoked
        await imageCacheService.Received(1).TransferImagesAsync(sourceId, targetId, Arg.Any<CancellationToken>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            // Source actor is deleted
            var sourceExists = await db.Actors.AnyAsync(a => a.Id == sourceId);
            Assert.False(sourceExists);

            // Target actor has consolidated metadata
            var targetInDb = await db.Actors
                .Include(a => a.Aliases)
                .Include(a => a.MovieActors)
                .SingleAsync(a => a.Id == targetId);

            Assert.Equal("三上悠亜", targetInDb.JapaneseNameKanji);
            Assert.Equal(999, targetInDb.R18DevId);

            // Aliases: source DisplayName and "Old Alias" registered on target
            var aliasNames = targetInDb.Aliases.Select(a => a.Name).ToList();
            Assert.Contains("Koizumi Momona", aliasNames);
            Assert.Contains("Old Alias", aliasNames);

            // MovieActor associations: both movies now linked to target, no duplicates
            Assert.Equal(2, targetInDb.MovieActors.Count);
            Assert.Contains(targetInDb.MovieActors, m => m.MovieId == movie1Id);
            Assert.Contains(targetInDb.MovieActors, m => m.MovieId == movie2Id);

            var orphanLinks = await db.MovieActors.Where(m => m.ActorId == sourceId).ToListAsync();
            Assert.Empty(orphanLinks);
        }
    }

    [Fact]
    public async Task HasImageAsync_ResolvesCorrectly_BasedOnCache()
    {
        using var factory = new TestDbContextFactory();
        int actorWithCacheId, actorWithoutImageId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var a1 = new Actor { FirstName = "Cached", LastName = "Actor" };
            var a2 = new Actor { FirstName = "NoImage", LastName = "Actor" };
            db.Actors.AddRange(a1, a2);
            await db.SaveChangesAsync();

            actorWithCacheId = a1.Id;
            actorWithoutImageId = a2.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorWithCacheId,
                Variant = "thumb",
                SourceMovieCode = "custom",
                StorageId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);

        Assert.True(await service.HasImageAsync(actorWithCacheId));
        Assert.False(await service.HasImageAsync(actorWithoutImageId));
        Assert.False(await service.HasImageAsync(99999));
    }

    [Fact]
    public async Task GetActorCardsAsync_ReturnsEmptyList_WhenNoActorsExist()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.GetActorCardsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActorCardsAsync_ReturnsProjectedActorCards_WithNormalizedMovieCounts_AndImages()
    {
        using var factory = new TestDbContextFactory();
        int a1Id, a2Id, a3Id;
        var a1UpdatedAt = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie1 = new Movie { Code = "MOV-101", MetaTitle = "Movie 1" };
            var movie2 = new Movie { Code = "MOV-102", MetaTitle = "Movie 2" };
            db.Movies.AddRange(movie1, movie2);

            var a1 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var a2 = new Actor { FirstName = "Ai", LastName = "Uehara" };
            var a3 = new Actor { FirstName = "Remu", LastName = null };
            db.Actors.AddRange(a1, a2, a3);
            await db.SaveChangesAsync();

            a1Id = a1.Id;
            a2Id = a2.Id;
            a3Id = a3.Id;

            // Actor 1 has 2 movies linked via MovieActor and a thumb ActorImage
            db.MovieActors.Add(new MovieActor { MovieId = movie1.Id, ActorId = a1Id });
            db.MovieActors.Add(new MovieActor { MovieId = movie2.Id, ActorId = a1Id });
            db.ActorImages.Add(new ActorImage
            {
                ActorId = a1Id,
                Variant = "thumb",
                SourceMovieCode = "MOV-101",
                StorageId = Guid.NewGuid(),
                UpdatedAt = a1UpdatedAt
            });
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = a1Id },
                new ActorPhoto { ActorId = a1Id });

            // Actor 2 has 1 movie linked and a remote ActorImage
            db.MovieActors.Add(new MovieActor { MovieId = movie1.Id, ActorId = a2Id });
            db.ActorImages.Add(new ActorImage
            {
                ActorId = a2Id,
                Variant = "thumb",
                SourceMovieCode = "remote",
                StorageId = Guid.NewGuid()
            });

            // Actor 3 has 0 movies and no image

            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var result = await service.GetActorCardsAsync();

        Assert.Equal(3, result.Count);

        // Ordered by LastName, then FirstName. Remu (LastName null) comes first in SQLite, then Mikami Yua, then Uehara Ai.
        var remu = result.Single(a => a.Id == a3Id);
        Assert.Equal("Remu", remu.DisplayName);
        Assert.Equal(0, remu.MovieCount);
        Assert.False(remu.HasImage);
        Assert.Null(remu.ImageVersion);

        var yua = result.Single(a => a.Id == a1Id);
        Assert.Equal("Mikami Yua", yua.DisplayName);
        Assert.Equal(2, yua.MovieCount);
        Assert.Equal(2, yua.ImageCount);
        Assert.True(yua.HasImage);
        Assert.Equal(a1UpdatedAt.Ticks, yua.ImageVersion);

        var ai = result.Single(a => a.Id == a2Id);
        Assert.Equal("Uehara Ai", ai.DisplayName);
        Assert.Equal(1, ai.MovieCount);
        Assert.Equal(0, ai.ImageCount);
        Assert.True(ai.HasImage);
        Assert.NotNull(ai.ImageVersion);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_TogglesIsFavorite_AndSetsFavoritedAt()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = false };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);

        // Toggle from false -> true
        var status1 = await service.ToggleFavoriteAsync(actorId);
        Assert.True(status1);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = await db.Actors.FindAsync(actorId);
            Assert.NotNull(actor);
            Assert.True(actor.IsFavorite);
            Assert.NotNull(actor.FavoritedAt);
        }

        // Toggle from true -> false
        var status2 = await service.ToggleFavoriteAsync(actorId);
        Assert.False(status2);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = await db.Actors.FindAsync(actorId);
            Assert.NotNull(actor);
            Assert.False(actor.IsFavorite);
            Assert.Null(actor.FavoritedAt);
        }
    }

    [Fact]
    public async Task ToggleFavoriteAsync_ReturnsFalse_WhenActorNotFound()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.ToggleFavoriteAsync(9999);
        Assert.False(result);
    }

    [Fact]
    public async Task GetActorCardsAsync_ProjectsIsFavorite()
    {
        using var factory = new TestDbContextFactory();
        int a1Id, a2Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var a1 = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            var a2 = new Actor { FirstName = "Remu", LastName = null, IsFavorite = false };
            db.Actors.AddRange(a1, a2);
            await db.SaveChangesAsync();
            a1Id = a1.Id;
            a2Id = a2.Id;
        }

        var service = new ActorService(factory);
        var cards = await service.GetActorCardsAsync();

        var card1 = cards.Single(c => c.Id == a1Id);
        Assert.True(card1.IsFavorite);

        var card2 = cards.Single(c => c.Id == a2Id);
        Assert.False(card2.IsFavorite);
    }

    [Fact]
    public async Task MergeAsync_MovesApexActorsOntoTheTarget()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId, soloApexId, sharedApexId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // The merge re-syncs the cast from MetaActresses, so it must name the target.
            var movie = new Movie { Code = "MOV-001", MetaActresses = "Target" };
            var source = new Actor { FirstName = "Source" };
            var target = new Actor { FirstName = "Target" };
            db.AddRange(movie, source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
            db.MovieActors.AddRange(new MovieActor { MovieId = movie.Id, ActorId = sourceId }, new MovieActor { MovieId = movie.Id, ActorId = targetId });
            var solo = new MovieApex { MovieId = movie.Id, Seconds = 10 };
            solo.ApexActors.Add(new ApexActor { MovieId = movie.Id, ActorId = sourceId });
            var shared = new MovieApex { MovieId = movie.Id, Seconds = 20 };
            shared.ApexActors.Add(new ApexActor { MovieId = movie.Id, ActorId = sourceId });
            shared.ApexActors.Add(new ApexActor { MovieId = movie.Id, ActorId = targetId });
            db.MovieApexes.AddRange(solo, shared);
            await db.SaveChangesAsync();
            soloApexId = solo.Id;
            sharedApexId = shared.Id;
        }

        var result = await new ActorService(factory, Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>()).MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var links = await db.ApexActors.OrderBy(aa => aa.ApexId).Select(aa => new { aa.ApexId, aa.ActorId }).ToListAsync();
            Assert.Equal([new { ApexId = soloApexId, ActorId = targetId }, new { ApexId = sharedApexId, ActorId = targetId }], links);
        }
    }

    [Fact]
    public async Task MergeAsync_MovesHighlightActorsOntoTheTarget()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId, soloId, sharedId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // The merge re-syncs the cast from MetaActresses, so it must name the target.
            var movie = new Movie { Code = "MOV-001", MetaActresses = "Target" };
            var source = new Actor { FirstName = "Source" };
            var target = new Actor { FirstName = "Target" };
            db.AddRange(movie, source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
            db.MovieActors.AddRange(new MovieActor { MovieId = movie.Id, ActorId = sourceId }, new MovieActor { MovieId = movie.Id, ActorId = targetId });
            var solo = new MovieHighlight { MovieId = movie.Id, StartSeconds = 10, EndSeconds = 20 };
            solo.HighlightActors.Add(new HighlightActor { MovieId = movie.Id, ActorId = sourceId });
            var shared = new MovieHighlight { MovieId = movie.Id, StartSeconds = 30, EndSeconds = 40 };
            shared.HighlightActors.Add(new HighlightActor { MovieId = movie.Id, ActorId = sourceId });
            shared.HighlightActors.Add(new HighlightActor { MovieId = movie.Id, ActorId = targetId });
            db.MovieHighlights.AddRange(solo, shared);
            await db.SaveChangesAsync();
            soloId = solo.Id;
            sharedId = shared.Id;
        }

        var result = await new ActorService(factory, Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>()).MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var links = await db.HighlightActors.OrderBy(ha => ha.HighlightId).Select(ha => new { ha.HighlightId, ha.ActorId }).ToListAsync();
            Assert.Equal([new { HighlightId = soloId, ActorId = targetId }, new { HighlightId = sharedId, ActorId = targetId }], links);
        }
    }

    [Fact]
    public async Task MergeAsync_MovesActorTagsOntoTheTarget()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId, sharedMovieId, soloMovieId, sceneId, blondeId, longId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            // The merge re-syncs the cast from MetaActresses, so it must name the target. In the solo movie only
            // the source is cast, so the merge replaces the cast link the tags hang off.
            var shared = new Movie { Code = "MOV-001", MetaActresses = "Target" };
            var solo = new Movie { Code = "MOV-002", MetaActresses = "Target" };
            var source = new Actor { FirstName = "Source" };
            var target = new Actor { FirstName = "Target" };
            var blonde = new Tag { Name = "Blonde", IsActorTag = true };
            var hairLong = new Tag { Name = "Long", IsActorTag = true };
            db.AddRange(shared, solo, source, target, blonde, hairLong);
            await db.SaveChangesAsync();
            (sourceId, targetId, sharedMovieId, soloMovieId, blondeId, longId) = (source.Id, target.Id, shared.Id, solo.Id, blonde.Id, hairLong.Id);
            db.MovieActors.AddRange(
                new MovieActor { MovieId = sharedMovieId, ActorId = sourceId },
                new MovieActor { MovieId = sharedMovieId, ActorId = targetId },
                new MovieActor { MovieId = soloMovieId, ActorId = sourceId });
            var scene = new Scene { MovieId = soloMovieId, StartSeconds = 0 };
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();
            sceneId = scene.Id;
            // In the shared movie both have Blonde (deduplicated) and only the source has Long.
            db.MovieActorTags.AddRange(
                new MovieActorTag { MovieId = sharedMovieId, ActorId = sourceId, TagId = blondeId },
                new MovieActorTag { MovieId = sharedMovieId, ActorId = sourceId, TagId = longId },
                new MovieActorTag { MovieId = sharedMovieId, ActorId = targetId, TagId = blondeId });
            db.SceneActorTags.Add(new SceneActorTag { SceneId = sceneId, MovieId = soloMovieId, ActorId = sourceId, TagId = blondeId });
            await db.SaveChangesAsync();
        }

        var result = await new ActorService(factory, Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>()).MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movieTags = await db.MovieActorTags.OrderBy(t => t.TagId).Select(t => new { t.MovieId, t.ActorId, t.TagId }).ToListAsync();
            Assert.Equal([new { MovieId = sharedMovieId, ActorId = targetId, TagId = blondeId }, new { MovieId = sharedMovieId, ActorId = targetId, TagId = longId }], movieTags);
            var sceneTags = await db.SceneActorTags.Select(t => new { t.SceneId, t.ActorId, t.TagId }).ToListAsync();
            Assert.Equal([new { SceneId = sceneId, ActorId = targetId, TagId = blondeId }], sceneTags);
        }
    }

    [Fact]
    public async Task MergeAsync_PreservesFavorite_WhenSourceOrTargetIsFavorite()
    {
        using var factory = new TestDbContextFactory();
        int sourceId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true, FavoritedAt = DateTime.UtcNow };
            var target = new Actor { FirstName = "Yuua", LastName = "Mikami", IsFavorite = false };
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var service = new ActorService(factory);
        var result = await service.MergeAsync(sourceId, targetId);

        Assert.True(result.Success);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var targetInDb = await db.Actors.FindAsync(targetId);
            Assert.NotNull(targetInDb);
            Assert.True(targetInDb.IsFavorite);
            Assert.NotNull(targetInDb.FavoritedAt);
        }
    }

    [Fact]
    public async Task GetActorCardsAsync_ProjectsCreatedAtAndSearchMetadata()
    {
        using var factory = new TestDbContextFactory();
        var created = new DateTime(2025, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                CreatedAt = created,
                JapaneseNameKanji = "三上悠亜",
                JapaneseNameKana = "みかみゆあ"
            };
            actor.Aliases.Add(new ActorAlias { Name = "Momona Kito" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var cards = await service.GetActorCardsAsync();

        var card = cards.Single(c => c.Id == actorId);
        Assert.Equal(created, card.CreatedAt);
        Assert.Equal("三上悠亜", card.JapaneseNameKanji);
        Assert.Equal("みかみゆあ", card.JapaneseNameKana);
        Assert.NotNull(card.Aliases);
        Assert.Contains("Momona Kito", card.Aliases);
    }

    [Fact]
    public async Task GetActorCardsAsync_DoesNotTriggerMultipleCollectionIncludeWarning()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        try
        {
            var options = new DbContextOptionsBuilder<Javbuddy.Data.AppDbContext>()
                .UseSqlite(connection)
                .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
                .Options;

            using (var context = new Javbuddy.Data.AppDbContext(options))
            {
                context.Database.EnsureCreated();
                var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
                actor.Aliases.Add(new ActorAlias { Name = "Momona Kito" });
                var movie = new Movie { Code = "SSNI-001" };
                context.Movies.Add(movie);
                context.Actors.Add(actor);
                context.MovieActors.Add(new MovieActor { Actor = actor, Movie = movie });
                await context.SaveChangesAsync();
            }

            var factorySubstitute = Substitute.For<IDbContextFactory<Javbuddy.Data.AppDbContext>>();
            factorySubstitute.CreateDbContextAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(new Javbuddy.Data.AppDbContext(options)));

            var service = new ActorService(factorySubstitute);
            var cards = await service.GetActorCardsAsync();
            Assert.Single(cards);
            Assert.Contains("Momona Kito", cards[0].Aliases!);
            Assert.Equal(1, cards[0].MovieCount);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateAsync_TrimsNamesAndPersistsActor()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.CreateAsync("  Yua ", " Mikami  ");

        Assert.True(result.Success);
        Assert.Equal("Mikami Yua", result.Actor!.DisplayName);
        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.Actors.SingleAsync();
        Assert.Equal("Yua", stored.FirstName);
        Assert.Equal("Mikami", stored.LastName);
    }

    [Fact]
    public async Task CreateAsync_BlankLastName_StoresNull()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        await service.CreateAsync("Remu", "  ");

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.Actors.SingleAsync()).LastName);
    }

    [Fact]
    public async Task CreateAsync_ReturnsFail_WhenNameExistsCaseInsensitively()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);
        await service.CreateAsync("Yua", "Mikami");

        var result = await service.CreateAsync("YUA", "Mikami");

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Actors.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_ReturnsFail_WhenFirstNameBlank()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.CreateAsync("  ", "Mikami");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenAliasIsEmptyOrWhitespace()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var emptyResult = await service.AddAliasAsync(1, "");
        Assert.False(emptyResult.Success);
        Assert.Contains("empty", emptyResult.ErrorMessage);

        var wsResult = await service.AddAliasAsync(1, "   ");
        Assert.False(wsResult.Success);
        Assert.Contains("empty", wsResult.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenActorNotFound()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var result = await service.AddAliasAsync(999, "Some Alias");
        Assert.False(result.Success);
        Assert.Equal("Actor not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenActorAlreadyHasAlias()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Momona Kito" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var result = await service.AddAliasAsync(actorId, "momona kito");

        Assert.False(result.Success);
        Assert.Contains("already has alias", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenAliasMatchesActorsOwnNameOrVariations()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜",
                JapaneseNameKana = "みかみ ゆあ",
                R18DevName = "Yua Mikami"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);

        // DisplayName
        var r1 = await service.AddAliasAsync(actorId, "Yua Mikami");
        Assert.False(r1.Success);
        Assert.Contains("matches the actor's current name", r1.ErrorMessage);

        // Kanji (compact variation without matching spaces)
        var r2 = await service.AddAliasAsync(actorId, "三上 悠亜");
        Assert.False(r2.Success);
        Assert.Contains("matches the actor's current Japanese kanji", r2.ErrorMessage);

        // Kana (compact variation without matching spaces)
        var r3 = await service.AddAliasAsync(actorId, "みかみゆあ");
        Assert.False(r3.Success);
        Assert.Contains("matches the actor's current Japanese kana", r3.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenAliasCollidesWithAnotherActorsPrimaryName()
    {
        using var factory = new TestDbContextFactory();
        int a1Id;
        string expectedName;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var a1 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var a2 = new Actor { FirstName = "Eimi", LastName = "Fukada" };
            db.Actors.AddRange(a1, a2);
            await db.SaveChangesAsync();
            a1Id = a1.Id;
            expectedName = a2.DisplayName;
        }

        var service = new ActorService(factory);
        var result = await service.AddAliasAsync(a1Id, "Eimi Fukada");

        Assert.False(result.Success);
        Assert.Contains($"already the primary name of actor \"{expectedName}\"", result.ErrorMessage);
        Assert.Contains("Merge", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_ReturnsFail_WhenAliasCollidesWithAnotherActorsAlias()
    {
        using var factory = new TestDbContextFactory();
        int a1Id;
        string expectedName;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var a1 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var a2 = new Actor { FirstName = "Eimi", LastName = "Fukada" };
            a2.Aliases.Add(new ActorAlias { Name = "Alternate Name" });
            db.Actors.AddRange(a1, a2);
            await db.SaveChangesAsync();
            a1Id = a1.Id;
            expectedName = a2.DisplayName;
        }

        var service = new ActorService(factory);
        var result = await service.AddAliasAsync(a1Id, "Alternate Name");

        Assert.False(result.Success);
        Assert.Contains($"already assigned to actor \"{expectedName}\"", result.ErrorMessage);
        Assert.Contains("Merge", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAliasAsync_Succeeds_AndSynchronizesMovieAssociations()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "SSNI-001", MetaActresses = "Momona Kito" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            movieId = movie.Id;
        }

        var service = new ActorService(factory);
        var result = await service.AddAliasAsync(actorId, "Momona Kito");

        Assert.True(result.Success);
        Assert.NotNull(result.Actor);
        Assert.Contains(result.Actor.Aliases, a => a.Name == "Momona Kito");

        // Verify movie association was created because of the alias
        await using (var db = await factory.CreateDbContextAsync())
        {
            var link = await db.MovieActors.FirstOrDefaultAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId);
            Assert.NotNull(link);

            var alias = await db.ActorAliases.FirstOrDefaultAsync(a => a.ActorId == actorId && a.Name == "Momona Kito");
            Assert.NotNull(alias);
        }
    }

    [Fact]
    public async Task RemoveAliasAsync_ReturnsFail_WhenAliasNotFound()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var result = await service.RemoveAliasAsync(actorId, 999);

        Assert.False(result.Success);
        Assert.Equal("Alias not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task RemoveAliasAsync_Succeeds_AndSynchronizesMovieAssociations()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        int aliasId;
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var alias = new ActorAlias { Name = "Momona Kito" };
            actor.Aliases.Add(alias);
            var movie = new Movie { Code = "SSNI-001", MetaActresses = "Momona Kito" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            aliasId = alias.Id;
            movieId = movie.Id;

            // Link movie to actor via alias
            await MovieActorAssociation.SynchronizeAllAsync(db);
            await db.SaveChangesAsync();
        }

        // Verify initially linked
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True(await db.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId));
        }

        var service = new ActorService(factory);
        var result = await service.RemoveAliasAsync(actorId, aliasId);

        Assert.True(result.Success);

        // Verify alias was removed and movie unlinked because it only matched Momona Kito
        await using (var db = await factory.CreateDbContextAsync())
        {
            var alias = await db.ActorAliases.FirstOrDefaultAsync(a => a.Id == aliasId);
            Assert.Null(alias);

            var link = await db.MovieActors.FirstOrDefaultAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId);
            Assert.Null(link);
        }
    }

    [Fact]
    public async Task GetPortraitInfoAsync_NoImages_ReturnsAllFalse()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorService(factory);

        var info = await service.GetPortraitInfoAsync(999);
        Assert.False(info.HasImage);
        Assert.False(info.HasCroppedImage);
        Assert.False(info.HasSourceImage);
    }

    [Fact]
    public async Task GetPortraitInfoAsync_UncroppedImage_ReturnsHasImageTrueAndCroppedFalse()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorId,
                Variant = "full",
                SourceMovieCode = "custom",
                StorageId = Guid.NewGuid(),
                CropWidth = null
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var info = await service.GetPortraitInfoAsync(actorId);

        Assert.True(info.HasImage);
        Assert.False(info.HasCroppedImage);
        Assert.False(info.HasSourceImage);
    }

    [Fact]
    public async Task GetPortraitInfoAsync_CroppedImage_WithSourceFile_ReturnsAllTrue()
    {
        using var factory = new TestDbContextFactory();
        var sourceStorageId = Guid.NewGuid();
        var tempFile = Path.GetTempFileName();
        try
        {
            int actorId;
            await using (var db = await factory.CreateDbContextAsync())
            {
                var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
                db.Actors.Add(actor);
                await db.SaveChangesAsync();
                actorId = actor.Id;

                db.ActorImages.Add(new ActorImage
                {
                    ActorId = actorId,
                    Variant = "full",
                    SourceMovieCode = "custom",
                    StorageId = Guid.NewGuid(),
                    SourceStorageId = sourceStorageId,
                    SourceExtension = ".jpg",
                    CropWidth = 0.5
                });
                await db.SaveChangesAsync();
            }

            var dataStore = Substitute.For<Javbuddy.Services.Images.IActorImageDataStore>();
            dataStore.ExistsAsync(sourceStorageId, ".jpg", Arg.Any<CancellationToken>()).Returns(true);

            var service = new ActorService(factory, dataStore: dataStore);
            var info = await service.GetPortraitInfoAsync(actorId);

            Assert.True(info.HasImage);
            Assert.True(info.HasCroppedImage);
            Assert.True(info.HasSourceImage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetPortraitInfoAsync_ReturnsImageVersion_FromMostRecentlyUpdatedImage()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        var olderUpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newerUpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorId,
                Variant = "thumb",
                SourceMovieCode = "custom",
                StorageId = Guid.NewGuid(),
                UpdatedAt = olderUpdatedAt
            });
            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorId,
                Variant = "full",
                SourceMovieCode = "custom",
                StorageId = Guid.NewGuid(),
                UpdatedAt = newerUpdatedAt
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var info = await service.GetPortraitInfoAsync(actorId);

        Assert.Equal(newerUpdatedAt.Ticks, info.ImageVersion);
    }

    [Theory]
    [InlineData(null, "DD", false, null)]
    [InlineData(null, "Huge", false, null)]
    [InlineData("DD", "dd", true, "DD")]
    [InlineData("DD", "C", true, "C")]
    [InlineData("DD", null, true, null)]
    [InlineData("C", "", true, null)]
    public async Task UpdateAsync_CupSize_AllowsStandardOrUnchangedStoredValue(
        string? stored, string? submitted, bool success, string? expected)
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", CupSize = stored };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var result = await service.UpdateAsync(new ActorUpdateModel { Id = actorId, FirstName = "Yua", LastName = "Mikami", CupSize = submitted });

        Assert.Equal(success, result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.Actors.FindAsync(actorId);
            Assert.NotNull(saved);
            Assert.Equal(success ? expected : stored, saved.CupSize);
        }
        if (!success)
        {
            Assert.Equal("Cup size must be one of A-K.", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsPhysicalAttributes_NormalizedCupSize()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var model = new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yua",
            LastName = "Mikami",
            HeightCm = 157,
            CupSize = " c ",
            Bust = 89,
            Waist = 65,
            Hips = 94,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true
        };

        var result = await service.UpdateAsync(model);

        Assert.True(result.Success);
        Assert.NotNull(result.Actor);
        Assert.Equal(157, result.Actor.HeightCm);
        Assert.Equal("C", result.Actor.CupSize);
        Assert.Equal(89, result.Actor.Bust);
        Assert.Equal(65, result.Actor.Waist);
        Assert.Equal(94, result.Actor.Hips);
        Assert.Equal(new DateTime(1993, 8, 16), result.Actor.BirthDate);
        Assert.True(result.Actor.IsRetired);

        // Verify from fresh DB context
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.Actors.FindAsync(actorId);
            Assert.NotNull(saved);
            Assert.Equal(157, saved.HeightCm);
            Assert.Equal("C", saved.CupSize);
            Assert.Equal(89, saved.Bust);
            Assert.Equal(65, saved.Waist);
            Assert.Equal(94, saved.Hips);
            Assert.Equal(new DateTime(1993, 8, 16), saved.BirthDate);
            Assert.True(saved.IsRetired);
            Assert.Equal("157 cm", saved.FormattedHeight);
            Assert.Equal("89-65-94", saved.FormattedMeasurements);
        }
    }

    [Fact]
    public async Task MergeAsync_DoesNotCopyNonStandardCupSize()
    {
        using var factory = new TestDbContextFactory();
        int sourceId;
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Source", LastName = "Actor", HeightCm = 160, CupSize = "L" };
            var target = new Actor { FirstName = "Target", LastName = "Actor" };
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var service = new ActorService(factory);
        var result = await service.MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = await db.Actors.FindAsync(targetId);
            Assert.NotNull(target);
            Assert.Null(target.CupSize);
            Assert.Equal(160, target.HeightCm);
        }
    }

    [Fact]
    public async Task MergeAsync_ConsolidatesPhysicalAttributes_WhenTargetMissing()
    {
        using var factory = new TestDbContextFactory();
        int sourceId;
        int targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor
            {
                FirstName = "Source",
                LastName = "Actor",
                HeightCm = 160,
                CupSize = "d",
                Bust = 90,
                Waist = 60,
                Hips = 88,
                BirthDate = new DateTime(1995, 5, 20),
                IsRetired = true
            };
            var target = new Actor
            {
                FirstName = "Target",
                LastName = "Actor",
                HeightCm = null,
                CupSize = null,
                Bust = null,
                Waist = null,
                Hips = null,
                BirthDate = null,
                IsRetired = false
            };
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var service = new ActorService(factory);
        var result = await service.MergeAsync(sourceId, targetId);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var target = await db.Actors.FindAsync(targetId);
            Assert.NotNull(target);
            Assert.Equal(160, target.HeightCm);
            Assert.Equal("D", target.CupSize);
            Assert.Equal(90, target.Bust);
            Assert.Equal(60, target.Waist);
            Assert.Equal(88, target.Hips);
            Assert.Equal(new DateTime(1995, 5, 20), target.BirthDate);
            Assert.True(target.IsRetired);
        }
    }

    [Fact]
    public async Task GetActorCardsAsync_IncludesCupSize()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Yua", LastName = "Mikami", CupSize = "F", BirthDate = new DateTime(1993, 8, 16), IsRetired = true, HeightCm = 158 },
                new Actor { FirstName = "Remu", LastName = null, CupSize = null, BirthDate = null, IsRetired = false, HeightCm = null }
            );
            await db.SaveChangesAsync();
        }

        var service = new ActorService(factory);
        var cards = await service.GetActorCardsAsync();

        Assert.Equal(2, cards.Count);
        var yua = cards.FirstOrDefault(c => c.DisplayName == "Mikami Yua");
        Assert.NotNull(yua);
        Assert.Equal("F", yua.CupSize);
        Assert.Equal(new DateTime(1993, 8, 16), yua.BirthDate);
        Assert.True(yua.IsRetired);
        Assert.Equal(158, yua.HeightCm);

        var remu = cards.FirstOrDefault(c => c.DisplayName == "Remu");
        Assert.NotNull(remu);
        Assert.Null(remu.CupSize);
        Assert.Null(remu.BirthDate);
        Assert.False(remu.IsRetired);
        Assert.Null(remu.HeightCm);
    }

    [Fact]
    public async Task UpdateAsync_RejectsBirthDateMakingActorUnder18_AndKeepsExistingBirthDate()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", BirthDate = new DateTime(1993, 8, 16) };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var result = await service.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yua",
            LastName = "Mikami",
            BirthDate = DateTime.UtcNow.Date.AddYears(-17)
        });

        Assert.False(result.Success);
        Assert.Equal("Birthdate must make the actor at least 18 years old.", result.ErrorMessage);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var saved = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
        Assert.Equal(new DateTime(1993, 8, 16), saved.BirthDate);
    }

    [Fact]
    public async Task UpdateAsync_AllowsClearingBirthDate()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", BirthDate = new DateTime(1993, 8, 16) };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorService(factory);
        var result = await service.UpdateAsync(new ActorUpdateModel { Id = actorId, FirstName = "Yua", LastName = "Mikami", BirthDate = null });

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Null((await verifyDb.Actors.SingleAsync(a => a.Id == actorId)).BirthDate);
    }
}
