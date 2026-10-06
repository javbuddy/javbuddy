using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Statistics;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Statistics;

public class LibraryStatisticsServiceTests
{
    private static LibraryStatisticsService Service(TestDbContextFactory factory, ILocalLibraryClient? library = null)
    {
        library ??= Substitute.For<ILocalLibraryClient>();
        library.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "/jav", "/vr" });
        return new LibraryStatisticsService(factory, library, new LibraryStatisticsCache());
    }

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(480, "SD")]
    [InlineData(720, "720p")]
    [InlineData(1080, "1080p")]
    [InlineData(2160, "4K")]
    [InlineData(4320, "8K+")]
    public void ResolutionBucket_GroupsHeights(int? height, string expected) =>
        Assert.Equal(expected, LibraryStatisticsService.ResolutionBucket(height));

    [Fact]
    public async Task Get_CountsEntitiesAndMovieBreakdowns()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var got = new Movie { Code = "AAA-001", Status = MovieStatus.Got, LocalFileSizeBytes = 1000, MediaHeight = 1080, MediaVideoCodec = "AVC", MediaDurationSeconds = 3600, IsFavorite = true };
            var vr = new Movie { Code = "AAA-002", Status = MovieStatus.Got, LocalFileSizeBytes = 3000, MediaHeight = 2160, MediaVideoCodec = "HEVC", MediaDurationSeconds = 3600, VrType = "SBS" };
            var missing = new Movie { Code = "AAA-003", Status = MovieStatus.Missing };
            var actor = new Actor { FirstName = "Aika", LastName = "Yumeno", IsFavorite = true };
            var tag = new Tag { Name = "Drama", NeedsReview = true };
            db.AddRange(got, vr, missing, actor, tag);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = got.Id, ActorId = actor.Id });
            db.MovieTags.Add(new MovieTag { MovieId = got.Id, TagId = tag.Id });
            db.Scenes.Add(new Scene { MovieId = got.Id, StartSeconds = 0 });
            db.MovieHighlights.Add(new MovieHighlight { MovieId = got.Id, StartSeconds = 1, EndSeconds = 5 });
            db.MovieApexes.Add(new MovieApex { MovieId = got.Id, Seconds = 2 });
            await db.SaveChangesAsync();
        }

        var library = Substitute.For<ILocalLibraryClient>();
        library.ResolveRootForCodeAsync("AAA-001", Arg.Any<CancellationToken>()).Returns("/jav");
        library.ResolveRootForCodeAsync("AAA-002", Arg.Any<CancellationToken>()).Returns("/vr");

        var stats = await Service(factory, library).GetAsync();

        Assert.Equal(3, stats.Movies);
        Assert.Equal(2, stats.MoviesGot);
        Assert.Equal(1, stats.MoviesMissing);
        Assert.Equal(1, stats.MoviesVr);
        Assert.Equal(1, stats.MoviesFavorite);
        Assert.Equal(4000, stats.TotalBytes);
        Assert.Equal(2.0, stats.TotalRuntimeHours);
        Assert.Equal((1, 1, 1), (stats.Scenes, stats.Highlights, stats.Apexes));
        Assert.Equal((1, 1), (stats.Actors, stats.ActorsFavorite));
        Assert.Equal((1, 1), (stats.Tags, stats.TagsNeedingReview));
        Assert.Equal(new[] { "4K", "1080p" }, stats.ByResolution.Select(r => r.Label));
        Assert.Equal(new[] { "AVC", "HEVC" }, stats.ByCodec.Select(r => r.Label).Order());
        Assert.Equal("Drama", Assert.Single(stats.TopTags).Label);
        Assert.Equal("Yumeno Aika", Assert.Single(stats.TopActors).Label);
        Assert.Equal(1000, stats.ByRoot.Single(r => r.Label == "/jav").Bytes);
        Assert.Equal(3000, stats.ByRoot.Single(r => r.Label == "/vr").Bytes);
    }

    [Fact]
    public async Task Get_ListsConfiguredRootsWithNoMovies_AndFlagsMoviesNotOnDisk()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "AAA-004", Status = MovieStatus.Got, LocalFileSizeBytes = 10 });
            await db.SaveChangesAsync();
        }

        var library = Substitute.For<ILocalLibraryClient>();
        library.ResolveRootForCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var stats = await Service(factory, library).GetAsync();

        Assert.Equal(0, stats.ByRoot.Single(r => r.Label == "/jav").Count);
        Assert.Equal(1, stats.ByRoot.Single(r => r.Label == "(not found on disk)").Count);
    }

    [Fact]
    public async Task Get_CachesTheResultWithinTheTtl()
    {
        using var factory = new TestDbContextFactory();
        var service = Service(factory);

        var first = await service.GetAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "AAA-005" });
            await db.SaveChangesAsync();
        }

        Assert.Same(first, await service.GetAsync());
    }
}
