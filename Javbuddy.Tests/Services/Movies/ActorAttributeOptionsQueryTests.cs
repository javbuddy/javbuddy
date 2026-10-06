using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Movies;

public sealed class ActorAttributeOptionsQueryTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly TestDbContextFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<ActorAttributeOptions> LoadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await ActorAttributeOptionsQuery.LoadAsync(ActorAttributeOptionsQuery.ForMovies(db), Today, default);
    }

    [Fact]
    public async Task ReadsCupsAndRanges_FromLinkedActorsOnly()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "OPT-001", MetaReleaseDate = new DateTime(2021, 6, 15) };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "A", CupSize = " e ", HeightCm = 150, Bust = 80, Waist = 55, Hips = 82, BirthDate = new DateTime(2000, 6, 16) } }); // 20 on release
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "B", CupSize = "DD", HeightCm = 172, Bust = 96, Waist = 66, Hips = 95, BirthDate = new DateTime(1985, 1, 1) } }); // 36
            db.Movies.Add(movie);
            db.Actors.Add(new Actor { FirstName = "Unlinked", CupSize = "K", HeightCm = 199, Bust = 120, BirthDate = new DateTime(1960, 1, 1) });
            await db.SaveChangesAsync();
        }

        var options = await LoadAsync();

        Assert.Equal(["E", "DD"], options.CupSizes);
        Assert.Equal(new RangeBounds(150, 172), options.Height);
        Assert.Equal(new RangeBounds(80, 96), options.Bust);
        Assert.Equal(new RangeBounds(55, 66), options.Waist);
        Assert.Equal(new RangeBounds(82, 95), options.Hips);
        Assert.Equal(new RangeBounds(20, 36), options.Age);
    }

    [Fact]
    public async Task CupSizes_AreTheOnesInEffectOnEachReleaseDate()
    {
        // B regularly, F from 2022-01-01; only the 2023 movie is after it.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "P", CupSize = "B" };
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2022, 1, 1), CupSize = "F" });
            actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2030, 1, 1), CupSize = "K" }); // after every release
            var after = new Movie { Code = "OPT-003", MetaReleaseDate = new DateTime(2023, 1, 1) };
            after.MovieActors.Add(new MovieActor { Actor = actor });
            var undated = new Movie { Code = "OPT-004" };
            undated.MovieActors.Add(new MovieActor { Actor = actor });
            db.Movies.AddRange(after, undated);
            await db.SaveChangesAsync();
        }

        Assert.Equal(["B", "F"], (await LoadAsync()).CupSizes);
    }

    [Fact]
    public async Task UsesTodayWhenTheMovieHasNoReleaseDate_AndNullBoundsWhenNothingIsKnown()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "OPT-002" };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "C", BirthDate = new DateTime(2000, 1, 1) } }); // 26 today
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "D" } });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var options = await LoadAsync();

        Assert.Equal(new RangeBounds(26, 26), options.Age);
        Assert.Empty(options.CupSizes);
        Assert.Null(options.Height);
        Assert.Null(options.Bust);
        Assert.Null(options.Waist);
        Assert.Null(options.Hips);
    }

    [Fact]
    public async Task AgeBounds_AreTheTrueAges_NotWidenedBelowTheYoungestActor()
    {
        // An actress who is 18 on release used to come out as a 17 low end.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "OPT-004", MetaReleaseDate = new DateTime(2018, 6, 15) };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "E", BirthDate = new DateTime(2000, 1, 1) } }); // 18 on release
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "F", BirthDate = new DateTime(1990, 6, 15) } }); // 28 on release (birthday)
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        Assert.Equal(new RangeBounds(18, 28), (await LoadAsync()).Age);
    }

    [Fact]
    public async Task EmptyLibrary_OffersNothing()
    {
        var options = await LoadAsync();

        Assert.Empty(options.CupSizes);
        Assert.Null(options.Age);
        Assert.Null(options.Height);
    }

    [Fact]
    public async Task AgeBounds_NeverComeOutInverted_EvenForABirthYearAfterTheReleaseYear()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "OPT-003", MetaReleaseDate = new DateTime(2021, 6, 15) };
            movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Bad", BirthDate = new DateTime(2022, 1, 1) } }); // bad data: born after release
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var age = (await LoadAsync()).Age;

        Assert.NotNull(age);
        Assert.True(age!.Min <= age.Max);
    }
}
